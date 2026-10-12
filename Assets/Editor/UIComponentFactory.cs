using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the design system's components ready to use, from
/// GameObject > Digital Twin UI: each with its shape, its state driver
/// (UIControlState), auto layout (layout groups and content size fitters, so
/// it sizes to its content like auto layout in a design tool), spacing from
/// the tokens and text in the tokens' text styles. Change the label and it
/// resizes; change a token and every one of them follows.
/// </summary>
public static class UIComponentFactory
{
    private const string Menu = "GameObject/Digital Twin UI/";
    private const int Priority = 10;
    private const string SpriteFolder = "Assets/UI/Sprites/";

    [MenuItem(Menu + "Panel", false, Priority)]
    private static void MenuPanel(MenuCommand command) => Place(command, Panel);

    [MenuItem(Menu + "Section", false, Priority)]
    private static void MenuSection(MenuCommand command) => Place(command, p => Section(p, "SECTION"));

    [MenuItem(Menu + "Button", false, Priority + 20)]
    private static void MenuButton(MenuCommand command) => Place(command, p => Button(p, "BUTTON"));

    [MenuItem(Menu + "Icon Button", false, Priority + 20)]
    private static void MenuIconButton(MenuCommand command) =>
        Place(command, p => IconButton(p, Load<Sprite>(SpriteFolder + "Close Icon.png")));

    [MenuItem(Menu + "Segmented Control", false, Priority + 20)]
    private static void MenuSegmented(MenuCommand command) =>
        Place(command, p => SegmentedControl(p, "FIRST", "SECOND", "THIRD"));

    [MenuItem(Menu + "Badge", false, Priority + 20)]
    private static void MenuBadge(MenuCommand command) => Place(command, p => Badge(p, "ON TRACK", UITone.Normal));

    [MenuItem(Menu + "Card", false, Priority + 40)]
    private static void MenuCard(MenuCommand command) => Place(command, p => Card(p, "READING", "42.0 UNIT", UITone.Normal));

    [MenuItem(Menu + "List Row", false, Priority + 40)]
    private static void MenuRow(MenuCommand command) =>
        Place(command, p => ListRow(p, "1", "PART NAME", "TASK", "120 H", UITone.Normal));

    // ---- builders ----------------------------------------------------------------

    /// <summary>A frame for a group of controls. Children stack vertically.</summary>
    public static GameObject Panel(Transform parent)
    {
        GameObject panel = Shape("Panel", parent, "Panel");
        Stack(panel, vertical: true, Space("L"), Space("S"));
        return panel;
    }

    /// <summary>A block of related content with a heading.</summary>
    public static GameObject Section(Transform parent, string heading)
    {
        GameObject section = Shape("Section", parent, "Section");
        Stack(section, vertical: true, Space("XL"), Space("M"));
        Text("Heading", section.transform, heading, "Label");
        return section;
    }

    public static GameObject Button(Transform parent, string label, bool interactable = true)
    {
        GameObject button = Shape(label, parent, "Button", raycast: true);
        Control(button, interactable);
        HorizontalOrVerticalLayoutGroup row = Stack(button, vertical: false, Space("L"), Space("S"));
        row.padding = new RectOffset((int)Space("XL"), (int)Space("XL"), (int)Space("M"), (int)Space("M"));
        row.childAlignment = TextAnchor.MiddleCenter;
        MinSize(button, 0f, 44f);
        Text("Label", button.transform, label, "Button").alignment = TextAlignmentOptions.Center;
        return button;
    }

    public static GameObject IconButton(Transform parent, Sprite icon)
    {
        GameObject button = Shape(icon != null ? icon.name : "Icon Button", parent, "Icon Button", raycast: true);
        Control(button, true);
        MinSize(button, 44f, 44f);
        ((RectTransform)button.transform).sizeDelta = new Vector2(44f, 44f);
        GameObject image = Shape("Icon", button.transform, "Icon");
        image.GetComponent<UIShape>().sprite = icon;
        image.GetComponent<UIShape>().color = Token("Accent");
        RectTransform rect = (RectTransform)image.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(20f, 20f);
        return button;
    }

