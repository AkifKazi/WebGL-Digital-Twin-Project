using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The telemetry card rails of one layout. Cards sit on rails beside the
/// model: columns left and right of it, or rows above and below it. The wide
/// layout (a desktop browser, a phone held sideways) runs columns, or rows in
/// a window taller than it is wide; the portrait layout (a phone held upright)
/// always runs rows and opens the history panel as a sheet instead of on a
/// rail. Both are this one component, set up differently. Each side
/// runs as many rails as the <see cref="TelemetryLayoutSettings"/> ask for:
/// the two authored ones, and further ones generated beside them. A rail
/// inside the outermost one shows only while it holds a card, so the model
/// keeps the room. When cards still do not fit they go to further pages.
///
/// With the history panel on, a detail rail of its own runs between the card
/// rails and the model on each side; the panel never takes a card rail.
///
/// A rebuild (an alarm, a new page, a resize, a language) keeps every card
/// that stays on screen: the same card, on the same side when its sensor has
/// not clearly crossed over, gliding from where it was. Nothing jumps.
/// </summary>
[DefaultExecutionOrder(-100)]
public class StatRailManager : MonoBehaviour
{
    // Rails stack inwards from two edges of the content area.
    private const int First = 0;   // left, or top
    private const int Second = 1;  // right, or bottom

    // A sensor keeps its card's side until it is this far (in viewport) past the middle.
    private const float EdgeHysteresis = 0.12f;

    // How long cards may pass each other while they glide to a rebuilt order.
    private const float ReorderGlideSeconds = 0.6f;

    public event Action LayoutRebuilt;
    public event Action<int> HiddenPresentationCountChanged;
    public event Action<int, int> PaginationChanged;

    public IReadOnlyDictionary<PerformanceStatSource, PerformanceStatCardView> ActiveCards => activeCards;
    public int HiddenPresentationCount { get; private set; }
    public int CurrentPageIndex { get; private set; }
    public int PageCount { get; private set; } = 1;
    public bool CanShowPreviousPage => CurrentPageIndex > 0;
    public bool CanShowNextPage => CurrentPageIndex + 1 < PageCount;

    /// <summary>True while a rail inside the outermost one holds a card.</summary>
    public bool HasSecondaryRailCards { get; private set; }

    /// <summary>True while the rails run as rows above and below the model.</summary>
    public bool RailsAreRows => rows;

    /// <summary>Card rails per side in the current layout. Those without a card are hidden.</summary>
    public int RailsPerSide => railCount;

    /// <summary>True while the history panel has its detail rail beside the cards.</summary>
    public bool DetailRailActive => detailRail;

    /// <summary>True for the layout that always runs rows (the phone held upright).</summary>
    public bool AlwaysRows => alwaysRows;

    [Header("References")]
    [SerializeField] private Camera worldCamera;
    [SerializeField] private Canvas canvas;
    [SerializeField] private PerformanceStatCardView cardPrefab;

    [Header("Sources")]
    [SerializeField] private TelemetryRegistry telemetryRegistry;
    [SerializeField] private TelemetrySourceSelectionMode sourceSelectionMode =
        TelemetrySourceSelectionMode.SceneDiscovery;
    [SerializeField] private PerformanceStatSource[] sources;

    [Header("Layout")]
    [Tooltip("The phone held upright: the rails always run as rows above and below the model. " +
             "Otherwise they run as columns, or as rows in a window taller than it is wide.")]
    [SerializeField] private bool alwaysRows;

    [Tooltip("The history panel opens on a rail of its own beside the cards. Off where it " +
             "opens another way (the portrait sheet).")]
    [SerializeField] private bool hasDetailRail = true;

    [Header("Card Rails")]
    [Tooltip("The outermost rail on each side, and the one inside it. First is the left column " +
             "or the top row; second the right column or the bottom row. Further rails are " +
             "generated inside these when the Telemetry Layout Settings ask for more.")]
    [SerializeField] private RectTransform firstCardContainer;
    [SerializeField] private RectTransform secondCardContainer;
    [SerializeField] private RectTransform innerFirstCardContainer;
    [SerializeField] private RectTransform innerSecondCardContainer;

    [Header("Columns")]
    [SerializeField, Min(0f)] private float columnGap = 8f;
    [SerializeField, Min(0f)] private float columnStartPadding;
    [SerializeField, Min(0f)] private float columnEndPadding;
    [SerializeField, Min(120f)] private float minimumColumnWidth = 180f;
    [SerializeField, Min(120f)] private float maximumColumnWidth = 280f;
    [Tooltip("A column's width as a share of the content area's, between the limits above.")]
    [SerializeField, Range(0.1f, 0.3f)] private float columnWidthFraction = 0.18f;

    [Header("Rows")]
    [Tooltip("Card width while the rails run as rows above and below the model.")]
    [SerializeField, Min(120f)] private float rowCardWidth = 300f;
    [SerializeField, Min(0f)] private float rowGap = 16f;
    [Tooltip("Space kept clear at the left and right ends of a row.")]
    [SerializeField, Min(0f)] private float rowStartPadding = 16f;
    [SerializeField, Min(0f)] private float rowEndPadding = 16f;
    [Tooltip("A row is never shorter than this, so the cards have room for their accent bar.")]
    [SerializeField, Min(100f)] private float minimumRowHeight = 150f;

    [Header("Cards")]
    [SerializeField, Min(1f)] private float cardHeight = 113f;
    [SerializeField, Range(0.05f, 0.5f)] private float smoothTime = 0.18f;
    [SerializeField, Min(1f)] private float maximumSpeed = 1500f;
    [SerializeField, Min(1)] private int maximumCardsPerRail = 32;

    [Tooltip("Cards keep their order along a rail so they never pass through each other.")]
    [SerializeField] private bool preserveCardOrder = true;

    [Header("Detail Connectors")]
    [Tooltip("While the detail panel has its rail, a connector runs from a card to its open panel, " +
             "across the rails nearer the model. Their cards keep this far clear of it on each side. " +
             "Those rails also hold one card fewer than the outermost one, so there is room to.")]
    [SerializeField, Min(0f)] private float connectorClearance = 10f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;

    // Each source's alarm level when the layout was last built.
    private readonly Dictionary<PerformanceStatSource, int> subscribedAlarmRanks = new();
    private readonly List<PresentationPosition> alarmRefill = new();
    private readonly List<PresentationPosition> normalRefill = new();
    private readonly Dictionary<PerformanceStatSource, PerformanceStatCardView> activeCards = new();
    private readonly Stack<PerformanceStatCardView> cardPool = new();

    // The previous layout, so a rebuild keeps cards where they were.
    private readonly Dictionary<PerformanceStatSource, CarriedCard> carriedCards = new();
    private readonly Dictionary<PerformanceStatSource, int> previousEdges = new();
    private bool previousRows;

    private readonly List<Rail>[] rails = { new(), new() };
    private readonly RectTransform[] detailRails = new RectTransform[2];
    private readonly float[] detailInsets = new float[2];
    private RectTransform stageArea;

