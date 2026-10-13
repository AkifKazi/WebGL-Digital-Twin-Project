using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>What a control means, beyond what the pointer is doing.</summary>
public enum UITone
{
    Normal,
    Warning,
    Critical,
    Unavailable
}

/// <summary>
/// Gives a control its design-system states with no sprites and no code: put
/// it on the control, next to its Button or Toggle, and every
/// <see cref="UIShape"/> under it that names a component shows that
/// component's state.
///
///   Disabled    the Selectable is not interactable;
///   Selected    <see cref="Selected"/> is on, or a Toggle on it is on;
///   Warning, Critical, Unavailable   from <see cref="Tone"/>;
///   Idle        otherwise.
///
/// Hover and press are reported on top, and the component answers for its
/// state: a hovered button lights from its bottom edge, a hovered warning card
/// brightens a little, a selected or disabled one stays as it is. Touch never
/// hovers: a finger has no hover, and one left after a tap reads as stuck.
///
/// Its label, a <see cref="UITextStyle"/> in the Button style, takes the
/// control colours for the state (<see cref="UIControlColors"/>).
/// </summary>
// Runs in edit mode too, so a control shows its state while it is authored.
[ExecuteAlways]
[DisallowMultipleComponent]
[AddComponentMenu("UI/Digital Twin Control State")]
public sealed class UIControlState : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [Tooltip("Shows the component's Selected state, such as the chosen segment or tab.")]
    [SerializeField] private bool selected;

    [Tooltip("Shows the component's alarm or unavailable state.")]
    [SerializeField] private UITone tone = UITone.Normal;

    private Selectable selectable;
    private Toggle toggle;
    private bool hovered;
    private bool pressed;
    private bool keyboardFocus;
    private bool dirty = true;
    private UIState applied = (UIState)(-1);
    private bool appliedHover;
    private bool appliedPress;

    public bool Selected
    {
        get => selected;
        set
        {
            if (selected == value)
                return;
            selected = value;
            Apply(force: true);
        }
    }

    public UITone Tone
    {
        get => tone;
        set
        {
            if (tone == value)
                return;
            tone = value;
            Apply(force: true);
        }
    }

    /// <summary>True while a mouse is over the control and it answers.</summary>
    public bool IsHovered => hovered && Answers;

    /// <summary>
    /// Set by a keyboard target that owns this control (a service row) while
    /// the keyboard is on it. A Selectable control needs no call: it shows its
    /// hover look while the keyboard has it selected.
    /// </summary>
    public void SetKeyboardFocus(bool focused)
    {
        if (keyboardFocus == focused)
            return;
        keyboardFocus = focused;
        dirty = true;
    }

    /// <summary>The keyboard, not the pointer, is on this control.</summary>
    private bool KeyboardFocused =>
        keyboardFocus || (KeyboardNavigator.KeyboardMode && selectable != null && EventSystem.current != null &&
                          EventSystem.current.currentSelectedGameObject == gameObject);

    /// <summary>The state the control's shapes show now.</summary>
    public UIState State
    {
        get
        {
            if (selectable != null && !selectable.IsInteractable())
                return UIState.Disabled;
            if (selected || (toggle != null && toggle.isOn))
                return UIState.Selected;
            return tone switch
            {
                UITone.Warning => UIState.Warning,
                UITone.Critical => UIState.Critical,
                UITone.Unavailable => UIState.Unavailable,
                _ => UIState.Idle
            };
        }
    }

    private bool Answers => State is UIState.Idle or UIState.Warning or UIState.Critical;

    private void Awake()
    {
        selectable = GetComponent<Selectable>();
        toggle = selectable as Toggle;
    }

    private void OnEnable()
    {
        selectable = GetComponent<Selectable>();
        toggle = selectable as Toggle;
        dirty = true;
        Apply(force: true);
    }

    private void OnDisable()
    {
        hovered = false;
        pressed = false;
        Apply(force: true);
    }

    private void OnValidate() => dirty = true;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (UIPointerUtility.IsTouch(eventData))
            return;
        hovered = true;
        dirty = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        pressed = false;
        dirty = true;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;
        dirty = true;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressed = false;
        dirty = true;
    }

    // Interactable and toggles change from other scripts, so the state is
    // settled once per frame.
    private void LateUpdate() => Apply(force: false);

    /// <summary>Sends the state to the shapes now, rather than at the end of the frame.</summary>
    public void Refresh() => Apply(force: true);

    private void Apply(bool force)
    {
        UIState state = State;
        bool hover = (hovered || KeyboardFocused) && Answers;
        bool press = pressed && Answers;
        if (!force && !dirty && state == applied && hover == appliedHover && press == appliedPress)
            return;
        dirty = false;
        applied = state;
        appliedHover = hover;
        appliedPress = press;
        // Read each time: views add a control's children after it wakes.
        foreach (UIShape shape in GetComponentsInChildren<UIShape>(true))
        {
            // A control inside this one (a button on a card) sets its own shapes.
            if (shape.Component == null || shape.GetComponentInParent<UIControlState>(true) != this)
                continue;
            shape.State = state;
            shape.SetOwnerPointer(hover, press);
        }
        // Its label, in the Button text style, takes the control colours for the state.
        foreach (UITextStyle text in GetComponentsInChildren<UITextStyle>(true))
        {
            if (text.GetComponentInParent<UIControlState>(true) == this)
                text.Apply();
        }
    }
}
