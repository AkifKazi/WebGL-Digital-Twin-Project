using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class TelemetryValueFitter : MonoBehaviour
{
    [Header("Single stable reading")]
    [SerializeField, Min(0f)] private float valueUnitSpacing = 6f;
    [SerializeField, Min(0f), Tooltip("Reserved after the final glyph so animation and font padding can never clip the unit.")]
    private float trailingSafetyBuffer = 10f;
    [SerializeField, Range(0.4f, 1f)] private float preferredMinimumScale = 0.72f;
    [SerializeField] private bool reduceDecimalPlaces = true;
    [SerializeField, Min(0.01f)] private float recoverySmoothTime = 0.08f;
    [SerializeField, Min(0.1f)] private float widthChangeThreshold = 0.5f;

    private TMP_Text readingText;
    private TMP_Text legacyUnitText;
    private RectTransform availableArea;
    private RectTransform readingRect;
    private PerformanceStatSource source;
    private bool accurateUnitCasing;
    private float baseFontSize;
    private float targetScale = 1f;
    private float displayedScale = 1f;
    private float appliedScale = -1f;
    private float scaleVelocity;
    private float lastMeasuredWidth = -1f;
    private string displayText = string.Empty;
    private bool layoutDirty;
    private bool bound;

    public void Bind(TMP_Text value, TMP_Text unit, RectTransform area)
    {
        readingText = value;
        legacyUnitText = unit;
        availableArea = area;
        if (readingText == null || availableArea == null)
            return;

        baseFontSize = readingText.fontSize;
        readingRect = readingText.rectTransform;
        displayText = ComposeReading(
            readingText.text,
            legacyUnitText != null ? legacyUnitText.text : string.Empty);

        // A single TMP object is the robust baseline primitive. Two separately
        // measured layout children can drift or clip their final glyph.
        HorizontalLayoutGroup obsoleteLayout = availableArea.GetComponent<HorizontalLayoutGroup>();
        if (obsoleteLayout != null)
            obsoleteLayout.enabled = false;
        if (legacyUnitText != null)
            legacyUnitText.gameObject.SetActive(false);

        readingText.textWrappingMode = TextWrappingModes.NoWrap;
        readingText.overflowMode = TextOverflowModes.Overflow;
        readingText.alignment = TextAlignmentOptions.MidlineLeft;
        readingText.richText = true;

        readingRect.anchorMin = new Vector2(0f, 0.5f);
        readingRect.anchorMax = new Vector2(1f, 0.5f);
        readingRect.pivot = new Vector2(0f, 0.5f);
        readingRect.anchoredPosition = Vector2.zero;
        readingRect.sizeDelta = new Vector2(0f, 48f);

        LayoutElement obsoleteValueLayout = readingText.GetComponent<LayoutElement>();
        if (obsoleteValueLayout != null)
            obsoleteValueLayout.enabled = false;

        displayedScale = 1f;
        targetScale = 1f;
        bound = true;
        ApplyScale(1f);
    }

    public void Fit(PerformanceStatSource newSource, bool preserveUnitCasing)
    {
        if (!bound || newSource == null)
            return;

        source = newSource;
        accurateUnitCasing = preserveUnitCasing;
        RecalculateTarget();
    }

    private void LateUpdate()
    {
        if (!bound)
            return;

        float width = availableArea.rect.width;
        if (width > 1f && Mathf.Abs(width - lastMeasuredWidth) >= widthChangeThreshold)
            RecalculateTarget();

        // Shrinking is immediate: an animated oversize intermediate must never
        // clip. Recovery can be brief and smooth once more width is available.
        if (targetScale < displayedScale)
        {
            displayedScale = targetScale;
            scaleVelocity = 0f;
        }
        else
        {
            displayedScale = Mathf.SmoothDamp(
                displayedScale,
                targetScale,
                ref scaleVelocity,
                recoverySmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
        }

        if (layoutDirty || Mathf.Abs(displayedScale - appliedScale) >= 0.001f)
        {
            ApplyScale(displayedScale);
            layoutDirty = false;
        }
    }

    private void RecalculateTarget()
    {
        if (source == null)
            return;

        float rowWidth = availableArea.rect.width;
        lastMeasuredWidth = rowWidth;
        displayText = ComposeReading(source.FormattedValue, FormatUnit(source.Unit));

        // Width zero is only a transient first-frame layout state. Do not make
        // a permanent typography decision from it.
        if (rowWidth <= 1f)
        {
            targetScale = 1f;
            readingText.text = displayText;
            layoutDirty = true;
            return;
        }

        float safeWidth = Mathf.Max(1f, rowWidth - trailingSafetyBuffer);
        float requiredWidth = MeasureAtBaseSize(displayText);

        if (reduceDecimalPlaces && requiredWidth > safeWidth / preferredMinimumScale)
        {
            for (int decimals = source.PreferredDecimalPlaces - 1; decimals >= 0; decimals--)
            {
                string candidate = ComposeReading(
                    source.FormatValue(decimals),
                    FormatUnit(source.Unit));
                float candidateWidth = MeasureAtBaseSize(candidate);
                displayText = candidate;
                requiredWidth = candidateWidth;
                if (candidateWidth <= safeWidth / preferredMinimumScale)
                    break;
            }
        }

        readingText.text = displayText;
        // No lower clamp is intentional. Readability is preferred, but the
        // non-negotiable invariant is that the complete engineering unit is
        // always visible at every intermediate responsive width.
        targetScale = Mathf.Min(1f, safeWidth / Mathf.Max(1f, requiredWidth));
        layoutDirty = true;

        if (targetScale < displayedScale)
        {
            displayedScale = targetScale;
            scaleVelocity = 0f;
            ApplyScale(displayedScale);
            layoutDirty = false;
        }
    }

    private float MeasureAtBaseSize(string text)
    {
        float previousSize = readingText.fontSize;
        string previousText = readingText.text;
        readingText.fontSize = baseFontSize;
        readingText.text = text;
        readingText.ForceMeshUpdate();
        float width = readingText.preferredWidth;
        readingText.fontSize = previousSize;
        readingText.text = previousText;
        return Mathf.Max(1f, width);
    }

    private void ApplyScale(float scale)
    {
        readingText.fontSize = baseFontSize * Mathf.Max(0.01f, scale);
        readingText.text = displayText;
        readingText.ForceMeshUpdate();
        appliedScale = scale;
    }

    private string FormatUnit(string unit)
    {
        if (string.IsNullOrEmpty(unit))
            return string.Empty;
        return accurateUnitCasing ? unit : unit.ToUpperInvariant();
    }

    private string ComposeReading(string value, string unit)
    {
        if (string.IsNullOrEmpty(unit))
            return value ?? string.Empty;
        return $"{value}<space={valueUnitSpacing}>{unit}";
    }
}
