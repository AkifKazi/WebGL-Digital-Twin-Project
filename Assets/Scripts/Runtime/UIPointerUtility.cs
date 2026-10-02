using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

/// <summary>Pointer checks shared by the cards, the detail panel and the control hover states.</summary>
public static class UIPointerUtility
{
    /// <summary>Movement in pixels beyond which a press counts as a drag (the EventSystem setting).</summary>
    public static float DragThreshold =>
        EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 10f;

    /// <summary>
    /// True for a press and release that stayed within the drag threshold. A
    /// press that travelled was a camera drag that happened to end on the UI.
    /// </summary>
    public static bool IsCleanClick(PointerEventData eventData)
    {
        if (eventData == null)
            return true;
        float threshold = DragThreshold;
        return !eventData.dragging &&
               (eventData.position - eventData.pressPosition).sqrMagnitude <= threshold * threshold;
    }

    /// <summary>
    /// True when a finger made this press. Some browsers deliver touches
    /// through the pointer device, so a touchscreen pressed (or lifted) this
    /// frame counts too.
    /// </summary>
    public static bool IsTouch(PointerEventData eventData)
    {
        if (eventData is ExtendedPointerEventData extended && extended.pointerType == UIPointerType.Touch)
            return true;
        Touchscreen screen = Touchscreen.current;
        return screen != null &&
               (screen.primaryTouch.press.isPressed || screen.primaryTouch.press.wasReleasedThisFrame);
    }
}
