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
        RenameModelFiles();

        WebGLLoadOptimization.Apply();
        DigitalTwinSceneOrganization.Apply();
        DigitalTwinSceneSetup.SetUpFromMenu();

        // Last, so the scene saved above no longer references what it removes.
        ProjectUnusedAssetCleanup.Apply();

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
