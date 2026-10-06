using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The wide layout's Service view: slides the service panel in from the
/// right, full height, as a column of its own while the layout's
/// <see cref="TelemetryVisibility"/> blends to Service. The viewport narrows
/// beside it as if the window had, so the bars and their controls sit left of
/// the panel and the model centres in that space.
///
/// The telemetry content area is held at its full width while the viewport
/// narrows, so the cards keep their layout and come back exactly as they
/// were. The model moves by an off-centre projection rather than a smaller
/// viewport, so orbiting and zoom behave as before. Fading is not done here:
/// <see cref="TelemetryVisibility"/> fades the telemetry regions and the panel.
///
/// Where the window is too narrow for the controls beside a panel, the panel
/// sits over the right of the content area instead, between the bars.
///
/// Opening a part's detail from its card pulls the camera back when the part
/// is not all in view in the space left of the panel.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(41)]
public sealed class WideServiceView : MonoBehaviour
{
    [Tooltip("This layout's telemetry visibility: it owns the view, and its blend places the panel.")]
    [SerializeField] private TelemetryVisibility visibility;

    [SerializeField] private Camera targetCamera;

    [Tooltip("The service panel, a child of the layout that holds the viewport.")]
    [SerializeField] private RectTransform servicePanel;

    [Tooltip("The wide layout's base layout. Its right edge moves in beside the panel.")]
    [SerializeField] private RectTransform viewport;

    [Tooltip("The telemetry content area inside the viewport. Held at its full width while the viewport narrows.")]
    [SerializeField] private RectTransform contentArea;

    [Header("Panel Width")]
    [SerializeField, Min(200f)] private float panelWidth = 560f;

    [Tooltip("Narrowest the panel becomes to leave the viewport room.")]
    [SerializeField, Min(200f)] private float minimumPanelWidth = 440f;

    [Tooltip("Narrowest the viewport becomes: room for the bottom controls side by side.")]
    [SerializeField, Min(0f)] private float minimumViewportWidth = 1000f;

    [Tooltip("Space kept clear around an opened part when the camera pulls back to show it, " +
             "as a share of the screen's half-width and half-height.")]
    [SerializeField, Range(0f, 0.4f)] private float framingMargin = 0.06f;

    private bool projectionShifted;
    private bool layoutCaptured;
    private Vector2 viewportOffsetMax;
    private float contentMinWidth;
    private TextAnchor viewportAlignment;
    private LayoutElement contentLayout;
    private LayoutGroup viewportGroup;
    private ServicePanelView panelView;
    private OrbitCameraController orbit;
    private Canvas rootCanvas;
    private static readonly Vector3[] Corners = new Vector3[4];

    public TelemetryVisibility Visibility => visibility;

    /// <summary>True while the panel is a column beside the viewport rather than over it.</summary>
    public bool PanelBesideViewport { get; private set; }

