using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A part's service bar in one mesh: a track, the hours run since the last
/// service as a fill, the estimated service point as a solid tick, the planned
/// one as a dotted tick, and the stretch between the two hatched. The bar is
/// as long as the later of the two points, so either can be the one at the end.
///
/// The fill brightens towards the hours run so far and carries a soft glow
/// that fades in towards its leading end. With the run bar shader, a bright
/// pulse travels along the fill into the lead.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIServiceBarGraphic : MaskableGraphic
{
    [SerializeField, Min(1f)] private float trackHeight = 6f;
    [SerializeField, Min(0f)] private float glowSize = 5f;
    [SerializeField, Range(0f, 1f)] private float glowStrength = 0.28f;
    [Tooltip("Brightness of the fill's start, as a share of its end.")]
    [SerializeField, Range(0f, 1f)] private float fillStartBrightness = 0.45f;
    [SerializeField] private Color trackColor = new(1f, 1f, 1f, 0.1f);
    [SerializeField] private Color fillColor = new(0f, 0.922f, 1f, 1f);
    [SerializeField] private Color markerColor = Color.white;
    [SerializeField] private Color planColor = new(1f, 1f, 1f, 0.7f);
    [SerializeField] private Color hatchColor = new(1f, 1f, 1f, 0.55f);

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
        Color start = new(end.r * fillStartBrightness, end.g * fillStartBrightness, end.b * fillStartBrightness, end.a);

        // UV x runs along the fill and y marks fill (1) or glow (2), plus this
        // bar's phase, for the run bar shader's travelling pulse.
        float fillMark = 1f + pulsePhase;
        float glowMark = 2f + pulsePhase;

        if (glowSize > 0f && glowStrength > 0f)
        {
            // Nothing at the start, full at the leading end: the glow fades in
            // along the bar rather than sitting over all of it.
            Color glowEnd = new(end.r, end.g, end.b, end.a * glowStrength);
            Color clear = new(end.r, end.g, end.b, 0f);
            Vector2 atStart = new(0f, glowMark);
            Vector2 atEnd = new(1f, glowMark);

            AddQuad(vh,
                new Vector2(x0, y1), new Vector2(x0, y1 + glowSize),
                new Vector2(x1, y1 + glowSize), new Vector2(x1, y1),
                clear, clear, clear, glowEnd,
                atStart, atStart, atEnd, atEnd);
            AddQuad(vh,
                new Vector2(x0, y0 - glowSize), new Vector2(x0, y0),
                new Vector2(x1, y0), new Vector2(x1, y0 - glowSize),
                clear, clear, glowEnd, clear,
                atStart, atStart, atEnd, atEnd);

            // Past the leading end, and its two corners.
            AddQuad(vh,
                new Vector2(x1, y0), new Vector2(x1, y1),
                new Vector2(x1 + glowSize, y1), new Vector2(x1 + glowSize, y0),
                glowEnd, glowEnd, clear, clear,
                atEnd, atEnd, atEnd, atEnd);
            AddQuad(vh,
                new Vector2(x1, y1), new Vector2(x1, y1 + glowSize),
                new Vector2(x1 + glowSize, y1 + glowSize), new Vector2(x1 + glowSize, y1),
                glowEnd, clear, clear, clear,
                atEnd, atEnd, atEnd, atEnd);
            AddQuad(vh,
                new Vector2(x1, y0 - glowSize), new Vector2(x1, y0),
                new Vector2(x1 + glowSize, y0), new Vector2(x1 + glowSize, y0 - glowSize),
                clear, glowEnd, clear, clear,
                atEnd, atEnd, atEnd, atEnd);
        }

        AddQuad(vh,
            new Vector2(x0, y0), new Vector2(x0, y1),
            new Vector2(x1, y1), new Vector2(x1, y0),
            start, start, end, end,
            new Vector2(0f, fillMark), new Vector2(0f, fillMark),
            new Vector2(1f, fillMark), new Vector2(1f, fillMark));
    }

    private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        Color32 ca, Color32 cb, Color32 cc, Color32 cd, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        int first = vh.currentVertCount;
        vh.AddVert(a, ca, ua);
        vh.AddVert(b, cb, ub);
        vh.AddVert(c, cc, uc);
        vh.AddVert(d, cd, ud);
        vh.AddTriangle(first, first + 1, first + 2);
        vh.AddTriangle(first, first + 2, first + 3);
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
