using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Up to two headline figures side by side, each in half of the width and left
/// aligned, so the second starts at the centre of the panel. With
/// <see cref="TelemetryKeyFigureLayout.MakeRoomForLongValues"/>, a first figure
/// too long for its half pushes the second to the right, as far as the second
/// still fits, before either shrinks. Each figure is a
/// caption above a value and its unit: the unit shares the value's size and
/// baseline in a lighter weight. A long value shrinks to fit its half, then
/// drops decimals, and only as a last resort is cut short; its unit always shows.
/// </summary>
public sealed class TelemetryKeyFiguresGraph : TelemetryGraphView
{
    private const int Columns = 2;
    private const float CaptionBoxHeight = 20f;
    private const float ValueTop = 20f;
    private const float ValueHeight = 38f;
    // Keeps the first figure clear of the second when it fills its half.
    private const float ColumnGap = 12f;
    // Smallest share of the full value size a long figure shrinks to.
    private const float MinimumScale = 0.6f;
    private const float UnitSpacing = 0.2f;
    private const float UnitOpacity = 0.72f;

    private readonly List<Column> columns = new();

    private sealed class Column
    {
        public RectTransform Rect;
        public TextMeshProUGUI Caption;
        public TextMeshProUGUI Value;
        public TextMeshProUGUI Unit;

        // What the column shows: a number with its decimals, or text (a dash, OVERDUE).
        public double Number;
        public int Decimals;
        public string Text;
        public string UnitText;
    }

    public override float PreferredHeight => ValueTop + ValueHeight;

    protected override void Build()
    {
        for (int i = 0; i < Columns; i++)
        {
            // Placed from the left on every draw, once the split is known.
            Column column = new() { Rect = CreateRect($"Figure {i + 1}", Rect) };
            column.Rect.anchorMin = Vector2.zero;
            column.Rect.anchorMax = new Vector2(0f, 1f);
            column.Rect.pivot = new Vector2(0f, 0.5f);

            column.Caption = CreateText("Caption", column.Rect, Style.labelSize - 2f, false, Style.muted, TextAlignmentOptions.TopLeft);
            // Taller than the caption row: a truncated line must fit its box
            // or TextMesh Pro hides it entirely. Top aligned, so it reads the same.
            PlaceRow(column.Caption.rectTransform, 0f, CaptionBoxHeight);
            column.Caption.characterSpacing = 6f;
            column.Caption.overflowMode = TextOverflowModes.Ellipsis;

            column.Value = CreateText("Value", column.Rect, Style.valueSize, true, Style.ink, TextAlignmentOptions.BottomLeft);
            column.Unit = CreateText("Unit", column.Rect, Style.valueSize, false, UnitColor, TextAlignmentOptions.BottomLeft);
            foreach (TextMeshProUGUI text in new[] { column.Value, column.Unit })
            {
                RectTransform rect = text.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
            }

            columns.Add(column);
        }
    }

    private Color UnitColor
    {
        get
        {
            Color color = Style.ink;
            color.a *= UnitOpacity;
            return color;
        }
    }

    protected override void Draw(in TelemetryGraphData data)
    {
        int shown = 0;
        foreach (TelemetryKeyFigure figure in KeyFigures)
        {
            if (figure == TelemetryKeyFigure.None || shown >= columns.Count)
                continue;
            Show(columns[shown], figure, data);
            shown++;
        }

        for (int i = shown; i < columns.Count; i++)
            columns[i].Rect.gameObject.SetActive(false);

        float width = Mathf.Max(1f, Rect.rect.width);
        float split = width * 0.5f;
        if (shown > 1 && KeyFigureLayout == TelemetryKeyFigureLayout.MakeRoomForLongValues)
        {
            // Move the second figure right only as far as the first needs and
            // the second can spare; never left of the centre.
            float first = NaturalWidth(columns[0], 1f) + ColumnGap;
            // The second keeps room for its caption as well as its value.
            float second = Mathf.Max(
                NaturalWidth(columns[1], 1f),
                columns[1].Caption.GetPreferredValues(columns[1].Caption.text).x + 2f);
            if (first > split)
                split = Mathf.Max(split, Mathf.Min(first, width - second));
        }

        for (int i = 0; i < shown; i++)
        {
            float left = i == 0 ? 0f : split;
            float right = i == 0 && shown > 1 ? split - ColumnGap : width;
            columns[i].Rect.offsetMin = new Vector2(left, 0f);
            columns[i].Rect.offsetMax = new Vector2(Mathf.Max(left + 1f, right), 0f);
            Fit(columns[i]);
        }
    }

    private void Show(Column column, TelemetryKeyFigure figure, in TelemetryGraphData data)
    {
        column.Rect.gameObject.SetActive(true);
        column.Caption.text = Caption(figure);
        column.Value.color = Style.ink;

        string unit = string.IsNullOrEmpty(data.Source.Unit) ? string.Empty : data.Source.Unit.ToUpperInvariant();
        bool hasNumber = TryReadFigure(figure, data, out double number, out int decimals, ref unit, out string text);
        if (!hasNumber && text == null)
        {
            text = "—";
            unit = string.Empty;
        }

        column.Number = hasNumber ? number : double.NaN;
        column.Decimals = Mathf.Max(0, decimals);
        column.Text = text;
        column.UnitText = unit;
        column.Unit.text = unit;
        column.Value.text = hasNumber ? Format(number, column.Decimals) : text;
        // Time to service only reads as text once the part is overdue.
        bool overdue = figure == TelemetryKeyFigure.TimeToService && !hasNumber && text != null;
        column.Value.color = overdue ? Style.warning : Style.ink;
    }

