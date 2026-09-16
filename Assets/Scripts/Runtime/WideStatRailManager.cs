using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>What the wide layout's secondary rails, the ones beside the model, are for.</summary>
public enum SecondaryRailUse
{
    // Values are stored in scenes: keep them when adding options.
    [InspectorName("Detail Panel (extra info)")] DetailPanel = 0,
    [InspectorName("Telemetry Cards")] TelemetryCards = 1,
    [InspectorName("Off (primary rails only)")] Off = 2
}

[DefaultExecutionOrder(-100)]
public class WideStatRailManager : MonoBehaviour
{
    public event Action LayoutRebuilt;
    public event Action<int> HiddenPresentationCountChanged;
    public event Action<int, int> PaginationChanged;

    public IReadOnlyDictionary<
        PerformanceStatSource,
        PerformanceStatCardView
    > ActiveCards => activeCards;
    public int HiddenPresentationCount { get; private set; }
    public int CurrentPageIndex { get; private set; }
    public int PageCount { get; private set; } = 1;
    public bool CanShowPreviousPage => CurrentPageIndex > 0;
    public bool CanShowNextPage => CurrentPageIndex + 1 < PageCount;
    public bool HasSecondaryRailCards =>
        innerLeftBindings.Count > 0 || innerRightBindings.Count > 0;

    /// <summary>True while the secondary rails are kept free of cards for the detail panel.</summary>
    public bool SecondaryRailsReserved => reservedRailWidthMultiplier > 0f;

    // Cards only go to the secondary rails while nothing has reserved them.
    private RectTransform InnerLeftCards => SecondaryRailsReserved ? null : innerLeftCardContainer;
    private RectTransform InnerRightCards => SecondaryRailsReserved ? null : innerRightCardContainer;

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
    [SerializeField] private TelemetryRegistry telemetryRegistry;
    [SerializeField] private TelemetrySourceSelectionMode sourceSelectionMode =
        TelemetrySourceSelectionMode.SceneDiscovery;
    [SerializeField] private PerformanceStatSource[] sources;

    public TelemetrySourceSelectionMode SourceSelectionMode => sourceSelectionMode;

    [Header("Card Placement")]
    [SerializeField, Min(1f)]
    private float cardHeight = 119f;

    [SerializeField, Min(0f)]
    private float minimumGap = 8f;

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
    private int maximumCardsPerRail = 32;

    [Header("Responsive Rail Geometry")]
    [SerializeField, Min(120f)] private float minimumRailWidth = 180f;
    [SerializeField, Min(120f)] private float maximumRailWidth = 280f;
    [SerializeField, Range(0.1f, 0.3f)] private float railWidthFraction = 0.18f;

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

    private float reservedRailWidthMultiplier;
    private bool layoutBuilt;
    private bool rebuildPending;

    /// <summary>
    /// Keeps the secondary rails free of cards and widens them to
    /// <paramref name="widthMultiplier"/> times the outer rail width, as room
    /// for the detail panel; cards that no longer fit move to further pages.
    /// Releasing hands the rails back to cards. A change rebuilds the layout
    /// on this manager's next frame, never inside the caller's own enable,
    /// disable or teardown.
    /// </summary>
    public void ReserveSecondaryRails(bool reserve, float widthMultiplier = 1f)
    {
        float value = reserve ? Mathf.Max(1f, widthMultiplier) : 0f;
        if (Mathf.Approximately(value, reservedRailWidthMultiplier))
            return;
        reservedRailWidthMultiplier = value;
        rebuildPending = layoutBuilt;
    }

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
        if (rebuildPending)
            RebuildLayout();

