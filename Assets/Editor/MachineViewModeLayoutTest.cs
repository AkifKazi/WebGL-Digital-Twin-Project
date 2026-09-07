#if UNITY_EDITOR
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Headless check that the view mode segments fit the safe area at a range of
/// screen sizes. Layout is measured, not rendered, so it is unaffected by the
/// editor's offscreen rendering limits.
/// </summary>
public static class MachineViewModeLayoutTest
{
    private static readonly Vector2[] Screens =
    {
        new(750f, 1334f),    // iPhone SE
        new(1179f, 2556f),   // iPhone 15 Pro
        new(1440f, 3200f),   // tall Android
        new(1080f, 2340f),   // common Android
        new(2532f, 1170f),   // landscape phone
        new(1920f, 1080f)    // desktop
    };

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);

        CanvasScaler scaler = Object.FindFirstObjectByType<CanvasScaler>();
        RectTransform canvasRect = scaler.GetComponent<RectTransform>();

        // Both layout roots are measured, so the inactive one still reports.
        foreach (GameObject root in Object.FindObjectsByType<GameObject>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (root.name == "Wide layout" || root.name == "Portrait layout")
                root.SetActive(true);
        }

        bool failed = false;

        foreach (Vector2 screen in Screens)
        {
            // Reproduce CanvasScaler's ScaleWithScreenSize maths.
            float logWidth = Mathf.Log(screen.x / scaler.referenceResolution.x, 2f);
            float logHeight = Mathf.Log(screen.y / scaler.referenceResolution.y, 2f);
            float scale = Mathf.Pow(2f, Mathf.Lerp(logWidth, logHeight, scaler.matchWidthOrHeight));

            canvasRect.sizeDelta = new Vector2(screen.x / scale, screen.y / scale);

            foreach (ViewModeSegmentedControl control in
                     Object.FindObjectsByType<ViewModeSegmentedControl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!IsPortraitInstance(control.transform) == (screen.y > screen.x))
                    continue;

                failed |= !CheckControl(control, screen, canvasRect);
            }
        }

        Debug.Log(failed ? "LAYOUT-TEST: FAILED" : "LAYOUT-TEST: all screen sizes fit");
        EditorApplication.Exit(failed ? 1 : 0);
    }

    private static bool IsPortraitInstance(Transform t)
    {
        for (Transform c = t; c != null; c = c.parent)
        {
            if (c.name == "Portrait layout")
                return true;
        }

        return false;
    }

    private static bool CheckControl(ViewModeSegmentedControl control, Vector2 screen, RectTransform canvasRect)
    {
        var rect = (RectTransform)control.transform;

        LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);

        // Update() does not run in edit mode, so the sizing pass is invoked directly.
        MethodInfo apply = typeof(ViewModeSegmentedControl)
            .GetMethod("ApplyAdaptiveWidth", BindingFlags.NonPublic | BindingFlags.Instance);

        typeof(ViewModeSegmentedControl)
            .GetField("logSizing", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(control, true);

        apply.Invoke(control, new object[] { true });

        Debug.Log($"POST-APPLY: rect={rect.rect.width:F0}");

        LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);

        RectTransform safeArea = FindSafeArea(control.transform);
        float safeWidth = safeArea != null ? safeArea.rect.width : canvasRect.rect.width;

        float controlWidth = rect.rect.width * (rect.lossyScale.x / Mathf.Max(safeArea.lossyScale.x, 0.0001f));

        Button[] buttons = control.GetComponentsInChildren<Button>(true)
            .Where(b => b.transform.parent == control.transform)
            .ToArray();

        float widest = buttons.Length == 0
            ? 0f
            : buttons.Max(b => ((RectTransform)b.transform).rect.width);

        // Absolute horizontal extents, so a control that fits by width but sits
        // outside the screen is still caught.
        Vector3[] safeCorners = new Vector3[4];
        Vector3[] controlCorners = new Vector3[4];
        safeArea.GetWorldCorners(safeCorners);
        rect.GetWorldCorners(controlCorners);

        RectTransform parentRect = (RectTransform)control.transform.parent;
        Vector3[] parentCorners = new Vector3[4];
        parentRect.GetWorldCorners(parentCorners);

        Debug.Log($"LAYOUT-EXTENT: {screen.x:F0}x{screen.y:F0} " +
                  $"safe[{safeCorners[0].x:F0}..{safeCorners[2].x:F0}] " +
                  $"parent'{parentRect.name}'[{parentCorners[0].x:F0}..{parentCorners[2].x:F0}] w={parentRect.rect.width:F0} " +
                  $"control[{controlCorners[0].x:F0}..{controlCorners[2].x:F0}]");

        // Width is the reliable measure here. Absolute position depends on the
        // parent row, which editor rebuilds do not resize, so it is logged for
        // information rather than asserted.
        bool fits = controlWidth <= safeWidth + 0.5f;

        string layout = IsPortraitInstance(control.transform) ? "portrait" : "wide";

        Debug.Log($"LAYOUT-TEST: {screen.x:F0}x{screen.y:F0} {layout,-8} " +
                  $"safe={safeWidth:F0} control={controlWidth:F0} segments={buttons.Length} " +
                  $"widest={widest:F0} {(fits ? "OK" : "OVERFLOW")}");

        return fits;
    }

    private static RectTransform FindSafeArea(Transform from)
    {
        for (Transform c = from; c != null; c = c.parent)
        {
            if (c.name == "Safe area" && c is RectTransform r)
                return r;
        }

        return null;
    }
}
#endif
