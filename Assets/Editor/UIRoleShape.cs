using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Design-system shapes for the setup tools: a shape names its component and
/// state itself (a panel, a segment, a card), so no background sprite has to
/// stand for it. Icons are still sprites, drawn by the theme's Icon component.
/// </summary>
public static class UIRoleShape
{
    /// <summary>Adds a <see cref="UIShape"/> showing <paramref name="component"/> in <paramref name="state"/>.</summary>
    public static UIShape Add(GameObject target, string component, UIState state = UIState.Idle)
    {
        UIShape shape = target.AddComponent<UIShape>();
        Set(shape, component, state);
        return shape;
    }

    /// <summary>
    /// Makes <paramref name="image"/>, a UIShape, show the theme's
    /// <paramref name="component"/> in <paramref name="state"/>, with no sprite.
    /// </summary>
    public static void Set(Image image, string component, UIState state = UIState.Idle)
    {
        if (image is not UIShape shape)
        {
            Debug.LogWarning($"UI_ROLE_SHAPE '{image?.name}' is a plain Image, not a UIShape: it cannot show {component}.", image);
            return;
        }
        UIComponentStyle style = UITheme.Active != null ? UITheme.Active.Component(component) : null;
        if (style == null)
            Debug.LogWarning($"UI_ROLE_SHAPE the theme has no {component} component; '{image.name}' draws as a plain fill.", image);
        shape.sprite = null;
        shape.Component = style;
        shape.State = state;
        shape.color = Color.white;
    }
}