    private float Blend => visibility != null ? visibility.ServiceBlend : 0f;
    private float EasedBlend => visibility != null ? visibility.EasedServiceBlend : 0f;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
        CaptureLayout();
    }

    private void OnEnable()
    {
        if (panelView != null)
            panelView.PartOpened += HandlePartOpened;
        ServicePartLink.Requested += HandlePartRequested;
    }

    private void OnDisable()
    {
        // The portrait layout has its own service view: leave the camera and
        // the viewport as the rest of the app expects them.
        if (panelView != null)
            panelView.PartOpened -= HandlePartOpened;
        ServicePartLink.Requested -= HandlePartRequested;
        ResetProjection();
        SetInset(0f);
    }

    private void LateUpdate()
    {
        ApplyLayout();
        ApplyProjection();
    }

    // -----------------------------------------------------------------------
    // Panel and viewport
    // -----------------------------------------------------------------------

    private void CaptureLayout()
    {
        if (layoutCaptured)
            return;
        layoutCaptured = true;

        if (servicePanel != null)
            panelView = servicePanel.GetComponent<ServicePanelView>();
        if (viewport != null)
        {
            viewportOffsetMax = viewport.offsetMax;
            viewportGroup = viewport.GetComponent<LayoutGroup>();
            if (viewportGroup != null)
                viewportAlignment = viewportGroup.childAlignment;
        }
        if (contentArea != null && contentArea.TryGetComponent(out contentLayout))
            contentMinWidth = contentLayout.minWidth;
    }

    /// <summary>
    /// Places the panel for this frame. Beside the viewport it runs the full
    /// height of the screen and the viewport's right edge follows its left
    /// edge in; over the content area it keeps the bars' margins.
    /// </summary>
    private void ApplyLayout()
    {
        if (servicePanel == null || !(servicePanel.parent is RectTransform space))
            return;

        float eased = EasedBlend;
        float available = space.rect.width;
        float width = Mathf.Min(panelWidth, available - minimumViewportWidth);
        bool beside = width >= minimumPanelWidth || viewport == null;

        float top = 0f;
        float bottom = 0f;
        float margin = 0f;
        if (!beside)
        {
            width = Mathf.Min(panelWidth, available * 0.5f);
            margin = viewportGroup != null ? viewportGroup.padding.right : 0f;
            ContentBands(space, out top, out bottom);
        }

        // Slides in from beyond the right edge.
        float right = -margin + (1f - eased) * (width + margin);
        SetOffsets(servicePanel, new Vector2(right - width, bottom), new Vector2(right, -top));

        PanelBesideViewport = beside;
        if (panelView != null)
        {
            if (beside)
                ContentBands(space, out top, out bottom);
            float edge = viewportGroup != null ? viewportGroup.padding.top : 0f;
            panelView.SetBands(beside ? top : 0f, beside ? bottom : 0f, edge);
        }

        SetInset(beside ? eased * width : 0f);
    }

    /// <summary>
    /// Narrows the viewport by <paramref name="inset"/> from the right. The
    /// content area inside keeps the full width, so only the bars move.
    /// </summary>
    private void SetInset(float inset)
    {
        if (viewport == null)
            return;

        SetOffsets(viewport, viewport.offsetMin, new Vector2(viewportOffsetMax.x - inset, viewportOffsetMax.y));

        if (contentLayout == null)
            return;

        // Checked every frame while narrowed: the full width follows the window.
        float minWidth = contentMinWidth;
        if (inset > 0f)
        {
            float padding = viewportGroup != null ? viewportGroup.padding.horizontal : 0f;
            minWidth = Mathf.Max(contentMinWidth, viewport.rect.width + inset - padding);
        }
        if (!Mathf.Approximately(contentLayout.minWidth, minWidth))
            contentLayout.minWidth = minWidth;

        // A child wider than the group is placed by the group's alignment:
        // aligned left, the content area keeps its left edge and runs on
        // under the panel. The bars fill the group's width either way.
        if (viewportGroup != null)
        {
            TextAnchor alignment = inset > 0f
                ? (TextAnchor)((int)viewportAlignment - (int)viewportAlignment % 3)
                : viewportAlignment;
            if (viewportGroup.childAlignment != alignment)
                viewportGroup.childAlignment = alignment;
        }
    }

    /// <summary>Heights of the bars above and below the content area, in <paramref name="space"/>'s units.</summary>
    private void ContentBands(RectTransform space, out float top, out float bottom)
    {
        top = 0f;
        bottom = 0f;
        if (contentArea == null)
            return;

        contentArea.GetWorldCorners(Corners);
        Vector3 low = space.InverseTransformPoint(Corners[0]);
        Vector3 high = space.InverseTransformPoint(Corners[1]);
        Rect rect = space.rect;
        top = Mathf.Max(0f, rect.yMax - high.y);
        bottom = Mathf.Max(0f, low.y - rect.yMin);
    }

    private static void SetOffsets(RectTransform rect, Vector2 min, Vector2 max)
    {
        if (rect.offsetMin != min)
            rect.offsetMin = min;
        if (rect.offsetMax != max)
            rect.offsetMax = max;
    }

    // -----------------------------------------------------------------------
    // Model
    // -----------------------------------------------------------------------

    /// <summary>
    /// Pulls the camera back when <paramref name="part"/> is not all inside the
    /// space left of the panel and between the bars. Never moves closer or
    /// turns the camera; the orbit controller eases the move.
    /// </summary>
    private void HandlePartOpened(MachinePartGroup part) => FramePart(part);

    /// <summary>The telemetry sheet asked for a part's service detail: switch and open it.</summary>
    private void HandlePartRequested(MachinePartGroup part)
    {
        visibility?.SetMode(DigitalTwinMode.Service);
        if (panelView == null)
            return;
        panelView.ShowDetail(part);
        if (panelView.SelectedPart == part)
            FramePart(part);
    }

    public bool FramePart(MachinePartGroup part) =>
        TryGetFraming(part, out Bounds bounds, out Rect window) &&
        orbit.EnsureBoundsVisible(bounds, window, framingMargin);

    /// <summary>True when all of <paramref name="part"/> is in view left of the panel, between the bars.</summary>
    public bool IsPartInView(MachinePartGroup part) =>
        TryGetFraming(part, out Bounds bounds, out Rect window) &&
        orbit.AreBoundsVisible(bounds, window, framingMargin);

    /// <summary>The part's bounds, and the space it should sit in, in normalised device coordinates.</summary>
    private bool TryGetFraming(MachinePartGroup part, out Bounds bounds, out Rect window)
    {
        bounds = default;
        window = default;
        if (part == null || targetCamera == null || servicePanel == null ||
            !(servicePanel.parent is RectTransform space) || !part.TryGetWorldBounds(out bounds))
            return false;

        if (orbit == null && !targetCamera.TryGetComponent(out orbit))
            return false;

        float height = space.rect.height;
        if (height <= 0f)
            return false;

        // From the screen's left edge to the panel, and between the top and
        // bottom bars.
        ContentBands(space, out float top, out float bottom);
        float right = 1f - 2f * PanelScreenShare();
        window = Rect.MinMaxRect(-1f, -1f + 2f * bottom / height, right, 1f - 2f * top / height);
        return true;
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

        // The panel slides in, so its left edge carries the transition.
        float shift = Blend > 0f ? PanelScreenShare() : 0f;
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
