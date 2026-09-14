using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Removes only explicitly audited assets that are not dependencies of the
/// production scene, prefabs, render settings, or build settings.
/// </summary>
public static class ProjectUnusedAssetCleanup
{
    private static readonly string[] ProductionRoots =
    {
        "Assets/Scenes/SampleScene.unity",
        "Assets/Prefabs/Performance Stat Card.prefab",
        "Assets/Prefabs/Stat Leader Line.prefab",
        "Assets/Settings"
    };

    private static readonly string[] Candidates =
    {
        "Assets/UI/Fonts/Rajdhani-Bold SDF.asset",
        "Assets/UI/Fonts/Rajdhani-Bold.ttf",
        "Assets/UI/Fonts/Rajdhani-Light SDF.asset",
        "Assets/UI/Fonts/Rajdhani-Light.ttf",
        "Assets/UI/Fonts/Rajdhani-Regular SDF.asset",
        "Assets/UI/Fonts/Rajdhani-Regular.ttf",
        "Assets/UI/Sprites/Compact Button Background.png",
        "Assets/UI/Sprites/Solid Line.png",
        "Assets/Materials/Textures/Galvanized Zinc - Metallic.jpg",
        "Assets/Materials/Review",
        "Assets/Scripts/Legacy",
        "Assets/Resources",

        // Superseded by the September separator model.
        "Assets/Models/Hopper - Full Model.fbx",
        "Assets/Models/Conveyor Assembly.fbx",
        "Assets/Models/Full Model.prefab",
        "Assets/Models/Cross-Section Model.prefab",
        "Assets/Materials/Dark Metal.mat",
        "Assets/Materials/Mid Grey Metal.mat",
        "Assets/Materials/Rocky Ground.mat",

        // Retired hover-overlay trial, replaced by the selection outline.
        "Assets/Materials/X-Ray/Machine X-Ray Overlay.mat",

        // TextMesh Pro's default font. It sat in a Resources folder, so it
        // shipped in every build; the interface uses Rajdhani only.
        "Assets/TextMesh Pro/Resources/Fonts & Materials",
        "Assets/TextMesh Pro/Fonts/LiberationSans.ttf",
        "Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt",

        // Retired with the single clipped model and its cut frame.
        "Assets/Models/Separator - Cross-Section Assembly.fbx",

        // Card art replaced by the current telemetry card sprites.
        "Assets/UI/Sprites/Telemetry Card - Normal.png",
        "Assets/UI/Sprites/Telemetry ui background active new.png",

        // HDRP text shaders; this project renders with URP.
        "Assets/TextMesh Pro/Shaders/TMP_SDF-HDRP LIT.shadergraph",
        "Assets/TextMesh Pro/Shaders/TMP_SDF-HDRP UNLIT.shadergraph"
    };

    [MenuItem("Tools/Digital Twin/Remove Verified Unused Assets")]
    public static void Apply()
    {
        HashSet<string> dependencies = CollectProductionDependencies();
        int removed = 0;

        foreach (string candidate in Candidates)
        {
            if (!AssetExists(candidate))
                continue;

            if (IsProtectedByProductionDependency(candidate, dependencies))
            {
                Debug.LogWarning($"UNUSED_ASSET_CLEANUP_SKIPPED dependency={candidate}");
                continue;
            }

            if (!AssetDatabase.DeleteAsset(candidate))
                throw new InvalidOperationException($"Could not delete verified unused asset '{candidate}'.");
            removed++;
        }

        DeleteFinderMetadata("Assets/.DS_Store");
        DeleteFinderMetadata("Assets/UI/.DS_Store");
        DeleteFinderMetadata("Assets/Materials/.DS_Store");
        DeleteFinderMetadata("Assets/TextMesh Pro/.DS_Store");

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"UNUSED_ASSET_CLEANUP_RESULT removed={removed}");
    }

    private static HashSet<string> CollectProductionDependencies()
    {
        List<string> roots = new();
        foreach (string root in ProductionRoots)
        {
            if (AssetDatabase.IsValidFolder(root))
                roots.AddRange(AssetDatabase.FindAssets(string.Empty, new[] { root })
                    .ConvertAll(AssetDatabase.GUIDToAssetPath));
            else if (AssetDatabase.LoadMainAssetAtPath(root) != null)
                roots.Add(root);
        }

        return new HashSet<string>(
            AssetDatabase.GetDependencies(roots.ToArray(), true),
            StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsProtectedByProductionDependency(
        string candidate,
        HashSet<string> dependencies)
    {
        if (!AssetDatabase.IsValidFolder(candidate))
            return dependencies.Contains(candidate);

        string prefix = candidate.TrimEnd('/') + "/";
        foreach (string dependency in dependencies)
        {
            if (dependency.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool AssetExists(string path) =>
        AssetDatabase.IsValidFolder(path) || AssetDatabase.LoadMainAssetAtPath(path) != null;

    private static void DeleteFinderMetadata(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private static List<string> ConvertAll(
        this string[] values,
        Func<string, string> converter)
    {
        List<string> converted = new(values.Length);
        foreach (string value in values)
            converted.Add(converter(value));
        return converted;
    }
}
