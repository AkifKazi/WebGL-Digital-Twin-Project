using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MachineViewRuntimeSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SharedControllerName = "Shared Machine View Controller";

    [MenuItem("Tools/Digital Twin/Set Up Shared Machine View Controller")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        HybridHopperClipController[] controllers = UnityEngine.Object
            .FindObjectsByType<HybridHopperClipController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
        if (controllers.Length == 0)
            throw new InvalidOperationException("No machine full/cross-section controller was found.");

        HybridHopperClipController shared = controllers
            .FirstOrDefault(controller => controller.name == SharedControllerName)
            ?? controllers[0];
        Transform runtime = UnityEngine.Object
            .FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(transform => transform.name == "Digital Twin Runtime");
        if (runtime == null)
            throw new InvalidOperationException("Digital Twin Runtime was not found.");

        shared.gameObject.name = SharedControllerName;
        shared.transform.SetParent(runtime, false);
        shared.transform.localPosition = Vector3.zero;
        shared.transform.localRotation = Quaternion.identity;
        shared.transform.localScale = Vector3.one;
        shared.enabled = true;

        foreach (ViewModeSegmentedControl control in UnityEngine.Object
                     .FindObjectsByType<ViewModeSegmentedControl>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
        {
            SerializedObject data = new(control);
            data.FindProperty("hopperController").objectReferenceValue = shared;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (TelemetrySequenceController sequence in UnityEngine.Object
                     .FindObjectsByType<TelemetrySequenceController>(
                         FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
        {
            SerializedObject data = new(sequence);
            data.FindProperty("hopperController").objectReferenceValue = shared;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        foreach (HybridHopperClipController duplicate in controllers)
        {
            if (duplicate != null && duplicate != shared)
                UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Wide and portrait controls now share one machine view controller.");
    }
}
