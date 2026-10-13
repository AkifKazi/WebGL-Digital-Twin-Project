using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Moves the UI from sliced sprites to the design system's procedural shapes
/// (see UIShape, and the UI design system section of USER_GUIDE.md).
///
/// Creates or updates the design system's assets (UIDesignSystemDefaults),
/// then turns every Image in SampleScene and the UI prefabs into a UIShape and
/// removes the room the old sprites left for their baked glow. Shapes name
/// their component and state themselves; only icons are still sprites, drawn
/// by the theme's Icon component.
///
/// Safe to run again. Run it after any setup tool that rebuilds a UI prefab
/// (such as Set Up Telemetry Detail Panel), for the insets and styles below.
/// </summary>
public static class ProceduralUISkinSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string CardPrefabPath = "Assets/Prefabs/Performance Stat Card.prefab";
    private const string LinePrefabPath = "Assets/Prefabs/Stat Leader Line.prefab";
    private const string PanelPrefabPath = "Assets/Prefabs/Telemetry Detail Panel.prefab";
    private const string ShapeScriptPath = "Assets/Scripts/Runtime/UIShape.cs";
    private const string ThemePath = UIDesignSystemDefaults.ThemePath;
    private const string SpriteFolder = "Assets/UI/Sprites/";
    private const string CardVariant = UIDesignSystemDefaults.CardVariant;

    [MenuItem("Tools/Digital Twin/UI Skin/Apply Procedural Skin")]
    public static void Apply()
    {
        UITheme theme = UIDesignSystemDefaults.Build(false);
        int prefabShapes = ApplyToPrefabs();
        int sceneShapes = ApplyToScene();
        UITheme.RaiseChanged();
        Debug.Log($"PROCEDURAL_UI_SKIN applied: converted {sceneShapes} scene and {prefabShapes} prefab images " +
                  $"to shapes; {ThemePath} maps {theme.styles.Count} roles.");
    }

    /// <summary>Apply with the whole design system reset to its defaults (batch: -executeMethod).</summary>
    public static void ApplyWithDefaultStyles()
    {
        UIDesignSystemDefaults.Build(true);
        Apply();
    }

    [MenuItem("Tools/Digital Twin/UI Skin/Reset Design System To Defaults")]
    public static void ResetStyles()
    {
        if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Reset the design system?",
                "Every token, component and style is rewritten from its default. Edits to them are lost.",
                "Reset", "Cancel"))
            return;
        UIDesignSystemDefaults.Build(true);
        UITheme.RaiseChanged();
        Debug.Log("PROCEDURAL_UI_SKIN design system reset to its defaults.");
    }

    // ---- scene and prefabs ------------------------------------------------------

    private static int ApplyToPrefabs()
    {
        int count = 0;
        count += EditPrefab(CardPrefabPath, root =>
        {
            RectTransform background = Find(root, "Background");
            Shape(background).Variant = CardVariant;
            // The status glyph draws as authored.
            Shape(Find(root, "Status glyph")).Style = null;
            Shape(Find(root, "Status glyph")).Component = null;

            // A bar 3 wide, 4 off the card's edge, where the leader line meets it,
            // and its trail running outwards from the bar. Both run the card's
            // whole length: its height in a column, its width (the background's,
            // 24 in from each end) in a row.
            SetRect(Find(root, "Left accent/Core"), new(0f, 0f), new(0f, 1f), new(0.5f, 0.5f), new(18f, 0f), new(3f, 0f));
            SetRect(Find(root, "Right accent/Core"), new(1f, 0f), new(1f, 1f), new(0.5f, 0.5f), new(-18f, 0f), new(3f, 0f));
            SetRect(Find(root, "Top accent/Core"), new(0f, 0.5f), new(1f, 0.5f), new(0.5f, 0.5f), new(0f, 6f), new(-48f, 3f));
            SetRect(Find(root, "Bottom accent/Core"), new(0f, 0f), new(1f, 0f), new(0.5f, 0.5f), new(0f, -6f), new(-48f, 3f));
            SetRect(Find(root, "Left accent/Trail"), new(0f, 0f), new(0f, 1f), new(1f, 0.5f), new(16f, 0f), new(56f, 0f));
            SetRect(Find(root, "Right accent/Trail"), new(1f, 0f), new(1f, 1f), new(0f, 0.5f), new(-16f, 0f), new(56f, 0f));
            SetRect(Find(root, "Top accent/Trail"), new(0f, 0.5f), new(1f, 0.5f), new(0.5f, 0f), new(0f, 8f), new(-48f, 30f));
            SetRect(Find(root, "Bottom accent/Trail"), new(0f, 0f), new(1f, 0f), new(0.5f, 1f), new(0f, -8f), new(-48f, 30f));
            Find(root, "Top connection point").anchoredPosition = new Vector2(0f, 6f);
            Find(root, "Bottom connection point").anchoredPosition = new Vector2(0f, -6f);

            // The card's visible box is now its background rect: no glow margin inside it.
            PerformanceStatCardView view = root.GetComponent<PerformanceStatCardView>();
            SetFloat(view, "contentPaddingInsideBackground", 22f);
            SetFloat(view, "verticalContentPadding", 14f);

            // Hover lights the card like a control (the Card's hover look);
            // alarm states keep their own, a little brighter.
            if (!root.TryGetComponent(out UIHoverSprite hoverLook))
                hoverLook = root.AddComponent<UIHoverSprite>();
            hoverLook.Configure(background.GetComponent<Image>(), false, null);
        });

        count += EditPrefab(LinePrefabPath, root =>
        {
            // The sprite was a 12-unit square inside a 50-unit glow.
            Find(root, "Anchor marker").sizeDelta = new Vector2(12f, 12f);
        });

        count += EditPrefab(PanelPrefabPath, root =>
        {
            TelemetryDetailPanel panel = root.GetComponent<TelemetryDetailPanel>();
            // The frame line is the rect's edge now; sections keep 15 inside it.
            SetFloat(panel, "frameInset", 0f);
            SetFloat(panel, "sectionInset", 15f);
            foreach (string section in new[] { "Graph Section", "Detail Section" })
            {
                UIShape shape = Shape(Find(root, section));
                shape.Component = LoadComponent("Inset");
                // The shape's colour tints its style; the Image's old fill colour
                // would darken the well into something the service rows don't match.
                shape.color = Color.white;
            }
            SegmentRanges(root);
            SelectedPin(root);
        });
        return count;
    }

    /// <summary>
    /// Restyles only the telemetry detail panel's pin (batch: -executeMethod), for
    /// when the rest of the skin is already applied.
    /// </summary>
    public static void ApplyToPin() =>
        EditPrefab(PanelPrefabPath, SelectedPin);

    private static int ApplyToScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            count += ConvertImages(root);

        // The panels' frame is their edge now, so the gaps are measured edge to edge.
        TelemetryDetailPanelController panels =
            UnityEngine.Object.FindAnyObjectByType<TelemetryDetailPanelController>(FindObjectsInactive.Include);
        if (panels != null)
        {
            SetFloat(panels, "railGap", 16f);
            SetFloat(panels, "stackGap", 10f);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return count;
    }

    private static int EditPrefab(string path, Action<GameObject> edit)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int count = ConvertImages(root);
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            return count;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Turns every Image under <paramref name="root"/> into a UIShape in place,
    /// by changing its script. Every serialized value and every reference to the
    /// component (Buttons, views, hover looks) is kept.
    /// </summary>
    private static int ConvertImages(GameObject root)
    {
        MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(ShapeScriptPath);
        if (script == null || script.GetClass() != typeof(UIShape))
            throw new InvalidOperationException($"{ShapeScriptPath} is missing or not compiled.");

        int count = 0;
        foreach (Image image in root.GetComponentsInChildren<Image>(true))
        {
            if (image.GetType() != typeof(Image))
                continue;
            var serialized = new SerializedObject(image);
            serialized.FindProperty("m_Script").objectReferenceValue = script;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            count++;
        }
        return count;
    }

    // ---- helpers ------------------------------------------------------------------

    private static UIShape Shape(RectTransform rect)
    {
        UIShape shape = rect.GetComponent<UIShape>();
        if (shape == null)
            throw new InvalidOperationException($"{rect.name} has no UIShape.");
        return shape;
    }

    private static RectTransform Find(GameObject root, string path)
    {
        Transform found = root.transform.Find(path) ??
                          root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == path);
        if (found == null)
            throw new InvalidOperationException($"{root.name} has no {path}.");
        return (RectTransform)found;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
        Vector2 position, Vector2 size)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void SetFloat(UnityEngine.Object target, string field, float value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
            throw new InvalidOperationException($"{target.GetType().Name} has no field {field}.");
        property.floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static UIComponentStyle LoadComponent(string name) =>
        UITheme.Active.Component(name) ?? throw new InvalidOperationException($"Component {name} is missing.");

    /// <summary>
    /// The detail panel's H | D | W | M | Y ranges as a segmented control, like
    /// Telemetry | Service: a panel frame, and each range a segment that shows
    /// Selected for the range on show (TelemetryDetailPanel sets the state).
    /// The underlines stay, unseen, as the hover look's selected indicator.
    /// </summary>
    private static void SegmentRanges(GameObject root)
    {
        // The panel widens the cells to fit its width at run time (TelemetryDetailPanel.UpdateHeaderLayout).
        const float Cell = 46f;
        const float CellHeight = 34f;
        const float Pad = 3f;
        RectTransform ranges = Find(root, "Ranges");
        HorizontalLayoutGroup row = ranges.GetComponent<HorizontalLayoutGroup>();
        row.spacing = 0f;
        row.padding = new RectOffset((int)Pad, (int)Pad, (int)Pad, (int)Pad);
        row.childAlignment = TextAnchor.MiddleCenter;
        int count = 0;
        foreach (Transform child in ranges)
        {
            if (child.name == "Divider")
            {
                child.gameObject.SetActive(false);
                continue;
            }
            count++;
            LayoutElement cell = child.GetComponent<LayoutElement>();
            cell.preferredWidth = cell.minWidth = Cell;
            cell.preferredHeight = cell.minHeight = CellHeight;
            UIShape segment = Shape((RectTransform)child);
            segment.Component = LoadComponent("Segment");
            segment.color = Color.white;
            child.GetComponentInChildren<TMPro.TMP_Text>(true).fontSize = 23f;
            UIShape underline = Shape(Find(child.gameObject, "Underline"));
            underline.Component = LoadComponent("Underline");
            underline.color = Color.clear;
        }
        if (!ranges.TryGetComponent(out UIShape frame))
            frame = ranges.gameObject.AddComponent<UIShape>();
        frame.Component = LoadComponent("Panel");
        frame.raycastTarget = false;
        SetRect(ranges, new(0f, 0.5f), new(0f, 0.5f), new(0f, 0.5f),
            new(ranges.anchoredPosition.x, 0f), new(count * Cell + Pad * 2f, CellHeight + Pad * 2f));
    }

    /// <summary>
    /// A pinned panel's pin, drawn as the selected range beside it is: a
    /// Selected segment, set in as far as the ranges sit inside their frame.
    /// Its icon takes the control colours (TelemetryDetailPanel.SetPinned).
    /// </summary>
    private static void SelectedPin(GameObject root)
    {
        const float Pad = 3f;
        RectTransform pinned = Find(root, "Pinned");
        UIShape shape = Shape(pinned);
        shape.Component = LoadComponent("Segment");
        shape.State = UIState.Selected;
        shape.color = Color.white;
        shape.raycastTarget = false;
        pinned.anchorMin = Vector2.zero;
        pinned.anchorMax = Vector2.one;
        pinned.offsetMin = new Vector2(Pad, Pad);
        pinned.offsetMax = new Vector2(-Pad, -Pad);
    }

    private static Sprite LoadSprite(string name) =>
        AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + name + ".png");
}
