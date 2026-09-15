using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The average of each bucket as a smooth line, Apple Health style: the
/// range's average as the headline, faint gridlines with values on the right,
/// dotted guides at the time labels, the warning and critical limits as thin
/// coloured lines, and the latest value as a ringed dot.
/// </summary>
public sealed class TelemetryLineChartGraph : TelemetryGraphView
{
    private const float CaptionHeight = 14f;
    private const float ValueHeight = 38f;
    private const float PlotTop = 66f;
    private const float PlotHeight = 118f;
    private const float AxisHeight = 22f;
    private const float Gutter = 42f;
    private const int MaxGridLines = 6;
    private static readonly float[] Guides = { 0f, 1f / 3f, 2f / 3f, 1f };

    private readonly List<Vector2> points = new();
    private readonly List<float> grid = new();
    private readonly List<float> limits = new();
    private readonly List<Color> limitColors = new();
    private readonly List<TextMeshProUGUI> yLabels = new();
    private readonly List<TextMeshProUGUI> xLabels = new();
    private UILineChartGraphic chart;
    private RectTransform plot;
    private TextMeshProUGUI average;
    private TextMeshProUGUI unit;

    public override float PreferredHeight => PlotTop + PlotHeight + AxisHeight;

    protected override void Build()
    {
        TextMeshProUGUI caption = CreateText("Caption", Rect, Style.labelSize - 2f, false, Style.muted, TextAlignmentOptions.TopLeft);
        PlaceRow(caption.rectTransform, 0f, CaptionHeight);
        caption.text = "AVERAGE";
        caption.characterSpacing = 6f;

        RectTransform valueRow = CreateRect("Average", Rect);
        PlaceRow(valueRow, CaptionHeight, ValueHeight);
        HorizontalLayoutGroup row = valueRow.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 6f;
        row.childAlignment = TextAnchor.LowerLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        average = CreateText("Value", valueRow, Style.valueSize, true, Style.ink, TextAlignmentOptions.BottomLeft);
        unit = CreateText("Unit", valueRow, Style.labelSize + 1f, false, Style.muted, TextAlignmentOptions.BottomLeft);

        plot = CreateRect("Plot", Rect);
        plot.anchorMin = Vector2.zero;
        plot.anchorMax = Vector2.one;
        plot.offsetMin = new Vector2(0f, AxisHeight);
        plot.offsetMax = new Vector2(-Gutter, -PlotTop);
        chart = plot.gameObject.AddComponent<UILineChartGraphic>();
        chart.raycastTarget = false;

        for (int i = 0; i < MaxGridLines; i++)
        {
            TextMeshProUGUI label = CreateText("Y", plot, Style.labelSize - 1f, false, Style.muted, TextAlignmentOptions.MidlineLeft);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(Gutter - 8f, 18f);
            yLabels.Add(label);
        }

        for (int i = 0; i < Guides.Length; i++)
        {
            TextAlignmentOptions alignment = i == 0 ? TextAlignmentOptions.TopLeft
                : i == Guides.Length - 1 ? TextAlignmentOptions.TopRight
                : TextAlignmentOptions.Top;
            TextMeshProUGUI label = CreateText("X", plot, Style.labelSize - 1f, false, Style.muted, alignment);
            label.rectTransform.pivot = new Vector2(i == 0 ? 0f : i == Guides.Length - 1 ? 1f : 0.5f, 1f);
            label.rectTransform.sizeDelta = new Vector2(80f, 18f);
            xLabels.Add(label);
        }
    }

