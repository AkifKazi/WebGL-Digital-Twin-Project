using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Darkens the top and bottom of the screen behind the interface, so the
/// controls there read over a bright sky or a pale floor: a gradient from the
/// edge inwards, set by the viewer's Edge Shading setting (stronger with High
/// Contrast). Stretch it over the canvas as its first child, so every panel
/// draws over it; it never takes the pointer.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIEdgeShade : MaskableGraphic
{
    [SerializeField] private Color shade = new(0f, 0.024f, 0.031f, 1f);

    [Tooltip("How far each gradient reaches in, as a share of the screen's height.")]
    [SerializeField, Range(0f, 0.5f)] private float topReach = 0.2f;
    [SerializeField, Range(0f, 0.5f)] private float bottomReach = 0.26f;

    [Tooltip("Darkness at the very edge, for the Subtle and Strong settings.")]
    [SerializeField, Range(0f, 1f)] private float subtle = 0.4f;
    [SerializeField, Range(0f, 1f)] private float strong = 0.7f;

    private const int Steps = 6;

    protected override void OnEnable()
    {
        base.OnEnable();
        raycastTarget = false;
        DigitalTwinSettings.Changed += SetVerticesDirty;
    }

    protected override void OnDisable()
    {
        DigitalTwinSettings.Changed -= SetVerticesDirty;
        base.OnDisable();
    }

    private float EdgeAlpha
    {
        get
        {
            float alpha = DigitalTwinSettings.EdgeShade switch
            {
                DigitalTwinSettings.EdgeShading.Strong => strong,
                DigitalTwinSettings.EdgeShading.Subtle => subtle,
                _ => 0f
            };
            return DigitalTwinSettings.HighContrast && alpha > 0f ? Mathf.Min(1f, alpha * 1.3f) : alpha;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        float alpha = EdgeAlpha;
        Rect r = GetPixelAdjustedRect();
        if (alpha <= 0f || r.height <= 0f)
            return;

        AddGradient(vh, r, r.yMax, r.yMax - r.height * topReach, alpha);
        AddGradient(vh, r, r.yMin, r.yMin + r.height * bottomReach, alpha);
    }

    /// <summary>
    /// From the edge to <paramref name="inner"/>, in steps along a smooth curve
    /// so the fade shows no band where it ends.
    /// </summary>
    private void AddGradient(VertexHelper vh, Rect r, float edge, float inner, float alpha)
    {
        for (int i = 0; i < Steps; i++)
        {
            float t0 = i / (float)Steps;
            float t1 = (i + 1) / (float)Steps;
            Color c0 = Shade(alpha * Falloff(t0));
            Color c1 = Shade(alpha * Falloff(t1));
            float y0 = Mathf.Lerp(edge, inner, t0);
            float y1 = Mathf.Lerp(edge, inner, t1);
            UIMesh.AddQuad(vh,
                new Vector2(r.xMin, y0), new Vector2(r.xMin, y1),
                new Vector2(r.xMax, y1), new Vector2(r.xMax, y0),
                c0, c1, c1, c0);
        }
    }

    private static float Falloff(float t)
    {
        float k = 1f - t;
        return k * k * (3f - 2f * k);
    }

    private Color Shade(float alpha)
    {
        Color c = shade * color;
        c.a *= alpha;
        return c;
    }
}
