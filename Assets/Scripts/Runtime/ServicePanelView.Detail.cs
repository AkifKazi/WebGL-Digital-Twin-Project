using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The part detail: clicking a component card opens that part in place of the
/// list, as three cards that scroll together.
///
/// Overview: hours to service and the date that falls on, the part's bar, the
/// numbers it is drawn from (hours run, the estimated and planned intervals,
/// how far the estimate sits from the plan), then the likely cause of that
/// difference and the suggested fix.
///
/// Service history: the hours the part ran before each past service against
/// the plan, then the current interval, as columns.
///
/// Service data: when it was last serviced, how critical it is, how long the
/// service takes and what it needs.
///
/// Information only: nothing here starts or records work. The back arrow
/// returns to the list, where it was scrolled to.
/// </summary>
public sealed partial class ServicePanelView
{
    private const float OverviewHeight = 414f;
    private const float HistoryHeight = 248f;
    private const float ServiceDataHeight = 260f;
    // The values sit in a row of their own above the plot, so the plan line
    // and the columns never cross them.
    private const float ValueRowTop = 56f;
    private const float ChartTop = 88f;
    private const float ChartHeight = 110f;
    private const int MaximumHistoryColumns = 5;

    private RectTransform listHeader;
    private RectTransform detailHeader;
    private ScrollView detailView;
    private Button backButton;
    private TextMeshProUGUI detailName;
    private UIFrameGraphic detailChip;
    private TextMeshProUGUI detailChipText;

    private TextMeshProUGUI detailHours;
    private TextMeshProUGUI detailDue;
    private UIServiceBarGraphic detailBar;
    private TextMeshProUGUI detailRun;
    private TextMeshProUGUI detailEstimate;
    private TextMeshProUGUI detailPlan;
    private TextMeshProUGUI detailDifference;
    private TextMeshProUGUI detailCause;
    private TextMeshProUGUI detailFix;

    private TextMeshProUGUI historyPlan;
    private UIServiceHistoryGraphic historyChart;
    private readonly TextMeshProUGUI[] historyValues = new TextMeshProUGUI[MaximumHistoryColumns + 1];
    private readonly TextMeshProUGUI[] historyDates = new TextMeshProUGUI[MaximumHistoryColumns + 1];
    private readonly float[] historyBuffer = new float[MaximumHistoryColumns];
    private readonly ArraySegmentList historyShown = new();

    private TextMeshProUGUI dataLastService;
    private TextMeshProUGUI dataCriticality;
    private TextMeshProUGUI dataDuration;
    private TextMeshProUGUI dataMaterials;

    private MachinePartGroup selectedPart;

    /// <summary>The part whose detail is open, if any.</summary>
    public MachinePartGroup SelectedPart => selectedPart;

    public bool DetailOpen => selectedPart != null;

    /// <summary>Returns from the detail to the list.</summary>
    public Button BackButton => backButton;

    /// <summary>The detail's scroll view.</summary>
    public ScrollRect Detail => detailView?.Scroll;

    /// <summary>Space right of the detail's cards: wider while the detail scrolls.</summary>
    public float DetailRightMargin => detailView != null ? detailView.Stack.padding.right : 0f;

    /// <summary>The state shown in the detail's header.</summary>
    public string DetailStateText => detailChipText != null ? detailChipText.text : string.Empty;

    /// <summary>The part to mark on the model: the hovered card's, else the open detail's.</summary>
    public MachinePartGroup HighlightedPart => hoveredPart != null ? hoveredPart : selectedPart;

    public void ShowDetail(MachinePartGroup part)
    {
        Build();
        if (part == null || !TryGetStatus(part, out _))
        {
            ShowList();
            return;
        }

        bool changed = selectedPart != part;
        selectedPart = part;
        SetView(true);
        RefreshDetail();
        if (changed)
            detailView.Content.anchoredPosition = Vector2.zero;
    }

    public void ShowList()
    {
        Build();
        selectedPart = null;
        SetView(false);
    }

    private void SetView(bool detail)
    {
        listHeader.gameObject.SetActive(!detail);
        listView.Root.gameObject.SetActive(!detail);
        detailHeader.gameObject.SetActive(detail);
        detailView.Root.gameObject.SetActive(detail);
    }

