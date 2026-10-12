using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A single-series line chart in the Apple Health manner: faint solid
/// gridlines and vertical guides, a smooth line that never overshoots its
/// data, an optional soft fill, limit lines, and the latest value marked by a
/// ringed dot with a thin line down to the axis.
///
/// With alarm bands set, the stretch beyond each limit is faintly shaded in
/// its tone, and the line takes the tone of the band it is in, changing
/// colour exactly where it crosses a limit. The line can carry a soft glow in
/// its own colour, under it; it is off by default, as a crisp line reads better.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UILineChartGraphic : MaskableGraphic
{
    public Color lineColor = new(0.08f, 0.9f, 1f, 1f);
    public Color areaColor = new(0.08f, 0.9f, 1f, 0.16f);
    public Color gridColor = new(1f, 1f, 1f, 0.1f);
    public Color guideColor = new(1f, 1f, 1f, 0.08f);
    public Color nowLineColor = new(1f, 1f, 1f, 0.3f);
    public Color markerRingColor = new(0.06f, 0.12f, 0.16f, 1f);
    public float lineWidth = 2.5f;
    public float markerRadius = 5.5f;
    public float ringWidth = 2.5f;
    public bool fillArea = true;
    [Tooltip("The glow under the line: its width beyond the line's, and its strength.")]
    public float glowWidth = 10f;
    [Range(0f, 1f)] public float glowStrength;
    [Tooltip("Strength of the shading beyond each limit, at the limit; it fades out towards the edge.")]
    [Range(0f, 0.3f)] public float bandStrength = 0.06f;

    private const int CurveSteps = 6;

    private readonly List<Vector2> points = new();
    private readonly List<float> gridValues = new();
    private readonly List<float> guideFractions = new();
    private readonly List<float> limitValues = new();
    private readonly List<Color> limitColors = new();
    private readonly List<Vector2> run = new();
    private readonly List<Vector2> curve = new();
    private readonly List<Band> bands = new();
    private readonly List<float> crossings = new();

    /// <summary>Values beyond <see cref="Limit"/> (above it, or below it) are in this band's tone.</summary>
    private readonly struct Band
    {
        public readonly float Limit;
        public readonly bool Above;
        public readonly Color Tone;

        public Band(float limit, bool above, Color tone)
        {
            Limit = limit;
            Above = above;
            Tone = tone;
        }
    }
    // Reused by MonotoneCurve. The mesh is rebuilt whenever the panel resizes,
    // so the smoothing must not allocate a pair of arrays each time.
    private float[] slopeBuffer = System.Array.Empty<float>();
    private float[] tangentBuffer = System.Array.Empty<float>();
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

    /// <summary>
    /// The alarm bands: from the warning limit on, then from the critical one.
    /// NaN leaves a band out. Call after <see cref="SetData"/>.
    /// </summary>
    public void SetBands(float warningAbove, float criticalAbove, float warningBelow, float criticalBelow,
        Color warning, Color critical)
    {
        bands.Clear();
        // Order matters: a value past the critical limit is critical even
        // though it is past the warning limit too.
        AddBand(warningAbove, true, warning);
        AddBand(criticalAbove, true, critical);
        AddBand(warningBelow, false, warning);
        AddBand(criticalBelow, false, critical);
        SetVerticesDirty();
    }

    private void AddBand(float limit, bool above, Color tone)
    {
        if (!float.IsNaN(limit))
            bands.Add(new Band(limit, above, tone));
    }

    /// <summary>The line's colour at a value: its most severe band's tone, else the line colour.</summary>
    private Color ColorAt(float value)
    {
        // Bands are added warning first, so a critical band it is also in wins.
        Color result = lineColor;
        foreach (Band band in bands)
        {
            if (band.Above ? value >= band.Limit : value <= band.Limit)
                result = band.Tone;
        }
        return result;
    }

    /// <summary>Where a value sits, as a fraction of the height (0 bottom, 1 top).</summary>
    public float ValueFraction(float value) => (value - minimum) / (maximum - minimum);

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        if (rect.width < 2f || rect.height < 2f)
            return;

        foreach (float value in gridValues)
        {
            float y = rect.yMin + ValueFraction(value) * rect.height;
            UIMesh.AddRect(vh, new Vector2(rect.xMin, y - 0.5f), new Vector2(rect.xMax, y + 0.5f), gridColor * color);
        }

        foreach (float fraction in guideFractions)
        {
            float x = rect.xMin + fraction * rect.width;
            UIMesh.AddRect(vh, new Vector2(x - 0.5f, rect.yMin), new Vector2(x + 0.5f, rect.yMax), guideColor * color);
        }

        // The bands beyond the limits, shaded from the limit outwards.
        foreach (Band band in bands)
        {
            float y = Mathf.Clamp(rect.yMin + ValueFraction(band.Limit) * rect.height, rect.yMin, rect.yMax);
            float edge = band.Above ? rect.yMax : rect.yMin;
            if (Mathf.Abs(edge - y) < 0.5f)
                continue;
            Color strong = band.Tone * color;
            strong.a *= bandStrength;
            Color faint = strong;
            faint.a = 0f;
            // Strongest at the limit, fading outwards; the critical band adds to the warning one.
            UIMesh.AddQuad(vh,
                new Vector2(rect.xMin, y), new Vector2(rect.xMin, edge),
                new Vector2(rect.xMax, edge), new Vector2(rect.xMax, y),
                strong, faint, faint, strong);
        }

        // Fill first so the limit lines and the curve sit on top of it.
        if (fillArea)
            ForEachRun(vh, rect, RunPass.Area);

        for (int i = 0; i < limitValues.Count; i++)
        {
            float y = rect.yMin + ValueFraction(limitValues[i]) * rect.height;
            if (y < rect.yMin - 0.5f || y > rect.yMax + 0.5f)
                continue;
            // A thin line in the limit's tone, so it reads as a limit and not a gridline.
            UIMesh.AddRect(vh, new Vector2(rect.xMin, y - 0.5f), new Vector2(rect.xMax, y + 0.5f), limitColors[i] * color);
        }

        // The glow under the whole line, then the line over it.
        if (glowStrength > 0f)
            ForEachRun(vh, rect, RunPass.Glow);
        ForEachRun(vh, rect, RunPass.Line);

        if (TryGetLastPoint(out Vector2 last))
        {
            Vector2 marker = ToLocal(last, rect);
            Color tone = ColorAt(last.y);
            UIMesh.AddRect(vh, new Vector2(marker.x - 0.5f, rect.yMin), new Vector2(marker.x + 0.5f, marker.y), nowLineColor * color);
            // A halo with the glow, so the latest reading reads first.
            if (glowStrength > 0f)
            {
                Color halo = tone * color;
                halo.a *= glowStrength * 1.4f;
                UIMesh.AddDisc(vh, marker, markerRadius + ringWidth + 6f, halo);
            }
            UIMesh.AddDisc(vh, marker, markerRadius + ringWidth, markerRingColor * color);
            UIMesh.AddDisc(vh, marker, markerRadius, tone * color);
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

    private enum RunPass
    {
        Area,
        Glow,
        Line
    }

    private Vector2 ToLocal(Vector2 data, Rect rect) => new(
        rect.xMin + data.x * rect.width,
        rect.yMin + ValueFraction(data.y) * rect.height);

    /// <summary>
    /// Draws each unbroken stretch of the line, smoothed. Takes the pass as a
    /// value rather than a callback: the callback form allocated a closure for
    /// the projection and another for the body on every mesh rebuild.
    /// </summary>
    private void ForEachRun(VertexHelper vh, Rect rect, RunPass pass)
    {
        run.Clear();
        for (int i = 0; i <= points.Count; i++)
        {
            bool gap = i == points.Count || float.IsNaN(points[i].y);
            if (!gap)
            {
                run.Add(ToLocal(points[i], rect));
                continue;
            }
            if (run.Count > 0)
            {
                MonotoneCurve(run, curve);
                if (pass == RunPass.Area)
                    AddArea(vh, curve, rect.yMin);
                else if (pass == RunPass.Glow)
                    AddGlowRibbon(vh, curve, rect);
                else
                    AddLineRun(vh, curve, rect);
                run.Clear();
            }
        }
    }

    /// <summary>
    /// One unbroken stretch of the line, or its glow. Each piece takes the
    /// colour of its band; a piece that crosses a limit is split where it
    /// crosses, so the colour changes exactly at the limit line.
    /// </summary>
    private void AddLineRun(VertexHelper vh, List<Vector2> r, Rect rect)
    {
        float width = lineWidth;
        const float alpha = 1f;
        if (r.Count == 1)
        {
            float value = ToValue(r[0].y, rect);
            UIMesh.AddDisc(vh, r[0], width * 0.75f, Tint(ColorAt(value), alpha));
            return;
        }

        for (int i = 0; i < r.Count - 1; i++)
        {
            Vector2 a = r[i];
            Vector2 b = r[i + 1];
            float va = ToValue(a.y, rect);
            float vb = ToValue(b.y, rect);

            crossings.Clear();
            foreach (Band band in bands)
            {
                if ((va - band.Limit) * (vb - band.Limit) < 0f)
                    crossings.Add((band.Limit - va) / (vb - va));
            }
            if (crossings.Count == 0)
            {
                UIMesh.AddLine(vh, a, b, width, Tint(ColorAt((va + vb) * 0.5f), alpha));
                continue;
            }

            crossings.Sort();
            float from = 0f;
            for (int c = 0; c <= crossings.Count; c++)
            {
                float to = c < crossings.Count ? crossings[c] : 1f;
                if (to - from > 1e-4f)
                {
                    float mid = Mathf.Lerp(va, vb, (from + to) * 0.5f);
                    UIMesh.AddLine(vh, Vector2.Lerp(a, b, from), Vector2.Lerp(a, b, to), width, Tint(ColorAt(mid), alpha));
                }
                from = to;
            }
        }
    }

    /// <summary>
    /// The glow: one continuous strip along the line, brightest on it and
    /// fading to nothing at its edges. Built as one strip rather than a quad
    /// per piece, so where pieces meet the glow does not double up into blots.
    /// Its colour follows the line's band at each point.
    /// </summary>
    private void AddGlowRibbon(VertexHelper vh, List<Vector2> r, Rect rect)
    {
        if (r.Count < 2)
            return;
        float half = (lineWidth + glowWidth) * 0.5f;
        int start = vh.currentVertCount;
        for (int i = 0; i < r.Count; i++)
        {
            Vector2 before = r[Mathf.Max(i - 1, 0)];
            Vector2 after = r[Mathf.Min(i + 1, r.Count - 1)];
            Vector2 direction = (after - before).normalized;
            Vector2 normal = new(-direction.y, direction.x);
            // Keep the strip's width across sharp turns, within reason.
            Vector2 offset = normal * half;
            Color centre = Tint(ColorAt(ToValue(r[i].y, rect)), glowStrength);
            Color edge = centre;
            edge.a = 0f;
            vh.AddVert(r[i] + offset, edge, Vector4.zero);
            vh.AddVert(r[i], centre, Vector4.zero);
            vh.AddVert(r[i] - offset, edge, Vector4.zero);
        }
        for (int i = 0; i < r.Count - 1; i++)
        {
            int a = start + i * 3;
            int b = a + 3;
            vh.AddTriangle(a, b, a + 1);
            vh.AddTriangle(a + 1, b, b + 1);
            vh.AddTriangle(a + 1, b + 1, a + 2);
            vh.AddTriangle(a + 2, b + 1, b + 2);
        }
    }

    private Color Tint(Color c, float alpha)
    {
        c *= color;
        c.a *= alpha;
        return c;
    }

    private float ToValue(float localY, Rect rect) =>
        minimum + (localY - rect.yMin) / Mathf.Max(rect.height, 1e-4f) * (maximum - minimum);

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
    private void MonotoneCurve(List<Vector2> p, List<Vector2> result)
    {
        result.Clear();
        int n = p.Count;
        if (n < 3)
        {
            result.AddRange(p);
            return;
        }

        if (slopeBuffer.Length < n - 1)
            slopeBuffer = new float[n - 1];
        if (tangentBuffer.Length < n)
            tangentBuffer = new float[n];
        float[] slopes = slopeBuffer;
        float[] tangents = tangentBuffer;
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