    /// <summary>Choices in a frame, one selected at a time (a ToggleGroup), like Telemetry | Service.</summary>
    public static GameObject SegmentedControl(Transform parent, params string[] labels)
    {
        GameObject control = Shape("Segmented Control", parent, "Panel");
        Stack(control, vertical: false, Space("XS"), 0f);
        ToggleGroup group = control.AddComponent<ToggleGroup>();
        for (int i = 0; i < labels.Length; i++)
        {
            GameObject segment = Shape(labels[i], control.transform, "Segment", raycast: true);
            Toggle toggle = segment.AddComponent<Toggle>();
            toggle.transition = Selectable.Transition.None;
            toggle.targetGraphic = segment.GetComponent<UIShape>();
            toggle.group = group;
            toggle.isOn = i == 0;
            segment.AddComponent<UIControlState>();
            HorizontalOrVerticalLayoutGroup inner = Stack(segment, vertical: false, Space("S"), 0f);
            inner.padding = new RectOffset((int)Space("XL"), (int)Space("XL"), (int)Space("S"), (int)Space("S"));
            inner.childAlignment = TextAnchor.MiddleCenter;
            MinSize(segment, 120f, 44f);
            Text("Label", segment.transform, labels[i], "Button").alignment = TextAlignmentOptions.Center;
        }
        return control;
    }

    public static GameObject Badge(Transform parent, string label, UITone tone)
    {
        GameObject badge = Shape(label, parent, "Badge");
        badge.GetComponent<UIShape>().State = StateOf(tone);
        HorizontalOrVerticalLayoutGroup row = Stack(badge, vertical: false, 0f, 0f);
        row.padding = new RectOffset((int)Space("M"), (int)Space("M"), (int)Space("XS"), (int)Space("XS"));
        row.childAlignment = TextAnchor.MiddleCenter;
        TextMeshProUGUI text = Text("Label", badge.transform, label, "Button", keepColor: true);
        text.color = ToneColor(tone);
        return badge;
    }

    /// <summary>A reading: a label in the tone's colour over its value.</summary>
    public static GameObject Card(Transform parent, string label, string value, UITone tone)
    {
        GameObject card = Shape(label, parent, "Card", raycast: true);
        Control(card, true).Tone = tone;
        HorizontalOrVerticalLayoutGroup stack = Stack(card, vertical: true, Space("XL"), Space("S"));
        stack.padding = new RectOffset((int)Space("XL"), (int)Space("XL"), (int)Space("L"), (int)Space("L"));
        MinSize(card, 240f, 0f);
        Text("Label", card.transform, label, "Label", keepColor: true).color = ToneColor(tone);
        Text("Value", card.transform, value, "Value");
        return card;
    }

    /// <summary>A list entry: number, title over subtitle, and a value on the right.</summary>
    public static GameObject ListRow(Transform parent, string number, string title, string subtitle, string value, UITone tone)
    {
        GameObject row = Shape(title, parent, "List Row", raycast: true);
        Control(row, true).Tone = tone;
        HorizontalOrVerticalLayoutGroup line = Stack(row, vertical: false, Space("XL"), Space("L"));
        line.padding = new RectOffset((int)Space("XL"), (int)Space("XL"), (int)Space("L"), (int)Space("L"));
        line.childAlignment = TextAnchor.MiddleLeft;
        MinSize(row, 420f, 88f);

        Text("Number", row.transform, number, "Value", keepColor: true).color = ToneColor(tone);
        GameObject names = new("Names", typeof(RectTransform));
        names.transform.SetParent(row.transform, false);
        Stack(names, vertical: true, 0f, Space("XS")).childAlignment = TextAnchor.MiddleLeft;
        names.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Text("Title", names.transform, title, "Title");
        Text("Subtitle", names.transform, subtitle, "Caption");
        TextMeshProUGUI hours = Text("Value", row.transform, value, "Value", keepColor: true);
        hours.color = tone == UITone.Normal ? Token("Text") : ToneColor(tone);
        hours.alignment = TextAlignmentOptions.MidlineRight;
        return row;
    }

