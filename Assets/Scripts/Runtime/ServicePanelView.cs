using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The service panel: a component card for every part's service position,
/// sorted by hours left, in a scrolling list between a header and a legend for
/// the bars. Parts due soon or overdue are marked in their state colour; parts
/// on track stay quiet.
///
/// Built at runtime from the service schedule, like the detail panel's graphs,
/// so adding a plan to the schedule adds a card with no other authoring. The
/// header and legend line up with the viewport's top and bottom bars when the
/// panel runs the full height beside it (see <see cref="SetBands"/>).
/// Clicking a card opens that part's detail in place of the list (see
/// ServicePanelView.Detail.cs); a chevron on every card says so. A name too
/// long for its card slides back and forth rather than being cut short.
///
/// Room for the scrollbar is only kept while the content scrolls; when every
/// card fits, the cards take the full width.
///
/// The portrait layout shows a second panel over the whole screen (see
/// PortraitServiceView): <see cref="fullScreen"/> drops its frame and
/// stacks the detail's notes so they keep their width on a phone.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class ServicePanelView : MonoBehaviour
{
    [SerializeField] private TelemetryServiceSchedule schedule;
    [SerializeField] private TMP_FontAsset labelFont;
    [SerializeField] private TMP_FontAsset valueFont;

    [Tooltip("Draws the run bars with a pulse travelling into their leading end. Empty: plain bars.")]
    [SerializeField] private Material runBarMaterial;

    [Tooltip("Seconds between refreshes of the hours shown.")]
    [SerializeField, Min(0.1f)] private float refreshInterval = 1f;

    [Tooltip("The whole screen on a phone held upright: no frame, and the detail's likely " +
             "cause and suggested fix stacked rather than side by side.")]
    [SerializeField] private bool fullScreen;

    [Tooltip("Space between the panel's edges and the header's text.")]
    [SerializeField, Min(0f)] private float inset = 28f;

    [Header("Colours")]
    [SerializeField] private Color ink = new(0.918f, 0.992f, 1f, 1f);
    // The accent the bottom controls use for their selected text.
    [SerializeField] private Color label = new(0f, 0.922f, 1f, 1f);
    [SerializeField] private Color normal = new(0f, 0.922f, 1f, 1f);
    [SerializeField] private Color warning = new(1f, 0.70f, 0.10f, 1f);
    [SerializeField] private Color critical = new(1f, 0.22f, 0.20f, 1f);

    [Tooltip("The panel's fill without the design system (no theme). Near opaque, so the model " +
             "does not show through the list.")]
    [SerializeField] private Color backdrop = new(0.031f, 0.086f, 0.118f, 0.96f);

    [Tooltip("Every card's fill without the design system: a solid step lighter than the panel.")]
    [SerializeField] private Color card = new(0.063f, 0.122f, 0.153f, 1f);

    private const float CardHeight = 104f;
    private const float CardGap = 12f;
    private const float CardPadding = 22f;
    // The cards' margins in a scroll view; the right one widens to clear the
    // scrollbar while the content scrolls.
    private const float ListLeft = 16f;
    private const float ListRight = 38f;
    private const float HoursWidth = 170f;
    // The chevron's column at the card's right edge, and its size.
    private const float ChevronSpace = 30f;
    private const float ChevronSize = 22f;
    private const float CompactHeaderHeight = 72f;
    private const float CompactLegendHeight = 64f;
    private const float ScrollHitWidth = 24f;
    private const float ScrollTrackWidth = 2f;
    private const float ScrollThumbWidth = 6f;
    // Neighbouring bars' pulses are this far apart in their cycle.
    private const float PulseStagger = 0.13f;

    private readonly List<ServiceItemStatus> statusBuffer = new();
    private readonly List<ServiceItemStatus> items = new();
    private readonly List<ComponentCard> cards = new();

    // The panel's surface: the Sheet component, its off-screen edges pushed past the screen.
    private RectTransform sheet;
    private Graphic sheetSurface;
    private bool frameShown = true;
    private float frameAlpha = 1f;
    private RectTransform header;
    private RectTransform headerText;
    private TextMeshProUGUI runHoursText;
    private ScrollView listView;
    private RectTransform legend;
    private RectTransform legendKeys;
    private bool built;
    private float nextRefresh;
    private long shownRunHours = -1;
    private MachinePartGroup hoveredPart;
    private Vector3 appliedBands = new(-1f, -1f, -1f);

    /// <summary>Every part's service position, soonest first.</summary>
    public IReadOnlyList<ServiceItemStatus> Items => items;

    /// <summary>The part whose card the pointer is over, if any.</summary>
    public MachinePartGroup HoveredPart => hoveredPart;

    /// <summary>The list's scroll view.</summary>
    public ScrollRect List => listView?.Scroll;

    /// <summary>The list's scrollbar, shown while the cards do not all fit.</summary>
    public Scrollbar Scrollbar => listView?.Bar;

    /// <summary>Space right of the list's cards: wider while the list scrolls.</summary>
    public float ListRightMargin => listView != null ? listView.Stack.padding.right : 0f;

    public event Action<MachinePartGroup> HoveredPartChanged;

    /// <summary>Raised when a card is clicked and its part's detail opens.</summary>
    public event Action<MachinePartGroup> PartOpened;

    private Color Muted => new(ink.r, ink.g, ink.b, 0.62f);

    public Color StateColor(ServiceState state) => state switch
    {
        ServiceState.Overdue => critical,
        ServiceState.DueSoon => warning,
        _ => normal
    };

    /// <summary>The number shown on the part's card and pin, 1 for the soonest.</summary>
    public bool TryGetNumber(MachinePartGroup part, out int number)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Part == part)
            {
                number = i + 1;
                return true;
            }
        }
        number = 0;
        return false;
    }

    /// <summary>
    /// Sizes the header and legend. Beside the viewport they take the heights
    /// of its top and bottom bars, <paramref name="top"/> and
    /// <paramref name="bottom"/>, with their content centred on the bars'
    /// controls, which sit <paramref name="edgeMargin"/> in from the screen's
    /// edges. Zero heights give a compact header and legend, for the panel
    /// sitting over the content area.
    /// </summary>
    public void SetBands(float top, float bottom, float edgeMargin)
    {
        Build();
        bool beside = top > 0f && bottom > 0f;
        float margin = beside ? edgeMargin : 0f;
        top = beside ? top : CompactHeaderHeight;
        bottom = beside ? bottom : CompactLegendHeight;

        Vector3 bands = new(top, bottom, beside ? margin : -1f);
        if (bands == appliedBands)
            return;
        appliedBands = bands;

        header.sizeDelta = new Vector2(0f, top);
        headerText.offsetMax = new Vector2(headerText.offsetMax.x, -margin);
        legend.sizeDelta = new Vector2(0f, bottom);
        legendKeys.offsetMin = new Vector2(legendKeys.offsetMin.x, margin);
        foreach (ScrollView view in ScrollViews())
        {
            view.Root.offsetMin = new Vector2(0f, bottom);
            view.Root.offsetMax = new Vector2(0f, -top);
        }

        // Beside the viewport only the left edge is drawn: the others fall on
        // the screen's edges. Over the content area the panel is framed; full
        // screen, every edge is the screen's.
        float outside = beside || fullScreen ? 2f : 0f;
        sheet.offsetMin = new Vector2(fullScreen ? -outside : 0f, -outside);
        sheet.offsetMax = new Vector2(outside, outside);
    }

    private IEnumerable<ScrollView> ScrollViews()
    {
        yield return listView;
        yield return detailView;
    }

    private void Awake() => Build();

    /// <summary>
    /// Draws the panel's own sheet, its fill and edges, or not: the portrait
    /// view sets the panel on an opaque fill of its own, where an edge would
    /// only draw a second line under the switch. Hidden, it still catches the
    /// pointer, so nothing behind the panel answers.
    /// </summary>
    public void SetFrameShown(bool shown)
    {
        frameShown = shown;
        if (sheetSurface == null)
            return;
        Color color = sheetSurface.color;
        color.a = shown ? frameAlpha : 0f;
        sheetSurface.color = color;
    }

    private void OnEnable()
    {
        nextRefresh = 0f;
        Refresh();
        UIText.Changed -= HandleLanguageChanged;
        UIText.Changed += HandleLanguageChanged;
    }

    private void OnDisable()
    {
        SetHovered(null);
        UIText.Changed -= HandleLanguageChanged;
    }

    private void HandleLanguageChanged()
    {
        // The run hours only redraw when they tick over; force them.
        shownRunHours = long.MinValue;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh)
            return;
        Refresh();
    }

    // After layout has settled for the frame's changes: the gutter follows
    // whether the view on show needs to scroll.
    private void LateUpdate()
    {
        if (!built)
            return;

        ScrollView shown = DetailOpen ? detailView : listView;
        UpdateGutter(shown);

        // The header's text and the legend line up with the cards.
        float right = shown.Stack.padding.right;
        float headerRight = right + (inset - ListLeft);
        if (!Mathf.Approximately(headerText.offsetMax.x, -headerRight))
            headerText.offsetMax = new Vector2(-headerRight, headerText.offsetMax.y);
        if (!Mathf.Approximately(legendKeys.offsetMax.x, -right))
            legendKeys.offsetMax = new Vector2(-right, legendKeys.offsetMax.y);
    }

    private static void UpdateGutter(ScrollView view)
    {
        if (!view.Root.gameObject.activeInHierarchy)
            return;
        int right = Mathf.RoundToInt(view.NeedsScroll ? ListRight : ListLeft);
        RectOffset padding = view.Stack.padding;
        if (padding.right != right)
            view.Stack.padding = new RectOffset(padding.left, right, padding.top, padding.bottom);
    }

    public void Refresh()
    {
        Build();
        nextRefresh = Time.unscaledTime + refreshInterval;

        items.Clear();
        if (schedule != null)
        {
            schedule.GetStatuses(statusBuffer);
            items.AddRange(statusBuffer);
            items.Sort(SoonestFirst);
        }

        long run = schedule != null ? (long)Math.Floor(schedule.MachineRunHours) : 0;
        if (run != shownRunHours)
        {
            shownRunHours = run;
            runHoursText.text = UIText.Format("service.run_hours", "{0} H RUN", run.ToString("#,0", CultureInfo.InvariantCulture));
        }

        RefreshCards();
        RefreshDetail();
    }

    private static int SoonestFirst(ServiceItemStatus a, ServiceItemStatus b) =>
        a.HoursLeft.CompareTo(b.HoursLeft);

    // -----------------------------------------------------------------------
    // Component cards
    // -----------------------------------------------------------------------

    private void RefreshCards()
    {
        while (cards.Count < items.Count)
            cards.Add(CreateCard(cards.Count));

        for (int i = 0; i < cards.Count; i++)
        {
            bool shown = i < items.Count;
            cards[i].Root.gameObject.SetActive(shown);
            if (shown)
                BindCard(cards[i], items[i], i + 1);
        }
    }

    private void BindCard(ComponentCard card, in ServiceItemStatus status, int number)
    {
        bool alert = status.State != ServiceState.OnTrack;
        Color state = StateColor(status.State);
        card.Hover.Part = status.Part;
        card.State.Tone = ToneOf(status.State);
        card.Number.color = state;
        SetText(card.Number, number.ToString(CultureInfo.InvariantCulture));
        SetText(card.Name, DisplayName(status.Part));
        SetText(card.Task, TaskLabel(status.Task));
        card.Hours.color = alert ? state : ink;
        SetText(card.Hours, HoursText(status.HoursLeft));
        FitNameToHours(card);
        card.Bar.SetValues(
            (float)status.RunHours,
            (float)status.PlanHours,
            (float)status.EstimateHours,
            state);
    }

    /// <summary>
    /// Gives the name and task every unit the hours leave: short hours, a
    /// longer name before it has to slide.
    /// </summary>
    private static void FitNameToHours(ComponentCard card)
    {
        float hours = Mathf.Min(HoursWidth, Mathf.Ceil(card.Hours.GetPreferredValues(card.Hours.text).x));
        float right = hours + ChevronSpace + 16f;
        SetRight(card.NameArea, right);
        SetRight(card.Task.rectTransform, right);
    }

    private static void SetRight(RectTransform rect, float right)
    {
        if (!Mathf.Approximately(rect.offsetMax.x, -right))
            rect.offsetMax = new Vector2(-right, rect.offsetMax.y);
    }

    // -----------------------------------------------------------------------
    // Pointer
    // -----------------------------------------------------------------------

    private void HandleClicked(ServiceHoverTarget target)
    {
        ShowDetail(target.Part);
        if (DetailOpen)
            PartOpened?.Invoke(selectedPart);
    }

    private void HandleHover(ServiceHoverTarget target, bool hovered)
    {
        if (hovered)
            SetHovered(target.Part);
        else if (hoveredPart == target.Part)
            SetHovered(null);
    }

    private void SetHovered(MachinePartGroup part)
    {
        if (hoveredPart == part)
            return;
        hoveredPart = part;
        HoveredPartChanged?.Invoke(part);
    }

    // -----------------------------------------------------------------------
    // Text
    // -----------------------------------------------------------------------

    private string HoursText(double hoursLeft)
    {
        string suffix = "<size=55%><color=#" + ColorUtility.ToHtmlStringRGBA(Muted) + "> " +
                        UIText.Get("unit.hours", "H") + "</color></size>";
        if (hoursLeft <= 0d)
            return "+" + Math.Ceiling(-hoursLeft).ToString("#,0", CultureInfo.InvariantCulture) + suffix;
        return Math.Ceiling(hoursLeft).ToString("#,0", CultureInfo.InvariantCulture) + suffix;
    }

    private static string DisplayName(MachinePartGroup part) =>
        part != null ? PartName(part).ToUpperInvariant() : string.Empty;

    /// <summary>The part's name from the language table (part.bearings), or its authored name.</summary>
    public static string PartName(MachinePartGroup part) =>
        part != null ? UIText.Get(PartKey(part), part.DisplayName) : string.Empty;

    /// <summary>Language table key of a part: part. and a slug of its authored name.</summary>
    public static string PartKey(MachinePartGroup part) =>
        part != null ? "part." + UIText.Slug(part.DisplayName) : string.Empty;

    public static string TaskLabel(ServiceTask task) => task switch
    {
        ServiceTask.Regrease => UIText.Get("task.regrease", "REGREASE"),
        ServiceTask.OilChange => UIText.Get("task.oil_change", "OIL CHANGE"),
        ServiceTask.Replace => UIText.Get("task.replace", "REPLACE"),
        _ => UIText.Get("task.inspect", "INSPECT")
    };

    private static void SetText(TMP_Text text, string value)
    {
        if (!string.Equals(text.text, value, StringComparison.Ordinal))
            text.text = value;
    }

    // -----------------------------------------------------------------------
    // Building
    // -----------------------------------------------------------------------

    /// <summary>One part's card in the list: number, name, task, hours left and bar.</summary>
    private sealed class ComponentCard
    {
        public RectTransform Root;
        public ServiceHoverTarget Hover;
        public UIControlState State;
        public TextMeshProUGUI Number;
        public RectTransform NameArea;
        public TextMeshProUGUI Name;
        public TextMeshProUGUI Task;
        public TextMeshProUGUI Hours;
        public UIIconGraphic Chevron;
        public UIServiceBarGraphic Bar;
    }

    /// <summary>A vertical stack of cards that scrolls, with the panel's scrollbar.</summary>
    private sealed class ScrollView
    {
        public RectTransform Root;
        public ScrollRect Scroll;
        public RectTransform Content;
        public VerticalLayoutGroup Stack;
        public Scrollbar Bar;

        public bool NeedsScroll => Content.rect.height > Scroll.viewport.rect.height + 0.5f;
    }

    private void Build()
    {
        if (built)
            return;
        built = true;

        RectTransform root = (RectTransform)transform;

        sheet = CreateRect("Backdrop", root);
        Stretch(sheet, 0f, 0f, 0f, 0f);
        // Catches the pointer over the whole panel, so the wheel and drags
        // here never reach the model behind it.
        sheetSurface = AddSurface(sheet, "Sheet", backdrop, true);
        frameAlpha = sheetSurface.color.a;
        SetFrameShown(frameShown);

        header = CreateRect("Header", root);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = Vector2.one;
        header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = Vector2.zero;

        headerText = CreateRect("Content", header);
        Stretch(headerText, inset, inset, 0f, 0f);

        listHeader = CreateRect("List", headerText);
        Stretch(listHeader, 0f, 0f, 0f, 0f);

        TextMeshProUGUI title = CreateText("Title", listHeader, 20f, false, label, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 6f;
        UITextBinding.Bind(title, "service.title", "SERVICE SCHEDULE");
        Stretch(title.rectTransform, 0f, 0f, 0f, 0f);

        runHoursText = CreateText("Run Hours", listHeader, 20f, false, Muted, TextAlignmentOptions.MidlineRight);
        Stretch(runHoursText.rectTransform, 0f, 0f, 0f, 0f);

        AddDivider(header, false);

        listView = BuildScrollView("Service List", root);
        BuildDetail(root, headerText);
        BuildLegend(root);
        SetBands(0f, 0f, 0f);
        ShowList();
    }

    private ScrollView BuildScrollView(string name, RectTransform root)
    {
        ScrollView view = new() { Root = CreateRect(name, root) };
        view.Root.anchorMin = Vector2.zero;
        view.Root.anchorMax = Vector2.one;
        view.Scroll = view.Root.gameObject.AddComponent<ScrollRect>();
        view.Scroll.horizontal = false;
        view.Scroll.movementType = ScrollRect.MovementType.Clamped;
        view.Scroll.inertia = true;
        // Wheel and trackpad move the list by the browser's own distance, 1:1.
        view.Root.gameObject.AddComponent<UIWheelScroll>();

        RectTransform viewport = CreateRect("Viewport", view.Root);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        // Invisible, but catches the wheel and drags between cards.
        AddFrame(viewport, Color.clear, 0f, true);
        view.Scroll.viewport = viewport;

        view.Content = CreateRect("Cards", viewport);
        view.Content.anchorMin = new Vector2(0f, 1f);
        view.Content.anchorMax = Vector2.one;
        view.Content.pivot = new Vector2(0.5f, 1f);
        view.Content.sizeDelta = Vector2.zero;
        view.Stack = view.Content.gameObject.AddComponent<VerticalLayoutGroup>();
        view.Stack.padding = new RectOffset((int)ListLeft, (int)ListLeft, (int)CardGap, (int)CardGap);
        view.Stack.spacing = CardGap;
        view.Stack.childControlWidth = true;
        view.Stack.childControlHeight = true;
        view.Stack.childForceExpandWidth = true;
        view.Stack.childForceExpandHeight = false;
        ContentSizeFitter fit = view.Content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        view.Scroll.content = view.Content;

        view.Bar = BuildScrollbar(view.Root);
        view.Scroll.verticalScrollbar = view.Bar;
        view.Scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return view;
    }

    /// <summary>
    /// A hairline track down the right edge with a thicker thumb of the same
    /// colour riding on it: its length is the share of the content in view,
    /// its place how far down it is. The thumb and track take a pointer across
    /// a wider strip than they draw, so the thin bar is still easy to drag.
    /// </summary>
    private Scrollbar BuildScrollbar(RectTransform parent)
    {
        RectTransform bar = CreateRect("Scrollbar", parent);
        bar.anchorMin = new Vector2(1f, 0f);
        bar.anchorMax = Vector2.one;
        bar.pivot = new Vector2(1f, 0.5f);
        bar.offsetMin = new Vector2(-ScrollHitWidth - 2f, 14f);
        bar.offsetMax = new Vector2(-2f, -14f);
        AddFrame(bar, Color.clear, 0f, true);

        Color line = new(normal.r, normal.g, normal.b, 0.35f);

        // The track runs along the thumb's right edge.
        float lineRight = 8f;
        RectTransform track = CreateRect("Track", bar);
        track.anchorMin = new Vector2(1f, 0f);
        track.anchorMax = Vector2.one;
        track.pivot = new Vector2(1f, 0.5f);
        track.offsetMin = new Vector2(-lineRight - ScrollTrackWidth, 0f);
        track.offsetMax = new Vector2(-lineRight, 0f);
        AddFrame(track, line, 0f, false);

        RectTransform area = CreateRect("Sliding Area", bar);
        Stretch(area, 0f, 0f, 0f, 0f);

        RectTransform handle = CreateRect("Handle", area);
        Stretch(handle, 0f, 0f, 0f, 0f);
        AddFrame(handle, Color.clear, 0f, true);

        RectTransform thumb = CreateRect("Thumb", handle);
        thumb.anchorMin = new Vector2(1f, 0f);
        thumb.anchorMax = Vector2.one;
        thumb.pivot = new Vector2(1f, 0.5f);
        thumb.offsetMin = new Vector2(-lineRight - ScrollThumbWidth, 0f);
        thumb.offsetMax = new Vector2(-lineRight, 0f);
        UIFrameGraphic thumbGraphic = AddFrame(thumb, line, 0f, false);

        Scrollbar scrollbar = bar.gameObject.AddComponent<Scrollbar>();
        // No tint, set before the graphic: the panel starts hidden and not
        // interactable, and a tint would dim the thumb for good.
        scrollbar.transition = Selectable.Transition.None;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scrollbar.handleRect = handle;
        scrollbar.targetGraphic = thumbGraphic;
        scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
        return scrollbar;
    }

    private ComponentCard CreateCard(int index)
    {
        ComponentCard card = new() { Root = CreateRect($"Component Card {index + 1}", listView.Content) };
        LayoutElement size = card.Root.gameObject.AddComponent<LayoutElement>();
        size.preferredHeight = CardHeight;
        size.minHeight = CardHeight;

        AddSurface(card.Root, "List Row", this.card, true);
        card.Hover = card.Root.gameObject.AddComponent<ServiceHoverTarget>();
        card.Hover.HoverChanged += HandleHover;
        card.Hover.Clicked += HandleClicked;
        card.State = card.Root.gameObject.AddComponent<UIControlState>();

        RectTransform inner = CreateRect("Content", card.Root);
        Stretch(inner, CardPadding, CardPadding, 0f, 0f);

        // A plain number in the state colour: easier to read than one set in a square.
        card.Number = CreateText("Number", inner, 32f, true, normal, TextAlignmentOptions.MidlineLeft);
        card.Number.overflowMode = TextOverflowModes.Overflow;
        Place(card.Number.rectTransform, 0f, -8f, 44f, 48f, left: true);

        // Names take the width left of the hours, and slide where a narrow
        // panel or a long translation leaves too little.
        card.NameArea = CreateRect("Name Area", inner);
        PlaceBeside(card.NameArea, 46f, HoursWidth + ChevronSpace + 12f, -10f, 36f);
        card.Name = CreateSlidingText("Name", card.NameArea, 24f, ink);
        card.Name.characterSpacing = 3f;

        card.Task = CreateText("Task", inner, 19f, false, Muted, TextAlignmentOptions.MidlineLeft);
        card.Task.characterSpacing = 3f;
        PlaceBeside(card.Task.rectTransform, 46f, HoursWidth + ChevronSpace + 12f, -42f, 30f);

        card.Hours = CreateText("Hours", inner, 40f, true, ink, TextAlignmentOptions.MidlineRight);
        Place(card.Hours.rectTransform, ChevronSpace, -4f, HoursWidth, 62f, left: false);

        // Centred on the hours' line: the card opens the part's detail.
        RectTransform chevron = CreateRect("Chevron", inner);
        Place(chevron, -4f, -24f, ChevronSize, ChevronSize, left: false);
        card.Chevron = chevron.gameObject.AddComponent<UIIconGraphic>();
        card.Chevron.IconShape = UIIconGraphic.Shape.Chevron;
        card.Chevron.color = Muted;
        card.Chevron.raycastTarget = false;

        RectTransform bar = CreateRect("Bar", inner);
        bar.anchorMin = Vector2.zero;
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 12f);
        bar.sizeDelta = new Vector2(0f, 22f);
        card.Bar = CreateBar(bar, index * PulseStagger);
        return card;
    }

    private UIServiceBarGraphic CreateBar(RectTransform rect, float pulsePhase)
    {
        UIServiceBarGraphic bar = rect.gameObject.AddComponent<UIServiceBarGraphic>();
        bar.raycastTarget = false;
        if (runBarMaterial != null)
            bar.material = runBarMaterial;
        bar.SetPulsePhase(pulsePhase);
        return bar;
    }

    /// <summary>
    /// A surface drawn as a design-system component (see UIComponentStyle).
    /// Without a theme, a flat frame in <paramref name="fallback"/>.
    /// </summary>
    private static Graphic AddSurface(RectTransform rect, string component, Color fallback, bool raycast)
    {
        UIComponentStyle style = UITheme.Active != null ? UITheme.Active.Component(component) : null;
        if (style == null)
            return AddFrame(rect, fallback, 0f, raycast);
        UIShape shape = rect.gameObject.AddComponent<UIShape>();
        shape.Component = style;
        shape.raycastTarget = raycast;
        return shape;
    }

    /// <summary>The design system's tone for a service state.</summary>
    private static UITone ToneOf(ServiceState state) => state switch
    {
        ServiceState.Overdue => UITone.Critical,
        ServiceState.DueSoon => UITone.Warning,
        _ => UITone.Normal
    };

    private void BuildLegend(RectTransform root)
    {
        legend = CreateRect("Legend", root);
        legend.anchorMin = Vector2.zero;
        legend.anchorMax = new Vector2(1f, 0f);
        legend.pivot = new Vector2(0.5f, 0f);
        legend.anchoredPosition = Vector2.zero;

        AddDivider(legend, true);

        RectTransform row = CreateRect("Keys", legend);
        // Centred under the cards.
        Stretch(row, ListLeft, ListLeft, 0f, 0f);
        legendKeys = row;
        HorizontalLayoutGroup keys = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        keys.spacing = 44f;
        keys.childAlignment = TextAnchor.MiddleCenter;
        keys.childControlWidth = true;
        keys.childControlHeight = true;
        keys.childForceExpandWidth = false;
        keys.childForceExpandHeight = false;

        AddLegendKey(row, UIServiceLegendSwatch.Kind.Run, normal, 22f, "RUN", "service.legend.run");
        AddLegendKey(row, UIServiceLegendSwatch.Kind.Estimate, Color.white, 8f, "ESTIMATE", "service.legend.estimate");
        AddLegendKey(row, UIServiceLegendSwatch.Kind.Plan, new Color(1f, 1f, 1f, 0.7f), 8f, "PLAN", "service.legend.plan");
        AddLegendKey(row, UIServiceLegendSwatch.Kind.Gap, new Color(1f, 1f, 1f, 0.55f), 22f, "GAP", "service.legend.gap");
    }

    private void AddLegendKey(RectTransform parent, UIServiceLegendSwatch.Kind kind, Color swatchColor, float swatchWidth, string text, string textKey)
    {
        RectTransform key = CreateRect(text, parent);
        HorizontalLayoutGroup pair = key.gameObject.AddComponent<HorizontalLayoutGroup>();
        pair.spacing = 10f;
        pair.childAlignment = TextAnchor.MiddleLeft;
        pair.childControlWidth = true;
        pair.childControlHeight = true;
        pair.childForceExpandWidth = false;
        pair.childForceExpandHeight = false;

        RectTransform swatch = CreateRect("Swatch", key);
        LayoutElement swatchSize = swatch.gameObject.AddComponent<LayoutElement>();
        swatchSize.preferredWidth = swatchWidth;
        swatchSize.preferredHeight = 20f;
        UIServiceLegendSwatch graphic = swatch.gameObject.AddComponent<UIServiceLegendSwatch>();
        graphic.raycastTarget = false;
        graphic.Configure(kind, swatchColor);

        TextMeshProUGUI caption = CreateText("Label", key, 17f, false, Muted, TextAlignmentOptions.MidlineLeft);
        caption.characterSpacing = 3f;
        UITextBinding.Bind(caption, textKey, text);
        UITextBinding.AllowShrinkToFit(caption);
    }

    /// <summary>A line along the inside of the band's bottom edge, or its top edge.</summary>
    private void AddDivider(RectTransform band, bool top)
    {
        RectTransform line = CreateRect("Divider", band);
        line.anchorMin = new Vector2(0f, top ? 1f : 0f);
        line.anchorMax = new Vector2(1f, top ? 1f : 0f);
        line.pivot = new Vector2(0.5f, top ? 1f : 0f);
        line.anchoredPosition = Vector2.zero;
        line.sizeDelta = new Vector2(0f, 1.5f);
        AddFrame(line, new Color(normal.r, normal.g, normal.b, 0.3f), 0f, false);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private TextMeshProUGUI CreateText(string name, RectTransform parent, float size, bool isValue, Color color, TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = isValue ? valueFont : labelFont;
        if (font != null)
            text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        // Ellipsis cuts a long name short, but TMP hides a line entirely when
        // its box is shorter than the font's line height: every text box here
        // is about 1.5 times its font size tall.
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.characterSpacing = isValue ? 1f : 2f;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// One line of text filling <paramref name="area"/>, which clips it. Text
    /// wider than the area slides back and forth, pausing at each end, and
    /// fades at the edge it runs under; text that fits stays still.
    /// </summary>
    private TextMeshProUGUI CreateSlidingText(string name, RectTransform area, float size, Color color)
    {
        TextMeshProUGUI text = CreateText(name, area, size, true, color, TextAlignmentOptions.MidlineLeft);
        Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
        text.overflowMode = TextOverflowModes.Overflow;
        TMPOverflowScroller scroller = text.gameObject.AddComponent<TMPOverflowScroller>();
        scroller.SetMotion(TextOverflowMotion.PingPong);
        scroller.CaptureRestingPosition();
        return text;
    }

    private static UIFrameGraphic AddFrame(RectTransform rect, Color fill, float thickness, bool raycast)
    {
        UIFrameGraphic frame = rect.gameObject.AddComponent<UIFrameGraphic>();
        frame.SetThickness(thickness);
        frame.SetColors(Color.clear, fill);
        frame.raycastTarget = raycast;
        return frame;
    }

    private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    /// <summary>Places a rect from the parent's top edge, <paramref name="left"/> and <paramref name="right"/> in from its sides.</summary>
    private static void PlaceBeside(RectTransform rect, float left, float right, float y, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0f, 1f);
        rect.offsetMin = new Vector2(left, y - height);
        rect.offsetMax = new Vector2(-right, y);
    }

    /// <summary>Places a rect from the parent's top-left or top-right corner.</summary>
    private static void Place(RectTransform rect, float x, float y, float width, float height, bool left)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(left ? 0f : 1f, 1f);
        rect.pivot = new Vector2(left ? 0f : 1f, 1f);
        rect.anchoredPosition = new Vector2(left ? x : -x, y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
