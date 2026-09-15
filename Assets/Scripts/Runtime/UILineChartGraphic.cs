using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single-series line chart in the Apple Health manner: faint solid
/// gridlines, dotted vertical guides, a smooth line that never overshoots its
/// data, an optional soft fill, limit lines, and the latest value marked by a
/// ringed dot with a thin line down to the axis.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UILineChartGraphic : MaskableGraphic
{
    public Color lineColor = new(0.08f, 0.9f, 1f, 1f);
    public Color areaColor = new(0.08f, 0.9f, 1f, 0.16f);
    public Color gridColor = new(1f, 1f, 1f, 0.1f);
    public Color guideColor = new(1f, 1f, 1f, 0.16f);
    public Color nowLineColor = new(1f, 1f, 1f, 0.3f);
    public Color markerRingColor = new(0.06f, 0.12f, 0.16f, 1f);
    public float lineWidth = 3f;
    public float markerRadius = 5.5f;
    public float ringWidth = 2.5f;
    public bool fillArea = true;

    private const int CurveSteps = 6;

    private readonly List<Vector2> points = new();
    private readonly List<float> gridValues = new();
    private readonly List<float> guideFractions = new();
    private readonly List<float> limitValues = new();
    private readonly List<Color> limitColors = new();
    private readonly List<Vector2> run = new();
    private readonly List<Vector2> curve = new();
    private float minimum;
    private float maximum = 1f;

    /// <summary>
    /// Points are (fraction of the width, value); a NaN value breaks the line
    /// where a bucket has no data.
    /// </summary>
    public void SetData(
        IReadOnlyList<Vector2> dataPoints,
        float yMinimum,
        float yMaximum,
        IReadOnlyList<float> grid,
        IReadOnlyList<float> guides,
        IReadOnlyList<float> limits,
        IReadOnlyList<Color> limitLineColors)
    {
        Copy(dataPoints, points);
        Copy(grid, gridValues);
        Copy(guides, guideFractions);
        Copy(limits, limitValues);
        Copy(limitLineColors, limitColors);
        minimum = yMinimum;
        maximum = yMaximum > yMinimum ? yMaximum : yMinimum + 1f;
        SetVerticesDirty();
    }

    /// <summary>Where a value sits, as a fraction of the height (0 bottom, 1 top).</summary>
    public float ValueFraction(float value) => (value - minimum) / (maximum - minimum);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width < 2f || rect.height < 2f)
            return;

        Vector2 ToLocal(Vector2 data) => new(
            rect.xMin + data.x * rect.width,
            rect.yMin + ValueFraction(data.y) * rect.height);

        foreach (float value in gridValues)
        {
            float y = rect.yMin + ValueFraction(value) * rect.height;
            UIMesh.AddRect(vh, new Vector2(rect.xMin, y - 0.5f), new Vector2(rect.xMax, y + 0.5f), gridColor * color);
        }

        foreach (float fraction in guideFractions)
        {
            float x = rect.xMin + fraction * rect.width;
            for (float y = rect.yMin; y < rect.yMax; y += 6f)
                UIMesh.AddRect(vh, new Vector2(x - 1f, y), new Vector2(x + 1f, Mathf.Min(y + 2f, rect.yMax)), guideColor * color);
        }

        // Fill first so the limit lines and the curve sit on top of it.
        if (fillArea)
            ForEachRun(ToLocal, r => AddArea(vh, r, rect.yMin));

        for (int i = 0; i < limitValues.Count; i++)
        {
            float y = rect.yMin + ValueFraction(limitValues[i]) * rect.height;
            if (y < rect.yMin - 0.5f || y > rect.yMax + 0.5f)
                continue;
            // Dashed: a limit, not data and not a gridline.
            for (float x = rect.xMin; x < rect.xMax; x += 7f)
                UIMesh.AddRect(vh, new Vector2(x, y - 0.6f), new Vector2(Mathf.Min(x + 4f, rect.xMax), y + 0.6f), limitColors[i] * color);
        }

        ForEachRun(ToLocal, r =>
        {
            if (r.Count == 1)
            {
                UIMesh.AddDisc(vh, r[0], lineWidth * 0.75f, lineColor * color);
                return;
            }
            for (int i = 0; i < r.Count - 1; i++)
                UIMesh.AddLine(vh, r[i], r[i + 1], lineWidth, lineColor * color);
        });

        if (TryGetLastPoint(out Vector2 last))
        {
            Vector2 marker = ToLocal(last);
            UIMesh.AddRect(vh, new Vector2(marker.x - 0.5f, rect.yMin), new Vector2(marker.x + 0.5f, marker.y), nowLineColor * color);
            UIMesh.AddDisc(vh, marker, markerRadius + ringWidth, markerRingColor * color);
            UIMesh.AddDisc(vh, marker, markerRadius, lineColor * color);
        }
    }

    private void AddArea(VertexHelper vh, List<Vector2> smoothed, float baseline)
    {
        Color top = areaColor * color;
        Color bottom = top;
        bottom.a = 0f;
        for (int i = 0; i < smoothed.Count - 1; i++)
        {
            Vector2 a = smoothed[i];
            Vector2 b = smoothed[i + 1];
            UIMesh.AddQuad(vh, new Vector2(a.x, baseline), a, b, new Vector2(b.x, baseline), bottom, top, top, bottom);
        }
    }

    /// <summary>Calls <paramref name="action"/> with each unbroken stretch of the line, smoothed.</summary>
    private void ForEachRun(System.Func<Vector2, Vector2> toLocal, System.Action<List<Vector2>> action)
    {
        run.Clear();
        for (int i = 0; i <= points.Count; i++)
        {
            bool gap = i == points.Count || float.IsNaN(points[i].y);
            if (!gap)
            {
                run.Add(toLocal(points[i]));
                continue;
            }
            if (run.Count > 0)
            {
                MonotoneCurve(run, curve);
                action(curve);
                run.Clear();
            }
        }
    }

    private bool TryGetLastPoint(out Vector2 last)
    {
        for (int i = points.Count - 1; i >= 0; i--)
        {
            if (!float.IsNaN(points[i].y))
            {
                last = points[i];
                return true;
            }
        }
        last = default;
        return false;
    }

    /// <summary>
    /// Monotone cubic interpolation (Fritsch-Carlson): smooth like a spline but
    /// never rising above a peak or dipping below a trough the data does not have.
    /// </summary>
    private static void MonotoneCurve(List<Vector2> p, List<Vector2> result)
    {
        result.Clear();
        int n = p.Count;
        if (n < 3)
        {
            result.AddRange(p);
            return;
        }

        float[] slopes = new float[n - 1];
        float[] tangents = new float[n];
        for (int i = 0; i < n - 1; i++)
            slopes[i] = (p[i + 1].y - p[i].y) / Mathf.Max(1e-4f, p[i + 1].x - p[i].x);

        tangents[0] = slopes[0];
        tangents[n - 1] = slopes[n - 2];
        for (int i = 1; i < n - 1; i++)
            tangents[i] = slopes[i - 1] * slopes[i] <= 0f ? 0f : (slopes[i - 1] + slopes[i]) * 0.5f;

        for (int i = 0; i < n - 1; i++)
        {
            if (Mathf.Approximately(slopes[i], 0f))
            {
                tangents[i] = 0f;
                tangents[i + 1] = 0f;
                continue;
            }
            float a = tangents[i] / slopes[i];
            float b = tangents[i + 1] / slopes[i];
            float s = a * a + b * b;
            if (s > 9f)
            {
                float t = 3f / Mathf.Sqrt(s);
                tangents[i] = t * a * slopes[i];
                tangents[i + 1] = t * b * slopes[i];
            }
        }

        for (int i = 0; i < n - 1; i++)
        {
            float h = p[i + 1].x - p[i].x;
            for (int step = 0; step < CurveSteps; step++)
            {
                float t = step / (float)CurveSteps;
                float t2 = t * t;
                float t3 = t2 * t;
                float y = (2f * t3 - 3f * t2 + 1f) * p[i].y +
                          (t3 - 2f * t2 + t) * h * tangents[i] +
                          (-2f * t3 + 3f * t2) * p[i + 1].y +
                          (t3 - t2) * h * tangents[i + 1];
                result.Add(new Vector2(p[i].x + t * h, y));
            }
        }
        result.Add(p[n - 1]);
    }

    private static void Copy<T>(IReadOnlyList<T> source, List<T> target)
    {
        target.Clear();
        if (source == null)
            return;
        for (int i = 0; i < source.Count; i++)
            target.Add(source[i]);
    }
}
