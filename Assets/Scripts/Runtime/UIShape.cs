using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// An Image drawn procedurally: a design-system component (a button, a card, a
/// panel) in a state, drawn by the UI/Digital Twin/Shape shader rather than
/// from sprite pixels. The rect is the shape's body; its glow and shadow are
/// drawn outside it, so layouts size controls without allowing for an effect
/// margin, and nothing is baked at one resolution. An icon keeps its sprite
/// and gets a shadow of its outline, and a glow while its control is hovered.
///
/// What it shows, first match wins:
///   1. its own <see cref="Style"/>, a single look;
///   2. its <see cref="Component"/> in its <see cref="State"/>;
///   3. for an authored icon sprite, the <see cref="UITheme"/>'s component for
///      it (the Icon).
/// The control that owns it (<see cref="UIControlState"/> or
/// <see cref="UIHoverSprite"/>) reports hover and press, and the component
/// answers with its hover look for the state. Changes blend over the new
/// style's transition.
///
/// It is still an Image, so every script, Button, layout and test that works
/// with Images works with it. Unstyled, or with the theme's shapes off, it
/// draws exactly like an Image.
/// </summary>
[AddComponentMenu("UI/Digital Twin Shape")]
public sealed class UIShape : Image
{
    [Tooltip("The design-system component this shape shows, such as Button or Card. " +
             "Empty: the theme's component for the icon sprite shown.")]
    [SerializeField] private UIComponentStyle component;

    [Tooltip("The component's state. A UI Control State on the control sets it for you.")]
    [SerializeField] private UIState state = UIState.Idle;

    [Tooltip("A single look of the shape's own, used instead of a component.")]
    [SerializeField] private UIShapeStyle style;

    [Tooltip("Narrows the theme's match for the sprite shown, such as Card.")]
    [SerializeField] private string variant;

    private UIShapeStyle shown;
    private UIShapeLook drawnLook;
    private UIShapeLook fromLook;
    private bool hasDrawn;
    private bool blending;
    private bool appearing;
    private float blendStart;
    private float blendDuration;
    private bool ownerHovered;
    private bool ownerPressed;
    private bool emphasised;

    public UIComponentStyle Component
    {
        get => component;
        set
        {
            if (component == value)
                return;
            component = value;
            SetAllDirty();
        }
    }

    public UIState State
    {
        get => state;
        set
        {
            if (state == value)
                return;
            state = value;
            SetAllDirty();
        }
    }

    public UIShapeStyle Style
    {
        get => style;
        set
        {
            if (style == value)
                return;
            style = value;
            SetAllDirty();
        }
    }

    public string Variant
    {
        get => variant;
        set
        {
            if (variant == value)
                return;
            variant = value;
            SetAllDirty();
        }
    }

    /// <summary>
    /// Shows the component's Selected look in place of its Idle and Hover
    /// looks, still answering the pointer if the component has a selected
    /// hover look. For UI authored with sprites whose sprite stays the same
    /// while it is chosen, such as the focused telemetry card. Alarm and other
    /// states are unaffected.
    /// </summary>
    public bool Emphasised
    {
        get => emphasised;
        set
        {
            if (emphasised == value)
                return;
            emphasised = value;
            SetVerticesDirty();
        }
    }

    /// <summary>
    /// Shows a segment's or tab's shape chosen (the component's Selected look)
    /// or at rest; a plain graphic is left as it is. For the segmented controls
    /// that choose by script rather than through a <see cref="UIControlState"/>.
    /// </summary>
    public static void ShowChosen(Graphic graphic, bool chosen)
    {
        if (graphic is UIShape shape)
            shape.State = chosen ? UIState.Selected : UIState.Idle;
    }

    /// <summary>
    /// Gives <paramref name="to"/> the look of <paramref name="from"/>: for
    /// shapes, its component, state and style; for any image, its sprite (an
    /// icon's), fill and colour. Used where one surface borrows another's look,
    /// such as a launcher framed like the switch beside it.
    /// </summary>
    public static void CopyLook(Image from, Image to)
    {
        if (from == null || to == null)
            return;
        to.sprite = from.sprite;
        to.type = from.type;
        to.color = from.color;
        to.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
        if (from is UIShape source && to is UIShape copy)
        {
            copy.Component = source.Component;
            copy.State = source.State;
            copy.Style = source.Style;
            copy.Variant = source.Variant;
        }
    }

    /// <summary>True while the control that owns this shape reports the pointer over it.</summary>
    public bool OwnerHovered => ownerHovered;

    /// <summary>The style drawn now, or null while drawn as a plain Image.</summary>
    public UIShapeStyle ShownStyle => ResolveStyle();

