using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// The part of the screen the UI may use, in screen pixels. Elsewhere this is
/// <see cref="Screen.safeArea"/>. In a browser, Unity reports the whole canvas,
/// so the page supplies the insets instead (see the DigitalTwin WebGL
/// template): the notch and home indicator, and the browser's own bottom
/// toolbar.
///
/// The template keeps the canvas at the tallest height the browser shows, so a
/// toolbar that shrinks or grows while the page is used (Safari, Firefox on
/// iOS) never resizes the canvas: it only covers the bottom inset, which is
/// kept at the toolbar's full height so the controls above it never move.
/// </summary>
public static class DeviceSafeArea
{
    private const int Top = 0;
    private const int Right = 1;
    private const int Bottom = 2;
    private const int Left = 3;

    private static int cachedFrame = -1;
    private static Rect cachedRect;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern float DigitalTwin_GetViewportInset(int side);
#endif

    /// <summary>The usable area this frame, in screen pixels.</summary>
    public static Rect Current
    {
        get
        {
            if (cachedFrame == Time.frameCount)
                return cachedRect;
            cachedFrame = Time.frameCount;
            cachedRect = Measure();
            return cachedRect;
        }
    }

#if UNITY_EDITOR
    /// <summary>Insets for play tests, as shares of the screen: top, right, bottom, left.</summary>
    public static Vector4? EditorInsets;
#endif

    private static Rect Measure()
    {
        Rect safe = Screen.safeArea;
#if UNITY_EDITOR
        if (EditorInsets is Vector4 share)
        {
            return Rect.MinMaxRect(
                share.w * Screen.width,
                share.z * Screen.height,
                (1f - share.y) * Screen.width,
                (1f - share.x) * Screen.height);
        }
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
        float width = Screen.width;
        float height = Screen.height;
        float top = Inset(Top, height);
        float right = Inset(Right, width);
        float bottom = Inset(Bottom, height);
        float left = Inset(Left, width);
        if (top + right + bottom + left <= 0f)
            return safe;

        // Screen coordinates start at the bottom left.
        float xMin = Mathf.Max(safe.xMin, left);
        float xMax = Mathf.Min(safe.xMax, width - right);
        float yMin = Mathf.Max(safe.yMin, bottom);
        float yMax = Mathf.Min(safe.yMax, height - top);
        // Insets that leave too little are a page still settling: use it all.
        if (xMax - xMin < width * 0.5f || yMax - yMin < height * 0.5f)
            return safe;
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
#else
        return safe;
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private static float Inset(int side, float limit)
    {
        float value = DigitalTwin_GetViewportInset(side);
        return float.IsNaN(value) || value <= 0f ? 0f : Mathf.Min(Mathf.Round(value), limit);
    }
#endif
}