    /// <summary>
    /// The figure's number and decimals, or its text when it is not a number
    /// (overdue). False with no text when the figure is unknown for this source.
    /// </summary>
    private bool TryReadFigure(
        TelemetryKeyFigure figure,
        in TelemetryGraphData data,
        out double number,
        out int decimals,
        ref string unit,
        out string text)
    {
        number = 0d;
        text = null;
        decimals = data.Source.PreferredDecimalPlaces;
        IReadOnlyList<TelemetryHistoryBucket> buckets = data.Buckets;

        switch (figure)
        {
            case TelemetryKeyFigure.Average:
            {
                double sum = 0d;
                long samples = 0;
                foreach (TelemetryHistoryBucket bucket in buckets)
                {
                    sum += bucket.Sum;
                    samples += bucket.SampleCount;
                }
                number = samples > 0 ? sum / samples : 0d;
                return samples > 0;
            }

            case TelemetryKeyFigure.Minimum:
            case TelemetryKeyFigure.Maximum:
            {
                bool any = false;
                bool minimum = figure == TelemetryKeyFigure.Minimum;
                foreach (TelemetryHistoryBucket bucket in buckets)
                {
                    if (!bucket.HasValue)
                        continue;
                    double value = minimum ? bucket.Min : bucket.Max;
                    number = !any ? value : minimum ? System.Math.Min(number, value) : System.Math.Max(number, value);
                    any = true;
                }
                return any;
            }

            case TelemetryKeyFigure.Latest:
                number = data.Source.CurrentValue;
                return true;

            case TelemetryKeyFigure.TimeToService:
                unit = UIText.Get("unit.hours", "H");
                decimals = 0;
                if (data.ServiceLife == null || !data.ServiceLife.TryGetHoursToService(data.Source, out double hours))
                    return false;
                if (hours <= 0d)
                {
                    text = UIText.Get("figure.overdue", "OVERDUE");
                    unit = string.Empty;
                    return false;
                }
                number = hours;
                return true;
        }

        return false;
    }

    private void Fit(Column column)
    {
        float width = Mathf.Max(1f, column.Rect.rect.width);
        double number = column.Number;
        int decimals = column.Decimals;
        string text = column.Text;
        string unit = column.UnitText;
        column.Value.overflowMode = TextOverflowModes.Overflow;

        // Shrink first; then give up decimals; the unit is never cut.
        float scale = 1f;
        for (int places = Mathf.Max(0, decimals); ; places--)
        {
            column.Value.text = double.IsNaN(number) ? text : Format(number, places);
            scale = Mathf.Clamp(width / NaturalWidth(column, 1f), MinimumScale, 1f);
            if (NaturalWidth(column, scale) <= width || places <= 0 || double.IsNaN(number))
                break;
        }

        float size = Style.valueSize * scale;
        column.Value.fontSize = size;
        column.Unit.fontSize = size;

        float gap = string.IsNullOrEmpty(unit) ? 0f : size * UnitSpacing;
        float unitWidth = string.IsNullOrEmpty(unit) ? 0f : column.Unit.GetPreferredValues(unit).x;
        float valueWidth = column.Value.GetPreferredValues(column.Value.text).x;
        float valueRoom = Mathf.Max(0f, width - gap - unitWidth);
        if (valueWidth > valueRoom)
        {
            column.Value.overflowMode = TextOverflowModes.Ellipsis;
            valueWidth = valueRoom;
        }

        // Both texts share one bottom edge and one size, so they share a baseline.
        Place(column.Value.rectTransform, 0f, valueWidth);
        Place(column.Unit.rectTransform, valueWidth + gap, unitWidth);
        column.Unit.gameObject.SetActive(unitWidth > 0f);
    }

    private float NaturalWidth(Column column, float scale)
    {
        float size = Style.valueSize * scale;
        column.Value.fontSize = size;
        column.Unit.fontSize = size;
        float width = column.Value.GetPreferredValues(column.Value.text).x;
        if (!string.IsNullOrEmpty(column.Unit.text))
            width += size * UnitSpacing + column.Unit.GetPreferredValues(column.Unit.text).x;
        return width;
    }

    private static void Place(RectTransform rect, float x, float width)
    {
        rect.anchoredPosition = new Vector2(x, -ValueTop);
        rect.sizeDelta = new Vector2(Mathf.Ceil(width) + 1f, ValueHeight);
    }

    private static string Format(double value, int decimals) =>
        value.ToString(decimals <= 0 ? "#,0" : "#,0." + new string('0', decimals), CultureInfo.InvariantCulture);

    private static string Caption(TelemetryKeyFigure figure) => figure switch
    {
        TelemetryKeyFigure.Average => UIText.Get("figure.average", "AVERAGE"),
        TelemetryKeyFigure.Minimum => UIText.Get("figure.minimum", "MINIMUM"),
        TelemetryKeyFigure.Maximum => UIText.Get("figure.maximum", "MAXIMUM"),
        TelemetryKeyFigure.Latest => UIText.Get("figure.latest", "LATEST"),
        TelemetryKeyFigure.TimeToService => UIText.Get("figure.time_to_service", "TIME TO SERVICE"),
        _ => string.Empty
    };
}
