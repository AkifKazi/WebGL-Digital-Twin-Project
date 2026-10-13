using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Lights a control while a mouse is over it, and while it is pressed. The
/// control's <see cref="UIShape"/>s are told, and each one's component
/// answers with its hover or pressed look for its state: a segment or card at
/// rest lights up, an icon glows, a warning or critical card brightens a
/// little, and a selected, disabled or unavailable one stays as it is unless
/// its component has a look for that. Touch never hovers: a finger has no
/// hover, and one left after a tap reads as stuck.
///
/// A control can have a hover backdrop that is drawn only while it is
/// hovered (<see cref="OnlyWhileHovered"/>), such as the square behind the
/// detail panel's pin and close icons.
///
/// (Named for the sprite it used to swap in; the look now comes from the
/// design system's components.)
/// </summary>
[DisallowMultipleComponent]
public sealed class UIHoverSprite : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("The control's shape. Empty uses the Selectable's target graphic.")]
    [SerializeField] private Image target;

    [Tooltip("The target is a hover backdrop: drawn only while the control is hovered, not at rest. " +
             "A target switched off in the hierarchy is treated the same.")]
    [SerializeField] private bool onlyWhileHovered;

    [Tooltip("Optional. A graphic that marks the control as selected, such as an underline " +
             "or a pinned fill. While it shows, the control never hovers.")]
    [SerializeField] private Graphic selectedIndicator;

    private Selectable selectable;
    private bool initialised;
    private bool hiddenWhenIdle;
    private bool pointerInside;
    private bool pressed;
    private bool showing;
    private bool reportedHover;
    private bool reportedPress;

    /// <summary>True while the control shows its hover look.</summary>
    public bool IsShowingHover => showing && target != null && target.isActiveAndEnabled;

    /// <summary>The target is drawn only while hovered.</summary>
    public bool OnlyWhileHovered => onlyWhileHovered;

    public Image Target => target;

    /// <summary>Sets it up from code (setup tools, runtime-built controls).</summary>
    public void Configure(Image image, bool drawnOnlyWhileHovered, Graphic selected)
    {
        target = image;
        onlyWhileHovered = drawnOnlyWhileHovered;
        selectedIndicator = selected;
        initialised = false;
        Initialise();
    }

    private void Awake() => Initialise();

    private void Initialise()
    {
        if (initialised)
            return;
        selectable = GetComponent<Selectable>();
        if (target == null && selectable != null)
            target = selectable.targetGraphic as Image;
        if (target == null)
            return;
        hiddenWhenIdle = onlyWhileHovered || !target.enabled;
        if (hiddenWhenIdle)
            target.enabled = false;
        initialised = true;
    }

    private void OnDisable()
    {
        pointerInside = false;
        pressed = false;
        Apply(false);
        Report(false, false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!UIPointerUtility.IsTouch(eventData))
            pointerInside = true;
    }

    public void OnPointerExit(PointerEventData eventData) => pointerInside = false;

    public void OnPointerDown(PointerEventData eventData) => pressed = true;

    public void OnPointerUp(PointerEventData eventData) => pressed = false;

    // Selected looks change from other scripts (a mode switch, a pin), so the
    // rule is settled once per frame after them.
    private void LateUpdate()
    {
        if (!initialised || (!pointerInside && !pressed && !showing && !reportedHover && !reportedPress))
            return;
        bool usable = selectable == null || selectable.IsInteractable();
        Apply(pointerInside && !pressed && IsIdle());
        Report(pointerInside && !pressed && usable, pressed && pointerInside && usable);
    }

    private void Report(bool hovered, bool pressing)
    {
        if (hovered == reportedHover && pressing == reportedPress)
            return;
        reportedHover = hovered;
        reportedPress = pressing;
        // Read on each change: views add a control's children after it wakes.
        foreach (UIShape shape in GetComponentsInChildren<UIShape>(true))
            shape.SetOwnerPointer(hovered, pressing);
        if (target is UIShape own && !own.transform.IsChildOf(transform))
            own.SetOwnerPointer(hovered, pressing);
    }

    /// <summary>At rest: usable, not marked selected, and its shape in its idle state.</summary>
    private bool IsIdle()
    {
        if (target == null)
            return false;
        if (selectable != null && !selectable.IsInteractable())
            return false;
        if (selectedIndicator != null && selectedIndicator.enabled && selectedIndicator.gameObject.activeInHierarchy)
            return false;
        return target is not UIShape shape || shape.State is UIState.Idle or UIState.Hover;
    }

    private void Apply(bool show)
    {
        if (target == null)
            return;
        if (hiddenWhenIdle && show != showing)
            target.enabled = show;
        showing = show;
    }
}
