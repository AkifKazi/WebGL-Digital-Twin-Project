using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One detail panel: range buttons, pin and close in the header, a section
/// holding the graphs chosen on the controller, and a detail section that only
/// shows when something fills it. The panel knows its card and points at it;
/// the controller decides where the panel sits.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryDetailPanel : MonoBehaviour
{
    [Header("Parts")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("Hour, day, week, month and year, in that order.")]
    [SerializeField] private Button[] rangeButtons = Array.Empty<Button>();
    [SerializeField] private TMP_Text[] rangeLabels = Array.Empty<TMP_Text>();
    [SerializeField] private Graphic[] rangeUnderlines = Array.Empty<Graphic>();
    [SerializeField] private Button pinButton;
    [SerializeField] private Graphic pinBackground;
    [SerializeField] private UIIconGraphic pinIcon;
    [SerializeField] private Button closeButton;
    [Tooltip("Background of the graph section.")]
    [SerializeField] private RectTransform graphSection;
    [Tooltip("Where the graphs stack, inside the graph section.")]
    [SerializeField] private RectTransform graphArea;
    [Tooltip("Background of the detail section, shown only when there is detail text.")]
    [SerializeField] private RectTransform detailSection;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private UITriangleGraphic pointer;
    [SerializeField] private RectTransform linkAnchor;

    [Header("Layout")]
    [Tooltip("Distance from the panel's edge to the frame line its sprite draws. The glow sits outside the line.")]
    [SerializeField, Min(0f)] private float frameInset = 10f;

    [Tooltip("Distance from the top of the panel to the graph section, header included.")]
    [SerializeField, Min(0f)] private float headerHeight = 59f;

    [Tooltip("Distance from the panel's sides and bottom to its sections.")]
    [SerializeField, Min(0f)] private float sectionInset = 23f;

    [Tooltip("Space between the graph section and the detail section.")]
    [SerializeField, Min(0f)] private float sectionGap = 10f;

    [Tooltip("Space between a section's edge and its content: x on the sides, y above and below.")]
    [SerializeField] private Vector2 sectionPadding = new(16f, 16f);

    [Tooltip("Space between two graphs.")]
    [SerializeField, Min(0f)] private float graphSpacing = 24f;

    [Tooltip("Closest the pointer gets to the top or bottom of the panel.")]
    [SerializeField, Min(0f)] private float pointerInset = 34f;

    [Header("Colours")]
    [SerializeField] private Color rangeSelected = new(0.918f, 0.992f, 1f, 1f);
    [SerializeField] private Color rangeIdle = new(0.408f, 0.651f, 0.686f, 1f);
    [SerializeField] private Color rangeUnavailable = new(0.408f, 0.651f, 0.686f, 0.3f);
    [SerializeField] private Color pinIdle = new(0.212f, 0.929f, 1f, 1f);
    [SerializeField] private Color pinActive = new(0.04f, 0.1f, 0.13f, 1f);

    private readonly List<TelemetryGraphView> graphViews = new();
    private readonly List<TelemetryHistoryBucket> buckets = new();
    private readonly bool[] rangeAvailable = { true, true, true, true, true };
    private ITelemetryHistoryProvider history;
    private ITelemetryInsightProvider insight;

    public event Action<TelemetryDetailPanel> PinToggled;
    public event Action<TelemetryDetailPanel> CloseRequested;

    public PerformanceStatCardView Card { get; private set; }
    /// <summary>The source the panel shows. Kept apart from the card, because cards are pooled and rebound.</summary>
    public PerformanceStatSource Source { get; private set; }
    public StatRailSide Side { get; private set; }
    public bool IsPinned { get; private set; }
    public TelemetryHistoryRange Range { get; private set; }
    public RectTransform RectTransform => (RectTransform)transform;
    public float Height => RectTransform.sizeDelta.y;

    // Placement state, owned by the controller.
    internal float CurrentTop;
    internal float TopVelocity;
    internal bool SnapNextLayout = true;
    internal float Alpha;

    public void Initialise(
        IReadOnlyList<TelemetryGraphKind> kinds,
        TelemetryGraphStyle style,
        TelemetryHistoryRange range,
        ITelemetryHistoryProvider historyProvider,
        ITelemetryInsightProvider insightProvider)
    {
        history = historyProvider;
        insight = insightProvider;
        Range = range;

        Inset(graphArea, sectionPadding);
        if (detailText != null)
            Inset(detailText.rectTransform, sectionPadding);

        if (!graphArea.TryGetComponent(out VerticalLayoutGroup stack))
            stack = graphArea.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.spacing = graphSpacing;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        foreach (TelemetryGraphKind kind in kinds)
            graphViews.Add(TelemetryGraphView.Create(kind, graphArea, style));

        for (int i = 0; i < rangeButtons.Length && i < TelemetryHistoryRanges.All.Length; i++)
        {
            TelemetryHistoryRange buttonRange = TelemetryHistoryRanges.All[i];
            if (rangeButtons[i] != null)
                rangeButtons[i].onClick.AddListener(() => SelectRange(buttonRange));
        }
        if (pinButton != null)
            pinButton.onClick.AddListener(() => PinToggled?.Invoke(this));
        if (closeButton != null)
            closeButton.onClick.AddListener(() => CloseRequested?.Invoke(this));

        SetPinned(false);
        SetAlpha(0f);
        ApplyRangeVisuals();
    }

    public void Bind(PerformanceStatCardView card)
    {
        if (Card != null && Card != card)
            Card.SetConnectionPointOverride(null);

        Card = card;
        Source = card != null ? card.BoundSource : null;
        Side = card != null ? card.RailSide : StatRailSide.Left;

        bool left = Side == StatRailSide.Left;
        if (pointer != null)
        {
            pointer.PointLeft = left;
            AnchorToEdge(pointer.rectTransform, left ? 0f : 1f, left ? 1f : 0f);
        }
        if (linkAnchor != null)
            AnchorToEdge(linkAnchor, left ? 1f : 0f, 0.5f);
        if (card != null)
            card.SetConnectionPointOverride(linkAnchor);

        Refresh();
    }

    public void Unbind()
    {
        if (Card != null)
            Card.SetConnectionPointOverride(null);
        Card = null;
    }

    public void SetPinned(bool pinned)
    {
        IsPinned = pinned;
        if (pinBackground != null)
            pinBackground.enabled = pinned;
        if (pinIcon != null)
            pinIcon.color = pinned ? pinActive : pinIdle;
    }

    public void SelectRange(TelemetryHistoryRange range)
    {
        if (!rangeAvailable[(int)range])
            return;
        Range = range;
        Refresh();
    }

    /// <summary>Points at the card from <paramref name="yFromTop"/> down the panel's frame line.</summary>
    public void SetPointer(float yFromTop, Color color)
    {
        float y = Mathf.Clamp(yFromTop, pointerInset, Mathf.Max(pointerInset, Height - pointerInset));
        bool left = Side == StatRailSide.Left;
        if (pointer != null)
        {
            // The pointer's base overlaps the frame line by a pixel so the two read as one.
            float x = frameInset + 1f;
            pointer.rectTransform.anchoredPosition = new Vector2(left ? x : -x, -y);
            pointer.color = color;
        }
        // The leader line leaves from the frame line on the model's side.
        if (linkAnchor != null)
            linkAnchor.anchoredPosition = new Vector2(left ? -frameInset : frameInset, -y);
    }

    public void SetWidth(float width)
    {
        if (!Mathf.Approximately(RectTransform.sizeDelta.x, width))
        {
            RectTransform.sizeDelta = new Vector2(width, Height);
            UpdateLayout();
        }
    }

    public void SetAlpha(float alpha)
    {
        Alpha = alpha;
        if (canvasGroup == null)
            return;
        canvasGroup.alpha = alpha;
        canvasGroup.blocksRaycasts = alpha > 0.5f;
        canvasGroup.interactable = alpha > 0.5f;
    }

    /// <summary>Queries the history again and redraws every graph.</summary>
    public void Refresh()
    {
        if (Source == null || history == null || !history.IsReady)
            return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < TelemetryHistoryRanges.All.Length; i++)
            rangeAvailable[i] = history.TryGetHistory(Source, TelemetryHistoryRanges.All[i], now, buckets);

        if (!rangeAvailable[(int)Range])
        {
            int first = Array.IndexOf(rangeAvailable, true);
            if (first >= 0)
                Range = TelemetryHistoryRanges.All[first];
        }

        history.TryGetHistory(Source, Range, now, buckets);
        TelemetryGraphData data = new(Source, Range, buckets);
        foreach (TelemetryGraphView view in graphViews)
            view.Render(data);

        string text = null;
        bool hasDetail = insight != null &&
                         insight.TryGetInsight(Source, Range, buckets, out text) &&
                         !string.IsNullOrWhiteSpace(text);
        if (detailSection != null)
            detailSection.gameObject.SetActive(hasDetail);
        if (hasDetail && detailText != null)
            detailText.text = text;

        ApplyRangeVisuals();
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        float width = RectTransform.sizeDelta.x;
        float graphsHeight = 0f;
        foreach (TelemetryGraphView view in graphViews)
            graphsHeight += view.PreferredHeight;
        if (graphViews.Count > 1)
            graphsHeight += graphSpacing * (graphViews.Count - 1);

        float y = headerHeight;
        float graphSectionHeight = graphsHeight + sectionPadding.y * 2f;
        PlaceSection(graphSection, y, graphSectionHeight);
        y += graphSectionHeight;

        if (detailSection != null && detailSection.gameObject.activeSelf && detailText != null)
        {
            float textWidth = width - (sectionInset + sectionPadding.x) * 2f;
            float textHeight = detailText.GetPreferredValues(detailText.text, textWidth, 0f).y;
            float detailHeight = textHeight + sectionPadding.y * 2f;
            y += sectionGap;
            PlaceSection(detailSection, y, detailHeight);
            y += detailHeight;
        }

        RectTransform.sizeDelta = new Vector2(width, y + sectionInset);
    }

    private void PlaceSection(RectTransform rect, float top, float height)
    {
        if (rect == null)
            return;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(sectionInset, -(top + height));
        rect.offsetMax = new Vector2(-sectionInset, -top);
    }

    private static void Inset(RectTransform rect, Vector2 padding)
    {
        if (rect == null)
            return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(padding.x, padding.y);
        rect.offsetMax = new Vector2(-padding.x, -padding.y);
    }

    private void ApplyRangeVisuals()
    {
        for (int i = 0; i < TelemetryHistoryRanges.All.Length; i++)
        {
            bool selected = i == (int)Range;
            bool available = rangeAvailable[i];
            if (i < rangeLabels.Length && rangeLabels[i] != null)
                rangeLabels[i].color = selected ? rangeSelected : available ? rangeIdle : rangeUnavailable;
            if (i < rangeUnderlines.Length && rangeUnderlines[i] != null)
                rangeUnderlines[i].enabled = selected;
            if (i < rangeButtons.Length && rangeButtons[i] != null)
                rangeButtons[i].interactable = available;
        }
    }

    private static void AnchorToEdge(RectTransform rect, float anchorX, float pivotX)
    {
        rect.anchorMin = new Vector2(anchorX, 1f);
        rect.anchorMax = new Vector2(anchorX, 1f);
        rect.pivot = new Vector2(pivotX, 0.5f);
    }

    private void OnDestroy() => Unbind();
}
