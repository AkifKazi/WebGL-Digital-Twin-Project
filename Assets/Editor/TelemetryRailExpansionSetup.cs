using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class TelemetryRailExpansionSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Digital Twin/Set Up Overflow Rails")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ConfigureWideRails();
        ConfigurePortraitRails();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Telemetry overflow rails configured successfully.");
    }

    private static void ConfigureWideRails()
    {
        WideStatRailManager manager = UnityEngine.Object.FindFirstObjectByType<WideStatRailManager>(
            FindObjectsInactive.Include);
        if (manager == null)
            throw new InvalidOperationException("Wide telemetry rail manager was not found.");

        RectTransform content = FindUnderLayout("Content area", "Wide layout");
        RectTransform stage = FindDirectChild(content, "Stage area");
        RectTransform leftOuter = FindDirectChild(content, "Left primary rail");
        RectTransform rightOuter = FindDirectChild(content, "Right primary rail");

        RectTransform innerLeft = CreateRail(
            content, "Left overflow rail", "Left overflow card container", 280f, -1f);
        RectTransform innerRight = CreateRail(
            content, "Right overflow rail", "Right overflow card container", 280f, -1f);

        innerLeft.parent.SetSiblingIndex(stage.GetSiblingIndex());
        innerRight.parent.SetSiblingIndex(stage.GetSiblingIndex() + 1);

        SerializedObject data = new(manager);
        data.FindProperty("innerLeftCardContainer").objectReferenceValue = innerLeft;
        data.FindProperty("innerRightCardContainer").objectReferenceValue = innerRight;
        data.ApplyModifiedPropertiesWithoutUndo();

        innerLeft.parent.gameObject.SetActive(false);
        innerRight.parent.gameObject.SetActive(false);
    }

    private static void ConfigurePortraitRails()
    {
        PortraitStatRailManager manager = UnityEngine.Object.FindFirstObjectByType<PortraitStatRailManager>(
            FindObjectsInactive.Include);
        if (manager == null)
            throw new InvalidOperationException("Portrait telemetry rail manager was not found.");

        RectTransform content = FindUnderLayout("Content area", "Portrait layout");
        RectTransform stage = FindDirectChild(content, "Stage area");

        RectTransform innerTop = CreateRail(
            content, "Top overflow rail", "Top overflow card container", -1f, 150f);
        RectTransform innerBottom = CreateRail(
            content, "Bottom overflow rail", "Bottom overflow card container", -1f, 150f);

        innerTop.parent.SetSiblingIndex(stage.GetSiblingIndex());
        innerBottom.parent.SetSiblingIndex(stage.GetSiblingIndex() + 1);

        SerializedObject data = new(manager);
        data.FindProperty("innerTopCardContainer").objectReferenceValue = innerTop;
        data.FindProperty("innerBottomCardContainer").objectReferenceValue = innerBottom;
        data.ApplyModifiedPropertiesWithoutUndo();

        innerTop.parent.gameObject.SetActive(false);
        innerBottom.parent.gameObject.SetActive(false);
    }

    private static RectTransform CreateRail(
        RectTransform parent,
        string railName,
        string containerName,
        float preferredWidth,
        float preferredHeight)
    {
        Transform existing = parent.Find(railName);
        GameObject railObject = existing != null
            ? existing.gameObject
            : new GameObject(railName, typeof(RectTransform), typeof(LayoutElement));
        railObject.layer = LayerMask.NameToLayer("UI");
        railObject.transform.SetParent(parent, false);

        LayoutElement layout = railObject.GetComponent<LayoutElement>();
        layout.preferredWidth = preferredWidth;
        layout.preferredHeight = preferredHeight;
        layout.flexibleWidth = preferredWidth > 0f ? 0f : 1f;
        layout.flexibleHeight = preferredHeight > 0f ? 0f : 1f;

        Transform existingContainer = railObject.transform.Find(containerName);
        GameObject containerObject = existingContainer != null
            ? existingContainer.gameObject
            : new GameObject(containerName, typeof(RectTransform));
        containerObject.layer = LayerMask.NameToLayer("UI");
        containerObject.transform.SetParent(railObject.transform, false);
        RectTransform container = containerObject.GetComponent<RectTransform>();
        container.anchorMin = Vector2.zero;
        container.anchorMax = Vector2.one;
        container.offsetMin = Vector2.zero;
        container.offsetMax = Vector2.zero;
        return container;
    }

    private static RectTransform FindUnderLayout(string name, string layoutName) =>
        UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include)
            .FirstOrDefault(rect => rect.name == name && HasAncestor(rect, layoutName))
        ?? throw new InvalidOperationException($"'{name}' under '{layoutName}' was not found.");

    private static RectTransform FindDirectChild(RectTransform parent, string name) =>
        parent.Cast<Transform>()
            .FirstOrDefault(child => child.name == name) as RectTransform
        ?? throw new InvalidOperationException($"Direct child '{name}' was not found under '{parent.name}'.");

    private static bool HasAncestor(Transform transform, string name)
    {
        while (transform != null)
        {
            if (transform.name == name)
                return true;
            transform = transform.parent;
        }
        return false;
    }
}
