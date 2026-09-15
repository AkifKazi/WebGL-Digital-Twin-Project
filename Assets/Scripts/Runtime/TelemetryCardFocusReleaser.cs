using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

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
    private bool pressActive;
    private bool pressOnOwnUI;
    private Vector2 pressPosition;

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            Dismiss();

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
                BeginPress(touch.position);
            else if (touch.phase == TouchPhase.Ended)
                EndPress(touch.position);
            else if (touch.phase == TouchPhase.Canceled)
                pressActive = false;
            return;
        }

        if (Input.GetMouseButtonDown(0))
            BeginPress(Input.mousePosition);
        else if (Input.GetMouseButtonUp(0))
            EndPress(Input.mousePosition);
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
