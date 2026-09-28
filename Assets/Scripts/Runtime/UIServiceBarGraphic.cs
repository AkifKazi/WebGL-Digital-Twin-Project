using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A part's service bar in one mesh: a track, the hours run since the last
/// service as a fill, the estimated service point as a solid tick, the planned
/// one as a dotted tick, and the stretch between the two hatched. The bar is
/// as long as the later of the two points, so either can be the one at the end.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIServiceBarGraphic : MaskableGraphic
{
    [SerializeField, Min(1f)] private float trackHeight = 8f;
    [SerializeField] private Color trackColor = new(1f, 1f, 1f, 0.1f);
    [SerializeField] private Color fillColor = new(0.08f, 0.9f, 1f, 0.55f);
    [SerializeField] private Color markerColor = Color.white;
    [SerializeField] private Color planColor = new(1f, 1f, 1f, 0.7f);
    [SerializeField] private Color hatchColor = new(1f, 1f, 1f, 0.55f);

    private float run;
    private float plan = 1f;
    private float estimate = 1f;

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
            UIMesh.AddRect(vh, new Vector2(r.xMin, y0), new Vector2(X(run), y1), fillColor * color);

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
