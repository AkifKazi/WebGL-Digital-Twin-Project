using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Building blocks for UI made in code (the settings panel, the Ask window):
/// rects, design-system shapes, icons and texts in the theme's text styles.
/// The editor's equivalent, for UI made in the hierarchy, is
/// GameObject &gt; Digital Twin UI.
/// </summary>
public static class UIBuilder
{
    public static RectTransform Rect(string name, RectTransform parent, int layer)
    {
        GameObject child = new(name, typeof(RectTransform));
        child.layer = parent != null ? parent.gameObject.layer : layer;
        RectTransform rect = (RectTransform)child.transform;
        if (parent != null)
            rect.SetParent(parent, false);
        return rect;
    }

    public static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>A design-system component; a plain dark fill when the theme is missing.</summary>
    public static Graphic Shape(RectTransform rect, string component, bool raycast)
    {
        UIComponentStyle style = UITheme.Active != null ? UITheme.Active.Component(component) : null;
        if (style == null)
        {
            Image plain = rect.gameObject.AddComponent<Image>();
            plain.color = new Color(0.016f, 0.07f, 0.106f, 0.9f);
            plain.raycastTarget = raycast;
            return plain;
        }
        UIShape shape = rect.gameObject.AddComponent<UIShape>();
        shape.Component = style;
        shape.raycastTarget = raycast;
        return shape;
    }

    /// <summary>
    /// An authored icon sprite, drawn by the theme's Icon component, in a
    /// control's colour at rest (UIControlColors); its control recolours it when chosen.
    /// </summary>
    public static Graphic Icon(RectTransform rect, Sprite sprite)
    {
        UIShape icon = rect.gameObject.AddComponent<UIShape>();
        icon.sprite = sprite;
        icon.preserveAspect = true;
        icon.raycastTarget = false;
        icon.color = UIControlColors.Idle;
        if (UITheme.Active != null)
            icon.Component = UITheme.Active.Component("Icon");
        return icon;
    }

    /// <summary>A text in one of the theme's text styles (Display, Value, Title, Label, Button, Body, Caption).</summary>
    public static TextMeshProUGUI Text(string name, RectTransform parent, string style, TextAlignmentOptions alignment,
        int layer = 0)
    {
        RectTransform rect = Rect(name, parent, layer);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.alignment = alignment;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        ApplyTextStyle(text, style);
        return text;
    }

    /// <summary>Gives <paramref name="text"/> a theme text style's font, size, spacing, case and colour.</summary>
    public static void ApplyTextStyle(TMP_Text text, string style)
    {
        UIDesignTokens tokens = UITheme.Active != null ? UITheme.Active.tokens : null;
        if (tokens != null && tokens.TryGetTextStyle(style, out UIDesignTokens.TextStyle s))
        {
            if (s.font != null)
                text.font = s.font;
            text.fontSize = s.size;
            text.characterSpacing = s.letterSpacing;
            if (s.uppercase)
                text.fontStyle |= FontStyles.UpperCase;
            else
                text.fontStyle &= ~FontStyles.UpperCase;
            text.color = s.color.Resolve();
        }
        else
        {
            text.fontSize = 20f;
            text.color = Color.white;
        }
    }

    /// <summary>A colour token of the active theme, or <paramref name="fallback"/> without one.</summary>
    public static Color Token(string name, float alpha, Color fallback)
    {
        UIDesignTokens tokens = UITheme.Active != null ? UITheme.Active.tokens : null;
        return tokens != null ? UIColor.Token(name, alpha).Resolve() : fallback;
    }
}
