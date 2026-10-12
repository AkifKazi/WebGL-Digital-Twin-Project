using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// The design system in one asset: its tokens, its components, the material
/// that draws them and the shadow under on-screen text.
///
/// New UI names its component and state on each <see cref="UIShape"/>. UI
/// authored with sprites is matched by the sprite it shows, which names its
/// role (a panel, a selected segment, a hovered control, an alarm card): the
/// entries below turn each sprite into a component and state, so code that
/// swaps sprites for a state swaps procedural looks too, with no change of its
/// own. A variant narrows a match (a card's background uses the same sprite as
/// a selected button, but the Card variant shows it as a card). Turning
/// <see cref="proceduralShapes"/> off draws the authored sprites again.
///
/// Loaded from Resources, so edit mode, play mode and builds find the same theme.
/// </summary>
[CreateAssetMenu(menuName = "Digital Twin/UI Theme", fileName = ResourceName)]
public sealed class UITheme : ScriptableObject
{
    public const string ResourceName = "UI Theme";

    [Serializable]
    public struct StyleEntry
    {
        [Tooltip("The authored sprite that names the role.")]
        public Sprite sprite;
        [Tooltip("Empty matches any shape; otherwise only shapes of this variant.")]
        public string variant;
        public UIComponentStyle component;
        public UIState state;
    }

    [Serializable]
    public struct FontEntry
    {
        public TMP_FontAsset font;
        [Tooltip("A preset of the font's material with an underlay: the text's shadow.")]
        public Material legibleMaterial;
    }

    [Tooltip("Off draws the authored sprites instead, to compare the two.")]
    public bool proceduralShapes = true;

    [Tooltip("Draws every shape. Uses the UI/Digital Twin/Shape shader.")]
    public Material shapeMaterial;

    [Tooltip("Colours, spacing, text styles and motion every style and component uses.")]
    public UIDesignTokens tokens;

    [Tooltip("The design system's components, in the order the catalog shows them.")]
    public List<UIComponentStyle> components = new();

    [Tooltip("Sprite-authored UI: the component and state each sprite stands for.")]
    public List<StyleEntry> styles = new();

    [Header("Text")]
    [Tooltip("Gives on-screen text a sharp dark shadow so it reads on bright backgrounds.")]
    public bool textShadow = true;
    public List<FontEntry> fonts = new();

    private static UITheme active;
    private static bool loaded;

    /// <summary>Raised when the theme is edited or switched, so every shape redraws.</summary>
    public static event Action Changed;

    public static UITheme Active
    {
        get
        {
            if (!loaded || active == null)
            {
                active = Resources.Load<UITheme>(ResourceName);
                loaded = true;
            }
            return active;
        }
    }

    public bool DrawsShapes => proceduralShapes && shapeMaterial != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        loaded = false;
        active = null;
    }

    /// <summary>
    /// The component and state a shape showing <paramref name="sprite"/> stands
    /// for: an entry of the shape's variant wins over one for any variant.
    /// False when the sprite names no role.
    /// </summary>
    public bool Find(Sprite sprite, string variant, out UIComponentStyle component, out UIState state)
    {
        component = null;
        state = UIState.Idle;
        bool hasVariant = !string.IsNullOrEmpty(variant);
        foreach (StyleEntry entry in styles)
        {
            if (sprite == null || entry.sprite != sprite || entry.component == null)
                continue;
            if (string.IsNullOrEmpty(entry.variant))
            {
                if (component == null)
                {
                    component = entry.component;
                    state = entry.state;
                }
            }
            else if (hasVariant && string.Equals(entry.variant, variant, StringComparison.Ordinal))
            {
                component = entry.component;
                state = entry.state;
                return true;
            }
        }
        return component != null;
    }

    /// <summary>A component by its asset name, such as Button or Card.</summary>
    public UIComponentStyle Component(string name)
    {
        foreach (UIComponentStyle component in components)
        {
            if (component != null && string.Equals(component.name, name, StringComparison.Ordinal))
                return component;
        }
        return null;
    }

    /// <summary>The shadowed material for <paramref name="font"/>, or null.</summary>
    public Material LegibleMaterial(TMP_FontAsset font)
    {
        if (font == null)
            return null;
        foreach (FontEntry entry in fonts)
        {
            if (entry.font == font)
                return entry.legibleMaterial;
        }
        return null;
    }

    public static void RaiseChanged() => Changed?.Invoke();

    private void OnValidate()
    {
        if (this == active || active == null)
            RaiseChanged();
    }
}
