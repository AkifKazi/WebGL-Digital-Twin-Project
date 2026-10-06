using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gives each layout its <see cref="TelemetryVisibility"/> and points
/// everything that hides, reveals or switches telemetry at it: the service
/// view, the Telemetry / Service switch, the service pins, the portrait sheet,
/// the pagers and the reveal sequence. Safe to run any number of times.
///
/// The telemetry regions are the parts of a layout that hold only telemetry:
/// in the wide layout the content area, the detail layer (a canvas of its
/// own, which a parent group would not stop the pointer on), the leader line
/// layer and the top controls; in the portrait layout the content area and
/// the bottom controls. Rails made at runtime sit inside the content area and
/// fade with it.
///
/// <see cref="ServiceModeSetup"/> calls <see cref="WireAll"/> after building
/// the service views; the menu item moves an existing scene over.
/// </summary>
public static class TelemetryVisibilitySetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ObjectName = "Telemetry Visibility";
    private const string WideLayout = "Wide layout";
    private const string PortraitLayout = "Portrait layout";

    [MenuItem("Tools/Digital Twin/Wire Telemetry Visibility")]
    private static void WireFromMenu()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        WireAll();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>Batch mode entry point: wires the scene, saves it and exits with 0 on success.</summary>
    public static void RunBatch()
    {
        int code = 0;
        try
        {
            WireFromMenu();
        }
        catch (Exception exception)
        {
            Debug.LogError($"TELEMETRY_VISIBILITY_SETUP_RESULT failed: {exception}");
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    /// <summary>Wires both layouts in the open scene.</summary>
    public static void WireAll()
    {
        TelemetryVisibility wide = Wire(FindLayout(WideLayout), true);
        TelemetryVisibility portrait = Wire(FindLayout(PortraitLayout), false);
        Debug.Log("TELEMETRY_VISIBILITY_SETUP_RESULT passed " +
                  $"wideRegions={Count(wide, "telemetryRegions")} portraitRegions={Count(portrait, "telemetryRegions")}");
    }

    /// <summary>The layout's <see cref="TelemetryVisibility"/>, added if missing. Not yet wired.</summary>
    public static TelemetryVisibility Ensure(Transform layout)
    {
        Transform existing = layout.Find(ObjectName);
        if (existing != null && existing.TryGetComponent(out TelemetryVisibility found))
            return found;

        GameObject owner = new(ObjectName, typeof(RectTransform));
        owner.layer = layout.gameObject.layer;
        owner.transform.SetParent(layout, false);
        return owner.AddComponent<TelemetryVisibility>();
    }

    /// <summary>Wires one layout and returns its <see cref="TelemetryVisibility"/>.</summary>
    public static TelemetryVisibility Wire(Transform layout, bool wide)
    {
        TelemetryVisibility visibility = Ensure(layout);
        Transform baseLayout = Child(layout, "Base layout");
        Transform content = Child(baseLayout, "Content area");

        CanvasGroup[] regions = wide
            ? new[]
            {
                Group(content),
                Group(Child(content, "Telemetry Detail Layer")),
                Group(Child(layout, "Runtime Leader Line Layer")),
                Group(Child(Child(baseLayout, "Top bar"), "Telemetry Top Controls"))
            }
            : new[]
            {
                Group(content),
                Group(Child(baseLayout, "Bottom controls"))
            };

        CanvasGroup[] serviceGroups = wide
            ? new[] { Group(Child(layout, "Service Panel")), Group(Child(layout, "Service Pin Layer")) }
            : new[] { Group(Child(layout, "Service Panel")), Group(Child(layout, "Service Backdrop")) };

        // The rails used to fade one by one; the content area fades them now.
        // A group left on a rail would only hide a rail made from it at runtime.
        foreach (Transform child in content)
        {
            if (child.name.EndsWith(" rail", StringComparison.Ordinal) && child.TryGetComponent(out CanvasGroup stale))
                UnityEngine.Object.DestroyImmediate(stale);
        }

        SerializedObject data = new(visibility);
        SetArray(data.FindProperty("telemetryRegions"), regions);
        SetArray(data.FindProperty("serviceGroups"), serviceGroups);
        data.FindProperty("inspectionView").objectReferenceValue =
            UnityEngine.Object.FindAnyObjectByType<MachineViewModeController>(FindObjectsInactive.Include)
            ?? throw new InvalidOperationException("The Machine View Mode Controller was not found.");
        data.FindProperty("focusReleaser").objectReferenceValue =
            UnityEngine.Object.FindAnyObjectByType<TelemetryCardFocusReleaser>(FindObjectsInactive.Include);
        data.ApplyModifiedPropertiesWithoutUndo();

        if (wide)
            Point<WideServiceView>(layout, "visibility", visibility, required: true);
        else
            Point<PortraitServiceView>(layout, "visibility", visibility, required: true);
        Point<DigitalTwinModeSwitch>(layout, "controller", visibility, required: true);
        Point<ServicePinLayer>(layout, "visibility", visibility, required: wide);
        Point<PortraitTelemetrySheetController>(layout, "visibility", visibility, required: !wide);
        Point<PortraitTelemetryPager>(layout, "visibility", visibility, required: true);
        Point<TelemetrySequenceController>(layout, "visibility", visibility, required: true);
        return visibility;
    }

    /// <summary>Points every <typeparamref name="T"/> in the layout at its visibility.</summary>
    private static void Point<T>(Transform layout, string field, TelemetryVisibility visibility, bool required)
        where T : Component
    {
        T[] components = layout.GetComponentsInChildren<T>(true);
        if (components.Length == 0 && required)
            throw new InvalidOperationException($"No {typeof(T).Name} found in '{layout.name}'.");

        foreach (T component in components)
        {
            SerializedObject data = new(component);
            SerializedProperty property = data.FindProperty(field)
                ?? throw new InvalidOperationException($"{typeof(T).Name} has no field '{field}'.");
            property.objectReferenceValue = visibility;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static int Count(TelemetryVisibility visibility, string field) =>
        new SerializedObject(visibility).FindProperty(field).arraySize;

    private static CanvasGroup Group(Transform target)
    {
        if (!target.TryGetComponent(out CanvasGroup group))
            group = target.gameObject.AddComponent<CanvasGroup>();
        return group;
    }

    private static void SetArray(SerializedProperty array, UnityEngine.Object[] values)
    {
        array.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static Transform FindLayout(string name) =>
        UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .FirstOrDefault(t => t.name == name)
        ?? throw new InvalidOperationException($"'{name}' was not found.");

    private static Transform Child(Transform parent, string name) =>
        parent.Find(name) ?? throw new InvalidOperationException($"'{name}' was not found under '{parent.name}'.");
}
