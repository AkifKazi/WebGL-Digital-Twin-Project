using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The design system's base values, like variables in a design tool: named
/// colours, spacing, text styles and motion times. Styles take their colours
/// from here by name (see <see cref="UIColor"/>), so changing a token changes
/// every style that uses it. New UI takes its spacing and text from here too.
/// </summary>
[CreateAssetMenu(menuName = "Digital Twin/UI Design Tokens", fileName = "UI Tokens")]
public sealed class UIDesignTokens : ScriptableObject
{
    [Serializable]
    public struct ColorToken
    {
        public string name;
        public Color color;
        [Tooltip("What the colour is for.")]
        public string use;
    }

    [Serializable]
    public struct SizeToken
    {
        public string name;
        public float value;
        public string use;
    }

    [Serializable]
    public struct TextStyle
    {
        public string name;
        public TMP_FontAsset font;
        [Min(1f)] public float size;
        [Tooltip("Extra space between letters, in TMP's units.")]
        public float letterSpacing;
        public bool uppercase;
        [Tooltip("A colour token, with its alpha.")]
        public UIColor color;
        public string use;
    }

    public List<ColorToken> colors = new();
    [Tooltip("The spacing scale: padding and gaps between elements.")]
    public List<SizeToken> spacing = new();
    public List<TextStyle> textStyles = new();
    [Tooltip("Seconds for a state change: hover in and out.")]
    [Min(0f)] public float fastMotion = 0.14f;
    [Tooltip("Seconds for larger changes: selection, alarm states.")]
    [Min(0f)] public float normalMotion = 0.2f;

    /// <summary>Raised when a token is edited, so every shape and text redraws.</summary>
    public static event Action Edited;

    public bool TryGetColor(string name, out Color color)
    {
        foreach (ColorToken token in colors)
        {
            if (string.Equals(token.name, name, StringComparison.Ordinal))
            {
                color = token.color;
                return true;
            }
        }
        color = Color.magenta;
        return false;
    }

    public float Space(string name, float fallback = 0f)
    {
        foreach (SizeToken token in spacing)
        {
            if (string.Equals(token.name, name, StringComparison.Ordinal))
                return token.value;
        }
        return fallback;
    }

    public bool TryGetTextStyle(string name, out TextStyle style)
    {
        foreach (TextStyle candidate in textStyles)
        {
            if (string.Equals(candidate.name, name, StringComparison.Ordinal))
            {
                style = candidate;
                return true;
            }
        }
        style = default;
        return false;
    }

    private void OnValidate() => Edited?.Invoke();
}

/// <summary>
/// A colour in a style: a token by name with an alpha, or a colour of its own
/// when no token is named. A missing token shows magenta, so it is noticed.
/// </summary>
[Serializable]
public struct UIColor
{
    [Tooltip("The colour token. Empty: the custom colour below.")]
    public string token;
    [Range(0f, 1f)] public float alpha;
    public Color custom;

    public static UIColor Token(string name, float alpha = 1f) => new() { token = name, alpha = alpha, custom = Color.clear };

    public static UIColor Custom(Color color) => new() { token = string.Empty, alpha = 1f, custom = color };

    public static readonly UIColor None = Custom(Color.clear);

    public Color Resolve()
    {
        if (string.IsNullOrEmpty(token))
            return custom;
        UIDesignTokens tokens = UITheme.Active != null ? UITheme.Active.tokens : null;
        Color color = Color.magenta;
        tokens?.TryGetColor(token, out color);
        color.a *= alpha;
        return color;
    }
}
