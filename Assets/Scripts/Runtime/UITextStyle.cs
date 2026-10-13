using TMPro;
using UnityEngine;

/// <summary>
/// Gives a text one of the design system's text styles (see
/// <see cref="UIDesignTokens"/>): its font, size, letter spacing, case and
/// colour. Editing the style in the tokens updates every text that uses it.
/// The text's own content, alignment and wrapping stay as authored.
///
/// A text in the Button style on a control (one with a
/// <see cref="UIControlState"/>) is that control's label: it takes the
/// control colours (<see cref="UIControlColors"/>) for the control's state
/// rather than the style's colour, near white when chosen and a quiet cyan at rest.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(TMP_Text))]
[AddComponentMenu("UI/Digital Twin Text Style")]
public sealed class UITextStyle : MonoBehaviour
{
    [Tooltip("A text style from the theme's tokens, such as Title, Label, Body, Value or Caption.")]
    [SerializeField] private string textStyle = "Body";

    [Tooltip("Keep the text's own colour (set by a view for a state) instead of the style's.")]
    [SerializeField] private bool keepColor;

    /// <summary>The text style a control's label is written in.</summary>
    public const string ControlLabelStyle = "Button";

    public string Name
    {
        get => textStyle;
        set
        {
            textStyle = value;
            Apply();
        }
    }

    private void OnEnable()
    {
        UIDesignTokens.Edited += Apply;
        UITheme.Changed += Apply;
        Apply();
    }

    private void OnDisable()
    {
        UIDesignTokens.Edited -= Apply;
        UITheme.Changed -= Apply;
    }

    private void OnValidate() => Apply();


    public void Apply()
    {
        if (this == null)
            return;
        UIDesignTokens tokens = UITheme.Active != null ? UITheme.Active.tokens : null;
        if (tokens == null || !tokens.TryGetTextStyle(textStyle, out UIDesignTokens.TextStyle style) ||
            !TryGetComponent(out TMP_Text text))
            return;
        if (style.font != null && text.font != style.font)
            text.font = style.font;
        text.fontSize = style.size;
        text.characterSpacing = style.letterSpacing;
        text.fontStyle = style.uppercase ? text.fontStyle | FontStyles.UpperCase : text.fontStyle & ~FontStyles.UpperCase;
        if (keepColor)
            return;
        // A control's label follows the control's state, read now: the label
        // may be added after the control last changed state.
        UIControlState control = string.Equals(textStyle, ControlLabelStyle, System.StringComparison.Ordinal)
            ? GetComponentInParent<UIControlState>(true)
            : null;
        if (control == null)
            text.color = style.color.Resolve();
        else
            text.color = control.State == UIState.Disabled
                ? UIControlColors.Disabled
                : UIControlColors.For(control.State == UIState.Selected);
    }
}