    private bool TryGetStatus(MachinePartGroup part, out ServiceItemStatus status)
    {
        foreach (ServiceItemStatus item in items)
        {
            if (item.Part == part)
            {
                status = item;
                return true;
            }
        }
        status = default;
        return false;
    }

    private void RefreshDetail()
    {
        if (selectedPart == null)
            return;
        // The part left the schedule, or was destroyed: back to the list.
        if (!TryGetStatus(selectedPart, out ServiceItemStatus status))
        {
            ShowList();
            return;
        }

        TelemetryServiceSchedule.ServicePlan plan = default;
        schedule?.TryGetPlan(selectedPart, out plan);
        bool alert = status.State != ServiceState.OnTrack;
        Color state = StateColor(status.State);

        RefreshDetailHeader(status, state);
        RefreshOverview(status, plan, alert, state);
        RefreshHistory(status, plan, state);
        RefreshServiceData(status, plan);
    }

    private void RefreshDetailHeader(in ServiceItemStatus status, Color state)
    {
        SetText(detailName, DisplayName(status.Part));

        string chip = status.State switch
        {
            ServiceState.Overdue => UIText.Get("service.state.overdue", "OVERDUE"),
            ServiceState.DueSoon => UIText.Get("service.state.due_soon", "DUE SOON"),
            _ => UIText.Get("service.state.on_track", "ON TRACK")
        };
        SetText(detailChipText, chip);
        detailChipText.color = state;
        detailChip.SetColors(state, Color.clear);
        float chipWidth = Mathf.Ceil(detailChipText.GetPreferredValues(chip).x) + 24f;
        // A long translation is cut short rather than pushing the part's name out.
        float chipLimit = Mathf.Max(96f, detailHeader.rect.width * 0.45f);
        detailChipText.overflowMode = chipWidth > chipLimit ? TextOverflowModes.Ellipsis : TextOverflowModes.Overflow;
        chipWidth = Mathf.Min(chipWidth, chipLimit);
        detailChip.rectTransform.sizeDelta = new Vector2(chipWidth, 36f);
        detailName.rectTransform.offsetMax = new Vector2(-(chipWidth + 12f), detailName.rectTransform.offsetMax.y);
    }

    private void RefreshOverview(in ServiceItemStatus status, in TelemetryServiceSchedule.ServicePlan plan, bool alert, Color state)
    {
        detailHours.color = alert ? state : ink;
        bool overdue = status.HoursLeft <= 0d;
        SetText(detailHours, WithSuffix(
            Number(Math.Ceiling(Math.Abs(status.HoursLeft))),
            " " + (overdue
                ? UIText.Get("service.hours_overdue", "H OVERDUE")
                : UIText.Get("service.hours_to_service", "H TO SERVICE")),
            40));
        if (schedule != null)
        {
            DateTime due = overdue ? schedule.DateBefore(-status.HoursLeft) : schedule.DateAfter(status.HoursLeft);
            SetText(detailDue, overdue
                ? UIText.Format("service.was_due", "WAS DUE {0}", ShortDate(due))
                : UIText.Format("service.due", "DUE {0}", ShortDate(due)));
        }

        detailBar.SetValues((float)status.RunHours, (float)status.PlanHours, (float)status.EstimateHours, state);
        string hours = " " + UIText.Get("unit.hours", "H");
        SetText(detailRun, WithSuffix(Number(status.RunHours), hours, 75));
        SetText(detailEstimate, WithSuffix(Number(status.EstimateHours), hours, 75));
        SetText(detailPlan, WithSuffix(Number(status.PlanHours), hours, 75));

        // Earlier than plan reads as a warning: the part wears faster than
        // the manufacturer allows for.
        double difference = Math.Round(status.EstimateHours - status.PlanHours);
        string sign = difference > 0d ? "+" : difference < 0d ? "-" : string.Empty;
        detailDifference.color = difference < 0d ? warning : difference > 0d ? ink : Muted;
        SetText(detailDifference, WithSuffix(sign + Number(Math.Abs(difference)), hours, 75));

        // Authored plan text is keyed by part (part.bearings.cause), so the
        // language table can translate it too.
        string partKey = PartKey(status.Part);
        SetText(detailCause, string.IsNullOrWhiteSpace(plan.likelyCause)
            ? DefaultCause(difference)
            : UIText.Get(partKey + ".cause", plan.likelyCause));
        SetText(detailFix, string.IsNullOrWhiteSpace(plan.suggestedFix)
            ? DefaultFix(status.Task)
            : UIText.Get(partKey + ".fix", plan.suggestedFix));
    }

