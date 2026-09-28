using UnityEditor;
using UnityEngine;

/// <summary>
/// One command for adopting an updated separator model: gives the model files
/// descriptive names, applies the WebGL load settings, organises and names the
/// hierarchy, rebuilds the digital-twin wiring, then removes assets the new
/// model made obsolete. Each step is idempotent, so the whole command can be
/// re-run after another model export.
///
/// The organiser and scene setup both save SampleScene. Back it up first.
/// </summary>
public static class SeparatorModelAdoption
{
    private static readonly (string From, string To)[] ModelFileNames =
    {
        ("Assets/Models/full model of hopper.fbx", "Assets/Models/Separator - Full Model.fbx"),
        ("Assets/Models/new detailed hopper and conveyor.fbx", "Assets/Models/Separator - Cross-Section Assembly.fbx"),

        // The old cutaway file now only supplies the bunker and support frame.
        ("Assets/Models/Hopper - Cross Section.fbx", "Assets/Models/Hopper - Bunker and Supports.fbx")
    };

    [MenuItem("Tools/Digital Twin/Adopt Separator Model Update", priority = 70)]
    public static void Run()
    {
        if (!Application.isBatchMode && !EditorUtility.DisplayDialog(
                "Adopt Separator Model Update",
                "This renames model files, reapplies the WebGL load settings, reorganises and " +
                "renames the scene hierarchy, rebuilds the rock flow and the sensors, then deletes " +
                "unused assets. Each step saves as it goes.\n\n" +
                "For a re-exported separator whose parts were split, use Reconnect Separator Model " +
                "instead.",
                "Run All Steps",
                "Cancel"))
            return;

        RenameModelFiles();

        WebGLLoadOptimization.Apply();
        DigitalTwinSceneOrganization.Apply();
        ConveyorRockFlowSetup.Apply();
        if (!DigitalTwinSceneSetup.SetUpFromMenu())
        {
            Debug.LogWarning("SEPARATOR_MODEL_ADOPTION stopped: sensor setup was cancelled, so no assets were removed.");
            return;
        }

        // Last, so the scene saved above no longer references what it removes.
        if (!ProjectUnusedAssetCleanup.Apply())
        {
            Debug.LogWarning("SEPARATOR_MODEL_ADOPTION stopped before removing unused assets.");
            return;
        }

        Debug.Log("SEPARATOR_MODEL_ADOPTION completed");
    }

    private static void RenameModelFiles()
    {
        foreach ((string from, string to) in ModelFileNames)
        {
            if (AssetDatabase.LoadMainAssetAtPath(from) == null || AssetDatabase.LoadMainAssetAtPath(to) != null)
                continue;

            // MoveAsset keeps the GUID, so every scene reference survives.
            string error = AssetDatabase.MoveAsset(from, to);

            if (!string.IsNullOrEmpty(error))
                Debug.LogError($"Could not rename '{from}': {error}");
        }

        AssetDatabase.SaveAssets();
    }
}
