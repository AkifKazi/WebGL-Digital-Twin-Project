using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Opens telemetry detail panels in the wide layout. The secondary rails beside
/// the model are widened and kept for the panels. A clean click on a card opens
/// a panel in the rail next to it. Clicking another card glides that panel to
/// it; a pinned panel stays put and the click opens a new one. A clean click
/// anywhere else, Esc, or the close button closes the unpinned panel. Dragging
/// to turn the camera never closes anything.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryDetailPanelController : MonoBehaviour
{
    [Header("Feature")]
    [Tooltip("What the secondary rails beside the model are for. Off (the default): no secondary " +
             "rails and no detail panel; cards stay on the primary rails and further pages. Detail " +
             "Panel: the rails are widened and kept for the extra-info panel. Telemetry Cards: they " +
             "hold cards and the panel is off. Until the references below and a ready history " +
             "provider are in place, Detail Panel falls back to cards.")]
    [SerializeField] private SecondaryRailUse secondaryRails = SecondaryRailUse.Off;

    [Tooltip("Width of a secondary rail kept for the panel, as a multiple of the outer rail width.")]
    [FormerlySerializedAs("widthMultiplier")]
    [SerializeField, Range(1f, 2.5f)] private float railWidthMultiplier = 1.5f;

    [Header("References")]
    [SerializeField] private WideStatRailManager railManager;
    [SerializeField] private TelemetryDetailPanel panelPrefab;

    [Tooltip("Supplies the history the graphs draw: the Telemetry History Recorder, " +
             "or any component implementing ITelemetryHistoryProvider.")]
    [SerializeField] private MonoBehaviour historyProvider;

    [Tooltip("Optional. Supplies text for the detail section (a recommendation, for " +
             "example). Empty, the section stays hidden.")]
    [SerializeField] private MonoBehaviour insightProvider;

    [Header("Graphs")]
    [Tooltip("Graphs in each panel, top to bottom.")]
    [SerializeField] private List<TelemetryGraphKind> graphs = new()
    {
        TelemetryGraphKind.StatusTimeline,
        TelemetryGraphKind.LineChart
    };

    [SerializeField] private TelemetryHistoryRange defaultRange = TelemetryHistoryRange.Hour;
    [SerializeField] private TelemetryGraphStyle graphStyle = new();

    [Header("Layout")]
    [Tooltip("Gap between the card rail and the panel. The panel sprite's outer glow adds " +
             "about 10 more before its frame line.")]
    [SerializeField, Min(0f)] private float railGap = 12f;

    [Tooltip("Gap between two panels on the same side, glow to glow.")]
    [SerializeField, Min(0f)] private float stackGap = 2f;

    [Tooltip("Closest a panel gets to the top or bottom of the screen area.")]
    [SerializeField, Min(0f)] private float edgeMargin = 4f;

    [Tooltip("Seconds a panel takes to glide to a newly clicked card.")]
    [SerializeField, Min(0.01f)] private float glideTime = 0.22f;

    [SerializeField, Min(0.01f)] private float fadeTime = 0.15f;

    [SerializeField, Min(1)] private int maximumPinnedPanels = 3;

    [Tooltip("Seconds between graph refreshes while a panel is open.")]
    [SerializeField, Min(0.1f)] private float refreshInterval = 1f;

    private static readonly StatRailSide[] Sides = { StatRailSide.Left, StatRailSide.Right };

    private readonly List<TelemetryDetailPanel> panels = new();
    private readonly List<TelemetryDetailPanel> closing = new();
    private readonly List<TelemetryDetailPanel> sidePanels = new();
    private readonly List<float> cardCentres = new();
    private readonly List<float> targets = new();
    private readonly Vector3[] corners = new Vector3[4];

    private TelemetryDetailPanel floating;
    private float nextRefreshTime;
    private bool warnedNotReady;

    public bool IsReady =>
        secondaryRails == SecondaryRailUse.DetailPanel && isActiveAndEnabled &&
        railManager != null && panelPrefab != null && History != null && History.IsReady;

    public SecondaryRailUse SecondaryRails => secondaryRails;
    public IReadOnlyList<TelemetryDetailPanel> OpenPanels => panels;
    public TelemetryDetailPanel FloatingPanel => floating;

    private ITelemetryHistoryProvider History => historyProvider as ITelemetryHistoryProvider;
    private ITelemetryInsightProvider Insight => insightProvider as ITelemetryInsightProvider;
    private RectTransform Layer => (RectTransform)transform;

    private void OnEnable()
    {
        PerformanceStatCardView.Clicked += HandleCardClicked;
        TelemetryCardFocusReleaser.Dismissed += DismissUnpinned;
        if (railManager != null)
            railManager.LayoutRebuilt += HandleLayoutRebuilt;
        SyncRailReservation();
    }

    private void OnDisable()
    {
        PerformanceStatCardView.Clicked -= HandleCardClicked;
        TelemetryCardFocusReleaser.Dismissed -= DismissUnpinned;
        if (railManager != null)
        {
            railManager.LayoutRebuilt -= HandleLayoutRebuilt;
            railManager.ReserveSecondaryRails(false);
        }

        foreach (TelemetryDetailPanel panel in panels)
            DestroyPanel(panel);
        foreach (TelemetryDetailPanel panel in closing)
            DestroyPanel(panel);
        panels.Clear();
        closing.Clear();
        floating = null;
    }

    /// <summary>
    /// Keeps the secondary rails for the panel while it is ready and gives them
    /// back to cards otherwise, so a half set-up panel never leaves empty rails.
    /// </summary>
    private void SyncRailReservation()
    {
        bool ready = IsReady;
        if (railManager != null)
        {
            // Off keeps the secondary rails empty at their normal width, so they hide.
            if (secondaryRails == SecondaryRailUse.Off)
                railManager.ReserveSecondaryRails(true);
            else
                railManager.ReserveSecondaryRails(ready, railWidthMultiplier);
        }
        if (!ready)
        {
            for (int i = panels.Count - 1; i >= 0; i--)
                Close(panels[i]);
        }
    }

    // -----------------------------------------------------------------------
    // Clicks
    // -----------------------------------------------------------------------

    private void HandleCardClicked(PerformanceStatCardView card)
    {
        if (!IsReady)
        {
            if (secondaryRails == SecondaryRailUse.DetailPanel && !warnedNotReady)
            {
                warnedNotReady = true;
                Debug.LogWarning(
                    "The telemetry detail panel is enabled but not ready: it needs the rail " +
                    "manager, the panel prefab and a ready history provider.", this);
            }
            return;
        }

        if (!railManager.OwnsCard(card) ||
            (card.RailSide != StatRailSide.Left && card.RailSide != StatRailSide.Right))
            return;

        // A second click released the card: its unpinned panel goes with it.
        if (!card.IsFocused)
        {
            if (floating != null && floating.Card == card)
                Close(floating);
            return;
        }

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
        SyncRailReservation();

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
        panel.Initialise(graphs, graphStyle, defaultRange, History, Insight);
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
        // Cards are pooled: after a rebuild a panel finds its source's new card,
        // or closes when the source is no longer on a rail (another page).
        for (int i = panels.Count - 1; i >= 0; i--)
        {
            TelemetryDetailPanel panel = panels[i];
            if (panel.Source == null || !railManager.TryGetCard(panel.Source, out PerformanceStatCardView card))
            {
                Close(panel);
                continue;
            }
            if (card != panel.Card)
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

        RectTransform outer = railManager != null ? railManager.GetRail(side, false) : null;
        if (sidePanels.Count == 0 || outer == null)
            return;

        // The panel fills the secondary rail next to the card rail.
        RectTransform inner = railManager.GetRail(side, true);
        GetLocalHorizontalBounds(outer, out float outerMin, out float outerMax);
        float railWidth = (outerMax - outerMin) * railWidthMultiplier;
        if (inner != null)
        {
            GetLocalHorizontalBounds(inner, out float innerMin, out float innerMax);
            railWidth = innerMax - innerMin;
        }
        float width = Mathf.Max(0f, railWidth - railGap);
        float x = side == StatRailSide.Left ? outerMax + railGap : outerMin - railGap - width;

        Rect layer = Layer.rect;
        float topLimit = layer.yMax - edgeMargin;
        float bottomLimit = layer.yMin + edgeMargin;

        // Top to bottom by card position, each panel centred on its card, then
        // pushed apart so none overlap and all stay on screen.
        sidePanels.Sort((a, b) => CardCentre(b).CompareTo(CardCentre(a)));
        cardCentres.Clear();
        targets.Clear();
        foreach (TelemetryDetailPanel panel in sidePanels)
        {
            panel.SetWidth(width);
            float centre = CardCentre(panel);
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
            if (panel.SnapNextLayout)
            {
                panel.CurrentTop = targets[i];
                panel.TopVelocity = 0f;
                panel.SnapNextLayout = false;
            }
            else
            {
                panel.CurrentTop = Mathf.SmoothDamp(
                    panel.CurrentTop, targets[i], ref panel.TopVelocity, glideTime,
                    Mathf.Infinity, Time.unscaledDeltaTime);
            }

            panel.RectTransform.anchoredPosition = new Vector2(x, panel.CurrentTop) - layer.center;
            panel.SetPointer(panel.CurrentTop - cardCentres[i], panel.Card.StateColor);
            panel.SetAlpha(Mathf.MoveTowards(panel.Alpha, CardVisibility(panel.Card), fadeStep));
        }
    }

    /// <summary>
    /// Cards fade out when telemetry is hidden (the full-body view); their panels
    /// fade with them and come back with them, pinned or not.
    /// </summary>
    private static float CardVisibility(PerformanceStatCardView card) =>
        card.TryGetComponent(out CanvasGroup group) ? group.alpha : 1f;

    private float CardCentre(TelemetryDetailPanel panel)
    {
        RectTransform card = (RectTransform)panel.Card.transform;
        return Layer.InverseTransformPoint(card.TransformPoint(card.rect.center)).y;
    }

    private void GetLocalHorizontalBounds(RectTransform rect, out float min, out float max)
    {
        rect.GetWorldCorners(corners);
        min = float.MaxValue;
        max = float.MinValue;
        foreach (Vector3 corner in corners)
        {
            float x = Layer.InverseTransformPoint(corner).x;
            min = Mathf.Min(min, x);
            max = Mathf.Max(max, x);
        }
    }
}
