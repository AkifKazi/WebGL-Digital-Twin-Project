using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts the Telemetry Layout Settings on the layout controller, beside the
/// responsive shell that hands them to the rails. Safe to run again.
/// </summary>
public static class TelemetryLayoutSettingsSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Digital Twin/Set Up Telemetry Layout Settings")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        ResponsiveLayoutShell shell = Object.FindObjectsByType<ResponsiveLayoutShell>(FindObjectsInactive.Include)
            .FirstOrDefault();
        if (shell == null)
        {
            Debug.LogError("TELEMETRY_LAYOUT_SETUP no Responsive Layout Shell in the scene.");
            return;
        }

        if (!shell.TryGetComponent(out TelemetryLayoutSettings settings))
            settings = Undo.AddComponent<TelemetryLayoutSettings>(shell.gameObject);

        SerializedObject data = new(shell);
        data.FindProperty("telemetrySettings").objectReferenceValue = settings;
        data.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"TELEMETRY_LAYOUT_SETUP settings on '{shell.name}'");
    }
}
