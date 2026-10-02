using System;
using UnityEngine;

/// <summary>
/// Switches the portrait layout between Telemetry and Service. The Telemetry /
/// Service switch sits centred in the top bar. Telemetry is the portrait layout
/// as it always was. Service fades the telemetry UI out and brings the service
/// panel up over the whole screen below the switch, on an opaque fill that runs
/// to the screen's edges: on a phone the model would be too small to read
/// beside the list, so this view has none.
///
/// The panel is the same <see cref="ServicePanelView"/> as the wide layout's,
/// drawn at <see cref="panelScale"/> so its type and touch targets suit a phone
/// held upright.
///
/// Nothing is deactivated or rebuilt: the telemetry UI fades through
/// CanvasGroups and comes back exactly as it was. The telemetry sheet hides the
/// same UI while it is open (see <see cref="SetTelemetryCover"/>); this
/// controller alone sets the groups, so the two never fight over them.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(40)]
public sealed class PortraitServiceModeController : MonoBehaviour, IDigitalTwinModeController
{
    [Tooltip("The service panel, a direct child of the portrait layout.")]
    [SerializeField] private RectTransform servicePanel;

    [Tooltip("The portrait top bar holding the switch. The panel starts below it.")]
    [SerializeField] private RectTransform topBar;

    [Tooltip("An opaque fill behind the switch and the panel, stretched to the screen's edges.")]
    [SerializeField] private RectTransform backdrop;

    [Tooltip("Shown in Telemetry: rails, leader lines, the bottom controls.")]
    [SerializeField] private CanvasGroup[] telemetryGroups = Array.Empty<CanvasGroup>();

    [Tooltip("Optional. Releases any focused telemetry card on entering Service.")]
    [SerializeField] private TelemetryCardFocusReleaser focusReleaser;

    [Tooltip("Seconds the switch between views takes.")]
    [SerializeField, Min(0.01f)] private float transitionSeconds = 0.35f;

    [SerializeField] private DigitalTwinMode startMode = DigitalTwinMode.Telemetry;

    [Header("Panel")]
    [Tooltip("Size the panel is drawn at, against the wide layout's. The portrait canvas is " +
             "1080 units across a phone, so 2 draws the panel 540 units wide.")]
    [SerializeField, Range(1f, 3f)] private float panelScale = 2f;

    [Tooltip("Space between the switch's bar and the panel.")]
    [SerializeField, Min(0f)] private float topGap = 8f;

    [Tooltip("How far the panel rises as it fades in.")]
    [SerializeField, Min(0f)] private float slideDistance = 48f;

    private float blend;
    private float appliedBlend = -1f;
    private float telemetryCover;
    private float appliedCover = -1f;
    private CanvasGroup panelGroup;
    private CanvasGroup backdropGroup;
    private Canvas rootCanvas;
    private static readonly Vector3[] Corners = new Vector3[4];

    public DigitalTwinMode CurrentMode => DigitalTwinModeState.Get(startMode);

    /// <summary>0 in Telemetry, 1 in Service, between the two while switching.</summary>
    public float ServiceBlend => blend;

    public event Action<DigitalTwinMode> ModeChanged;

    /// <summary>
    /// How far something over the model (the telemetry sheet) hides the
    /// telemetry UI in Telemetry: 0 not at all, 1 fully, which also makes it inert.
    /// </summary>
    public void SetTelemetryCover(float cover)
    {
        telemetryCover = Mathf.Clamp01(cover);
        ApplyGroups();
    }

    private float Target => CurrentMode == DigitalTwinMode.Service ? 1f : 0f;

    private void Awake()
    {
        if (servicePanel != null)
            panelGroup = servicePanel.GetComponent<CanvasGroup>();
        if (backdrop != null)
            backdropGroup = backdrop.GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        ServicePartLink.Requested += HandlePartRequested;
        // Turning the phone lands on the chosen view straight away.
        blend = Target;
        appliedBlend = -1f;
        ApplyGroups();
        ApplyLayout();
    }

    private void OnDisable()
    {
        ServicePartLink.Requested -= HandlePartRequested;
        // The wide layout takes over: leave the telemetry UI as it expects.
        telemetryCover = 0f;
        appliedCover = -1f;
        SetGroups(telemetryGroups, 1f, true);
        SetGroup(panelGroup, 0f, false);
        SetGroup(backdropGroup, 0f, false);
        appliedBlend = -1f;
    }

    public void SetMode(DigitalTwinMode mode)
    {
        if (mode == CurrentMode)
            return;

        DigitalTwinModeState.Set(mode);
        if (mode == DigitalTwinMode.Service)
            focusReleaser?.Dismiss();

        ModeChanged?.Invoke(mode);
    }

