using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds the Ask window to the scene: the window (which builds its Ask buttons,
/// window and the portrait ASK tab at run time) under the Digital Twin root, and the
/// assistant that answers it on the Digital Twin Runtime object, where the
/// hosting page's SendMessage reaches it. Creates the design system's message
/// components if they are missing. Safe to run any number of times.
/// </summary>
public static class AskTwinSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SpriteFolder = "Assets/UI/Sprites/";

    [MenuItem("Tools/Digital Twin/Set Up Ask Window")]
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
            Debug.Log("ASK_SETUP_RESULT passed");
        }
        catch (Exception exception)
        {
            Debug.LogError($"ASK_SETUP_RESULT failed: {exception}");
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    public static void SetUp()
    {
        // The bubbles are design-system components; edits to existing ones are kept.
        UIDesignSystemDefaults.Build(false);

        GameObject twin = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(root => root.name == "Digital Twin");
        if (twin == null)
            throw new InvalidOperationException("The scene has no Digital Twin root.");

        GameObject runtime = GameObject.Find(UITextTableLoader.RuntimeObjectName);
        if (runtime == null)
            throw new InvalidOperationException($"The scene has no {UITextTableLoader.RuntimeObjectName} object.");
        if (!runtime.TryGetComponent(out TwinAssistant assistant))
            assistant = runtime.AddComponent<TwinAssistant>();

        AskTwinWindow window = UnityEngine.Object.FindAnyObjectByType<AskTwinWindow>(FindObjectsInactive.Include);
        if (window == null)
            window = new GameObject("Ask Twin", typeof(AskTwinWindow)).GetComponent<AskTwinWindow>();
        window.transform.SetParent(twin.transform, false);

        SerializedObject data = new(window);
        data.FindProperty("askIcon").objectReferenceValue = Sprite("Ask Icon");
        data.FindProperty("closeIcon").objectReferenceValue = Sprite("Close Icon");
        data.FindProperty("sendIcon").objectReferenceValue = Sprite("Arrow Right Icon");
        data.FindProperty("assistant").objectReferenceValue = assistant;
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
