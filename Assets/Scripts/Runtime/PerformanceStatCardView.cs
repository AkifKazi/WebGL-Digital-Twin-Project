using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum StatRailSide
{
    Left,
    Right,
    Top,
    Bottom
}

public enum StatVisualState
{
    Normal,
    Warning,
    Critical,
    Unavailable
}

[DisallowMultipleComponent]
public sealed class PerformanceStatCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public event Action<PerformanceStatCardView, bool> FocusChanged;

    [Header("Text")]
    [SerializeField] private TMP_Text metricName;
    [SerializeField] private TMP_Text valueText;
    [SerializeField] private TMP_Text unitText;

    [Header("Placement Graphics")]
    [SerializeField] private GameObject leftAccent;
    [SerializeField] private GameObject rightAccent;
    [SerializeField] private GameObject topAccent;
    [SerializeField] private GameObject bottomAccent;

    [Header("Connection Points")]
    [SerializeField] private RectTransform leftConnectionPoint;
    [SerializeField] private RectTransform rightConnectionPoint;
    [SerializeField] private RectTransform topConnectionPoint;
    [SerializeField] private RectTransform bottomConnectionPoint;

    [Header("State Graphics")]
    [SerializeField] private Image statusGlyph;
    [SerializeField] private Image glowImage;

    [Header("State Icons — Optional")]
    [SerializeField] private Sprite normalIcon;
    [SerializeField] private Sprite warningIcon;
    [SerializeField] private Sprite criticalIcon;
    [SerializeField] private Sprite unavailableIcon;

    [Header("Background")]
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Sprite normalBackground;
    [SerializeField] private Sprite warningBackground;
    [SerializeField] private Sprite criticalBackground;
    [SerializeField] private Sprite unavailableBackground;

    [Header("Tintable Accent Visuals")]
    [SerializeField] private Image leftAccentCore;
    [SerializeField] private Image leftAccentTrail;
    [SerializeField] private Image rightAccentCore;
    [SerializeField] private Image rightAccentTrail;
    [SerializeField] private Image topAccentCore;
    [SerializeField] private Image topAccentTrail;
    [SerializeField] private Image bottomAccentCore;
    [SerializeField] private Image bottomAccentTrail;

    [Header("Colours")]
    [SerializeField] private Color normalColor = new(0.08f, 0.90f, 1f, 1f);
    [SerializeField] private Color warningColor = new(1f, 0.70f, 0.10f, 1f);
    [SerializeField] private Color criticalColor = new(1f, 0.22f, 0.20f, 1f);
    [SerializeField] private Color unavailableColor = new(0.45f, 0.50f, 0.55f, 1f);

    [Header("Initial Preview")]
    [SerializeField] private StatRailSide railSide = StatRailSide.Left;
    [SerializeField] private StatVisualState visualState = StatVisualState.Normal;

    [Header("Metric Fitting")]
    [SerializeField] private TextOverflowMotion labelOverflowMotion = TextOverflowMotion.PingPong;
    [SerializeField, Tooltip("Preserve case-sensitive engineering unit symbols such as mm/s, kN, kW and MPa. Disable for an all-uppercase visual style.")]
    private bool useAccurateUnitCasing;
    [SerializeField, Range(1, 6)] private int maximumVisibleMetrics = 3;
    [SerializeField, Min(0f)] private float mobileTapFocusSeconds = 4f;
    [SerializeField, Min(0.05f)] private float focusToRestDuration = 1.5f;
    [SerializeField, Min(0f)] private float contentPaddingInsideBackground = 12f;

    private readonly List<MetricSlot> metricSlots = new();
    private readonly List<PerformanceStatSource> boundSources = new();
    private RectTransform metricTemplateRoot;
    private TMP_Text overflowBadge;
    private bool focused;
    private float tapFocusUntil;
    private Transform presentationAnchor;
    private bool fadingConnectionToRest;
    private float connectionFadeStartedAt;
    private float connectionFadeStartEmphasis;
    private float connectionEmphasis;
    private RectTransformSnapshot[] accentGeometry = Array.Empty<RectTransformSnapshot>();

    public StatRailSide RailSide => railSide;
    public StatVisualState VisualState => visualState;
    public Color StateColor => GetStateColor();
    public IReadOnlyList<PerformanceStatSource> BoundSources => boundSources;
    public Transform WorldAnchor => presentationAnchor != null
        ? presentationAnchor
        : boundSources.Count > 0 ? boundSources[0].WorldAnchor : null;
    public bool IsFocused => focused;
    public float RequestedCardExpansion => metricSlots.Count == 0
        ? 1f
        : metricSlots.Max(slot => slot.RequestedCardExpansion);

    public RectTransform ActiveConnectionPoint
    {
        get
        {
            return railSide switch
            {
                StatRailSide.Left => rightConnectionPoint,
                StatRailSide.Right => leftConnectionPoint,
                StatRailSide.Top => bottomConnectionPoint,
                StatRailSide.Bottom => topConnectionPoint,
                _ => rightConnectionPoint
            };
        }
    }

    private void Awake()
    {
        CaptureAccentGeometry();
        InitialiseMetricTemplate();
        if (backgroundImage != null)
            backgroundImage.raycastTarget = true;
        ApplySide();
        ApplyState();
        SetConnectionEmphasis(IsAlarmState() ? 1f : 0f);
    }

    private void Update()
    {
        if (tapFocusUntil > 0f && Time.unscaledTime >= tapFocusUntil)
        {
            tapFocusUntil = 0f;
            SetFocused(false);
        }

        if (IsAlarmState())
        {
            fadingConnectionToRest = false;
            SetConnectionEmphasis(1f);
        }
        else if (fadingConnectionToRest)
        {
            float progress = Mathf.Clamp01(
                (Time.unscaledTime - connectionFadeStartedAt) / focusToRestDuration);
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            SetConnectionEmphasis(Mathf.Lerp(connectionFadeStartEmphasis, 0f, eased));
            if (progress >= 1f)
                fadingConnectionToRest = false;
        }
    }

    public void SetContent(string label, string value, string unit)
    {
        if (metricName != null)
            metricName.text = string.IsNullOrEmpty(label) ? string.Empty : label.ToUpperInvariant();

        if (valueText != null)
            valueText.text = value;

        if (unitText != null)
            unitText.text = string.IsNullOrEmpty(unit) ? string.Empty : unit.ToUpperInvariant();
    }

    public void SetPresentation(TelemetryCardPresentation presentation, StatRailSide side)
    {
        if (presentation == null)
            return;

        presentationAnchor = presentation.WorldAnchor;
        maximumVisibleMetrics = presentation.MaximumVisibleMetrics;
        SetSide(side);
        SetState(presentation.VisualState);
        BindSources(presentation.Sources);
    }

    public void BindSources(IReadOnlyList<PerformanceStatSource> sources)
    {
        boundSources.Clear();
        if (sources != null)
            boundSources.AddRange(sources.Where(source => source != null));

        int visibleCount = Mathf.Min(maximumVisibleMetrics, boundSources.Count);
        EnsureMetricSlots(Mathf.Max(1, visibleCount));
        ConfigureSlotLayout(Mathf.Max(1, visibleCount));

        for (int i = 0; i < metricSlots.Count; i++)
        {
            bool visible = i < visibleCount;
            metricSlots[i].Root.gameObject.SetActive(visible);
            if (visible)
                metricSlots[i].Render(
                    boundSources[i],
                    labelOverflowMotion,
                    useAccurateUnitCasing,
                    GetStateColor());
        }

        UpdateOverflowBadge(boundSources.Count - visibleCount);
    }

    public void RefreshBoundSources()
    {
        if (boundSources.Count == 0)
            return;
        SetState(boundSources[0].VisualState);
        BindSources(boundSources.ToArray());
    }

    public void SetValue(float value, string format = "0.0")
    {
        if (valueText != null)
            valueText.text = value.ToString(format, CultureInfo.InvariantCulture);
    }

    public void SetLabel(string label)
    {
        if (metricName != null)
            metricName.text = string.IsNullOrEmpty(label) ? string.Empty : label.ToUpperInvariant();
    }

    public void SetUnit(string unit)
    {
        if (unitText != null)
            unitText.text = string.IsNullOrEmpty(unit) ? string.Empty : unit.ToUpperInvariant();
    }

    public void SetSide(StatRailSide side)
    {
        railSide = side;
        ApplySide();
        if (boundSources.Count > 0)
            BindSources(boundSources.ToArray());
    }

    public void SetState(StatVisualState state)
    {
        if (visualState == state)
            return;
        visualState = state;
        ApplyState();
    }

    private void ApplySide()
    {
        // Cards are pooled and can move between rails. Restore the prefab geometry
        // so a previous layout or presentation animation cannot leave one accent
        // thinner, shorter, rotated, or scaled differently from the others.
        RestoreAccentGeometry();

        SetActive(leftAccent, railSide == StatRailSide.Right);
        SetActive(rightAccent, railSide == StatRailSide.Left);
        SetActive(topAccent, railSide == StatRailSide.Bottom);
        SetActive(bottomAccent, railSide == StatRailSide.Top);

        SetActive(leftConnectionPoint, railSide == StatRailSide.Right);
        SetActive(rightConnectionPoint, railSide == StatRailSide.Left);
        SetActive(topConnectionPoint, railSide == StatRailSide.Bottom);
        SetActive(bottomConnectionPoint, railSide == StatRailSide.Top);
    }

    private void CaptureAccentGeometry()
    {
        var transforms = new HashSet<RectTransform>();
        AddAccentTree(leftAccent, transforms);
        AddAccentTree(rightAccent, transforms);
        AddAccentTree(topAccent, transforms);
        AddAccentTree(bottomAccent, transforms);

        accentGeometry = transforms
            .Select(rect => new RectTransformSnapshot(rect))
            .ToArray();
    }

    private static void AddAccentTree(
        GameObject accent,
        ISet<RectTransform> results)
    {
        if (accent == null)
            return;

        foreach (RectTransform rect in accent.GetComponentsInChildren<RectTransform>(true))
            results.Add(rect);
    }

    private void RestoreAccentGeometry()
    {
        foreach (RectTransformSnapshot snapshot in accentGeometry)
            snapshot.Restore();
    }

    private void ApplyState()
    {
        Color stateColor = GetStateColor();
        Sprite stateIcon = GetStateIcon();

        if (statusGlyph != null)
        {
            statusGlyph.color = stateColor;

            if (stateIcon != null)
                statusGlyph.sprite = stateIcon;
        }

        SetImageColor(leftAccentCore, stateColor);
        SetImageRgbPreserveAlpha(leftAccentTrail, stateColor);
        SetImageColor(rightAccentCore, stateColor);
        SetImageRgbPreserveAlpha(rightAccentTrail, stateColor);
        SetImageColor(topAccentCore, stateColor);
        SetImageRgbPreserveAlpha(topAccentTrail, stateColor);
        SetImageColor(bottomAccentCore, stateColor);
        SetImageRgbPreserveAlpha(bottomAccentTrail, stateColor);

        if (glowImage != null)
        {
            Color glowColor = stateColor;
            glowColor.a = visualState == StatVisualState.Unavailable ? 0.03f : 0.12f;
            glowImage.color = glowColor;
        }

        if (metricName != null)
            metricName.color = stateColor;

        foreach (MetricSlot slot in metricSlots)
            slot.SetLabelColor(stateColor);

        if (backgroundImage != null)
        {
            Sprite background = GetStateBackground();

            if (background != null)
                backgroundImage.sprite = background;

            backgroundImage.color = Color.white;
        }

        if (IsAlarmState())
            SetConnectionEmphasis(1f);
        else if (!focused && !fadingConnectionToRest)
            SetConnectionEmphasis(0f);
    }

    public void SetConnectionEmphasis(float emphasis)
    {
        emphasis = Mathf.Clamp01(emphasis);
        connectionEmphasis = emphasis;
        SetTrailAlpha(leftAccentTrail, emphasis);
        SetTrailAlpha(rightAccentTrail, emphasis);
        SetTrailAlpha(topAccentTrail, emphasis);
        SetTrailAlpha(bottomAccentTrail, emphasis);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        tapFocusUntil = 0f;
        SetFocused(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tapFocusUntil <= 0f)
            SetFocused(false);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // A mouse hover already owns focus. The timed focus is only for touch,
        // where there is no persistent hover state and no click-to-lock mode.
        if (eventData != null && eventData.pointerId < 0)
            return;

        SetFocused(true);
        tapFocusUntil = Time.unscaledTime + mobileTapFocusSeconds;
    }

    private void SetFocused(bool value)
    {
        if (focused == value)
            return;
        focused = value;
        if (focused || IsAlarmState())
        {
            fadingConnectionToRest = false;
            SetConnectionEmphasis(1f);
        }
        else
        {
            // Focus-to-rest begins immediately, but takes 1.5 seconds. There
            // is no extra delay and no click-to-lock state.
            fadingConnectionToRest = true;
            connectionFadeStartedAt = Time.unscaledTime;
            connectionFadeStartEmphasis = connectionEmphasis;
        }
        FocusChanged?.Invoke(this, focused);
    }

    private void InitialiseMetricTemplate()
    {
        metricTemplateRoot = metricName != null
            ? metricName.transform.parent?.parent as RectTransform
            : null;
        if (metricTemplateRoot == null)
            return;

        metricSlots.Clear();
        metricSlots.Add(new MetricSlot(metricTemplateRoot, metricName, valueText, unitText));
        metricSlots[0].EnsureHelpers(labelOverflowMotion);
        DisableDecorativeRaycasts();
    }

    private void DisableDecorativeRaycasts()
    {
        // The card background is the single stable pointer hit area. Child
        // labels, accents and glyphs must not compete with it as the pointer
        // moves across the card.
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = graphic == backgroundImage;
        if (backgroundImage != null)
            backgroundImage.raycastTarget = true;
    }

    private void EnsureMetricSlots(int count)
    {
        if (metricTemplateRoot == null)
            InitialiseMetricTemplate();
        if (metricTemplateRoot == null)
            return;

        while (metricSlots.Count < count)
        {
            RectTransform clone = Instantiate(metricTemplateRoot, metricTemplateRoot.parent);
            clone.name = $"Metric slot {metricSlots.Count + 1}";
            TMP_Text label = clone.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => text.name == metricName.name);
            TMP_Text value = clone.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => text.name == valueText.name);
            TMP_Text unit = clone.GetComponentsInChildren<TMP_Text>(true)
                .FirstOrDefault(text => text.name == unitText.name);
            MetricSlot slot = new(clone, label, value, unit);
            slot.EnsureHelpers(labelOverflowMotion);
            metricSlots.Add(slot);
        }
        DisableDecorativeRaycasts();
    }

    private void ConfigureSlotLayout(int count)
    {
        bool horizontal = railSide == StatRailSide.Top || railSide == StatRailSide.Bottom;
        for (int i = 0; i < count && i < metricSlots.Count; i++)
        {
            RectTransform root = metricSlots[i].Root;
            if (horizontal)
            {
                float min = i / (float)count;
                float max = (i + 1f) / count;
                root.anchorMin = new Vector2(min, 0f);
                root.anchorMax = new Vector2(max, 1f);
            }
            else
            {
                float max = 1f - i / (float)count;
                float min = 1f - (i + 1f) / count;
                root.anchorMin = new Vector2(0f, min);
                root.anchorMax = new Vector2(1f, max);
            }

            GetVisibleBackgroundInsets(out float backgroundLeft, out float backgroundRight);
            float leftInset = backgroundLeft + contentPaddingInsideBackground;
            float rightInset = backgroundRight + contentPaddingInsideBackground;
            root.offsetMin = new Vector2(leftInset, 8f);
            root.offsetMax = new Vector2(-rightInset, -8f);
        }
    }

    private void GetVisibleBackgroundInsets(out float left, out float right)
    {
        left = 24f;
        right = 24f;
        if (backgroundImage == null || backgroundImage.rectTransform == null)
            return;

        RectTransform backgroundRect = backgroundImage.rectTransform;
        // The supplied card artwork is horizontally inset inside the card
        // root. Content must use the artwork's visible rectangle, not the
        // wider root that also contains the external accent trail.
        if (backgroundRect.anchorMin.x <= 0.001f &&
            backgroundRect.anchorMax.x >= 0.999f)
        {
            left = Mathf.Max(0f, backgroundRect.offsetMin.x);
            right = Mathf.Max(0f, -backgroundRect.offsetMax.x);
        }
    }

    private void UpdateOverflowBadge(int hiddenCount)
    {
        if (hiddenCount <= 0)
        {
            if (overflowBadge != null)
                overflowBadge.gameObject.SetActive(false);
            return;
        }

        if (overflowBadge == null && metricName != null)
        {
            overflowBadge = Instantiate(metricName, transform);
            overflowBadge.name = "Hidden metric count";
            TMPOverflowScroller inheritedScroller = overflowBadge.GetComponent<TMPOverflowScroller>();
            if (inheritedScroller != null)
                inheritedScroller.enabled = false;
            overflowBadge.fontSize = Mathf.Max(12f, metricName.fontSize * 0.62f);
            overflowBadge.alignment = TextAlignmentOptions.BottomRight;
            RectTransform rect = overflowBadge.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(10f, 5f);
            rect.offsetMax = new Vector2(-10f, -5f);
        }

        if (overflowBadge != null)
        {
            overflowBadge.gameObject.SetActive(true);
            overflowBadge.text = $"+{hiddenCount} MORE";
            overflowBadge.color = GetStateColor();
        }
    }

    private static void SetTrailAlpha(Image image, float alpha)
    {
        if (image == null)
            return;
        Color color = image.color;
        color.a = alpha;
        image.color = color;
    }

    private readonly struct RectTransformSnapshot
    {
        private readonly RectTransform rect;
        private readonly Vector2 anchorMin;
        private readonly Vector2 anchorMax;
        private readonly Vector2 pivot;
        private readonly Vector2 anchoredPosition;
        private readonly Vector2 sizeDelta;
        private readonly Vector3 localScale;
        private readonly Quaternion localRotation;

        public RectTransformSnapshot(RectTransform source)
        {
            rect = source;
            anchorMin = source.anchorMin;
            anchorMax = source.anchorMax;
            pivot = source.pivot;
            anchoredPosition = source.anchoredPosition;
            sizeDelta = source.sizeDelta;
            localScale = source.localScale;
            localRotation = source.localRotation;
        }

        public void Restore()
        {
            if (rect == null)
                return;

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            rect.localScale = localScale;
            rect.localRotation = localRotation;
        }
    }

    private Color GetStateColor()
    {
        return visualState switch
        {
            StatVisualState.Warning => warningColor,
            StatVisualState.Critical => criticalColor,
            StatVisualState.Unavailable => unavailableColor,
            _ => normalColor
        };
    }

    private Sprite GetStateIcon()
    {
        return visualState switch
        {
            StatVisualState.Warning => warningIcon,
            StatVisualState.Critical => criticalIcon,
            StatVisualState.Unavailable => unavailableIcon,
            _ => normalIcon
        };
    }

    private Sprite GetStateBackground()
    {
        return visualState switch
        {
            StatVisualState.Warning => warningBackground != null ? warningBackground : normalBackground,
            StatVisualState.Critical => criticalBackground != null ? criticalBackground : normalBackground,
            StatVisualState.Unavailable => unavailableBackground != null ? unavailableBackground : normalBackground,
            _ => normalBackground
        };
    }

    private static void SetActive(Component component, bool active)
    {
        if (component != null)
            component.gameObject.SetActive(active);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null)
            target.SetActive(active);
    }

    private static void SetImageColor(Image image, Color color)
    {
        if (image != null)
            image.color = color;
    }

    private static void SetImageRgbPreserveAlpha(Image image, Color color)
    {
        if (image == null)
            return;
        color.a = image.color.a;
        image.color = color;
    }

    private bool IsAlarmState() =>
        visualState == StatVisualState.Warning || visualState == StatVisualState.Critical;

    private sealed class MetricSlot
    {
        public readonly RectTransform Root;
        private readonly TMP_Text label;
        private readonly TMP_Text value;
        private readonly TMP_Text unit;
        private TMPOverflowScroller scroller;
        private TelemetryValueFitter fitter;
        private bool headerPrepared;
        private RectTransform labelViewport;

        public MetricSlot(RectTransform root, TMP_Text label, TMP_Text value, TMP_Text unit)
        {
            Root = root;
            this.label = label;
            this.value = value;
            this.unit = unit;
        }

        public void EnsureHelpers(TextOverflowMotion motion)
        {
            // Clip every metric slot to the card's actual inset bounds. This
            // also protects value/unit content if a prefab layout component
            // reports a stale preferred width during its first frame.
            if (Root != null && Root.GetComponent<RectMask2D>() == null)
                Root.gameObject.AddComponent<RectMask2D>();

            if (label != null)
            {
                PrepareHeaderViewport();
                scroller = label.GetComponent<TMPOverflowScroller>();
                if (scroller == null)
                    scroller = label.gameObject.AddComponent<TMPOverflowScroller>();
                scroller.SetMotion(motion);
                scroller.CaptureRestingPosition();
            }

            if (value != null && unit != null)
            {
                fitter = Root.GetComponent<TelemetryValueFitter>();
                if (fitter == null)
                    fitter = Root.gameObject.AddComponent<TelemetryValueFitter>();
                fitter.Bind(value, unit, value.transform.parent as RectTransform);
            }
        }

        public void Render(
            PerformanceStatSource source,
            TextOverflowMotion motion,
            bool accurateUnitCasing,
            Color stateColor)
        {
            if (label != null)
            {
                label.text = source.MetricName.ToUpperInvariant();
                label.color = stateColor;
            }
            scroller?.SetMotion(motion);
            fitter?.Fit(source, accurateUnitCasing);
        }

        public void SetLabelColor(Color color)
        {
            if (label != null)
                label.color = color;
        }

        public float RequestedCardExpansion => fitter != null
            ? fitter.RequestedCardExpansion
            : 1f;

        private void PrepareHeaderViewport()
        {
            if (headerPrepared || label == null)
                return;

            RectTransform immediateParent = label.transform.parent as RectTransform;
            RectTransform labelRect = label.rectTransform;
            bool alreadyInsideViewport = immediateParent != null &&
                                         immediateParent.name == "Label viewport";
            RectTransform header = alreadyInsideViewport
                ? immediateParent.parent as RectTransform
                : immediateParent;
            if (header == null)
                return;

            HorizontalLayoutGroup layout = header.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
                layout.enabled = false;

            Transform existingViewport = alreadyInsideViewport
                ? immediateParent
                : header.Find("Label viewport");
            if (existingViewport != null)
                labelViewport = existingViewport as RectTransform;
            else
            {
                GameObject viewportObject = new(
                    "Label viewport",
                    typeof(RectTransform),
                    typeof(RectMask2D));
                labelViewport = viewportObject.GetComponent<RectTransform>();
                labelViewport.SetParent(header, false);
                labelViewport.SetAsFirstSibling();
            }

            labelViewport.anchorMin = Vector2.zero;
            labelViewport.anchorMax = Vector2.one;
            labelViewport.pivot = new Vector2(0.5f, 0.5f);
            labelViewport.offsetMin = new Vector2(20f, 0f);
            labelViewport.offsetMax = new Vector2(-20f, 0f);

            labelRect.SetParent(labelViewport, false);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = Vector2.zero;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            RectTransform valueRow = value != null
                ? value.transform.parent as RectTransform
                : null;
            if (valueRow != null)
            {
                valueRow.offsetMin = new Vector2(20f, valueRow.offsetMin.y);
                valueRow.offsetMax = new Vector2(-20f, valueRow.offsetMax.y);
            }

            foreach (RectTransform child in header)
            {
                if (child == labelViewport)
                    continue;
                child.anchorMin = new Vector2(1f, 0.5f);
                child.anchorMax = new Vector2(1f, 0.5f);
                child.pivot = new Vector2(1f, 0.5f);
                child.anchoredPosition = new Vector2(-2f, 0f);
                child.sizeDelta = new Vector2(20f, 20f);
            }

            headerPrepared = true;
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplySide();
        ApplyState();
    }
#endif
}
