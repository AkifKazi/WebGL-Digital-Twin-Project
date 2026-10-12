using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The design system's starting point, written out as assets the team then
/// edits in the Inspector:
///
///   Assets/UI/Design System/UI Tokens.asset    colours, spacing, text styles, motion
///   Assets/UI/Design System/Components/        one folder per component: the
///                                              component and a style per state
///   Assets/Resources/UI Theme.asset            the components, and the sprite each
///                                              sprite-authored role stands for
///
/// Run with reset off, only missing assets are created; edits are kept. Reset
/// rewrites every token, style and component from the values below.
/// </summary>
public static class UIDesignSystemDefaults
{
    public const string Folder = "Assets/UI/Design System";
    public const string ComponentFolder = Folder + "/Components";
    public const string TokensPath = Folder + "/UI Tokens.asset";
    public const string ThemePath = "Assets/Resources/" + UITheme.ResourceName + ".asset";
    private const string MaterialPath = "Assets/Materials/UI/UI Shape.mat";
    private const string ShaderName = "UI/Digital Twin/Shape";
    private const string SpriteFolder = "Assets/UI/Sprites/";
    private const string FontFolder = "Assets/UI/Fonts/";
    private const string MediumFont = FontFolder + "Rajdhani-Medium SDF.asset";
    private const string SemiBoldFont = FontFolder + "Rajdhani-SemiBold SDF.asset";
    public const string CardVariant = "Card";

    // Component groups, as the catalog and the docs list them.
    public const string Containers = "Containers";
    public const string Controls = "Controls";
    public const string Telemetry = "Telemetry";
    public const string Marks = "Marks";
    public const string Parts = "Parts";

    private static bool reset;
    private static readonly Dictionary<string, UIShapeStyle> Styles = new();
    private static readonly List<UIComponentStyle> Components = new();

    /// <summary>Creates or updates every design-system asset and returns the theme.</summary>
    public static UITheme Build(bool resetToDefaults)
    {
        reset = resetToDefaults;
        Styles.Clear();
        Components.Clear();
        EnsureFolder(ComponentFolder);
        EnsureFolder("Assets/Resources");

        UITheme theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
        if (theme == null)
        {
            theme = ScriptableObject.CreateInstance<UITheme>();
            AssetDatabase.CreateAsset(theme, ThemePath);
        }
        theme.shapeMaterial = ShapeMaterial();
        theme.tokens = Tokens();
        // Styles resolve their token colours through the active theme.
        EditorUtility.SetDirty(theme);

        DefineComponents();
        RemoveStale();
        theme.components = Components.ToList();
        theme.styles = SpriteRoles();
        theme.fonts = LegibleFonts();
        EditorUtility.SetDirty(theme);
        AssetDatabase.SaveAssets();
        return theme;
    }

    // ---- tokens ---------------------------------------------------------------