    private void RefreshHistory(in ServiceItemStatus status, in TelemetryServiceSchedule.ServicePlan plan, Color state)
    {
        SetText(historyPlan, UIText.Format("service.history.plan", "PLAN {0} H", Number(status.PlanHours)));

        // The latest past intervals that fit, oldest first.
        float[] source = plan.pastIntervals ?? Array.Empty<float>();
        int count = Mathf.Min(source.Length, MaximumHistoryColumns);
        int first = source.Length - count;
        for (int i = 0; i < count; i++)
            historyBuffer[i] = source[first + i];
        historyShown.Set(historyBuffer, count);

        historyChart.SetValues(
            historyShown,
            (float)status.PlanHours,
            (float)status.EstimateHours,
            (float)status.RunHours,
            new Color(normal.r, normal.g, normal.b, 0.32f),
            new Color(warning.r, warning.g, warning.b, 0.6f),
            state);

        // Each past service is dated back from the last one, which was the
        // hours run ago.
        int columns = count + 1;
        double hoursAgo = status.RunHours;
        for (int i = historyValues.Length - 1; i >= 0; i--)
        {
            bool shown = i < columns;
            historyValues[i].gameObject.SetActive(shown);
            historyDates[i].gameObject.SetActive(shown);
            if (!shown)
                continue;

            bool current = i == columns - 1;
            float value = current ? (float)status.EstimateHours : historyBuffer[i];
            PlaceColumn(historyValues[i].rectTransform, i, columns, ChartHeight + (ChartTop - ValueRowTop) - 26f);
            PlaceColumn(historyDates[i].rectTransform, i, columns, -6f);
            SetText(historyValues[i], Number(value));
            historyValues[i].color = current ? state : ink;

            if (current)
            {
                SetText(historyDates[i], UIText.Get("service.history.next", "NEXT"));
                historyDates[i].color = ink;
            }
            else
            {
                SetText(historyDates[i], schedule != null ? MonthYear(schedule.DateBefore(hoursAgo)) : string.Empty);
                historyDates[i].color = Muted;
                hoursAgo += historyBuffer[i];
            }
        }
    }

    private static void PlaceColumn(RectTransform rect, int column, int columns, float y)
    {
        float centre = (column + 0.5f) / columns;
        rect.anchorMin = rect.anchorMax = new Vector2(centre, 0f);
        rect.anchoredPosition = new Vector2(0f, y);
    }

    private void RefreshServiceData(in ServiceItemStatus status, in TelemetryServiceSchedule.ServicePlan plan)
    {
        if (schedule != null)
            SetText(dataLastService, ShortDate(schedule.DateBefore(status.RunHours)) + " " +
                                     schedule.DateBefore(status.RunHours).Year.ToString(CultureInfo.InvariantCulture));
        SetText(dataCriticality, plan.criticality switch
        {
            ServiceCriticality.High => UIText.Get("service.criticality.high", "High, stops production"),
            ServiceCriticality.Medium => UIText.Get("service.criticality.medium", "Medium, reduces output"),
            _ => UIText.Get("service.criticality.low", "Low, no production loss")
        });
        SetText(dataDuration, plan.durationMinutes > 0f
            ? Duration(plan.durationMinutes)
            : UIText.Get("service.data.not_recorded", "Not recorded"));
        SetText(dataMaterials, string.IsNullOrWhiteSpace(plan.materials)
            ? UIText.Get("service.data.none", "None")
            : UIText.Get(PartKey(status.Part) + ".materials", plan.materials));
    }

    private static string DefaultCause(double difference) => difference switch
    {
        < 0d => UIText.Get("service.cause.faster", "Running conditions wear it faster than planned"),
        > 0d => UIText.Get("service.cause.easier", "Running conditions are easier than planned"),
        _ => UIText.Get("service.cause.planned", "Wear follows the planned rate")
    };

