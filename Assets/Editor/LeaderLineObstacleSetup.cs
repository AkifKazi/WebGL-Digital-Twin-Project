using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Marks the translucent controls that leader lines fade over, in both
/// layouts: the view mode, graphics quality and zoom controls, the
/// Telemetry / Service switch, and the pagination (in the portrait layout,
/// where the page buttons share a row with the zoom, just the two buttons).
/// Found by their components, not their names. Safe to run any number of times.
///
/// The setup tools that rebuild these controls call <see cref="MarkAll"/>.
/// </summary>
public static class LeaderLineObstacleSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Digital Twin/Mark Leader Line Obstacles")]
    private static void MarkFromMenu()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        MarkAll();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>Batch mode entry point: marks the scene, saves it and exits with 0 on success.</summary>
    public static void RunBatch()
    {
        int code = 0;
        try
        {
            MarkFromMenu();
        }
        catch (Exception exception)
        {
            Debug.LogError($"LEADER_LINE_OBSTACLE_SETUP_RESULT failed: {exception}");
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    /// <summary>Marks every translucent control in the open scene.</summary>
    public static void MarkAll()
    {
        List<GameObject> controls = new();
        Add<ViewModeSegmentedControl>(controls);
        Add<QualityToggleControl>(controls);
        Add<ZoomControlAvailabilityView>(controls);
        Add<DigitalTwinModeSwitch>(controls);
        foreach (PortraitTelemetryPager pager in Find<PortraitTelemetryPager>())
        {
            if (pager.RailManager != null && pager.RailManager.AlwaysRows)
            {
                // The portrait row also holds the zoom and quality controls,
                // marked on their own; only its page buttons are added here.
                if (pager.PreviousButton != null)
                    controls.Add(pager.PreviousButton.gameObject);
                if (pager.NextButton != null)
                    controls.Add(pager.NextButton.gameObject);
            }
            else
            {
                controls.Add(pager.gameObject);
            }
        }

        int added = 0;
        foreach (GameObject control in controls)
        {
            if (control.GetComponent<LeaderLineObstacle>() != null)
                continue;
            control.AddComponent<LeaderLineObstacle>();
            added++;
        }
        Debug.Log($"LEADER_LINE_OBSTACLE_SETUP_RESULT passed controls={controls.Count} added={added}");
    }

    private static void Add<T>(List<GameObject> controls) where T : Component
    {
        foreach (T component in Find<T>())
            controls.Add(component.gameObject);
    }

    private static T[] Find<T>() where T : Component =>
        UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include);
}
