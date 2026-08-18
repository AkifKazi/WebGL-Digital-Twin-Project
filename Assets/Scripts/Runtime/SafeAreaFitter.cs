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

        Vector2 anchorMin = previousSafeArea.position;
        Vector2 anchorMax =
            previousSafeArea.position + previousSafeArea.size;

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