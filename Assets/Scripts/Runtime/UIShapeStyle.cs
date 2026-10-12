using System;
using UnityEngine;

public enum UIShapeKind
{
    /// <summary>A procedural box: panels, controls, cards, lines.</summary>
    Box,
    /// <summary>The shape's own sprite, tinted, with a shadow and glow of its outline: icons.</summary>
    Sprite
}

/// <summary>Where a gradient is at full strength; it runs to the opposite side.</summary>
public enum UIGradientStart
{
    Top,
    Bottom,
    Left,
    Right
}

[Flags]
public enum UIShapeCorners
{
    None = 0,
    TopLeft = 1,
    TopRight = 2,
    BottomRight = 4,
    BottomLeft = 8,
    All = 15
}

/// <summary>
/// One look for a <see cref="UIShape"/>, like a style in a design tool: fill,
/// stroke, glow, shadow, corners and motion. Sizes are in canvas units at the
/// reference resolution, so they scale with the rest of the UI. Shapes pick
/// their style from the <see cref="UITheme"/>; editing a style updates every
/// shape that shows it. Sprite styles use fill as the icon's tint and only
/// the glow, shadow and motion settings. Colours name a token of the theme's
/// <see cref="UIDesignTokens"/>, so a palette change reaches every style.
/// A <see cref="UIComponentStyle"/> groups styles into a component's states.
/// </summary>
[CreateAssetMenu(menuName = "Digital Twin/UI Shape Style", fileName = "UI Shape Style")]
public sealed class UIShapeStyle : ScriptableObject
{
    [Header("Shape")]
    public UIShapeKind kind = UIShapeKind.Box;
    [Min(0f)] public float cornerRadius;
    [Tooltip("Cut corners, in units. Clamped to the shape's size.")]
    [Min(0f)] public float chamfer;
    public UIShapeCorners chamferCorners = UIShapeCorners.None;

    [Header("Fill")]
    [Tooltip("A colour token with its alpha, or a custom colour. An icon's fill tints its sprite.")]
    public UIColor fill = UIColor.Token("Surface", 0.7f);
    public UIGradientStart gradientStart = UIGradientStart.Top;
    [Tooltip("The fill's strength at the far side, as a share of its strength at the start. 1 is no gradient.")]
    [Range(0f, 2f)] public float gradientEnd = 1f;
    [Tooltip("Diagonal stripes in the stroke colour, like a locked or unavailable item.")]
    [Range(0f, 1f)] public float hatch;

    [Header("Stroke (inside the edge)")]
    public UIColor stroke = UIColor.Token("Accent", 0.35f);
    [Min(0f)] public float strokeWidth = 1f;

    [Header("Glow")]
    public UIColor glow = UIColor.Token("Accent");
    [Tooltip("How far the glow reaches outside the edge. The shape's rect never includes it.")]
    [Min(0f)] public float glowSize = 12f;
    [Range(0f, 4f)] public float glowStrength = 0.2f;
    [Tooltip("Light along the inside of the edge, in the glow colour.")]
    [Range(0f, 1f)] public float innerGlow = 0.1f;
    [Tooltip("Overexposure: the stroke and the glow's core whiten, as a light too bright for the " +
             "camera does, while the wider glow keeps the colour. For selected and alarm looks.")]
    [Range(0f, 1f)] public float highlight;

    [Header("Bottom light")]
    [Tooltip("A lit bottom edge and light rising from it, in the stroke colour, as if a lamp under " +
             "the shape were on. Blending into a look with more of it turns the light on.")]
    [Range(0f, 2f)] public float bottomLight;
    [Tooltip("How far the light rises, as a share of the shape's height.")]
    [Range(0f, 1f)] public float bottomLightHeight = 0.5f;

    [Header("Shadow (keeps edges readable on bright backgrounds)")]
    public UIColor shadow = UIColor.Token("Shadow", 0.45f);
    public Vector2 shadowOffset = new(0f, -2f);
    [Tooltip("0 is a sharp shadow.")]
    [Min(0f)] public float shadowSoftness = 4f;

