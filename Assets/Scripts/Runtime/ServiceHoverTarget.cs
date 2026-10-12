using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Reports the pointer entering and leaving one service row,
/// so the part it lists can be marked on the model, and clicks on it, which
/// open the part's detail. Touch never hovers, but a tap clicks. The keyboard
/// reaches it too: Tab onto it hovers it, Enter opens it.
/// </summary>
[DisallowMultipleComponent]
public sealed class ServiceHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler,
    IKeyboardTarget
{
    public MachinePartGroup Part { get; set; }

    public event Action<ServiceHoverTarget, bool> HoverChanged;

    public event Action<ServiceHoverTarget> Clicked;

    public bool IsHovered { get; private set; }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (UIPointerUtility.IsTouch(eventData))
            return;
        IsHovered = true;
        HoverChanged?.Invoke(this, true);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // A drag that scrolls the list is not a click: the event system
        // only sends one when the pointer did not move far.
        if (eventData.button == PointerEventData.InputButton.Left)
            Clicked?.Invoke(this);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (!IsHovered)
            return;
        IsHovered = false;
        HoverChanged?.Invoke(this, false);
    }

    private void OnEnable() => KeyboardNavigator.Register(this);

    private void OnDisable()
    {
        KeyboardNavigator.Unregister(this);
        if (!IsHovered)
            return;
        IsHovered = false;
        HoverChanged?.Invoke(this, false);
    }

    RectTransform IKeyboardTarget.FocusRect => (RectTransform)transform;

    bool IKeyboardTarget.CanTakeKeyboardFocus => isActiveAndEnabled && Part != null;

    void IKeyboardTarget.ActivateFromKeyboard() => Clicked?.Invoke(this);

    void IKeyboardTarget.SetKeyboardFocus(bool focused)
    {
        if (TryGetComponent(out UIControlState state))
            state.SetKeyboardFocus(focused);
        if (IsHovered == focused)
            return;
        IsHovered = focused;
        HoverChanged?.Invoke(this, focused);
    }
}
