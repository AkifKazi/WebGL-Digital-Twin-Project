using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
public class PortraitStatRailManager : MonoBehaviour
{
    public event Action LayoutRebuilt;
    public event Action<int> HiddenPresentationCountChanged;

    public IReadOnlyDictionary<PerformanceStatSource, PerformanceStatCardView> ActiveCards => activeCards;
    public int HiddenPresentationCount { get; private set; }
    public bool HasSecondaryRailCards =>
        innerTopBindings.Count > 0 || innerBottomBindings.Count > 0;

    [Header("References")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Canvas canvas;
    [SerializeField] private PerformanceStatCardView cardPrefab;
    [SerializeField] private RectTransform topCardContainer;
    [SerializeField] private RectTransform bottomCardContainer;
    [SerializeField] private RectTransform innerTopCardContainer;
    [SerializeField] private RectTransform innerBottomCardContainer;

    [Header("Sources")]
    [SerializeField] private TelemetryRegistry telemetryRegistry;
    [SerializeField] private TelemetrySourceSelectionMode sourceSelectionMode =
        TelemetrySourceSelectionMode.SceneDiscovery;
    [SerializeField] private PerformanceStatSource[] sources;

    public TelemetrySourceSelectionMode SourceSelectionMode => sourceSelectionMode;

    [Header("Card Placement")]
    [SerializeField, Min(1f)] private float cardWidth = 280f;
    [SerializeField, Min(1f)] private float cardHeight = 116f;
    [SerializeField, Min(0f)] private float minimumGap = 20f;
    [SerializeField, Min(0f)] private float leftPadding = 20f;
    [SerializeField, Min(0f)] private float rightPadding = 20f;
    [SerializeField, Range(0.05f, 0.5f)] private float smoothTime = 0.18f;
    [SerializeField, Min(1f)] private float maximumSpeed = 1500f;

    [Header("Capacity")]
    [SerializeField, Min(1)] private int maximumCardsPerRail = 3;

    [Header("Behaviour")]
    [SerializeField] private bool preserveCardOrder = true;
    [SerializeField] private bool showDebugLogs;

    private readonly Dictionary<PerformanceStatSource, PerformanceStatCardView> activeCards = new();
    private readonly List<PerformanceStatCardView> generatedCards = new();
    private readonly Stack<PerformanceStatCardView> cardPool = new();
    private readonly List<RailCardBinding> topBindings = new();
    private readonly List<RailCardBinding> bottomBindings = new();
    private readonly List<RailCardBinding> innerTopBindings = new();
    private readonly List<RailCardBinding> innerBottomBindings = new();
    private readonly List<SourcePosition> topSources = new();
    private readonly List<SourcePosition> bottomSources = new();
    private readonly List<SourcePosition> innerTopSources = new();
    private readonly List<SourcePosition> innerBottomSources = new();
    private TelemetryEquipmentGroup[] equipmentGroups = Array.Empty<TelemetryEquipmentGroup>();

    private IEnumerator Start()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        yield return null;
        RebuildLayout();
    }

    private void LateUpdate()
    {
        UpdateRail(topBindings, topCardContainer);
        UpdateRail(bottomBindings, bottomCardContainer);
        UpdateRail(innerTopBindings, innerTopCardContainer);
        UpdateRail(innerBottomBindings, innerBottomCardContainer);
    }

    private void OnDestroy()
    {
        UnsubscribeFromSources();
    }

