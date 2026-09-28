using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A part's service history as columns in one mesh: the hours it ran before
/// each past service, then the current interval, drawn as its estimate
/// outlined and hatched with the hours run so far filled in. A dotted line
/// marks the plan. Past intervals that ended well short of the plan are drawn
/// in the warning colour.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIServiceHistoryGraphic : MaskableGraphic
{
    [Tooltip("A past interval shorter than this share of the plan is drawn as a warning.")]
    [SerializeField, Range(0f, 1f)] private float shortShare = 0.9f;

    [SerializeField, Min(4f)] private float maximumColumnWidth = 56f;
    [SerializeField] private Color planColor = new(1f, 1f, 1f, 0.55f);

    private readonly List<float> past = new();
    private float plan = 1f;
    private float estimate = 1f;
    private float run;
    private Color pastColor = Color.cyan;
    private Color shortColor = Color.yellow;
    private Color currentColor = Color.cyan;

    /// <summary>Columns: every past interval, then the current one.</summary>
    public int ColumnCount => past.Count + 1;

    /// <summary>The value at the top of the chart; heights are shares of it.</summary>
    public float Ceiling => Mathf.Max(1f, Mathf.Max(plan, estimate, run, Max(past)) * 1.18f);

    public void SetValues(IReadOnlyList<float> pastIntervals, float planHours, float estimateHours, float runHours,
        Color pastFill, Color shortFill, Color current)
    {
        bool same = pastIntervals != null && pastIntervals.Count == past.Count &&
                    Mathf.Approximately(plan, planHours) && Mathf.Approximately(estimate, estimateHours) &&
                    Mathf.Approximately(run, runHours) && pastColor == pastFill && shortColor == shortFill &&
                    currentColor == current;
        for (int i = 0; same && i < past.Count; i++)
            same = Mathf.Approximately(past[i], pastIntervals[i]);
        if (same)
            return;

        past.Clear();
        if (pastIntervals != null)
        {
            foreach (float value in pastIntervals)
                past.Add(Mathf.Max(0f, value));
        }
        plan = Mathf.Max(0f, planHours);
        estimate = Mathf.Max(0f, estimateHours);
        run = Mathf.Max(0f, runHours);
        pastColor = pastFill;
        shortColor = shortFill;
        currentColor = current;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width < 4f || r.height < 4f)
            return;

        float scale = r.height / Ceiling;
        int columns = ColumnCount;
        float columnWidth = r.width / columns;
        float half = Mathf.Min(maximumColumnWidth, columnWidth * 0.55f) * 0.5f;

        for (int i = 0; i < past.Count; i++)
        {
            float centre = r.xMin + columnWidth * (i + 0.5f);
            Color fill = plan > 0f && past[i] < plan * shortShare ? shortColor : pastColor;
            UIMesh.AddRect(vh, new Vector2(centre - half, r.yMin), new Vector2(centre + half, r.yMin + past[i] * scale), fill * color);
        }

        // The current interval: the estimate hatched and outlined, the hours
        // run so far filled in, past the outline once overdue.
        float x = r.xMin + columnWidth * (columns - 0.5f);
        float top = r.yMin + estimate * scale;
        float runTop = r.yMin + run * scale;
        Color edge = currentColor * color;
        Color hatch = new Color(currentColor.r, currentColor.g, currentColor.b, currentColor.a * 0.45f) * color;
        if (runTop < top)
            UIServiceBarGraphic.AddHatch(vh, x - half, x + half, runTop, top, hatch);
        UIMesh.AddRect(vh, new Vector2(x - half, r.yMin), new Vector2(x + half, runTop),
            new Color(currentColor.r, currentColor.g, currentColor.b, currentColor.a * 0.85f) * color);
        const float Line = 2f;
        UIMesh.AddRect(vh, new Vector2(x - half, top - Line), new Vector2(x + half, top), edge);
        UIMesh.AddRect(vh, new Vector2(x - half, r.yMin), new Vector2(x - half + Line, top), edge);
        UIMesh.AddRect(vh, new Vector2(x + half - Line, r.yMin), new Vector2(x + half, top), edge);

        // The plan, dotted across every column.
        if (plan > 0f)
        {
            float y = r.yMin + plan * scale;
            for (float dash = r.xMin; dash < r.xMax; dash += 9f)
                UIMesh.AddRect(vh, new Vector2(dash, y - 1f), new Vector2(Mathf.Min(dash + 5f, r.xMax), y + 1f), planColor * color);
        }

        // The baseline.
        UIMesh.AddRect(vh, new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin + 1.5f), new Color(1f, 1f, 1f, 0.2f) * color);
    }

    private static float Max(List<float> values)
    {
        float max = 0f;
        foreach (float value in values)
            max = Mathf.Max(max, value);
        return max;
    }
}
