using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the viewer's settings and keyboard use to the scene: the settings
/// panel (which builds its gear buttons, modal and edge shading at run time)
/// and the keyboard navigator on the EventSystem. Safe to run any number of times.
/// </summary>
public static class SettingsSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SpriteFolder = "Assets/UI/Sprites/";

    [MenuItem("Tools/Digital Twin/Set Up Settings And Keyboard")]
    private static void SetUpFromMenu()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SetUp();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    /// <summary>Batch mode entry point: sets up the scene, saves it and exits with 0 on success.</summary>
    public static void RunBatch()
    {
        int code = 0;
        try
        {
            SetUpFromMenu();
            Debug.Log("SETTINGS_SETUP_RESULT passed");
        }
        catch (Exception exception)
        {
            Debug.LogError($"SETTINGS_SETUP_RESULT failed: {exception}");
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    public static void SetUp()
    {
        EventSystem events = UnityEngine.Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Include);
        if (events == null)
            throw new InvalidOperationException("The scene has no EventSystem.");
        if (!events.TryGetComponent(out KeyboardNavigator _))
            events.gameObject.AddComponent<KeyboardNavigator>();

        SettingsPanel settings = UnityEngine.Object.FindAnyObjectByType<SettingsPanel>(FindObjectsInactive.Include);
        if (settings == null)
            settings = new GameObject("Settings", typeof(SettingsPanel)).GetComponent<SettingsPanel>();
        // The scene keeps two roots, Scene and Digital Twin; the settings are part of the twin.
        GameObject twin = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(root => root.name == "Digital Twin");
        if (twin == null)
            throw new InvalidOperationException("The scene has no Digital Twin root.");
        settings.transform.SetParent(twin.transform, false);

        SerializedObject data = new(settings);
        data.FindProperty("settingsIcon").objectReferenceValue = Sprite("Configure Icon");
        data.FindProperty("backIcon").objectReferenceValue = Sprite("Arrow Left Icon");
        data.FindProperty("closeIcon").objectReferenceValue = Sprite("Close Icon");
        data.FindProperty("quality").objectReferenceValue =
            UnityEngine.Object.FindAnyObjectByType<AdaptiveQualityController>(FindObjectsInactive.Include);
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Sprite Sprite(string name)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + name + ".png");
        if (sprite == null)
            throw new InvalidOperationException($"Sprite {name} is missing from {SpriteFolder}.");
        return sprite;
    }
}