    private static string DefaultFix(ServiceTask task) => task switch
    {
        ServiceTask.Regrease => UIText.Get("service.fix.regrease", "Regrease as planned"),
        ServiceTask.OilChange => UIText.Get("service.fix.oil_change", "Change the oil as planned"),
        ServiceTask.Replace => UIText.Get("service.fix.replace", "Replace as planned"),
        _ => UIText.Get("service.fix.inspect", "Inspect as planned")
    };

    private static string ShortDate(DateTime date) =>
        date.Day.ToString(CultureInfo.InvariantCulture) + " " + UIText.ShortMonth(date.Month);

    private static string MonthYear(DateTime date) =>
        UIText.ShortMonth(date.Month) + " " + date.ToString("yy", CultureInfo.InvariantCulture);

    private static string Duration(float minutes)
    {
        if (minutes < 60f)
            return UIText.Format("duration.minutes", "{0} min", Mathf.RoundToInt(minutes));
        float hours = Mathf.Round(minutes / 30f) * 0.5f;
        return UIText.Format("duration.hours", "{0} h", hours.ToString("0.#", CultureInfo.InvariantCulture));
    }

    private static string Number(double hours) =>
        Math.Round(hours).ToString("#,0", CultureInfo.InvariantCulture);

    private string WithSuffix(string value, string suffix, int sizePercent) =>
        value + "<size=" + sizePercent.ToString(CultureInfo.InvariantCulture) + "%><color=#" +
        ColorUtility.ToHtmlStringRGBA(Muted) + ">" + suffix + "</color></size>";

    // -----------------------------------------------------------------------
    // Building
    // -----------------------------------------------------------------------

    private void BuildDetail(RectTransform root, RectTransform headerContent)
    {
        BuildDetailHeader(headerContent);

        detailView = BuildScrollView("Part Detail", root);
        BuildOverview(DetailCard("Overview", OverviewHeight));
        BuildHistory(DetailCard("Service History", HistoryHeight));
        BuildServiceData(DetailCard("Service Data", ServiceDataHeight));
    }

    private void BuildDetailHeader(RectTransform headerContent)
    {
        detailHeader = CreateRect("Detail", headerContent);
        Stretch(detailHeader, 0f, 0f, 0f, 0f);

        // The arrow draws in line with the list's title; its square is large
        // enough to hit easily.
        RectTransform back = CreateRect("Back Button", detailHeader);
        back.anchorMin = back.anchorMax = new Vector2(0f, 0.5f);
        back.pivot = new Vector2(0f, 0.5f);
        back.anchoredPosition = new Vector2(-12f, 0f);
        back.sizeDelta = new Vector2(48f, 48f);
        UIFrameGraphic hit = AddFrame(back, Color.clear, 0f, true);
        AddHoverLook(back);
        RectTransform arrowRect = CreateRect("Arrow", back);
        Stretch(arrowRect, 0f, 0f, 0f, 0f);
        UIBackArrowGraphic arrow = arrowRect.gameObject.AddComponent<UIBackArrowGraphic>();
        arrow.color = normal;
        arrow.raycastTarget = false;

        backButton = back.gameObject.AddComponent<Button>();
        // No tint, set before the graphic, as for the scrollbar.
        backButton.transition = Selectable.Transition.None;
        backButton.targetGraphic = hit;
        backButton.navigation = new Navigation { mode = Navigation.Mode.None };
        backButton.onClick.AddListener(ShowList);

        detailName = CreateText("Name", detailHeader, 24f, true, ink, TextAlignmentOptions.MidlineLeft);
        detailName.characterSpacing = 3f;
        detailName.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        detailName.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        detailName.rectTransform.pivot = new Vector2(0f, 0.5f);
        detailName.rectTransform.offsetMin = new Vector2(44f, -18f);
        detailName.rectTransform.offsetMax = new Vector2(0f, 18f);

        RectTransform chip = CreateRect("State", detailHeader);
        chip.anchorMin = chip.anchorMax = new Vector2(1f, 0.5f);
        chip.pivot = new Vector2(1f, 0.5f);
        chip.anchoredPosition = Vector2.zero;
        detailChip = AddFrame(chip, Color.clear, 2f, false);
        detailChipText = CreateText("Label", chip, 17f, true, normal, TextAlignmentOptions.Center);
        detailChipText.characterSpacing = 3f;
        detailChipText.overflowMode = TextOverflowModes.Overflow;
        Stretch(detailChipText.rectTransform, 0f, 0f, 0f, 0f);
    }

