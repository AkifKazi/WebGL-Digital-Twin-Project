using UnityEngine;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    private RectTransform safeAreaRect;

    private Rect previousSafeArea;
    private int previousWidth;
    private int previousHeight;

    private void Awake()
    {
        safeAreaRect = GetComponent<RectTransform>();
        ApplySafeArea();
    }

    private void Update()
    {
        if (Screen.width != previousWidth ||
            Screen.height != previousHeight ||
            Screen.safeArea != previousSafeArea)
        {
            ApplySafeArea();
        }
    }

    private void ApplySafeArea()
    {
        if (Screen.width <= 0 || Screen.height <= 0)
            return;

        previousWidth = Screen.width;
        previousHeight = Screen.height;
        previousSafeArea = Screen.safeArea;

        // Some mobile browsers briefly report an empty or out-of-bounds safe
        // area while rotating. Falling back to the full viewport is safer than
        // collapsing the entire UI for a frame.
        Rect effectiveSafeArea = previousSafeArea;
        if (effectiveSafeArea.width <= 1f || effectiveSafeArea.height <= 1f)
            effectiveSafeArea = new Rect(0f, 0f, Screen.width, Screen.height);

        float xMin = Mathf.Clamp(effectiveSafeArea.xMin, 0f, Screen.width);
        float yMin = Mathf.Clamp(effectiveSafeArea.yMin, 0f, Screen.height);
        float xMax = Mathf.Clamp(effectiveSafeArea.xMax, xMin, Screen.width);
        float yMax = Mathf.Clamp(effectiveSafeArea.yMax, yMin, Screen.height);

        Vector2 anchorMin = new(xMin, yMin);
        Vector2 anchorMax = new(xMax, yMax);

        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;

        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;

        safeAreaRect.anchorMin = anchorMin;
        safeAreaRect.anchorMax = anchorMax;

        safeAreaRect.offsetMin = Vector2.zero;
        safeAreaRect.offsetMax = Vector2.zero;
    }
}
