using UnityEngine;

/// <summary>
/// The colours of a control's own label and icon, from the design system's
/// tokens: one rule for every control in the interface.
///
///   Control           at rest: an unselected segment, tab or option, a
///                     button, an icon button. A quiet cyan, so the chosen
///                     one stands out without the rest reading as disabled.
///   Control Selected  the chosen segment, tab or option, or a launcher whose
///                     panel is open: near white.
///   Neutral           a control that cannot be used now.
///
/// Content is not a control: readings, titles, sentences and headings keep
/// their text styles' colours.
/// </summary>
public static class UIControlColors
{
    public const string IdleToken = "Control";
    public const string SelectedToken = "Control Selected";
    public const string DisabledToken = "Neutral";

    // Used when the theme or a token is missing.
    private static readonly Color IdleFallback = new Color32(0xCE, 0xE3, 0xE6, 0xFF);
    private static readonly Color SelectedFallback = new Color32(0xEA, 0xFD, 0xFF, 0xFF);
    private static readonly Color DisabledFallback = new Color32(0x73, 0x80, 0x8C, 0xFF);

    /// <summary>A control's label or icon at rest.</summary>
    public static Color Idle => Resolve(IdleToken, IdleFallback);

    /// <summary>The chosen control's label or icon.</summary>
    public static Color Selected => Resolve(SelectedToken, SelectedFallback);

    /// <summary>A control's label or icon while it cannot be used.</summary>
    public static Color Disabled => Resolve(DisabledToken, DisabledFallback);

    /// <summary>Selected or at rest.</summary>
    public static Color For(bool selected) => selected ? Selected : Idle;

    /// <summary>Selected, at rest, or unusable.</summary>
    public static Color For(bool selected, bool usable) => !usable ? Disabled : For(selected);

    private static Color Resolve(string token, Color fallback)
    {
        UIDesignTokens tokens = UITheme.Active != null ? UITheme.Active.tokens : null;
        return tokens != null && tokens.TryGetColor(token, out Color color) ? color : fallback;
    }
}
