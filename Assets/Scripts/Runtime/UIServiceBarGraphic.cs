using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A part's service bar in one mesh: a track, the hours run since the last
/// service as a fill, the estimated service point as a solid tick, the planned
/// one as a dotted tick, and the stretch between the two hatched. The bar is
/// as long as the later of the two points, so either can be the one at the end.
///
/// With the run bar shader the fill is drawn per pixel: it brightens towards
/// the hours run so far, a soft round glow grows towards its leading end, and
/// a bright pulse travels along it into the lead (see ServiceRunBar.shader).
/// Without it the fill is a plain gradient.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIServiceBarGraphic : MaskableGraphic
{
    [SerializeField, Min(1f)] private float trackHeight = 6f;
    [Tooltip("How far the glow reaches beyond the fill. Its strength and falloff are the run bar material's.")]
    [SerializeField, Min(0f)] private float glowSize = 7f;
    [Tooltip("Brightness of the fill's start, as a share of its end, without the run bar shader.")]
    [SerializeField, Range(0f, 1f)] private float fillStartBrightness = 0.45f;
    [SerializeField] private Color trackColor = new(1f, 1f, 1f, 0.1f);
    [SerializeField] private Color fillColor = new(0f, 0.922f, 1f, 1f);
    [SerializeField] private Color markerColor = Color.white;
    [SerializeField] private Color planColor = new(1f, 1f, 1f, 0.7f);
    [SerializeField] private Color hatchColor = new(1f, 1f, 1f, 0.42f);

    private const string RunBarShader = "UI/Service Run Bar";
    // How far the ticks reach above and below the track.
    private const float TickReach = 5f;

    // The gap's hatching, shared with the legend's swatch: wide enough apart
    // that it stays open when the interface is scaled down.
    public const float GapHatchPitch = 7f;
    public const float GapHatchStripe = 2f;

    private static readonly Vector2[] stripeCorners = new Vector2[4];
    private static readonly Vector2[] clipped = new Vector2[8];
    private static readonly Vector2[] scratch = new Vector2[8];

    private float pulsePhase;
    private float run;
    private float plan = 1f;
    private float estimate = 1f;

    /// <summary>
    /// Where this bar's pulse is in its cycle, 0 to 1, so neighbouring bars do
    /// not pulse in step. Only drawn by the run bar shader.
    /// </summary>
    public void SetPulsePhase(float phase)
    {
        phase = Mathf.Repeat(phase, 1f);
        if (Mathf.Approximately(pulsePhase, phase))
            return;
        pulsePhase = phase;
        SetVerticesDirty();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        EnableShaderChannels();
    }

    protected override void OnCanvasHierarchyChanged()
    {
        base.OnCanvasHierarchyChanged();
        EnableShaderChannels();
    }

    /// <summary>The run bar shader reads the fill's size and glow from UV1 and UV2.</summary>
    private void EnableShaderChannels()
    {
        const AdditionalCanvasShaderChannels Needed =
            AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
        for (Canvas target = canvas; target != null; target = target.isRootCanvas ? null : target.rootCanvas)
        {
            if ((target.additionalShaderChannels & Needed) != Needed)
                target.additionalShaderChannels |= Needed;
        }
    }

    private bool DrawsPerPixel =>
        material != null && material.shader != null && material.shader.name == RunBarShader;

    /// <summary>Sets the bar. Rebuilds the mesh only when something changed.</summary>
    public void SetValues(float runHours, float planHours, float estimateHours, Color fill)
    {
        if (Mathf.Approximately(run, runHours) && Mathf.Approximately(plan, planHours) &&
            Mathf.Approximately(estimate, estimateHours) && fillColor == fill)
            return;

        run = Mathf.Max(0f, runHours);
        plan = Mathf.Max(0f, planHours);
        estimate = Mathf.Max(0f, estimateHours);
        fillColor = fill;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        float end = Mathf.Max(plan, estimate, run);
        if (r.width < 2f || end <= 0f)
            return;

        float X(float hours) => r.xMin + Mathf.Clamp01(hours / end) * r.width;
        float mid = r.center.y;
        float y0 = mid - trackHeight * 0.5f;
        float y1 = mid + trackHeight * 0.5f;

        UIMesh.AddRect(vh, new Vector2(r.xMin, y0), new Vector2(r.xMax, y1), trackColor * color);
        if (run > 0f)
            AddGlowingFill(vh, r.xMin, X(run), y0, y1);

        // The gap between plan and estimate: a bracketed, hatched segment.
        float gapStart = X(Mathf.Min(plan, estimate));
        float gapEnd = X(Mathf.Max(plan, estimate));
        if (gapEnd - gapStart >= 1f)
        {
            Color hatch = hatchColor * color;
            Color edge = new Color(hatchColor.r, hatchColor.g, hatchColor.b, hatchColor.a * 0.55f) * color;
            AddHatch(vh, gapStart, gapEnd, y0, y1, hatch, GapHatchPitch, GapHatchStripe);
            UIMesh.AddRect(vh, new Vector2(gapStart, y1), new Vector2(gapEnd, y1 + 1f), edge);
            UIMesh.AddRect(vh, new Vector2(gapStart, y0 - 1f), new Vector2(gapEnd, y0), edge);
        }

        // Ticks a little taller than the track, ending on whole pixels.
        float tickBottom = Mathf.Max(r.yMin, y0 - TickReach);
        float tickTop = Mathf.Min(r.yMax, y1 + TickReach);

        // Plan: a dotted tick. Estimate: a solid one, drawn last so it reads first.
        float planX = Mathf.Max(r.xMin + 2f, X(plan));
        for (float y = tickBottom; y < tickTop; y += 4f)
            UIMesh.AddRect(vh, new Vector2(planX - 2f, y), new Vector2(planX, Mathf.Min(y + 2f, tickTop)), planColor * color);

        float estimateX = Mathf.Max(r.xMin + 2f, X(estimate));
        UIMesh.AddRect(vh, new Vector2(estimateX - 2f, tickBottom), new Vector2(estimateX, tickTop), markerColor * color);
    }

    private void AddGlowingFill(VertexHelper vh, float x0, float x1, float y0, float y1)
    {
        if (x1 - x0 < 0.5f)
            return;

        Color end = fillColor * color;
        if (!DrawsPerPixel)
        {
            Color start = new(end.r * fillStartBrightness, end.g * fillStartBrightness, end.b * fillStartBrightness, end.a);
            UIMesh.AddQuad(vh,
                new Vector2(x0, y0), new Vector2(x0, y1),
                new Vector2(x1, y1), new Vector2(x1, y0),
                start, start, end, end);
            return;
        }

        // One quad over the fill and as far round it as the glow reaches; the
        // shader draws both. Every vertex carries its position from the fill's
        // start and centre line, the fill's size, the reach and the pulse phase.
        float length = x1 - x0;
        float half = (y1 - y0) * 0.5f;
        float mid = (y0 + y1) * 0.5f;
        float reach = Mathf.Max(glowSize, 0.5f);
        Vector2 size = new(length, half);
        Vector2 extra = new(reach, 1f + pulsePhase);

        int first = vh.currentVertCount;
        AddFillVertex(vh, x0 - reach, mid - half - reach, x0, mid, end, size, extra);
        AddFillVertex(vh, x0 - reach, mid + half + reach, x0, mid, end, size, extra);
        AddFillVertex(vh, x1 + reach, mid + half + reach, x0, mid, end, size, extra);
        AddFillVertex(vh, x1 + reach, mid - half - reach, x0, mid, end, size, extra);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
    }

    private static void AddFillVertex(VertexHelper vh, float x, float y, float startX, float midY,
        Color32 tint, Vector2 size, Vector2 extra)
    {
        vh.AddVert(new Vector3(x, y), tint, new Vector2(x - startX, y - midY), size, extra,
            Vector2.zero, Vector3.back, new Vector4(1f, 0f, 0f, -1f));
    }

    /// <summary>
    /// Diagonal stripes at 45 degrees between two x positions, clipped to the
    /// band, so every stripe keeps its slant right up to the band's edges.
    /// </summary>
    public static void AddHatch(VertexHelper vh, float x0, float x1, float y0, float y1, Color color,
        float pitch = 5f, float stripe = 1.5f)
    {
        if (x1 - x0 < 1f || y1 - y0 <= 0f)
            return;

        float lean = y1 - y0;
        // Anchored to the band's start so the stripes stand still as it grows.
        for (float x = x0 - lean; x < x1; x += pitch)
        {
            stripeCorners[0] = new Vector2(x, y0);
            stripeCorners[1] = new Vector2(x + lean, y1);
            stripeCorners[2] = new Vector2(x + lean + stripe, y1);
            stripeCorners[3] = new Vector2(x + stripe, y0);

            int count = ClipX(stripeCorners, 4, scratch, x0, true);
            count = ClipX(scratch, count, clipped, x1, false);
            if (count < 3)
                continue;

            int first = vh.currentVertCount;
            for (int i = 0; i < count; i++)
                vh.AddVert(clipped[i], color, Vector4.zero);
            for (int i = 1; i < count - 1; i++)
                vh.AddTriangle(first, first + i, first + i + 1);
        }
    }

    /// <summary>Clips a convex polygon to one side of a vertical line (Sutherland-Hodgman).</summary>
    private static int ClipX(Vector2[] polygon, int count, Vector2[] output, float edge, bool keepRight)
    {
        int written = 0;
        for (int i = 0; i < count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % count];
            bool aIn = keepRight ? a.x >= edge : a.x <= edge;
            bool bIn = keepRight ? b.x >= edge : b.x <= edge;
            if (aIn)
                output[written++] = a;
            if (aIn != bIn)
            {
                float t = (edge - a.x) / (b.x - a.x);
                output[written++] = new Vector2(edge, Mathf.Lerp(a.y, b.y, t));
            }
        }
        return written;
    }
}
