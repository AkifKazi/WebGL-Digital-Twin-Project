using System;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Applies the approved Rajdhani type hierarchy without modifying UI graphics.
/// Medium is used for labels and controls; SemiBold is used for readings.
/// </summary>
public static class UIFontStandardization
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string CardPrefabPath = "Assets/Prefabs/Performance Stat Card.prefab";
    private const string MediumPath = "Assets/UI/Fonts/Rajdhani-Medium SDF.asset";
    private const string SemiBoldPath = "Assets/UI/Fonts/Rajdhani-SemiBold SDF.asset";

    [MenuItem("Tools/Digital Twin/Standardize UI Fonts")]
    public static void Apply()
    {
        TMP_FontAsset medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumPath);
        TMP_FontAsset semiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SemiBoldPath);
        if (medium == null || semiBold == null)
            throw new InvalidOperationException("Rajdhani Medium or SemiBold SDF font is missing.");

        int prefabCount = ApplyToCardPrefab(medium, semiBold);
        int sceneCount = ApplyToScene(medium, semiBold);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"UI_FONT_STANDARDIZATION_RESULT sceneTexts={sceneCount} prefabTexts={prefabCount}");
    }

    private static int ApplyToCardPrefab(TMP_FontAsset medium, TMP_FontAsset semiBold)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(CardPrefabPath);
        try
        {
            int count = 0;
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                bool reading = IsReadingText(text.transform);
                AssignFont(text, reading ? semiBold : medium);
                count++;
            }

            PerformanceStatCardView card = root.GetComponent<PerformanceStatCardView>();
            if (card == null)
                throw new InvalidOperationException("Performance Stat Card prefab has no card view.");

            SerializedObject cardData = new(card);
            cardData.FindProperty("useAccurateUnitCasing").boolValue = false;
            cardData.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
            return count;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static int ApplyToScene(TMP_FontAsset medium, TMP_FontAsset semiBold)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                // Bottom controls deliberately use Medium for every label.
                bool reading = !HasAncestor(text.transform, "Bottom controls") &&
                               IsReadingText(text.transform);
                AssignFont(text, reading ? semiBold : medium);
                count++;
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return count;
    }

    private static bool IsReadingText(Transform transform)
    {
        string objectName = transform.name;
        if (objectName.Equals("Value", StringComparison.OrdinalIgnoreCase) ||
            objectName.Equals("Value text", StringComparison.OrdinalIgnoreCase) ||
            objectName.Equals("Unit text", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return HasAncestor(transform, "Value row") ||
               HasAncestor(transform, "FPS Diagnostics");
    }

    private static bool HasAncestor(Transform transform, string expectedName)
    {
        for (Transform current = transform.parent; current != null; current = current.parent)
        {
            if (current.name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void AssignFont(TMP_Text text, TMP_FontAsset font)
    {
        text.font = font;
        text.fontStyle = FontStyles.Normal;
        EditorUtility.SetDirty(text);
    }
}
