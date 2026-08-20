using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-100)]
public class WideStatRailManager : MonoBehaviour
{
    public event Action LayoutRebuilt;
    public event Action<int> HiddenPresentationCountChanged;

    public IReadOnlyDictionary<
        PerformanceStatSource,
        PerformanceStatCardView
    > ActiveCards => activeCards;
    public int HiddenPresentationCount { get; private set; }
    public bool HasSecondaryRailCards =>
        innerLeftBindings.Count > 0 || innerRightBindings.Count > 0;

    [Header("References")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Canvas canvas;
    [SerializeField] private PerformanceStatCardView cardPrefab;

    [Header("Continuous Rail Areas")]
    [SerializeField] private RectTransform leftCardContainer;
    [SerializeField] private RectTransform rightCardContainer;
    [SerializeField] private RectTransform innerLeftCardContainer;
    [SerializeField] private RectTransform innerRightCardContainer;

    [Header("Sources")]
    [SerializeField] private PerformanceStatSource[] sources;

    [Header("Card Placement")]
    [SerializeField, Min(1f)]
    private float cardHeight = 116f;

    [SerializeField, Min(0f)]
    private float minimumGap = 12f;

    [SerializeField, Min(0f)]
    private float topPadding = 0f;

    [SerializeField, Min(0f)]
    private float bottomPadding = 0f;

    [SerializeField, Range(0.05f, 0.5f)]
    private float smoothTime = 0.18f;

    [SerializeField, Min(1f)]
    private float maximumSpeed = 1500f;

    [Header("Capacity")]
    [SerializeField, Min(1)]
    private int maximumCardsPerRail = 6;

    [Header("Responsive Rail Geometry")]
    [SerializeField, Min(120f)] private float minimumRailWidth = 180f;
    [SerializeField, Min(120f)] private float maximumRailWidth = 280f;
    [SerializeField, Range(0.1f, 0.3f)] private float railWidthFraction = 0.18f;

    [Header("Stable content fitting")]
    [SerializeField, Range(1f, 1.2f)] private float maximumRailWidthExpansion = 1.2f;
    [SerializeField, Min(1f)] private float railWidthDecreaseDelay = 30f;
    [SerializeField, Min(0.05f)] private float railWidthSmoothTime = 0.35f;

    [Header("Behaviour")]
    [Tooltip(
        "Cards keep their top-to-bottom order so they never pass through each other."
    )]
    [SerializeField]
    private bool preserveCardOrder = true;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;

    private readonly Dictionary<
        PerformanceStatSource,
        PerformanceStatCardView
    > activeCards = new();

    private readonly List<PerformanceStatCardView>
        generatedCards = new();

    private readonly Stack<PerformanceStatCardView>
        cardPool = new();

    private readonly List<RailCardBinding>
        leftBindings = new();

    private readonly List<RailCardBinding>
        rightBindings = new();

    private readonly List<RailCardBinding> innerLeftBindings = new();
    private readonly List<RailCardBinding> innerRightBindings = new();

    private readonly List<PresentationPosition>
        leftSources = new();

    private readonly List<PresentationPosition>
        rightSources = new();

    private readonly List<PresentationPosition> innerLeftSources = new();
    private readonly List<PresentationPosition> innerRightSources = new();

    private TelemetryEquipmentGroup[] equipmentGroups = Array.Empty<TelemetryEquipmentGroup>();
    private readonly RailWidthState leftWidthState = new();
    private readonly RailWidthState rightWidthState = new();
    private readonly RailWidthState innerLeftWidthState = new();
    private readonly RailWidthState innerRightWidthState = new();

    private IEnumerator Start()
    {
        // The responsive shell and parent layout groups must resolve before
        // physical rail capacity is measured.
        yield return null;
        yield return new WaitForEndOfFrame();
        yield return null;
        RebuildLayout();
    }

    private void LateUpdate()
    {
        UpdateRail(
            leftBindings,
            leftCardContainer
        );

        UpdateRail(
            rightBindings,
            rightCardContainer
        );
        UpdateRail(innerLeftBindings, innerLeftCardContainer);
        UpdateRail(innerRightBindings, innerRightCardContainer);
        UpdateRailWidth(leftBindings, leftCardContainer, leftWidthState);
        UpdateRailWidth(rightBindings, rightCardContainer, rightWidthState);
        UpdateRailWidth(innerLeftBindings, innerLeftCardContainer, innerLeftWidthState);
        UpdateRailWidth(innerRightBindings, innerRightCardContainer, innerRightWidthState);
    }

    private void UpdateRailWidth(
        List<RailCardBinding> bindings,
        RectTransform container,
        RailWidthState state)
    {
        if (container == null || container.parent == null)
            return;
        LayoutElement layout = container.parent.GetComponent<LayoutElement>();
        if (layout == null || layout.preferredWidth <= 0f)
            return;

        state.Initialise(layout.preferredWidth);
        float requested = bindings.Count == 0
            ? 1f
            : Mathf.Clamp(
                bindings.Max(binding => binding.Card.RequestedCardExpansion),
                1f,
                maximumRailWidthExpansion);

        if (requested > state.TargetScale + 0.001f)
        {
            state.TargetScale = requested;
            state.LastExpansionRequestTime = Time.unscaledTime;
        }
        else if (requested < state.TargetScale - 0.001f &&
                 Time.unscaledTime - state.LastExpansionRequestTime >= railWidthDecreaseDelay)
        {
            state.TargetScale = requested;
        }

        state.CurrentScale = Mathf.SmoothDamp(
            state.CurrentScale,
            state.TargetScale,
            ref state.Velocity,
            railWidthSmoothTime,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
        layout.preferredWidth = state.BaseWidth * state.CurrentScale;
    }

    private void OnDestroy()
    {
        UnsubscribeFromSources();
    }

    public void RebuildLayout()
    {
        ClearGeneratedCards();
        UnsubscribeFromSources();

        if (worldCamera == null ||
            canvas == null ||
            cardPrefab == null ||
            leftCardContainer == null ||
            rightCardContainer == null)
        {
            Debug.LogWarning(
                "WideStatRailManager is missing one or more references.",
                this
            );

            return;
        }

        SetSecondaryRailActive(innerLeftCardContainer, true);
        SetSecondaryRailActive(innerRightCardContainer, true);
        ConfigureDeterministicRailGeometry();
        Canvas.ForceUpdateCanvases();
        if (leftCardContainer.parent?.parent is RectTransform contentArea)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentArea);
        Canvas.ForceUpdateCanvases();

        leftSources.Clear();
        rightSources.Clear();
        innerLeftSources.Clear();
        innerRightSources.Clear();
        SetHiddenPresentationCount(0);

        if (sources == null)
            sources = Array.Empty<PerformanceStatSource>();

        List<PresentationPosition> candidates = new();
        foreach (TelemetryCardPresentation presentation in TelemetryPresentationBuilder.Build(sources))
        {
            if (presentation == null || presentation.WorldAnchor == null)
            {
                continue;
            }

            Vector3 viewportPosition =
                worldCamera.WorldToViewportPoint(
                    presentation.WorldAnchor.position
                );

            if (viewportPosition.z <= 0f)
                continue;

            candidates.Add(new PresentationPosition(
                presentation,
                viewportPosition.x,
                viewportPosition.y));
        }

        AllocateAcrossRails(candidates);

        Debug.Log(
            $"TELEMETRY_WIDE_LAYOUT candidates={candidates.Count} " +
            $"outerLeft={leftSources.Count} outerRight={rightSources.Count} " +
            $"innerLeft={innerLeftSources.Count} innerRight={innerRightSources.Count} " +
            $"heights={GetAvailableRailHeight(leftCardContainer):F1}/" +
            $"{GetAvailableRailHeight(rightCardContainer):F1}",
            this);

        BuildRail(
            leftSources,
            leftCardContainer,
            StatRailSide.Left,
            leftBindings
        );

        BuildRail(
            rightSources,
            rightCardContainer,
            StatRailSide.Right,
            rightBindings
        );

        BuildRail(innerLeftSources, innerLeftCardContainer, StatRailSide.Left, innerLeftBindings);
        BuildRail(innerRightSources, innerRightCardContainer, StatRailSide.Right, innerRightBindings);

        SetSecondaryRailActive(innerLeftCardContainer, innerLeftBindings.Count > 0);
        SetSecondaryRailActive(innerRightCardContainer, innerRightBindings.Count > 0);

        SubscribeToSources();

        // Calculate valid starting positions immediately.
        InitialiseRailPositions(
            leftBindings,
            leftCardContainer
        );

        InitialiseRailPositions(
            rightBindings,
            rightCardContainer
        );
        InitialiseRailPositions(innerLeftBindings, innerLeftCardContainer);
        InitialiseRailPositions(innerRightBindings, innerRightCardContainer);

        LayoutRebuilt?.Invoke();
    }

    private void ConfigureDeterministicRailGeometry()
    {
        if (leftCardContainer?.parent?.parent is not RectTransform content)
            return;
        HorizontalLayoutGroup layout = content.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
            layout.enabled = false;

        float resolvedRailWidth = Mathf.Clamp(
            content.rect.width * railWidthFraction,
            minimumRailWidth,
            maximumRailWidth);

        ConfigureVerticalRail(leftCardContainer.parent as RectTransform, 0f, resolvedRailWidth, true);
        ConfigureVerticalRail(
            innerLeftCardContainer?.parent as RectTransform,
            resolvedRailWidth,
            resolvedRailWidth,
            true);
        ConfigureVerticalRail(rightCardContainer.parent as RectTransform, 0f, resolvedRailWidth, false);
        ConfigureVerticalRail(
            innerRightCardContainer?.parent as RectTransform,
            resolvedRailWidth,
            resolvedRailWidth,
            false);

        RectTransform stage = content.Cast<Transform>()
            .FirstOrDefault(child => child.name == "Stage area") as RectTransform;
        if (stage != null)
        {
            stage.anchorMin = Vector2.zero;
            stage.anchorMax = Vector2.one;
            stage.offsetMin = new Vector2(resolvedRailWidth, 0f);
            stage.offsetMax = new Vector2(-resolvedRailWidth, 0f);
        }
    }

    private static void ConfigureVerticalRail(
        RectTransform rail,
        float edgeInset,
        float width,
        bool left)
    {
        if (rail == null)
            return;
        float x = left ? 0f : 1f;
        rail.anchorMin = new Vector2(x, 0f);
        rail.anchorMax = new Vector2(x, 1f);
        rail.pivot = new Vector2(left ? 0f : 1f, 0.5f);
        rail.sizeDelta = new Vector2(width, 0f);
        rail.anchoredPosition = new Vector2(left ? edgeInset : -edgeInset, 0f);
    }

    private void AllocateAcrossRails(List<PresentationPosition> candidates)
    {
        candidates.Sort((a, b) => b.Presentation.DisplayRank.CompareTo(a.Presentation.DisplayRank));
        foreach (PresentationPosition candidate in candidates)
        {
            StatRailSide preferred = DetermineSide(
                candidate.Presentation,
                new Vector3(candidate.ViewportX, candidate.ViewportY, 1f));
            List<PresentationPosition> preferredOuter = preferred == StatRailSide.Left ? leftSources : rightSources;
            List<PresentationPosition> otherOuter = preferred == StatRailSide.Left ? rightSources : leftSources;
            RectTransform preferredOuterContainer = preferred == StatRailSide.Left ? leftCardContainer : rightCardContainer;
            RectTransform otherOuterContainer = preferred == StatRailSide.Left ? rightCardContainer : leftCardContainer;
            List<PresentationPosition> preferredInner = preferred == StatRailSide.Left ? innerLeftSources : innerRightSources;
            List<PresentationPosition> otherInner = preferred == StatRailSide.Left ? innerRightSources : innerLeftSources;
            RectTransform preferredInnerContainer = preferred == StatRailSide.Left ? innerLeftCardContainer : innerRightCardContainer;
            RectTransform otherInnerContainer = preferred == StatRailSide.Left ? innerRightCardContainer : innerLeftCardContainer;

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
        PresentationPosition candidate,
        List<PresentationPosition> target,
        RectTransform container)
    {
        if (container == null || target.Count >= maximumCardsPerRail)
            return false;
        float used = target.Sum(item => GetCardHeight(item.Presentation)) +
                     Mathf.Max(0, target.Count - 1) * minimumGap;
        float required = GetCardHeight(candidate.Presentation) + (target.Count > 0 ? minimumGap : 0f);
        float available = GetAvailableRailHeight(container);
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

    public bool TryGetCard(
        PerformanceStatSource source,
        out PerformanceStatCardView card)
    {
        return activeCards.TryGetValue(
            source,
            out card
        );
    }

    private StatRailSide DetermineSide(
    TelemetryCardPresentation presentation,
    Vector3 viewportPosition)
{
    switch (presentation.PreferredRail)
    {
        case PreferredStatRail.Left:
            return StatRailSide.Left;

        case PreferredStatRail.Right:
            return StatRailSide.Right;

        default:
            // Automatic placement uses the closest rail.
            return viewportPosition.x <= 0.5f
                ? StatRailSide.Left
                : StatRailSide.Right;
    }
}

    private void BuildRail(
        List<PresentationPosition> railSources,
        RectTransform container,
        StatRailSide side,
        List<RailCardBinding> bindings)
    {
        bindings.Clear();

        if (container == null)
            return;

        // Alarms always win limited rail space. Manual priority breaks ties.
        railSources.Sort((a, b) =>
        {
            int rankComparison = b.Presentation.DisplayRank.CompareTo(
                a.Presentation.DisplayRank
            );

            return rankComparison != 0
                ? rankComparison
                : string.CompareOrdinal(a.Presentation.Key, b.Presentation.Key);
        });

        float availableHeight = GetAvailableRailHeight(container);
        float usedHeight = 0f;
        int allowedCount = 0;
        for (int i = 0; i < railSources.Count && allowedCount < maximumCardsPerRail; i++)
        {
            float height = GetCardHeight(railSources[i].Presentation);
            float required = height + (allowedCount > 0 ? minimumGap : 0f);
            if (usedHeight + required > availableHeight)
                continue;
            usedHeight += required;
            railSources[allowedCount] = railSources[i];
            allowedCount++;
        }
        if (railSources.Count > allowedCount)
            railSources.RemoveRange(allowedCount, railSources.Count - allowedCount);

        // Establish a stable vertical order.
        railSources.Sort((a, b) =>
            b.ViewportY.CompareTo(a.ViewportY)
        );

        foreach (PresentationPosition positionedSource in railSources)
        {
            TelemetryCardPresentation presentation = positionedSource.Presentation;

            PerformanceStatCardView card =
                AcquireCard(container);

            RectTransform cardRect =
                card.transform as RectTransform;

            float resolvedHeight = GetCardHeight(presentation);
            ConfigureGeneratedCard(cardRect, resolvedHeight);
            card.SetPresentation(presentation, side);

            RailCardBinding binding =
                new RailCardBinding(
                    presentation,
                    card,
                    cardRect,
                    resolvedHeight
                );

            bindings.Add(binding);
            foreach (PerformanceStatSource source in presentation.Sources)
                activeCards[source] = card;
            generatedCards.Add(card);
        }
    }

    private float GetCardHeight(TelemetryCardPresentation presentation) =>
        cardHeight * Mathf.Min(presentation.MaximumVisibleMetrics, presentation.Sources.Count);

    private float GetAvailableRailHeight(RectTransform container)
    {
        if (container == null)
            return 0f;
        float height = container.rect.height;
        if (height <= 1f && container.parent is RectTransform rail)
            height = rail.rect.height;
        if (height <= 1f && container.parent?.parent is RectTransform content)
            height = content.rect.height;
        if (height <= 1f)
            height = canvas != null && canvas.scaleFactor > 0f
                ? Screen.safeArea.height / canvas.scaleFactor
                : Screen.safeArea.height;
        return Mathf.Max(0f, height - topPadding - bottomPadding);
    }

    private void ConfigureGeneratedCard(
        RectTransform cardRect,
        float resolvedHeight)
    {
        if (cardRect == null)
            return;

        // Stretch horizontally but retain a fixed height.
        cardRect.anchorMin =
            new Vector2(0f, 0.5f);

        cardRect.anchorMax =
            new Vector2(1f, 0.5f);

        cardRect.pivot =
            new Vector2(0.5f, 0.5f);

        cardRect.offsetMin =
            new Vector2(0f, -resolvedHeight * 0.5f);

        cardRect.offsetMax =
            new Vector2(0f, resolvedHeight * 0.5f);

        cardRect.anchoredPosition =
            new Vector2(0f, 0f);

        cardRect.localScale = Vector3.one;
        cardRect.localRotation = Quaternion.identity;
    }

    private void InitialiseRailPositions(
        List<RailCardBinding> bindings,
        RectTransform container)
    {
        if (bindings.Count == 0 || container == null)
            return;

        CalculateResolvedTargets(
            bindings,
            container
        );

        foreach (RailCardBinding binding in bindings)
        {
            Vector2 position =
                binding.RectTransform.anchoredPosition;

            position.y = binding.TargetY;

            binding.RectTransform.anchoredPosition =
                position;

            binding.CurrentVelocity = 0f;
        }
    }

    private void UpdateRail(
        List<RailCardBinding> bindings,
        RectTransform container)
    {
        if (bindings.Count == 0 ||
            container == null ||
            worldCamera == null)
        {
            return;
        }

        CalculateResolvedTargets(
            bindings,
            container
        );

        // First perform the smooth movement.
        foreach (RailCardBinding binding in bindings)
        {
            RectTransform cardRect =
                binding.RectTransform;

            if (cardRect == null)
                continue;

            Vector2 position =
                cardRect.anchoredPosition;

            position.y = Mathf.SmoothDamp(
                position.y,
                binding.TargetY,
                ref binding.CurrentVelocity,
                smoothTime,
                maximumSpeed,
                Time.unscaledDeltaTime
            );

            cardRect.anchoredPosition = position;
        }

        // Enforce separation during the animation too.
        // This prevents temporary intersection while cards are moving.
        ResolveCurrentPositionCollisions(
            bindings,
            container
        );
    }

    private void CalculateResolvedTargets(
        List<RailCardBinding> bindings,
        RectTransform container)
    {
        if (!preserveCardOrder)
        {
            bindings.Sort((a, b) =>
                GetDesiredLocalY(
                    b.Presentation,
                    container
                ).CompareTo(
                    GetDesiredLocalY(
                        a.Presentation,
                        container
                    )
                )
            );
        }

        float upperLimit =
            container.rect.yMax -
            topPadding -
            bindings[0].Height * 0.5f;

        float lowerLimit =
            container.rect.yMin +
            bottomPadding +
            bindings[^1].Height * 0.5f;

        // Start with each sensor's projected vertical position.
        for (int i = 0; i < bindings.Count; i++)
        {
            float desiredY =
                GetDesiredLocalY(
                    bindings[i].Presentation,
                    container
                );

            bindings[i].TargetY =
                Mathf.Clamp(
                    desiredY,
                    lowerLimit,
                    upperLimit
                );
        }

        // Push cards downward to eliminate intersections.
        for (int i = 1; i < bindings.Count; i++)
        {
            float highestAllowedY =
                bindings[i - 1].TargetY -
                (bindings[i - 1].Height + bindings[i].Height) * 0.5f - minimumGap;

            bindings[i].TargetY =
                Mathf.Min(
                    bindings[i].TargetY,
                    highestAllowedY
                );
        }

        // If the group crossed the bottom edge, push upward.
        if (bindings[bindings.Count - 1].TargetY < lowerLimit)
        {
            bindings[bindings.Count - 1].TargetY =
                lowerLimit;

            for (int i = bindings.Count - 2; i >= 0; i--)
            {
                float lowestAllowedY =
                    bindings[i + 1].TargetY +
                    (bindings[i + 1].Height + bindings[i].Height) * 0.5f + minimumGap;

                bindings[i].TargetY =
                    Mathf.Max(
                        bindings[i].TargetY,
                        lowestAllowedY
                    );
            }
        }

        // If the correction crossed the top edge, push downward again.
        if (bindings[0].TargetY > upperLimit)
        {
            bindings[0].TargetY =
                upperLimit;

            for (int i = 1; i < bindings.Count; i++)
            {
                bindings[i].TargetY =
                    bindings[i - 1].TargetY -
                    (bindings[i - 1].Height + bindings[i].Height) * 0.5f - minimumGap;
            }
        }
    }

    private void ResolveCurrentPositionCollisions(
        List<RailCardBinding> bindings,
        RectTransform container)
    {
        if (bindings.Count == 0)
            return;

        float upperLimit =
            container.rect.yMax -
            topPadding -
            bindings[0].Height * 0.5f;

        float lowerLimit =
            container.rect.yMin +
            bottomPadding +
            bindings[^1].Height * 0.5f;

        // Clamp the first card to the top boundary.
        SetCardY(
            bindings[0],
            Mathf.Min(
                GetCardY(bindings[0]),
                upperLimit
            )
        );

        // Prevent each following card from intersecting the one above.
        for (int i = 1; i < bindings.Count; i++)
        {
            float highestAllowedY =
                GetCardY(bindings[i - 1]) -
                (bindings[i - 1].Height + bindings[i].Height) * 0.5f - minimumGap;

            if (GetCardY(bindings[i]) > highestAllowedY)
            {
                SetCardY(
                    bindings[i],
                    highestAllowedY
                );

                bindings[i].CurrentVelocity = 0f;
            }
        }

        // Correct the bottom boundary.
        if (GetCardY(bindings[bindings.Count - 1]) < lowerLimit)
        {
            SetCardY(
                bindings[bindings.Count - 1],
                lowerLimit
            );

            bindings[bindings.Count - 1].CurrentVelocity = 0f;

            for (int i = bindings.Count - 2; i >= 0; i--)
            {
                float lowestAllowedY =
                    GetCardY(bindings[i + 1]) +
                    (bindings[i + 1].Height + bindings[i].Height) * 0.5f + minimumGap;

                if (GetCardY(bindings[i]) < lowestAllowedY)
                {
                    SetCardY(
                        bindings[i],
                        lowestAllowedY
                    );

                    bindings[i].CurrentVelocity = 0f;
                }
            }
        }
    }

    private float GetDesiredLocalY(
        TelemetryCardPresentation presentation,
        RectTransform container)
    {
        if (presentation == null || presentation.WorldAnchor == null)
        {
            return 0f;
        }

        Vector2 screenPoint =
            worldCamera.WorldToScreenPoint(
                presentation.WorldAnchor.position
            );

        Camera uiCamera = GetUICamera();

        bool converted =
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                container,
                screenPoint,
                uiCamera,
                out Vector2 localPoint
            );

        return converted ? localPoint.y : 0f;
    }

    private Camera GetUICamera()
    {
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        if (canvas.worldCamera != null)
            return canvas.worldCamera;

        return worldCamera;
    }

    private static float GetCardY(
        RailCardBinding binding)
    {
        return binding.RectTransform != null
            ? binding.RectTransform.anchoredPosition.y
            : 0f;
    }

    private static void SetCardY(
        RailCardBinding binding,
        float y)
    {
        if (binding.RectTransform == null)
            return;

        Vector2 position =
            binding.RectTransform.anchoredPosition;

        position.y = y;

        binding.RectTransform.anchoredPosition =
            position;
    }

    private void HandleSourceChanged(
        PerformanceStatSource source)
    {
        if (!source.IsVisible)
            return;

        if (!activeCards.TryGetValue(source, out var card))
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
        if (sources == null)
            return;

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
        leftBindings.Clear();
        rightBindings.Clear();
        innerLeftBindings.Clear();
        innerRightBindings.Clear();

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
        public readonly float Height;

        public float TargetY;
        public float CurrentVelocity;

        public RailCardBinding(
            TelemetryCardPresentation presentation,
            PerformanceStatCardView card,
            RectTransform rectTransform,
            float height)
        {
            Presentation = presentation;
            Card = card;
            RectTransform = rectTransform;
            Height = height;

            TargetY = 0f;
            CurrentVelocity = 0f;
        }
    }

    private readonly struct PresentationPosition
    {
        public readonly TelemetryCardPresentation Presentation;
        public readonly float ViewportX;
        public readonly float ViewportY;

        public PresentationPosition(
            TelemetryCardPresentation presentation,
            float viewportX,
            float viewportY)
        {
            Presentation = presentation;
            ViewportX = viewportX;
            ViewportY = viewportY;
        }
    }

    private sealed class RailWidthState
    {
        public float BaseWidth;
        public float CurrentScale = 1f;
        public float TargetScale = 1f;
        public float Velocity;
        public float LastExpansionRequestTime;

        public void Initialise(float width)
        {
            if (BaseWidth > 0f)
                return;
            BaseWidth = width;
            LastExpansionRequestTime = Time.unscaledTime;
        }
    }
}
