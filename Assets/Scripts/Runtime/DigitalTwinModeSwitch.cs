using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Telemetry / Service segmented control in the top bar. Styled like the
/// view-mode control: the selected segment takes the outlined sprite and cannot
/// be pressed again, the other stays idle and shows the hover sprite.
///
/// Drives its layout's <see cref="TelemetryVisibility"/>.
/// </summary>
[DisallowMultipleComponent]
public sealed class DigitalTwinModeSwitch : MonoBehaviour
{
    [Tooltip("A component implementing IDigitalTwinModeController: the layout's Telemetry Visibility.")]
    [SerializeField] private MonoBehaviour controller;
    [SerializeField] private Button telemetryButton;
    [SerializeField] private Button serviceButton;

    [Header("Look")]
    [SerializeField] private Sprite selectedSprite;
    [SerializeField] private Sprite idleSprite;
    [SerializeField] private Color selectedTextColor = new Color32(234, 253, 255, 255);
    [SerializeField] private Color idleTextColor = new Color32(104, 166, 175, 255);

    private IDigitalTwinModeController Controller => controller as IDigitalTwinModeController;

    public Button TelemetryButton => telemetryButton;
    public Button ServiceButton => serviceButton;

    private void Awake()
    {
        BindLabel(telemetryButton, "mode.telemetry");
        BindLabel(serviceButton, "mode.service");
    }

    // The authored label text is the English fallback.
    private static void BindLabel(Button button, string key)
    {
        TMP_Text label = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        if (label == null || label.TryGetComponent(out UITextBinding _))
            return;
        UITextBinding.Bind(label, key, label.text);
        // A longer translation shrinks inside its segment rather than running into the other.
        UITextBinding.AllowShrinkToFit(label, 0.6f);
    }

    private void OnEnable()
    {
        if (telemetryButton != null)
            telemetryButton.onClick.AddListener(SelectTelemetry);
        if (serviceButton != null)
            serviceButton.onClick.AddListener(SelectService);
        if (Controller != null)
        {
            Controller.ModeChanged += Apply;
            Apply(Controller.CurrentMode);
        }
    }

    private void OnDisable()
    {
        if (telemetryButton != null)
            telemetryButton.onClick.RemoveListener(SelectTelemetry);
        if (serviceButton != null)
            serviceButton.onClick.RemoveListener(SelectService);
        if (Controller != null)
            Controller.ModeChanged -= Apply;
    }

    public void SelectTelemetry() => Controller?.SetMode(DigitalTwinMode.Telemetry);

    public void SelectService() => Controller?.SetMode(DigitalTwinMode.Service);

    private void Apply(DigitalTwinMode mode)
    {
        ApplySegment(telemetryButton, mode == DigitalTwinMode.Telemetry);
        ApplySegment(serviceButton, mode == DigitalTwinMode.Service);
    }

    private void ApplySegment(Button button, bool selected)
    {
        if (button == null)
            return;

        if (button.targetGraphic is Image background)
        {
            background.sprite = selected ? selectedSprite : idleSprite;
            // Preserve the colours authored into the sprites.
            background.color = Color.white;
        }

        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.color = selected ? selectedTextColor : idleTextColor;

        // The selected view cannot be selected again.
        button.interactable = !selected;
    }
}
