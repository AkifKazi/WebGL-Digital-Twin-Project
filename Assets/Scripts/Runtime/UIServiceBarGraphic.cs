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
    [SerializeField] private Color hatchColor = new(1f, 1f, 1f, 0.55f);

    private const string RunBarShader = "UI/Service Run Bar";

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

        float gapStart = X(Mathf.Min(plan, estimate));
        float gapEnd = X(Mathf.Max(plan, estimate));
        AddHatch(vh, gapStart, gapEnd, y0, y1, hatchColor * color);

        // Plan: a dotted tick. Estimate: a solid one, drawn last so it reads first.
        float planX = X(plan);
        for (float y = r.yMin; y < r.yMax; y += 5f)
            UIMesh.AddRect(vh, new Vector2(planX - 2f, y), new Vector2(planX, Mathf.Min(y + 2.5f, r.yMax)), planColor * color);

        float estimateX = X(estimate);
        UIMesh.AddRect(vh, new Vector2(estimateX - 3f, r.yMin), new Vector2(estimateX, r.yMax), markerColor * color);
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

    /// <summary>Diagonal stripes between two x positions, clipped to the band.</summary>
    public static void AddHatch(VertexHelper vh, float x0, float x1, float y0, float y1, Color color)
    {
        if (x1 - x0 < 1f)
            return;

        const float Pitch = 5f;
        const float Stripe = 2f;
        float lean = y1 - y0;
        for (float x = x0 - lean; x < x1; x += Pitch)
        {
            float bottomLeft = Mathf.Clamp(x, x0, x1);
            float bottomRight = Mathf.Clamp(x + Stripe, x0, x1);
            float topLeft = Mathf.Clamp(x + lean, x0, x1);
            float topRight = Mathf.Clamp(x + lean + Stripe, x0, x1);
            if (bottomRight - bottomLeft <= 0f && topRight - topLeft <= 0f)
                continue;
            // Same winding as UIMesh.AddRect: bottom left, top left, top right, bottom right.
            UIMesh.AddQuad(vh,
                new Vector2(bottomLeft, y0), new Vector2(topLeft, y1),
                new Vector2(topRight, y1), new Vector2(bottomRight, y0),
                color, color, color, color);
        }
    }
}