    private UIShapeStyle ResolveStyle()
    {
        UITheme theme = UITheme.Active;
        if (theme == null || !theme.DrawsShapes)
            return null;
        if (style != null)
            return style;
        if (component != null)
            return Resolve(component, state);
        return theme.Find(overrideSprite, variant, out UIComponentStyle authored, out UIState authoredState)
            ? Resolve(authored, authoredState)
            : null;
    }

    private UIShapeStyle Resolve(UIComponentStyle from, UIState shownState)
    {
        bool hovered = ownerHovered;
        if (emphasised && from.selected != null && shownState is UIState.Idle or UIState.Hover)
        {
            // A hover sprite swapped in while emphasised is a hover of the selected look.
            hovered |= shownState == UIState.Hover;
            shownState = UIState.Selected;
        }
        return from.Resolve(shownState, hovered, ownerPressed);
    }

    /// <summary>
    /// Set by the control that owns this shape while the pointer is over it or
    /// pressing it; the shape's component answers with its hover or pressed look.
    /// </summary>
    public void SetOwnerPointer(bool hovered, bool pressed)
    {
        if (ownerHovered == hovered && ownerPressed == pressed)
            return;
        ownerHovered = hovered;
        ownerPressed = pressed;
        SetVerticesDirty();
    }

    public override Material defaultMaterial =>
        ResolveStyle() != null ? UITheme.Active.shapeMaterial : base.defaultMaterial;

    public override Material material
    {
        get
        {
            Material authored = base.material;
            if (ResolveStyle() == null)
                return authored;
            // A material made for sprites (the leader line's reveal) cannot read
            // a shape's vertices. A shape material, such as a reveal clone of
            // the theme's, is kept.
            Material shapes = UITheme.Active.shapeMaterial;
            return authored != null && authored.shader == shapes.shader ? authored : shapes;
        }
        set => base.material = value;
    }

    // Every box samples the same white texture, so all of them batch together.
    // An icon samples its sprite.
    public override Texture mainTexture
    {
        get
        {
            UIShapeStyle resolved = ResolveStyle();
            return resolved == null || resolved.kind == UIShapeKind.Sprite ? base.mainTexture : s_WhiteTexture;
        }
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        EnableShaderChannels();
        UITheme.Changed += HandleThemeChanged;
        UIShapeStyle.Edited += HandleStyleEdited;
        UIDesignTokens.Edited += HandleThemeChanged;
        DigitalTwinSettings.Changed += HandleSettingsChanged;
    }

    protected override void OnDisable()
    {
        UITheme.Changed -= HandleThemeChanged;
        UIShapeStyle.Edited -= HandleStyleEdited;
        UIDesignTokens.Edited -= HandleThemeChanged;
        DigitalTwinSettings.Changed -= HandleSettingsChanged;
        // Shown again, a hover backdrop fades in rather than popping.
        if (Application.isPlaying && hasDrawn)
            appearing = true;
        shown = null;
        hasDrawn = false;
        blending = false;
        base.OnDisable();
    }

    protected override void OnCanvasHierarchyChanged()
    {
        base.OnCanvasHierarchyChanged();
        EnableShaderChannels();
    }

    private void HandleThemeChanged() => SetAllDirty();

    private void HandleSettingsChanged()
    {
        // Contrast and motion change how every look is drawn.
        if (contrast != DigitalTwinSettings.HighContrast || stillness != DigitalTwinSettings.ReduceMotion)
            SetVerticesDirty();
    }

    private bool contrast;
    private bool stillness;

    private void HandleStyleEdited(UIShapeStyle edited)
    {
        // A style edited may be any of the component's states.
        SetVerticesDirty();
    }

    /// <summary>The shape shader reads everything but position and tint from UV0 to UV3.</summary>
    private void EnableShaderChannels()
    {
        const AdditionalCanvasShaderChannels Needed =
            AdditionalCanvasShaderChannels.TexCoord1 |
            AdditionalCanvasShaderChannels.TexCoord2 |
            AdditionalCanvasShaderChannels.TexCoord3;
        Canvas target = canvas;
        if (target == null)
            return;
        if ((target.additionalShaderChannels & Needed) != Needed)
            target.additionalShaderChannels |= Needed;
        Canvas root = target.rootCanvas;
        if (root != null && (root.additionalShaderChannels & Needed) != Needed)
            root.additionalShaderChannels |= Needed;
    }

    private void LateUpdate()
    {
        if (blending)
            SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        UIShapeStyle target = ResolveStyle();
        if (target == null)
        {
            shown = null;
            hasDrawn = false;
            blending = false;
            base.OnPopulateMesh(vh);
            return;
        }

        UIShapeLook look = AdvanceLook(target);
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0f && r.height <= 0f)
            return;

