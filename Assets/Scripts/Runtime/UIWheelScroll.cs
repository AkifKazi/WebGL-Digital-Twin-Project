using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Scrolls a <see cref="ScrollRect"/> by exactly the distance the browser
/// reports for the wheel or trackpad, as a web page or a native list does:
/// a finger moved a centimetre on a trackpad moves the list a centimetre, and
/// a mouse wheel notch moves it the browser's usual step. There is no added
/// smoothing; a trackpad's momentum comes from the system's own events.
///
/// Unity's wheel handling scales each event by per-tick factors that do not
/// match a browser's pixel deltas, which made a small trackpad movement jump
/// the list to its end. In the browser this component reads the wheel events
/// itself and turns the scroll rect's own wheel handling off. Elsewhere (the
/// editor) the scroll rect keeps its own handling at <see cref="editorSensitivity"/>.
/// Dragging, including touch, is left to the scroll rect.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ScrollRect))]
public sealed class UIWheelScroll : MonoBehaviour
{
    [Tooltip("Scroll rect sensitivity outside the browser, where Unity's own wheel handling is used.")]
    [SerializeField, Min(0f)] private float editorSensitivity = 12f;

    private ScrollRect scrollRect;
    private Canvas rootCanvas;

    private void Awake()
    {
        scrollRect = GetComponent<ScrollRect>();
#if UNITY_WEBGL && !UNITY_EDITOR
        DigitalTwin_StartWheelCapture();
        scrollRect.scrollSensitivity = 0f;
#else
        scrollRect.scrollSensitivity = editorSensitivity;
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // One read of the browser's wheel per frame, shared by every list.
    private static int takenFrame = -1;
    private static float frameDeltaPixels;

    [DllImport("__Internal")]
    private static extern void DigitalTwin_StartWheelCapture();

    [DllImport("__Internal")]
    private static extern float DigitalTwin_TakeWheelDeltaY();

    private void Update()
    {
        if (takenFrame != Time.frameCount)
        {
            takenFrame = Time.frameCount;
            frameDeltaPixels = DigitalTwin_TakeWheelDeltaY();
        }

        if (frameDeltaPixels != 0f && IsPointerOver())
            ScrollByScreenPixels(frameDeltaPixels);
    }
#endif

    /// <summary>
    /// Moves the list by <paramref name="pixels"/> screen pixels, positive
    /// towards its end, clamped to its ends. Public so tests can drive it.
    /// </summary>
    public void ScrollByScreenPixels(float pixels)
    {
        if (scrollRect == null || scrollRect.content == null || !scrollRect.vertical)
            return;

        if (rootCanvas == null)
            rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        float scale = rootCanvas != null && rootCanvas.scaleFactor > 0f ? rootCanvas.scaleFactor : 1f;

        RectTransform content = scrollRect.content;
        RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)transform;
        float range = Mathf.Max(0f, content.rect.height - viewport.rect.height);
        if (range <= 0f)
            return;

        // A drag's leftover glide must not fight the wheel.
        scrollRect.StopMovement();
        Vector2 position = content.anchoredPosition;
        position.y = Mathf.Clamp(position.y + pixels / scale, 0f, range);
        content.anchoredPosition = position;
    }

    private bool IsPointerOver()
    {
        if (!isActiveAndEnabled || !scrollRect.IsActive())
            return false;

        Pointer pointer = Pointer.current;
        if (pointer == null)
            return false;

        if (rootCanvas == null)
            rootCanvas = GetComponentInParent<Canvas>()?.rootCanvas;
        RectTransform viewport = scrollRect.viewport != null ? scrollRect.viewport : (RectTransform)transform;
        Camera eventCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera
            : null;
        return RectTransformUtility.RectangleContainsScreenPoint(viewport, pointer.position.ReadValue(), eventCamera);
    }
}