    // ---- parts ---------------------------------------------------------------------

    private static GameObject Shape(string name, Transform parent, string component, bool raycast = false)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.layer = parent != null ? parent.gameObject.layer : LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        UIShape shape = go.AddComponent<UIShape>();
        shape.Component = UITheme.Active != null ? UITheme.Active.Component(component) : null;
        shape.raycastTarget = raycast;
        return go;
    }

    private static UIControlState Control(GameObject go, bool interactable)
    {
        var button = go.AddComponent<UnityEngine.UI.Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = go.GetComponent<UIShape>();
        button.interactable = interactable;
        return go.AddComponent<UIControlState>();
    }

    private static HorizontalOrVerticalLayoutGroup Stack(GameObject go, bool vertical, float padding, float spacing)
    {
        HorizontalOrVerticalLayoutGroup group = vertical
            ? go.AddComponent<VerticalLayoutGroup>()
            : go.AddComponent<HorizontalLayoutGroup>();
        int pad = Mathf.RoundToInt(padding);
        group.padding = new RectOffset(pad, pad, pad, pad);
        group.spacing = spacing;
        group.childControlWidth = true;
        group.childControlHeight = true;
        group.childForceExpandWidth = false;
        group.childForceExpandHeight = false;
        // Sized by its content, unless a layout group above it does the sizing:
        // a fitter there would fight the group and leave gaps.
        Transform parent = go.transform.parent;
        if (parent == null || parent.GetComponent<LayoutGroup>() == null)
        {
            ContentSizeFitter fit = go.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
        return group;
    }

    private static void MinSize(GameObject go, float width, float height)
    {
        LayoutElement element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        element.minWidth = width;
        element.minHeight = height;
    }

    public static TextMeshProUGUI Text(string name, Transform parent, string content, string style, bool keepColor = false)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        UITextStyle textStyle = go.AddComponent<UITextStyle>();
        var serialized = new SerializedObject(textStyle);
        serialized.FindProperty("textStyle").stringValue = style;
        serialized.FindProperty("keepColor").boolValue = keepColor;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        textStyle.Apply();
        return text;
    }

    public static float Space(string token) =>
        UITheme.Active != null && UITheme.Active.tokens != null ? UITheme.Active.tokens.Space(token, 8f) : 8f;

    public static Color Token(string name, float alpha = 1f) => UIColor.Token(name, alpha).Resolve();

    public static Color ToneColor(UITone tone) => tone switch
    {
        UITone.Warning => Token("Warning"),
        UITone.Critical => Token("Critical"),
        UITone.Unavailable => Token("Neutral"),
        _ => Token("Label")
    };

    public static UIState StateOf(UITone tone) => tone switch
    {
        UITone.Warning => UIState.Warning,
        UITone.Critical => UIState.Critical,
        UITone.Unavailable => UIState.Unavailable,
        _ => UIState.Idle
    };

    private static T Load<T>(string path) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(path);

    /// <summary>Creates the component under the selected object, or on a canvas, with undo.</summary>
    private static void Place(MenuCommand command, Func<Transform, GameObject> build)
    {
        Transform parent = (command.context as GameObject)?.transform ?? Selection.activeTransform;
        if (parent == null || parent.GetComponentInParent<Canvas>() == null)
        {
            Canvas canvas = UnityEngine.Object.FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                    typeof(GraphicRaycaster)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Undo.RegisterCreatedObjectUndo(canvas.gameObject, "Create Canvas");
            }
            parent = canvas.transform;
        }
        GameObject created = build(parent);
        Undo.RegisterCreatedObjectUndo(created, "Create " + created.name);
        Selection.activeGameObject = created;
    }
}
