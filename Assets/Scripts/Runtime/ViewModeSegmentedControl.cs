using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ViewModeSegmentedControl : MonoBehaviour
{
    [Header("View Controller")]
    [SerializeField]
    private HybridHopperClipController hopperController;

    [Header("Buttons")]
    [SerializeField]
    private Button exteriorButton;

    [SerializeField]
    private Button sectionButton;

    [Header("Background Images")]
    [SerializeField]
    private Image exteriorBackground;

    [SerializeField]
    private Image sectionBackground;

    [Header("Icons")]
    [SerializeField]
    private Image exteriorIcon;

    [SerializeField]
    private Image sectionIcon;

    [Header("Labels")]
    [SerializeField]
    private TMP_Text exteriorLabel;

    [SerializeField]
    private TMP_Text sectionLabel;

    [Header("Background Sprites")]
    [SerializeField]
    private Sprite selectedSprite;

    [SerializeField]
    private Sprite inactiveSprite;

    [Header("Selected Colours")]
    [SerializeField]
    private Color selectedTextColor =
        new Color32(234, 253, 255, 255);

    [SerializeField]
    private Color selectedIconColor =
        new Color32(54, 237, 255, 255);

    [Header("Inactive Colours")]
    [SerializeField]
    private Color inactiveTextColor =
        new Color32(104, 166, 175, 255);

    [SerializeField]
    private Color inactiveIconColor =
        new Color32(104, 166, 175, 255);

    private bool listenersAdded;

    private void Awake()
    {
        AddButtonListeners();
    }

    private void OnEnable()
    {
        SubscribeToController();
        RefreshFromController();
    }

    private void Start()
    {
        // Refresh again because the hopper controller may initialise
        // its starting state during Start().
        RefreshFromController();
    }

    private void OnDisable()
    {
        UnsubscribeFromController();
    }

    private void OnDestroy()
    {
        RemoveButtonListeners();
        UnsubscribeFromController();
    }

    private void AddButtonListeners()
    {
        if (listenersAdded)
            return;

        if (exteriorButton != null)
        {
            exteriorButton.onClick.AddListener(
                SelectExterior
            );
        }

        if (sectionButton != null)
        {
            sectionButton.onClick.AddListener(
                SelectSection
            );
        }

        listenersAdded = true;
    }

    private void RemoveButtonListeners()
    {
        if (!listenersAdded)
            return;

        if (exteriorButton != null)
        {
            exteriorButton.onClick.RemoveListener(
                SelectExterior
            );
        }

        if (sectionButton != null)
        {
            sectionButton.onClick.RemoveListener(
                SelectSection
            );
        }

        listenersAdded = false;
    }

    private void SubscribeToController()
    {
        if (hopperController == null)
            return;

        // Prevent duplicate subscriptions.
        hopperController.ViewStateChanged -=
            HandleViewStateChanged;

        hopperController.TransitionStateChanged -=
            HandleTransitionStateChanged;

        hopperController.ViewStateChanged +=
            HandleViewStateChanged;

        hopperController.TransitionStateChanged +=
            HandleTransitionStateChanged;
    }

    private void UnsubscribeFromController()
    {
        if (hopperController == null)
            return;

        hopperController.ViewStateChanged -=
            HandleViewStateChanged;

        hopperController.TransitionStateChanged -=
            HandleTransitionStateChanged;
    }

    public void SelectExterior()
    {
        if (hopperController == null ||
            hopperController.IsTransitioning)
        {
            return;
        }

        hopperController.ShowFull();
    }

    public void SelectSection()
    {
        if (hopperController == null ||
            hopperController.IsTransitioning)
        {
            return;
        }

        hopperController.ShowCrossSection();
    }

    private void HandleViewStateChanged(
        bool isCrossSection)
    {
        ApplyVisualState(isCrossSection);
    }

    private void HandleTransitionStateChanged(
        bool isTransitioning)
    {
        ApplyInteractionState(isTransitioning);
    }

    private void RefreshFromController()
    {
        if (hopperController == null)
        {
            ApplyVisualState(false);
            ApplyInteractionState(true);
            return;
        }

        ApplyVisualState(
            hopperController.IsCrossSection
        );

        ApplyInteractionState(
            hopperController.IsTransitioning
        );
    }

    private void ApplyVisualState(
        bool isCrossSection)
    {
        bool exteriorSelected = !isCrossSection;
        bool sectionSelected = isCrossSection;

        ApplySegmentVisual(
            exteriorSelected,
            exteriorBackground,
            exteriorIcon,
            exteriorLabel
        );

        ApplySegmentVisual(
            sectionSelected,
            sectionBackground,
            sectionIcon,
            sectionLabel
        );
    }

    private void ApplySegmentVisual(
        bool selected,
        Image background,
        Image icon,
        TMP_Text label)
    {
        if (background != null)
        {
            background.sprite =
                selected
                    ? selectedSprite
                    : inactiveSprite;

            // Preserve the colours authored into the sprites.
            background.color = Color.white;
        }

        if (icon != null)
        {
            icon.color =
                selected
                    ? selectedIconColor
                    : inactiveIconColor;
        }

        if (label != null)
        {
            label.color =
                selected
                    ? selectedTextColor
                    : inactiveTextColor;
        }
    }

    private void ApplyInteractionState(
        bool isTransitioning)
    {
        if (hopperController == null)
        {
            if (exteriorButton != null)
                exteriorButton.interactable = false;

            if (sectionButton != null)
                sectionButton.interactable = false;

            return;
        }

        if (isTransitioning)
        {
            // Lock both controls while the model is transitioning.
            if (exteriorButton != null)
                exteriorButton.interactable = false;

            if (sectionButton != null)
                sectionButton.interactable = false;

            return;
        }

        bool isCrossSection =
            hopperController.IsCrossSection;

        // The selected mode cannot be selected again.
        if (exteriorButton != null)
        {
            exteriorButton.interactable =
                isCrossSection;
        }

        if (sectionButton != null)
        {
            sectionButton.interactable =
                !isCrossSection;
        }
    }
}