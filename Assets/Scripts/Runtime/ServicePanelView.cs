using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The service panel: what needs attention at the top, then every part's
/// service position sorted by hours left, then a legend for the bars.
///
/// Built at runtime from the service schedule, like the detail panel's graphs,
/// so adding a plan to the schedule adds a row with no other authoring. Colour
/// is kept for what needs attention; parts on track stay quiet.
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

    [Tooltip("Most items the Needs Attention list shows.")]
    [SerializeField, Range(1, 5)] private int maximumAttentionItems = 3;

    [Header("Colours")]
    [SerializeField] private Color ink = new(0.918f, 0.992f, 1f, 1f);
    [SerializeField] private Color label = new(0.212f, 0.929f, 1f, 1f);
    [SerializeField] private Color normal = new(0.08f, 0.9f, 1f, 1f);
    [SerializeField] private Color warning = new(1f, 0.70f, 0.10f, 1f);
    [SerializeField] private Color critical = new(1f, 0.22f, 0.20f, 1f);
    [SerializeField] private Color onBadge = new(0.024f, 0.075f, 0.102f, 1f);

    [Tooltip("Fill inside the panel's frame. Near opaque, so the model does not show through the list.")]
    [SerializeField] private Color backdrop = new(0.031f, 0.086f, 0.118f, 0.94f);

    private const float RowHeight = 116f;
    private const float AttentionItemHeight = 80f;
    private const float AttentionGap = 10f;
    private const float HeaderHeight = 36f;
    private const float SectionPadding = 18f;
    private const float EmptyHeight = 40f;
    private const float LegendHeight = 60f;
    // Kept low: the project blends in linear space, where a small alpha reads strong.
    private const float AlertTint = 0.05f;

    private readonly List<ServiceItemStatus> statusBuffer = new();
    private readonly List<ServiceItemStatus> items = new();
    private readonly List<int> attention = new();
    private readonly List<Row> rows = new();
    private readonly List<AttentionItem> attentionItems = new();

    private RectTransform attentionSection;
    private RectTransform attentionList;
    private TextMeshProUGUI runHoursText;
    private TextMeshProUGUI emptyText;
    private RectTransform divider;
    private RectTransform listRect;
    private RectTransform listContent;
    private bool built;
    private float nextRefresh;
    private long shownRunHours = -1;
    private MachinePartGroup hoveredPart;

    /// <summary>Every part's service position, soonest first.</summary>
    public IReadOnlyList<ServiceItemStatus> Items => items;

    /// <summary>How many items the Needs Attention list is showing.</summary>
    public int AttentionCount => attention.Count;

    /// <summary>The part whose row or attention item the pointer is over, if any.</summary>
    public MachinePartGroup HoveredPart => hoveredPart;

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

    /// <summary>True when the part is in the Needs Attention list.</summary>
    public bool NeedsAttention(MachinePartGroup part)
    {
        foreach (int index in attention)
        {
            if (items[index].Part == part)
                return true;
        }
        return false;
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

        attention.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].State != ServiceState.OnTrack)
                attention.Add(i);
        }
        attention.Sort((a, b) =>
        {
            int severity = items[b].State.CompareTo(items[a].State);
            return severity != 0 ? severity : a.CompareTo(b);
        });
        if (attention.Count > maximumAttentionItems)
            attention.RemoveRange(maximumAttentionItems, attention.Count - maximumAttentionItems);

        RefreshAttention();
        RefreshRows();
    }

    private static int SoonestFirst(ServiceItemStatus a, ServiceItemStatus b) =>
        a.HoursLeft.CompareTo(b.HoursLeft);

    // -----------------------------------------------------------------------
    // Needs attention
    // -----------------------------------------------------------------------

    private void RefreshAttention()
    {
        long run = schedule != null ? (long)Math.Floor(schedule.MachineRunHours) : 0;
        if (run != shownRunHours)
        {
            shownRunHours = run;
            runHoursText.text = run.ToString("#,0", CultureInfo.InvariantCulture) + " H RUN";
        }

        while (attentionItems.Count < attention.Count)
            attentionItems.Add(CreateAttentionItem(attentionItems.Count));

        for (int i = 0; i < attentionItems.Count; i++)
        {
            bool shown = i < attention.Count;
            attentionItems[i].Root.gameObject.SetActive(shown);
            if (shown)
                BindAttention(attentionItems[i], items[attention[i]], attention[i] + 1);
        }

        emptyText.gameObject.SetActive(attention.Count == 0);

        float listHeight = attention.Count > 0
            ? attention.Count * AttentionItemHeight + (attention.Count - 1) * AttentionGap
            : EmptyHeight;
        float sectionHeight = SectionPadding + HeaderHeight + 12f + listHeight + SectionPadding;
        attentionSection.sizeDelta = new Vector2(0f, sectionHeight);
        attentionList.sizeDelta = new Vector2(attentionList.sizeDelta.x, listHeight);
        divider.anchoredPosition = new Vector2(0f, -sectionHeight);
        listRect.offsetMax = new Vector2(0f, -sectionHeight - 1.5f);
    }

    private void BindAttention(AttentionItem item, in ServiceItemStatus status, int number)
    {
        Color state = StateColor(status.State);
        item.Hover.Part = status.Part;
        item.Frame.SetColors(state, new Color(state.r, state.g, state.b, AlertTint * 0.7f));
        item.Badge.SetColors(Color.clear, state);
        SetText(item.Number, number.ToString(CultureInfo.InvariantCulture));
        item.ChipFrame.SetColors(state, Color.clear);
        item.ChipText.color = state;
        SetText(item.ChipText, status.State == ServiceState.Overdue ? "OVERDUE" : "DUE SOON");
        float chipWidth = Mathf.Ceil(item.ChipText.GetPreferredValues(item.ChipText.text).x) + 16f;
        item.ChipFrame.rectTransform.sizeDelta = new Vector2(chipWidth, 26f);
        item.Title.rectTransform.anchoredPosition = new Vector2(64f + chipWidth + 12f, -8f);
        SetText(item.Title, DisplayName(status.Part, false));

        string action = status.State == ServiceState.Overdue
            ? $"{TaskVerb(status.Task)} now"
            : $"{TaskVerb(status.Task)} by {schedule.DateAfter(status.HoursLeft).ToString("d MMM", CultureInfo.InvariantCulture)}";
        SetText(item.Action, action);
        item.Hours.color = state;
        SetText(item.Hours, HoursText(status.HoursLeft));
    }

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
        SetText(row.Name, DisplayName(status.Part, true));
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

    private static string DisplayName(MachinePartGroup part, bool upper)
    {
        string name = part != null ? part.DisplayName : string.Empty;
        return upper ? name.ToUpperInvariant() : name;
    }

    public static string TaskLabel(ServiceTask task) => task switch
    {
        ServiceTask.Regrease => "REGREASE",
        ServiceTask.OilChange => "OIL CHANGE",
        ServiceTask.Replace => "REPLACE",
        _ => "INSPECT"
    };

    private static string TaskVerb(ServiceTask task) => task switch
    {
        ServiceTask.Regrease => "Regrease",
        ServiceTask.OilChange => "Change oil",
        ServiceTask.Replace => "Replace",
        _ => "Inspect"
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

    private sealed class AttentionItem
    {
        public RectTransform Root;
        public ServiceHoverTarget Hover;
        public UIFrameGraphic Frame;
        public UIFrameGraphic Badge;
        public TextMeshProUGUI Number;
        public UIFrameGraphic ChipFrame;
        public TextMeshProUGUI ChipText;
        public TextMeshProUGUI Title;
        public TextMeshProUGUI Action;
        public TextMeshProUGUI Hours;
    }

    private void Build()
    {
        if (built)
            return;
        built = true;

        RectTransform root = (RectTransform)transform;

        RectTransform fill = CreateRect("Backdrop", root);
        Stretch(fill, 2f, 2f, 2f, 2f);
        AddFrame(fill, backdrop, 0f, false);

        // Needs attention, pinned to the top.
        attentionSection = CreateRect("Needs Attention", root);
        attentionSection.anchorMin = new Vector2(0f, 1f);
        attentionSection.anchorMax = Vector2.one;
        attentionSection.pivot = new Vector2(0.5f, 1f);
        attentionSection.anchoredPosition = Vector2.zero;

        TextMeshProUGUI heading = CreateText("Heading", attentionSection, 19f, false, label, TextAlignmentOptions.MidlineLeft);
        heading.characterSpacing = 6f;
        heading.text = "NEEDS ATTENTION";
        Place(heading.rectTransform, inset, -SectionPadding, 300f, HeaderHeight, left: true);

        runHoursText = CreateText("Run Hours", attentionSection, 19f, false, Muted, TextAlignmentOptions.MidlineRight);
        Place(runHoursText.rectTransform, inset, -SectionPadding, 240f, HeaderHeight, left: false);

        attentionList = CreateRect("Items", attentionSection);
        attentionList.anchorMin = new Vector2(0f, 1f);
        attentionList.anchorMax = Vector2.one;
        attentionList.pivot = new Vector2(0.5f, 1f);
        // Items sit a little wider than the text column so their frames align with it.
        attentionList.sizeDelta = new Vector2(-2f * (inset - 8f), 0f);
        attentionList.anchoredPosition = new Vector2(0f, -(SectionPadding + HeaderHeight + 12f));

        emptyText = CreateText("All On Track", attentionList, 20f, false, Muted, TextAlignmentOptions.MidlineLeft);
        emptyText.text = "All parts on track";
        Stretch(emptyText.rectTransform, 8f, 0f, 0f, 0f);

        divider = CreateRect("Divider", root);
        divider.anchorMin = new Vector2(0f, 1f);
        divider.anchorMax = Vector2.one;
        divider.pivot = new Vector2(0.5f, 1f);
        divider.sizeDelta = new Vector2(0f, 1.5f);
        AddFrame(divider, new Color(normal.r, normal.g, normal.b, 0.3f), 0f, true);

        // The list scrolls between the attention section and the legend.
        listRect = CreateRect("Service List", root);
        listRect.anchorMin = Vector2.zero;
        listRect.anchorMax = Vector2.one;
        listRect.offsetMin = new Vector2(0f, LegendHeight);
        ScrollRect scroll = listRect.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        scroll.inertia = true;

        RectTransform viewport = CreateRect("Viewport", listRect);
        Stretch(viewport, 0f, 0f, 0f, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        // Invisible, but catches the wheel and drags between rows.
        AddFrame(viewport, Color.clear, 0f, true).raycastTarget = true;
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

        BuildLegend(root);
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
        Stretch(inner, inset, inset, 0f, 0f);

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
        Place(row.Name.rectTransform, 54f, -8f, 300f, 36f, left: true);

        row.Task = CreateText("Task", inner, 19f, false, Muted, TextAlignmentOptions.MidlineLeft);
        row.Task.characterSpacing = 3f;
        Place(row.Task.rectTransform, 54f, -44f, 300f, 30f, left: true);

        row.Hours = CreateText("Hours", inner, 40f, true, ink, TextAlignmentOptions.MidlineRight);
        Place(row.Hours.rectTransform, 0f, -6f, 200f, 62f, left: false);

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

    private AttentionItem CreateAttentionItem(int index)
    {
        AttentionItem item = new() { Root = CreateRect($"Item {index + 1}", attentionList) };
        item.Root.anchorMin = new Vector2(0f, 1f);
        item.Root.anchorMax = Vector2.one;
        item.Root.pivot = new Vector2(0.5f, 1f);
        item.Root.sizeDelta = new Vector2(0f, AttentionItemHeight);
        item.Root.anchoredPosition = new Vector2(0f, -index * (AttentionItemHeight + AttentionGap));

        item.Frame = AddFrame(item.Root, Color.clear, 2f, true);
        item.Frame.raycastTarget = true;
        item.Hover = item.Root.gameObject.AddComponent<ServiceHoverTarget>();
        item.Hover.HoverChanged += HandleHover;

        RectTransform badge = CreateRect("Badge", item.Root);
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f);
        badge.pivot = new Vector2(0f, 0.5f);
        badge.anchoredPosition = new Vector2(14f, 0f);
        badge.sizeDelta = new Vector2(36f, 36f);
        item.Badge = AddFrame(badge, Color.clear, 0f, false);
        item.Number = CreateText("Number", badge, 21f, true, onBadge, TextAlignmentOptions.Center);
        Stretch(item.Number.rectTransform, 0f, 0f, 0f, 0f);

        RectTransform chip = CreateRect("State", item.Root);
        chip.anchorMin = chip.anchorMax = new Vector2(0f, 1f);
        chip.pivot = new Vector2(0f, 1f);
        chip.anchoredPosition = new Vector2(64f, -12f);
        item.ChipFrame = AddFrame(chip, Color.clear, 1.5f, false);
        item.ChipText = CreateText("Label", chip, 16f, true, warning, TextAlignmentOptions.Center);
        item.ChipText.characterSpacing = 4f;
        Stretch(item.ChipText.rectTransform, 0f, 0f, 0f, 0f);

        item.Title = CreateText("Part", item.Root, 22f, true, ink, TextAlignmentOptions.MidlineLeft);
        item.Title.characterSpacing = 1f;
        item.Title.rectTransform.anchorMin = item.Title.rectTransform.anchorMax = new Vector2(0f, 1f);
        item.Title.rectTransform.pivot = new Vector2(0f, 1f);
        item.Title.rectTransform.sizeDelta = new Vector2(260f, 34f);

        item.Action = CreateText("Action", item.Root, 19f, false, Muted, TextAlignmentOptions.MidlineLeft);
        Place(item.Action.rectTransform, 64f, -42f, 320f, 30f, left: true);

        item.Hours = CreateText("Hours", item.Root, 30f, true, warning, TextAlignmentOptions.MidlineRight);
        item.Hours.rectTransform.anchorMin = item.Hours.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        item.Hours.rectTransform.pivot = new Vector2(1f, 0.5f);
        item.Hours.rectTransform.anchoredPosition = new Vector2(-16f, 0f);
        item.Hours.rectTransform.sizeDelta = new Vector2(150f, 48f);
        return item;
    }

    private void BuildLegend(RectTransform root)
    {
        RectTransform legend = CreateRect("Legend", root);
        legend.anchorMin = Vector2.zero;
        legend.anchorMax = new Vector2(1f, 0f);
        legend.pivot = new Vector2(0.5f, 0f);
        legend.sizeDelta = new Vector2(0f, LegendHeight);

        RectTransform line = CreateRect("Divider", legend);
        line.anchorMin = new Vector2(0f, 1f);
        line.anchorMax = Vector2.one;
        line.pivot = new Vector2(0.5f, 1f);
        line.sizeDelta = new Vector2(0f, 1.5f);
        AddFrame(line, new Color(normal.r, normal.g, normal.b, 0.3f), 0f, false);

        RectTransform row = CreateRect("Keys", legend);
        Stretch(row, inset, inset, 0f, 0f);
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

    /// <summary>Places a rect from the parent's top-left or top-right corner.</summary>
    private static void Place(RectTransform rect, float x, float y, float width, float height, bool left)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(left ? 0f : 1f, 1f);
        rect.pivot = new Vector2(left ? 0f : 1f, 1f);
        rect.anchoredPosition = new Vector2(left ? x : -x, y);
        rect.sizeDelta = new Vector2(width, height);
    }
}