        ReconcileCardHeights(leftBindings, leftCardContainer);
        ReconcileCardHeights(rightBindings, rightCardContainer);
        ReconcileCardHeights(innerLeftBindings, innerLeftCardContainer);
        ReconcileCardHeights(innerRightBindings, innerRightCardContainer);
        UpdateRail(leftBindings, leftCardContainer);
        UpdateRail(rightBindings, rightCardContainer);
        UpdateRail(innerLeftBindings, innerLeftCardContainer);
        UpdateRail(innerRightBindings, innerRightCardContainer);
    }

    private void ReconcileCardHeights(
        List<RailCardBinding> bindings,
        RectTransform container)
    {
        if (container == null || bindings.Count == 0)
            return;

        float width = container.rect.width;
        if (width <= 1f && container.parent is RectTransform rail)
            width = rail.rect.width;
        if (width <= 1f)
            return;

        foreach (RailCardBinding binding in bindings)
        {
            if (binding?.Card == null || binding.RectTransform == null)
                continue;

            float resolvedHeight = binding.Card.EstimateRequestedCardHeight(
                binding.Presentation,
                binding.Card.RailSide,
                cardHeight,
                width);
            if (Mathf.Abs(resolvedHeight - binding.Height) < 0.5f)
                continue;

            binding.Height = resolvedHeight;
            binding.RectTransform.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                resolvedHeight);
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromSources();
    }

    public void RebuildLayout()
    {
        rebuildPending = false;
        ClearGeneratedCards();
        UnsubscribeFromSources();

        ResolveSources();

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

        candidates.Sort((a, b) =>
        {
            int rankComparison = b.Presentation.DisplayRank.CompareTo(
                a.Presentation.DisplayRank);
            return rankComparison != 0
                ? rankComparison
                : string.CompareOrdinal(a.Presentation.Key, b.Presentation.Key);
        });

        List<List<PresentationPosition>> pages = BuildPages(candidates);
        PageCount = Mathf.Max(1, pages.Count);
        CurrentPageIndex = Mathf.Clamp(CurrentPageIndex, 0, PageCount - 1);

        List<PresentationPosition> pageCandidates = pages.Count > 0
            ? pages[CurrentPageIndex]
            : new List<PresentationPosition>();

        AllocateAcrossRails(pageCandidates);
        int visibleOnPage = leftSources.Count + rightSources.Count +
                            innerLeftSources.Count + innerRightSources.Count;
        SetHiddenPresentationCount(candidates.Count - visibleOnPage);

        Debug.Log(
            $"TELEMETRY_WIDE_LAYOUT candidates={candidates.Count} " +
            $"page={CurrentPageIndex + 1}/{PageCount} " +
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

        layoutBuilt = true;
        LayoutRebuilt?.Invoke();
        PaginationChanged?.Invoke(CurrentPageIndex, PageCount);
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
            telemetryRegistry = UnityEngine.Object.FindAnyObjectByType<TelemetryRegistry>(
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
        if (leftCardContainer?.parent?.parent is not RectTransform content)
            return;
        HorizontalLayoutGroup layout = content.GetComponent<HorizontalLayoutGroup>();
        if (layout != null)
            layout.enabled = false;

        float resolvedRailWidth = Mathf.Clamp(
            content.rect.width * railWidthFraction,
            minimumRailWidth,
            maximumRailWidth);

        float secondaryRailWidth = resolvedRailWidth *
            (SecondaryRailsReserved ? reservedRailWidthMultiplier : 1f);

        ConfigureVerticalRail(leftCardContainer.parent as RectTransform, 0f, resolvedRailWidth, true);
        ConfigureVerticalRail(
            innerLeftCardContainer?.parent as RectTransform,
            resolvedRailWidth,
            secondaryRailWidth,
            true);
        ConfigureVerticalRail(rightCardContainer.parent as RectTransform, 0f, resolvedRailWidth, false);
        ConfigureVerticalRail(
            innerRightCardContainer?.parent as RectTransform,
            resolvedRailWidth,
            secondaryRailWidth,
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
            RectTransform preferredInnerContainer = preferred == StatRailSide.Left ? InnerLeftCards : InnerRightCards;
            RectTransform otherInnerContainer = preferred == StatRailSide.Left ? InnerRightCards : InnerLeftCards;

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

    private List<List<PresentationPosition>> BuildPages(
        IReadOnlyList<PresentationPosition> candidates)
    {
        List<List<PresentationPosition>> pages = new();
        PageAllocation allocation = new();

        foreach (PresentationPosition candidate in candidates)
        {
            if (TryAllocateToPage(candidate, allocation))
                continue;

            if (allocation.Candidates.Count > 0)
            {
                pages.Add(allocation.Candidates);
                allocation = new PageAllocation();
            }

            if (!TryAllocateToPage(candidate, allocation))
            {
                // A single card should always fit a rail. Retaining it on a page
                // is safer than silently making telemetry permanently inaccessible.
                allocation.Candidates.Add(candidate);
            }
        }

        if (allocation.Candidates.Count > 0)
            pages.Add(allocation.Candidates);

        return pages;
    }

    private bool TryAllocateToPage(
        PresentationPosition candidate,
        PageAllocation allocation)
    {
        StatRailSide preferred = DetermineSide(
            candidate.Presentation,
            new Vector3(candidate.ViewportX, candidate.ViewportY, 1f));

        List<PresentationPosition> preferredOuter = preferred == StatRailSide.Left
            ? allocation.Left
            : allocation.Right;
        List<PresentationPosition> otherOuter = preferred == StatRailSide.Left
            ? allocation.Right
            : allocation.Left;
        RectTransform preferredOuterContainer = preferred == StatRailSide.Left
            ? leftCardContainer
            : rightCardContainer;
        RectTransform otherOuterContainer = preferred == StatRailSide.Left
            ? rightCardContainer
            : leftCardContainer;
        List<PresentationPosition> preferredInner = preferred == StatRailSide.Left
            ? allocation.InnerLeft
            : allocation.InnerRight;
        List<PresentationPosition> otherInner = preferred == StatRailSide.Left
            ? allocation.InnerRight
            : allocation.InnerLeft;
        RectTransform preferredInnerContainer = preferred == StatRailSide.Left
            ? InnerLeftCards
            : InnerRightCards;
        RectTransform otherInnerContainer = preferred == StatRailSide.Left
            ? InnerRightCards
            : InnerLeftCards;

        bool allocated =
            TryAllocate(candidate, preferredOuter, preferredOuterContainer) ||
            TryAllocate(candidate, otherOuter, otherOuterContainer) ||
            TryAllocate(candidate, preferredInner, preferredInnerContainer) ||
            TryAllocate(candidate, otherInner, otherInnerContainer);

        if (allocated)
            allocation.Candidates.Add(candidate);

        return allocated;
    }

    public void ShowPreviousPage()
    {
        ShowPage(CurrentPageIndex - 1);
    }

    public void ShowNextPage()
    {
        ShowPage(CurrentPageIndex + 1);
    }

    public void ShowFirstPage()
    {
        ShowPage(0);
    }

    public void ShowPage(int pageIndex)
    {
        int clamped = Mathf.Clamp(pageIndex, 0, Mathf.Max(0, PageCount - 1));
        if (clamped == CurrentPageIndex)
            return;

        CurrentPageIndex = clamped;
        RebuildLayout();
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
        float used = target.Sum(item => GetCardHeight(item.Presentation, container)) +
                     Mathf.Max(0, target.Count - 1) * minimumGap;
        float required = GetCardHeight(candidate.Presentation, container) +
                         (target.Count > 0 ? minimumGap : 0f);
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

    /// <summary>The outer (primary) or inner (overflow) rail on one side.</summary>
    public RectTransform GetRail(StatRailSide side, bool inner)
    {
        RectTransform container = side == StatRailSide.Left
            ? inner ? innerLeftCardContainer : leftCardContainer
            : inner ? innerRightCardContainer : rightCardContainer;
        return container != null ? container.parent as RectTransform : null;
    }

    /// <summary>True when the card is currently shown on one of this manager's rails.</summary>
    public bool OwnsCard(PerformanceStatCardView card) =>
        card != null && activeCards.ContainsValue(card);

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
            float height = GetCardHeight(railSources[i].Presentation, container);
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

            float resolvedHeight = GetCardHeight(presentation, container);
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
            activeCards[presentation.Source] = card;
            generatedCards.Add(card);
        }
    }

    private float GetCardHeight(
        TelemetryCardPresentation presentation,
        RectTransform container)
    {
        float width = container != null ? container.rect.width : 0f;
        if (width <= 1f && container?.parent is RectTransform rail)
            width = rail.rect.width;
        if (width <= 1f)
            width = maximumRailWidth;
        return cardPrefab != null
            ? cardPrefab.EstimateRequestedCardHeight(
                presentation,
                StatRailSide.Left,
                cardHeight,
                width)
            : cardHeight;
    }

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
        // A newly raised alarm must always return to the highest-priority page.
        CurrentPageIndex = 0;
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
        public float Height;

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

    private sealed class PageAllocation
    {
        public readonly List<PresentationPosition> Candidates = new();
        public readonly List<PresentationPosition> Left = new();
        public readonly List<PresentationPosition> Right = new();
        public readonly List<PresentationPosition> InnerLeft = new();
        public readonly List<PresentationPosition> InnerRight = new();
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

}
