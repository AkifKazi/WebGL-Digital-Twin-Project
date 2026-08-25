using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class ConnectionHealthUISetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private static readonly Color Cyan = new(0.08f, 0.9f, 1f, 1f);
    private static readonly Color LabelColor = new(0.48f, 0.67f, 0.72f, 1f);

    [MenuItem("Tools/Digital Twin/Set Up Connection Health UI")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ConnectionHealthMonitor monitor = ConfigureMonitor();

        RectTransform wideTopBar = FindTopBar("Wide layout");
        RectTransform portraitTopBar = FindTopBar("Portrait layout");

        if (wideTopBar == null || portraitTopBar == null)
            throw new InvalidOperationException("Wide or portrait Top bar was not found.");

        CreateHealthStrip(wideTopBar, monitor, false);
        CreateHealthStrip(portraitTopBar, monitor, true);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Connection health UI setup completed successfully.");
    }

    private static ConnectionHealthMonitor ConfigureMonitor()
    {
        GameObject runtime = UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .First(transform => transform.name == "Digital Twin Runtime")
            .gameObject;

        ConnectionHealthMonitor monitor = runtime.GetComponent<ConnectionHealthMonitor>();
        if (monitor == null)
            monitor = runtime.AddComponent<ConnectionHealthMonitor>();

        TelemetryJsonIngestor ingestor = runtime.GetComponent<TelemetryJsonIngestor>();
        if (ingestor != null)
        {
            SerializedObject ingestorData = new(ingestor);
            ingestorData.FindProperty("connectionHealthMonitor").objectReferenceValue = monitor;
            ingestorData.ApplyModifiedPropertiesWithoutUndo();
        }

        return monitor;
    }

    private static RectTransform FindTopBar(string layoutName)
    {
        return UnityEngine.Object.FindObjectsByType<RectTransform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .FirstOrDefault(rect => rect.name == "Top bar" && HasAncestor(rect, layoutName));
    }

    private static bool HasAncestor(Transform transform, string ancestorName)
    {
        while (transform != null)
        {
            if (transform.name == ancestorName)
                return true;
            transform = transform.parent;
        }
        return false;
    }

    private static void CreateHealthStrip(
        RectTransform topBar,
        ConnectionHealthMonitor monitor,
        bool portrait)
    {
        LayoutElement topBarLayout = topBar.GetComponent<LayoutElement>();
        if (portrait && topBarLayout != null)
            topBarLayout.preferredHeight = 112f;

        Transform existing = topBar.Find("Connection Health");
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing.gameObject);

        GameObject rootObject = CreateUIObject("Connection Health", topBar);
        RectTransform root = rootObject.GetComponent<RectTransform>();
        Stretch(root);

        List<Image> accents = new();
        List<TMP_Text> values = new();
        TMP_Text modeValue;
        TMP_Text gatewayValue;
        TMP_Text lastUpdateValue;
        TMP_Text qualityValue;
        Image gatewayDot;

        HorizontalLayoutGroup row = rootObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = portrait ? new RectOffset(16, 16, 10, 10) : new RectOffset(24, 24, 4, 4);
        row.spacing = 10f;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = true;
        row.childForceExpandHeight = true;

        if (portrait)
        {
            modeValue = null;
            qualityValue = null;
            gatewayValue = CreateCell(
                root,
                "GATEWAY",
                true,
                true,
                accents,
                values,
                out gatewayDot);
            lastUpdateValue = CreateCell(
                root,
                "LAST UPDATE",
                false,
                true,
                accents,
                values,
                out _);
        }
        else
        {
            CreateCombinedCell(
                root,
                "Connection",
                "MODE",
                "GATEWAY",
                false,
                true,
                accents,
                values,
                out modeValue,
                out gatewayValue,
                out gatewayDot);
            CreateCombinedCell(
                root,
                "Data freshness",
                "LAST UPDATE",
                "DATA QUALITY",
                false,
                false,
                accents,
                values,
                out lastUpdateValue,
                out qualityValue,
                out _);
        }

        ConnectionHealthView view = rootObject.AddComponent<ConnectionHealthView>();
        SerializedObject viewData = new(view);
        viewData.FindProperty("monitor").objectReferenceValue = monitor;
        viewData.FindProperty("modeValue").objectReferenceValue = modeValue;
        viewData.FindProperty("gatewayValue").objectReferenceValue = gatewayValue;
        viewData.FindProperty("lastUpdateValue").objectReferenceValue = lastUpdateValue;
        viewData.FindProperty("qualityValue").objectReferenceValue = qualityValue;
        viewData.FindProperty("gatewayDot").objectReferenceValue = gatewayDot;
        SetObjectArray(viewData.FindProperty("accentImages"), accents);
        SetObjectArray(viewData.FindProperty("stateValueTexts"), values);
        viewData.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateCombinedCell(
        RectTransform parent,
        string objectName,
        string firstLabel,
        string secondLabel,
        bool stacked,
        bool secondHasDot,
        ICollection<Image> accents,
        ICollection<TMP_Text> values,
        out TMP_Text firstValue,
        out TMP_Text secondValue,
        out Image dot)
    {
        GameObject cellObject = CreateUIObject(objectName, parent);
        LayoutElement layout = cellObject.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.flexibleHeight = 1f;

        Image background = cellObject.AddComponent<Image>();
        background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/UI/Sprites/Panel Background.png");
        background.type = Image.Type.Sliced;
        background.color = new Color(1f, 1f, 1f, 0.88f);
        background.raycastTarget = false;

        RectTransform cell = cellObject.GetComponent<RectTransform>();
        GameObject contentObject = CreateUIObject("Content", cell);
        RectTransform content = contentObject.GetComponent<RectTransform>();
        content.anchorMin = Vector2.zero;
        content.anchorMax = Vector2.one;
        content.offsetMin = new Vector2(14f, 5f);
        content.offsetMax = new Vector2(-8f, -5f);

        HorizontalOrVerticalLayoutGroup group;
        if (stacked)
            group = contentObject.AddComponent<VerticalLayoutGroup>();
        else
            group = contentObject.AddComponent<HorizontalLayoutGroup>();
        group.spacing = stacked ? 3f : 12f;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = true;
        group.childForceExpandHeight = true;

        firstValue = CreateMetricBlock(content, firstLabel, stacked, false, out _);
        secondValue = CreateMetricBlock(content, secondLabel, stacked, secondHasDot, out dot);
        values.Add(firstValue);
        values.Add(secondValue);
    }

    private static TMP_Text CreateMetricBlock(
        RectTransform parent,
        string label,
        bool compact,
        bool showDot,
        out Image dot)
    {
        GameObject blockObject = CreateUIObject(label, parent);
        LayoutElement layout = blockObject.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.flexibleHeight = 1f;
        RectTransform block = blockObject.GetComponent<RectTransform>();

        TMP_Text labelText = CreateText("Label", block, label, compact ? 13f : 12f, LabelColor, false);
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0.52f);
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = new Vector2(-4f, -2f);

        TMP_Text valueText = CreateText("Value", block, "—", compact ? 18f : 18f, Cyan, true);
        RectTransform valueRect = valueText.rectTransform;
        valueRect.anchorMin = Vector2.zero;
        valueRect.anchorMax = new Vector2(1f, 0.58f);
        valueRect.offsetMin = Vector2.zero;
        valueRect.offsetMax = new Vector2(showDot ? -26f : -4f, 0f);

        dot = null;
        if (showDot)
        {
            dot = CreateImage(
                "Gateway status",
                block,
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Solid UI Fill.png"),
                Cyan);
            RectTransform dotRect = dot.rectTransform;
            dotRect.anchorMin = new Vector2(1f, 0.29f);
            dotRect.anchorMax = new Vector2(1f, 0.29f);
            dotRect.pivot = new Vector2(1f, 0.5f);
            dotRect.anchoredPosition = new Vector2(-2f, 0f);
            dotRect.sizeDelta = new Vector2(18f, 18f);
        }

        return valueText;
    }

    private static RectTransform CreateRow(RectTransform parent, float spacing)
    {
        GameObject rowObject = CreateUIObject("Health row", parent);
        LayoutElement layout = rowObject.AddComponent<LayoutElement>();
        layout.flexibleHeight = 1f;
        layout.flexibleWidth = 1f;

        HorizontalLayoutGroup row = rowObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = spacing;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = true;
        row.childForceExpandHeight = true;
        return rowObject.GetComponent<RectTransform>();
    }

    private static TMP_Text CreateCell(
        RectTransform parent,
        string label,
        bool showDot,
        bool compact,
        ICollection<Image> accents,
        ICollection<TMP_Text> values,
        out Image dot)
    {
        GameObject cellObject = CreateUIObject(label, parent);
        LayoutElement layout = cellObject.AddComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.flexibleHeight = 1f;
        layout.minWidth = compact ? 0f : 180f;

        Image background = cellObject.AddComponent<Image>();
        background.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
            "Assets/UI/Sprites/Panel Background.png");
        background.type = Image.Type.Sliced;
        background.color = new Color(1f, 1f, 1f, 0.88f);
        background.raycastTarget = false;

        RectTransform cell = cellObject.GetComponent<RectTransform>();

        float left = 16f;
        float labelSize = compact ? 18f : 12f;
        float valueSize = compact ? 24f : 18f;

        TMP_Text labelText = CreateText("Label", cell, label, labelSize, LabelColor, false);
        RectTransform labelRect = labelText.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0.52f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(left, 0f);
        labelRect.offsetMax = new Vector2(compact ? -16f : -10f, -4f);

        TMP_Text valueText = CreateText("Value", cell, "—", valueSize, Cyan, true);
        RectTransform valueRect = valueText.rectTransform;
        valueRect.anchorMin = new Vector2(0f, 0f);
        valueRect.anchorMax = new Vector2(1f, 0.58f);
        valueRect.offsetMin = new Vector2(left, 3f);
        valueRect.offsetMax = new Vector2(
            showDot ? (compact ? -44f : -34f) : (compact ? -16f : -8f),
            0f);
        values.Add(valueText);

        dot = null;
        if (showDot)
        {
            dot = CreateImage(
                "Gateway status",
                cell,
                AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Solid UI Fill.png"),
                Cyan);
            RectTransform dotRect = dot.rectTransform;
            dotRect.anchorMin = new Vector2(1f, 0.5f);
            dotRect.anchorMax = new Vector2(1f, 0.5f);
            dotRect.pivot = new Vector2(1f, 0.5f);
            dotRect.anchoredPosition = new Vector2(compact ? -16f : -10f, -4f);
            float size = compact ? 24f : 22f;
            dotRect.sizeDelta = new Vector2(size, size);
        }

        return valueText;
    }

    private static TMP_Text CreateText(
        string name,
        RectTransform parent,
        string text,
        float fontSize,
        Color color,
        bool semibold)
    {
        GameObject textObject = CreateUIObject(name, parent);
        TextMeshProUGUI component = textObject.AddComponent<TextMeshProUGUI>();
        component.text = text;
        component.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(semibold
            ? "Assets/UI/Fonts/Rajdhani-SemiBold SDF.asset"
            : "Assets/UI/Fonts/Rajdhani-Medium SDF.asset");
        component.fontSize = fontSize;
        component.color = color;
        component.alignment = TextAlignmentOptions.Left;
        component.textWrappingMode = TextWrappingModes.NoWrap;
        component.overflowMode = TextOverflowModes.Ellipsis;
        component.characterSpacing = semibold ? 1.2f : 1.8f;
        component.raycastTarget = false;
        return component;
    }

    private static Image CreateImage(
        string name,
        RectTransform parent,
        Sprite sprite,
        Color color)
    {
        GameObject imageObject = CreateUIObject(name, parent);
        Image image = imageObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        image.preserveAspect = sprite != null;
        return image;
    }

    private static GameObject CreateUIObject(string name, Transform parent)
    {
        GameObject gameObject = new(name, typeof(RectTransform));
        gameObject.layer = LayerMask.NameToLayer("UI");
        gameObject.transform.SetParent(parent, false);
        return gameObject;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetObjectArray<T>(SerializedProperty property, IReadOnlyList<T> values)
        where T : UnityEngine.Object
    {
        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
