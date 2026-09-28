using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Marks parts on the model in the service view: a numbered pin, matching the
/// part's row, on every part that needs attention, and on the part whose row
/// the pointer is over, which is also outlined. Parts on track carry no pin,
/// so the model stays quiet until something is due.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(60)]
public sealed class ServicePinLayer : MonoBehaviour
{
    [SerializeField] private ServiceModeController controller;
    [SerializeField] private ServicePanelView panel;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private TMP_FontAsset font;

    [SerializeField, Min(8f)] private float pinSize = 36f;
    [SerializeField] private Color onPin = new(0.024f, 0.075f, 0.102f, 1f);
    [SerializeField] private Color hoverRing = new(0.918f, 0.992f, 1f, 1f);

    [Tooltip("Seconds for the hovered part's outline to appear or go.")]
    [SerializeField, Min(0f)] private float outlineFadeSeconds = 0.15f;

    private readonly List<Pin> pins = new();
    private readonly List<ServiceItemStatus> shown = new();
    private RectTransform layer;
    private Canvas rootCanvas;

    private MachinePartGroup outlinedPart;
    private Color outlineColor;
    private float outlineStrength;

    private sealed class Pin
    {
        public RectTransform Root;
        public UIFrameGraphic Fill;
        public UIFrameGraphic Ring;
        public TextMeshProUGUI Number;
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
        bool service = controller != null && controller.CurrentMode == DigitalTwinMode.Service;
        MachinePartGroup hovered = service && panel != null ? panel.HoveredPart : null;

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
            if (item.Part == hovered || panel.NeedsAttention(item.Part))
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
                continue;

            ServiceItemStatus item = shown[i];
            Color state = panel.StateColor(item.State);
            pin.Fill.SetColors(Color.clear, state);
            pin.Ring.gameObject.SetActive(item.Part == hovered);
            if (panel.TryGetNumber(item.Part, out int number))
            {
                string text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (pin.Number.text != text)
                    pin.Number.text = text;
            }
        }
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

        RectTransform ring = CreateRect("Hover Ring", pin.Root);
        ring.anchorMin = Vector2.zero;
        ring.anchorMax = Vector2.one;
        ring.offsetMin = new Vector2(-6f, -6f);
        ring.offsetMax = new Vector2(6f, 6f);
        pin.Ring = ring.gameObject.AddComponent<UIFrameGraphic>();
        pin.Ring.SetThickness(2.5f);
        pin.Ring.SetColors(hoverRing, Color.clear);
        pin.Ring.raycastTarget = false;

        pin.Fill = pin.Root.gameObject.AddComponent<UIFrameGraphic>();
        pin.Fill.SetThickness(0f);
        pin.Fill.raycastTarget = false;

        RectTransform label = CreateRect("Number", pin.Root);
        label.anchorMin = Vector2.zero;
        label.anchorMax = Vector2.one;
        label.offsetMin = label.offsetMax = Vector2.zero;
        pin.Number = label.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null)
            pin.Number.font = font;
        pin.Number.fontSize = pinSize * 0.58f;
        pin.Number.color = onPin;
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