    [ContextMenu("Rebuild Layout")]
    public void RebuildLayout()
    {
        ClearGeneratedCards();
        UnsubscribeFromSources();

        ResolveSources();

        if (worldCamera == null || canvas == null || cardPrefab == null ||
            topCardContainer == null || bottomCardContainer == null)
        {
            Debug.LogWarning("PortraitStatRailManager is missing one or more references.", this);
            return;
        }

        SetSecondaryRailActive(innerTopCardContainer, true);
        SetSecondaryRailActive(innerBottomCardContainer, true);
        ConfigureDeterministicRailGeometry();
        Canvas.ForceUpdateCanvases();
        if (topCardContainer.parent?.parent is RectTransform contentArea)
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(contentArea);
        Canvas.ForceUpdateCanvases();

        topSources.Clear();
        bottomSources.Clear();
        innerTopSources.Clear();
        innerBottomSources.Clear();
        SetHiddenPresentationCount(0);

        if (sources == null)
            sources = Array.Empty<PerformanceStatSource>();

        List<SourcePosition> candidates = new();
        foreach (TelemetryCardPresentation presentation in TelemetryPresentationBuilder.Build(sources))
        {
            if (presentation == null || presentation.WorldAnchor == null)
                continue;

            Vector3 viewport = worldCamera.WorldToViewportPoint(presentation.WorldAnchor.position);

            if (viewport.z <= 0f)
                continue;

            candidates.Add(new SourcePosition(presentation, viewport.x, viewport.y));
        }

        AllocateAcrossRails(candidates);

        Debug.Log(
            $"TELEMETRY_PORTRAIT_LAYOUT candidates={candidates.Count} " +
            $"outerTop={topSources.Count} outerBottom={bottomSources.Count} " +
            $"innerTop={innerTopSources.Count} innerBottom={innerBottomSources.Count} " +
            $"widths={GetAvailableRailWidth(topCardContainer):F1}/" +
            $"{GetAvailableRailWidth(bottomCardContainer):F1}",
            this);

        BuildRail(topSources, topCardContainer, StatRailSide.Top, topBindings);
        BuildRail(bottomSources, bottomCardContainer, StatRailSide.Bottom, bottomBindings);
        BuildRail(innerTopSources, innerTopCardContainer, StatRailSide.Top, innerTopBindings);
        BuildRail(innerBottomSources, innerBottomCardContainer, StatRailSide.Bottom, innerBottomBindings);
        SetSecondaryRailActive(innerTopCardContainer, innerTopBindings.Count > 0);
        SetSecondaryRailActive(innerBottomCardContainer, innerBottomBindings.Count > 0);
        SubscribeToSources();
        InitialiseRailPositions(topBindings, topCardContainer);
        InitialiseRailPositions(bottomBindings, bottomCardContainer);
        InitialiseRailPositions(innerTopBindings, innerTopCardContainer);
        InitialiseRailPositions(innerBottomBindings, innerBottomCardContainer);
        LayoutRebuilt?.Invoke();
    }

    private void ResolveSources()
    {
        if (sourceSelectionMode == TelemetrySourceSelectionMode.ExplicitList)
        {
            sources = TelemetrySourceResolver.Resolve(sourceSelectionMode, sources);
            return;
        }

        if (telemetryRegistry == null)
        {
            telemetryRegistry = UnityEngine.Object.FindFirstObjectByType<TelemetryRegistry>(
                FindObjectsInactive.Include);
        }

        if (telemetryRegistry != null)
        {
            telemetryRegistry.RebuildIndex();
            sources = telemetryRegistry.Sources.ToArray();
            return;
        }

        sources = TelemetrySourceResolver.Resolve(sourceSelectionMode, sources);
    }

    private void ConfigureDeterministicRailGeometry()
    {
        if (topCardContainer?.parent?.parent is not RectTransform content)
            return;
        VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
            layout.enabled = false;

        ConfigureHorizontalRail(topCardContainer.parent as RectTransform, 0f, 150f, true);
        ConfigureHorizontalRail(innerTopCardContainer?.parent as RectTransform, 150f, 150f, true);
        ConfigureHorizontalRail(bottomCardContainer.parent as RectTransform, 0f, 150f, false);
        ConfigureHorizontalRail(innerBottomCardContainer?.parent as RectTransform, 150f, 150f, false);

        RectTransform stage = content.Cast<Transform>()
            .FirstOrDefault(child => child.name == "Stage area") as RectTransform;
        if (stage != null)
        {
            stage.anchorMin = Vector2.zero;
            stage.anchorMax = Vector2.one;
            stage.offsetMin = new Vector2(0f, 150f);
            stage.offsetMax = new Vector2(0f, -150f);
        }
    }

