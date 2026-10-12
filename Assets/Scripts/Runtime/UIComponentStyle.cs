using UnityEngine;

/// <summary>
/// The state a component shows. Hover and press are what the pointer is doing;
/// the rest are what the component means: chosen, unavailable to press, or a
/// reading's alarm level.
/// </summary>
public enum UIState
{
    Idle,
    Hover,
    Pressed,
    Selected,
    Disabled,
    Warning,
    Critical,
    Unavailable
}

/// <summary>
/// One component of the design system (a button, a card, a panel) and its look
/// in every state, each a <see cref="UIShapeStyle"/>. A state left empty falls
/// back to Idle, so a component only defines the states it needs. Alarm states
/// have a hover look of their own, so a warning card still answers the pointer.
///
/// A <see cref="UIShape"/> shows a component in a state; <see cref="UIControlState"/>
/// sets that state from the pointer and the control.
/// </summary>
[CreateAssetMenu(menuName = "Digital Twin/UI Component", fileName = "UI Component")]
public sealed class UIComponentStyle : ScriptableObject
{
    [TextArea(2, 4)]
    [Tooltip("What the component is for and when to use it.")]
    public string description;

    [Header("Interaction")]
    public UIShapeStyle idle;
    public UIShapeStyle hover;
    public UIShapeStyle pressed;
    public UIShapeStyle selected;
    [Tooltip("Optional. A selected component that still answers the pointer, such as the focused card.")]
    public UIShapeStyle selectedHover;
    public UIShapeStyle disabled;

    [Header("Meaning")]
    public UIShapeStyle warning;
    public UIShapeStyle warningHover;
    public UIShapeStyle critical;
    public UIShapeStyle criticalHover;
    public UIShapeStyle unavailable;

    /// <summary>The style for a state, falling back to Idle where the state has none.</summary>
    public UIShapeStyle Get(UIState state)
    {
        UIShapeStyle style = state switch
        {
            UIState.Hover => hover,
            UIState.Pressed => pressed != null ? pressed : hover,
            UIState.Selected => selected,
            UIState.Disabled => disabled,
            UIState.Warning => warning,
            UIState.Critical => critical,
            UIState.Unavailable => unavailable,
            _ => idle
        };
        return style != null ? style : idle;
    }

    /// <summary>
    /// The style while the pointer is over (or pressing) a component in
    /// <paramref name="state"/>. Disabled and unavailable do not answer, nor
    /// does Selected unless the component has a selected hover look.
    /// </summary>
    public UIShapeStyle Resolve(UIState state, bool hovered, bool pressing)
    {
        if (hovered || pressing)
        {
            UIShapeStyle answer = state switch
            {
                UIState.Idle or UIState.Hover => pressing && pressed != null ? pressed : hover,
                UIState.Warning => warningHover,
                UIState.Critical => criticalHover,
                UIState.Selected => selectedHover,
                _ => null
            };
            if (answer != null)
                return answer;
        }
        return Get(state);
    }

    private void OnValidate() => UITheme.RaiseChanged();
}
