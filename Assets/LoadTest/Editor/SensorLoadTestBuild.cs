using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds and smoke-tests the sensor load test (see SensorLoadTestHarness).
///
///   Unity -batchmode -projectPath . -executeMethod SensorLoadTestBuild.BuildWebGL
///     WebGL release settings plus the DIGITAL_TWIN_LOAD_TEST define, written
///     to Builds/LoadTest. The define is removed again afterwards, so the
///     release build never contains the harness.
///
///   Unity -batchmode -projectPath . -executeMethod SensorLoadTestBuild.SmokeTest -loadtest 30,60
///     Runs the harness in editor play mode to catch errors before a build.
///     Editor frame times are not representative; use the WebGL build.
/// </summary>
public static class SensorLoadTestBuild
{
    private const string Define = "DIGITAL_TWIN_LOAD_TEST";
    private const string OutputPath = "Builds/LoadTest";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SmokeActiveKey = "SensorLoadTestBuild.SmokeActive";
    private const string SmokeStartKey = "SensorLoadTestBuild.SmokeStart";

    public static void BuildWebGL()
    {
        NamedBuildTarget target = NamedBuildTarget.WebGL;
        string original = PlayerSettings.GetScriptingDefineSymbols(target);
        try
        {
            PlayerSettings.SetScriptingDefineSymbols(target, AddDefine(original));
            WebGLLoadOptimization.ApplyCodeSizeSettings();

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            BuildSummary summary = report.summary;
            Debug.Log(
                $"LOADTEST_BUILD result={summary.result} bytes={summary.totalSize} " +
                $"duration={summary.totalTime.TotalSeconds:F1}s output={OutputPath}");
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("Load test build failed.");
        }
        finally
        {
            PlayerSettings.SetScriptingDefineSymbols(target, original);
            AssetDatabase.SaveAssets();
        }
    }

    public static void SmokeTest()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(SmokeActiveKey, true);
        SessionState.SetFloat(SmokeStartKey, (float)EditorApplication.timeSinceStartup);
        Register();
        EditorApplication.EnterPlaymode();
    }

    // Entering play mode reloads the domain, which drops static handlers, so
    // the smoke test re-registers itself after every reload while active.
    [InitializeOnLoadMethod]
    private static void Register()
    {
        if (!SessionState.GetBool(SmokeActiveKey, false))
            return;
        Application.logMessageReceived -= OnLog;
        Application.logMessageReceived += OnLog;
        EditorApplication.update -= CheckTimeout;
        EditorApplication.update += CheckTimeout;
    }

    private static void CheckTimeout()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(SmokeStartKey, 0f) > 300f)
            Finish(1, "timeout");
    }

    private static void OnLog(string message, string stack, LogType type)
    {
        if (message.StartsWith("LOADTEST_DONE", StringComparison.Ordinal))
            Finish(0, "done");
        else if (type == LogType.Exception || message.StartsWith("LOADTEST_ERROR", StringComparison.Ordinal))
            Finish(1, message);
    }

    private static void Finish(int code, string reason)
    {
        SessionState.SetBool(SmokeActiveKey, false);
        Debug.Log($"LOADTEST_SMOKE finished={reason}");
        EditorApplication.Exit(code);
    }

    private static string AddDefine(string symbols) =>
        string.IsNullOrEmpty(symbols) ? Define : symbols + ";" + Define;
}
