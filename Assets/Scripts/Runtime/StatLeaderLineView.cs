using UnityEngine;
using UnityEngine.UI;

public class StatLeaderLineView : MonoBehaviour
{
    [Header("Line Parts")]
    [SerializeField] private RectTransform horizontalSegment;
    [SerializeField] private RectTransform angledSegment;
    [SerializeField] private RectTransform anchorMarker;

    [Header("Line Routing")]
    [Tooltip("How much straight distance leaves the card before the bend.")]
    [SerializeField, Range(0.4f, 0.95f)] private float horizontalFraction = 0.7f;

    [Tooltip("Use approximately 0.7 for the thinner line requested by your mentor.")]
    [SerializeField, Min(0.25f)] private float lineThickness = 0.7f;

    [Header("Reveal Preview")]
    [SerializeField, Range(0f, 1f)] private float revealProgress = 1f;
    [SerializeField, Range(0f, 1f)] private float anchorProgress = 1f;

    [Header("Appearance")]
    [SerializeField] private bool matchCardStateColor = true;
    [SerializeField] private Color defaultColor = Color.cyan;

    [Header("Focus Presentation")]
    [SerializeField, Range(0f, 1f)] private float restingLineOpacity = 0.025f;
    [Tooltip("Resting line opacity used when no secondary rail contains a card.")]
    [SerializeField, Range(0f, 1f)] private float emptySecondaryRailsRestingOpacity = 0.4f;
    [SerializeField, Range(0f, 1f)] private float restingAnchorOpacity = 0.4f;
    [SerializeField, Min(0.05f)] private float focusToRestDuration = 0.23f;
    [Tooltip("Fade duration when secondary-rail occupancy changes the resting line opacity.")]
    [SerializeField, Min(0.05f)] private float secondaryRailOpacityTransitionDuration = 1f;

    private PerformanceStatSource source;
    private PerformanceStatCardView card;
    private Camera worldCamera;
    private Canvas canvas;
    private RectTransform lineLayer;

    private Image horizontalImage;
    private Image angledImage;
    private Image anchorImage;
    private Material horizontalMaterialInstance;
    private Material angledMaterialInstance;
    private Vector3 anchorOriginalScale = Vector3.one;

    private float horizontalLength;
    private float angledLength;
    private float totalPathLength;
    private float presentationLineOpacity = 1f;
    private float presentationAnchorOpacity = 1f;
    private float fadeStartTime;
    private float activeFadeDuration;
    private float fadeStartLineOpacity = 1f;
    private float fadeStartAnchorOpacity = 1f;
    private float activeRestingLineOpacity;
    private bool fadingToRest;
    private CanvasGroup peerPresentationGroup;
    private float peerPresentationAlpha = 1f;
    private float peerPresentationStartAlpha = 1f;
    private float peerPresentationTargetAlpha = 1f;
    private float peerPresentationTransitionStartedAt;
    private bool peerPresentationTransitioning;

    private static readonly int RevealDistanceId = Shader.PropertyToID("_RevealDistance");
    private static readonly int SegmentStartDistanceId = Shader.PropertyToID("_SegmentStartDistance");
    private static readonly int SegmentLengthId = Shader.PropertyToID("_SegmentLength");
    private static readonly int ReverseDirectionId = Shader.PropertyToID("_ReverseDirection");

    public PerformanceStatSource Source => source;
    public PerformanceStatCardView Card => card;
    public float RevealProgress => revealProgress;
    public float AnchorProgress => anchorProgress;
    public float TotalPathLength => totalPathLength;
    public float RestingLineOpacity => restingLineOpacity;
    public float EmptySecondaryRailsRestingOpacity => emptySecondaryRailsRestingOpacity;
    public float SecondaryRailOpacityTransitionDuration =>
        secondaryRailOpacityTransitionDuration;