    /// <summary>A card in the detail's stack; returns its content area, inside the card's padding.</summary>
    private RectTransform DetailCard(string name, float height)
    {
        RectTransform cardRect = CreateRect(name, detailView.Content);
        LayoutElement size = cardRect.gameObject.AddComponent<LayoutElement>();
        size.preferredHeight = height;
        size.minHeight = height;
        AddFrame(cardRect, card, 0f, false);

        RectTransform inner = CreateRect("Content", cardRect);
        Stretch(inner, CardPadding, CardPadding, 0f, 0f);
        return inner;
    }

    private void BuildOverview(RectTransform inner)
    {
        detailHours = CreateText("Hours To Service", inner, 60f, true, ink, TextAlignmentOptions.MidlineLeft);
        PlaceBeside(detailHours.rectTransform, 0f, 0f, -12f, 90f);

        detailDue = CreateText("Due", inner, 20f, false, Muted, TextAlignmentOptions.MidlineLeft);
        detailDue.characterSpacing = 3f;
        PlaceBeside(detailDue.rectTransform, 0f, 0f, -100f, 30f);

        RectTransform bar = CreateRect("Bar", inner);
        PlaceBeside(bar, 0f, 0f, -146f, 28f);
        detailBar = CreateBar(bar, 0f);

        detailRun = AddFigure(inner, 0, 4, -190f, "RUN", "service.figure.run");
        detailEstimate = AddFigure(inner, 1, 4, -190f, "ESTIMATE", "service.figure.estimate");
        detailPlan = AddFigure(inner, 2, 4, -190f, "PLAN", "service.figure.plan");
        detailDifference = AddFigure(inner, 3, 4, -190f, "VS PLAN", "service.figure.vs_plan");

        RectTransform line = CreateRect("Divider", inner);
        PlaceBeside(line, 0f, 0f, -272f, 1.5f);
        AddFrame(line, new Color(normal.r, normal.g, normal.b, 0.14f), 0f, false);

        detailCause = AddNote(inner, 0, "LIKELY CAUSE", "service.note.cause");
        detailFix = AddNote(inner, 1, "SUGGESTED FIX", "service.note.fix");
    }

    /// <summary>A labelled number in one of the columns under the bar.</summary>
    private TextMeshProUGUI AddFigure(RectTransform parent, int column, int columns, float y, string caption, string key)
    {
        RectTransform cell = Column(parent, caption, column, columns, y, 60f);

        TextMeshProUGUI heading = CreateText("Label", cell, 16f, false, label, TextAlignmentOptions.MidlineLeft);
        heading.characterSpacing = 2f;
        UITextBinding.Bind(heading, key, caption);
        PlaceBeside(heading.rectTransform, 0f, 4f, 0f, 26f);

        TextMeshProUGUI value = CreateText("Value", cell, 24f, true, ink, TextAlignmentOptions.MidlineLeft);
        PlaceBeside(value.rectTransform, 0f, 4f, -26f, 34f);
        return value;
    }

    /// <summary>A labelled line of text, up to three lines long, in one of two columns.</summary>
    private TextMeshProUGUI AddNote(RectTransform parent, int column, string caption, string key)
    {
        RectTransform cell = Column(parent, caption, column, 2, -290f, 104f);

        TextMeshProUGUI heading = CreateText("Label", cell, 16f, false, label, TextAlignmentOptions.MidlineLeft);
        heading.characterSpacing = 2f;
        UITextBinding.Bind(heading, key, caption);
        PlaceBeside(heading.rectTransform, 0f, 12f, 0f, 26f);

        TextMeshProUGUI text = CreateText("Text", cell, 19f, false, ink, TextAlignmentOptions.TopLeft);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.lineSpacing = -8f;
        PlaceBeside(text.rectTransform, 0f, 12f, -30f, 74f);
        return text;
    }

    private static RectTransform Column(RectTransform parent, string name, int column, int columns, float y, float height)
    {
        RectTransform cell = CreateRect(name, parent);
        cell.anchorMin = new Vector2((float)column / columns, 1f);
        cell.anchorMax = new Vector2((float)(column + 1) / columns, 1f);
        cell.pivot = new Vector2(0f, 1f);
        cell.offsetMin = new Vector2(0f, y - height);
        cell.offsetMax = new Vector2(0f, y);
        return cell;
    }