    [Header("Motion")]
    [Tooltip("Seconds to blend into this look from the previous one.")]
    [Min(0f)] public float transition = 0.14f;
    [Tooltip("Fades in when a shape showing this look is switched on, such as a hover backdrop.")]
    public bool fadeIn;
    [Tooltip("How much the glow breathes while the shape shows this look.")]
    [Range(0f, 1f)] public float pulse;
    [Tooltip("Breaths per second.")]
    [Range(0.1f, 2.5f)] public float pulseSpeed = 0.6f;

    /// <summary>Raised when a style is edited, so shapes showing it redraw.</summary>
    public static event Action<UIShapeStyle> Edited;

    /// <summary>The numbers a shape draws with; blended while a look changes.</summary>
    public UIShapeLook Look => new()
    {
        fill = fill.Resolve(),
        stroke = stroke.Resolve(),
        glow = glow.Resolve(),
        shadow = shadow.Resolve(),
        cornerRadius = cornerRadius,
        chamfer = chamfer,
        strokeWidth = strokeWidth,
        glowSize = glowSize,
        glowStrength = glowStrength,
        innerGlow = innerGlow,
        highlight = highlight,
        bottomLight = bottomLight,
        bottomLightHeight = bottomLightHeight,
        shadowOffset = shadowOffset,
        shadowSoftness = shadowSoftness,
        gradientEnd = gradientEnd,
        hatch = hatch,
        pulse = pulse,
        pulseSpeed = pulseSpeed
    };

    private void OnValidate() => Edited?.Invoke(this);
}

/// <summary>A style's numbers. Kind, corners and gradient direction are not blended.</summary>
public struct UIShapeLook
{
    public Color fill, stroke, glow, shadow;
    public float cornerRadius, chamfer, strokeWidth;
    public float glowSize, glowStrength, innerGlow, highlight, bottomLight, bottomLightHeight;
    public Vector2 shadowOffset;
    public float shadowSoftness, gradientEnd, hatch, pulse, pulseSpeed;

    public static UIShapeLook Lerp(in UIShapeLook a, in UIShapeLook b, float t) => new()
    {
        fill = Color.Lerp(a.fill, b.fill, t),
        stroke = Color.Lerp(a.stroke, b.stroke, t),
        glow = Color.Lerp(a.glow, b.glow, t),
        shadow = Color.Lerp(a.shadow, b.shadow, t),
        cornerRadius = Mathf.Lerp(a.cornerRadius, b.cornerRadius, t),
        chamfer = Mathf.Lerp(a.chamfer, b.chamfer, t),
        strokeWidth = Mathf.Lerp(a.strokeWidth, b.strokeWidth, t),
        glowSize = Mathf.Lerp(a.glowSize, b.glowSize, t),
        glowStrength = Mathf.Lerp(a.glowStrength, b.glowStrength, t),
        innerGlow = Mathf.Lerp(a.innerGlow, b.innerGlow, t),
        highlight = Mathf.Lerp(a.highlight, b.highlight, t),
        bottomLight = Mathf.Lerp(a.bottomLight, b.bottomLight, t),
        bottomLightHeight = Mathf.Lerp(a.bottomLightHeight, b.bottomLightHeight, t),
        shadowOffset = Vector2.Lerp(a.shadowOffset, b.shadowOffset, t),
        shadowSoftness = Mathf.Lerp(a.shadowSoftness, b.shadowSoftness, t),
        gradientEnd = Mathf.Lerp(a.gradientEnd, b.gradientEnd, t),
        hatch = Mathf.Lerp(a.hatch, b.hatch, t),
        pulse = Mathf.Lerp(a.pulse, b.pulse, t),
        pulseSpeed = Mathf.Lerp(a.pulseSpeed, b.pulseSpeed, t)
    };
}
