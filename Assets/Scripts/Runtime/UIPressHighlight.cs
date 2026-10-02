using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Shows a highlight while a mouse is over the control or while any pointer
/// presses it. Unlike <see cref="UIHoverSprite"/>, a finger gets it too: a
/// link with no other look needs to answer the touch that presses it.
/// </summary>
[DisallowMultipleComponent]
public sealed class UIPressHighlight : MonoBehaviour,
    IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private Graphic highlight;

    private bool hovered;
    private bool pressed;

    public bool IsShowing => highlight != null && highlight.enabled;

    public void Configure(Graphic target)
    {
        highlight = target;
        Apply();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // A finger has no hover: one left behind after a tap reads as stuck.
        hovered = !UIPointerUtility.IsTouch(eventData);
        Apply();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        pressed = false;
        Apply();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        pressed = true;
        Apply();
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        pressed = false;
        Apply();
    }

    private void OnDisable()
    {
        hovered = false;
        pressed = false;
        Apply();
    }

    private void Apply()
    {
        if (highlight != null)
            highlight.enabled = hovered || pressed;
    }
}
