using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TelemetryValueFitter : MonoBehaviour
{
    [Header("Strategies — evaluated in this order")]
    [SerializeField] private bool reduceUnitSize = true;
    [SerializeField] private bool reduceValueSize = true;
    [SerializeField] private bool requestCardExpansion = true;
    [SerializeField] private bool reduceDecimalPlaces = true;

    [Header("Limits")]
    [SerializeField, Range(0.5f, 1f)] private float minimumUnitScale = 0.5f;
    [SerializeField, Range(0.8f, 1f)] private float minimumValueScale = 0.8f;
    [SerializeField, Min(0f)] private float spacing = 8f;
    [SerializeField, Range(1f, 1.2f)] private float maximumCardExpansion = 1.2f;

    private TMP_Text valueText;
    private TMP_Text unitText;
    private RectTransform availableArea;
    private float baseValueSize;
    private float baseUnitSize;
    private float baseAvailableWidth;

    public float RequestedCardExpansion { get; private set; } = 1f;

    public void Bind(TMP_Text value, TMP_Text unit, RectTransform area)
    {
        valueText = value;
        unitText = unit;
        availableArea = area;
        baseValueSize = value != null ? value.fontSize : 0f;
        baseUnitSize = unit != null ? unit.fontSize : 0f;
        baseAvailableWidth = area != null && area.rect.width > 1f
            ? area.rect.width
            : 0f;
    }

    public void Fit(PerformanceStatSource source, bool accurateUnitCasing)
    {
        if (source == null || valueText == null || unitText == null || availableArea == null)
            return;

        valueText.fontSize = baseValueSize;
        unitText.fontSize = baseUnitSize;
        RequestedCardExpansion = 1f;
        valueText.text = source.FormattedValue;
        unitText.text = accurateUnitCasing
            ? source.Unit
            : source.Unit.ToUpperInvariant();
        ForceLayout();
        CaptureBaseWidth();

        if (Fits())
            return;

        if (reduceUnitSize)
            ShrinkUntilFit(unitText, baseUnitSize * minimumUnitScale);

        if (!Fits() && reduceValueSize)
            ShrinkUntilFit(valueText, baseValueSize * minimumValueScale);

        if (!Fits() && requestCardExpansion && baseAvailableWidth > 1f)
        {
            RequestedCardExpansion = Mathf.Clamp(
                ContentWidth() / baseAvailableWidth,
                1f,
                maximumCardExpansion);
        }

        if (!FitsAtRequestedExpansion() && reduceDecimalPlaces)
        {
            for (int decimals = source.PreferredDecimalPlaces - 1; decimals >= 0; decimals--)
            {
                valueText.text = source.FormatValue(decimals);
                ForceLayout();
                if (requestCardExpansion && baseAvailableWidth > 1f)
                {
                    RequestedCardExpansion = Mathf.Clamp(
                        ContentWidth() / baseAvailableWidth,
                        1f,
                        maximumCardExpansion);
                }
                if (FitsAtRequestedExpansion())
                    break;
            }
        }
    }

    private void ShrinkUntilFit(TMP_Text target, float minimum)
    {
        while (!Fits() && target.fontSize > minimum + 0.1f)
        {
            target.fontSize = Mathf.Max(minimum, target.fontSize - 0.5f);
            ForceLayout();
        }
    }

    private bool Fits()
    {
        return ContentWidth() <= availableArea.rect.width;
    }

    private bool FitsAtRequestedExpansion() =>
        baseAvailableWidth > 1f
            ? ContentWidth() <= baseAvailableWidth * RequestedCardExpansion
            : Fits();

    private float ContentWidth() =>
        valueText.preferredWidth + unitText.preferredWidth + spacing;

    private void CaptureBaseWidth()
    {
        if (baseAvailableWidth <= 1f && availableArea.rect.width > 1f)
            baseAvailableWidth = availableArea.rect.width;
    }

    private void ForceLayout()
    {
        valueText.ForceMeshUpdate();
        unitText.ForceMeshUpdate();
    }
}
