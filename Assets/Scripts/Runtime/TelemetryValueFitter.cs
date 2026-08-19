using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    [SerializeField, Min(0f), Tooltip("Clear gap maintained between the value and its unit.")]
    private float spacing = 10f;
    [SerializeField, Range(1f, 1.2f)] private float maximumCardExpansion = 1.2f;

    [Header("Motion stability")]
    [SerializeField, Min(0f), Tooltip("Width changes smaller than this are absorbed instead of moving the unit.")]
    private float widthDeadZone = 3f;
    [SerializeField, Min(0f), Tooltip("Extra reserved width that prevents tiny value changes from shifting the unit.")]
    private float valueWidthBuffer = 2f;
    [SerializeField, Min(0.01f), Tooltip("Time used to smoothly animate meaningful size and position changes.")]
    private float smoothTime = 0.2f;
    [SerializeField, Min(0f), Tooltip("How long spare value width is retained before it may contract.")]
    private float widthReleaseDelay = 8f;
    [SerializeField, Min(0f), Tooltip("How long a smaller font remains stable before returning toward its full size.")]
    private float fontRecoveryDelay = 3f;

    private TMP_Text valueText;
    private TMP_Text unitText;
    private RectTransform availableArea;
    private HorizontalLayoutGroup rowLayout;
    private LayoutElement valueLayout;
    private float baseValueSize;
    private float baseUnitSize;
    private float baseAvailableWidth;
    private float targetValueSize;
    private float targetUnitSize;
    private float valueSizeVelocity;
    private float unitSizeVelocity;
    private float reservedValueWidth;
    private float targetReservedValueWidth;
    private float reservedWidthVelocity;
    private float lastWidthIncreaseTime;
    private float lastFontConstraintTime;
    private bool bound;

    public float RequestedCardExpansion { get; private set; } = 1f;

    public void Bind(TMP_Text value, TMP_Text unit, RectTransform area)
    {
        valueText = value;
        unitText = unit;
        availableArea = area;
        if (valueText == null || unitText == null || availableArea == null)
            return;

        baseValueSize = valueText.fontSize;
        baseUnitSize = unitText.fontSize;
        targetValueSize = baseValueSize;
        targetUnitSize = baseUnitSize;
        baseAvailableWidth = availableArea.rect.width > 1f ? availableArea.rect.width : 0f;

        rowLayout = availableArea.GetComponent<HorizontalLayoutGroup>();
        if (rowLayout != null)
            rowLayout.spacing = spacing;

        valueLayout = valueText.GetComponent<LayoutElement>();
        if (valueLayout == null)
            valueLayout = valueText.gameObject.AddComponent<LayoutElement>();

        valueText.ForceMeshUpdate();
        reservedValueWidth = Mathf.Max(0f, valueText.preferredWidth + valueWidthBuffer);
        targetReservedValueWidth = reservedValueWidth;
        valueLayout.preferredWidth = reservedValueWidth;
        lastWidthIncreaseTime = Time.unscaledTime;
        bound = true;
    }

    public void Fit(PerformanceStatSource source, bool accurateUnitCasing)
    {
        if (!bound || source == null)
            return;

        valueText.text = source.FormattedValue;
        unitText.text = accurateUnitCasing ? source.Unit : source.Unit.ToUpperInvariant();

        float currentValueSize = valueText.fontSize;
        float currentUnitSize = unitText.fontSize;
        CalculateTargets(source, out float calculatedValueSize, out float calculatedUnitSize,
            out float calculatedExpansion);

        // Calculation temporarily changes TMP properties. Restore the visible state so
        // the user only sees the smoothed transition performed in LateUpdate.
        valueText.fontSize = currentValueSize;
        unitText.fontSize = currentUnitSize;
        ForceLayout();

        bool needsMoreRoom = calculatedValueSize < targetValueSize - 0.01f ||
                             calculatedUnitSize < targetUnitSize - 0.01f;
        if (needsMoreRoom)
        {
            targetValueSize = calculatedValueSize;
            targetUnitSize = calculatedUnitSize;
            lastFontConstraintTime = Time.unscaledTime;
        }
        else if (Time.unscaledTime - lastFontConstraintTime >= fontRecoveryDelay)
        {
            targetValueSize = calculatedValueSize;
            targetUnitSize = calculatedUnitSize;
        }

        RequestedCardExpansion = calculatedExpansion;
        UpdateReservedValueWidth(calculatedValueSize);
    }

    private void LateUpdate()
    {
        if (!bound)
            return;

        float deltaTime = Time.unscaledDeltaTime;
        valueText.fontSize = Mathf.SmoothDamp(valueText.fontSize, targetValueSize,
            ref valueSizeVelocity, smoothTime, Mathf.Infinity, deltaTime);
        unitText.fontSize = Mathf.SmoothDamp(unitText.fontSize, targetUnitSize,
            ref unitSizeVelocity, smoothTime, Mathf.Infinity, deltaTime);
        reservedValueWidth = Mathf.SmoothDamp(reservedValueWidth, targetReservedValueWidth,
            ref reservedWidthVelocity, smoothTime, Mathf.Infinity, deltaTime);
        valueLayout.preferredWidth = reservedValueWidth;
    }

    private void CalculateTargets(
        PerformanceStatSource source,
        out float calculatedValueSize,
        out float calculatedUnitSize,
        out float calculatedExpansion)
    {
        valueText.fontSize = baseValueSize;
        unitText.fontSize = baseUnitSize;
        valueText.text = source.FormattedValue;
        calculatedExpansion = 1f;
        ForceLayout();
        CaptureBaseWidth();

        if (!Fits() && reduceUnitSize)
            ShrinkUntilFit(unitText, baseUnitSize * minimumUnitScale);
        if (!Fits() && reduceValueSize)
            ShrinkUntilFit(valueText, baseValueSize * minimumValueScale);

        if (!Fits() && requestCardExpansion && baseAvailableWidth > 1f)
            calculatedExpansion = RequiredExpansion();

        if (!FitsAtExpansion(calculatedExpansion) && reduceDecimalPlaces)
        {
            for (int decimals = source.PreferredDecimalPlaces - 1; decimals >= 0; decimals--)
            {
                valueText.text = source.FormatValue(decimals);
                ForceLayout();
                if (requestCardExpansion && baseAvailableWidth > 1f)
                    calculatedExpansion = RequiredExpansion();
                if (FitsAtExpansion(calculatedExpansion))
                    break;
            }
        }

        calculatedValueSize = valueText.fontSize;
        calculatedUnitSize = unitText.fontSize;
    }

    private void UpdateReservedValueWidth(float calculatedValueSize)
    {
        float previousSize = valueText.fontSize;
        valueText.fontSize = calculatedValueSize;
        valueText.ForceMeshUpdate();
        float requiredWidth = valueText.preferredWidth + valueWidthBuffer;
        valueText.fontSize = previousSize;
        valueText.ForceMeshUpdate();

        if (requiredWidth > targetReservedValueWidth + widthDeadZone)
        {
            targetReservedValueWidth = requiredWidth;
            lastWidthIncreaseTime = Time.unscaledTime;
        }
        else if (requiredWidth < targetReservedValueWidth - widthDeadZone &&
                 Time.unscaledTime - lastWidthIncreaseTime >= widthReleaseDelay)
        {
            targetReservedValueWidth = requiredWidth;
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

    private bool Fits() => ContentWidth() <= availableArea.rect.width;

    private bool FitsAtExpansion(float expansion) => baseAvailableWidth > 1f
        ? ContentWidth() <= baseAvailableWidth * expansion
        : Fits();

    private float RequiredExpansion() => Mathf.Clamp(
        ContentWidth() / baseAvailableWidth, 1f, maximumCardExpansion);

    private float ContentWidth() => valueText.preferredWidth + unitText.preferredWidth + spacing;

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