        Sprite icon = target.kind == UIShapeKind.Sprite ? overrideSprite : null;
        if (target.kind == UIShapeKind.Sprite && icon == null)
            return;
        if (icon != null && preserveAspect)
            r = FitAspect(r, icon.rect.width / Mathf.Max(icon.rect.height, 0.0001f));

        Vector2 half = r.size * 0.5f;
        float pixel = 1f / Mathf.Max(canvas != null ? canvas.scaleFactor : 1f, 0.01f);
        // A box's tail, if it has one, reaches past its rect: the quad grows to hold it.
        bool hasTail = icon == null && target.tail != UIShapeTail.None && look.tailSize > 0f;
        float tailReach = hasTail ? look.tailSize : 0f;
        float reach = Mathf.Max(look.glowSize, look.shadowOffset.magnitude + look.shadowSoftness) + tailReach +
                      pixel * 2f + 1f;
        Vector2 grown = half + new Vector2(reach, reach);

        Color glowColor = look.glow;
        float glowStrength = look.glowStrength * glowColor.a;
        Vector2 offset = look.shadowOffset + new Vector2(128f, 128f);
        // The chamfer's byte carries the tail's size instead when there is a tail;
        // the side rides in the two bits above the gradient direction.
        int corners = hasTail ? 0 : (int)target.chamferCorners & 15;
        int tailSide = hasTail ? (int)target.tail : 0;
        float cut = hasTail ? look.tailSize : look.chamfer;
        // The kind's byte also carries the highlight, in 127 steps above the kind's bit.
        int highlight = Mathf.RoundToInt(Mathf.Clamp01(look.highlight) * 127f);
        float flags = Byte3((int)target.kind + highlight * 2, Mathf.RoundToInt(Mathf.Clamp(cut, 0f, 255f)),
            corners + (int)target.gradientStart * 16 + tailSide * 64);

        Vector4 shape;
        Vector4 colors;
        Vector4 motion;
        if (icon != null)
        {
            // The UV rect the whole drawn rect covers, the shadow's offset where a
            // box keeps its stroke colour, and the glow's reach where a box keeps
            // its light.
            shape = FullRectUv(icon);
            colors = new Vector4(
                PackRgb(look.fill),
                Pack888(look.fill.a, 0f, glowStrength * 0.25f),
                Pack12(offset.x, offset.y),
                PackRgb(glowColor));
            motion = new Vector4(
                PackRgb(look.shadow),
                Pack888(look.shadow.a, look.pulse, look.pulseSpeed / 2.55f),
                Pack12(look.glowSize, look.shadowSoftness),
                flags);
        }
        else
        {
            shape = new Vector4(
                Pack12(look.cornerRadius, look.strokeWidth),
                Pack12(look.glowSize, look.shadowSoftness),
                Pack12(offset.x, offset.y),
                Pack888(look.innerGlow, look.gradientEnd * 0.5f, look.hatch));
            colors = new Vector4(
                PackRgb(look.fill),
                Pack888(look.fill.a, look.stroke.a, glowStrength * 0.25f),
                PackRgb(look.stroke),
                PackRgb(glowColor));
            motion = new Vector4(
                PackRgb(look.shadow),
                Pack888(look.shadow.a, look.pulse, look.pulseSpeed / 2.55f),
                Pack12(look.bottomLight * 100f, look.bottomLightHeight * 100f),
                flags);
        }