    protected override void Draw(in TelemetryGraphData data)
    {
        IReadOnlyList<TelemetryHistoryBucket> buckets = data.Buckets;
        int count = buckets.Count;
        points.Clear();
        double sum = 0d;
        long samples = 0;
        float low = float.MaxValue;
        float high = float.MinValue;

        for (int i = 0; i < count; i++)
        {
            TelemetryHistoryBucket bucket = buckets[i];
            float x = (i + 0.5f) / count;
            if (!bucket.HasValue)
            {
                points.Add(new Vector2(x, float.NaN));
                continue;
            }
            float mean = bucket.Mean;
            points.Add(new Vector2(x, mean));
            sum += bucket.Sum;
            samples += bucket.SampleCount;
            low = Mathf.Min(low, mean);
            high = Mathf.Max(high, mean);
        }

        string format = string.IsNullOrWhiteSpace(data.Source.ValueFormat) ? "0.0" : data.Source.ValueFormat;
        unit.text = string.IsNullOrEmpty(data.Source.Unit) ? string.Empty : data.Source.Unit.ToUpperInvariant();

        if (samples == 0)
        {
            average.text = "—";
            chart.SetData(points, 0f, 1f, null, Guides, null, null);
            foreach (TextMeshProUGUI label in yLabels)
                label.gameObject.SetActive(false);
            PlaceTimeLabels(buckets, data.Range);
            return;
        }

        average.text = ((float)(sum / samples)).ToString(format, CultureInfo.InvariantCulture);

        // Limits near the data are drawn (and pulled into the scale); limits far
        // outside it would only squash the line flat.
        limits.Clear();
        limitColors.Clear();
        float span = Mathf.Max(high - low, Mathf.Abs(high) * 0.1f, 1e-3f);
        TelemetryLimits sourceLimits = data.Source.Limits;
        if (sourceLimits.HasHigh)
        {
            AddLimit(sourceLimits.WarningAbove, Style.warning, low, high, span);
            AddLimit(sourceLimits.CriticalAbove, Style.critical, low, high, span);
        }
        if (sourceLimits.HasLow)
        {
            AddLimit(sourceLimits.WarningBelow, Style.warning, low, high, span);
            AddLimit(sourceLimits.CriticalBelow, Style.critical, low, high, span);
        }
        foreach (float limit in limits)
        {
            low = Mathf.Min(low, limit);
            high = Mathf.Max(high, limit);
        }

        // Magnitudes read best from zero, as in the reference; values far from
        // zero (a motor at 1480 rpm) keep a tight scale so their movement shows.
        if (low >= 0f && low <= high * 0.5f)
            low = 0f;
        float step = NiceStep(Mathf.Max(high - low, 1e-3f) / 3f);
        low = Mathf.Floor(low / step) * step;
        high = Mathf.Ceil(high / step) * step;
        if (high - low < step * 0.5f)
            high = low + step;

        grid.Clear();
        for (float value = low; value <= high + step * 0.01f && grid.Count < MaxGridLines; value += step)
            grid.Add(value);

        chart.lineColor = Style.normal;
        Color area = Style.normal;
        area.a = 0.16f;
        chart.areaColor = area;
        chart.fillArea = Style.fillLineArea;
        chart.gridColor = Style.grid;
        chart.markerRingColor = Style.surface;
        chart.SetData(points, low, high, grid, Guides, limits, limitColors);

        int decimals = step >= 1f ? 0 : step >= 0.1f ? 1 : 2;
        string tickFormat = decimals == 0 ? "0" : "0." + new string('0', decimals);
        for (int i = 0; i < yLabels.Count; i++)
        {
            TextMeshProUGUI label = yLabels[i];
            bool visible = i < grid.Count;
            label.gameObject.SetActive(visible);
            if (!visible)
                continue;
            float fraction = chart.ValueFraction(grid[i]);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(1f, fraction);
            label.rectTransform.anchoredPosition = new Vector2(8f, 0f);
            label.text = grid[i].ToString(tickFormat, CultureInfo.InvariantCulture);
        }

        PlaceTimeLabels(buckets, data.Range);
    }

    private void AddLimit(float value, Color color, float low, float high, float span)
    {
        if (value < low - span * 0.6f || value > high + span * 0.6f)
            return;
        color.a *= 0.75f;
        limits.Add(value);
        limitColors.Add(color);
    }

    private void PlaceTimeLabels(IReadOnlyList<TelemetryHistoryBucket> buckets, TelemetryHistoryRange range)
    {
        if (buckets.Count == 0)
            return;
        long start = buckets[0].StartUnixMs;
        long spanMs = TelemetryHistoryRanges.SpanMilliseconds(range);
        string format = range switch
        {
            TelemetryHistoryRange.Week => "ddd",
            TelemetryHistoryRange.Month => "d MMM",
            TelemetryHistoryRange.Year => "MMM",
            _ => "HH:mm"
        };

        for (int i = 0; i < xLabels.Count; i++)
        {
            TextMeshProUGUI label = xLabels[i];
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(Guides[i], 0f);
            label.rectTransform.anchoredPosition = new Vector2(0f, -5f);
            DateTimeOffset time = DateTimeOffset.FromUnixTimeMilliseconds(start + (long)(spanMs * Guides[i])).ToLocalTime();
            label.text = time.ToString(format, CultureInfo.InvariantCulture).ToUpperInvariant();
        }
    }

    private static float NiceStep(float raw)
    {
        float exponent = Mathf.Floor(Mathf.Log10(raw));
        float magnitude = Mathf.Pow(10f, exponent);
        float fraction = raw / magnitude;
        float nice = fraction < 1.5f ? 1f : fraction < 3f ? 2f : fraction < 7f ? 5f : 10f;
        return nice * magnitude;
    }
}
