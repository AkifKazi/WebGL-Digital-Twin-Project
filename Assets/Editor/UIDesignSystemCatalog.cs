using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// The design system's catalog: a scene that shows every token, text style and
/// component in each of its states, built from the live assets, and a row of
/// working components to try in Play mode. Rebuild it after changing the
/// system; the image in the docs is rendered from it.
///
///   Tools > Digital Twin > UI Skin > Open Design System Catalog
///   batch: -executeMethod UIDesignSystemCatalog.BuildAndCapture
/// </summary>
public static class UIDesignSystemCatalog
{
    public const string ScenePath = UIDesignSystemDefaults.Folder + "/UI Design System Catalog.unity";
    public const string ImagePath = "Assets/Documentation/Images/UI Design System.png";
    private const int Width = 1920;
    private const int MaxHeight = 4200;
    private static readonly Color Backdrop = new(0.23f, 0.27f, 0.29f, 1f);

    [MenuItem("Tools/Digital Twin/UI Skin/Open Design System Catalog")]
    public static void Open()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        Build();
    }

    /// <summary>Rebuilds the catalog scene and renders it to the docs image (batch).</summary>
    public static void BuildAndCapture()
    {
        (Camera camera, RectTransform root) = Build();
        Capture(camera, root);
    }

    private static (Camera, RectTransform) Build()
    {
        UIDesignSystemDefaults.EnsureFolder(UIDesignSystemDefaults.Folder);
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Camera camera = new GameObject("Catalog Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Backdrop;
        camera.orthographic = true;

        Canvas canvas = new GameObject("Catalog", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster)).GetComponent<Canvas>();
        canvas.gameObject.layer = LayerMask.NameToLayer("UI");
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = camera;
        canvas.planeDistance = 5f;
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        RectTransform root = Group("Contents", canvas.transform, vertical: true, 40f, 48);
        root.anchorMin = root.anchorMax = root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = Vector2.zero;
        root.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        root.GetComponent<ContentSizeFitter>().horizontalFit = ContentSizeFitter.FitMode.PreferredSize;

        UITheme theme = UITheme.Active;
        Heading(root, "DIGITAL TWIN UI  ·  DESIGN SYSTEM",
            "Tokens, text styles and components, each in every state it defines. Built from Assets/UI/Design System.");
        Tokens(root, theme.tokens);
        TextStyles(root, theme.tokens);
        foreach (string group in new[]
                 {
                     UIDesignSystemDefaults.Containers, UIDesignSystemDefaults.Controls,
                     UIDesignSystemDefaults.Telemetry, UIDesignSystemDefaults.Marks
                 })
        {
            List<UIComponentStyle> members = theme.components
                .Where(c => c != null && c.description.StartsWith(group + "."))
                .ToList();
            if (members.Count == 0)
                continue;
            RectTransform section = Group(group, root, vertical: true, 16f, 0);
            Label(section, group.ToUpperInvariant(), "Label");
            foreach (UIComponentStyle component in members)
                ComponentRow(section, component);
        }
        LiveRow(root);

        EditorSceneManager.SaveScene(scene, ScenePath);
        return (camera, root);
    }

    // ---- sections ------------------------------------------------------------------

    private static void Heading(RectTransform root, string title, string subtitle)
    {
        RectTransform block = Group("Heading", root, vertical: true, 6f, 0);
        Label(block, title, "Title");
        Label(block, subtitle, "Body");
    }

    private static void Tokens(RectTransform root, UIDesignTokens tokens)
    {
        RectTransform block = Group("Colour Tokens", root, vertical: true, 12f, 0);
        Label(block, "COLOUR TOKENS", "Label");
        RectTransform row = Group("Swatches", block, vertical: false, 14f, 0);
        foreach (UIDesignTokens.ColorToken token in tokens.colors)
        {
            RectTransform cell = Group(token.name, row, vertical: true, 6f, 0);
            Image swatch = new GameObject("Swatch", typeof(RectTransform)).AddComponent<Image>();
            swatch.transform.SetParent(cell, false);
            swatch.color = token.color;
            Size(swatch.gameObject, 92f, 44f);
            Sized(Label(cell, token.name, "Caption"), 13f);
        }
        RectTransform spacing = Group("Spacing", block, vertical: false, 20f, 0);
        foreach (UIDesignTokens.SizeToken size in tokens.spacing)
            Label(spacing, $"{size.name} {size.value:0}", "Caption");
    }

    private static void TextStyles(RectTransform root, UIDesignTokens tokens)
    {
        RectTransform block = Group("Text Styles", root, vertical: true, 10f, 0);
        Label(block, "TEXT STYLES", "Label");
        RectTransform row = Group("Styles", block, vertical: false, 36f, 0);
        foreach (UIDesignTokens.TextStyle style in tokens.textStyles)
            Label(row, style.name, style.name);
    }

    private static readonly (string label, System.Func<UIComponentStyle, UIShapeStyle> get, UIState state)[] States =
    {
        ("Idle", c => c.idle, UIState.Idle),
        ("Hover", c => c.hover, UIState.Hover),
        ("Pressed", c => c.pressed, UIState.Pressed),
        ("Selected", c => c.selected, UIState.Selected),
        ("Selected hover", c => c.selectedHover, UIState.Selected),
        ("Disabled", c => c.disabled, UIState.Disabled),
        ("Warning", c => c.warning, UIState.Warning),
        ("Warning hover", c => c.warningHover, UIState.Warning),
        ("Critical", c => c.critical, UIState.Critical),
        ("Critical hover", c => c.criticalHover, UIState.Critical),
        ("Unavailable", c => c.unavailable, UIState.Unavailable)
    };

    private static void ComponentRow(RectTransform section, UIComponentStyle component)
    {
        RectTransform row = Group(component.name, section, vertical: false, 24f, 0);
        row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;

        RectTransform about = Group("About", row, vertical: true, 4f, 0);
        Size(about.gameObject, 300f, 0f);
        Sized(Label(about, component.name.ToUpperInvariant(), "Title"), 20f);
        TextMeshProUGUI description = Label(about, component.description.Substring(component.description.IndexOf('.') + 2), "Caption");
        Sized(description, 14f);
        description.textWrappingMode = TextWrappingModes.Normal;
        description.fontStyle &= ~FontStyles.UpperCase;
        Size(description.gameObject, 300f, 0f);

        foreach ((string label, System.Func<UIComponentStyle, UIShapeStyle> get, UIState state) in States)
        {
            UIShapeStyle style = get(component);
            if (style == null)
                continue;
            RectTransform cell = Group(label, row, vertical: true, 8f, 0);
            cell.GetComponent<VerticalLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            Sample(cell, component, style, state);
            Sized(Label(cell, label, "Caption"), 13f);
        }
    }

    /// <summary>One component drawn in one state's style, at a size that suits it.</summary>
    private static void Sample(RectTransform cell, UIComponentStyle component, UIShapeStyle style, UIState state)
    {
        GameObject frame = new("Sample", typeof(RectTransform));
        frame.transform.SetParent(cell, false);
        Vector2 size = component.name switch
        {
            "Leader Line" => new Vector2(140f, 2f),
            "Leader Anchor" => new Vector2(12f, 12f),
            "Accent Bar" => new Vector2(4f, 64f),
            "Underline" => new Vector2(40f, 2f),
            "Icon" => new Vector2(28f, 28f),
            "Icon Button" => new Vector2(44f, 44f),
            "Badge" => new Vector2(124f, 34f),
            "Segment" or "Button" => new Vector2(150f, 44f),
            "Card" => new Vector2(200f, 96f),
            "List Row" => new Vector2(224f, 72f),
            _ => new Vector2(180f, 88f)
        };
        // Marks are tinted by their owner; show them in the accent.
        bool mark = component.description.StartsWith(UIDesignSystemDefaults.Marks);
        Color tint = mark ? UIComponentFactory.ToneColor(UITone.Normal) : Color.white;
        UIShape shape = frame.AddComponent<UIShape>();
        shape.Style = style;
        shape.color = tint;
        shape.raycastTarget = false;
        if (component.name == "Icon")
        {
            shape.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Cross Section Icon.png");
            shape.color = UIComponentFactory.Token("Text");
        }
        Size(frame, size.x, size.y);

        string text = component.name switch
        {
            "Segment" or "Button" => "LABEL",
            "Badge" => state switch { UIState.Warning => "DUE SOON", UIState.Critical => "OVERDUE", UIState.Unavailable => "NO DATA", _ => "ON TRACK" },
            "Card" => "READING",
            "List Row" => "PART NAME",
            _ => null
        };
        if (text == null)
        {
            if (component.name == "Icon Button")
                Icon(frame.transform, "Close Icon");
            return;
        }
        TextMeshProUGUI content = UIComponentFactory.Text("Text", frame.transform, text,
            component.name is "Card" or "List Row" ? "Label" : "Button", keepColor: true);
        RectTransform rect = content.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(component.name is "Card" or "List Row" ? 18f : 0f, 0f);
        rect.offsetMax = Vector2.zero;
        content.alignment = component.name is "Card" or "List Row" ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center;
        content.color = state switch
        {
            UIState.Warning => UIComponentFactory.Token("Warning"),
            UIState.Critical => UIComponentFactory.Token("Critical"),
            UIState.Unavailable or UIState.Disabled => UIComponentFactory.Token("Neutral"),
            UIState.Selected => UIComponentFactory.Token("Accent"),
            _ => component.name is "Card" or "List Row" or "Badge" ? UIComponentFactory.Token("Label") : UIComponentFactory.Token("Text")
        };
    }

    private static void Icon(Transform parent, string sprite)
    {
        GameObject icon = new("Icon", typeof(RectTransform));
        icon.transform.SetParent(parent, false);
        UIShape shape = icon.AddComponent<UIShape>();
        shape.Component = UITheme.Active.Component("Icon");
        shape.sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/UI/Sprites/{sprite}.png");
        shape.color = UIComponentFactory.Token("Accent");
        shape.raycastTarget = false;
        ((RectTransform)icon.transform).sizeDelta = new Vector2(20f, 20f);
    }

    /// <summary>Working components, as GameObject > Digital Twin UI makes them: hover them in Play mode.</summary>
    private static void LiveRow(RectTransform root)
    {
        RectTransform block = Group("Live", root, vertical: true, 16f, 0);
        Label(block, "LIVE COMPONENTS  ·  PLAY THE SCENE AND HOVER THEM", "Label");
        RectTransform controls = Group("Controls", block, vertical: false, 24f, 0);
        controls.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleLeft;
        UIComponentFactory.SegmentedControl(controls, "TELEMETRY", "SERVICE");
        UIComponentFactory.Button(controls, "BUTTON");
        UIComponentFactory.Button(controls, "DISABLED", interactable: false);
        UIComponentFactory.IconButton(controls, AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Sprites/Pin Icon.png"));
        UIComponentFactory.Badge(controls, "ON TRACK", UITone.Normal);
        UIComponentFactory.Badge(controls, "DUE SOON", UITone.Warning);
        UIComponentFactory.Badge(controls, "OVERDUE", UITone.Critical);

        RectTransform cards = Group("Cards", block, vertical: false, 16f, 0);
        UIComponentFactory.Card(cards, "MOTOR LOAD", "79.7 %", UITone.Normal);
        UIComponentFactory.Card(cards, "BELT SPEED", "0.8 M/S", UITone.Warning);
        UIComponentFactory.Card(cards, "BELT SLIP", "1.3 %", UITone.Critical);
        UIComponentFactory.Card(cards, "MOTOR SPEED", "— RPM", UITone.Unavailable);

        RectTransform rows = Group("Rows", block, vertical: false, 16f, 0);
        UIComponentFactory.ListRow(rows, "1", "BEARINGS", "REGREASE", "160 H", UITone.Warning);
        UIComponentFactory.ListRow(rows, "2", "SCREEN DECK", "REPLACE", "40 H", UITone.Critical);
        UIComponentFactory.ListRow(rows, "3", "DRIVE ROLLER", "REGREASE", "740 H", UITone.Normal);
    }

    // ---- layout helpers -----------------------------------------------------------

    private static RectTransform Group(string name, Transform parent, bool vertical, float spacing, int padding)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        HorizontalOrVerticalLayoutGroup group = vertical
            ? go.AddComponent<VerticalLayoutGroup>()
            : go.AddComponent<HorizontalLayoutGroup>();
        group.spacing = spacing;
        group.padding = new RectOffset(padding, padding, padding, padding);
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        return (RectTransform)go.transform;
    }

    private static TextMeshProUGUI Label(Transform parent, string text, string style) =>
        UIComponentFactory.Text(style, parent, text, style);

    /// <summary>A catalog label at its own size: it keeps the style's font and colour, not its size.</summary>
    private static TextMeshProUGUI Sized(TextMeshProUGUI text, float size)
    {
        Object.DestroyImmediate(text.GetComponent<UITextStyle>());
        text.fontSize = size;
        return text;
    }

    private static void Size(GameObject go, float width, float height)
    {
        LayoutElement element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        if (width > 0f)
            element.preferredWidth = element.minWidth = width;
        if (height > 0f)
            element.preferredHeight = element.minHeight = height;
    }

    private static void Capture(Camera camera, RectTransform root)
    {
        var target = new RenderTexture(Width, MaxHeight, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        Canvas.ForceUpdateCanvases();
        int height = Mathf.Clamp(Mathf.CeilToInt(LayoutUtility.GetPreferredHeight(root)), 200, MaxHeight);
        camera.Render();

        RenderTexture.active = target;
        var image = new Texture2D(Width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, MaxHeight - height, Width, height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;

        Directory.CreateDirectory(Path.GetDirectoryName(ImagePath)!);
        byte[] png = image.EncodeToPNG();
        File.WriteAllBytes(ImagePath, png);
        Directory.CreateDirectory("Logs/ProceduralUISkin");
        File.WriteAllBytes("Logs/ProceduralUISkin/00_design_system.png", png);
        Object.DestroyImmediate(image);
        AssetDatabase.ImportAsset(ImagePath);
        Debug.Log($"UI_DESIGN_SYSTEM catalog saved: {ScenePath}, {ImagePath} ({Width}x{height})");
    }
}
