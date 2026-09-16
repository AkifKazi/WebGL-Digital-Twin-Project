using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Releases the focused telemetry card when the user clicks (or taps) anything
/// that is not a card or a detail panel, or presses Esc. A press that travels
/// is a camera drag and releases nothing. Lives on the EventSystem so it works
/// in every layout; the detail panel listens to <see cref="Dismissed"/> to
/// close its unpinned panel at the same moment.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryCardFocusReleaser : MonoBehaviour
{
    /// <summary>Raised after an outside click or Esc has released the focused card.</summary>
    public static event Action Dismissed;

    private readonly List<RaycastResult> raycastResults = new();
    private InputAction pointerPress;
    private InputAction escape;
    private bool pressActive;
    private bool pressOnOwnUI;
    private Vector2 pressPosition;

    // Input actions report every press and release as it happens. Polling
    // "pressed this frame" missed quick clicks and trackpad taps, whose press
    // and release land in the same frame, so the card stayed focused.
    private void OnEnable()
    {
        // The mouse button, or on a phone the primary touch. A pass-through
        // action reports a button's press as "started" and its release as
        // "canceled"; listening only to "performed" missed both, which is why an
        // outside click never released the card in the player.
        pointerPress = new InputAction("Pointer Press", InputActionType.PassThrough, "<Pointer>/press");
        pointerPress.started += HandlePointerPress;
        pointerPress.performed += HandlePointerPress;
        pointerPress.canceled += HandlePointerPress;
        pointerPress.Enable();

        escape = new InputAction("Dismiss", InputActionType.Button, "<Keyboard>/escape");
        escape.performed += HandleEscape;
        escape.Enable();
    }

    private void OnDisable()
    {
        pointerPress?.Dispose();
        escape?.Dispose();
        pointerPress = null;
        escape = null;
        pressActive = false;
    }

    private void HandleEscape(InputAction.CallbackContext context) => Dismiss();

    private void HandlePointerPress(InputAction.CallbackContext context)
    {
        if (context.control.device is not Pointer pointer)
            return;

        // The same handler serves press and release, so the button's own value
        // decides which this is rather than which callback delivered it.
        Vector2 position = pointer.position.ReadValue();
        bool down = context.control is UnityEngine.InputSystem.Controls.ButtonControl button
            ? button.isPressed
            : context.ReadValueAsButton();

        if (down)
            BeginPress(position);
        else
            EndPress(position);
    }

    private void BeginPress(Vector2 position)
    {
        pressActive = true;
        pressPosition = position;
        pressOnOwnUI = IsOverCardOrPanel(position);
    }

    private void EndPress(Vector2 position)
    {
        if (!pressActive)
            return;
        pressActive = false;
        HandlePointerGesture(pressPosition, position, pressOnOwnUI);
    }

    /// <summary>
    /// Classifies one press and release. A press that travelled changes nothing;
    /// a clean click outside every card and panel releases focus. Cards and
    /// panels handle their own clicks.
    /// </summary>
    public void HandlePointerGesture(Vector2 pressScreen, Vector2 releaseScreen, bool pressedOnCardOrPanel = false)
    {
        float threshold = UIPointerUtility.DragThreshold;
        if ((releaseScreen - pressScreen).sqrMagnitude > threshold * threshold)
            return;
        if (pressedOnCardOrPanel || IsOverCardOrPanel(releaseScreen))
            return;
        Dismiss();
    }

    public void Dismiss()
    {
        PerformanceStatCardView.ClearActiveFocus();
        Dismissed?.Invoke();
    }

    private bool IsOverCardOrPanel(Vector2 screenPosition)
    {
        EventSystem system = EventSystem.current;
        if (system == null)
            return false;

        raycastResults.Clear();
        system.RaycastAll(new PointerEventData(system) { position = screenPosition }, raycastResults);
        if (raycastResults.Count == 0)
            return false;

        GameObject top = raycastResults[0].gameObject;
        return top.GetComponentInParent<PerformanceStatCardView>() != null ||
               top.GetComponentInParent<TelemetryDetailPanel>() != null;
    }
}