    private static void ConfigureHorizontalRail(
        RectTransform rail,
        float edgeInset,
        float height,
        bool top)
    {
        if (rail == null)
            return;
        float y = top ? 1f : 0f;
        rail.anchorMin = new Vector2(0f, y);
        rail.anchorMax = new Vector2(1f, y);
        rail.pivot = new Vector2(0.5f, top ? 1f : 0f);
        rail.sizeDelta = new Vector2(0f, height);
        rail.anchoredPosition = new Vector2(0f, top ? -edgeInset : edgeInset);
    }

    private void AllocateAcrossRails(List<SourcePosition> candidates)
    {
        candidates.Sort((a, b) => b.Presentation.DisplayRank.CompareTo(a.Presentation.DisplayRank));
        foreach (SourcePosition candidate in candidates)
        {
            StatRailSide preferred = DetermineSide(
                candidate.Presentation,
                new Vector3(candidate.ViewportX, candidate.ViewportY, 1f));
            List<SourcePosition> preferredOuter = preferred == StatRailSide.Top ? topSources : bottomSources;
            List<SourcePosition> otherOuter = preferred == StatRailSide.Top ? bottomSources : topSources;
            RectTransform preferredOuterContainer = preferred == StatRailSide.Top ? topCardContainer : bottomCardContainer;
            RectTransform otherOuterContainer = preferred == StatRailSide.Top ? bottomCardContainer : topCardContainer;
            List<SourcePosition> preferredInner = preferred == StatRailSide.Top ? innerTopSources : innerBottomSources;
            List<SourcePosition> otherInner = preferred == StatRailSide.Top ? innerBottomSources : innerTopSources;
            RectTransform preferredInnerContainer = preferred == StatRailSide.Top ? innerTopCardContainer : innerBottomCardContainer;
            RectTransform otherInnerContainer = preferred == StatRailSide.Top ? innerBottomCardContainer : innerTopCardContainer;

            if (TryAllocate(candidate, preferredOuter, preferredOuterContainer) ||
                TryAllocate(candidate, otherOuter, otherOuterContainer) ||
                TryAllocate(candidate, preferredInner, preferredInnerContainer) ||
                TryAllocate(candidate, otherInner, otherInnerContainer))
                continue;

            if (showDebugLogs)
                Debug.Log($"Telemetry presentation '{candidate.Presentation.Key}' hidden: all four rails are full.", this);
            SetHiddenPresentationCount(HiddenPresentationCount + 1);
        }
    }

    private void SetHiddenPresentationCount(int value)
    {
        value = Mathf.Max(0, value);
        if (HiddenPresentationCount == value)
            return;
        HiddenPresentationCount = value;
        HiddenPresentationCountChanged?.Invoke(value);
    }

    private bool TryAllocate(
        SourcePosition candidate,
        List<SourcePosition> target,
        RectTransform container)
    {
        if (container == null || target.Count >= maximumCardsPerRail)
            return false;
        float used = target.Sum(item => GetCardWidth(item.Presentation)) +
                     Mathf.Max(0, target.Count - 1) * minimumGap;
        float required = GetCardWidth(candidate.Presentation) + (target.Count > 0 ? minimumGap : 0f);
        float available = GetAvailableRailWidth(container);
        if (used + required > available)
            return false;
        target.Add(candidate);
        return true;
    }

    private static void SetSecondaryRailActive(RectTransform container, bool active)
    {
        if (container != null && container.parent != null)
            container.parent.gameObject.SetActive(active);
    }

    public bool TryGetCard(PerformanceStatSource source, out PerformanceStatCardView card)
    {
        return activeCards.TryGetValue(source, out card);
    }

    private static StatRailSide DetermineSide(TelemetryCardPresentation presentation, Vector3 viewport)
    {
        return presentation.PreferredPortraitRail switch
        {
            PreferredPortraitStatRail.Top => StatRailSide.Top,
            PreferredPortraitStatRail.Bottom => StatRailSide.Bottom,
            _ => viewport.y >= 0.5f ? StatRailSide.Top : StatRailSide.Bottom
        };
    }