    private TelemetryLayoutProfile requested;
    private bool profileSet;
    private bool rows;
    private bool detailRail;
    private int railCount = 1;
    private float detailThickness;
    private int warnedRailRequest = -1;

    private bool layoutBuilt;
    private bool rebuildPending;

    // Reused every frame by the card spread.
    private float[] spreadDesired;
    private float[] spreadExtents;
    private float[] spreadTargets;
    private float[] keepOutStarts = new float[4];
    private float[] keepOutEnds = new float[4];
    private static readonly Vector3[] ConnectorPoints = new Vector3[2];

    // -----------------------------------------------------------------------
    // The layout
    // -----------------------------------------------------------------------

    /// <summary>The authored card container at this depth (0 outermost, 1 inside it), or null.</summary>
    private RectTransform AuthoredContainer(int edge, int depth) => (edge, depth) switch
    {
        (First, 0) => firstCardContainer,
        (Second, 0) => secondCardContainer,
        (First, 1) => innerFirstCardContainer,
        (Second, 1) => innerSecondCardContainer,
        _ => null
    };

    /// <summary>Width of a card column.</summary>
    private float ColumnWidth(RectTransform content) => Mathf.Clamp(
        (content != null ? content.rect.width : 0f) * columnWidthFraction,
        minimumColumnWidth,
        maximumColumnWidth);

    /// <summary>
    /// A sensor whose authored preference names a side keeps that side: its
    /// column preference while the rails run as columns, its row preference
    /// while they run as rows.
    /// </summary>
    private static bool TryGetFixedEdge(TelemetryCardPresentation presentation, bool rowsLayout, out int edge)
    {
        edge = First;
        if (rowsLayout)
        {
            switch (presentation.PreferredPortraitRail)
            {
                case PreferredPortraitStatRail.Top: edge = First; return true;
                case PreferredPortraitStatRail.Bottom: edge = Second; return true;
                default: return false;
            }
        }

        switch (presentation.PreferredRail)
        {
            case PreferredStatRail.Left: edge = First; return true;
            case PreferredStatRail.Right: edge = Second; return true;
            default: return false;
        }
    }

    // -----------------------------------------------------------------------
    // Settings
    // -----------------------------------------------------------------------

    /// <summary>
    /// What the next rebuild lays out: rows or columns, how many rails, and
    /// whether the history panel has its rail. The responsive shell sets it
    /// and rebuilds once the screen has settled, so the rails never spend a
    /// frame as rows holding cards placed as columns.
    /// </summary>
    public void SetLayoutProfile(TelemetryLayoutProfile profile)
    {
        requested = profile;
        profileSet = true;
    }

    /// <summary>Lays the rails out again on this manager's next frame.</summary>
    public void RequestRebuild() => rebuildPending = layoutBuilt;

    // -----------------------------------------------------------------------
    // Queries
    // -----------------------------------------------------------------------

    public bool TryGetCard(PerformanceStatSource source, out PerformanceStatCardView card) =>
        activeCards.TryGetValue(source, out card);

    /// <summary>True when the card is currently shown on one of this manager's rails.</summary>
    public bool OwnsCard(PerformanceStatCardView card) =>
        card != null && activeCards.ContainsValue(card);

    /// <summary>A card rail on one side (0 is the outermost), or null when it is not shown.</summary>
    public RectTransform GetRail(StatRailSide side, int depth)
    {
        List<Rail> edgeRails = rails[EdgeOf(side)];
        if (depth < 0 || depth >= edgeRails.Count)
            return null;
        Rail rail = edgeRails[depth];
        return rail.Shown ? rail.Root : null;
    }

    /// <summary>The history panel's rail on one side, or null while the panel is off.</summary>
    public RectTransform GetDetailRail(StatRailSide side)
    {
        RectTransform rail = detailRails[EdgeOf(side)];
        return detailRail && rail != null && rail.gameObject.activeSelf ? rail : null;
    }

    private static int EdgeOf(StatRailSide side) =>
        side is StatRailSide.Left or StatRailSide.Top ? First : Second;

    private StatRailSide SideOf(int edge) => rows
        ? edge == First ? StatRailSide.Top : StatRailSide.Bottom
        : edge == First ? StatRailSide.Left : StatRailSide.Right;

    private string EdgeName(int edge) => SideOf(edge).ToString();

    // -----------------------------------------------------------------------
    // Lifecycle
    // -----------------------------------------------------------------------

    private void OnEnable()
    {
        UIText.Changed -= HandleLanguageChanged;
        UIText.Changed += HandleLanguageChanged;
    }