    /// <summary>The telemetry sheet asked for a part's service detail: switch and open it.</summary>
    private void HandlePartRequested(MachinePartGroup part)
    {
        SetMode(DigitalTwinMode.Service);
        if (servicePanel != null && servicePanel.TryGetComponent(out ServicePanelView view))
            view.ShowDetail(part);
    }

    private void LateUpdate()
    {
        blend = Mathf.MoveTowards(blend, Target, Time.unscaledDeltaTime / transitionSeconds);
        ApplyGroups();
        ApplyLayout();
    }

    private static float Ease(float t) => t * t * (3f - 2f * t);

    private void ApplyGroups()
    {
        if (Mathf.Approximately(appliedBlend, blend) && Mathf.Approximately(appliedCover, telemetryCover))
            return;
        appliedBlend = blend;
        appliedCover = telemetryCover;

        // Eased so the two views cross over rather than both sitting at half.
        float eased = Ease(blend);
        bool service = CurrentMode == DigitalTwinMode.Service;
        SetGroups(telemetryGroups, (1f - eased) * (1f - telemetryCover), !service && telemetryCover < 0.5f);
        SetGroup(panelGroup, eased, service);
        SetGroup(backdropGroup, eased, service);
    }

    private static void SetGroups(CanvasGroup[] groups, float alpha, bool interactive)
    {
        foreach (CanvasGroup group in groups)
            SetGroup(group, alpha, interactive);
    }

    private static void SetGroup(CanvasGroup group, float alpha, bool interactive)
    {
        if (group == null)
            return;
        group.alpha = alpha;
        group.interactable = interactive;
        group.blocksRaycasts = interactive && alpha > 0.5f;
    }

    // -----------------------------------------------------------------------
    // Layout
    // -----------------------------------------------------------------------

    /// <summary>
    /// Places the panel under the top bar, the full width and the rest of the
    /// height of the safe area, drawn at <see cref="panelScale"/>; and the
    /// backdrop over the whole screen, past the safe area's edges.
    /// </summary>
    private void ApplyLayout()
    {
        if (blend <= 0f && appliedBlend == 0f)
            return;

        if (servicePanel != null && servicePanel.parent is RectTransform space)
        {
            Rect area = space.rect;
            float top = TopBand(space);
            float scale = Mathf.Max(1f, panelScale);
            float width = Mathf.Max(0f, area.width / scale);
            float height = Mathf.Max(0f, (area.height - top) / scale);
            float rise = (1f - Ease(blend)) * slideDistance;

            Vector2 anchor = new(0.5f, 1f);
            if (servicePanel.anchorMin != anchor)
                servicePanel.anchorMin = anchor;
            if (servicePanel.anchorMax != anchor)
                servicePanel.anchorMax = anchor;
            if (servicePanel.pivot != anchor)
                servicePanel.pivot = anchor;
            Vector3 scaleVector = new(scale, scale, 1f);
            if (servicePanel.localScale != scaleVector)
                servicePanel.localScale = scaleVector;
            Vector2 size = new(width, height);
            if (servicePanel.sizeDelta != size)
                servicePanel.sizeDelta = size;
            Vector2 position = new(0f, -top - rise);
            if (servicePanel.anchoredPosition != position)
                servicePanel.anchoredPosition = position;
        }

        PlaceBackdrop();
    }

    /// <summary>Height from the top of <paramref name="space"/> to the bottom of the top bar, plus the gap.</summary>
    private float TopBand(RectTransform space)
    {
        if (topBar == null)
            return topGap;
        topBar.GetWorldCorners(Corners);
        Vector3 bottom = space.InverseTransformPoint(Corners[0]);
        return Mathf.Clamp(space.rect.yMax - bottom.y, 0f, space.rect.height) + topGap;
    }

    /// <summary>Stretches the backdrop from its parent, the safe area, out to the canvas's edges.</summary>
    private void PlaceBackdrop()
    {
        if (backdrop == null || !(backdrop.parent is RectTransform parent))
            return;

        if (rootCanvas == null)
        {
            Canvas canvas = backdrop.GetComponentInParent<Canvas>();
            rootCanvas = canvas != null ? canvas.rootCanvas : null;
        }
        if (rootCanvas == null)
            return;

        ((RectTransform)rootCanvas.transform).GetWorldCorners(Corners);
        Vector3 low = parent.InverseTransformPoint(Corners[0]);
        Vector3 high = parent.InverseTransformPoint(Corners[2]);
        Rect rect = parent.rect;

        backdrop.anchorMin = Vector2.zero;
        backdrop.anchorMax = Vector2.one;
        Vector2 min = new(low.x - rect.xMin, low.y - rect.yMin);
        Vector2 max = new(high.x - rect.xMax, high.y - rect.yMax);
        if (backdrop.offsetMin != min)
            backdrop.offsetMin = min;
        if (backdrop.offsetMax != max)
            backdrop.offsetMax = max;
    }
}
