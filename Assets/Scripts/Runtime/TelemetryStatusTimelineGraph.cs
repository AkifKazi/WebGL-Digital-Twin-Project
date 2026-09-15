using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// When the source was normal, in warning and in critical over the range: one
/// segment per bucket coloured by the worst state it reached, and the total
/// time in each alarm state underneath.
/// </summary>
public sealed class TelemetryStatusTimelineGraph : TelemetryGraphView
{
    private const float StripHeight = 22f;
    private const float TimeTop = 30f;
    private const float TimeHeight = 16f;
    private const float LegendTop = 56f;
    private const float LegendHeight = 20f;

    private readonly List<Color> colors = new();
    private UIStateStripGraphic strip;
    private TextMeshProUGUI startLabel;
    private TextMeshProUGUI criticalValue;
    private TextMeshProUGUI warningValue;

    public override float PreferredHeight => LegendTop + LegendHeight;

    protected override void Build()
    {
        RectTransform stripRect = CreateRect("Strip", Rect);
        PlaceRow(stripRect, 0f, StripHeight);
        strip = stripRect.gameObject.AddComponent<UIStateStripGraphic>();
        strip.raycastTarget = false;

        RectTransform time = CreateRect("Time", Rect);
        PlaceRow(time, TimeTop, TimeHeight);
        startLabel = CreateText("Start", time, Style.labelSize, false, Style.muted, TextAlignmentOptions.MidlineLeft);
        Stretch(startLabel.rectTransform);
        TextMeshProUGUI end = CreateText("End", time, Style.labelSize, false, Style.muted, TextAlignmentOptions.MidlineRight);
        Stretch(end.rectTransform);
        end.text = "now";

        RectTransform legend = CreateRect("Legend", Rect);
        PlaceRow(legend, LegendTop, LegendHeight);
        HorizontalLayoutGroup row = legend.gameObject.AddComponent<HorizontalLayoutGroup>();
        ConfigureRow(row, 22f);
        criticalValue = CreateLegendItem(legend, "critical", Style.critical);
        warningValue = CreateLegendItem(legend, "warning", Style.warning);
    }

    private TextMeshProUGUI CreateLegendItem(RectTransform parent, string label, Color swatchColor)
    {
        RectTransform item = CreateRect(label, parent);
        ConfigureRow(item.gameObject.AddComponent<HorizontalLayoutGroup>(), 7f);

        RectTransform swatch = CreateRect("Swatch", item);
        Image image = swatch.gameObject.AddComponent<Image>();
        image.color = swatchColor;
        image.raycastTarget = false;
        LayoutElement size = swatch.gameObject.AddComponent<LayoutElement>();
        size.minWidth = size.preferredWidth = 12f;
        size.minHeight = size.preferredHeight = 12f;

        CreateText("Label", item, Style.labelSize, false, Style.muted, TextAlignmentOptions.MidlineLeft).text = label;
        return CreateText("Value", item, Style.labelSize, true, Style.ink, TextAlignmentOptions.MidlineLeft);
    }

    private static void ConfigureRow(HorizontalLayoutGroup row, float spacing)
    {
        row.spacing = spacing;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
    }

    protected override void Draw(in TelemetryGraphData data)
    {
        colors.Clear();
        float critical = 0f;
        float warning = 0f;

        foreach (TelemetryHistoryBucket bucket in data.Buckets)
        {
            critical += bucket.CriticalSeconds;
            warning += bucket.WarningSeconds;

            if (!bucket.HasData)
            {
                colors.Add(Style.noData);
                continue;
            }

            StatVisualState state = bucket.WorstState;
            Color color = Style.StateColor(state);
            if (state == StatVisualState.Normal)
                color.a *= Style.normalOpacity;
            else if (state == StatVisualState.Unavailable)
                color.a *= 0.6f;
            colors.Add(color);
        }

        strip.SetSegments(colors);
        startLabel.text = StartLabel(data.Range);
        criticalValue.text = FormatDuration(critical);
        warningValue.text = FormatDuration(warning);
    }
}