    private void OnDisable()
    {
        UIText.Changed -= HandleLanguageChanged;
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

    private void OnDestroy() => UnsubscribeFromSources();

    // A translated label can wrap onto more or fewer lines, so the cards are
    // redrawn and the rails laid out again on the next frame.
    private void HandleLanguageChanged()
    {
        foreach (PerformanceStatCardView card in activeCards.Values)
        {
            if (card != null)
                card.RefreshBoundSources();
        }
        rebuildPending = layoutBuilt;
    }

    private void LateUpdate()
    {
        if (rebuildPending)
            RebuildLayout();

        bool resized = false;
        for (int edge = 0; edge < 2; edge++)
        {
            foreach (Rail rail in rails[edge])
            {
                if (rail.Shown)
                    resized |= ReconcileCardHeights(rail);
            }
        }
        // A row is as tall as its tallest card.
        if (resized && rows)
            PlaceRails(true);

        for (int edge = 0; edge < 2; edge++)
        {
            foreach (Rail rail in rails[edge])
            {
                if (rail.Shown)
                    UpdateRail(rail);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Building
    // -----------------------------------------------------------------------

    [ContextMenu("Rebuild Layout")]
    public void RebuildLayout()
    {
        rebuildPending = false;
        if (!profileSet)
            requested = new TelemetryLayoutProfile(alwaysRows, TelemetryRailCount.Auto, hasDetailRail);
        bool wasRows = rows;
        rows = alwaysRows || requested.Rows;
        detailRail = hasDetailRail && requested.DetailPanel;

        CarryCurrentLayout(wasRows);
        UnsubscribeFromSources();
        ResolveSources();

        if (worldCamera == null || canvas == null || cardPrefab == null ||
            AuthoredContainer(First, 0) == null || AuthoredContainer(Second, 0) == null ||
            ContentArea == null)
        {
            Debug.LogWarning($"{name} is missing one or more references.", this);
            ReleaseCarriedCards();
            return;
        }

        if (ContentArea.TryGetComponent(out LayoutGroup contentLayout))
            contentLayout.enabled = false;

        sources ??= Array.Empty<PerformanceStatSource>();
        List<PresentationPosition> candidates = new();
        foreach (TelemetryCardPresentation presentation in TelemetryPresentationBuilder.Build(sources))
        {
            if (presentation == null || presentation.WorldAnchor == null)
                continue;
            Vector2 viewport = TelemetryPresentationBuilder.PlacementViewport(
                worldCamera, presentation.WorldAnchor.position);
            candidates.Add(new PresentationPosition(presentation, viewport.x, viewport.y));
        }

        // Alarms first, then display priority, then a stable key.
        candidates.Sort((a, b) =>
        {
            int rank = b.Presentation.DisplayRank.CompareTo(a.Presentation.DisplayRank);
            return rank != 0 ? rank : string.CompareOrdinal(a.Presentation.Key, b.Presentation.Key);
        });

        float thickness = NominalRailThickness(candidates);
        detailThickness = detailRail ? DetailThickness() : 0f;
        railCount = ResolveRailCount(thickness);
        EnsureRails(railCount);
        EnsureDetailRails();

        // Every rail this layout may use is placed before it is measured.
        for (int edge = 0; edge < 2; edge++)
        {
            List<Rail> edgeRails = rails[edge];
            for (int depth = 0; depth < edgeRails.Count; depth++)
            {
                Rail rail = edgeRails[depth];
                rail.Bindings.Clear();
                rail.Thickness = thickness;
                rail.Shown = depth < railCount;
            }
        }
        PlaceRails(false);
        Canvas.ForceUpdateCanvases();

        List<PageAllocation> pages = BuildPages(candidates);
        PageCount = Mathf.Max(1, pages.Count);
        CurrentPageIndex = Mathf.Clamp(CurrentPageIndex, 0, PageCount - 1);
        PageAllocation page = pages.Count > 0 ? pages[CurrentPageIndex] : new PageAllocation(railCount);

        int visible = 0;
        HasSecondaryRailCards = false;
        for (int edge = 0; edge < 2; edge++)
        {
            for (int depth = 0; depth < railCount; depth++)
            {
                Rail rail = rails[edge][depth];
                BuildRail(rail, page.Lists[edge, depth]);
                visible += rail.Bindings.Count;
                if (depth > 0 && rail.Bindings.Count > 0)
                    HasSecondaryRailCards = true;
            }
        }
        SetHiddenPresentationCount(candidates.Count - visible);

        // Inner rails without a card hide, rows take their tallest card's height,
        // and the detail rail moves in beside the innermost rail shown.
        for (int edge = 0; edge < 2; edge++)
        {
            for (int depth = 0; depth < rails[edge].Count; depth++)
            {
                Rail rail = rails[edge][depth];
                rail.Shown = depth < railCount && (depth == 0 || rail.Bindings.Count > 0);
            }
        }
        PlaceRails(true);
        Canvas.ForceUpdateCanvases();

        SubscribeToSources();
        for (int edge = 0; edge < 2; edge++)
        {
            foreach (Rail rail in rails[edge])
            {
                if (rail.Shown)
                    InitialisePositions(rail);
            }
        }
        ReleaseCarriedCards();

        if (showDebugLogs)
        {
            Debug.Log(
                $"TELEMETRY_RAIL_LAYOUT {name} {(rows ? "rows" : "columns")} candidates={candidates.Count} " +
                $"page={CurrentPageIndex + 1}/{PageCount} railsPerSide={railCount} (asked {requested.Rails}) " +
                $"detail={detailRail} first=[{RailSummary(First)}] second=[{RailSummary(Second)}]",
                this);
        }

        layoutBuilt = true;
        LayoutRebuilt?.Invoke();
        PaginationChanged?.Invoke(CurrentPageIndex, PageCount);
    }

    private string RailSummary(int edge) =>
        string.Join(",", rails[edge].Where(rail => rail.Shown).Select(rail => rail.Bindings.Count));

    private RectTransform ContentArea =>
        AuthoredContainer(First, 0) != null ? AuthoredContainer(First, 0).parent?.parent as RectTransform : null;

    private void ResolveSources()
    {
        if (sourceSelectionMode == TelemetrySourceSelectionMode.ExplicitList)
        {
            sources = TelemetrySourceResolver.Resolve(sourceSelectionMode, sources);
            return;
        }

        if (telemetryRegistry == null)
            telemetryRegistry = FindAnyObjectByType<TelemetryRegistry>(FindObjectsInactive.Include);

        if (telemetryRegistry != null)
        {
            telemetryRegistry.RebuildIndex();
            sources = telemetryRegistry.Sources.ToArray();
            return;
        }

        sources = TelemetrySourceResolver.Resolve(sourceSelectionMode, sources);
    }

    // -----------------------------------------------------------------------
    // How many rails
    // -----------------------------------------------------------------------

    /// <summary>A rail's thickness across the screen before its cards are known.</summary>
    private float NominalRailThickness(List<PresentationPosition> candidates)
    {
        if (!rows)
            return ColumnWidth(ContentArea);

        float width = Mathf.Min(rowCardWidth, Mathf.Max(1f, ContentLength() - rowStartPadding - rowEndPadding));
        float tallest = minimumRowHeight;
        foreach (PresentationPosition candidate in candidates)
        {
            tallest = Mathf.Max(tallest, cardPrefab.EstimateRequestedCardHeight(
                candidate.Presentation, StatRailSide.Top, cardHeight, width));
        }
        return tallest;
    }

    private float DetailThickness() => rows
        ? requested.DetailRailHeight
        : ColumnWidth(ContentArea) * requested.DetailRailWidthMultiplier;

    /// <summary>The content area's size across the rails: its width for columns, its height for rows.</summary>
    private float ContentCross()
    {
        Rect rect = ContentArea.rect;
        float cross = rows ? rect.height : rect.width;
        if (cross <= 1f)
        {
            Rect safe = DeviceSafeArea.Current;
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            cross = (rows ? safe.height : safe.width) / scale;
        }
        return cross;
    }

    /// <summary>The content area's size along the rails.</summary>
    private float ContentLength()
    {
        Rect rect = ContentArea.rect;
        float length = rows ? rect.width : rect.height;
        if (length <= 1f)
        {
            Rect safe = DeviceSafeArea.Current;
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            length = (rows ? safe.width : safe.height) / scale;
        }
        return length;
    }

    /// <summary>
    /// Auto: as many rails as fit while the model keeps its share of the screen
    /// and the history panel's rail still fits between them. A set number is
    /// kept as long as the rails and that panel fit at all; asking for more
    /// than fit runs as many as do, with a warning.
    /// </summary>
    private int ResolveRailCount(float thickness)
    {
        float cross = ContentCross();
        float modelShare = requested.AutoModelShare;

        // At the least, one history panel and a sliver of the model stay clear.
        int fit = FitCount(cross, Mathf.Max(detailThickness, cross * 0.12f), thickness);
        if (requested.Rails == TelemetryRailCount.Auto)
            return Mathf.Clamp(FitCount(cross, Mathf.Max(detailThickness, cross * modelShare), thickness), 1, fit);

        int asked = (int)requested.Rails;
        if (asked > fit && warnedRailRequest != asked)
        {
            warnedRailRequest = asked;
            Debug.LogWarning(
                $"{name}: {asked} {(rows ? "rows" : "columns")} per side do not fit on this screen" +
                (detailRail ? " beside the history panel's rail" : string.Empty) + $"; showing {fit}.", this);
        }
        return Mathf.Clamp(asked, 1, fit);
    }

    private static int FitCount(float cross, float reserve, float thickness)
    {
        if (thickness <= 1f)
            return 1;
        int count = Mathf.FloorToInt((cross - reserve) / (2f * thickness) + 0.001f);
        return Mathf.Clamp(count, 1, TelemetryLayoutSettings.MaximumRailsPerSide);
    }

    // -----------------------------------------------------------------------
    // Rails
    // -----------------------------------------------------------------------

    private void EnsureRails(int count)
    {
        for (int edge = 0; edge < 2; edge++)
        {
            List<Rail> edgeRails = rails[edge];
            // The authored rails come first; a reference changed in the inspector is picked up.
            for (int depth = 0; depth < 2; depth++)
            {
                RectTransform container = AuthoredContainer(edge, depth);
                if (container == null || container.parent is not RectTransform root)
                    break;
                if (depth < edgeRails.Count)
                {
                    if (edgeRails[depth].Container != container)
                        edgeRails[depth] = new Rail(root, container, edge);
                }
                else
                {
                    edgeRails.Add(new Rail(root, container, edge));
                }
            }

            while (edgeRails.Count < count)
                edgeRails.Add(CreateRail(edge, edgeRails.Count));
        }
    }

    /// <summary>A new rail inside the deepest one on its side, laid out like the authored ones.</summary>
    private Rail CreateRail(int edge, int depth)
    {
        Rail template = rails[edge][0];
        Rail inner = rails[edge][depth - 1];
        string side = edge == First ? "Left or top" : "Right or bottom";

        GameObject railObject = new($"{side} card rail {depth + 1}", typeof(RectTransform));
        railObject.layer = template.Root.gameObject.layer;
        RectTransform root = (RectTransform)railObject.transform;
        root.SetParent(template.Root.parent, false);
        root.SetSiblingIndex(edge == First ? inner.Root.GetSiblingIndex() + 1 : inner.Root.GetSiblingIndex());

        GameObject containerObject = new($"{side} card container {depth + 1}", typeof(RectTransform));
        containerObject.layer = railObject.layer;
        RectTransform container = (RectTransform)containerObject.transform;
        container.SetParent(root, false);
        container.anchorMin = Vector2.zero;
        container.anchorMax = Vector2.one;
        container.offsetMin = Vector2.zero;
        container.offsetMax = Vector2.zero;

        return new Rail(root, container, edge);
    }

    private void EnsureDetailRails()
    {
        for (int edge = 0; edge < 2; edge++)
        {
            if (detailRails[edge] != null)
                continue;
            if (!detailRail)
                continue;
            Rail template = rails[edge][0];
            GameObject railObject = new(
                $"{(edge == First ? "Left or top" : "Right or bottom")} detail rail", typeof(RectTransform));
            railObject.layer = template.Root.gameObject.layer;
            RectTransform root = (RectTransform)railObject.transform;
            root.SetParent(template.Root.parent, false);
            detailRails[edge] = root;
        }
    }

    /// <summary>
    /// Stacks each side's shown rails inwards from its edge, the detail rail
    /// inside them, and fits the stage between the outermost rails. With
    /// <paramref name="fitRows"/>, a row first takes its tallest card's height.
    /// </summary>
    private void PlaceRails(bool fitRows)
    {
        RectTransform content = ContentArea;
        if (content == null)
            return;

        for (int edge = 0; edge < 2; edge++)
        {
            float inset = 0f;
            foreach (Rail rail in rails[edge])
            {
                if (rail.Root.gameObject.activeSelf != rail.Shown)
                    rail.Root.gameObject.SetActive(rail.Shown);
                if (!rail.Shown)
                    continue;
                if (fitRows && rows)
                    rail.Thickness = TallestCard(rail);
                ConfigureRailRect(rail.Root, inset, rail.Thickness, edge);
                inset += rail.Thickness;
            }

            detailInsets[edge] = inset;
            RectTransform detail = detailRails[edge];
            if (detail != null)
            {
                if (detail.gameObject.activeSelf != detailRail)
                    detail.gameObject.SetActive(detailRail);
                if (detailRail)
                    ConfigureRailRect(detail, inset, detailThickness, edge);
            }
        }

        float firstOuter = rails[First].Count > 0 ? rails[First][0].Thickness : 0f;
        float secondOuter = rails[Second].Count > 0 ? rails[Second][0].Thickness : 0f;
        if (stageArea == null || stageArea.parent != content)
            stageArea = content.Find("Stage area") as RectTransform;
        if (stageArea != null)
        {
            stageArea.anchorMin = Vector2.zero;
            stageArea.anchorMax = Vector2.one;
            stageArea.offsetMin = rows ? new Vector2(0f, secondOuter) : new Vector2(firstOuter, 0f);
            stageArea.offsetMax = rows ? new Vector2(0f, -firstOuter) : new Vector2(-secondOuter, 0f);
        }
    }

    private float TallestCard(Rail rail)
    {
        float tallest = minimumRowHeight;
        foreach (RailCardBinding binding in rail.Bindings)
        {
            if (binding.RectTransform != null)
                tallest = Mathf.Max(tallest, binding.RectTransform.sizeDelta.y);
        }
        return tallest;
    }

    private void ConfigureRailRect(RectTransform rail, float inset, float thickness, int edge)
    {
        bool first = edge == First;
        if (rows)
        {
            float y = first ? 1f : 0f;
            rail.anchorMin = new Vector2(0f, y);
            rail.anchorMax = new Vector2(1f, y);
            rail.pivot = new Vector2(0.5f, y);
            rail.sizeDelta = new Vector2(0f, thickness);
            rail.anchoredPosition = new Vector2(0f, first ? -inset : inset);
        }
        else
        {
            float x = first ? 0f : 1f;
            rail.anchorMin = new Vector2(x, 0f);
            rail.anchorMax = new Vector2(x, 1f);
            rail.pivot = new Vector2(x, 0.5f);
            rail.sizeDelta = new Vector2(thickness, 0f);
            rail.anchoredPosition = new Vector2(first ? inset : -inset, 0f);
        }
    }

    // -----------------------------------------------------------------------
    // Pages
    // -----------------------------------------------------------------------

    private List<PageAllocation> BuildPages(List<PresentationPosition> candidates)
    {
        List<PageAllocation> pages = new();
        PageAllocation page = new(railCount);

        foreach (PresentationPosition candidate in candidates)
        {
            if (TryAllocate(candidate, page))
                continue;

            if (page.Count > 0)
            {
                PlaceAlarmsInnermost(page);
                pages.Add(page);
                page = new PageAllocation(railCount);
            }

            // A single card always fits an empty rail; one that somehow does
            // not still gets a page of its own rather than vanishing.
            if (!TryAllocate(candidate, page))
                page.Count++;
        }

        if (page.Count > 0)
        {
            PlaceAlarmsInnermost(page);
            pages.Add(page);
        }
        return pages;
    }

    /// <summary>
    /// Warning and critical cards move to the innermost rail shown on their
    /// side, nearest the model: their lines are always drawn, so they should be
    /// the shortest and run across no other card. Normal cards then fill
    /// outward first, as they were allocated. No rail opens for this: a side
    /// with one rail shown is left alone, and so is a side whose cards would
    /// not all fit, or would leave a shown rail empty, placed this way.
    /// </summary>
    private void PlaceAlarmsInnermost(PageAllocation page)
    {
        for (int edge = 0; edge < 2; edge++)
        {
            int innermost = 0;
            for (int depth = railCount - 1; depth > 0; depth--)
            {
                if (page.Lists[edge, depth].Count > 0)
                {
                    innermost = depth;
                    break;
                }
            }
            if (innermost == 0)
                continue;

            alarmRefill.Clear();
            normalRefill.Clear();
            for (int depth = 0; depth <= innermost; depth++)
            {
                foreach (PresentationPosition candidate in page.Lists[edge, depth])
                    (IsAlarm(candidate) ? alarmRefill : normalRefill).Add(candidate);
            }
            if (alarmRefill.Count == 0 || AlarmsAlreadyInnermost(page, edge, innermost))
                continue;

            PageAllocation trial = new(railCount);
            bool fits = true;
            foreach (PresentationPosition alarm in alarmRefill)
                fits &= PlaceFrom(alarm, trial, edge, innermost, inward: false);
            foreach (PresentationPosition normal in normalRefill)
                fits &= PlaceFrom(normal, trial, edge, innermost, inward: true);
            for (int depth = 0; depth <= innermost && fits; depth++)
                fits = trial.Lists[edge, depth].Count > 0;
            if (!fits)
                continue;

            for (int depth = 0; depth <= innermost; depth++)
            {
                page.Lists[edge, depth].Clear();
                page.Lists[edge, depth].AddRange(trial.Lists[edge, depth]);
                page.Used[edge, depth] = trial.Used[edge, depth];
            }
        }
    }

    /// <summary>
    /// Allocates on one side only: outward from the innermost rail, or inward
    /// from the outermost one up to it.
    /// </summary>
    private bool PlaceFrom(PresentationPosition candidate, PageAllocation page, int edge, int innermost, bool inward)
    {
        for (int i = 0; i <= innermost; i++)
        {
            if (TryAllocate(candidate, page, edge, inward ? i : innermost - i))
                return true;
        }
        return false;
    }

    private static bool AlarmsAlreadyInnermost(PageAllocation page, int edge, int innermost)
    {
        for (int depth = 0; depth < innermost; depth++)
        {
            foreach (PresentationPosition candidate in page.Lists[edge, depth])
            {
                if (IsAlarm(candidate))
                    return false;
            }
        }
        return true;
    }

    private static bool IsAlarm(PresentationPosition candidate) =>
        candidate.Presentation.Source != null && AlarmRank(candidate.Presentation.Source) > 0;

    /// <summary>
    /// The sensor's own side first, then the other side, one depth at a time,
    /// so inner rails only fill once both outer rails are full and the model
    /// keeps as much room as it can.
    /// </summary>
    private bool TryAllocate(PresentationPosition candidate, PageAllocation page)
    {
        int preferred = PreferredEdge(candidate);
        for (int depth = 0; depth < railCount; depth++)
        {
            if (TryAllocate(candidate, page, preferred, depth) ||
                TryAllocate(candidate, page, 1 - preferred, depth))
                return true;
        }
        return false;
    }

    private bool TryAllocate(PresentationPosition candidate, PageAllocation page, int edge, int depth)
    {
        List<PresentationPosition> list = page.Lists[edge, depth];
        Rail rail = rails[edge][depth];
        if (list.Count >= maximumCardsPerRail)
            return false;

        float extent = EstimateExtent(candidate.Presentation, rail);
        float required = extent + (list.Count > 0 ? Gap : 0f);
        if (page.Used[edge, depth] + required > AvailableLength(rail.Container) - ConnectorRoom(rail, depth))
            return false;

        page.Used[edge, depth] += required;
        list.Add(candidate);
        page.Count++;
        return true;
    }

    private int PreferredEdge(PresentationPosition candidate)
    {
        TelemetryCardPresentation presentation = candidate.Presentation;
        if (TryGetFixedEdge(presentation, rows, out int fixedEdge))
            return fixedEdge;

        // Across the screen from the first edge: left to right, or top to bottom.
        float across = rows ? 1f - candidate.ViewportY : candidate.ViewportX;
        if (previousRows == rows && presentation.Source != null &&
            previousEdges.TryGetValue(presentation.Source, out int previous))
        {
            if (previous == First && across < 0.5f + EdgeHysteresis)
                return First;
            if (previous == Second && across > 0.5f - EdgeHysteresis)
                return Second;
        }
        return across <= 0.5f ? First : Second;
    }

    public void ShowPreviousPage() => ShowPage(CurrentPageIndex - 1);
    public void ShowNextPage() => ShowPage(CurrentPageIndex + 1);
    public void ShowFirstPage() => ShowPage(0);

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

    // -----------------------------------------------------------------------
    // Cards
    // -----------------------------------------------------------------------

    private float Gap => rows ? rowGap : columnGap;

    /// <summary>A card's length along its rail: its height in a column, its width in a row.</summary>
    private float EstimateExtent(TelemetryCardPresentation presentation, Rail rail)
    {
        if (rows)
            return RowCardWidthFor(rail.Container);
        float width = rail.Thickness > 1f ? rail.Thickness : ColumnWidth(ContentArea);
        return cardPrefab.EstimateRequestedCardHeight(presentation, SideOf(rail.Edge), cardHeight, width);
    }

    /// <summary>The row card width, narrowed to the row so one card always fits.</summary>
    private float RowCardWidthFor(RectTransform container)
    {
        float length = AvailableLength(container);
        return length > 1f ? Mathf.Min(rowCardWidth, length) : rowCardWidth;
    }

    /// <summary>
    /// Room an inner rail keeps free, about one card's worth, while detail
    /// panels have their rail: the cards on it can then always step aside for
    /// a connector from an outer card to its panel. The outermost rail keeps
    /// none, so it holds as many cards as fit and the inner ones one fewer.
    /// </summary>
    private float ConnectorRoom(Rail rail, int depth)
    {
        if (!detailRail || depth == 0)
            return 0f;
        float nominal = rows ? RowCardWidthFor(rail.Container) : cardHeight;
        return nominal + Gap + ConnectorWidth;
    }

    /// <summary>The stretch of a rail a connector keeps clear: the line and the clearance on both sides.</summary>
    private float ConnectorWidth => connectorClearance * 2f + 1f;

    /// <summary>The length of a rail cards can occupy, less its padding at each end.</summary>
    private float AvailableLength(RectTransform container)
    {
        if (container == null)
            return 0f;
        Rect rect = container.rect;
        float length = rows ? rect.width : rect.height;
        if (length <= 1f && container.parent is RectTransform rail)
            length = rows ? rail.rect.width : rail.rect.height;
        if (length <= 1f)
            length = ContentLength();
        return Mathf.Max(0f, length - (rows ? rowStartPadding + rowEndPadding : columnStartPadding + columnEndPadding));
    }

    private void BuildRail(Rail rail, List<PresentationPosition> railSources)
    {
        rail.Bindings.Clear();

        // Top to bottom in a column, left to right in a row: the order the
        // cards keep along the rail.
        if (rows)
            railSources.Sort((a, b) => a.ViewportX.CompareTo(b.ViewportX));
        else
            railSources.Sort((a, b) => b.ViewportY.CompareTo(a.ViewportY));

        StatRailSide side = SideOf(rail.Edge);
        foreach (PresentationPosition positioned in railSources)
        {
            TelemetryCardPresentation presentation = positioned.Presentation;
            carriedCards.TryGetValue(presentation.Source, out CarriedCard carried);
            if (carried.Card != null)
                carriedCards.Remove(presentation.Source);
            PerformanceStatCardView card = carried.Card != null ? carried.Card : AcquireCard();
            card.transform.SetParent(rail.Container, false);
            if (!card.gameObject.activeSelf)
                card.gameObject.SetActive(true);
            RectTransform cardRect = (RectTransform)card.transform;

            card.SetPresentation(presentation, side);
            float extent;
            if (rows)
            {
                extent = RowCardWidthFor(rail.Container);
                float height = card.EstimateRequestedCardHeight(presentation, side, cardHeight, extent);
                ConfigureRowCard(cardRect, extent, height);
            }
            else
            {
                extent = card.EstimateRequestedCardHeight(presentation, side, cardHeight, rail.Thickness);
                ConfigureColumnCard(cardRect, extent);
            }

            RailCardBinding binding = new(presentation, card, cardRect, extent);
            // The same card on the same rail glides on from where it was.
            if (carried.Card != null && carried.Container == rail.Container && carried.Rows == rows)
            {
                binding.HasCarriedPosition = true;
                binding.CarriedAlong = carried.Along;
                binding.CurrentVelocity = carried.Velocity;
            }
            rail.Bindings.Add(binding);
            activeCards[presentation.Source] = card;
        }

        // A rebuild orders the cards by their sensors again. Cards already on
        // screen glide to their new places, passing each other if they must,
        // rather than being pushed there in one frame.
        if (rail.Bindings.Exists(binding => binding.HasCarriedPosition))
            rail.GlideUntil = Time.unscaledTime + ReorderGlideSeconds;
    }

    private static void ConfigureColumnCard(RectTransform cardRect, float height)
    {
        // Stretch across the rail at a fixed height.
        cardRect.anchorMin = new Vector2(0f, 0.5f);
        cardRect.anchorMax = new Vector2(1f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.offsetMin = new Vector2(0f, -height * 0.5f);
        cardRect.offsetMax = new Vector2(0f, height * 0.5f);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.localScale = Vector3.one;
        cardRect.localRotation = Quaternion.identity;
    }

    private static void ConfigureRowCard(RectTransform cardRect, float width, float height)
    {
        cardRect.anchorMin = new Vector2(0.5f, 0.5f);
        cardRect.anchorMax = new Vector2(0.5f, 0.5f);
        cardRect.pivot = new Vector2(0.5f, 0.5f);
        cardRect.sizeDelta = new Vector2(width, height);
        cardRect.anchoredPosition = Vector2.zero;
        cardRect.localScale = Vector3.one;
        cardRect.localRotation = Quaternion.identity;
    }

    /// <summary>
    /// Resizes cards whose content asks for a different height. A column card
    /// takes its rail's width; a row card keeps the width it was built at.
    /// True when any card changed.
    /// </summary>
    private bool ReconcileCardHeights(Rail rail)
    {
        bool changed = false;
        foreach (RailCardBinding binding in rail.Bindings)
        {
            if (binding.Card == null || binding.RectTransform == null)
                continue;

            float width = rows ? binding.RectTransform.sizeDelta.x : rail.Thickness;
            float current = rows ? binding.RectTransform.sizeDelta.y : binding.Extent;
            float resolved = binding.Card.EstimateRequestedCardHeight(
                binding.Presentation, binding.Card.RailSide, cardHeight, width);
            if (Mathf.Abs(resolved - current) < 0.5f)
                continue;

            if (!rows)
                binding.Extent = resolved;
            binding.RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, resolved);
            changed = true;
        }
        return changed;
    }

    // -----------------------------------------------------------------------
    // Continuity between layouts
    // -----------------------------------------------------------------------

    /// <summary>Takes every card off the rails, remembering where each one was.</summary>
    private void CarryCurrentLayout(bool wasRows)
    {
        carriedCards.Clear();
        previousEdges.Clear();
        previousRows = wasRows;

        for (int edge = 0; edge < 2; edge++)
        {
            foreach (Rail rail in rails[edge])
            {
                foreach (RailCardBinding binding in rail.Bindings)
                {
                    PerformanceStatSource source = binding.Presentation?.Source;
                    if (binding.Card == null)
                        continue;
                    if (source == null || carriedCards.ContainsKey(source))
                    {
                        PoolCard(binding.Card);
                        continue;
                    }
                    carriedCards[source] = new CarriedCard(
                        binding.Card, rail.Container, wasRows, GetCardAlong(binding, wasRows), binding.CurrentVelocity);
                    previousEdges[source] = edge;
                }
                rail.Bindings.Clear();
            }
        }
        activeCards.Clear();
    }

    /// <summary>Cards whose sensors left the screen go back to the pool.</summary>
    private void ReleaseCarriedCards()
    {
        foreach (CarriedCard carried in carriedCards.Values)
            PoolCard(carried.Card);
        carriedCards.Clear();
    }

    private void PoolCard(PerformanceStatCardView card)
    {
        if (card == null)
            return;
        card.gameObject.SetActive(false);
        cardPool.Push(card);
    }

    private PerformanceStatCardView AcquireCard()
    {
        PerformanceStatCardView card = null;
        while (card == null && cardPool.Count > 0)
            card = cardPool.Pop();
        return card != null ? card : Instantiate(cardPrefab, rails[First][0].Container);
    }

    // -----------------------------------------------------------------------
    // Motion
    // -----------------------------------------------------------------------

    private void InitialisePositions(Rail rail)
    {
        if (rail.Bindings.Count == 0)
            return;

        CalculateResolvedTargets(rail);
        bool carried = false;
        foreach (RailCardBinding binding in rail.Bindings)
        {
            if (binding.HasCarriedPosition)
            {
                SetCardAlong(binding, binding.CarriedAlong);
                carried = true;
            }
            else
            {
                SetCardAlong(binding, binding.Target);
                binding.CurrentVelocity = 0f;
            }
        }
        // Carried cards glide from where they were (see UpdateRail); a rail
        // with none starts settled.
        if (!carried)
            ResolveCurrentPositionCollisions(rail);
    }

    private void UpdateRail(Rail rail)
    {
        if (rail.Bindings.Count == 0 || worldCamera == null)
            return;

        CalculateResolvedTargets(rail);
        foreach (RailCardBinding binding in rail.Bindings)
        {
            if (binding.RectTransform == null)
                continue;
            SetCardAlong(binding, Mathf.SmoothDamp(
                GetCardAlong(binding, rows), binding.Target, ref binding.CurrentVelocity,
                smoothTime, maximumSpeed, Time.unscaledDeltaTime));
        }

        // Separation holds during the animation too, except while the cards
        // glide into a rebuilt order and may have to pass each other.
        if (Time.unscaledTime >= rail.GlideUntil)
            ResolveCurrentPositionCollisions(rail);
    }

    private void CalculateResolvedTargets(Rail rail)
    {
        List<RailCardBinding> bindings = rail.Bindings;
        RectTransform container = rail.Container;

        // Each sensor is projected once a frame.
        for (int i = 0; i < bindings.Count; i++)
            bindings[i].Desired = GetDesiredAlong(bindings[i].Presentation, container);

        if (!preserveCardOrder)
            bindings.Sort(FirstAlongRail);

        // The spread works in increasing positions; along a rail the first
        // card has the highest, so positions are negated on the way in and out.
        int count = bindings.Count;
        RailCardSpread.EnsureBuffer(ref spreadDesired, count);
        RailCardSpread.EnsureBuffer(ref spreadExtents, count);
        RailCardSpread.EnsureBuffer(ref spreadTargets, count);
        for (int i = 0; i < count; i++)
        {
            spreadDesired[i] = -bindings[i].Desired;
            spreadExtents[i] = bindings[i].Extent;
        }

        int keepOuts = CollectConnectorKeepOuts(rail);
        RailCardSpread.Solve(
            spreadDesired, spreadExtents, count, Gap,
            -RailStart(container), -RailEnd(container),
            keepOutStarts, keepOutEnds, keepOuts, rail.Split, spreadTargets);

        for (int i = 0; i < count; i++)
            bindings[i].Target = -spreadTargets[i];
    }

    /// <summary>
    /// Where connectors cross this rail: one from each card on a rail outside
    /// it, on the same side, whose detail panel is open beside the rails. In
    /// the spread's positions (the negated distance along the rail). Returns
    /// how many there are.
    /// </summary>
    private int CollectConnectorKeepOuts(Rail rail)
    {
        if (!detailRail)
            return 0;
        List<Rail> edgeRails = rails[rail.Edge];
        int depth = edgeRails.IndexOf(rail);
        if (depth <= 0)
            return 0;

        RectTransform container = rail.Container;
        Rect rect = container.rect;
        float crossMin = rows ? rect.yMin : rect.xMin;
        float crossMax = rows ? rect.yMax : rect.xMax;
        Camera uiCamera = GetUICamera();
        int count = 0;

        for (int outer = 0; outer < depth; outer++)
        {
            Rail outerRail = edgeRails[outer];
            if (!outerRail.Shown)
                continue;
            foreach (RailCardBinding binding in outerRail.Bindings)
            {
                PerformanceStatCardView card = binding.Card;
                if (card == null || !card.isActiveAndEnabled ||
                    !CardFocus.TryGetDetailLink(card, out ICardDetailLink link) ||
                    link.CardSidePoint == null || card.OwnConnectionPoint == null)
                    continue;

                ConnectorPoints[0] = card.OwnConnectionPoint.position;
                ConnectorPoints[1] = link.CardSidePoint.position;
                if (!TryGetLocal(container, ConnectorPoints[0], uiCamera, out Vector2 from) ||
                    !TryGetLocal(container, ConnectorPoints[1], uiCamera, out Vector2 to))
                    continue;

                // The connector runs across the rail; where it is not square
                // to it (a panel pushed along by another), it sweeps a stretch.
                float alongFrom = rows ? -from.x : from.y;
                float alongTo = rows ? -to.x : to.y;
                float crossFrom = rows ? from.y : from.x;
                float crossTo = rows ? to.y : to.x;
                float a = alongFrom;
                float b = alongTo;
                if (!Mathf.Approximately(crossFrom, crossTo))
                {
                    float t0 = Mathf.Clamp01((crossMin - crossFrom) / (crossTo - crossFrom));
                    float t1 = Mathf.Clamp01((crossMax - crossFrom) / (crossTo - crossFrom));
                    a = Mathf.Lerp(alongFrom, alongTo, t0);
                    b = Mathf.Lerp(alongFrom, alongTo, t1);
                }

                if (count == keepOutStarts.Length)
                {
                    System.Array.Resize(ref keepOutStarts, count * 2);
                    System.Array.Resize(ref keepOutEnds, count * 2);
                }
                float half = ConnectorWidth * 0.5f;
                keepOutStarts[count] = -Mathf.Max(a, b) - half;
                keepOutEnds[count] = -Mathf.Min(a, b) + half;
                count++;
            }
        }
        return count;
    }

    private static bool TryGetLocal(RectTransform container, Vector3 worldPoint, Camera uiCamera, out Vector2 local) =>
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            container, RectTransformUtility.WorldToScreenPoint(uiCamera, worldPoint), uiCamera, out local);

    private void ResolveCurrentPositionCollisions(Rail rail)
    {
        List<RailCardBinding> bindings = rail.Bindings;
        if (bindings.Count == 0)
            return;

        float upperLimit = RailStart(rail.Container) - bindings[0].Extent * 0.5f;
        float lowerLimit = RailEnd(rail.Container) + bindings[^1].Extent * 0.5f;

        SetCardAlong(bindings[0], Mathf.Min(GetCardAlong(bindings[0], rows), upperLimit));

        for (int i = 1; i < bindings.Count; i++)
        {
            float highestAllowed = GetCardAlong(bindings[i - 1], rows) -
                (bindings[i - 1].Extent + bindings[i].Extent) * 0.5f - Gap;
            if (GetCardAlong(bindings[i], rows) > highestAllowed)
            {
                SetCardAlong(bindings[i], highestAllowed);
                bindings[i].CurrentVelocity = 0f;
            }
        }

        if (GetCardAlong(bindings[^1], rows) < lowerLimit)
        {
            SetCardAlong(bindings[^1], lowerLimit);
            bindings[^1].CurrentVelocity = 0f;

            for (int i = bindings.Count - 2; i >= 0; i--)
            {
                float lowestAllowed = GetCardAlong(bindings[i + 1], rows) +
                    (bindings[i + 1].Extent + bindings[i].Extent) * 0.5f + Gap;
                if (GetCardAlong(bindings[i], rows) < lowestAllowed)
                {
                    SetCardAlong(bindings[i], lowestAllowed);
                    bindings[i].CurrentVelocity = 0f;
                }
            }
        }
    }

    // Cached so sorting a rail allocates no delegate. First along the rail:
    // topmost in a column, leftmost in a row.
    private static readonly Comparison<RailCardBinding> FirstAlongRail =
        (a, b) => b.Desired.CompareTo(a.Desired);

    /// <summary>The sensor's projected position as a distance along the rail.</summary>
    private float GetDesiredAlong(TelemetryCardPresentation presentation, RectTransform container)
    {
        if (presentation == null || presentation.WorldAnchor == null)
            return 0f;

        Vector2 screenPoint = TelemetryPresentationBuilder.PlacementScreenPoint(
            worldCamera, presentation.WorldAnchor.position);
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                container, screenPoint, GetUICamera(), out Vector2 localPoint))
            return 0f;

        return rows ? -localPoint.x : localPoint.y;
    }

    private Camera GetUICamera()
    {
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;
        return canvas.worldCamera != null ? canvas.worldCamera : worldCamera;
    }

    // Positions along a rail run from its first card to its last: down a
    // column, so y as it is, and across a row from the left, so x negated.
    // The ordering and collision passes then read the same for both.

    private float RailStart(RectTransform container) => rows
        ? -(container.rect.xMin + rowStartPadding)
        : container.rect.yMax - columnStartPadding;

    private float RailEnd(RectTransform container) => rows
        ? -(container.rect.xMax - rowEndPadding)
        : container.rect.yMin + columnEndPadding;

    private static float GetCardAlong(RailCardBinding binding, bool rowsLayout)
    {
        if (binding.RectTransform == null)
            return 0f;
        Vector2 position = binding.RectTransform.anchoredPosition;
        return rowsLayout ? -position.x : position.y;
    }

    private void SetCardAlong(RailCardBinding binding, float along)
    {
        if (binding.RectTransform == null)
            return;
        Vector2 position = binding.RectTransform.anchoredPosition;
        if (rows)
            position.x = -along;
        else
            position.y = along;
        binding.RectTransform.anchoredPosition = position;
    }

    // -----------------------------------------------------------------------
    // Sources
    // -----------------------------------------------------------------------

    private void HandleSourceChanged(PerformanceStatSource source)
    {
        if (source.IsVisible && activeCards.TryGetValue(source, out PerformanceStatCardView card))
            card.RefreshBoundSources();
    }

    private void HandleLayoutPriorityChanged(PerformanceStatSource source)
    {
        // A newly raised or worsened alarm returns to the first page, where
        // alarms sort. An alarm clearing reorders the cards but leaves the
        // viewer on the page they are reading.
        if (IsNewOrWorseAlarm(source))
            CurrentPageIndex = 0;
        // Several sensors can change alarm state in one batch; rebuilding once
        // per frame instead of once per sensor gives the same layout.
        rebuildPending = layoutBuilt;
    }

    private bool IsNewOrWorseAlarm(PerformanceStatSource source)
    {
        bool alarm = source.VisualState is StatVisualState.Warning or StatVisualState.Critical;
        int previous = subscribedAlarmRanks.TryGetValue(source, out int rank) ? rank : 0;
        return alarm && AlarmRank(source) > previous;
    }

    private static int AlarmRank(PerformanceStatSource source) => source.VisualState switch
    {
        StatVisualState.Critical => 2,
        StatVisualState.Warning => 1,
        _ => 0
    };

    private void SubscribeToSources()
    {
        if (sources == null)
            return;
        foreach (PerformanceStatSource source in sources)
        {
            if (source == null)
                continue;
            source.Changed += HandleSourceChanged;
            source.LayoutPriorityChanged += HandleLayoutPriorityChanged;
            subscribedAlarmRanks[source] = AlarmRank(source);
        }
    }

    private void UnsubscribeFromSources()
    {
        if (sources == null)
            return;
        foreach (PerformanceStatSource source in sources)
        {
            if (source == null)
                continue;
            source.Changed -= HandleSourceChanged;
            source.LayoutPriorityChanged -= HandleLayoutPriorityChanged;
        }
    }

    // -----------------------------------------------------------------------
    // Types
    // -----------------------------------------------------------------------

    private sealed class Rail
    {
        public readonly RectTransform Root;
        public readonly RectTransform Container;
        public readonly int Edge;
        public readonly List<RailCardBinding> Bindings = new();
        /// <summary>How many of its cards sit before each connector crossing it, kept so they stay on their side.</summary>
        public readonly List<int> Split = new();
        /// <summary>Its size across the screen: a column's width, a row's height.</summary>
        public float Thickness;
        public bool Shown;
        /// <summary>Until then, cards glide into a rebuilt order without being pushed apart.</summary>
        public float GlideUntil;

        public Rail(RectTransform root, RectTransform container, int edge)
        {
            Root = root;
            Container = container;
            Edge = edge;
        }
    }

    private sealed class RailCardBinding
    {
        public readonly TelemetryCardPresentation Presentation;
        public readonly PerformanceStatCardView Card;
        public readonly RectTransform RectTransform;
        /// <summary>The card's length along its rail: its height in a column, its width in a row.</summary>
        public float Extent;
        /// <summary>Where the card is heading, as a distance along the rail.</summary>
        public float Target;
        public float CurrentVelocity;
        /// <summary>The sensor's projected rail position this frame.</summary>
        public float Desired;
        public bool HasCarriedPosition;
        public float CarriedAlong;

        public RailCardBinding(
            TelemetryCardPresentation presentation,
            PerformanceStatCardView card,
            RectTransform rectTransform,
            float extent)
        {
            Presentation = presentation;
            Card = card;
            RectTransform = rectTransform;
            Extent = extent;
        }
    }

    private readonly struct CarriedCard
    {
        public readonly PerformanceStatCardView Card;
        public readonly RectTransform Container;
        public readonly bool Rows;
        public readonly float Along;
        public readonly float Velocity;

        public CarriedCard(PerformanceStatCardView card, RectTransform container, bool rowsLayout, float along, float velocity)
        {
            Card = card;
            Container = container;
            Rows = rowsLayout;
            Along = along;
            Velocity = velocity;
        }
    }

    private sealed class PageAllocation
    {
        public readonly List<PresentationPosition>[,] Lists;
        public readonly float[,] Used;
        public int Count;

        public PageAllocation(int railsPerSide)
        {
            Lists = new List<PresentationPosition>[2, railsPerSide];
            Used = new float[2, railsPerSide];
            for (int edge = 0; edge < 2; edge++)
            {
                for (int depth = 0; depth < railsPerSide; depth++)
                    Lists[edge, depth] = new List<PresentationPosition>();
            }
        }
    }

    private readonly struct PresentationPosition
    {
        public readonly TelemetryCardPresentation Presentation;
        public readonly float ViewportX;
        public readonly float ViewportY;

        public PresentationPosition(TelemetryCardPresentation presentation, float viewportX, float viewportY)
        {
            Presentation = presentation;
            ViewportX = viewportX;
            ViewportY = viewportY;
        }
    }
}
