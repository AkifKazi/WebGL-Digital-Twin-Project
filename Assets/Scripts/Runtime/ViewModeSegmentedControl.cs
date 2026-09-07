using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ViewModeSegmentedControl : MonoBehaviour
{
    [Header("View Controller")]
    [SerializeField]
    private HybridHopperClipController hopperController;

    [Tooltip("Optional three-state controller. When assigned it drives this " +
             "control and the X-Ray segment becomes available. Left empty, " +
             "the control keeps its original two-segment behaviour.")]
    [SerializeField]
    private MachineViewModeController viewModeController;

    [Header("Buttons")]
    [SerializeField]
    private Button exteriorButton;

    [SerializeField]
    private Button sectionButton;

    [SerializeField]
    private Button xrayButton;

    [Header("Background Images")]
    [SerializeField]
    private Image exteriorBackground;

    [SerializeField]
    private Image sectionBackground;

    [SerializeField]
    private Image xrayBackground;

    [Header("Icons")]
    [SerializeField]
    private Image exteriorIcon;

    [SerializeField]
    private Image sectionIcon;

    [SerializeField]
    private Image xrayIcon;

    [Header("Labels")]
    [SerializeField]
    private TMP_Text exteriorLabel;

    [SerializeField]
    private TMP_Text sectionLabel;

    [SerializeField]
    private TMP_Text xrayLabel;

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

    [Header("Segment copy")]
    [Tooltip("Label for the exterior segment. Portrait layouts use shorter copy so three segments fit.")]
    [SerializeField]
    private string exteriorText = "FULL BODY";

    [Tooltip("Label for the cross-section segment.")]
    [SerializeField]
    private string sectionText = "CROSS SECTION";

    [Tooltip("Label for the X-Ray segment.")]
    [SerializeField]
    private string xrayText = "X-RAY";

    [Tooltip("Applies the copy above on enable. Turn off to keep whatever is authored on the labels.")]
    [SerializeField]
    private bool applySegmentText = true;

    [Header("Segment icons")]
    [Tooltip("Shows the segment icons. Turn off on narrow layouts to give the labels room.")]
    [SerializeField]
    private bool showIcons = true;

    private bool listenersAdded;

    private void Awake()
    {
        AddButtonListeners();
    }

    private void OnEnable()
    {
        ApplySegmentPresentation();
        SubscribeToController();
        RefreshFromController();
    }

    /// <summary>
    /// Pushes the authored copy and icon visibility onto the segments. Wide and
    /// portrait layouts are separate instances, so each carries its own values
    /// and no runtime breakpoint logic is needed.
    /// </summary>
    private void ApplySegmentPresentation()
    {
        if (applySegmentText)
        {
            SetLabelText(exteriorLabel, exteriorText);
            SetLabelText(sectionLabel, sectionText);
            SetLabelText(xrayLabel, xrayText);
        }

        SetIconVisible(exteriorIcon);
        SetIconVisible(sectionIcon);
        SetIconVisible(xrayIcon);
    }

    private static void SetLabelText(TMP_Text label, string text)
    {
        if (label != null && !string.IsNullOrEmpty(text))
            label.text = text;
    }

    private void SetIconVisible(Image icon)
    {
        if (icon == null || icon.gameObject.activeSelf == showIcons)
            return;

        // Deactivating the object also removes it from the layout group, which
        // is what frees the horizontal space on narrow screens.
        icon.gameObject.SetActive(showIcons);
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

        if (xrayButton != null)
        {
            xrayButton.onClick.AddListener(
                SelectXRay
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

        if (xrayButton != null)
        {
            xrayButton.onClick.RemoveListener(
                SelectXRay
            );
        }

        listenersAdded = false;
    }

    private void SubscribeToController()
    {
        if (viewModeController != null)
        {
            viewModeController.ModeChanged -= HandleModeChanged;
            viewModeController.TransitionStateChanged -= HandleTransitionStateChanged;

            viewModeController.ModeChanged += HandleModeChanged;
            viewModeController.TransitionStateChanged += HandleTransitionStateChanged;

            // The three-state controller is the single source of truth when
            // present, so the two-state events are not needed as well.
            return;
        }

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
        if (viewModeController != null)
        {
            viewModeController.ModeChanged -= HandleModeChanged;
            viewModeController.TransitionStateChanged -= HandleTransitionStateChanged;
            return;
        }

        if (hopperController == null)
            return;

        hopperController.ViewStateChanged -=
            HandleViewStateChanged;

        hopperController.TransitionStateChanged -=
            HandleTransitionStateChanged;
    }

    public void SelectExterior()
    {
        if (viewModeController != null)
        {
            if (!viewModeController.IsTransitioning)
                viewModeController.SelectFullModel();

            return;
        }

        if (hopperController == null ||
            hopperController.IsTransitioning)
        {
            return;
        }

        hopperController.ShowFull();
    }

    public void SelectSection()
    {
        if (viewModeController != null)
        {
            if (!viewModeController.IsTransitioning)
                viewModeController.SelectCrossSection();

            return;
        }

        if (hopperController == null ||
            hopperController.IsTransitioning)
        {
            return;
        }

        hopperController.ShowCrossSection();
    }

    public void SelectXRay()
    {
        if (viewModeController == null ||
            viewModeController.IsTransitioning)
        {
            return;
        }

        viewModeController.SelectXRay();
    }

    private void HandleModeChanged(MachineViewMode mode)
    {
        ApplyVisualState(mode);
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
        if (viewModeController != null)
        {
            ApplyVisualState(viewModeController.CurrentMode);
            ApplyInteractionState(viewModeController.IsTransitioning);
            return;
        }

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
        ApplyVisualState(
            isCrossSection
                ? MachineViewMode.CrossSection
                : MachineViewMode.FullModel
        );
    }

    private void ApplyVisualState(MachineViewMode mode)
    {
        ApplySegmentVisual(
            mode == MachineViewMode.FullModel,
            exteriorBackground,
            exteriorIcon,
            exteriorLabel
        );

        ApplySegmentVisual(
            mode == MachineViewMode.CrossSection,
            sectionBackground,
            sectionIcon,
            sectionLabel
        );

        ApplySegmentVisual(
            mode == MachineViewMode.XRay,
            xrayBackground,
            xrayIcon,
            xrayLabel
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
        if (viewModeController != null)
        {
            MachineViewMode mode = viewModeController.CurrentMode;

            // Every segment locks while a view transition runs, and the active
            // segment cannot be selected again.
            SetInteractable(exteriorButton, !isTransitioning && mode != MachineViewMode.FullModel);
            SetInteractable(sectionButton, !isTransitioning && mode != MachineViewMode.CrossSection);
            SetInteractable(xrayButton, !isTransitioning && mode != MachineViewMode.XRay);
            return;
        }

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

    private static void SetInteractable(Button button, bool interactable)
    {
        if (button != null)
            button.interactable = interactable;
    }
}