    private void BuildRail(
        List<SourcePosition> railSources,
        RectTransform container,
        StatRailSide side,
        List<RailCardBinding> bindings)
    {
        bindings.Clear();
        // Alarms always win limited rail space. Manual priority breaks ties.
        railSources.Sort((a, b) =>
        {
            int rankComparison = b.Presentation.DisplayRank.CompareTo(a.Presentation.DisplayRank);
            return rankComparison != 0
                ? rankComparison
                : string.CompareOrdinal(a.Presentation.Key, b.Presentation.Key);
        });

        float availableWidth = GetAvailableRailWidth(container);
        float usedWidth = 0f;
        int allowedCount = 0;
        for (int i = 0; i < railSources.Count && allowedCount < maximumCardsPerRail; i++)
        {
            float width = GetCardWidth(railSources[i].Presentation);
            float required = width + (allowedCount > 0 ? minimumGap : 0f);
            if (usedWidth + required > availableWidth)
                continue;
            usedWidth += required;
            railSources[allowedCount] = railSources[i];
            allowedCount++;
        }
        if (railSources.Count > allowedCount)
            railSources.RemoveRange(allowedCount, railSources.Count - allowedCount);

        railSources.Sort((a, b) => a.ViewportX.CompareTo(b.ViewportX));

        foreach (SourcePosition positionedSource in railSources)
        {
            TelemetryCardPresentation presentation = positionedSource.Presentation;
            PerformanceStatCardView card = AcquireCard(container);
            RectTransform cardRect = card.transform as RectTransform;

            float resolvedWidth = GetCardWidth(presentation);
            ConfigureGeneratedCard(cardRect, resolvedWidth);
            card.SetPresentation(presentation, side);

            RailCardBinding binding = new(presentation, card, cardRect, resolvedWidth);
            bindings.Add(binding);
            foreach (PerformanceStatSource source in presentation.Sources)
                activeCards[source] = card;
            generatedCards.Add(card);
        }
    }

    private float GetCardWidth(TelemetryCardPresentation presentation) =>
        cardWidth * Mathf.Min(presentation.MaximumVisibleMetrics, presentation.Sources.Count);

    private float GetAvailableRailWidth(RectTransform container)
    {
        if (container == null)
            return 0f;
        float width = container.rect.width;
        if (width <= 1f && container.parent is RectTransform rail)
            width = rail.rect.width;
        if (width <= 1f && container.parent?.parent is RectTransform content)
            width = content.rect.width;
        if (width <= 1f)
            width = canvas != null && canvas.scaleFactor > 0f
                ? Screen.safeArea.width / canvas.scaleFactor
                : Screen.safeArea.width;
        return Mathf.Max(0f, width - leftPadding - rightPadding);
    }

    private int CalculatePhysicalCapacity(RectTransform container)
    {
        float availableWidth = container.rect.width - leftPadding - rightPadding;

        if (availableWidth <= 0f)
            return 1;

        return Mathf.Max(1, Mathf.FloorToInt(
            (availableWidth + minimumGap) / (cardWidth + minimumGap)
        ));
    }

    private void ConfigureGeneratedCard(RectTransform cardRect, float resolvedWidth)
    {
        if (cardRect == null)
            return;

        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(resolvedWidth, cardHeight);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.localScale = Vector3.one;
        cardRect.localRotation = Quaternion.identity;
    }

    private void InitialiseRailPositions(List<RailCardBinding> bindings, RectTransform container)
    {
        if (bindings.Count == 0 || container == null)
            return;

        CalculateResolvedTargets(bindings, container);

        foreach (RailCardBinding binding in bindings)
        {
            Vector2 position = binding.RectTransform.anchoredPosition;
            position.x = binding.TargetX;
            binding.RectTransform.anchoredPosition = position;
            binding.CurrentVelocity = 0f;
        }
    }

