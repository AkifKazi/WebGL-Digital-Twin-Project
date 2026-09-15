using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shows a control's hover sprite while a mouse is over it. A control has three
/// looks that never mix: idle, hover, and selected or pressed. The hover sprite
/// only ever replaces the idle one, so a selected, pressed or unavailable
/// control never shows it. Touch never hovers: a finger has no hover, and a
/// hover left behind after a tap reads as a stuck state.
/// </summary>
[DisallowMultipleComponent]
public sealed class UIHoverSprite : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("The image that shows the control's idle, hover and selected sprites. " +
             "Empty uses the Selectable's target graphic.")]
    [SerializeField] private Image target;

    [SerializeField] private Sprite hoverSprite;

    [Tooltip("The sprite the target shows while the control is idle; the hover sprite only " +
             "replaces this one. Empty: whatever the target shows when the control wakes. " +
             "A target with no idle sprite at all stays hidden until hovered.")]
    [SerializeField] private Sprite idleSprite;

    [Tooltip("Optional. A graphic that marks the control as selected, such as an underline " +
             "or a pinned fill. While it shows, the control never hovers.")]
    [SerializeField] private Graphic selectedIndicator;

    private Selectable selectable;
    private bool initialised;
    private bool hiddenWhenIdle;
    private bool pointerInside;
    private bool pressed;
    private bool showing;

    /// <summary>True while the hover sprite is actually on screen.</summary>
    public bool IsShowingHover =>
        showing && target != null && target.isActiveAndEnabled && target.overrideSprite == hoverSprite;

    /// <summary>Sets the sprites from code (setup tools, runtime-built controls).</summary>
    public void Configure(Image image, Sprite hover, Sprite idle, Graphic selected)
    {
        target = image;
        hoverSprite = hover;
        idleSprite = idle;
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
        // A target that starts switched off is a dedicated hover backdrop: it is
        // only drawn while hovered, whatever sprite it was authored with.
        hiddenWhenIdle = !target.enabled;
        if (idleSprite == null)
            idleSprite = target.sprite;
        hiddenWhenIdle |= idleSprite == null;
        if (hiddenWhenIdle)
            target.enabled = false;
        initialised = true;
    }

    private void OnDisable()
    {
        pointerInside = false;
        pressed = false;
        Apply(false);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!UIPointerUtility.IsTouch(eventData))
            pointerInside = true;
    }

    public void OnPointerExit(PointerEventData eventData) => pointerInside = false;

    public void OnPointerDown(PointerEventData eventData) => pressed = true;

    public void OnPointerUp(PointerEventData eventData) => pressed = false;

    // Selected looks change from other scripts (a mode switch, a pin), and a
    // Selectable's own sprite swap runs in its event handlers, so the rule is
    // settled once per frame after both.
    private void LateUpdate()
    {
        if (!initialised || (!pointerInside && !showing))
            return;
        Apply(pointerInside && !pressed && IsIdle());
    }

    private bool IsIdle()
    {
        if (hoverSprite == null)
            return false;
        if (selectable != null && !selectable.IsInteractable())
            return false;
        if (selectedIndicator != null && selectedIndicator.enabled && selectedIndicator.gameObject.activeInHierarchy)
            return false;
        // A different base sprite is a selected look.
        if (target.sprite != idleSprite)
            return false;
        // Another sprite laid over the base (a pressed or selected swap) wins too.
        Sprite shown = target.overrideSprite;
        return shown == hoverSprite || shown == target.sprite;
    }

    private void Apply(bool show)
    {
        if (target == null)
            return;
        if (show)
        {
            target.overrideSprite = hoverSprite;
            if (hiddenWhenIdle)
                target.enabled = true;
        }
        else if (showing)
        {
            // Only undo our own sprite; a swap made since belongs to someone else.
            if (target.overrideSprite == hoverSprite)
                target.overrideSprite = null;
            if (hiddenWhenIdle)
                target.enabled = false;
        }
        showing = show;
    }
}
