using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The SD / HD segmented control in the bottom controls. Styled like the
/// view-mode and Telemetry / Service controls: the selected segment takes the
/// outlined sprite and cannot be pressed again, the other stays idle and shows
/// the hover sprite. SD is the light preset every device starts on; HD raises
/// the graphics preset on a desktop browser. Phones never show the control,
/// because they stay on the light preset.
/// </summary>
[DisallowMultipleComponent]
public sealed class QualityToggleControl : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The quality controller. Empty finds the one in the scene.")]
    [SerializeField] private AdaptiveQualityController quality;

    [SerializeField] private Button sdButton;
    [SerializeField] private Button hdButton;

    [Tooltip("The whole control, hidden on phones. Empty hides this object.")]
    [SerializeField] private GameObject controlRoot;


    public Button SdButton => sdButton;
    public Button HdButton => hdButton;

    private void Awake()
    {
        if (quality == null)
            quality = FindAnyObjectByType<AdaptiveQualityController>(FindObjectsInactive.Include);
    }

    private void OnEnable()
    {
        if (sdButton != null)
            sdButton.onClick.AddListener(SelectSD);
        if (hdButton != null)
            hdButton.onClick.AddListener(SelectHD);
        if (quality != null)
            quality.QualityChanged += Apply;

        // Phones never show the control: the light preset is not a choice there.
        GameObject root = controlRoot != null ? controlRoot : gameObject;
        bool available = quality != null && quality.IsDesktop;
        if (root.activeSelf != available)
            root.SetActive(available);

        Apply();
    }

    private void OnDisable()
    {
        if (sdButton != null)
            sdButton.onClick.RemoveListener(SelectSD);
        if (hdButton != null)
            hdButton.onClick.RemoveListener(SelectHD);
        if (quality != null)
            quality.QualityChanged -= Apply;
    }

    public void SelectSD() => SetHighQuality(false);

    public void SelectHD() => SetHighQuality(true);

    private void SetHighQuality(bool enabled)
    {
        if (quality != null)
            quality.SetHighQuality(enabled);
    }

    private void Apply()
    {
        bool hd = quality != null && quality.HighQualityEnabled;
        ApplySegment(sdButton, !hd);
        ApplySegment(hdButton, hd);
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

        // The selected preset cannot be selected again.
        button.interactable = !selected;
    }
}
