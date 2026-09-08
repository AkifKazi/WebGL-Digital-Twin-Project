using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLBuildAudit
{
    public static void BuildOptimizedRelease()
    {
        // Asset optimisation is an explicit authoring step. A release build
        // validates and packages the checked-in state without silently
        // rewriting texture importers or project assets.
        DigitalTwinProjectValidator.Validate();
        BuildRelease();
    }

    public static void BuildRelease()
    {
        string outputPath = Environment.GetEnvironmentVariable("WEBGL_AUDIT_OUTPUT");

        string commandLineOutput = GetCommandLineValue("-auditOutput");

        if (!string.IsNullOrWhiteSpace(commandLineOutput))
            outputPath = commandLineOutput;

        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = "Builds/WebGL";

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        // Code size settings are build configuration rather than content, so
        // they are applied here. This does not rewrite any asset.
        WebGLLoadOptimization.ApplyCodeSizeSettings();

        BuildPlayerOptions options = new()
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        Debug.Log(
            $"WEBGL_AUDIT result={summary.result} " +
            $"bytes={summary.totalSize} " +
            $"duration={summary.totalTime.TotalSeconds:F1}s " +
            $"warnings={summary.totalWarnings} errors={summary.totalErrors} " +
            $"output={outputPath}");

        if (summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("WebGL audit build failed.");
    }

    private static string GetCommandLineValue(string key)
    {
        string[] arguments = Environment.GetCommandLineArgs();

        for (int i = 0; i < arguments.Length - 1; i++)
        {
            if (string.Equals(arguments[i], key, StringComparison.Ordinal))
                return arguments[i + 1];
        }

        return null;
    }
}
