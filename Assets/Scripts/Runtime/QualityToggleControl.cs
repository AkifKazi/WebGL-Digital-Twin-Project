using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HD button in the bottom controls. It raises the graphics preset on a
/// desktop browser and hides itself on phones, which stay on the light preset.
/// The three looks follow the other bottom controls: the idle sprite, the hover
/// sprite while a mouse is over it, and the pressed sprite while HD is on.
/// </summary>
[DisallowMultipleComponent]
public sealed class QualityToggleControl : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The quality controller. Empty finds the one in the scene.")]
    [SerializeField] private AdaptiveQualityController quality;

    [Tooltip("The HD button itself.")]
    [SerializeField] private Button button;

    [Tooltip("The button's background, which shows the idle and pressed sprites.")]
    [SerializeField] private Image background;

    [Tooltip("The HD icon.")]
    [SerializeField] private Image icon;

    [Tooltip("Optional label beside the icon.")]
    [SerializeField] private TMP_Text label;

    [Tooltip("The whole control, hidden on phones. Empty hides this object.")]
    [SerializeField] private GameObject controlRoot;

    [Header("Sprites")]
    [Tooltip("Shown while HD is off.")]
    [SerializeField] private Sprite idleSprite;

    [Tooltip("Shown while HD is on, the same sprite the selected view-mode segment uses.")]
    [SerializeField] private Sprite selectedSprite;

    [Header("Colours")]
    [SerializeField] private Color onIconColor = new(0.212f, 0.929f, 1f, 1f);
    [SerializeField] private Color offIconColor = new(0.408f, 0.651f, 0.686f, 1f);
    [SerializeField] private Color onTextColor = new(0.918f, 0.992f, 1f, 1f);
    [SerializeField] private Color offTextColor = new(0.408f, 0.651f, 0.686f, 1f);

    private void Awake()
    {
        if (quality == null)
            quality = FindAnyObjectByType<AdaptiveQualityController>(FindObjectsInactive.Include);
        if (button == null)
            button = GetComponentInChildren<Button>(true);
    }

    private void OnEnable()
    {
        if (button != null)
            button.onClick.AddListener(Toggle);
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
        if (button != null)
            button.onClick.RemoveListener(Toggle);
        if (quality != null)
            quality.QualityChanged -= Apply;
    }

    private void Toggle()
    {
        if (quality != null)
            quality.ToggleHighQuality();
    }

    private void Apply()
    {
        bool on = quality != null && quality.HighQualityEnabled;

        if (background != null)
        {
            // The idle sprite stays the base: UIHoverSprite lays the hover sprite
            // over it, and it must never show while the pressed look is on.
            background.sprite = on ? selectedSprite : idleSprite;
            background.color = Color.white;
        }
        if (icon != null)
            icon.color = on ? onIconColor : offIconColor;
        if (label != null)
            label.color = on ? onTextColor : offTextColor;
    }
}
