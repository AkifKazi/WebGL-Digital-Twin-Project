using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The portrait layout's Service view. The Telemetry / Service switch sits
/// centred in the top bar. Service brings the service panel up over the whole
/// screen below the switch, on an opaque fill that runs to the screen's edges:
/// on a phone the model would be too small to read beside the list, so this
/// view has none.
///
/// The panel is the same <see cref="ServicePanelView"/> as the wide layout's,
/// drawn at <see cref="panelScale"/> so its type and touch targets suit a phone
/// held upright.
///
/// This only places the panel and the backdrop as the layout's
/// <see cref="TelemetryVisibility"/> blends to Service; that fades the
/// telemetry regions, the panel and the backdrop.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(41)]
public sealed class PortraitServiceView : MonoBehaviour
{
    [Tooltip("This layout's telemetry visibility: it owns the view, and its blend places the panel.")]
    [SerializeField] private TelemetryVisibility visibility;

    [Tooltip("The service panel, a direct child of the portrait layout.")]
    [SerializeField] private RectTransform servicePanel;

    [Tooltip("The portrait top bar holding the switch. The panel starts below it.")]
    [SerializeField] private RectTransform topBar;

    [Tooltip("An opaque fill behind the switch and the panel, stretched to the screen's edges.")]
    [SerializeField] private RectTransform backdrop;

    [Header("Panel")]
    [Tooltip("Size the panel is drawn at, against the wide layout's. The portrait canvas is " +
             "1080 units across a phone, so 2 draws the panel 540 units wide.")]
    [SerializeField, Range(1f, 3f)] private float panelScale = 2f;

    [Tooltip("Space between the switch's bar and the panel.")]
    [SerializeField, Min(0f)] private float topGap = 8f;

    [Tooltip("How far the panel rises as it fades in.")]
    [SerializeField, Min(0f)] private float slideDistance = 48f;

    private float placedBlend = -1f;
    private Canvas rootCanvas;
    private static readonly Vector3[] Corners = new Vector3[4];

    public TelemetryVisibility Visibility => visibility;

    /// <summary>The opaque fill behind the switch and the panel; the Ask tab borrows its look.</summary>
    public RectTransform Backdrop => backdrop;

    // The backdrop's own colour, for a scene without a portrait layout.
    private static readonly Color FallbackFill = new(0.031f, 0.086f, 0.118f, 1f);

    /// <summary>
    /// Gives <paramref name="to"/> the backdrop's look: the opaque fill a
    /// phone's pages share (the Service and Ask tabs, the telemetry sheet,
    /// the settings), so they read as one surface.
    /// </summary>
    public static void CopyBackdropLook(Image to)
    {
        PortraitServiceView view = FindAnyObjectByType<PortraitServiceView>(FindObjectsInactive.Include);
        if (view == null || view.backdrop == null || !view.backdrop.TryGetComponent(out Image look))
        {
            to.color = FallbackFill;
            return;
        }
        UIShape.CopyLook(look, to);
    }

    private void OnEnable()
    {
        ServicePartLink.Requested += HandlePartRequested;
        placedBlend = -1f;
        // The backdrop is the fill here; the panel's own edge would draw a
        // line under the switch, above the panel's header and its rule.
        if (servicePanel != null && servicePanel.TryGetComponent(out ServicePanelView view))
            view.SetFrameShown(false);
    }

    private void OnDisable()
    {
        ServicePartLink.Requested -= HandlePartRequested;
    }

    /// <summary>The telemetry sheet asked for a part's service detail: switch and open it.</summary>
    private void HandlePartRequested(MachinePartGroup part)
    {
        visibility?.SetMode(DigitalTwinMode.Service);
        if (servicePanel != null && servicePanel.TryGetComponent(out ServicePanelView view))
            view.ShowDetail(part);
    }

    private void LateUpdate() => ApplyLayout();

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
        float blend = visibility != null ? visibility.ServiceBlend : 0f;
        // Hidden and already placed hidden: nothing to move.
        if (blend <= 0f && placedBlend == 0f)
            return;
        placedBlend = blend;

        if (servicePanel != null && servicePanel.parent is RectTransform space)
        {
            Rect area = space.rect;
            float top = TopBand(space);
            float scale = Mathf.Max(1f, panelScale);
            float width = Mathf.Max(0f, area.width / scale);
            float height = Mathf.Max(0f, (area.height - top) / scale);
            float rise = (1f - TelemetryVisibilityState.Ease(blend)) * slideDistance;

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
