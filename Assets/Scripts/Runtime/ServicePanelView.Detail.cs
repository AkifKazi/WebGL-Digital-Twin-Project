using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The part detail: clicking a row opens that part in place of the list, with
/// its hours to service, the date that falls on, its bar, and the numbers the
/// bar is drawn from: hours run, the estimated and planned intervals, and how
/// far the estimate sits from the plan. The back arrow returns to the list,
/// where it was scrolled to.
/// </summary>
public sealed partial class ServicePanelView
{
    private const float DetailCardHeight = 272f;

    private RectTransform listHeader;
    private RectTransform detailHeader;
    private RectTransform detailBody;
    private Button backButton;
    private TextMeshProUGUI detailName;
    private TextMeshProUGUI detailTask;
    private UIFrameGraphic detailChip;
    private TextMeshProUGUI detailChipText;
    private UIFrameGraphic detailCard;
    private TextMeshProUGUI detailHours;
    private TextMeshProUGUI detailDue;
    private UIServiceBarGraphic detailBar;
    private TextMeshProUGUI detailRun;
    private TextMeshProUGUI detailEstimate;
    private TextMeshProUGUI detailPlan;
    private TextMeshProUGUI detailDifference;
    private MachinePartGroup selectedPart;

    /// <summary>The part whose detail is open, if any.</summary>
    public MachinePartGroup SelectedPart => selectedPart;

    public bool DetailOpen => selectedPart != null;

    /// <summary>Returns from the detail to the list.</summary>
    public Button BackButton => backButton;

    /// <summary>The part to mark on the model: the hovered row's, else the open detail's.</summary>
    public MachinePartGroup HighlightedPart => hoveredPart != null ? hoveredPart : selectedPart;

