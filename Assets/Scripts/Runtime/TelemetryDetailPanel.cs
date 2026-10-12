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
///
/// On a phone held upright the same panel is a bottom sheet (see
/// <see cref="UseAsSheet"/>): no pin, no pointer or leader line to the card,
/// and a close button sized for a thumb.
///
/// Bound to a card, it records with <see cref="CardFocus"/> that the card's
/// detail is open, and, beside the rails, where the card's connector meets it
/// and where its leader line leaves it.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryDetailPanel : MonoBehaviour, ICardDetailLink
{
    [Header("Parts")]
    [SerializeField] private CanvasGroup canvasGroup;
    [Tooltip("Hour, day, week, month and year, in that order.")]
    [SerializeField] private Button[] rangeButtons = Array.Empty<Button>();
    [SerializeField] private TMP_Text[] rangeLabels = Array.Empty<TMP_Text>();
    [SerializeField] private Graphic[] rangeUnderlines = Array.Empty<Graphic>();
    [SerializeField] private Button pinButton;
    [SerializeField] private Graphic pinBackground;
    [SerializeField] private Graphic pinIcon;
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
    [SerializeField, Min(0f)] private float headerHeight = 66f;

    [Tooltip("Distance from the panel's sides and bottom to its sections.")]
    [SerializeField, Min(0f)] private float sectionInset = 25f;

    [Tooltip("Space between the graph section and the detail section.")]
    [SerializeField, Min(0f)] private float sectionGap = 10f;

    [Tooltip("Space between a section's edge and its content: x on the sides, y above and below.")]
    [SerializeField] private Vector2 sectionPadding = new(16f, 16f);

    [Tooltip("Space between two graphs.")]
    [SerializeField, Min(0f)] private float graphSpacing = 24f;

    [Tooltip("Closest the pointer gets to the ends of the edge it sits on.")]
    [SerializeField, Min(0f)] private float pointerInset = 34f;

    [Header("Header")]
    [Tooltip("Height of the range control and of the pin and close buttons. The sheet's close button " +
             "can be taller, for a thumb; then everything in the header takes its height.")]
    [SerializeField, Min(16f)] private float headerControlHeight = 40f;

    [Tooltip("Narrowest and widest a range button gets. Between the two, they share the room the pin " +
             "and close buttons and the gap before them leave.")]
    [SerializeField] private Vector2 rangeCellWidth = new(40f, 52f);

    [Tooltip("Space between the range control and the pin button.")]
    [SerializeField, Min(0f)] private float headerGap = 36f;

    [Tooltip("Space between the pin and close buttons.")]
    [SerializeField, Min(0f)] private float headerButtonSpacing = 6f;

    [Header("Colours")]
    [SerializeField] private Color rangeSelected = new(0.918f, 0.992f, 1f, 1f);
    [SerializeField] private Color rangeIdle = new(0.408f, 0.651f, 0.686f, 1f);
    [SerializeField] private Color rangeUnavailable = new(0.408f, 0.651f, 0.686f, 0.3f);
    [SerializeField] private Color pinIdle = new(0.212f, 0.929f, 1f, 1f);
    [SerializeField] private Color pinActive = new(0.04f, 0.1f, 0.13f, 1f);
    [Tooltip("The pointer towards the card. One colour whatever the card's alarm state, " +
             "matching the panel frame.")]
    [SerializeField] private Color pointerColor = new(0.08f, 0.9f, 1f, 1f);

    private readonly List<TelemetryGraphView> graphViews = new();
    private readonly List<TelemetryHistoryBucket> buckets = new();
    private readonly bool[] rangeAvailable = { true, true, true, true, true };
    private ITelemetryHistoryProvider history;
    private ITelemetryInsightProvider insight;
    private ITelemetryServiceLifeProvider serviceLife;

    public event Action<TelemetryDetailPanel> PinToggled;
    public event Action<TelemetryDetailPanel> CloseRequested;

    public PerformanceStatCardView Card { get; private set; }
    /// <summary>The source the panel shows. Kept apart from the card, because cards are pooled and rebound.</summary>
    public PerformanceStatSource Source { get; private set; }
    public StatRailSide Side { get; private set; }
    public bool IsPinned { get; private set; }
    public bool IsSheet { get; private set; }
    public TelemetryHistoryRange Range { get; private set; }
    public RectTransform RectTransform => (RectTransform)transform;
    public float Height => RectTransform.sizeDelta.y;
    public float Width => RectTransform.sizeDelta.x;

    /// <summary>True when the panel opened from a card row, so it sits below or above the cards.</summary>
    public bool OnRow => Side is StatRailSide.Top or StatRailSide.Bottom;

    // Placement state, owned by the controller: the panel's top edge beside a
    // column, its left edge beside a row.
    internal float CurrentAlong;
    internal float AlongVelocity;
    internal bool SnapNextLayout = true;
    internal float Alpha;

    RectTransform ICardDetailLink.CardSidePoint => pointer != null ? pointer.rectTransform : null;
    RectTransform ICardDetailLink.ModelSidePoint => linkAnchor;
    float ICardDetailLink.Alpha => Alpha;

    private Vector2 pointerSize;
    private bool pointerSizeCaptured;
    private float headerLaidOutFor = float.NaN;

    public void Initialise(
        IReadOnlyList<TelemetryGraphKind> kinds,
        IReadOnlyList<TelemetryKeyFigure> keyFigures,
        TelemetryKeyFigureLayout keyFigureLayout,
        TelemetryGraphStyle style,
        TelemetryHistoryRange range,
        ITelemetryHistoryProvider historyProvider,
        ITelemetryInsightProvider insightProvider,
        ITelemetryServiceLifeProvider serviceLifeProvider)
    {
        history = historyProvider;
        insight = insightProvider;
        serviceLife = serviceLifeProvider;
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
            graphViews.Add(TelemetryGraphView.Create(kind, graphArea, style, keyFigures, keyFigureLayout));

        for (int i = 0; i < rangeButtons.Length && i < TelemetryHistoryRanges.All.Length; i++)
        {
            TelemetryHistoryRange buttonRange = TelemetryHistoryRanges.All[i];
            if (rangeButtons[i] != null)
                rangeButtons[i].onClick.AddListener(() => SelectRange(buttonRange));
        }
        for (int i = 0; i < rangeLabels.Length && i < TelemetryHistoryRanges.All.Length; i++)
        {
            TelemetryHistoryRange labelRange = TelemetryHistoryRanges.All[i];
            UITextBinding.Bind(rangeLabels[i], TelemetryHistoryRanges.ShortLabelKey(labelRange),
                TelemetryHistoryRanges.ShortLabel(labelRange));
            UITextBinding.AllowShrinkToFit(rangeLabels[i], 0.5f);
        }
        if (pinButton != null)
            pinButton.onClick.AddListener(() => PinToggled?.Invoke(this));
        if (closeButton != null)
            closeButton.onClick.AddListener(() => CloseRequested?.Invoke(this));

        SetPinned(false);
        SetAlpha(0f);
        ApplyRangeVisuals();
    }

    /// <summary>
    /// Makes the panel a bottom sheet: the pin and the pointer go, the card
    /// keeps its own leader line, and the close button grows to
    /// <paramref name="closeSize"/> with its touch area reaching
    /// <paramref name="closeHitPadding"/> further on every side.
    /// </summary>
    public void UseAsSheet(float closeSize, float closeHitPadding)
    {
        IsSheet = true;
        headerLaidOutFor = float.NaN;
        if (pinButton != null)
            pinButton.gameObject.SetActive(false);
        if (pointer != null)
            pointer.gameObject.SetActive(false);
        // A sheet is not beside the rails: no connector, and the line leaves from the card.
        if (Card != null)
        {
            CardFocus.CloseDetail(Card, this);
            CardFocus.OpenDetail(Card, null);
        }
        if (closeButton == null)
            return;
        RectTransform close = (RectTransform)closeButton.transform;
        close.sizeDelta = new Vector2(closeSize, closeSize);
        // The button's own clear image takes the pointer.
        if (closeButton.TryGetComponent(out Graphic hit))
            hit.raycastPadding = -Vector4.one * closeHitPadding;
    }

    public void Bind(PerformanceStatCardView card)
    {
        if (Card != null && Card != card)
            CloseDetail();

        Card = card;
        Source = card != null ? card.BoundSource : null;
        Side = card != null ? card.RailSide : StatRailSide.Left;

        OrientToSide();
        if (card != null)
            CardFocus.OpenDetail(card, IsSheet ? null : this);

        Refresh();
    }

    /// <summary>
    /// The pointer goes on the edge facing the card and points at it; the
    /// leader line leaves from the opposite edge, the model's side. A panel
    /// beside a column has them on its left and right edges, one below or
    /// above a row on its top and bottom edges.
    /// </summary>
    private void OrientToSide()
    {
        if (pointer != null)
        {
            RectTransform rect = pointer.rectTransform;
            if (!pointerSizeCaptured)
            {
                // Authored pointing sideways: wide as the tip is deep, tall as its base.
                pointerSize = rect.sizeDelta;
                pointerSizeCaptured = true;
            }

            switch (Side)
            {
                case StatRailSide.Top:
                    pointer.Direction = UITriangleGraphic.PointDirection.Up;
                    SetAnchors(rect, new Vector2(0f, 1f), new Vector2(0.5f, 0f));
                    rect.sizeDelta = new Vector2(pointerSize.y, pointerSize.x);
                    break;
                case StatRailSide.Bottom:
                    pointer.Direction = UITriangleGraphic.PointDirection.Down;
                    SetAnchors(rect, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
                    rect.sizeDelta = new Vector2(pointerSize.y, pointerSize.x);
                    break;
                case StatRailSide.Right:
                    pointer.Direction = UITriangleGraphic.PointDirection.Right;
                    SetAnchors(rect, new Vector2(1f, 1f), new Vector2(0f, 0.5f));
                    rect.sizeDelta = pointerSize;
                    break;
                default:
                    pointer.Direction = UITriangleGraphic.PointDirection.Left;
                    SetAnchors(rect, new Vector2(0f, 1f), new Vector2(1f, 0.5f));
                    rect.sizeDelta = pointerSize;
                    break;
            }
        }

        if (linkAnchor != null)
        {
            switch (Side)
            {
                case StatRailSide.Top:
                    SetAnchors(linkAnchor, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f));
                    break;
                case StatRailSide.Bottom:
                    SetAnchors(linkAnchor, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f));
                    break;
                case StatRailSide.Right:
                    SetAnchors(linkAnchor, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f));
                    break;
                default:
                    SetAnchors(linkAnchor, new Vector2(1f, 1f), new Vector2(0.5f, 0.5f));
                    break;
            }
        }
    }

    public void Unbind()
    {
        CloseDetail();
        Card = null;
    }

    private void CloseDetail()
    {
        if (Card == null)
            return;
        CardFocus.CloseDetail(Card, IsSheet ? null : this);
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

    /// <summary>
    /// Points at the card from <paramref name="offset"/> along the panel's
    /// frame line: down from the top beside a column, across from the left
    /// below or above a row.
    /// </summary>
    public void SetPointer(float offset)
    {
        float length = OnRow ? Width : Height;
        float along = Mathf.Clamp(offset, pointerInset, Mathf.Max(pointerInset, length - pointerInset));
        // The pointer's base overlaps the frame line by a pixel so the two read as one.
        float edge = frameInset + 1f;
        if (pointer != null)
        {
            pointer.rectTransform.anchoredPosition = Side switch
            {
                StatRailSide.Top => new Vector2(along, -edge),
                StatRailSide.Bottom => new Vector2(along, edge),
                StatRailSide.Right => new Vector2(-edge, -along),
                _ => new Vector2(edge, -along)
            };
            pointer.color = pointerColor;
        }
        // The leader line leaves from the frame line on the model's side.
        if (linkAnchor != null)
        {
            linkAnchor.anchoredPosition = Side switch
            {
                StatRailSide.Top => new Vector2(along, frameInset),
                StatRailSide.Bottom => new Vector2(along, -frameInset),
                StatRailSide.Right => new Vector2(frameInset, -along),
                _ => new Vector2(-frameInset, -along)
            };
        }
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

        // Every probe builds that range's buckets in full. The shown range is
        // probed last so its buckets are the ones left in the list, which saves
        // querying it a second time for the graphs below.
        int selected = (int)Range;
        for (int i = 0; i < TelemetryHistoryRanges.All.Length; i++)
        {
            if (i != selected)
                rangeAvailable[i] = history.TryGetHistory(Source, TelemetryHistoryRanges.All[i], now, buckets);
        }

        rangeAvailable[selected] = history.TryGetHistory(Source, Range, now, buckets);

        if (!rangeAvailable[selected])
        {
            int first = Array.IndexOf(rangeAvailable, true);
            if (first >= 0)
            {
                Range = TelemetryHistoryRanges.All[first];
                history.TryGetHistory(Source, Range, now, buckets);
            }
        }
        TelemetryGraphData data = new(Source, Range, buckets, serviceLife);
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
        UpdateHeaderLayout(width);
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

    /// <summary>
    /// The header across the sections' width: the range control on their left
    /// edge, close on their right with pin beside it, all one height. The range
    /// buttons widen to take what room the panel has, short of the gap before pin.
    /// </summary>
    private void UpdateHeaderLayout(float width)
    {
        if (Mathf.Approximately(headerLaidOutFor, width) || rangeButtons.Length == 0 || rangeButtons[0] == null)
            return;
        RectTransform ranges = rangeButtons[0].transform.parent as RectTransform;
        RectTransform header = ranges != null ? ranges.parent as RectTransform : null;
        if (header == null)
            return;
        headerLaidOutFor = width;

        header.offsetMin = new Vector2(sectionInset, header.offsetMin.y);
        header.offsetMax = new Vector2(-sectionInset, header.offsetMax.y);

        float height = headerControlHeight;
        float buttons = 0f;
        if (closeButton != null)
        {
            RectTransform close = (RectTransform)closeButton.transform;
            if (IsSheet)
                height = Mathf.Max(height, close.sizeDelta.y);
            close.anchoredPosition = new Vector2(0f, close.anchoredPosition.y);
            close.sizeDelta = new Vector2(height, height);
            SizeIcon(close, height);
            buttons += height;
        }
        if (pinButton != null && pinButton.gameObject.activeSelf)
        {
            RectTransform pin = (RectTransform)pinButton.transform;
            pin.anchoredPosition = new Vector2(-(buttons + headerButtonSpacing), pin.anchoredPosition.y);
            pin.sizeDelta = new Vector2(height, height);
            SizeIcon(pin, height);
            buttons += headerButtonSpacing + height;
        }

        HorizontalLayoutGroup row = ranges.GetComponent<HorizontalLayoutGroup>();
        float padX = row != null ? row.padding.horizontal : 0f;
        float padY = row != null ? row.padding.vertical : 0f;
        float spacing = row != null ? row.spacing : 0f;
        int count = rangeButtons.Length;
        float room = width - sectionInset * 2f - buttons - headerGap - padX - spacing * (count - 1);
        float cell = Mathf.Clamp(room / count, rangeCellWidth.x, Mathf.Max(rangeCellWidth.x, rangeCellWidth.y));
        foreach (Button button in rangeButtons)
        {
            if (button == null || !button.TryGetComponent(out LayoutElement size))
                continue;
            size.minWidth = size.preferredWidth = cell;
            size.minHeight = size.preferredHeight = height - padY;
        }
        ranges.anchoredPosition = new Vector2(0f, ranges.anchoredPosition.y);
        ranges.sizeDelta = new Vector2(cell * count + spacing * (count - 1) + padX, height);
    }

    /// <summary>An icon button's glyph, a little over half its height, centred.</summary>
    private static void SizeIcon(RectTransform button, float height)
    {
        if (button.Find("Icon") is RectTransform icon)
            icon.sizeDelta = -Vector2.one * Mathf.Round(height * 0.45f);
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
            {
                rangeButtons[i].interactable = available;
                // Ranges drawn as a segmented control show the range on show as selected.
                if (rangeButtons[i].targetGraphic is UIShape segment)
                    segment.State = selected ? UIState.Selected : available ? UIState.Idle : UIState.Disabled;
            }
        }
    }

    private static void SetAnchors(RectTransform rect, Vector2 anchor, Vector2 pivot)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
    }

    private void OnDestroy() => Unbind();
}