    private void Awake()
    {
        activeRestingLineOpacity = restingLineOpacity;
        activeFadeDuration = focusToRestDuration;
        CacheImages();
        CreateRuntimeMaterials();

        if (anchorMarker != null)
            anchorOriginalScale = anchorMarker.localScale;

        ApplyAnchorAppearance();
    }

    private void OnEnable()
    {
        PerformanceStatCardView.ActiveFocusChanged -= HandleActiveFocusChanged;
        PerformanceStatCardView.ActiveFocusChanged += HandleActiveFocusChanged;
        CachePeerPresentationGroup();
        ApplyPeerPresentationInstant(ResolvePeerPresentationAlpha());
    }

    private void OnDisable()
    {
        PerformanceStatCardView.ActiveFocusChanged -= HandleActiveFocusChanged;
        ApplyPeerPresentationInstant(1f);
    }

    public void Bind(
        PerformanceStatSource newSource,
        PerformanceStatCardView newCard,
        Camera newWorldCamera,
        Canvas newCanvas,
        RectTransform newLineLayer)
    {
        source = newSource;
        card = newCard;
        worldCamera = newWorldCamera;
        canvas = newCanvas;
        lineLayer = newLineLayer;
        gameObject.name = newSource != null && !string.IsNullOrWhiteSpace(newSource.MetricName)
            ? $"Leader Line - {newSource.MetricName}"
            : "Leader Line";
        if (card != null)
        {
            card.FocusChanged -= HandleCardFocusChanged;
            card.FocusChanged += HandleCardFocusChanged;
        }
        if (IsAlarmState() || (card != null && card.IsFocused))
            SetPresentationOpacityInstant(1f, 1f);
        else
            SetPresentationOpacityInstant(activeRestingLineOpacity, restingAnchorOpacity);
        fadingToRest = false;
        ApplyPeerPresentationInstant(ResolvePeerPresentationAlpha());
        CacheImages();
        CreateRuntimeMaterials();
        PrepareLinePart(horizontalSegment);
        PrepareLinePart(angledSegment);
        RefreshColor();
        UpdateLine();
    }

    public void Unbind()
    {
        if (card != null)
            card.FocusChanged -= HandleCardFocusChanged;
        source = null;
        card = null;
        worldCamera = null;
        canvas = null;
        lineLayer = null;
        gameObject.name = "Leader Line (Pooled)";
        SetGeometryVisible(false);
    }

    private void LateUpdate()
    {
        UpdatePeerPresentation();
        UpdateFocusPresentation();
        UpdateLine();
    }

    public void SetRevealProgress(float progress)
    {
        revealProgress = Mathf.Clamp01(progress);
        ApplyRevealToMaterials();
    }

    public void SetAnchorProgress(float progress)
    {
        anchorProgress = Mathf.Clamp01(progress);
        ApplyAnchorAppearance();
    }

    public void SetPresentationInstant(bool visible)
    {
        float progress = visible ? 1f : 0f;
        SetAnchorProgress(progress);
        SetRevealProgress(progress);
    }

    public void SetAdaptiveRestingLineOpacity(
        float opacity,
        float transitionDuration,
        bool applyInstantly = false)
    {
        activeRestingLineOpacity = Mathf.Clamp01(opacity);

        // Focused, warning and critical lines stay at 100%. The new resting
        // target is retained and used when they next return to rest.
        if (card != null && (card.IsFocused || IsAlarmState()))
            return;

        if (applyInstantly || transitionDuration <= 0.01f)
        {
            fadingToRest = false;
            SetPresentationOpacityInstant(activeRestingLineOpacity, restingAnchorOpacity);
            return;
        }

        BeginFadeToRest(transitionDuration);
    }