    private void UpdateRail(List<RailCardBinding> bindings, RectTransform container)
    {
        if (bindings.Count == 0 || container == null || worldCamera == null)
            return;

        CalculateResolvedTargets(bindings, container);

        foreach (RailCardBinding binding in bindings)
        {
            if (binding.RectTransform == null)
                continue;

            Vector2 position = binding.RectTransform.anchoredPosition;
            position.x = Mathf.SmoothDamp(
                position.x,
                binding.TargetX,
                ref binding.CurrentVelocity,
                smoothTime,
                maximumSpeed,
                Time.unscaledDeltaTime
            );
            binding.RectTransform.anchoredPosition = position;
        }

        ResolveCurrentPositionCollisions(bindings, container);
    }

    private void CalculateResolvedTargets(List<RailCardBinding> bindings, RectTransform container)
    {
        if (!preserveCardOrder)
        {
            bindings.Sort((a, b) =>
                GetDesiredLocalX(a.Presentation, container).CompareTo(GetDesiredLocalX(b.Presentation, container))
            );
        }

        float leftLimit = container.rect.xMin + leftPadding + bindings[0].Width * 0.5f;
        float rightLimit = container.rect.xMax - rightPadding - bindings[^1].Width * 0.5f;

        for (int i = 0; i < bindings.Count; i++)
        {
            bindings[i].TargetX = Mathf.Clamp(
                GetDesiredLocalX(bindings[i].Presentation, container),
                leftLimit,
                rightLimit
            );
        }

        for (int i = 1; i < bindings.Count; i++)
        {
            float lowestAllowedX = bindings[i - 1].TargetX +
                (bindings[i - 1].Width + bindings[i].Width) * 0.5f + minimumGap;
            bindings[i].TargetX = Mathf.Max(bindings[i].TargetX, lowestAllowedX);
        }

        if (bindings[^1].TargetX > rightLimit)
        {
            bindings[^1].TargetX = rightLimit;

            for (int i = bindings.Count - 2; i >= 0; i--)
            {
                float highestAllowedX = bindings[i + 1].TargetX -
                    (bindings[i + 1].Width + bindings[i].Width) * 0.5f - minimumGap;
                bindings[i].TargetX = Mathf.Min(bindings[i].TargetX, highestAllowedX);
            }
        }

        if (bindings[0].TargetX < leftLimit)
        {
            bindings[0].TargetX = leftLimit;

            for (int i = 1; i < bindings.Count; i++)
                bindings[i].TargetX = bindings[i - 1].TargetX +
                    (bindings[i - 1].Width + bindings[i].Width) * 0.5f + minimumGap;
        }
    }

    private void ResolveCurrentPositionCollisions(List<RailCardBinding> bindings, RectTransform container)
    {
        if (bindings.Count == 0)
            return;

        float leftLimit = container.rect.xMin + leftPadding + bindings[0].Width * 0.5f;
        float rightLimit = container.rect.xMax - rightPadding - bindings[^1].Width * 0.5f;

        SetCardX(bindings[0], Mathf.Max(GetCardX(bindings[0]), leftLimit));

        for (int i = 1; i < bindings.Count; i++)
        {
            float lowestAllowedX = GetCardX(bindings[i - 1]) +
                (bindings[i - 1].Width + bindings[i].Width) * 0.5f + minimumGap;

            if (GetCardX(bindings[i]) < lowestAllowedX)
            {
                SetCardX(bindings[i], lowestAllowedX);
                bindings[i].CurrentVelocity = 0f;
            }
        }

        if (GetCardX(bindings[^1]) > rightLimit)
        {
            SetCardX(bindings[^1], rightLimit);
            bindings[^1].CurrentVelocity = 0f;

            for (int i = bindings.Count - 2; i >= 0; i--)
            {
                float highestAllowedX = GetCardX(bindings[i + 1]) -
                    (bindings[i + 1].Width + bindings[i].Width) * 0.5f - minimumGap;

                if (GetCardX(bindings[i]) > highestAllowedX)
                {
                    SetCardX(bindings[i], highestAllowedX);
                    bindings[i].CurrentVelocity = 0f;
                }
            }
        }
    }

    private float GetDesiredLocalX(TelemetryCardPresentation presentation, RectTransform container)
    {
        Vector2 screenPoint = worldCamera.WorldToScreenPoint(presentation.WorldAnchor.position);
        Camera uiCamera = GetUICamera();

        bool converted = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            container,
            screenPoint,
            uiCamera,
            out Vector2 localPoint
        );

