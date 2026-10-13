using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Telemetry / Service segmented control in the top bar. Styled like the
/// view-mode control: the selected segment takes the outlined sprite and cannot
/// be pressed again, the other stays idle and shows the hover sprite.
///
/// Drives its layout's <see cref="TelemetryVisibility"/>.
///
/// A further tab can join the bar (the portrait layout's Ask): it is styled
/// through <see cref="ApplyTab"/>, and while it is chosen
/// (<see cref="OtherTabChosen"/>) neither view shows as chosen and both can be
/// pressed. Choosing a view raises <see cref="ViewChosen"/>, so that tab gives way.
/// </summary>
[DisallowMultipleComponent]
public sealed class DigitalTwinModeSwitch : MonoBehaviour
{
    [Tooltip("A component implementing IDigitalTwinModeController: the layout's Telemetry Visibility.")]
    [SerializeField] private MonoBehaviour controller;
    [SerializeField] private Button telemetryButton;
    [SerializeField] private Button serviceButton;


    private bool otherTabChosen;

    private IDigitalTwinModeController Controller => controller as IDigitalTwinModeController;

    /// <summary>Raised when Telemetry or Service is chosen, by a press or a script, even the view already on show.</summary>
    public event Action<DigitalTwinMode> ViewChosen;

    /// <summary>A further tab in this bar is chosen: neither view shows as chosen, and either can be pressed to go back.</summary>
    public bool OtherTabChosen
    {
        get => otherTabChosen;
        set
        {
            if (otherTabChosen == value)
                return;
            otherTabChosen = value;
            if (Controller != null)
                Apply(Controller.CurrentMode);
        }
    }

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

    public void SelectTelemetry() => Select(DigitalTwinMode.Telemetry);

    public void SelectService() => Select(DigitalTwinMode.Service);

    private void Select(DigitalTwinMode mode)
    {
        Controller?.SetMode(mode);
        ViewChosen?.Invoke(mode);
    }

    /// <summary>Styles a further tab in this bar as its own segments are: chosen, or idle.</summary>
    public void ApplyTab(Button tab, bool chosen) => ApplySegment(tab, chosen);

    private void Apply(DigitalTwinMode mode)
    {
        ApplySegment(telemetryButton, !otherTabChosen && mode == DigitalTwinMode.Telemetry);
        ApplySegment(serviceButton, !otherTabChosen && mode == DigitalTwinMode.Service);
    }

    private void ApplySegment(Button button, bool selected)
    {
        if (button == null)
            return;

        // The segment's shape: the design system's Segment, chosen or at rest.
        UIShape.ShowChosen(button.targetGraphic, selected);

        // Labels in the design system's control colours (UIControlColors).
        TMP_Text label = button.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
            label.color = UIControlColors.For(selected);

        // The selected view cannot be selected again.
        button.interactable = !selected;
    }
}