    public void ShowDetail(MachinePartGroup part)
    {
        Build();
        if (part == null || !TryGetStatus(part, out _))
        {
            ShowList();
            return;
        }

        selectedPart = part;
        SetView(true);
        RefreshDetail();
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
        listRect.gameObject.SetActive(!detail);
        detailHeader.gameObject.SetActive(detail);
        detailBody.gameObject.SetActive(detail);
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
        if (!TryGetStatus(selectedPart, out ServiceItemStatus status))
        {
            ShowList();
            return;
        }

        bool alert = status.State != ServiceState.OnTrack;
        Color state = StateColor(status.State);

        SetText(detailName, DisplayName(status.Part));
        SetText(detailTask, TaskLabel(status.Task));

        string chip = status.State switch
        {
            ServiceState.Overdue => "OVERDUE",
            ServiceState.DueSoon => "DUE SOON",
            _ => "ON TRACK"
        };
        SetText(detailChipText, chip);
        detailChipText.color = state;
        detailChip.SetColors(state, Color.clear);
        float chipWidth = Mathf.Ceil(detailChipText.GetPreferredValues(chip).x) + 24f;
        detailChip.rectTransform.sizeDelta = new Vector2(chipWidth, 36f);
        detailName.rectTransform.offsetMax = new Vector2(-(chipWidth + 12f), detailName.rectTransform.offsetMax.y);
        detailTask.rectTransform.offsetMax = new Vector2(-(chipWidth + 12f), detailTask.rectTransform.offsetMax.y);

        detailCard.SetColors(Color.clear, alert ? Color.Lerp(card, new Color(state.r, state.g, state.b, 1f), AlertTint) : card);
        detailHours.color = alert ? state : ink;
        bool overdue = status.HoursLeft <= 0d;
        SetText(detailHours, WithSuffix(
            Number(Math.Ceiling(Math.Abs(status.HoursLeft))),
            overdue ? " H OVERDUE" : " H TO SERVICE",
            40));
        if (schedule != null)
            SetText(detailDue, "DUE " + schedule.DateAfter(status.HoursLeft).ToString("d MMM", CultureInfo.InvariantCulture).ToUpperInvariant());

        detailBar.SetValues((float)status.RunHours, (float)status.PlanHours, (float)status.EstimateHours, state);
        SetText(detailRun, WithSuffix(Number(status.RunHours), " H", 75));
        SetText(detailEstimate, WithSuffix(Number(status.EstimateHours), " H", 75));
        SetText(detailPlan, WithSuffix(Number(status.PlanHours), " H", 75));

        // Earlier than plan reads as a warning: the part wears faster than
        // the manufacturer allows for.
        double difference = Math.Round(status.EstimateHours - status.PlanHours);
        string sign = difference > 0d ? "+" : difference < 0d ? "-" : string.Empty;
        detailDifference.color = difference < 0d ? warning : difference > 0d ? ink : Muted;
        SetText(detailDifference, WithSuffix(sign + Number(Math.Abs(difference)), " H", 75));
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
        PlaceAcrossMiddle(detailName.rectTransform, -1f, 35f);

        detailTask = CreateText("Task", detailHeader, 19f, false, Muted, TextAlignmentOptions.MidlineLeft);
        detailTask.characterSpacing = 3f;
        PlaceAcrossMiddle(detailTask.rectTransform, -31f, -1f);

        RectTransform chip = CreateRect("State", detailHeader);
        chip.anchorMin = chip.anchorMax = new Vector2(1f, 0.5f);
        chip.pivot = new Vector2(1f, 0.5f);
        chip.anchoredPosition = Vector2.zero;
        detailChip = AddFrame(chip, Color.clear, 2f, false);
        detailChipText = CreateText("Label", chip, 17f, true, normal, TextAlignmentOptions.Center);
        detailChipText.characterSpacing = 3f;
        detailChipText.overflowMode = TextOverflowModes.Overflow;
        Stretch(detailChipText.rectTransform, 0f, 0f, 0f, 0f);

        detailBody = CreateRect("Part Detail", root);
        detailBody.anchorMin = Vector2.zero;
        detailBody.anchorMax = Vector2.one;

        RectTransform cardRect = CreateRect("Card", detailBody);
        cardRect.anchorMin = new Vector2(0f, 1f);
        cardRect.anchorMax = Vector2.one;
        cardRect.pivot = new Vector2(0.5f, 1f);
        cardRect.offsetMin = new Vector2(ListLeft, -RowGap - DetailCardHeight);
        cardRect.offsetMax = new Vector2(-ListRight, -RowGap);
        detailCard = AddFrame(cardRect, card, 0f, false);

        RectTransform inner = CreateRect("Content", cardRect);
        Stretch(inner, CardPadding, CardPadding, 0f, 0f);

        detailHours = CreateText("Hours To Service", inner, 60f, true, ink, TextAlignmentOptions.MidlineLeft);
        PlaceBeside(detailHours.rectTransform, 0f, 0f, -12f, 90f);

        detailDue = CreateText("Due", inner, 20f, false, Muted, TextAlignmentOptions.MidlineLeft);
        detailDue.characterSpacing = 3f;
        PlaceBeside(detailDue.rectTransform, 0f, 0f, -100f, 30f);

        RectTransform bar = CreateRect("Bar", inner);
        PlaceBeside(bar, 0f, 0f, -146f, 28f);
        detailBar = bar.gameObject.AddComponent<UIServiceBarGraphic>();
        detailBar.raycastTarget = false;

        detailRun = AddFigure(inner, 0, "RUN");
        detailEstimate = AddFigure(inner, 1, "ESTIMATE");
        detailPlan = AddFigure(inner, 2, "PLAN");
        detailDifference = AddFigure(inner, 3, "VS PLAN");
    }

    /// <summary>A labelled number in one of four columns under the bar.</summary>
    private TextMeshProUGUI AddFigure(RectTransform parent, int column, string caption)
    {
        RectTransform cell = CreateRect(caption, parent);
        cell.anchorMin = new Vector2(column / 4f, 1f);
        cell.anchorMax = new Vector2((column + 1) / 4f, 1f);
        cell.pivot = new Vector2(0f, 1f);
        cell.offsetMin = new Vector2(0f, -250f);
        cell.offsetMax = new Vector2(0f, -190f);

        TextMeshProUGUI heading = CreateText("Label", cell, 16f, false, label, TextAlignmentOptions.MidlineLeft);
        heading.characterSpacing = 2f;
        heading.text = caption;
        PlaceBeside(heading.rectTransform, 0f, 4f, 0f, 26f);

        TextMeshProUGUI value = CreateText("Value", cell, 24f, true, ink, TextAlignmentOptions.MidlineLeft);
        PlaceBeside(value.rectTransform, 0f, 4f, -26f, 34f);
        return value;
    }

    /// <summary>Spans the header from right of the back arrow, between two heights about its middle.</summary>
    private static void PlaceAcrossMiddle(RectTransform rect, float bottom, float top)
    {
        rect.anchorMin = new Vector2(0f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.offsetMin = new Vector2(44f, bottom);
        rect.offsetMax = new Vector2(0f, top);
    }
}