        return converted ? localPoint.x : 0f;
    }

    private Camera GetUICamera()
    {
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return canvas.worldCamera != null ? canvas.worldCamera : worldCamera;
    }

    private static float GetCardX(RailCardBinding binding)
    {
        return binding.RectTransform != null ? binding.RectTransform.anchoredPosition.x : 0f;
    }

    private static void SetCardX(RailCardBinding binding, float x)
    {
        if (binding.RectTransform == null)
            return;

        Vector2 position = binding.RectTransform.anchoredPosition;
        position.x = x;
        binding.RectTransform.anchoredPosition = position;
    }

    private void HandleSourceChanged(PerformanceStatSource source)
    {
        if (!source.IsVisible || !activeCards.TryGetValue(source, out PerformanceStatCardView card))
            return;

        card.RefreshBoundSources();
    }

    private void HandleLayoutPriorityChanged(PerformanceStatSource source)
    {
        if (equipmentGroups.Any(group => group != null && group.Contains(source)))
            return;
        RebuildLayout();
    }

    private void SubscribeToSources()
    {
        foreach (PerformanceStatSource source in sources)
        {
            if (source != null)
            {
                source.Changed += HandleSourceChanged;
                source.LayoutPriorityChanged += HandleLayoutPriorityChanged;
            }
        }

        equipmentGroups = UnityEngine.Object.FindObjectsByType<TelemetryEquipmentGroup>(
            FindObjectsInactive.Exclude);
        foreach (TelemetryEquipmentGroup group in equipmentGroups)
        {
            group.PresentationChanged -= RebuildLayout;
            group.PresentationChanged += RebuildLayout;
        }
    }

    private void UnsubscribeFromSources()
    {
        if (sources == null)
            return;

        foreach (PerformanceStatSource source in sources)
        {
            if (source != null)
            {
                source.Changed -= HandleSourceChanged;
                source.LayoutPriorityChanged -= HandleLayoutPriorityChanged;
            }
        }

        foreach (TelemetryEquipmentGroup group in equipmentGroups)
        {
            if (group != null)
                group.PresentationChanged -= RebuildLayout;
        }
        equipmentGroups = Array.Empty<TelemetryEquipmentGroup>();
    }

    private void ClearGeneratedCards()
    {
        activeCards.Clear();
        topBindings.Clear();
        bottomBindings.Clear();
        innerTopBindings.Clear();
        innerBottomBindings.Clear();

        foreach (PerformanceStatCardView card in generatedCards)
        {
            if (card != null)
            {
                card.gameObject.SetActive(false);
                cardPool.Push(card);
            }
        }

        generatedCards.Clear();
    }

    private PerformanceStatCardView AcquireCard(RectTransform container)
    {
        PerformanceStatCardView card = null;

        while (card == null && cardPool.Count > 0)
            card = cardPool.Pop();

        if (card == null)
            card = Instantiate(cardPrefab, container);
        else
        {
            card.transform.SetParent(container, false);
            card.gameObject.SetActive(true);
        }

        return card;
    }

    private sealed class RailCardBinding
    {
        public readonly TelemetryCardPresentation Presentation;
        public readonly PerformanceStatCardView Card;
        public readonly RectTransform RectTransform;
        public readonly float Width;
        public float TargetX;
        public float CurrentVelocity;

        public RailCardBinding(
            TelemetryCardPresentation presentation,
            PerformanceStatCardView card,
            RectTransform rectTransform,
            float width)
        {
            Presentation = presentation;
            Card = card;
            RectTransform = rectTransform;
            Width = width;
        }
    }

    private readonly struct SourcePosition
    {
        public readonly TelemetryCardPresentation Presentation;
        public readonly float ViewportX;
        public readonly float ViewportY;

        public SourcePosition(
            TelemetryCardPresentation presentation,
            float viewportX,
            float viewportY)
        {
            Presentation = presentation;
            ViewportX = viewportX;
            ViewportY = viewportY;
        }
    }
}