    private static UIDesignTokens Tokens()
    {
        UIDesignTokens tokens = AssetDatabase.LoadAssetAtPath<UIDesignTokens>(TokensPath);
        bool created = tokens == null;
        if (created)
        {
            tokens = ScriptableObject.CreateInstance<UIDesignTokens>();
            AssetDatabase.CreateAsset(tokens, TokensPath);
        }
        if (!created && !reset)
            return tokens;

        tokens.colors = new List<UIDesignTokens.ColorToken>
        {
            C("Accent", "36EDFF", "Interactive edges, selected text, normal readings."),
            C("Accent Bright", "8CF6FF", "The edge of what is selected or pressed."),
            C("Accent Deep", "16596A", "The fill of what is selected, hovered or pressed."),
            C("Warning", "FFB31A", "Readings and services that need attention soon."),
            C("Critical", "FF3833", "Readings and services that need attention now."),
            C("Neutral", "73808C", "Unavailable or disabled."),
            C("Surface", "04121B", "Panels over the scene."),
            C("Surface Raised", "061722", "Cards, rows and sections on a panel."),
            C("Surface Hover", "0C2A38", "A hovered card or row."),
            C("Surface Inset", "02070A", "Wells for graphs, detail text and list rows: near black, so content reads on it."),
            C("Accent Surface", "062A36", "The focused card."),
            C("Warning Surface", "251804", "A warning card or row."),
            C("Critical Surface", "2A0606", "A critical card or row."),
            C("Unavailable Surface", "10161A", "An unavailable card."),
            C("Shadow", "000608", "Shadows that keep edges and text readable on bright scenes."),
            C("Text", "EAFDFF", "Values and primary text."),
            C("Label", "00EBFF", "Section labels and headings."),
            C("White", "FFFFFF", "Marks tinted by their owner: lines, bars, icons.")
        };
        tokens.spacing = new List<UIDesignTokens.SizeToken>
        {
            S("XS", 4f, "Inside a segmented control; icon to its label."),
            S("S", 8f, "Between related items."),
            S("M", 12f, "Between cards and rows."),
            S("L", 16f, "Padding inside a control."),
            S("XL", 24f, "Padding inside a card or section."),
            S("XXL", 32f, "Between groups; a panel's margin.")
        };
        TMP_FontAsset medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MediumFont);
        TMP_FontAsset semiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SemiBoldFont);
        tokens.textStyles = new List<UIDesignTokens.TextStyle>
        {
            T("Display", semiBold, 40f, 1f, false, "Text", 1f, "The headline figure: hours to service, a reading."),
            T("Value", semiBold, 32f, 1f, false, "Text", 1f, "A reading on a card."),
            T("Title", semiBold, 24f, 3f, true, "Text", 1f, "A card's or part's name."),
            T("Label", medium, 20f, 6f, true, "Label", 1f, "Section labels and headings."),
            T("Button", medium, 20f, 2f, true, "Text", 1f, "Text on a button or segment."),
            T("Body", medium, 20f, 1f, false, "Text", 1f, "Sentences: causes, fixes, notes."),
            T("Caption", medium, 17f, 3f, true, "Text", 0.62f, "Secondary labels and units.")
        };
        tokens.fastMotion = 0.14f;
        tokens.normalMotion = 0.2f;
        EditorUtility.SetDirty(tokens);
        return tokens;
    }

    // ---- components -------------------------------------------------------------

    /// <summary>Every component and the style of each of its states. Sizes are canvas units at 1920 x 1080.</summary>
    private static void DefineComponents()
    {
        // Containers ---------------------------------------------------------
        // Dark glass with a faint edge and its bottom edge lit, as the earlier
        // panel sprite had it.
        Component("Panel", Containers, "A group of controls over the scene, such as a segmented control's frame.",
            c => c.idle = Style("Panel", "Idle", s =>
            {
                Glass(s, "Surface", 0.62f, 0.75f, 0.2f);
                s.glowSize = 10f;
                s.glowStrength = 0.05f;
                s.bottomLight = 0.35f;
                s.bottomLightHeight = 0.22f;
                Shadow(s, 0.35f, -2f, 6f);
            }));
        Component("Sheet", Containers, "A full-height side panel, near opaque so the model does not show through.",
            c => c.idle = Style("Sheet", "Idle", s =>
            {
                Glass(s, "Surface", 0.92f, 0.85f, 0.3f);
                s.cornerRadius = 0f;
                s.glowSize = 10f;
                s.glowStrength = 0.04f;
                s.bottomLight = 0.25f;
                s.bottomLightHeight = 0.04f;
                Shadow(s, 0.4f, -2f, 10f);
            }));
        Component("Section", Containers, "A block of related content on a panel: a summary, a chart, a table.",
            c => c.idle = Style("Section", "Idle", Well));
        Component("Detail Panel", Containers, "A popover anchored to what it describes, such as a card's history.",
            c => c.idle = Style("Detail Panel", "Idle", s =>
            {
                Glass(s, "Surface", 0.9f, 0.85f, 0.42f);
                s.glowSize = 12f;
                s.glowStrength = 0.1f;
                s.innerGlow = 0.06f;
                s.bottomLight = 0.25f;
                s.bottomLightHeight = 0.08f;
                Shadow(s, 0.45f, -3f, 12f);
            }));
        Component("Inset", Containers, "A recessed well inside a panel, for a graph or detail text.",
            c => c.idle = Style("Inset", "Idle", Well));

        // Controls -----------------------------------------------------------
        // Hover turns on a faint light at the control's bottom edge; selected
        // takes the bright, overexposed edge and a glow. The two never look alike.
        UIShapeStyle selected = null;
        Component("Segment", Controls, "One choice in a segmented control or tab bar. Idle shows the frame behind it.", c =>
        {
            c.idle = Style("Segment", "Idle", s =>
            {
                s.fill = UIColor.Token("Accent Deep", 0f);
                s.stroke = UIColor.Token("Accent", 0f);
                NoGlow(s);
                s.bottomLightHeight = 0f;
                NoShadow(s);
            });
            c.hover = Style("Segment", "Hover", s => Lit(s, 0.1f, 0.16f, 0.28f, 0.55f));
            c.pressed = Style("Segment", "Pressed", s =>
            {
                Lit(s, 0.4f, 0.6f, 0.8f, 0.9f);
                s.stroke = UIColor.Token("Accent Bright", 0.7f);
                s.highlight = 0.3f;
                s.transition = 0.08f;
            });
            c.selected = selected = Style("Segment", "Selected", s =>
            {
                s.fill = UIColor.Token("Accent Deep", 0.45f);
                s.gradientEnd = 0.7f;
                s.stroke = UIColor.Token("Accent Bright", 0.95f);
                s.strokeWidth = 1.5f;
                s.glow = UIColor.Token("Accent");
                s.glowSize = 18f;
                s.glowStrength = 0.3f;
                s.innerGlow = 0.14f;
                s.highlight = 0.6f;
                s.bottomLight = 0.4f;
                s.bottomLightHeight = 0.5f;
                Shadow(s, 0.3f, -1f, 2f);
                s.transition = 0.16f;
            });
        });
        Component("Button", Controls, "A standalone action. Visible on its own, unlike a segment.", c =>
        {
            c.idle = Style("Button", "Idle", s =>
            {
                Glass(s, "Surface Raised", 0.7f, 0.8f, 0.25f);
                s.innerGlow = 0.04f;
                s.bottomLight = 0.2f;
                s.bottomLightHeight = 0.2f;
                Shadow(s, 0.3f, -1f, 3f);
            });
            c.hover = Style("Button", "Hover", s =>
            {
                Lit(s, 0.2f, 0.36f, 0.3f, 0.55f);
                s.fill = UIColor.Token("Surface Hover", 0.82f);
                Shadow(s, 0.3f, -1f, 3f);
            });
            c.pressed = Style("Button", "Pressed", s =>
            {
                Lit(s, 0.5f, 0.8f, 0.9f, 0.9f);
                s.stroke = UIColor.Token("Accent Bright", 0.8f);
                s.highlight = 0.3f;
                s.transition = 0.08f;
            });
            c.selected = selected;
            c.disabled = Style("Button", "Disabled", s =>
            {
                Glass(s, "Surface Raised", 0.4f, 1f, 0f);
                s.stroke = UIColor.Token("Neutral", 0.25f);
                NoShadow(s);
            });
        });
        Component("Icon Button", Controls, "An icon on its own: back, close, pin. Only hover shows a frame.", c =>
        {
            c.idle = Style("Icon Button", "Idle", s =>
            {
                s.fill = UIColor.Token("Accent Deep", 0f);
                s.stroke = UIColor.Token("Accent", 0f);
                NoGlow(s);
                s.bottomLightHeight = 0f;
                NoShadow(s);
                s.cornerRadius = 2f;
            });
            c.hover = Style("Icon Button", "Hover", s =>
            {
                Lit(s, 0.1f, 0.16f, 0.3f, 0.6f);
                s.cornerRadius = 2f;
            });
            c.pressed = Style("Icon Button", "Pressed", s =>
            {
                Lit(s, 0.35f, 0.5f, 0.8f, 0.9f);
                s.stroke = UIColor.Token("Accent Bright", 0.5f);
                s.cornerRadius = 2f;
                s.transition = 0.08f;
            });
        });
        Component("Icon", Controls, "An authored icon sprite, tinted by its control. Glows faintly while hovered.", c =>
        {
            c.idle = Style("Icon", "Idle", s => Icon(s, 0f, 1f));
            c.hover = Style("Icon", "Hover", s => Icon(s, 0.1f, 1f));
            c.disabled = Style("Icon", "Disabled", s => Icon(s, 0f, 0.35f));
        });
        Component("Focus Ring", Controls, "Drawn round whatever the keyboard has reached, a gap outside it. Never shown for the pointer.",
            c => c.idle = Style("Focus Ring", "Idle", s =>
            {
                s.fill = UIColor.Token("Accent", 0f);
                s.cornerRadius = 3f;
                s.stroke = UIColor.Token("Text");
                s.strokeWidth = 2f;
                s.glow = UIColor.Token("Accent");
                s.glowSize = 12f;
                s.glowStrength = 0.35f;
                s.innerGlow = 0f;
                s.highlight = 0.5f;
                s.bottomLightHeight = 0f;
                Shadow(s, 0.5f, -1f, 2f);
                s.transition = 0.1f;
                s.fadeIn = true;
            }));
        Component("Badge", Controls, "A short status label, such as ON TRACK: text in the state's colour, not a control.", c =>
        {
            c.idle = Style("Badge", "Idle", s => Badge(s, "Accent"));
            c.warning = Style("Badge", "Warning", s => Badge(s, "Warning"));
            c.critical = Style("Badge", "Critical", s => Badge(s, "Critical"));
            c.unavailable = Style("Badge", "Unavailable", s => Badge(s, "Neutral"));
        });

        // Telemetry ----------------------------------------------------------
        Component("Card", Telemetry, "A sensor reading. Alarm states breathe gently and still answer the pointer.", c =>
        {
            c.idle = Style("Card", "Idle", s =>
            {
                Card(s, "Surface Raised", 0.74f, "Accent", 0.3f, 0.03f);
                s.glowSize = 10f;
            });
            c.hover = Style("Card", "Hover", s =>
            {
                Card(s, "Surface Hover", 0.76f, "Accent", 0.55f, 0.06f);
                s.innerGlow = 0.07f;
                s.bottomLight = 0.22f;
                s.bottomLightHeight = 0.4f;
                s.transition = 0.18f;
                s.fadeIn = true;
            });
            // The focused card: lit in the accent the way an alarm card is lit in its tone.
            c.selected = Style("Card", "Selected", s =>
            {
                Alarm(s, "Accent Surface", 0.82f, 0.95f, 0.34f, 0.5f, 0f, 0.5f, "Accent");
                s.innerGlow = 0.16f;
                s.bottomLightHeight = 0.55f;
            });
            c.selectedHover = Style("Card", "Selected Hover", s =>
            {
                Alarm(s, "Accent Surface", 0.86f, 1f, 0.4f, 0.62f, 0f, 0.5f, "Accent");
                s.innerGlow = 0.2f;
                s.bottomLightHeight = 0.62f;
                s.transition = 0.14f;
            });
            c.warning = Style("Card", "Warning", s => Alarm(s, "Warning Surface", 0.78f, 0.85f, 0.2f, 0.25f, 0.12f, 0.5f, "Warning"));
            c.warningHover = Style("Card", "Warning Hover", s => AlarmHover(s, "Warning", 0.84f, 1f, 0.26f));
            c.critical = Style("Card", "Critical", s => Alarm(s, "Critical Surface", 0.8f, 0.95f, 0.28f, 0.35f, 0.25f, 0.9f, "Critical"));
            c.criticalHover = Style("Card", "Critical Hover", s => AlarmHover(s, "Critical", 0.86f, 1f, 0.34f));
            c.unavailable = Style("Card", "Unavailable", s =>
            {
                Card(s, "Unavailable Surface", 0.74f, "Neutral", 0.45f, 0f);
                s.innerGlow = 0f;
                s.hatch = 0.22f;
            });
        });
        Component("List Row", Telemetry, "A row in a list, such as a part in the service schedule. Opens more on click.", c =>
        {
            // At rest, the same near-black well as the telemetry detail (alarm rows in
            // their tone, as opaque); only hover lights it.
            c.idle = Style("List Row", "Idle", s =>
            {
                Well(s);
                s.transition = 0.14f;
            });
            c.hover = Style("List Row", "Hover", s =>
            {
                Glass(s, "Surface Hover", 0.8f, 0.8f, 0.35f);
                s.glowSize = 8f;
                s.glowStrength = 0.04f;
                s.innerGlow = 0.05f;
                s.bottomLight = 0.24f;
                s.bottomLightHeight = 0.45f;
                NoShadow(s);
                s.transition = 0.14f;
            });
            c.warning = Style("List Row", "Warning", s => Row(s, "Warning", 0.85f, 0.6f, 0f));
            c.warningHover = Style("List Row", "Warning Hover", s => Row(s, "Warning", 0.88f, 0.85f, 0.24f));
            c.critical = Style("List Row", "Critical", s => Row(s, "Critical", 0.85f, 0.65f, 0f));
            c.criticalHover = Style("List Row", "Critical Hover", s => Row(s, "Critical", 0.88f, 0.9f, 0.24f));
        });

        // Marks: tinted by their owner, a faint glow and a sharp shadow ----------
        Component("Leader Line", Marks, "The line from a card to its sensor.", c => c.idle = Style("Leader Line", "Idle", s => Mark(s, 5f, 0.3f)));
        Component("Leader Anchor", Marks, "The square on the sensor end of a leader line.", c => c.idle = Style("Leader Anchor", "Idle", s => Mark(s, 6f, 0.35f)));
        Component("Accent Bar", Marks, "A card's connection bar, where its leader line meets it.", c => c.idle = Style("Accent Bar", "Idle", s =>
        {
            Mark(s, 10f, 0.3f);
            s.highlight = 0.35f;
            s.cornerRadius = 1f;
            Shadow(s, 0.5f, -1f, 0f, 1f);
        }));
        Component("Underline", Marks, "Marks the chosen tab in an underlined tab bar.", c => c.idle = Style("Underline", "Idle", s =>
        {
            Mark(s, 6f, 0.3f);
            s.cornerRadius = 1f;
            NoShadow(s);
        }));
        foreach (UIGradientStart start in Enum.GetValues(typeof(UIGradientStart)))
        {
            Component($"Accent Trail {start}", Parts, "The fade behind a card's connection bar.", c => c.idle = Style($"Accent Trail {start}", "Idle", s =>
            {
                s.fill = UIColor.Token("White", 0.12f);
                s.gradientStart = start;
                s.gradientEnd = 0f;
                s.stroke = UIColor.None;
                NoGlow(s);
                NoShadow(s);
            }));
        }
    }

    // ---- look recipes, shared so components stay consistent --------------------

    /// <summary>
    /// A near-black, slightly see-through well: the telemetry detail's inner
    /// panel, and the service schedule's rows and sections. Barely an edge, no
    /// glow and no light; content sits in it rather than on it.
    /// </summary>
    private static void Well(UIShapeStyle s)
    {
        s.fill = UIColor.Token("Surface Inset", 0.85f);
        s.cornerRadius = 2f;
        s.stroke = UIColor.Token("Accent", 0.04f);
        s.strokeWidth = 1f;
        s.glow = UIColor.Token("Accent");
        NoGlow(s);
        s.innerGlow = 0f;
        s.bottomLightHeight = 0f;
        NoShadow(s);
    }

    private static void Glass(UIShapeStyle s, string surface, float alpha, float gradientEnd, float edge)
    {
        s.fill = UIColor.Token(surface, alpha);
        s.gradientStart = UIGradientStart.Top;
        s.gradientEnd = gradientEnd;
        s.cornerRadius = 2f;
        s.stroke = UIColor.Token("Accent", edge);
        s.strokeWidth = 1f;
        s.glow = UIColor.Token("Accent");
        s.glowStrength = 0f;
        s.innerGlow = 0.05f;
        s.bottomLightHeight = 0f;
    }

    /// <summary>A control with its light on: a tinted fill, an edge, light rising from the bottom.</summary>
    private static void Lit(UIShapeStyle s, float fill, float edge, float light, float height)
    {
        s.fill = UIColor.Token("Accent Deep", fill);
        s.stroke = UIColor.Token("Accent", edge);
        s.glow = UIColor.Token("Accent");
        s.glowSize = 8f;
        s.glowStrength = 0.08f;
        s.innerGlow = 0.05f;
        s.bottomLight = light;
        s.bottomLightHeight = height;
        NoShadow(s);
        s.transition = 0.18f;
        s.fadeIn = true;
    }

    private static void Card(UIShapeStyle s, string surface, float alpha, string edge, float edgeAlpha, float glow)
    {
        s.fill = UIColor.Token(surface, alpha);
        s.gradientEnd = 0.7f;
        s.stroke = UIColor.Token(edge, edgeAlpha);
        s.glow = UIColor.Token(edge);
        s.glowSize = 12f;
        s.glowStrength = glow;
        s.innerGlow = 0.06f;
        s.bottomLightHeight = 0f;
        Shadow(s, 0.45f, -2f, 6f);
        s.transition = 0.2f;
    }

    /// <summary>A lit card: a tinted surface, an overexposed edge, a wide glow in the tone.</summary>
    private static void Alarm(UIShapeStyle s, string surface, float fill, float edge, float glow, float light,
        float pulse, float speed, string tone)
    {
        Card(s, surface, fill, tone, edge, glow);
        s.glowSize = 18f;
        s.strokeWidth = 1.5f;
        s.innerGlow = 0.12f;
        s.highlight = 0.4f;
        s.bottomLight = light;
        s.bottomLightHeight = 0.45f;
        s.pulse = pulse;
        s.pulseSpeed = speed;
    }

    /// <summary>An alarm card under the pointer: a touch brighter, its light higher, the breath held.</summary>
    private static void AlarmHover(UIShapeStyle s, string tone, float fill, float edge, float glow)
    {
        Card(s, tone + " Surface", fill, tone, edge, glow);
        s.glowSize = 18f;
        s.strokeWidth = 1.75f;
        s.innerGlow = 0.16f;
        s.highlight = 0.5f;
        s.bottomLight = 0.5f;
        s.bottomLightHeight = 0.6f;
        s.transition = 0.14f;
    }

    /// <summary>A row needing attention: a dark warm well edged in its tone. Light only on hover.</summary>
    private static void Row(UIShapeStyle s, string tone, float fill, float edge, float light)
    {
        Glass(s, tone + " Surface", fill, 1f, 0f);
        s.stroke = UIColor.Token(tone, edge);
        s.glow = UIColor.Token(tone);
        s.glowSize = 10f;
        s.glowStrength = light > 0f ? 0.07f : 0.04f;
        s.innerGlow = light > 0f ? 0.06f : 0.03f;
        s.bottomLight = light;
        s.bottomLightHeight = 0.45f;
        NoShadow(s);
        s.transition = 0.14f;
    }

    /// <summary>A status shown as its text alone: no box, so it never reads as a button.</summary>
    private static void Badge(UIShapeStyle s, string tone)
    {
        s.fill = UIColor.Token(tone, 0f);
        s.stroke = UIColor.None;
        s.strokeWidth = 0f;
        NoGlow(s);
        s.bottomLightHeight = 0f;
        NoShadow(s);
        s.transition = 0.2f;
    }

    private static void Icon(UIShapeStyle s, float glow, float alpha)
    {
        s.kind = UIShapeKind.Sprite;
        s.fill = UIColor.Token("White", alpha);
        s.glow = UIColor.Token("White");
        s.glowSize = 4f;
        s.glowStrength = glow;
        Shadow(s, 0.55f, -1f, 0f, 1f);
        s.transition = 0.15f;
    }

    private static void Mark(UIShapeStyle s, float glowSize, float glowStrength)
    {
        s.fill = UIColor.Token("White");
        s.stroke = UIColor.None;
        s.glow = UIColor.Token("White");
        s.glowSize = glowSize;
        s.glowStrength = glowStrength;
        s.innerGlow = 0f;
        Shadow(s, 0.55f, -1f, 0f, 1f);
    }

    private static void Shadow(UIShapeStyle s, float alpha, float y, float softness, float x = 0f)
    {
        s.shadow = UIColor.Token("Shadow", alpha);
        s.shadowOffset = new Vector2(x, y);
        s.shadowSoftness = softness;
    }

    private static void NoShadow(UIShapeStyle s)
    {
        s.shadow = UIColor.None;
        s.shadowSoftness = 0f;
    }

    private static void NoGlow(UIShapeStyle s)
    {
        s.glowSize = 0f;
        s.glowStrength = 0f;
        s.innerGlow = 0f;
    }

    // ---- sprite-authored roles -------------------------------------------------

    /// <summary>Each authored sprite, the component and state it stands for.</summary>
    private static List<UITheme.StyleEntry> SpriteRoles()
    {
        var map = new List<UITheme.StyleEntry>();
        void Map(string sprite, string component, UIState state, string variant = null)
        {
            Sprite asset = AssetDatabase.LoadAssetAtPath<Sprite>(SpriteFolder + sprite + ".png");
            UIComponentStyle target = Components.FirstOrDefault(c => c.name == component);
            if (asset == null || target == null)
            {
                Debug.LogWarning($"UI_DESIGN_SYSTEM: {sprite} or {component} not found; that role keeps its sprite.");
                return;
            }
            map.Add(new UITheme.StyleEntry { sprite = asset, variant = variant, component = target, state = state });
        }

        Map("Panel Background", "Panel", UIState.Idle);
        Map("Detail Panel Background", "Detail Panel", UIState.Idle);
        Map("Solid UI Fill", "Segment", UIState.Idle);
        Map("Button Hover Background", "Segment", UIState.Hover);
        Map("Compact Button Background", "Segment", UIState.Selected);
        Map("Compact Button Background", "Card", UIState.Idle, CardVariant);
        Map("Button Hover Background", "Card", UIState.Hover, CardVariant);
        Map("Telemetry Card - Warning", "Card", UIState.Warning);
        Map("Telemetry Card - Critical", "Card", UIState.Critical);
        Map("Telemetry Card - Unavailable", "Card", UIState.Unavailable);
        Map("Accent Core", "Accent Bar", UIState.Idle);
        // Each trail sprite is brightest on the side named here.
        Map("Accent Trail - Fade Left", "Accent Trail Left", UIState.Idle);
        Map("Accent Trail - Fade Right", "Accent Trail Right", UIState.Idle);
        Map("Accent Trail - Top", "Accent Trail Bottom", UIState.Idle);
        Map("Accent Trail - Bottom", "Accent Trail Top", UIState.Idle);
        Map("White Pixel", "Leader Line", UIState.Idle);
        Map("Leader Line Anchor", "Leader Anchor", UIState.Idle);
        foreach (string icon in new[]
                 {
                     "Full Model Icon", "Cross Section Icon", "XRay Icon", "Arrow Left Icon", "Arrow Right Icon",
                     "Zoom In Icon", "Zoom Out Icon", "Close Icon", "Pin Icon"
                 })
            Map(icon, "Icon", UIState.Idle);
        return map;
    }

    /// <summary>A shadowed preset of each UI font: a sharp, dark underlay just below and right.</summary>
    private static List<UITheme.FontEntry> LegibleFonts()
    {
        var entries = new List<UITheme.FontEntry>();
        foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { FontFolder.TrimEnd('/') }))
        {
            TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
            if (font == null || font.material == null)
                continue;
            string path = $"{FontFolder}{font.name} - Shadow.mat";
            Material preset = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (preset == null)
            {
                preset = new Material(font.material);
                AssetDatabase.CreateAsset(preset, path);
            }
            else
            {
                preset.shader = font.material.shader;
                preset.CopyPropertiesFromMaterial(font.material);
            }
            preset.name = $"{font.name} - Shadow";
            preset.EnableKeyword("UNDERLAY_ON");
            preset.SetColor("_UnderlayColor", new Color(0f, 0.024f, 0.031f, 0.78f));
            preset.SetFloat("_UnderlayOffsetX", 0.55f);
            preset.SetFloat("_UnderlayOffsetY", -0.55f);
            preset.SetFloat("_UnderlayDilate", 0.2f);
            preset.SetFloat("_UnderlaySoftness", 0.08f);
            EditorUtility.SetDirty(preset);
            entries.Add(new UITheme.FontEntry { font = font, legibleMaterial = preset });
        }
        return entries;
    }

    // ---- asset plumbing ---------------------------------------------------------

    private static void Component(string name, string group, string description, Action<UIComponentStyle> define)
    {
        string folder = $"{ComponentFolder}/{(group == Parts ? "Parts" : name)}";
        EnsureFolder(folder);
        string path = $"{folder}/{name}.asset";
        UIComponentStyle component = AssetDatabase.LoadAssetAtPath<UIComponentStyle>(path);
        bool created = component == null;
        if (created)
        {
            component = ScriptableObject.CreateInstance<UIComponentStyle>();
            AssetDatabase.CreateAsset(component, path);
        }
        currentFolder = folder;
        if (created || reset)
        {
            EditorUtility.CopySerialized(ScriptableObject.CreateInstance<UIComponentStyle>(), component);
            component.name = name;
            component.description = $"{group}. {description}";
            define(component);
            EditorUtility.SetDirty(component);
        }
        else
        {
            // Kept as edited; its styles still count as live.
            foreach (UIShapeStyle style in States(component))
                Styles[AssetDatabase.GetAssetPath(style)] = style;
        }
        Components.Add(component);
    }

    private static string currentFolder;

    private static UIShapeStyle Style(string component, string state, Action<UIShapeStyle> define)
    {
        string path = $"{currentFolder}/{component} {state}.asset";
        UIShapeStyle style = AssetDatabase.LoadAssetAtPath<UIShapeStyle>(path);
        bool created = style == null;
        if (created)
        {
            style = ScriptableObject.CreateInstance<UIShapeStyle>();
            AssetDatabase.CreateAsset(style, path);
        }
        if (created || reset)
        {
            // Every style starts from the same base so the looks stay consistent.
            EditorUtility.CopySerialized(ScriptableObject.CreateInstance<UIShapeStyle>(), style);
            style.name = $"{component} {state}";
            define(style);
            EditorUtility.SetDirty(style);
        }
        Styles[path] = style;
        return style;
    }

    private static IEnumerable<UIShapeStyle> States(UIComponentStyle c) =>
        new[] { c.idle, c.hover, c.pressed, c.selected, c.selectedHover, c.disabled, c.warning, c.warningHover, c.critical, c.criticalHover, c.unavailable }
            .Where(s => s != null);

    /// <summary>Styles and components this tool no longer defines are removed, so the folders list the live system.</summary>
    private static void RemoveStale()
    {
        var live = new HashSet<string>(Styles.Keys);
        foreach (UIComponentStyle component in Components)
            live.Add(AssetDatabase.GetAssetPath(component));
        foreach (string guid in AssetDatabase.FindAssets("t:UIShapeStyle t:UIComponentStyle", new[] { ComponentFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!live.Contains(path))
                AssetDatabase.DeleteAsset(path);
        }
        // The earlier flat style folder.
        if (AssetDatabase.IsValidFolder("Assets/UI/Styles"))
            AssetDatabase.DeleteAsset("Assets/UI/Styles");
    }

    private static Material ShapeMaterial()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
            throw new InvalidOperationException($"Shader {ShaderName} is missing or does not compile.");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "UI Shape" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else if (material.shader != shader)
        {
            material.shader = shader;
            EditorUtility.SetDirty(material);
        }
        return material;
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(parent))
            EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    private static UIDesignTokens.ColorToken C(string name, string hex, string use)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color color);
        return new UIDesignTokens.ColorToken { name = name, color = color, use = use };
    }

    private static UIDesignTokens.SizeToken S(string name, float value, string use) =>
        new() { name = name, value = value, use = use };

    private static UIDesignTokens.TextStyle T(string name, TMP_FontAsset font, float size, float spacing, bool upper,
        string color, float alpha, string use) =>
        new()
        {
            name = name, font = font, size = size, letterSpacing = spacing, uppercase = upper,
            color = UIColor.Token(color, alpha), use = use
        };
}
