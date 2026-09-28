using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Reports the pointer entering and leaving one service row or attention item,
/// so the part it lists can be marked on the model. Touch never hovers.
/// </summary>
[DisallowMultipleComponent]
public sealed class ServiceHoverTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public MachinePartGroup Part { get; set; }

    public event Action<ServiceHoverTarget, bool> HoverChanged;

    public bool IsHovered { get; private set; }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (UIPointerUtility.IsTouch(eventData))
            return;
        IsHovered = true;
        HoverChanged?.Invoke(this, true);
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
