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

public enum MetricLabelLayoutMode
{
    Sliding,
    AdaptiveWrap
}

[DisallowMultipleComponent]
public sealed class PerformanceStatCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    public event Action<PerformanceStatCardView, bool> FocusChanged;
    public static event Action<PerformanceStatCardView> ActiveFocusChanged;

    public static PerformanceStatCardView ActiveFocusedCard { get; private set; }

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
    [SerializeField] private MetricLabelLayoutMode labelLayoutMode =
        MetricLabelLayoutMode.AdaptiveWrap;
    [SerializeField, Tooltip("Use the single-line sliding treatment for overflowing labels on the portrait top and bottom rails, while keeping the selected layout mode for wide rails.")]
    private bool useSlidingLabelsInPortrait = true;
    [SerializeField, Range(2, 4)] private int maximumWrappedLabelLines = 4;
    [SerializeField, Min(28f)] private float singleLineHeaderHeight = 31f;
    [SerializeField, Min(20f)] private float wrappedLineHeight = 30f;
    [SerializeField, Min(0f)] private float metricContentGap = 8f;
    [SerializeField, Min(32f)] private float valueRowHeight = 48f;
    [SerializeField, Tooltip("Preserve case-sensitive engineering unit symbols such as mm/s, kN, kW and MPa. Disable for an all-uppercase visual style.")]
    private bool useAccurateUnitCasing;
    [SerializeField, Min(0f), Tooltip("Seconds a tapped card stays focused on touch devices, which have no hover state. " +
        "Long enough to read the isolated mechanism in the X-Ray view before focus releases.")]
    private float mobileTapFocusSeconds = 4f;
    [SerializeField, Min(0.05f)] private float focusToRestDuration = 0.5f;
    [SerializeField, Range(0.05f, 1f)] private float unfocusedPeerOpacity = 0.15f;
    [SerializeField, Min(0.02f)] private float peerFocusTransitionDuration = 0.23f;
    [SerializeField, Min(0f)] private float contentPaddingInsideBackground = 32f;
    [SerializeField, Min(0f)] private float verticalContentPadding = 16f;

    private readonly List<MetricSlot> metricSlots = new();
    private PerformanceStatSource boundSource;
    private RectTransform metricTemplateRoot;
    private bool focused;
    private float tapFocusUntil;
    private Transform presentationAnchor;
    private bool fadingConnectionToRest;
    private float connectionFadeStartedAt;
    private float connectionFadeStartEmphasis;
    private float connectionEmphasis;
    private RectTransformSnapshot[] accentGeometry = Array.Empty<RectTransformSnapshot>();
    private float nextAccentGeometryCheck;
    private Graphic[] focusGraphics = Array.Empty<Graphic>();
    private float peerPresentationAlpha = 1f;
    private float peerPresentationStartAlpha = 1f;
    private float peerPresentationTargetAlpha = 1f;
    private float peerPresentationTransitionStartedAt;
    private bool peerPresentationTransitioning;

    private const float AccentGeometryCheckInterval = 0.5f;

    public StatRailSide RailSide => railSide;
    public StatVisualState VisualState => visualState;
    public Color StateColor => GetStateColor();
    public PerformanceStatSource BoundSource => boundSource;
    public Transform WorldAnchor => presentationAnchor != null
        ? presentationAnchor
        : boundSource != null ? boundSource.WorldAnchor : null;
    public bool IsFocused => focused;
    public float UnfocusedPeerOpacity => unfocusedPeerOpacity;
    public float PeerFocusTransitionDuration => peerFocusTransitionDuration;
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
        CacheFocusGraphics();
        CaptureAccentGeometry();
        InitialiseMetricTemplate();
        if (backgroundImage != null)
            backgroundImage.raycastTarget = true;
        ApplySide();
        ApplyState();
        SetConnectionEmphasis(IsAlarmState() ? 1f : 0f);
    }

    private void OnEnable()
    {
        ActiveFocusChanged -= HandleActiveFocusChanged;
        ActiveFocusChanged += HandleActiveFocusChanged;
        // Pooled cards may have previously been resized, reoriented, or caught
        // mid-presentation. Reapply the prefab accent geometry before the card
        // becomes visible in its new rail.
        if (accentGeometry.Length == 0)
            CaptureAccentGeometry();
        RestoreAccentGeometry();
        SetConnectionEmphasis(IsAlarmState() ? 1f : 0f);
        nextAccentGeometryCheck = Time.unscaledTime + AccentGeometryCheckInterval;
        ApplyPeerPresentationInstant(ResolvePeerPresentationAlpha(ActiveFocusedCard));
    }

    private void OnDisable()
    {
        ActiveFocusChanged -= HandleActiveFocusChanged;
        if (ActiveFocusedCard == this)
            SetActiveFocusedCard(null);

        bool wasFocused = focused;
        focused = false;
        tapFocusUntil = 0f;
        fadingConnectionToRest = false;
        connectionFadeStartEmphasis = 0f;
        SetConnectionEmphasis(0f);
        ApplyPeerPresentationInstant(1f);
        if (wasFocused)
            FocusChanged?.Invoke(this, false);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        // Browsers do not always deliver a pointer-exit event when the tab or
        // window loses focus. Never leave a normal card permanently focused.
        if (!hasFocus && focused && !IsAlarmState())
            SetFocused(false);
    }

    private void OnRectTransformDimensionsChange()
    {
        // Check again on the next LateUpdate after card-content or responsive
        // rail sizing has finished for this frame.
        nextAccentGeometryCheck = 0f;
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

        UpdatePeerPresentation();
    }

    private void LateUpdate()
    {
        if (Time.unscaledTime < nextAccentGeometryCheck)
            return;

        nextAccentGeometryCheck = Time.unscaledTime + AccentGeometryCheckInterval;

        // Layout rebuilds and interrupted presentation animations must never
        // leave one card with a compressed accent trail. The comparison is
        // deliberately infrequent and writes only when a transform changed.
        foreach (RectTransformSnapshot snapshot in accentGeometry)
        {
            if (!snapshot.MatchesCurrentGeometry())
                snapshot.Restore();
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
        SetSide(side);
        SetState(presentation.VisualState);
        BindSource(presentation.Source);
    }

    public void BindSource(PerformanceStatSource source)
    {
        boundSource = source;

        ConfigureSlotLayout();

        for (int i = 0; i < metricSlots.Count; i++)
        {
            bool visible = i == 0 && source != null;
            metricSlots[i].Root.gameObject.SetActive(visible);
            if (visible)
                metricSlots[i].Render(
                    source,
                    labelOverflowMotion,
                    ResolveLabelLayoutMode(),
                    maximumWrappedLabelLines,
                    singleLineHeaderHeight,
                    wrappedLineHeight,
                    useAccurateUnitCasing,
                    GetStateColor());
        }

        // TextMeshPro can refresh its renderers when telemetry text changes.
        // Reapply the presentation alpha so a dimmed peer cannot flash opaque.
        ApplyPeerPresentationAlpha(peerPresentationAlpha);
    }

    public void RefreshBoundSources()
    {
        if (boundSource == null)
            return;
        SetState(boundSource.VisualState);
        BindSource(boundSource);
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
        if (boundSource != null)
            BindSource(boundSource);
    }

    public void SetState(StatVisualState state)
    {
        if (visualState == state)
        {
            ApplyPeerPresentationAlpha(peerPresentationAlpha);
            return;
        }
        visualState = state;
        ApplyState();
        BeginPeerPresentation(ResolvePeerPresentationAlpha(ActiveFocusedCard));
        ApplyPeerPresentationAlpha(peerPresentationAlpha);
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
        // Opacity belongs to the focused/relaxed state machine. Geometry is
        // guarded independently by the captured RectTransform snapshots, so
        // fading can never resize or rescale the gradient trail.
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
        if (focused)
            SetActiveFocusedCard(this);
        else if (ActiveFocusedCard == this)
            SetActiveFocusedCard(null);
        if (focused || IsAlarmState())
        {
            fadingConnectionToRest = false;
            SetConnectionEmphasis(1f);
        }
        else
        {
            // Focus-to-rest begins immediately with no delay or click-to-lock state.
            fadingConnectionToRest = true;
            connectionFadeStartedAt = Time.unscaledTime;
            connectionFadeStartEmphasis = connectionEmphasis;
        }
        FocusChanged?.Invoke(this, focused);
    }

    private static void SetActiveFocusedCard(PerformanceStatCardView card)
    {
        if (ActiveFocusedCard == card)
            return;

        ActiveFocusedCard = card;
        ActiveFocusChanged?.Invoke(card);
    }

    private void HandleActiveFocusChanged(PerformanceStatCardView activeCard)
    {
        BeginPeerPresentation(ResolvePeerPresentationAlpha(activeCard));
    }

    private float ResolvePeerPresentationAlpha(PerformanceStatCardView activeCard)
    {
        if (activeCard == null || activeCard == this || IsAlarmState())
            return 1f;

        return unfocusedPeerOpacity;
    }

    private void BeginPeerPresentation(float target)
    {
        target = Mathf.Clamp01(target);
        if (Mathf.Approximately(peerPresentationTargetAlpha, target) &&
            (!peerPresentationTransitioning ||
             Mathf.Approximately(peerPresentationAlpha, target)))
        {
            return;
        }

        peerPresentationStartAlpha = peerPresentationAlpha;
        peerPresentationTargetAlpha = target;
        peerPresentationTransitionStartedAt = Time.unscaledTime;
        peerPresentationTransitioning = true;
    }

    private void UpdatePeerPresentation()
    {
        if (!peerPresentationTransitioning)
            return;

        float progress = Mathf.Clamp01(
            (Time.unscaledTime - peerPresentationTransitionStartedAt) /
            Mathf.Max(0.02f, peerFocusTransitionDuration));
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        ApplyPeerPresentationAlpha(Mathf.Lerp(
            peerPresentationStartAlpha,
            peerPresentationTargetAlpha,
            eased));

        if (progress >= 1f)
            peerPresentationTransitioning = false;
    }

    private void ApplyPeerPresentationInstant(float alpha)
    {
        peerPresentationAlpha = Mathf.Clamp01(alpha);
        peerPresentationStartAlpha = peerPresentationAlpha;
        peerPresentationTargetAlpha = peerPresentationAlpha;
        peerPresentationTransitioning = false;
        ApplyPeerPresentationAlpha(peerPresentationAlpha);
    }

    private void ApplyPeerPresentationAlpha(float alpha)
    {
        peerPresentationAlpha = Mathf.Clamp01(alpha);
        if (focusGraphics.Length == 0)
            CacheFocusGraphics();

        foreach (Graphic graphic in focusGraphics)
        {
            if (graphic != null)
                graphic.canvasRenderer.SetAlpha(peerPresentationAlpha);
        }
    }

    private void CacheFocusGraphics()
    {
        focusGraphics = GetComponentsInChildren<Graphic>(true);
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

    private void ConfigureSlotLayout()
    {
        if (metricSlots.Count == 0)
            return;

        for (int i = 0; i < metricSlots.Count; i++)
        {
            RectTransform root = metricSlots[i].Root;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;

            GetVisibleBackgroundInsets(out float backgroundLeft, out float backgroundRight);
            float leftInset = backgroundLeft + contentPaddingInsideBackground;
            float rightInset = backgroundRight + contentPaddingInsideBackground;
            root.offsetMin = new Vector2(leftInset, verticalContentPadding);
            root.offsetMax = new Vector2(-rightInset, -verticalContentPadding);
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

    public float EstimateRequestedCardHeight(
        TelemetryCardPresentation presentation,
        StatRailSide side,
        float baseMetricHeight,
        float cardWidth)
    {
        float stableMetricHeight = Mathf.Max(
            baseMetricHeight,
            verticalContentPadding * 2f + singleLineHeaderHeight +
            metricContentGap + valueRowHeight);
        if (presentation?.Source == null ||
            ResolveLabelLayoutMode(side) == MetricLabelLayoutMode.Sliding || metricName == null)
        {
            return stableMetricHeight;
        }

        GetVisibleBackgroundInsets(out float backgroundLeft, out float backgroundRight);
        float availableLabelWidth = Mathf.Max(
            32f,
            cardWidth - backgroundLeft - backgroundRight -
            contentPaddingInsideBackground * 2f);

        string label = presentation.Source.MetricName.ToUpperInvariant();
        int lines = EstimateWrappedLineCount(label, availableLabelWidth);
        float extra = lines <= maximumWrappedLabelLines
            ? Mathf.Max(0, lines - 1) * wrappedLineHeight
            : 0f;
        return stableMetricHeight + extra;
    }

    private int EstimateWrappedLineCount(string text, float width)
    {
        if (metricName == null || string.IsNullOrEmpty(text) || width <= 1f)
            return 1;

        return CalculateWrappedLineCount(metricName, text, width);
    }

    private MetricLabelLayoutMode ResolveLabelLayoutMode()
    {
        return ResolveLabelLayoutMode(railSide);
    }

    private MetricLabelLayoutMode ResolveLabelLayoutMode(StatRailSide side)
    {
        bool portraitRail = side == StatRailSide.Top || side == StatRailSide.Bottom;
        return useSlidingLabelsInPortrait && portraitRail
            ? MetricLabelLayoutMode.Sliding
            : labelLayoutMode;
    }

    private static int CalculateWrappedLineCount(
        TMP_Text textComponent,
        string text,
        float width)
    {
        if (textComponent == null || string.IsNullOrEmpty(text) || width <= 1f)
            return 1;

        TextWrappingModes previousWrapping = textComponent.textWrappingMode;
        textComponent.textWrappingMode = TextWrappingModes.NoWrap;

        Vector2 unwrappedSize = textComponent.GetPreferredValues(
            text,
            100000f,
            1000f);
        if (unwrappedSize.x <= width + 0.5f)
        {
            textComponent.textWrappingMode = previousWrapping;
            return 1;
        }

        textComponent.textWrappingMode = TextWrappingModes.Normal;

        // Measure the same glyphs in both cases. Using a different baseline
        // string (previously "AG") made ascender differences look like a
        // fractional second line, which CeilToInt promoted to a full line.
        float singleLineHeight = Mathf.Max(
            1f,
            unwrappedSize.y);
        float wrappedHeight = textComponent.GetPreferredValues(text, width, 1000f).y;
        textComponent.textWrappingMode = previousWrapping;

        return Mathf.Max(1, Mathf.RoundToInt(wrappedHeight / singleLineHeight));
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

        public bool MatchesCurrentGeometry()
        {
            if (rect == null)
                return true;

            return Approximately(rect.anchorMin, anchorMin) &&
                   Approximately(rect.anchorMax, anchorMax) &&
                   Approximately(rect.pivot, pivot) &&
                   Approximately(rect.anchoredPosition, anchoredPosition) &&
                   Approximately(rect.sizeDelta, sizeDelta) &&
                   Approximately(rect.localScale, localScale) &&
                   Approximately(rect.localRotation, localRotation);
        }

        private static bool Approximately(Vector2 a, Vector2 b) =>
            (a - b).sqrMagnitude <= 0.0001f;

        private static bool Approximately(Vector3 a, Vector3 b) =>
            (a - b).sqrMagnitude <= 0.0001f;

        private static bool Approximately(Quaternion a, Quaternion b) =>
            Mathf.Abs(Quaternion.Dot(a, b)) >= 0.99999f;
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
                scroller.enabled = false;
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
            MetricLabelLayoutMode layoutMode,
            int maximumWrappedLines,
            float baseHeaderHeight,
            float lineHeight,
            bool accurateUnitCasing,
            Color stateColor)
        {
            if (label != null)
            {
                label.text = source.MetricName.ToUpperInvariant();
                label.color = stateColor;
                ConfigureLabel(
                    layoutMode,
                    maximumWrappedLines,
                    baseHeaderHeight,
                    lineHeight,
                    motion);
            }
            if (scroller != null && scroller.enabled)
                scroller.SetMotion(motion);
            fitter?.Fit(source, accurateUnitCasing);
        }

        public void SetLabelColor(Color color)
        {
            if (label != null)
                label.color = color;
        }

        private void ConfigureLabel(
            MetricLabelLayoutMode layoutMode,
            int maximumWrappedLines,
            float baseHeaderHeight,
            float lineHeight,
            TextOverflowMotion motion)
        {
            if (label == null || labelViewport == null)
                return;

            bool sliding = layoutMode == MetricLabelLayoutMode.Sliding;
            if (!sliding)
            {
                label.textWrappingMode = TextWrappingModes.Normal;
                label.overflowMode = TextOverflowModes.Overflow;
                label.maxVisibleLines = 0;
                float width = labelViewport.rect.width;
                if (width <= 1f && Root != null)
                    width = Root.rect.width;
                width = Mathf.Max(32f, width);
                int lines = CalculateWrappedLineCount(label, label.text, width);
                sliding = lines > maximumWrappedLines;
                if (!sliding)
                {
                    RectTransform header = labelViewport.parent as RectTransform;
                    if (header != null)
                    {
                        header.sizeDelta = new Vector2(
                            header.sizeDelta.x,
                            baseHeaderHeight + Mathf.Max(0, lines - 1) * lineHeight);
                    }
                    label.maxVisibleLines = maximumWrappedLines;
                    scroller.enabled = false;
                    scroller.ResetMotion();
                    return;
                }
            }

            RectTransform slidingHeader = labelViewport.parent as RectTransform;
            if (slidingHeader != null)
                slidingHeader.sizeDelta = new Vector2(
                    slidingHeader.sizeDelta.x,
                    baseHeaderHeight);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.maxVisibleLines = 1;
            scroller.enabled = true;
            scroller.SetMotion(motion);
            scroller.CaptureRestingPosition();
            scroller.ResetMotion();
        }

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
            labelViewport.offsetMin = Vector2.zero;
            labelViewport.offsetMax = Vector2.zero;

            labelRect.SetParent(labelViewport, false);
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.maxVisibleLines = 1;
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
                valueRow.offsetMin = new Vector2(0f, valueRow.offsetMin.y);
                valueRow.offsetMax = new Vector2(0f, valueRow.offsetMax.y);
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
        unfocusedPeerOpacity = Mathf.Clamp(unfocusedPeerOpacity, 0.05f, 1f);
        peerFocusTransitionDuration = Mathf.Max(0.02f, peerFocusTransitionDuration);
        ApplySide();
        ApplyState();
    }
#endif
}
