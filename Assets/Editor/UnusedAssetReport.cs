using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Batch-only, read-only list of project assets nothing in the build depends
/// on, largest first. The build roots are the enabled scenes, everything in
/// Resources and Settings folders, and every asset the project settings point
/// at (render pipeline, input actions, always-included shaders). Scripts and
/// editor-only folders are left out. Never deletes.
/// </summary>
public static class UnusedAssetReport
{
    public static void Run()
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);

        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled)
                roots.Add(scene.path);
        }

        string[] allAssets = AssetDatabase.GetAllAssetPaths();

        foreach (string path in allAssets)
        {
            if (path.StartsWith("Assets/", StringComparison.Ordinal) &&
                (path.Contains("/Resources/") || path.StartsWith("Assets/Settings/", StringComparison.Ordinal)))
            {
                roots.Add(path);
            }
        }

        foreach (string settings in Directory.GetFiles("ProjectSettings", "*.asset"))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(settings), @"guid: ([0-9a-f]{32})"))
            {
                string path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);

                if (!string.IsNullOrEmpty(path))
                    roots.Add(path);
            }
        }

        HashSet<string> used = new(AssetDatabase.GetDependencies(roots.ToArray(), true), StringComparer.OrdinalIgnoreCase);

        List<(string path, long bytes)> unused = allAssets
            .Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) && !AssetDatabase.IsValidFolder(path))
            .Where(path => !used.Contains(path))
            .Where(path => !path.EndsWith(".cs", StringComparison.Ordinal) &&
                           !path.Contains("/Editor/") &&
                           !path.EndsWith(".asmdef", StringComparison.Ordinal) &&
                           !path.EndsWith(".asmref", StringComparison.Ordinal))
            .Select(path => (path, new FileInfo(path).Length))
            .OrderByDescending(entry => entry.Item2)
            .ToList();

        StringBuilder report = new();
        long total = unused.Sum(entry => entry.bytes);
        report.AppendLine($"UNUSED {unused.Count} files, {total / 1048576.0:F2} MB (build roots: {roots.Count}, used: {used.Count})");

        foreach ((string path, long bytes) in unused)
            report.AppendLine($"{bytes / 1024.0,10:F1} KB  {path}");

        File.WriteAllText("Logs/UnusedAssetReport.txt", report.ToString());
        Debug.Log($"UNUSED_ASSET_REPORT files={unused.Count} mb={total / 1048576.0:F2}");
    }
}
