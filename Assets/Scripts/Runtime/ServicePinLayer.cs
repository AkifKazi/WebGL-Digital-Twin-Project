using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Marks parts on the model in the service view: a numbered pin, matching the
/// part's row, on every part due soon or overdue, and on the part whose row
/// the pointer is over or whose detail is open, which is also outlined. Parts on track carry no pin,
/// so the model stays quiet until something is due.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(60)]
public sealed class ServicePinLayer : MonoBehaviour
{
    [SerializeField] private TelemetryVisibility visibility;
    [SerializeField] private ServicePanelView panel;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private TMP_FontAsset font;

    [SerializeField, Min(8f)] private float pinSize = 34f;

    [Tooltip("Seconds for the hovered pin's selection frame to settle in.")]
    [SerializeField, Min(0f)] private float selectSeconds = 0.14f;

    [Tooltip("Seconds for the hovered part's outline to appear or go.")]
    [SerializeField, Min(0f)] private float outlineFadeSeconds = 0.15f;

    private readonly List<Pin> pins = new();
    private readonly List<ServiceItemStatus> shown = new();
    private readonly List<Pin> stack = new();
    private RectTransform layer;
    private Canvas rootCanvas;

    private MachinePartGroup outlinedPart;
    private Color outlineColor;
    private float outlineStrength;

    private sealed class Pin
    {
        public RectTransform Root;
        public UIServicePinGraphic Graphic;
        public TextMeshProUGUI Number;
        public float Selected;
    }

    private void Awake()
    {
        layer = (RectTransform)transform;
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void OnDisable()
    {
        foreach (Pin pin in pins)
            pin.Root.gameObject.SetActive(false);
        ReleaseOutline();
    }

    private void LateUpdate()
    {
        bool service = visibility != null && visibility.CurrentMode == DigitalTwinMode.Service;
        MachinePartGroup hovered = service && panel != null ? panel.HighlightedPart : null;

        CollectShown(service, hovered);
        PlacePins(hovered);
        UpdateOutline(hovered);
    }

    private void CollectShown(bool service, MachinePartGroup hovered)
    {
        shown.Clear();
        if (!service || panel == null)
            return;

        foreach (ServiceItemStatus item in panel.Items)
        {
            if (item.Part == hovered || item.State != ServiceState.OnTrack)
                shown.Add(item);
        }
    }

    private void PlacePins(MachinePartGroup hovered)
    {
        while (pins.Count < shown.Count)
            pins.Add(CreatePin(pins.Count));

        Camera uiCamera = UICamera();
        for (int i = 0; i < pins.Count; i++)
        {
            Pin pin = pins[i];
            bool visible = i < shown.Count && TryPlace(pin, shown[i], uiCamera);
            if (pin.Root.gameObject.activeSelf != visible)
                pin.Root.gameObject.SetActive(visible);
            if (!visible)
            {
                pin.Selected = 0f;
                continue;
            }

            ServiceItemStatus item = shown[i];
            Color state = panel.StateColor(item.State);
            pin.Graphic.SetState(state, item.State != ServiceState.OnTrack);
            if (pin.Number.color != state)
                pin.Number.color = state;

            float goal = item.Part == hovered ? 1f : 0f;
            pin.Selected = selectSeconds <= 0f
                ? goal
                : Mathf.MoveTowards(pin.Selected, goal, Time.unscaledDeltaTime / selectSeconds);
            pin.Graphic.SetSelected(pin.Selected);
            if (panel.TryGetNumber(item.Part, out int number))
            {
                string text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (pin.Number.text != text)
                    pin.Number.text = text;
            }
        }

        Stack(hovered);
    }

    /// <summary>
    /// Where pins overlap, the soonest due draws on top, and the hovered one
    /// above them all.
    /// </summary>
    private void Stack(MachinePartGroup hovered)
    {
        stack.Clear();
        Pin top = null;
        for (int i = pins.Count - 1; i >= 0; i--)
        {
            if (hovered != null && i < shown.Count && shown[i].Part == hovered)
                top = pins[i];
            else
                stack.Add(pins[i]);
        }
        if (top != null)
            stack.Add(top);

        bool ordered = layer.childCount == stack.Count;
        for (int i = 0; ordered && i < stack.Count; i++)
            ordered = layer.GetChild(i) == stack[i].Root;
        if (ordered)
            return;
        foreach (Pin pin in stack)
            pin.Root.SetAsLastSibling();
    }

    private bool TryPlace(Pin pin, in ServiceItemStatus item, Camera uiCamera)
    {
        if (targetCamera == null || item.Part == null || !item.Part.TryGetWorldBounds(out Bounds bounds))
            return false;

        Vector3 screen = targetCamera.WorldToScreenPoint(bounds.center);
        if (screen.z <= 0f)
            return false;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, uiCamera, out Vector2 local))
            return false;

        pin.Root.anchoredPosition = local;
        return true;
    }

    private Camera UICamera()
    {
        if (rootCanvas == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            rootCanvas = canvas != null ? canvas.rootCanvas : null;
        }
        return rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera
            : null;
    }

    // -----------------------------------------------------------------------
    // Outline of the hovered part
    // -----------------------------------------------------------------------

    private void UpdateOutline(MachinePartGroup hovered)
    {
        if (hovered != null && hovered != outlinedPart)
        {
            outlinedPart = hovered;
            SelectionOutline.SetTargets(hovered.Renderers);
            foreach (ServiceItemStatus item in shown)
            {
                if (item.Part == hovered)
                    outlineColor = panel.StateColor(item.State);
            }
        }

        if (outlinedPart == null)
            return;

        float goal = hovered == outlinedPart ? 1f : 0f;
        outlineStrength = outlineFadeSeconds <= 0f
            ? goal
            : Mathf.MoveTowards(outlineStrength, goal, Time.unscaledDeltaTime / outlineFadeSeconds);

        if (outlineStrength <= 0f)
        {
            ReleaseOutline();
            return;
        }

        // The card highlighter clears the shared outline when its own fade
        // ends; claim the targets back if that happened under us.
        if (SelectionOutline.Targets.Count == 0)
            SelectionOutline.SetTargets(outlinedPart.Renderers);
        SelectionOutline.SetAppearance(outlineColor, outlineStrength);
    }

    private void ReleaseOutline()
    {
        if (outlinedPart == null)
            return;
        outlinedPart = null;
        outlineStrength = 0f;
        SelectionOutline.Clear();
    }

    private Pin CreatePin(int index)
    {
        Pin pin = new() { Root = CreateRect($"Pin {index + 1}", layer) };
        pin.Root.anchorMin = pin.Root.anchorMax = new Vector2(0.5f, 0.5f);
        pin.Root.pivot = new Vector2(0.5f, 0.5f);
        pin.Root.sizeDelta = new Vector2(pinSize, pinSize);

        pin.Graphic = pin.Root.gameObject.AddComponent<UIServicePinGraphic>();
        pin.Graphic.raycastTarget = false;

        // Clear of the lit bar down the left edge.
        RectTransform label = CreateRect("Number", pin.Root);
        label.anchorMin = Vector2.zero;
        label.anchorMax = Vector2.one;
        label.offsetMin = new Vector2(2f, 0f);
        label.offsetMax = Vector2.zero;
        pin.Number = label.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            pin.Number.font = font;
        pin.Number.fontSize = pinSize * 0.56f;
        pin.Number.alignment = TextAlignmentOptions.Center;
        pin.Number.textWrappingMode = TextWrappingModes.NoWrap;
        pin.Number.raycastTarget = false;
        return pin;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.layer = parent.gameObject.layer;
        RectTransform rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        return rect;
    }
}