    private void BuildHistory(RectTransform inner)
    {
        TextMeshProUGUI title = CreateText("Title", inner, 16f, false, label, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 2f;
        UITextBinding.Bind(title, "service.history.title", "SERVICE HISTORY");
        PlaceBeside(title.rectTransform, 0f, 0f, -18f, 30f);

        historyPlan = CreateText("Plan", inner, 16f, false, Muted, TextAlignmentOptions.MidlineRight);
        historyPlan.characterSpacing = 2f;
        PlaceBeside(historyPlan.rectTransform, 0f, 0f, -18f, 30f);

        RectTransform chart = CreateRect("Chart", inner);
        PlaceBeside(chart, 0f, 0f, -ChartTop, ChartHeight);
        historyChart = chart.gameObject.AddComponent<UIServiceHistoryGraphic>();
        historyChart.raycastTarget = false;

        for (int i = 0; i < historyValues.Length; i++)
        {
            historyValues[i] = CreateText($"Value {i + 1}", chart, 17f, true, ink, TextAlignmentOptions.Bottom);
            historyValues[i].rectTransform.pivot = new Vector2(0.5f, 0f);
            historyValues[i].rectTransform.sizeDelta = new Vector2(96f, 26f);
            historyValues[i].overflowMode = TextOverflowModes.Overflow;

            historyDates[i] = CreateText($"Date {i + 1}", chart, 15f, false, Muted, TextAlignmentOptions.Top);
            historyDates[i].characterSpacing = 1f;
            historyDates[i].rectTransform.pivot = new Vector2(0.5f, 1f);
            historyDates[i].rectTransform.sizeDelta = new Vector2(96f, 24f);
            historyDates[i].overflowMode = TextOverflowModes.Overflow;
        }
    }

    private void BuildServiceData(RectTransform inner)
    {
        TextMeshProUGUI title = CreateText("Title", inner, 16f, false, label, TextAlignmentOptions.MidlineLeft);
        title.characterSpacing = 2f;
        UITextBinding.Bind(title, "service.data.title", "SERVICE DATA");
        PlaceBeside(title.rectTransform, 0f, 0f, -18f, 30f);

        dataLastService = AddDataRow(inner, "LAST SERVICE", "service.data.last_service", -62f, 40f);
        dataCriticality = AddDataRow(inner, "CRITICALITY", "service.data.criticality", -102f, 40f);
        dataDuration = AddDataRow(inner, "DURATION", "service.data.duration", -142f, 40f);
        dataMaterials = AddDataRow(inner, "MATERIALS", "service.data.materials", -182f, 60f);
        dataMaterials.textWrappingMode = TextWrappingModes.Normal;
        dataMaterials.lineSpacing = -8f;
        dataMaterials.alignment = TextAlignmentOptions.TopLeft;
    }

    /// <summary>A label and its value on one row, like a data sheet.</summary>
    private TextMeshProUGUI AddDataRow(RectTransform parent, string caption, string key, float y, float height)
    {
        TextMeshProUGUI heading = CreateText(caption, parent, 16f, false, Muted, TextAlignmentOptions.TopLeft);
        heading.characterSpacing = 2f;
        UITextBinding.Bind(heading, key, caption);
        PlaceBeside(heading.rectTransform, 0f, 0f, y - 6f, 26f);
        heading.rectTransform.anchorMax = new Vector2(0f, 1f);
        heading.rectTransform.offsetMax = new Vector2(160f, heading.rectTransform.offsetMax.y);

        TextMeshProUGUI value = CreateText(caption + " Value", parent, 19f, false, ink, TextAlignmentOptions.TopLeft);
        PlaceBeside(value.rectTransform, 164f, 0f, y - 4f, height - 4f);
        return value;
    }

    /// <summary>A fixed array seen as a list of its first entries, without allocating.</summary>
    private sealed class ArraySegmentList : System.Collections.Generic.IReadOnlyList<float>
    {
        private float[] values = Array.Empty<float>();
        private int count;

        public void Set(float[] source, int length)
        {
            values = source;
            count = Mathf.Clamp(length, 0, source.Length);
        }

        public float this[int index] => values[index];

        public int Count => count;

        public System.Collections.Generic.IEnumerator<float> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
                yield return values[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
