using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The service panel: every part's service position sorted by hours left, in a
/// scrolling list between a header and a legend for the bars. Parts due soon
/// or overdue are marked in their state colour; parts on track stay quiet.
///
/// Built at runtime from the service schedule, like the detail panel's graphs,
/// so adding a plan to the schedule adds a row with no other authoring. The
/// header and legend line up with the viewport's top and bottom bars when the
/// panel runs the full height beside it (see <see cref="SetBands"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class ServicePanelView : MonoBehaviour
{
    [SerializeField] private TelemetryServiceSchedule schedule;
    [SerializeField] private TMP_FontAsset labelFont;
    [SerializeField] private TMP_FontAsset valueFont;

    [Tooltip("Seconds between refreshes of the hours shown.")]
    [SerializeField, Min(0.1f)] private float refreshInterval = 1f;

    [Tooltip("Space between the panel's edges and its content.")]
    [SerializeField, Min(0f)] private float inset = 28f;

    [Header("Colours")]
    [SerializeField] private Color ink = new(0.918f, 0.992f, 1f, 1f);
    [SerializeField] private Color label = new(0.212f, 0.929f, 1f, 1f);
    [SerializeField] private Color normal = new(0.08f, 0.9f, 1f, 1f);
    [SerializeField] private Color warning = new(1f, 0.70f, 0.10f, 1f);
    [SerializeField] private Color critical = new(1f, 0.22f, 0.20f, 1f);
    [SerializeField] private Color onBadge = new(0.024f, 0.075f, 0.102f, 1f);

    [Tooltip("The panel's fill. Near opaque, so the model does not show through the list.")]
    [SerializeField] private Color backdrop = new(0.031f, 0.086f, 0.118f, 0.96f);

    private const float RowHeight = 116f;
    private const float HoursWidth = 170f;
    private const float CompactHeaderHeight = 72f;
    private const float CompactLegendHeight = 64f;
    // Room on the right of each row for the scrollbar.
    private const float ScrollGutter = 12f;
    private const float ScrollHitWidth = 24f;
    private const float ScrollTrackWidth = 2f;
    private const float ScrollThumbWidth = 6f;
    // Kept low: the project blends in linear space, where a small alpha reads strong.
    private const float AlertTint = 0.012f;

    private readonly List<ServiceItemStatus> statusBuffer = new();
    private readonly List<ServiceItemStatus> items = new();
    private readonly List<Row> rows = new();

    private RectTransform border;
    private RectTransform header;
    private RectTransform headerText;
    private TextMeshProUGUI runHoursText;
    private RectTransform listRect;
    private RectTransform listContent;
    private RectTransform legend;
    private RectTransform legendKeys;
    private bool built;
    private float nextRefresh;
    private long shownRunHours = -1;
    private MachinePartGroup hoveredPart;
    private Vector3 appliedBands = new(-1f, -1f, -1f);

    /// <summary>Every part's service position, soonest first.</summary>
    public IReadOnlyList<ServiceItemStatus> Items => items;

    /// <summary>The part whose row the pointer is over, if any.</summary>
    public MachinePartGroup HoveredPart => hoveredPart;

    /// <summary>The list's scroll view.</summary>
    public ScrollRect List { get; private set; }

    /// <summary>The list's scrollbar, shown while the rows do not all fit.</summary>
    public Scrollbar Scrollbar { get; private set; }

    public event Action<MachinePartGroup> HoveredPartChanged;

    private Color Muted => new(ink.r, ink.g, ink.b, 0.62f);

    public Color StateColor(ServiceState state) => state switch
    {
        ServiceState.Overdue => critical,
        ServiceState.DueSoon => warning,
        _ => normal
    };

    /// <summary>The number shown on the part's row and pin, 1 for the soonest.</summary>
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
        listRect.offsetMin = new Vector2(0f, bottom);
        listRect.offsetMax = new Vector2(0f, -top);

        // Beside the viewport only the left edge is drawn: the others fall on
        // the screen's edges. Over the content area the panel is framed.
        float outside = beside ? 2f : 0f;
        border.offsetMin = new Vector2(0f, -outside);
        border.offsetMax = new Vector2(outside, outside);
    }

    private void Awake() => Build();

    private void OnEnable()
    {
        nextRefresh = 0f;
        Refresh();
    }

    private void OnDisable() => SetHovered(null);

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh)
            return;
        Refresh();
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
            runHoursText.text = run.ToString("#,0", CultureInfo.InvariantCulture) + " H RUN";
        }

        RefreshRows();
    }

    private static int SoonestFirst(ServiceItemStatus a, ServiceItemStatus b) =>
        a.HoursLeft.CompareTo(b.HoursLeft);

    // -----------------------------------------------------------------------
    // Rows
    // -----------------------------------------------------------------------

    private void RefreshRows()
    {
        while (rows.Count < items.Count)
            rows.Add(CreateRow(rows.Count));

        for (int i = 0; i < rows.Count; i++)
        {
            bool shown = i < items.Count;
            rows[i].Root.gameObject.SetActive(shown);
            if (shown)
                BindRow(rows[i], items[i], i + 1);
        }
    }

    private void BindRow(Row row, in ServiceItemStatus status, int number)
    {
        bool alert = status.State != ServiceState.OnTrack;
        Color state = StateColor(status.State);
        row.Hover.Part = status.Part;
        row.Background.SetColors(Color.clear, alert ? new Color(state.r, state.g, state.b, AlertTint) : Color.clear);
        row.Accent.gameObject.SetActive(alert);
        row.Accent.SetColors(Color.clear, state);
        row.Badge.SetColors(Color.clear, alert ? state : new Color(normal.r, normal.g, normal.b, 0.55f));
        SetText(row.Number, number.ToString(CultureInfo.InvariantCulture));
        SetText(row.Name, DisplayName(status.Part));
        SetText(row.Task, TaskLabel(status.Task));
        row.Hours.color = alert ? state : ink;
        SetText(row.Hours, HoursText(status.HoursLeft));
        row.Bar.SetValues(
            (float)status.RunHours,
            (float)status.PlanHours,
            (float)status.EstimateHours,
            alert ? state : new Color(normal.r, normal.g, normal.b, 0.55f));
    }

    // -----------------------------------------------------------------------
    // Hover
    // -----------------------------------------------------------------------

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
        string suffix = "<size=55%><color=#" + ColorUtility.ToHtmlStringRGBA(Muted) + "> H</color></size>";
        if (hoursLeft <= 0d)
            return "+" + Math.Ceiling(-hoursLeft).ToString("#,0", CultureInfo.InvariantCulture) + suffix;
        return Math.Ceiling(hoursLeft).ToString("#,0", CultureInfo.InvariantCulture) + suffix;
    }

    private static string DisplayName(MachinePartGroup part) =>
        part != null ? part.DisplayName.ToUpperInvariant() : string.Empty;

    public static string TaskLabel(ServiceTask task) => task switch
    {
        ServiceTask.Regrease => "REGREASE",
        ServiceTask.OilChange => "OIL CHANGE",
        ServiceTask.Replace => "REPLACE",
        _ => "INSPECT"
    };

    private static void SetText(TMP_Text text, string value)
    {
        if (!string.Equals(text.text, value, StringComparison.Ordinal))
            text.text = value;
    }

    // -----------------------------------------------------------------------
    // Building
    // -----------------------------------------------------------------------

    private sealed class Row
    {
        public RectTransform Root;
        public ServiceHoverTarget Hover;
        public UIFrameGraphic Background;
        public UIFrameGraphic Accent;
        public UIFrameGraphic Badge;
        public TextMeshProUGUI Number;
        public TextMeshProUGUI Name;
        public TextMeshProUGUI Task;
        public TextMeshProUGUI Hours;
        public UIServiceBarGraphic Bar;
    }

    private void Build()
    {
        if (built)
            return;
        built = true;

        RectTransform root = (RectTransform)transform;

        RectTransform fill = CreateRect("Backdrop", root);
        Stretch(fill, 0f, 0f, 0f, 0f);
        // Catches the pointer over the whole panel, so the wheel and drags
        // here never reach the model behind it.
        AddFrame(fill, backdrop, 0f, true);

        border = CreateRect("Border", root);
        Stretch(border, 0f, 0f, 0f, 0f);
        UIFrameGraphic edge = AddFrame(border, Color.clear, 2f, false);
        edge.SetColors(new Color(normal.r, normal.g, normal.b, 0.5f), Color.clear);

        header = CreateRect("Header", root);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = Vector2.one;
        header.pivot = new Vector2(0.5f, 1f);
        header.anchoredPosition = Vector2.zero;

        headerText = CreateRect("Content", header);
        Stretch(headerText, inset, inset + ScrollGutter, 0f, 0f);

        TextMeshProUGUI title = CreateText("Title", headerText, 20f, false, label, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 6f;
        title.text = "SERVICE SCHEDULE";
        Stretch(title.rectTransform, 0f, 0f, 0f, 0f);

        runHoursText = CreateText("Run Hours", headerText, 20f, false, Muted, TextAlignmentOptions.MidlineRight);
        Stretch(runHoursText.rectTransform, 0f, 0f, 0f, 0f);

        AddDivider(header, false);

        BuildList(root);
        BuildLegend(root);
        SetBands(0f, 0f, 0f);
    }

    private void BuildList(RectTransform root)
    {
        listRect = CreateRect("Service List", root);
        listRect.anchorMin = Vector2.zero;
        listRect.anchorMax = Vector2.one;
        ScrollRect scroll = listRect.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.inertia = true;
        List = scroll;

        RectTransform viewport = CreateRect("Viewport", listRect);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        // Invisible, but catches the wheel and drags between rows.
        AddFrame(viewport, Color.clear, 0f, true);
        scroll.viewport = viewport;

        listContent = CreateRect("Rows", viewport);
        listContent.anchorMin = new Vector2(0f, 1f);
        listContent.anchorMax = Vector2.one;
        listContent.pivot = new Vector2(0.5f, 1f);
        listContent.sizeDelta = Vector2.zero;
        VerticalLayoutGroup stack = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        ContentSizeFitter fit = listContent.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = listContent;

        Scrollbar = BuildScrollbar(listRect);
        scroll.verticalScrollbar = Scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
    }

    /// <summary>
    /// A hairline track down the right edge with a solid thumb riding on it:
    /// its length is the share of the list in view, its place how far down it
    /// is. The thumb and track take a pointer across a wider strip than they
    /// draw, so the thin bar is still easy to drag.
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

        // Centred under the thumb's right edge, as in the track-and-block style.
        float lineRight = 8f;
        RectTransform track = CreateRect("Track", bar);
        track.anchorMin = new Vector2(1f, 0f);
        track.anchorMax = Vector2.one;
        track.pivot = new Vector2(1f, 0.5f);
        track.offsetMin = new Vector2(-lineRight - ScrollTrackWidth, 0f);
        track.offsetMax = new Vector2(-lineRight, 0f);
        AddFrame(track, new Color(normal.r, normal.g, normal.b, 0.35f), 0f, false);

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
        UIFrameGraphic thumbGraphic = AddFrame(thumb, ink, 0f, false);

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

    private Row CreateRow(int index)
    {
        Row row = new() { Root = CreateRect($"Row {index + 1}", listContent) };
        LayoutElement size = row.Root.gameObject.AddComponent<LayoutElement>();
        size.preferredHeight = RowHeight;
        size.minHeight = RowHeight;

        row.Background = AddFrame(row.Root, Color.clear, 0f, true);
        row.Background.raycastTarget = true;
        row.Hover = row.Root.gameObject.AddComponent<ServiceHoverTarget>();
        row.Hover.HoverChanged += HandleHover;

        RectTransform accent = CreateRect("Accent", row.Root);
        accent.anchorMin = Vector2.zero;
        accent.anchorMax = new Vector2(0f, 1f);
        accent.pivot = new Vector2(0f, 0.5f);
        accent.sizeDelta = new Vector2(4f, 0f);
        row.Accent = AddFrame(accent, Color.clear, 0f, false);

        RectTransform line = CreateRect("Divider", row.Root);
        line.anchorMin = Vector2.zero;
        line.anchorMax = new Vector2(1f, 0f);
        line.pivot = new Vector2(0.5f, 0f);
        line.sizeDelta = new Vector2(0f, 1.5f);
        AddFrame(line, new Color(normal.r, normal.g, normal.b, 0.14f), 0f, false);

        RectTransform inner = CreateRect("Content", row.Root);
        Stretch(inner, inset, inset + ScrollGutter, 0f, 0f);

        RectTransform badge = CreateRect("Badge", inner);
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 1f);
        badge.pivot = new Vector2(0f, 1f);
        badge.anchoredPosition = new Vector2(0f, -20f);
        badge.sizeDelta = new Vector2(36f, 36f);
        row.Badge = AddFrame(badge, Color.clear, 0f, false);
        row.Number = CreateText("Number", badge, 21f, true, onBadge, TextAlignmentOptions.Center);
        Stretch(row.Number.rectTransform, 0f, 0f, 0f, 0f);

        row.Name = CreateText("Name", inner, 24f, true, ink, TextAlignmentOptions.MidlineLeft);
        row.Name.characterSpacing = 4f;
        // Names take the width left of the hours, and cut short where a
        // narrow panel leaves too little.
        PlaceBeside(row.Name.rectTransform, 54f, HoursWidth + 12f, -8f, 36f);

        row.Task = CreateText("Task", inner, 19f, false, Muted, TextAlignmentOptions.MidlineLeft);
        row.Task.characterSpacing = 3f;
        PlaceBeside(row.Task.rectTransform, 54f, HoursWidth + 12f, -44f, 30f);

        row.Hours = CreateText("Hours", inner, 40f, true, ink, TextAlignmentOptions.MidlineRight);
        Place(row.Hours.rectTransform, 0f, -6f, HoursWidth, 62f, left: false);

        RectTransform bar = CreateRect("Bar", inner);
        bar.anchorMin = Vector2.zero;
        bar.anchorMax = new Vector2(1f, 0f);
        bar.pivot = new Vector2(0.5f, 0f);
        bar.anchoredPosition = new Vector2(0f, 18f);
        bar.sizeDelta = new Vector2(0f, 20f);
        row.Bar = bar.gameObject.AddComponent<UIServiceBarGraphic>();
        row.Bar.raycastTarget = false;
        return row;
    }

    private void BuildLegend(RectTransform root)
    {
        legend = CreateRect("Legend", root);
        legend.anchorMin = Vector2.zero;
        legend.anchorMax = new Vector2(1f, 0f);
        legend.pivot = new Vector2(0.5f, 0f);
        legend.anchoredPosition = Vector2.zero;

        AddDivider(legend, true);

        RectTransform row = CreateRect("Keys", legend);
        Stretch(row, inset, inset + ScrollGutter, 0f, 0f);
        legendKeys = row;
        HorizontalLayoutGroup keys = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        keys.childAlignment = TextAnchor.MiddleLeft;
        keys.childControlWidth = true;
        keys.childControlHeight = true;
        keys.childForceExpandWidth = true;
        keys.childForceExpandHeight = false;

        AddLegendKey(row, UIServiceLegendSwatch.Kind.Run, new Color(normal.r, normal.g, normal.b, 0.55f), 22f, "RUN");
        AddLegendKey(row, UIServiceLegendSwatch.Kind.Estimate, Color.white, 8f, "ESTIMATE");
        AddLegendKey(row, UIServiceLegendSwatch.Kind.Plan, new Color(1f, 1f, 1f, 0.7f), 8f, "PLAN");
        AddLegendKey(row, UIServiceLegendSwatch.Kind.Gap, new Color(1f, 1f, 1f, 0.55f), 22f, "GAP");
    }

    private void AddLegendKey(RectTransform parent, UIServiceLegendSwatch.Kind kind, Color swatchColor, float swatchWidth, string text)
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
        caption.text = text;
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
