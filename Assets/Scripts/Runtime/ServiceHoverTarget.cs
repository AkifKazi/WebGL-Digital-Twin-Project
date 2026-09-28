using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Reports the pointer entering and leaving one service row,
/// so the part it lists can be marked on the model, and clicks on it, which
/// open the part's detail. Touch never hovers, but a tap clicks.
/// </summary>
[DisallowMultipleComponent]
public sealed class ServiceHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
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

    private void OnDisable()
    {
        if (!IsHovered)
            return;
        IsHovered = false;
        HoverChanged?.Invoke(this, false);
    }
}
