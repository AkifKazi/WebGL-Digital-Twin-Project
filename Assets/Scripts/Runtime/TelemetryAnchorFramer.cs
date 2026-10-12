using UnityEngine;

/// <summary>
/// Brings a card's sensor into view. Clicking a telemetry card whose anchor sits
/// off screen pulls the camera back just far enough to show it, so the leader
/// line always leads somewhere the viewer can see. The camera never moves closer
/// and never turns, so the view the user chose is kept.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryAnchorFramer : MonoBehaviour
{
    [Tooltip("The orbit camera to pull back. Empty uses the one on this object, then the scene's.")]
    [SerializeField] private OrbitCameraController orbitCamera;

    [Tooltip("Off, clicking a card never moves the camera.")]
    [SerializeField] private bool frameOffscreenAnchors = true;

    private void OnEnable()
    {
        if (orbitCamera == null)
            orbitCamera = GetComponent<OrbitCameraController>();
        if (orbitCamera == null)
            orbitCamera = FindAnyObjectByType<OrbitCameraController>(FindObjectsInactive.Include);

        CardFocus.Changed += HandleCardFocused;
    }

    private void OnDisable()
    {
        CardFocus.Changed -= HandleCardFocused;
    }

    private void HandleCardFocused(PerformanceStatCardView card)
    {
        if (!frameOffscreenAnchors || orbitCamera == null || card == null || !card.IsFocused)
            return;

        Transform anchor = card.WorldAnchor;
        if (anchor != null)
            orbitCamera.EnsureWorldPointVisible(anchor.position);
    }
}
