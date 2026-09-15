using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the telemetry detail panel prefab and wires the panel controller, the
/// history recorder, the card focus releaser and the control hover states into
/// SampleScene. Safe to run again: it rebuilds the prefab and reuses what the
/// scene already has. Needs the sprite names from Tools/Digital Twin/Organize
/// and Rename Assets.
/// </summary>
public static class TelemetryDetailPanelSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string PrefabPath = "Assets/Prefabs/Telemetry Detail Panel.prefab";
    private const string PanelSpritePath = "Assets/UI/Sprites/Detail Panel Background.png";
    private const string HoverSpritePath = "Assets/UI/Sprites/Button Hover Background.png";
    private const string MediumFontPath = "Assets/UI/Fonts/Rajdhani-Medium SDF.asset";
    private const string SemiBoldFontPath = "Assets/UI/Fonts/Rajdhani-SemiBold SDF.asset";
    private const string LayerName = "Telemetry Detail Layer";

    private static readonly Color Teal = new(0.08f, 0.9f, 1f, 1f);
    private static readonly Color Idle = new(0.408f, 0.651f, 0.686f, 1f);
    private static readonly Color Ink = new(0.918f, 0.992f, 1f, 1f);
    private static readonly Color Clear = new(1f, 1f, 1f, 0f);
    private static readonly Color Section = new(0.035f, 0.102f, 0.137f, 1f);

    // Panel geometry, in canvas units. The panel sprite draws its frame line
    // 10 in from the rectangle's edge; the spacing below is measured from it.
    private const float Frame = 10f;
    private const float HeaderHeight = 44f;
    private const float RangeCell = 36f;
    private const float RangeSpacing = 4f;
    private const float HeaderButton = 32f;

    [MenuItem("Tools/Digital Twin/Set Up Telemetry Detail Panel")]
    public static void Apply()
    {
        BuildPrefab();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        // Load the prefab after the scene opens: opening a scene drops references
        // held from before, which would leave the controller without its prefab.
        TelemetryDetailPanel prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)
            .GetComponent<TelemetryDetailPanel>();
        TelemetryHistoryRecorder recorder = ConfigureRecorder();
        ConfigureController(prefab, recorder);
        ConfigureFocusReleaser();
        int hovers = ConfigureControlHoverStates();
        RenameSecondaryRails();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"TELEMETRY_DETAIL_PANEL_SETUP prefab='{PrefabPath}' hoverStates={hovers}");
    }

    // -----------------------------------------------------------------------
    // Prefab
    // -----------------------------------------------------------------------

    private static TelemetryDetailPanel BuildPrefab()
    {
        Sprite panelSprite = LoadSprite(PanelSpritePath);
        Sprite hoverSprite = LoadSprite(HoverSpritePath);
        TMP_FontAsset medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumFontPath);

        GameObject root = new("Telemetry Detail Panel", typeof(RectTransform));
        root.layer = LayerMask.NameToLayer("UI");
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(420f, 420f);
        CanvasGroup group = root.AddComponent<CanvasGroup>();
        TelemetryDetailPanel panel = root.AddComponent<TelemetryDetailPanel>();

        Image background = CreateImage("Background", rect, panelSprite, Color.white);
        Stretch(background.rectTransform, 0f);
        background.type = Image.Type.Sliced;
        background.raycastTarget = true; // clicks on the panel never reach the scene

        // Header: range buttons on the left, pin and close on the right, all on
        // one row centred 22 below the frame line.
        RectTransform header = CreateUIObject("Header", rect);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = new Vector2(1f, 1f);
        header.pivot = new Vector2(0.5f, 1f);
        header.offsetMin = new Vector2(Frame, -(Frame + HeaderHeight));
        header.offsetMax = new Vector2(-Frame, -Frame);

        RectTransform ranges = CreateUIObject("Ranges", header);
        ranges.anchorMin = new Vector2(0f, 0f);
        ranges.anchorMax = new Vector2(0f, 1f);
        ranges.pivot = new Vector2(0f, 0.5f);
        int count = TelemetryHistoryRanges.All.Length;
        ranges.sizeDelta = new Vector2(count * RangeCell + (count - 1) * (1f + RangeSpacing * 2f), 0f);
        ranges.anchoredPosition = new Vector2(7f, 0f); // first letter centred 25 in from the frame
        HorizontalLayoutGroup row = ranges.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = RangeSpacing;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;

        List<Button> rangeButtons = new();
        List<TMP_Text> rangeLabels = new();
        List<Graphic> rangeUnderlines = new();
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
            {
                Image divider = CreateImage("Divider", ranges, null, new Color(Idle.r, Idle.g, Idle.b, 0.35f));
                LayoutElement dividerSize = divider.gameObject.AddComponent<LayoutElement>();
                dividerSize.preferredWidth = dividerSize.minWidth = 1f;
                dividerSize.preferredHeight = dividerSize.minHeight = 9f;
            }

            string letter = TelemetryHistoryRanges.ShortLabel(TelemetryHistoryRanges.All[i]);
            Image hit = CreateImage(letter, ranges, null, Clear);
            hit.raycastTarget = true;
            LayoutElement cell = hit.gameObject.AddComponent<LayoutElement>();
            cell.preferredWidth = cell.minWidth = RangeCell;
            cell.preferredHeight = cell.minHeight = RangeCell;
            Button button = hit.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = hit;

            Image hover = CreateHoverImage(hit.rectTransform, 3f);

            TMP_Text label = CreateText("Label", hit.rectTransform, letter, 19f, Idle, medium);
            Stretch(label.rectTransform, 0f);
            label.alignment = TextAlignmentOptions.Center;

            Image underline = CreateImage("Underline", hit.rectTransform, null, Teal);
            underline.rectTransform.anchorMin = new Vector2(0.5f, 0f);
            underline.rectTransform.anchorMax = new Vector2(0.5f, 0f);
            underline.rectTransform.pivot = new Vector2(0.5f, 0f);
            underline.rectTransform.sizeDelta = new Vector2(26f, 2f);
            underline.rectTransform.anchoredPosition = new Vector2(0f, 7f);
            underline.enabled = false;

            // The selected range shows its underline and never the hover.
            AddHover(hit.gameObject, hover, hoverSprite, null, underline);
            rangeButtons.Add(button);
            rangeLabels.Add(label);
            rangeUnderlines.Add(underline);
        }

        // Close centred 23 in from the frame's right edge; pin's 21 px square 16 to its left.
        Button closeButton = CreateIconButton("Close", header, -7f, UIIconGraphic.Shape.Close,
            new Color(Ink.r, Ink.g, Ink.b, 0.85f), hoverSprite, out _, out Image closeHover);
        AddHover(closeButton.gameObject, closeHover, hoverSprite, null, null);

        Button pinButton = CreateIconButton("Pin", header, -41f, UIIconGraphic.Shape.Pin,
            Teal, hoverSprite, out UIIconGraphic pinIcon, out Image pinHover);
        Image pinBackground = CreateImage("Pinned", (RectTransform)pinButton.transform, null, new Color(Teal.r, Teal.g, Teal.b, 0.9f));
        Stretch(pinBackground.rectTransform, 5.5f);
        pinBackground.transform.SetAsFirstSibling();
        pinBackground.enabled = false;
        // A pinned panel shows the filled square and never the hover.
        AddHover(pinButton.gameObject, pinHover, hoverSprite, null, pinBackground);

        Image graphSection = CreateImage("Graph Section", rect, null, Section);
        RectTransform graphs = CreateUIObject("Graphs", graphSection.rectTransform);
        VerticalLayoutGroup stack = graphs.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        Image detailSection = CreateImage("Detail Section", rect, null, Section);
        TMP_Text detailText = CreateText("Text", detailSection.rectTransform, string.Empty, 16f, new Color(Ink.r, Ink.g, Ink.b, 0.85f), medium);
        detailText.alignment = TextAlignmentOptions.TopLeft;
        detailText.textWrappingMode = TextWrappingModes.Normal;
        detailSection.gameObject.SetActive(false);

        RectTransform pointerRect = CreateUIObject("Pointer", rect);
        pointerRect.sizeDelta = new Vector2(11f, 20f);
        UITriangleGraphic pointer = pointerRect.gameObject.AddComponent<UITriangleGraphic>();
        pointer.color = Teal;
        pointer.raycastTarget = false;

        RectTransform link = CreateUIObject("Leader Line Anchor", rect);
        link.sizeDelta = new Vector2(4f, 4f);

        SerializedObject data = new(panel);
        data.FindProperty("canvasGroup").objectReferenceValue = group;
        SetArray(data.FindProperty("rangeButtons"), rangeButtons);
        SetArray(data.FindProperty("rangeLabels"), rangeLabels);
        SetArray(data.FindProperty("rangeUnderlines"), rangeUnderlines);
        data.FindProperty("pinButton").objectReferenceValue = pinButton;
        data.FindProperty("pinBackground").objectReferenceValue = pinBackground;
        data.FindProperty("pinIcon").objectReferenceValue = pinIcon;
        data.FindProperty("closeButton").objectReferenceValue = closeButton;
        data.FindProperty("graphSection").objectReferenceValue = graphSection.rectTransform;
        data.FindProperty("graphArea").objectReferenceValue = graphs;
        data.FindProperty("detailSection").objectReferenceValue = detailSection.rectTransform;
        data.FindProperty("detailText").objectReferenceValue = detailText;
        data.FindProperty("pointer").objectReferenceValue = pointer;
        data.FindProperty("linkAnchor").objectReferenceValue = link;
        data.FindProperty("frameInset").floatValue = Frame;
        data.ApplyModifiedPropertiesWithoutUndo();

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        return saved.GetComponent<TelemetryDetailPanel>();
    }

    private static Button CreateIconButton(
        string name,
        RectTransform header,
        float rightOffset,
        UIIconGraphic.Shape shape,
        Color color,
        Sprite hoverSprite,
        out UIIconGraphic icon,
        out Image hover)
    {
        Image hit = CreateImage(name, header, null, Clear);
        hit.raycastTarget = true;
        RectTransform rect = hit.rectTransform;
        rect.anchorMin = new Vector2(1f, 0.5f);
        rect.anchorMax = new Vector2(1f, 0.5f);
        rect.pivot = new Vector2(1f, 0.5f);
        rect.sizeDelta = new Vector2(HeaderButton, HeaderButton);
        rect.anchoredPosition = new Vector2(rightOffset, 0f);
        Button button = hit.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;

        hover = CreateHoverImage(rect, 4f);

        RectTransform iconRect = CreateUIObject("Icon", rect);
        // Close is a 14 px cross; the pin's shape sits inside its box, so it gets more room.
        Stretch(iconRect, shape == UIIconGraphic.Shape.Pin ? 7f : 9f);
        icon = iconRect.gameObject.AddComponent<UIIconGraphic>();
        icon.IconShape = shape;
        icon.color = color;
        icon.raycastTarget = false;
        return button;
    }

    /// <summary>
    /// A hidden hover backdrop for a header control, drawn under its label or
    /// icon. It has no sprite of its own: idle is nothing, and UIHoverSprite
    /// lays the hover sprite on while the control is hovered.
    /// </summary>
    private static Image CreateHoverImage(RectTransform control, float inset)
    {
        Image hover = CreateImage("Hover", control, null, Color.white);
        hover.type = Image.Type.Sliced;
        hover.pixelsPerUnitMultiplier = 3f; // the sprite's 16 px border at the size of a small control
        Stretch(hover.rectTransform, inset);
        hover.transform.SetAsFirstSibling();
        hover.enabled = false;
        return hover;
    }

    private static void AddHover(GameObject control, Image target, Sprite hover, Sprite idle, Graphic selectedIndicator)
    {
        if (!control.TryGetComponent(out UIHoverSprite component))
            component = control.AddComponent<UIHoverSprite>();
        SerializedObject data = new(component);
        data.FindProperty("target").objectReferenceValue = target;
        data.FindProperty("hoverSprite").objectReferenceValue = hover;
        data.FindProperty("idleSprite").objectReferenceValue = idle;
        data.FindProperty("selectedIndicator").objectReferenceValue = selectedIndicator;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    // -----------------------------------------------------------------------
    // Scene
    // -----------------------------------------------------------------------

    private static TelemetryHistoryRecorder ConfigureRecorder()
    {
        TelemetryRegistry registry = UnityEngine.Object.FindObjectsByType<TelemetryRegistry>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault() ?? throw new InvalidOperationException("Telemetry Registry not found.");

        if (!registry.TryGetComponent(out TelemetryHistoryRecorder recorder))
            recorder = registry.gameObject.AddComponent<TelemetryHistoryRecorder>();

        SerializedObject data = new(recorder);
        data.FindProperty("registry").objectReferenceValue = registry;
        data.ApplyModifiedPropertiesWithoutUndo();
        return recorder;
    }

    private static void ConfigureController(TelemetryDetailPanel prefab, TelemetryHistoryRecorder recorder)
    {
        WideStatRailManager rails = FindRailManager();
        RectTransform leftContainer = new SerializedObject(rails)
            .FindProperty("leftCardContainer").objectReferenceValue as RectTransform;
        RectTransform content = leftContainer != null ? leftContainer.parent?.parent as RectTransform : null;
        if (content == null)
            throw new InvalidOperationException("The wide layout's Content area was not found.");

        RectTransform layer = content.Find(LayerName) as RectTransform;
        if (layer == null)
            layer = CreateUIObject(LayerName, content);
        Stretch(layer, 0f);
        layer.pivot = new Vector2(0.5f, 0.5f);
        layer.SetAsLastSibling();
        if (!layer.TryGetComponent(out LayoutElement ignore))
            ignore = layer.gameObject.AddComponent<LayoutElement>();
        ignore.ignoreLayout = true; // the Content area lays out its rails; this layer floats above them

        // Panels draw above the leader lines, which sit in a later layer of the
        // layout; the raycaster keeps the panel buttons clickable.
        if (!layer.TryGetComponent(out Canvas panelCanvas))
            panelCanvas = layer.gameObject.AddComponent<Canvas>();
        panelCanvas.overrideSorting = true;
        panelCanvas.sortingOrder = 10;
        if (!layer.TryGetComponent(out GraphicRaycaster _))
            layer.gameObject.AddComponent<GraphicRaycaster>();

        if (!layer.TryGetComponent(out TelemetryDetailPanelController controller))
            controller = layer.gameObject.AddComponent<TelemetryDetailPanelController>();

        SerializedObject data = new(controller);
        data.FindProperty("railManager").objectReferenceValue = rails;
        data.FindProperty("panelPrefab").objectReferenceValue = prefab;
        data.FindProperty("historyProvider").objectReferenceValue = recorder;
        data.FindProperty("graphStyle.labelFont").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumFontPath);
        data.FindProperty("graphStyle.valueFont").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SemiBoldFontPath);
        data.FindProperty("graphStyle.surface").colorValue = Section;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureFocusReleaser()
    {
        EventSystem system = UnityEngine.Object.FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault() ?? throw new InvalidOperationException("EventSystem not found.");
        if (!system.TryGetComponent(out TelemetryCardFocusReleaser _))
            system.gameObject.AddComponent<TelemetryCardFocusReleaser>();
    }

    /// <summary>
    /// Gives every bottom control and pagination button its three looks: idle,
    /// the hover sprite, and the pressed or selected sprite, never two at once.
    /// </summary>
    private static int ConfigureControlHoverStates()
    {
        Sprite hover = LoadSprite(HoverSpritePath);
        int configured = 0;
        foreach (Button button in UnityEngine.Object.FindObjectsByType<Button>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!HasAncestor(button.transform, "Bottom controls") &&
                !HasAncestor(button.transform, "Telemetry Pagination Controls"))
                continue;
            if (button.targetGraphic is not Image image)
            {
                Debug.LogWarning($"TELEMETRY_DETAIL_PANEL_SETUP '{button.name}' has no target image; no hover state.", button);
                continue;
            }

            Sprite idle;
            ViewModeSegmentedControl segments = button.GetComponentInParent<ViewModeSegmentedControl>(true);
            if (segments != null)
            {
                SerializedObject control = new(segments);
                idle = control.FindProperty("inactiveSprite").objectReferenceValue as Sprite;
                // A pressed segment shows its selected look, as the other
                // controls show their pressed sprite.
                button.transition = Selectable.Transition.SpriteSwap;
                button.spriteState = new SpriteState
                {
                    pressedSprite = control.FindProperty("selectedSprite").objectReferenceValue as Sprite
                };
            }
            else
            {
                idle = image.sprite;
                // The pressed sprite no longer doubles as the hover.
                SpriteState state = button.spriteState;
                state.highlightedSprite = null;
                button.spriteState = state;
            }

            AddHover(button.gameObject, image, hover, idle, null);
            EditorUtility.SetDirty(button);
            configured++;
            Debug.Log($"TELEMETRY_DETAIL_PANEL_SETUP hover '{GetPath(button.transform)}' idle={(idle != null ? idle.name : "none")} " +
                      $"imageType={image.type} ppu={image.pixelsPerUnitMultiplier}");
        }
        return configured;
    }

    /// <summary>The wide layout's inner rails now hold the detail panel by default; their names say so.</summary>
    private static void RenameSecondaryRails()
    {
        SerializedObject data = new(FindRailManager());
        RenameRail(data.FindProperty("innerLeftCardContainer").objectReferenceValue as RectTransform, "Left");
        RenameRail(data.FindProperty("innerRightCardContainer").objectReferenceValue as RectTransform, "Right");
    }

    private static void RenameRail(RectTransform container, string side)
    {
        if (container == null)
            return;
        container.name = $"{side} secondary card container";
        if (container.parent != null)
            container.parent.name = $"{side} secondary rail";
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static WideStatRailManager FindRailManager() =>
        UnityEngine.Object.FindObjectsByType<WideStatRailManager>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault() ?? throw new InvalidOperationException("Wide Stat Rail Manager not found.");

    private static Sprite LoadSprite(string path) =>
        AssetDatabase.LoadAssetAtPath<Sprite>(path)
        ?? throw new InvalidOperationException($"Sprite missing: {path} (run Tools/Digital Twin/Organize and Rename Assets).");

    private static bool HasAncestor(Transform transform, string ancestorName)
    {
        for (; transform != null; transform = transform.parent)
        {
            if (transform.name == ancestorName)
                return true;
        }
        return false;
    }

    private static string GetPath(Transform transform) =>
        transform.parent == null ? transform.name : GetPath(transform.parent) + "/" + transform.name;

    private static RectTransform CreateUIObject(string name, Transform parent)
    {
        GameObject gameObject = new(name, typeof(RectTransform));
        gameObject.layer = LayerMask.NameToLayer("UI");
        gameObject.transform.SetParent(parent, false);
        return (RectTransform)gameObject.transform;
    }

    private static Image CreateImage(string name, RectTransform parent, Sprite sprite, Color color)
    {
        Image image = CreateUIObject(name, parent).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static TMP_Text CreateText(string name, RectTransform parent, string text, float size, Color color, TMP_FontAsset font)
    {
        TextMeshProUGUI label = CreateUIObject(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        if (font != null)
            label.font = font;
        label.fontSize = size;
        label.color = color;
        label.characterSpacing = 1.8f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.raycastTarget = false;
        return label;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void SetArray<T>(SerializedProperty property, IReadOnlyList<T> values) where T : UnityEngine.Object
    {
        property.arraySize = values.Count;
        for (int i = 0; i < values.Count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
