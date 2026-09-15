using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Graphs the detail panel can show. Add a kind here and a view below to extend it.</summary>
public enum TelemetryGraphKind
{
    StatusTimeline,
    LineChart
}

/// <summary>Colours and type shared by every graph in the detail panel.</summary>
[Serializable]
public sealed class TelemetryGraphStyle
{
    public TMP_FontAsset labelFont;
    public TMP_FontAsset valueFont;

    [Header("States")]
    public Color normal = new(0.08f, 0.90f, 1f, 1f);
    public Color warning = new(1f, 0.70f, 0.10f, 1f);
    public Color critical = new(1f, 0.22f, 0.20f, 1f);
    public Color unavailable = new(0.45f, 0.50f, 0.55f, 1f);

    [Tooltip("Opacity of normal-state segments, so the alarms stand out against them.")]
    [Range(0.1f, 1f)] public float normalOpacity = 0.45f;

    [Tooltip("Segments with no history at all.")]
    public Color noData = new(1f, 1f, 1f, 0.08f);

    [Header("Ink")]
    public Color ink = new(0.918f, 0.992f, 1f, 1f);
    public Color muted = new(0.918f, 0.992f, 1f, 0.58f);
    public Color grid = new(1f, 1f, 1f, 0.1f);

    [Tooltip("The fill behind the graphs (the panel's graph section), used for the ring round the latest-value marker.")]
    public Color surface = new(0.035f, 0.102f, 0.137f, 1f);

    [Tooltip("A soft fill under the line chart's line. Off matches the reference charts.")]
    public bool fillLineArea;

    [Header("Type")]
    [Min(8f)] public float labelSize = 15f;
    [Min(12f)] public float valueSize = 30f;

    public Color StateColor(StatVisualState state) => state switch
    {
        StatVisualState.Critical => critical,
        StatVisualState.Warning => warning,
        StatVisualState.Unavailable => unavailable,
        _ => normal
    };
}

/// <summary>Everything a graph needs to draw one source over one range.</summary>
public readonly struct TelemetryGraphData
{
    public readonly PerformanceStatSource Source;
    public readonly TelemetryHistoryRange Range;
    public readonly IReadOnlyList<TelemetryHistoryBucket> Buckets;

    public TelemetryGraphData(
        PerformanceStatSource source,
        TelemetryHistoryRange range,
        IReadOnlyList<TelemetryHistoryBucket> buckets)
    {
        Source = source;
        Range = range;
        Buckets = buckets;
    }
}

/// <summary>
/// Base of every detail-panel graph. A graph builds its own children once and
/// redraws from <see cref="TelemetryGraphData"/>; it keeps the last data so a
/// resize redraws without a new query.
/// </summary>
public abstract class TelemetryGraphView : MonoBehaviour
{
    private TelemetryGraphData lastData;
    private bool hasData;

    protected TelemetryGraphStyle Style { get; private set; }
    protected RectTransform Rect => (RectTransform)transform;

    /// <summary>Height the panel reserves for this graph.</summary>
    public abstract float PreferredHeight { get; }

    public static TelemetryGraphView Create(
        TelemetryGraphKind kind,
        RectTransform parent,
        TelemetryGraphStyle style)
    {
        RectTransform rect = CreateRect($"{kind} Graph", parent);
        TelemetryGraphView view = kind switch
        {
            TelemetryGraphKind.LineChart => rect.gameObject.AddComponent<TelemetryLineChartGraph>(),
            _ => rect.gameObject.AddComponent<TelemetryStatusTimelineGraph>()
        };
        view.Style = style ?? new TelemetryGraphStyle();

        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = view.PreferredHeight;
        layout.preferredHeight = view.PreferredHeight;
        view.Build();
        return view;
    }

    public void Render(in TelemetryGraphData data)
    {
        lastData = data;
        hasData = data.Buckets != null && data.Source != null;
        if (hasData && Rect.rect.width > 1f)
            Draw(data);
    }

    protected abstract void Build();
    protected abstract void Draw(in TelemetryGraphData data);

    private void OnRectTransformDimensionsChange()
    {
        if (hasData && isActiveAndEnabled && Rect.rect.width > 1f)
            Draw(lastData);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    protected static RectTransform CreateRect(string name, RectTransform parent)
    {
        GameObject gameObject = new(name, typeof(RectTransform));
        gameObject.layer = parent.gameObject.layer;
        RectTransform rect = (RectTransform)gameObject.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    /// <summary>Stretches across the width, <paramref name="top"/> pixels below the top edge.</summary>
    protected static void PlaceRow(RectTransform rect, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(0f, -(top + height));
        rect.offsetMax = new Vector2(0f, -top);
    }

    protected static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    protected TextMeshProUGUI CreateText(
        string name,
        RectTransform parent,
        float size,
        bool isValue,
        Color color,
        TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRect(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        TMP_FontAsset font = isValue ? Style.valueFont : Style.labelFont;
        if (font != null)
            text.font = font;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.characterSpacing = isValue ? 1f : 2f;
        text.raycastTarget = false;
        return text;
    }

    protected static string FormatDuration(float seconds)
    {
        if (seconds < 7200f)
            return $"{Mathf.RoundToInt(seconds / 60f)} min";
        if (seconds < 172800f)
            return $"{(seconds / 3600f).ToString("0.#", CultureInfo.InvariantCulture)} h";
        return $"{(seconds / 86400f).ToString("0.#", CultureInfo.InvariantCulture)} d";
    }

    protected static string StartLabel(TelemetryHistoryRange range) => range switch
    {
        TelemetryHistoryRange.Hour => "60 min ago",
        TelemetryHistoryRange.Day => "24 h ago",
        TelemetryHistoryRange.Week => "7 days ago",
        TelemetryHistoryRange.Month => "30 days ago",
        TelemetryHistoryRange.Year => "1 year ago",
        _ => string.Empty
    };
}
