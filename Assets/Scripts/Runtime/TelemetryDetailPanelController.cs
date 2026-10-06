using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Opens telemetry detail panels in the wide layout, on a detail rail of their
/// own between the card rails and the model: beside a column of cards, or
/// below the top rows and above the bottom rows in a desktop window taller
/// than it is wide. The panel never takes a card rail.
///
/// A clean click on a card opens its panel. On a touch screen a tap only
/// focuses the card and a second tap on it opens the panel, so tapping round
/// the cards does not keep covering the model; once a panel is open, a tap on
/// another card moves it. Clicking another card glides that panel to it; a
/// pinned panel stays put and the click opens a new one. A clean click
/// anywhere else, Esc, or the close button closes the unpinned panel. Dragging
/// to turn the camera never closes anything.
///
/// Whether panels open at all is set per device in the
/// <see cref="TelemetryLayoutSettings"/>; the responsive shell passes it on.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryDetailPanelController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private StatRailManager railManager;
    [SerializeField] private TelemetryDetailPanel panelPrefab;

    [Tooltip("Supplies the history the graphs draw: the Telemetry History Recorder, " +
             "or any component implementing ITelemetryHistoryProvider.")]
    [SerializeField] private MonoBehaviour historyProvider;

    [Tooltip("Optional. Supplies text for the detail section (a recommendation, for " +
             "example). Empty, the section stays hidden.")]
    [SerializeField] private MonoBehaviour insightProvider;

    [Tooltip("Optional. Supplies the hours until a sensor's part is due for service: the " +
             "Telemetry Service Schedule, or any component implementing " +
             "ITelemetryServiceLifeProvider. Empty, Time To Service shows a dash.")]
    [SerializeField] private MonoBehaviour serviceLifeProvider;

    [Header("Graphs")]
    [Tooltip("Graphs in each panel, top to bottom.")]
    [SerializeField] private List<TelemetryGraphKind> graphs = new()
    {
        TelemetryGraphKind.StatusTimeline,
        TelemetryGraphKind.KeyFigures,
        TelemetryGraphKind.LineChart
    };

    [Tooltip("What the Key Figures graph shows, left to right. The first two that are not " +
             "None are shown, each in half of the panel's width.")]
    [SerializeField] private List<TelemetryKeyFigure> keyFigures = new()
    {
        TelemetryKeyFigure.Average,
        TelemetryKeyFigure.TimeToService
    };

    [Tooltip("Equal Halves: each key figure keeps half of the row. Make Room For Long Values: " +
             "a first figure too long for its half pushes the second to the right, as far as " +
             "the second still fits, before either shrinks.")]
    [SerializeField] private TelemetryKeyFigureLayout keyFigureLayout = TelemetryKeyFigureLayout.MakeRoomForLongValues;

    [SerializeField] private TelemetryHistoryRange defaultRange = TelemetryHistoryRange.Hour;
    [SerializeField] private TelemetryGraphStyle graphStyle = new();

    [Header("Layout")]
    [Tooltip("Gap between the card rail and the panel. The panel sprite's outer glow adds " +
             "about 10 more before its frame line.")]
    [SerializeField, Min(0f)] private float railGap = 12f;

    [Tooltip("Gap between two panels on the same side, glow to glow.")]
    [SerializeField, Min(0f)] private float stackGap = 2f;

    [Tooltip("Closest a panel gets to the edges of the screen area.")]
    [SerializeField, Min(0f)] private float edgeMargin = 4f;

    [Tooltip("Width of a panel opened from a card row, below the top rows or above the bottom ones.")]
    [SerializeField, Min(200f)] private float rowPanelWidth = 420f;

    [Tooltip("A panel is never drawn narrower than this, even where the rails leave less room.")]
    [SerializeField, Min(120f)] private float minimumPanelWidth = 240f;

    [Tooltip("Seconds a panel takes to glide to a newly clicked card.")]
    [SerializeField, Min(0.01f)] private float glideTime = 0.22f;

    [SerializeField, Min(0.01f)] private float fadeTime = 0.15f;

    [SerializeField, Min(1)] private int maximumPinnedPanels = 3;

    [Tooltip("Seconds between graph refreshes while a panel is open.")]
    [SerializeField, Min(0.1f)] private float refreshInterval = 1f;

    private static readonly StatRailSide[] Sides =
        { StatRailSide.Left, StatRailSide.Right, StatRailSide.Top, StatRailSide.Bottom };

    private readonly List<TelemetryDetailPanel> panels = new();
    private readonly List<TelemetryDetailPanel> closing = new();
    private readonly List<TelemetryDetailPanel> sidePanels = new();
    private readonly List<float> cardCentres = new();
    private readonly List<float> targets = new();
    private readonly Vector3[] corners = new Vector3[4];

    private TelemetryDetailPanel floating;
    private float nextRefreshTime;
    private bool warnedNotReady;
    private bool searchedForInsight;
    private bool featureEnabled = true;

    /// <summary>True while a card would open its panel: switched on, set up, and with history to show.</summary>
    public bool IsReady =>
        featureEnabled && isActiveAndEnabled && railManager != null && railManager.DetailRailActive &&
        panelPrefab != null && History != null && History.IsReady;

    /// <summary>Whether panels open on this device, from the Telemetry Layout Settings.</summary>
    public bool FeatureEnabled => featureEnabled;
    public IReadOnlyList<TelemetryDetailPanel> OpenPanels => panels;
    public TelemetryDetailPanel FloatingPanel => floating;

    private ITelemetryHistoryProvider History => historyProvider as ITelemetryHistoryProvider;
    private ITelemetryInsightProvider Insight
    {
        get
        {
            // Without an assigned provider, AI insights sent by the page (through
            // the AI data table) fill the detail section. Looked up once.
            if (insightProvider == null && !searchedForInsight)
            {
                searchedForInsight = true;
                insightProvider = FindAnyObjectByType<TelemetryAiDataTable>();
            }
            return insightProvider as ITelemetryInsightProvider;
        }
    }
    private ITelemetryServiceLifeProvider ServiceLife => serviceLifeProvider as ITelemetryServiceLifeProvider;
    private RectTransform Layer => (RectTransform)transform;

    private void OnEnable()
    {
        PerformanceStatCardView.Clicked += HandleCardClicked;
        PerformanceStatCardView.DetailRequested += HandleDetailRequested;
        TelemetryCardFocusReleaser.Dismissed += DismissUnpinned;
        if (railManager != null)
            railManager.LayoutRebuilt += HandleLayoutRebuilt;
    }

    private void OnDisable()
    {
        PerformanceStatCardView.Clicked -= HandleCardClicked;
        PerformanceStatCardView.DetailRequested -= HandleDetailRequested;
        TelemetryCardFocusReleaser.Dismissed -= DismissUnpinned;
        if (railManager != null)
            railManager.LayoutRebuilt -= HandleLayoutRebuilt;

        foreach (TelemetryDetailPanel panel in panels)
            DestroyPanel(panel);
        foreach (TelemetryDetailPanel panel in closing)
            DestroyPanel(panel);
        panels.Clear();
        closing.Clear();
        floating = null;
    }

    /// <summary>
    /// Turns panels on or off for this device. Off closes any that are open;
    /// the rails then keep no room for them.
    /// </summary>
    public void SetFeatureEnabled(bool enabled)
    {
        featureEnabled = enabled;
        if (!enabled)
            CloseAll();
    }

    private void CloseAll()
    {
        for (int i = panels.Count - 1; i >= 0; i--)
            Close(panels[i]);
    }

    // -----------------------------------------------------------------------
    // Clicks
    // -----------------------------------------------------------------------

    private void HandleCardClicked(PerformanceStatCardView card)
    {
        if (!AcceptsCard(card))
            return;

        // A tap only focuses: a second tap opens its panel.
        if (card.LastPressWasTouch && floating == null)
            return;

        ShowPanel(card);
    }

    private void HandleDetailRequested(PerformanceStatCardView card)
    {
        if (AcceptsCard(card) && card.IsFocused)
            ShowPanel(card);
    }

    private bool AcceptsCard(PerformanceStatCardView card)
    {
        if (!IsReady)
        {
            if (featureEnabled && !warnedNotReady && isActiveAndEnabled)
            {
                warnedNotReady = true;
                Debug.LogWarning(
                    "The telemetry detail panel is switched on but not ready: it needs the rail " +
                    "manager, its detail rail, the panel prefab and a ready history provider.", this);
            }
            return false;
        }

        return railManager.OwnsCard(card);
    }

    /// <summary>Opens the floating panel on <paramref name="card"/>, or moves it there.</summary>
    private void ShowPanel(PerformanceStatCardView card)
    {
        card.HoldFocus();

        foreach (TelemetryDetailPanel panel in panels)
        {
            if (panel.IsPinned && panel.Card == card)
                return; // Already open, pinned.
        }

        if (floating == null)
        {
            floating = Open(card);
            return;
        }

        bool sideChanged = floating.Side != card.RailSide;
        floating.Bind(card);
        if (sideChanged)
        {
            floating.SnapNextLayout = true;
            floating.SetAlpha(0f);
        }
    }

    private void Update()
    {
        if (panels.Count > 0 && !IsReady)
            CloseAll();

        if (panels.Count > 0 && Time.unscaledTime >= nextRefreshTime)
        {
            nextRefreshTime = Time.unscaledTime + refreshInterval;
            foreach (TelemetryDetailPanel panel in panels)
                panel.Refresh();
        }
    }

    /// <summary>Closes the unpinned panel. Pinned panels stay.</summary>
    public void DismissUnpinned()
    {
        if (floating != null)
            Close(floating);
    }

    // -----------------------------------------------------------------------
    // Opening, pinning, closing
    // -----------------------------------------------------------------------

    private TelemetryDetailPanel Open(PerformanceStatCardView card)
    {
        TelemetryDetailPanel panel = Instantiate(panelPrefab, transform);
        panel.name = panelPrefab.name;
        panel.Initialise(graphs, keyFigures, keyFigureLayout, graphStyle, defaultRange, History, Insight, ServiceLife);
        panel.PinToggled += HandlePinToggled;
        panel.CloseRequested += HandleCloseRequested;
        panels.Add(panel);
        panel.Bind(card);
        panel.SnapNextLayout = true;
        return panel;
    }

    private void HandlePinToggled(TelemetryDetailPanel panel)
    {
        if (!panel.IsPinned)
        {
            panel.SetPinned(true);
            if (floating == panel)
                floating = null;

            int pinned = 0;
            foreach (TelemetryDetailPanel open in panels)
                pinned += open.IsPinned ? 1 : 0;
            for (int i = 0; i < panels.Count && pinned > maximumPinnedPanels; i++)
            {
                if (panels[i].IsPinned && panels[i] != panel)
                {
                    Close(panels[i]);
                    pinned--;
                    i--;
                }
            }
            return;
        }

        // Unpinning makes this the floating panel; there is only ever one.
        panel.SetPinned(false);
        if (floating != null && floating != panel)
            Close(floating);
        floating = panel;
    }

    private void HandleCloseRequested(TelemetryDetailPanel panel)
    {
        if (panel.Card != null && panel.Card.IsFocused)
            panel.Card.ReleaseFocus();
        Close(panel);
    }

    private void Close(TelemetryDetailPanel panel)
    {
        if (panel == null || !panels.Remove(panel))
            return;
        if (floating == panel)
            floating = null;
        panel.Unbind();
        closing.Add(panel);
    }

    private void DestroyPanel(TelemetryDetailPanel panel)
    {
        if (panel == null)
            return;
        panel.Unbind();
        Destroy(panel.gameObject);
    }

    private void HandleLayoutRebuilt()
    {
        // A rebuild keeps a card on screen, but it can change rail or side (the
        // rails turned into rows, say). A panel follows its source's card, and
        // closes when the source is no longer on screen (another page).
        for (int i = panels.Count - 1; i >= 0; i--)
        {
            TelemetryDetailPanel panel = panels[i];
            if (panel.Source == null || !railManager.TryGetCard(panel.Source, out PerformanceStatCardView card))
            {
                Close(panel);
                continue;
            }
            if (card != panel.Card || card.RailSide != panel.Side)
            {
                panel.Bind(card);
                panel.SnapNextLayout = true;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Placement
    // -----------------------------------------------------------------------

    private void LateUpdate()
    {
        float fadeStep = Time.unscaledDeltaTime / fadeTime;

        for (int i = closing.Count - 1; i >= 0; i--)
        {
            TelemetryDetailPanel panel = closing[i];
            if (panel == null)
            {
                closing.RemoveAt(i);
                continue;
            }
            panel.SetAlpha(Mathf.MoveTowards(panel.Alpha, 0f, fadeStep));
            if (panel.Alpha <= 0f)
            {
                closing.RemoveAt(i);
                DestroyPanel(panel);
            }
        }

        foreach (StatRailSide side in Sides)
            LayoutSide(side, fadeStep);
    }

    private void LayoutSide(StatRailSide side, float fadeStep)
    {
        sidePanels.Clear();
        foreach (TelemetryDetailPanel panel in panels)
        {
            if (panel.Card != null && panel.Side == side)
                sidePanels.Add(panel);
        }
        if (sidePanels.Count == 0)
            return;

        RectTransform rail = railManager != null ? railManager.GetDetailRail(side) : null;
        if (rail == null)
        {
            // The rails changed under the panel (switched off, say): nowhere to sit.
            for (int i = sidePanels.Count - 1; i >= 0; i--)
                Close(sidePanels[i]);
            return;
        }

        GetLocalBounds(rail, out Vector2 railMin, out Vector2 railMax);
        if (side is StatRailSide.Top or StatRailSide.Bottom)
            LayoutBesideRow(side, railMin, railMax, fadeStep);
        else
            LayoutBesideColumn(side, railMin, railMax, fadeStep);
    }

    /// <summary>
    /// Panels beside a card column fill the detail rail's width and stack top
    /// to bottom, each centred on its card, pushed apart so none overlap and
    /// all stay on screen.
    /// </summary>
    private void LayoutBesideColumn(StatRailSide side, Vector2 railMin, Vector2 railMax, float fadeStep)
    {
        bool left = side == StatRailSide.Left;
        float width = Mathf.Max(0f, railMax.x - railMin.x - railGap);

        // Panels open on both sides at once share the room between the card
        // rails when their detail rails would meet.
        StatRailSide opposite = left ? StatRailSide.Right : StatRailSide.Left;
        RectTransform otherRail = railManager.GetDetailRail(opposite);
        if (otherRail != null && HasPanelOn(opposite))
        {
            GetLocalBounds(otherRail, out Vector2 otherMin, out Vector2 otherMax);
            float between = left ? otherMax.x - railMin.x : railMax.x - otherMin.x;
            width = Mathf.Min(width, between * 0.5f - railGap * 1.5f);
        }
        width = Mathf.Max(width, minimumPanelWidth);
        float x = left ? railMin.x + railGap : railMax.x - railGap - width;

        Rect layer = Layer.rect;
        float topLimit = layer.yMax - edgeMargin;
        float bottomLimit = layer.yMin + edgeMargin;

        // Top to bottom by card position.
        sidePanels.Sort((a, b) => CardCentre(b).y.CompareTo(CardCentre(a).y));
        cardCentres.Clear();
        targets.Clear();
        foreach (TelemetryDetailPanel panel in sidePanels)
        {
            panel.SetWidth(width);
            float centre = CardCentre(panel).y;
            cardCentres.Add(centre);
            targets.Add(Mathf.Min(centre + panel.Height * 0.5f, topLimit));
        }

        for (int i = 1; i < targets.Count; i++)
            targets[i] = Mathf.Min(targets[i], targets[i - 1] - sidePanels[i - 1].Height - stackGap);

        int last = targets.Count - 1;
        if (targets[last] - sidePanels[last].Height < bottomLimit)
        {
            targets[last] = bottomLimit + sidePanels[last].Height;
            for (int i = last - 1; i >= 0; i--)
                targets[i] = Mathf.Max(targets[i], targets[i + 1] + sidePanels[i].Height + stackGap);
        }

        for (int i = 0; i < sidePanels.Count; i++)
        {
            TelemetryDetailPanel panel = sidePanels[i];
            float top = Glide(panel, targets[i]);
            panel.RectTransform.anchoredPosition = new Vector2(x, top) - layer.center;
            panel.SetPointer(top - cardCentres[i]);
            panel.SetAlpha(Mathf.MoveTowards(panel.Alpha, CardVisibility(panel.Card), fadeStep));
        }
    }

    /// <summary>
    /// Panels opened from a card row sit in the detail rail just inside the
    /// rows, below the top ones or above the bottom ones, each centred under
    /// or over its card and pushed apart along the row so none overlap.
    /// </summary>
    private void LayoutBesideRow(StatRailSide side, Vector2 railMin, Vector2 railMax, float fadeStep)
    {
        bool top = side == StatRailSide.Top;
        Rect layer = Layer.rect;
        float leftLimit = layer.xMin + edgeMargin;
        float rightLimit = layer.xMax - edgeMargin;
        float width = Mathf.Clamp(rowPanelWidth, Mathf.Min(minimumPanelWidth, rightLimit - leftLimit),
            rightLimit - leftLimit);

        // Left to right by card position.
        sidePanels.Sort((a, b) => CardCentre(a).x.CompareTo(CardCentre(b).x));
        cardCentres.Clear();
        targets.Clear();
        foreach (TelemetryDetailPanel panel in sidePanels)
        {
            panel.SetWidth(width);
            float centre = CardCentre(panel).x;
            cardCentres.Add(centre);
            targets.Add(Mathf.Max(centre - width * 0.5f, leftLimit));
        }

        for (int i = 1; i < targets.Count; i++)
            targets[i] = Mathf.Max(targets[i], targets[i - 1] + width + stackGap);

        int last = targets.Count - 1;
        if (targets[last] + width > rightLimit)
        {
            targets[last] = rightLimit - width;
            for (int i = last - 1; i >= 0; i--)
                targets[i] = Mathf.Min(targets[i], targets[i + 1] - width - stackGap);
        }

        for (int i = 0; i < sidePanels.Count; i++)
        {
            TelemetryDetailPanel panel = sidePanels[i];
            float left = Glide(panel, targets[i]);
            // Hangs from the top rows, stands on the bottom ones; never off screen.
            float panelTop = top
                ? railMax.y - railGap
                : railMin.y + railGap + panel.Height;
            panelTop = Mathf.Clamp(panelTop, layer.yMin + edgeMargin + panel.Height, layer.yMax - edgeMargin);
            panel.RectTransform.anchoredPosition = new Vector2(left, panelTop) - layer.center;
            panel.SetPointer(cardCentres[i] - left);
            panel.SetAlpha(Mathf.MoveTowards(panel.Alpha, CardVisibility(panel.Card), fadeStep));
        }
    }

    /// <summary>Moves a panel's along-rail position towards its target, or snaps it there once.</summary>
    private float Glide(TelemetryDetailPanel panel, float target)
    {
        if (panel.SnapNextLayout)
        {
            panel.CurrentAlong = target;
            panel.AlongVelocity = 0f;
            panel.SnapNextLayout = false;
        }
        else
        {
            panel.CurrentAlong = Mathf.SmoothDamp(
                panel.CurrentAlong, target, ref panel.AlongVelocity, glideTime,
                Mathf.Infinity, Time.unscaledDeltaTime);
        }
        return panel.CurrentAlong;
    }

    private bool HasPanelOn(StatRailSide side)
    {
        foreach (TelemetryDetailPanel panel in panels)
        {
            if (panel.Card != null && panel.Side == side)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Cards fade out when telemetry is hidden (the full-body view); their panels
    /// fade with them and come back with them, pinned or not.
    /// </summary>
    private static float CardVisibility(PerformanceStatCardView card) =>
        card.TryGetComponent(out CanvasGroup group) ? group.alpha : 1f;

    private Vector2 CardCentre(TelemetryDetailPanel panel)
    {
        RectTransform card = (RectTransform)panel.Card.transform;
        return Layer.InverseTransformPoint(card.TransformPoint(card.rect.center));
    }

    private void GetLocalBounds(RectTransform rect, out Vector2 min, out Vector2 max)
    {
        rect.GetWorldCorners(corners);
        min = new Vector2(float.MaxValue, float.MaxValue);
        max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in corners)
        {
            Vector2 local = Layer.InverseTransformPoint(corner);
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
    }
}