        Color32 tint = color;
        Vector2 centre = r.center;
        for (int i = 0; i < 4; i++)
        {
            Vector2 corner = new(i == 0 || i == 1 ? -grown.x : grown.x, i == 0 || i == 3 ? -grown.y : grown.y);
            // AddVert(UIVertex), not AddUIVertexQuad: the quad helper drops UV2 and UV3.
            vh.AddVert(new UIVertex
            {
                position = centre + corner,
                color = tint,
                uv0 = new Vector4(corner.x, corner.y, half.x, half.y),
                uv1 = shape,
                uv2 = colors,
                uv3 = motion,
                normal = Vector3.back,
                tangent = new Vector4(1f, 0f, 0f, -1f)
            });
        }
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }

    /// <summary>
    /// The UV rect (min, size) that maps onto the sprite's whole rect. Unity trims
    /// a sprite's transparent border: its UVs cover only what is left, which an
    /// Image places inside the rect by the trimmed padding. The shader samples
    /// the full rect, so the UVs are extended by that padding.
    /// </summary>
    private static Vector4 FullRectUv(Sprite icon)
    {
        Vector4 outer = UnityEngine.Sprites.DataUtility.GetOuterUV(icon);
        Vector4 padding = UnityEngine.Sprites.DataUtility.GetPadding(icon);
        Vector2 size = new(Mathf.Max(icon.rect.width, 0.0001f), Mathf.Max(icon.rect.height, 0.0001f));
        Vector2 min = new(padding.x / size.x, padding.y / size.y);
        Vector2 max = new((size.x - padding.z) / size.x, (size.y - padding.w) / size.y);
        Vector2 uvSize = new(
            (outer.z - outer.x) / Mathf.Max(max.x - min.x, 0.0001f),
            (outer.w - outer.y) / Mathf.Max(max.y - min.y, 0.0001f));
        return new Vector4(outer.x - min.x * uvSize.x, outer.y - min.y * uvSize.y, uvSize.x, uvSize.y);
    }

    /// <summary>The largest rect of <paramref name="aspect"/> centred in <paramref name="r"/>.</summary>
    private static Rect FitAspect(Rect r, float aspect)
    {
        Vector2 size = r.size;
        if (size.x / Mathf.Max(size.y, 0.0001f) > aspect)
            size.x = size.y * aspect;
        else
            size.y = size.x / aspect;
        return new Rect(r.center - size * 0.5f, size);
    }

    /// <summary>Moves towards the target style and returns the look to draw now.</summary>
    private UIShapeLook AdvanceLook(UIShapeStyle target)
    {
        if (target != shown)
        {
            bool playing = Application.isPlaying;
            UIShapeLook targetLook = target.Look;
            if (hasDrawn)
                fromLook = drawnLook;
            else if (appearing)
                fromLook = Transparent(targetLook);
            bool animate = playing && target.transition > 0f && (hasDrawn || (appearing && target.fadeIn)) &&
                           !DigitalTwinSettings.ReduceMotion;
            if (animate)
            {
                blending = true;
                blendStart = Time.unscaledTime;
                blendDuration = target.transition;
            }
            else
            {
                blending = false;
            }
            shown = target;
            appearing = false;
        }

        UIShapeLook look = target.Look;
        if (blending)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - blendStart) / Mathf.Max(blendDuration, 0.0001f));
            if (t >= 1f)
                blending = false;
            else
                look = UIShapeLook.Lerp(fromLook, look, t * t * (3f - 2f * t));
        }
        drawnLook = look;
        hasDrawn = true;
        return Adjusted(look);
    }

    /// <summary>The viewer's display settings applied to a look, after blending.</summary>
    private UIShapeLook Adjusted(UIShapeLook look)
    {
        contrast = DigitalTwinSettings.HighContrast;
        stillness = DigitalTwinSettings.ReduceMotion;
        if (stillness)
            look.pulse = 0f;
        if (contrast)
        {
            // Surfaces that are meant to be seen become near opaque; clear ones
            // (an idle segment, a badge) stay clear. Edges and text-like marks firm up.
            if (look.fill.a > 0.3f)
                look.fill.a = Mathf.Lerp(look.fill.a, 1f, 0.7f);
            if (look.stroke.a > 0.05f)
                look.stroke.a = Mathf.Min(1f, look.stroke.a * 1.8f + 0.15f);
            look.strokeWidth = Mathf.Max(look.strokeWidth, look.stroke.a > 0.05f ? 1.25f : 0f);
            look.shadow.a = Mathf.Min(1f, look.shadow.a * 1.4f);
        }
        return look;
    }

    private static UIShapeLook Transparent(UIShapeLook look)
    {
        look.fill.a = 0f;
        look.stroke.a = 0f;
        look.glowStrength = 0f;
        look.bottomLight = 0f;
        look.shadow.a = 0f;
        return look;
    }

    // ---- packing: exact integers of up to 24 bits in a float -------------

    private static float Byte3(int a, int b, int c) =>
        Mathf.Clamp(a, 0, 255) + Mathf.Clamp(b, 0, 255) * 256f + Mathf.Clamp(c, 0, 255) * 65536f;

    private static int ToByte(float value) => Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

    private static float Pack888(float a, float b, float c) => Byte3(ToByte(a), ToByte(b), ToByte(c));

    private static float PackRgb(Color c) => Pack888(c.r, c.g, c.b);

    /// <summary>Two values of 0 to 255.9, in sixteenths.</summary>
    private static float Pack12(float low, float high)
    {
        int a = Mathf.Clamp(Mathf.RoundToInt(low * 16f), 0, 4095);
        int b = Mathf.Clamp(Mathf.RoundToInt(high * 16f), 0, 4095);
        return a + b * 4096f;
    }
}