    private void UpdateLine()
    {
        if (!ReferencesAreValid() || card.WorldAnchor == null)
        {
            SetGeometryVisible(false);
            return;
        }

        Vector3 projectedSensorPoint = worldCamera.WorldToScreenPoint(
            card.WorldAnchor.position
        );

        // A point behind the camera has no truthful 2D screen location.
        // Points beyond the left, right, top or bottom are intentionally
        // NOT rejected or clamped. Their line geometry continues to update
        // using the real projected sensor position.
        if (projectedSensorPoint.z <= 0f)
        {
            SetGeometryVisible(false);
            return;
        }

        Vector2 sensorScreenPoint = new Vector2(
            projectedSensorPoint.x,
            projectedSensorPoint.y
        );

        Camera uiCamera = GetUICamera();
        Vector2 cardScreenPoint = RectTransformUtility.WorldToScreenPoint(
            uiCamera,
            card.ActiveConnectionPoint.position
        );

        bool cardConverted = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            lineLayer,
            cardScreenPoint,
            uiCamera,
            out Vector2 cardPoint
        );

        bool sensorConverted = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            lineLayer,
            sensorScreenPoint,
            uiCamera,
            out Vector2 sensorPoint
        );

        if (!cardConverted || !sensorConverted)
        {
            SetGeometryVisible(false);
            return;
        }

        DrawElbow(cardPoint, sensorPoint);
        RefreshColor();
        SetGeometryVisible(true);
    }

    private void DrawElbow(Vector2 cardPoint, Vector2 sensorPoint)
    {
        Vector2 bendPoint;

        if (card.RailSide == StatRailSide.Top ||
            card.RailSide == StatRailSide.Bottom)
        {
            // Portrait: leave the card vertically, then bend toward the sensor.
            float bendY = Mathf.Lerp(
                cardPoint.y,
                sensorPoint.y,
                horizontalFraction
            );

            bendPoint = new Vector2(cardPoint.x, bendY);
        }
        else
        {
            // Wide: leave the card horizontally, then bend toward the sensor.
            float bendX = Mathf.Lerp(
                cardPoint.x,
                sensorPoint.x,
                horizontalFraction
            );

            bendPoint = new Vector2(bendX, cardPoint.y);
        }

        horizontalLength = SetLineSegment(horizontalSegment, cardPoint, bendPoint);
        angledLength = SetLineSegment(angledSegment, bendPoint, sensorPoint);
        totalPathLength = angledLength + horizontalLength;

        if (anchorMarker != null)
        {
            anchorMarker.anchoredPosition = sensorPoint;
            anchorMarker.localRotation = Quaternion.identity;
        }

        ApplyRevealToMaterials();
        ApplyAnchorAppearance();
    }

    private float SetLineSegment(RectTransform segment, Vector2 start, Vector2 end)
    {
        if (segment == null)
            return 0f;

        Vector2 difference = end - start;
        float length = difference.magnitude;

        if (length <= 0.01f)
        {
            segment.gameObject.SetActive(false);
            return 0f;
        }

        float angle = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg;
        segment.gameObject.SetActive(true);
        segment.anchoredPosition = (start + end) * 0.5f;
        segment.sizeDelta = new Vector2(length, lineThickness);
        segment.localRotation = Quaternion.Euler(0f, 0f, angle);
        return length;
    }

    private void ApplyRevealToMaterials()
    {
        if (totalPathLength <= 0.01f)
            return;

        float revealDistance = revealProgress * totalPathLength;

        SetMaterialPathValues(
            angledMaterialInstance,
            0f,
            angledLength,
            revealDistance,
            1f
        );

        SetMaterialPathValues(
            horizontalMaterialInstance,
            angledLength,
            horizontalLength,
            revealDistance,
            1f
        );
    }

    private static void SetMaterialPathValues(
        Material material,
        float segmentStartDistance,
        float segmentLength,
        float revealDistance,
        float reverseDirection)
    {
        if (material == null)
            return;

        if (material.HasProperty(SegmentStartDistanceId))
            material.SetFloat(SegmentStartDistanceId, segmentStartDistance);

        if (material.HasProperty(SegmentLengthId))
            material.SetFloat(SegmentLengthId, segmentLength);

        if (material.HasProperty(RevealDistanceId))
            material.SetFloat(RevealDistanceId, revealDistance);

        if (material.HasProperty(ReverseDirectionId))
            material.SetFloat(ReverseDirectionId, reverseDirection);
    }

    private void CacheImages()
    {
        if (horizontalSegment != null)
            horizontalImage = horizontalSegment.GetComponent<Image>();

        if (angledSegment != null)
            angledImage = angledSegment.GetComponent<Image>();

        if (anchorMarker != null)
            anchorImage = anchorMarker.GetComponent<Image>();
    }

    private void CreateRuntimeMaterials()
    {
        if (horizontalImage != null && horizontalMaterialInstance == null)
            horizontalMaterialInstance = CreateMaterialInstance(horizontalImage, "Horizontal");

        if (angledImage != null && angledMaterialInstance == null)
            angledMaterialInstance = CreateMaterialInstance(angledImage, "Angled");
    }

    private static Material CreateMaterialInstance(Image image, string suffix)
    {
        Material sourceMaterial = image.material;
        if (sourceMaterial == null)
            return null;

        Material instance = new Material(sourceMaterial)
        {
            name = sourceMaterial.name + " - " + suffix + " Instance",
            hideFlags = HideFlags.DontSave
        };

        image.material = instance;
        return instance;
    }

    private static void PrepareLinePart(RectTransform segment)
    {
        if (segment == null)
            return;

        segment.anchorMin = new Vector2(0.5f, 0.5f);
        segment.anchorMax = new Vector2(0.5f, 0.5f);
        segment.pivot = new Vector2(0.5f, 0.5f);
        segment.localScale = Vector3.one;

        if (segment.TryGetComponent(out Image image))
        {
            image.raycastTarget = false;
            image.preserveAspect = false;
        }
    }

    private void RefreshColor()
    {
        Color lineColor = matchCardStateColor && card != null
            ? card.StateColor
            : defaultColor;

        if (horizontalImage != null)
        {
            Color color = lineColor;
            color.a *= presentationLineOpacity;
            horizontalImage.color = color;
        }

        if (angledImage != null)
        {
            Color color = lineColor;
            color.a *= presentationLineOpacity;
            angledImage.color = color;
        }

        ApplyAnchorColor(lineColor);
    }

    private void ApplyAnchorColor(Color lineColor)
    {
        if (anchorImage == null)
            return;

        Color anchorColor = lineColor;
        anchorColor.a *= anchorProgress * presentationAnchorOpacity;
        anchorImage.color = anchorColor;
    }

    private void ApplyAnchorAppearance()
    {
        if (anchorMarker == null)
            return;

        float eased = EaseOutBack(anchorProgress);
        float scale = Mathf.Lerp(0.65f, 1f, eased);
        anchorMarker.localScale = anchorOriginalScale * scale;

        Color lineColor = matchCardStateColor && card != null
            ? card.StateColor
            : defaultColor;

        ApplyAnchorColor(lineColor);
    }

    private void HandleCardFocusChanged(PerformanceStatCardView changedCard, bool focused)
    {
        if (IsAlarmState())
        {
            SetPresentationOpacityInstant(1f, 1f);
            return;
        }

        if (focused)
        {
            fadingToRest = false;
            SetPresentationOpacityInstant(1f, 1f);
        }
        else
        {
            BeginFadeToRest();
        }
    }

    private void UpdateFocusPresentation()
    {
        if (card == null)
            return;

        if (IsAlarmState())
        {
            SetPresentationOpacityInstant(1f, 1f);
            return;
        }

        if (!fadingToRest)
            return;

        float progress = Mathf.Clamp01((Time.unscaledTime - fadeStartTime) / activeFadeDuration);
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        presentationLineOpacity = Mathf.Lerp(
            fadeStartLineOpacity,
            activeRestingLineOpacity,
            eased);
        presentationAnchorOpacity = Mathf.Lerp(fadeStartAnchorOpacity, restingAnchorOpacity, eased);
        RefreshColor();

        if (progress >= 1f)
            fadingToRest = false;
    }

    private void BeginFadeToRest()
    {
        BeginFadeToRest(focusToRestDuration);
    }

    private void BeginFadeToRest(float duration)
    {
        fadingToRest = true;
        fadeStartTime = Time.unscaledTime;
        activeFadeDuration = Mathf.Max(0.01f, duration);
        fadeStartLineOpacity = presentationLineOpacity;
        fadeStartAnchorOpacity = presentationAnchorOpacity;
    }

    private void SetPresentationOpacityInstant(float lineOpacity, float anchorOpacity)
    {
        presentationLineOpacity = lineOpacity;
        presentationAnchorOpacity = anchorOpacity;
        RefreshColor();
    }

    private bool IsAlarmState() =>
        card != null && (card.VisualState == StatVisualState.Warning ||
                         card.VisualState == StatVisualState.Critical);

    private void HandleActiveFocusChanged(PerformanceStatCardView activeCard)
    {
        BeginPeerPresentation(ResolvePeerPresentationAlpha());
    }

    private float ResolvePeerPresentationAlpha()
    {
        PerformanceStatCardView activeCard = PerformanceStatCardView.ActiveFocusedCard;
        if (card == null || activeCard == null || activeCard == card || IsAlarmState())
            return 1f;

        return card.UnfocusedPeerOpacity;
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
        float resolvedTarget = ResolvePeerPresentationAlpha();
        if (!Mathf.Approximately(resolvedTarget, peerPresentationTargetAlpha))
            BeginPeerPresentation(resolvedTarget);

        if (!peerPresentationTransitioning)
            return;

        float duration = card != null ? card.PeerFocusTransitionDuration : 0.23f;
        float progress = Mathf.Clamp01(
            (Time.unscaledTime - peerPresentationTransitionStartedAt) /
            Mathf.Max(0.02f, duration));
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
        CachePeerPresentationGroup();
        if (peerPresentationGroup != null)
            peerPresentationGroup.alpha = peerPresentationAlpha;
    }

    private void CachePeerPresentationGroup()
    {
        if (peerPresentationGroup == null)
            peerPresentationGroup = GetComponent<CanvasGroup>();
    }

    private bool ReferencesAreValid()
    {
        return source != null &&
               card != null &&
               worldCamera != null &&
               canvas != null &&
               lineLayer != null &&
               card.ActiveConnectionPoint != null &&
               horizontalSegment != null &&
               angledSegment != null;
    }

    private Camera GetUICamera()
    {
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return canvas.worldCamera != null ? canvas.worldCamera : worldCamera;
    }

    private void SetGeometryVisible(bool visible)
    {
        if (horizontalSegment != null)
            horizontalSegment.gameObject.SetActive(visible);

        if (angledSegment != null)
            angledSegment.gameObject.SetActive(visible);

        if (anchorMarker != null)
            anchorMarker.gameObject.SetActive(visible);
    }

    private static float EaseOutBack(float value)
    {
        value = Mathf.Clamp01(value);
        const float overshoot = 1.70158f;
        const float shifted = 1f + overshoot;
        float x = value - 1f;
        return 1f + shifted * x * x * x + overshoot * x * x;
    }

    private void OnDestroy()
    {
        PerformanceStatCardView.ActiveFocusChanged -= HandleActiveFocusChanged;
        if (card != null)
            card.FocusChanged -= HandleCardFocusChanged;
        DestroyRuntimeMaterial(horizontalMaterialInstance);
        DestroyRuntimeMaterial(angledMaterialInstance);
    }

    private static void DestroyRuntimeMaterial(Material material)
    {
        if (material == null)
            return;

        if (Application.isPlaying)
            Destroy(material);
        else
            DestroyImmediate(material);
    }
}
