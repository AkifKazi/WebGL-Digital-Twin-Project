using System;
using UnityEngine;

/// <summary>The two top-level views of the digital twin.</summary>
public enum DigitalTwinMode
{
    Telemetry = 0,
    Service = 1
}

/// <summary>
/// Switches the wide layout between Telemetry and Service. Service hides every
/// telemetry element (cards, leader lines, detail panels, pagination) and shows
/// the service panel on the right, and moves the model into the space left of
/// the panel.
///
/// Nothing is deactivated or re-laid out: the telemetry UI fades through
/// CanvasGroups, so the rails keep their layout and come back exactly as they
/// were. The model moves by an off-centre projection rather than a smaller
/// viewport, so the whole screen is still rendered behind the panel and the
/// top and bottom bars.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(40)]
public sealed class ServiceModeController : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;

    [Tooltip("The service panel. Its left edge is where the model's space ends.")]
    [SerializeField] private RectTransform servicePanel;

    [Tooltip("Shown in Service: the panel and the pins on the model.")]
    [SerializeField] private CanvasGroup[] serviceGroups = Array.Empty<CanvasGroup>();

    [Tooltip("Shown in Telemetry: rails, leader lines, detail panels, pagination.")]
    [SerializeField] private CanvasGroup[] telemetryGroups = Array.Empty<CanvasGroup>();

    [Tooltip("Optional. Its unpinned panel closes on entering Service.")]
    [SerializeField] private TelemetryDetailPanelController detailPanels;

    [Tooltip("Optional. Releases any focused telemetry card on entering Service.")]
    [SerializeField] private TelemetryCardFocusReleaser focusReleaser;

    [Tooltip("Seconds the switch between views takes.")]
    [SerializeField, Min(0.01f)] private float transitionSeconds = 0.35f;

    [SerializeField] private DigitalTwinMode startMode = DigitalTwinMode.Telemetry;

    private float blend;
    private float appliedBlend = -1f;
    private bool projectionShifted;
    private Canvas rootCanvas;
    private static readonly Vector3[] Corners = new Vector3[4];

    public DigitalTwinMode CurrentMode { get; private set; }

    /// <summary>0 in Telemetry, 1 in Service, between the two while switching.</summary>
    public float ServiceBlend => blend;

    public event Action<DigitalTwinMode> ModeChanged;

    private void Awake()
    {
        CurrentMode = startMode;
        blend = Target;
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void OnEnable()
    {
        // Returning to the wide layout lands on the chosen view straight away.
        blend = Target;
        appliedBlend = -1f;
        ApplyGroups();
    }

    private void OnDisable()
    {
        // The portrait layout has no service view: leave the camera and the
        // telemetry UI as the rest of the app expects them.
        ResetProjection();
        SetGroups(telemetryGroups, 1f, true);
        SetGroups(serviceGroups, 0f, false);
        appliedBlend = -1f;
    }

    public void SetMode(DigitalTwinMode mode)
    {
        if (mode == CurrentMode)
            return;

        CurrentMode = mode;
        if (mode == DigitalTwinMode.Service)
        {
            focusReleaser?.Dismiss();
            detailPanels?.DismissUnpinned();
        }

        ModeChanged?.Invoke(mode);
    }

    private float Target => CurrentMode == DigitalTwinMode.Service ? 1f : 0f;

    private void LateUpdate()
    {
        blend = Mathf.MoveTowards(blend, Target, Time.unscaledDeltaTime / transitionSeconds);
        ApplyGroups();
        ApplyProjection();
    }

    private void ApplyGroups()
    {
        if (Mathf.Approximately(appliedBlend, blend))
            return;
        appliedBlend = blend;

        // Eased so the two views cross over rather than both sitting at half.
        float eased = blend * blend * (3f - 2f * blend);
        bool service = CurrentMode == DigitalTwinMode.Service;
        SetGroups(telemetryGroups, 1f - eased, !service);
        SetGroups(serviceGroups, eased, service);
    }

    private static void SetGroups(CanvasGroup[] groups, float alpha, bool interactive)
    {
        foreach (CanvasGroup group in groups)
        {
            if (group == null)
                continue;
            group.alpha = alpha;
            group.interactable = interactive;
            group.blocksRaycasts = interactive && alpha > 0.5f;
        }
    }

    /// <summary>
    /// Moves the picture left so the orbit target sits in the middle of the
    /// space left of the panel. An off-centre projection keeps the camera's
    /// position, rotation and field of view, so orbiting and zoom behave as
    /// before, and screen-point conversions (pins, framing) stay correct.
    /// </summary>
    private void ApplyProjection()
    {
        if (targetCamera == null)
            return;

        float shift = blend > 0f ? blend * PanelScreenShare() : 0f;
        if (shift <= 0.0001f)
        {
            ResetProjection();
            return;
        }

        Matrix4x4 projection = Matrix4x4.Perspective(
            targetCamera.fieldOfView,
            targetCamera.aspect,
            targetCamera.nearClipPlane,
            targetCamera.farClipPlane);

        // A point straight ahead lands at x = -m02 in normalised device
        // coordinates, which run -1..1 across the screen: a share s of the
        // screen width is a shift of s.
        projection.m02 = shift;
        targetCamera.projectionMatrix = projection;
        projectionShifted = true;
    }

    private void ResetProjection()
    {
        if (!projectionShifted || targetCamera == null)
            return;
        targetCamera.ResetProjectionMatrix();
        projectionShifted = false;
    }

    /// <summary>Share of the screen width from the panel's left edge to the screen's right edge.</summary>
    private float PanelScreenShare()
    {
        if (servicePanel == null)
            return 0f;

        if (rootCanvas == null)
        {
            Canvas canvas = servicePanel.GetComponentInParent<Canvas>();
            rootCanvas = canvas != null ? canvas.rootCanvas : null;
        }

        // Overlay canvases draw in screen pixels; a camera canvas (as the
        // capture tests use) is measured through its own camera.
        Camera uiCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera
            : null;
        float width = uiCamera != null ? uiCamera.pixelWidth : Screen.width;
        if (width <= 0f)
            return 0f;

        servicePanel.GetWorldCorners(Corners);
        float panelLeft = RectTransformUtility.WorldToScreenPoint(uiCamera, Corners[0]).x;
        return Mathf.Clamp01((width - panelLeft) / width);
    }
}
