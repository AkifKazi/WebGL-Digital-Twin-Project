using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The line from a telemetry card (or the detail panel opened from it) to its
/// sensor on the model. While a detail panel beside the rails shows the card,
/// the line leaves from the panel, and a connector runs from the card to the
/// panel so it is plain which card the panel belongs to. It is drawn last each frame, after the rails have moved
/// the cards, the detail panels have moved, and the views that shift the
/// camera's projection have run, so it always meets both ends of this frame
/// rather than trailing one of them by a frame.
/// </summary>
[DefaultExecutionOrder(100)]
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
    [Tooltip("Resting line opacity while a secondary (inner) rail holds cards. A line that runs " +
             "across another card already fades on its own (Reversed Lines below), so this need not " +
             "be lower than the empty-rails value: lower, the phone's lines, which always have inner " +
             "rows, all but vanish.")]
    [SerializeField, Range(0f, 1f)] private float restingLineOpacity = 0.2f;
    [Tooltip("Resting line opacity used when no secondary rail contains a card.")]
    [SerializeField, Range(0f, 1f)] private float emptySecondaryRailsRestingOpacity = 0.2f;
    [SerializeField, Range(0f, 1f)] private float restingAnchorOpacity = 0.4f;
    [SerializeField, Min(0.05f)] private float focusToRestDuration = 0.23f;
    [Tooltip("Fade duration when secondary-rail occupancy changes the resting line opacity.")]
    [SerializeField, Min(0.05f)] private float secondaryRailOpacityTransitionDuration = 1f;

    [Header("Reversed Lines")]
    [Tooltip("Opacity of a line that doubles back across its own card, because the sensor sits " +
             "on the card's side of the connection point, or that runs across another card or a " +
             "translucent control. Keeps the line off their text.")]
    [SerializeField, Range(0f, 1f)] private float reversedLineOpacity = 0.02f;
    [Tooltip("The same fade for warning and critical lines, a little above the normal fade. " +
             "Their anchor stays at full strength, so the sensor is still easy to find.")]
    [SerializeField, Range(0f, 1f)] private float reversedAlarmLineOpacity = 0.08f;
    [Tooltip("Seconds a line takes to fade when it turns back across its card or crosses " +
             "another card, and to return.")]
    [SerializeField, Min(0.01f)] private float reversedLineFadeDuration = 0.7f;
    [Tooltip("Also fade a line where it runs across another card or a translucent control " +
             "(a LeaderLineObstacle), not only when it doubles back across its own card.")]
    [SerializeField] private bool fadeLinesCrossingOtherCards = true;
    [Tooltip("How far into another card or control, in canvas units, a line has to cut before " +
             "it fades, and how far clear it has to be to come back. Stops a line grazing an " +
             "edge from flickering.")]
    [SerializeField, Min(0f)] private float cardCrossingMargin = 6f;
    [Tooltip("How far past the connection point, in canvas units, the sensor has to move before " +
             "the line changes direction. Stops the fade flickering at the edge.")]
    [SerializeField, Min(0f)] private float reversedDirectionMargin = 12f;
    [Tooltip("Off: warning and critical lines fade to the alarm opacity above when they turn " +
             "back across their card or cross another. On: they stay at full opacity.")]
    [SerializeField] private bool keepAlarmLinesOpaqueWhenReversed;

    [Header("Panel Connector")]
    [Tooltip("Lowest opacity of the connector from a card to its open detail panel. A pinned " +
             "panel's card rests like any other, but its connector must stay easy to follow.")]
    [SerializeField, Range(0f, 1f)] private float restingConnectorOpacity = 0.35f;

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
    private Material horizontalSourceMaterial;
    private Material angledSourceMaterial;
    private int revealMaterialState = UnknownRevealState;
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
    private SmoothFade peerFade = new(1f);
    private bool cardFocused;
    private RectTransform connectorSegment;
    private Image connectorImage;
    private bool connectorActive;
    private bool connectorCrossing;
    private SmoothFade connectorCrossingFade = new(1f);
    private bool lineReversed;
    private bool directionKnown;
    private float directionOpacity = 1f;
    private float directionStartOpacity = 1f;
    private float directionTargetOpacity = 1f;
    private float directionTransitionStartedAt;
    private bool crossingUi;
    private Vector2 lastBendPoint;

    // Each card's line, so the rails can keep a focused card's line clear of
    // the cards on the rails inside it (see TryGetPath).
    private static readonly Dictionary<PerformanceStatCardView, StatLeaderLineView> ByCard = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode() => ByCard.Clear();

    /// <summary>
    /// Where <paramref name="lineCard"/>'s line is drawn now, in world space:
    /// the card's end, the bend and the sensor, in <paramref name="path"/>
    /// (three points). False while the card has no line drawn.
    /// </summary>
    public static bool TryGetPath(PerformanceStatCardView lineCard, Vector3[] path)
    {
        if (lineCard == null || !ByCard.TryGetValue(lineCard, out StatLeaderLineView line) || line == null ||
            !line.isActiveAndEnabled || !line.geometryValid || line.lineLayer == null)
            return false;
        path[0] = line.lineLayer.TransformPoint(line.lastCardPoint);
        path[1] = line.lineLayer.TransformPoint(line.lastBendPoint);
        path[2] = line.lineLayer.TransformPoint(line.lastSensorPoint);
        return true;
    }

    private void Unregister()
    {
        if (card != null && ByCard.TryGetValue(card, out StatLeaderLineView line) && line == this)
            ByCard.Remove(card);
    }
    private Vector2 lastCardPoint;
    private Vector2 lastSensorPoint;
    private bool geometryValid;
    private float appliedRevealDistance = float.NaN;
    private float appliedAngledLength = float.NaN;
    private float appliedHorizontalLength = float.NaN;
    private bool horizontalActive = true;
    private bool angledActive = true;
    private bool anchorActive = true;
    private float appliedAnchorProgress = float.NaN;
    private IReadOnlyList<PerformanceStatCardView> cardsToAvoid;
    private IReadOnlyList<LeaderLineObstacle> obstaclesToAvoid;
    private static readonly Vector3[] CardCorners = new Vector3[4];

    private const int UnknownRevealState = -1;
    private const int SharedRevealState = 0;
    private const int ClonedRevealState = 1;

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
    /// <summary>
    /// True while the line doubles back across its own card. A line leaving
    /// from a detail panel never does: if it turns back, it runs behind the
    /// opaque panel.
    /// </summary>
    public bool IsReversed => lineReversed && !LeavesFromPanel;

    /// <summary>True while the line runs across another card or a translucent control.</summary>
    public bool IsCrossingUi => crossingUi;

    private bool LeavesFromPanel => card != null && card.ConnectsFromPanel;
    public float DirectionOpacity => directionOpacity;
    public float FadedDirectionOpacity => IsAlarmState() ? reversedAlarmLineOpacity : reversedLineOpacity;

    private void Awake()
    {
        activeRestingLineOpacity = restingLineOpacity;
        activeFadeDuration = focusToRestDuration;
        CacheImages();
        CreateRuntimeMaterials();

        if (anchorMarker != null)
            anchorOriginalScale = anchorMarker.localScale;

        // Seed the cached activation state from what the prefab actually has.
        horizontalActive = horizontalSegment != null && horizontalSegment.gameObject.activeSelf;
        angledActive = angledSegment != null && angledSegment.gameObject.activeSelf;
        anchorActive = anchorMarker != null && anchorMarker.gameObject.activeSelf;

        ApplyAnchorAppearance();
    }

    private void OnEnable()
    {
        CardFocus.Changed -= HandleFocusChanged;
        CardFocus.Changed += HandleFocusChanged;
        CachePeerPresentationGroup();
        ApplyPeerPresentationInstant(CardFocus.PeerAlpha(card));
    }

    private void OnDisable()
    {
        CardFocus.Changed -= HandleFocusChanged;
        ApplyPeerPresentationInstant(1f);
    }

    public void Bind(
        PerformanceStatSource newSource,
        PerformanceStatCardView newCard,
        Camera newWorldCamera,
        Canvas newCanvas,
        RectTransform newLineLayer)
    {
        Unregister();
        source = newSource;
        card = newCard;
        if (card != null)
            ByCard[card] = this;
        worldCamera = newWorldCamera;
        canvas = newCanvas;
        lineLayer = newLineLayer;
        gameObject.name = newSource != null && !string.IsNullOrWhiteSpace(newSource.MetricName)
            ? $"Leader Line - {newSource.MetricName}"
            : "Leader Line";
        cardFocused = card != null && card.IsFocused;
        if (IsAlarmState() || cardFocused)
            SetPresentationOpacityInstant(1f, 1f);
        else
            SetPresentationOpacityInstant(activeRestingLineOpacity, restingAnchorOpacity);
        fadingToRest = false;
        directionKnown = false;
        crossingUi = false;
        geometryValid = false;
        connectorCrossing = false;
        connectorCrossingFade.Snap(1f);
        ApplyPeerPresentationInstant(CardFocus.PeerAlpha(card));
        CacheImages();
        CreateRuntimeMaterials();
        PrepareLinePart(horizontalSegment);
        PrepareLinePart(angledSegment);
        RefreshColor();
        UpdateLine();
    }

    public void Unbind()
    {
        Unregister();
        source = null;
        card = null;
        cardsToAvoid = null;
        obstaclesToAvoid = null;
        worldCamera = null;
        canvas = null;
        lineLayer = null;
        gameObject.name = "Leader Line (Pooled)";
        geometryValid = false;
        SetGeometryVisible(false);
    }

    private void LateUpdate()
    {
        UpdatePeerPresentation();
        UpdateFocusPresentation();
        UpdateLine();
        UpdateConnector();
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

    /// <summary>
    /// The cards laid out with this line, and the translucent controls of its
    /// layout. It fades where it runs across any of them other than its own
    /// card. The lists are read every frame, not copied.
    /// </summary>
    public void SetUiToAvoid(
        IReadOnlyList<PerformanceStatCardView> cards,
        IReadOnlyList<LeaderLineObstacle> obstacles)
    {
        cardsToAvoid = cards;
        obstaclesToAvoid = obstacles;
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
        // An unchanged rest level must not restart a fade already under way.
        if (!applyInstantly && Mathf.Approximately(activeRestingLineOpacity, Mathf.Clamp01(opacity)))
            return;
        activeRestingLineOpacity = Mathf.Clamp01(opacity);

        // Focused, warning and critical lines stay at 100%. The new resting
        // target is retained and used when they next return to rest.
        if (card != null && (cardFocused || IsAlarmState()))
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

        // The point the rails place the card by: the sensor's projection on
        // screen, held just past the edge when it is further out or behind the
        // camera, so the line runs off that edge towards it.
        Vector2 sensorScreenPoint = TelemetryPresentationBuilder.PlacementScreenPoint(
            worldCamera,
            card.WorldAnchor.position);

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

        // Rebuilding the elbow resizes and rotates two UI images, which dirties
        // their meshes and makes the canvas rebuild. The camera is still for most
        // of a session, so the geometry is only rebuilt when an endpoint actually
        // moved; a sub-pixel move is not visible and does not count.
        const float MoveEpsilon = 0.01f;
        if (!geometryValid ||
            (cardPoint - lastCardPoint).sqrMagnitude > MoveEpsilon ||
            (sensorPoint - lastSensorPoint).sqrMagnitude > MoveEpsilon)
        {
            lastCardPoint = cardPoint;
            lastSensorPoint = sensorPoint;
            geometryValid = true;
            DrawElbow(cardPoint, sensorPoint);
        }
        else
        {
            ApplyRevealToMaterials();
            ApplyAnchorAppearance();
        }

        UpdateDirectionPresentation(cardPoint, sensorPoint);
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

        lastBendPoint = bendPoint;

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

        // A zero-length segment is collapsed rather than deactivated: the
        // caller re-activates every part afterwards, which would otherwise show
        // the segment again at its previous, stale size.
        if (length <= 0.01f)
        {
            segment.anchoredPosition = start;
            segment.sizeDelta = new Vector2(0f, lineThickness);
            return 0f;
        }

        float angle = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg;
        segment.anchoredPosition = (start + end) * 0.5f;
        segment.sizeDelta = new Vector2(length, lineThickness);
        segment.localRotation = Quaternion.Euler(0f, 0f, angle);
        return length;
    }

    private void ApplyRevealToMaterials()
    {
        bool revealing = revealProgress < 1f;
        ApplyRevealMaterials(revealing);

        // A fully revealed line draws from the shared UI material, which must
        // never be written to: it is the one every other UI element uses.
        if (!revealing || totalPathLength <= 0.01f)
            return;

        float revealDistance = revealProgress * totalPathLength;

        // Writing a material property is a native call that also invalidates the
        // shader's constant buffer, so the reveal only re-uploads when one of its
        // three inputs actually moved.
        if (Mathf.Approximately(appliedRevealDistance, revealDistance) &&
            Mathf.Approximately(appliedAngledLength, angledLength) &&
            Mathf.Approximately(appliedHorizontalLength, horizontalLength))
        {
            return;
        }

        appliedRevealDistance = revealDistance;
        appliedAngledLength = angledLength;
        appliedHorizontalLength = horizontalLength;

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

        material.SetFloat(SegmentStartDistanceId, segmentStartDistance);
        material.SetFloat(SegmentLengthId, segmentLength);
        material.SetFloat(RevealDistanceId, revealDistance);
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

    /// <summary>
    /// Records the prefab's own line material, once, before anything swaps it.
    /// Graphic.material reports the shared default material once it has been
    /// cleared, so capturing after a swap would capture the wrong material and
    /// the line would never get its reveal shader back.
    /// </summary>
    private void CaptureSourceMaterials()
    {
        if (revealMaterialState != UnknownRevealState)
            return;

        if (horizontalSourceMaterial == null && horizontalImage != null)
            horizontalSourceMaterial = horizontalImage.material;

        if (angledSourceMaterial == null && angledImage != null)
            angledSourceMaterial = angledImage.material;
    }

    private void CreateRuntimeMaterials()
    {
        CaptureSourceMaterials();

        if (horizontalMaterialInstance == null)
        {
            horizontalMaterialInstance =
                CreateMaterialInstance(horizontalSourceMaterial, "Horizontal");
        }

        if (angledMaterialInstance == null)
            angledMaterialInstance = CreateMaterialInstance(angledSourceMaterial, "Angled");

        ApplyRevealMaterials(revealProgress < 1f);
    }

    private static Material CreateMaterialInstance(Material sourceMaterial, string suffix)
    {
        if (sourceMaterial == null)
            return null;

        return new Material(sourceMaterial)
        {
            name = sourceMaterial.name + " - " + suffix + " Instance",
            hideFlags = HideFlags.DontSave
        };
    }

    /// <summary>
    /// The reveal material is a per-line clone, and a clone is its own UI batch:
    /// every leader line on screen costs two extra draw calls, and splits the
    /// batch of whatever the canvas draws between them. The shader only masks the
    /// part of the path beyond the reveal distance, so once a line is fully
    /// revealed - which is the whole of the steady state, the intro aside - it
    /// draws exactly the vertex colour the default UI material draws. Handing the
    /// images back to that shared material there is pixel for pixel identical and
    /// lets every line batch again.
    /// </summary>
    private void ApplyRevealMaterials(bool useRevealMaterial)
    {
        int wanted = useRevealMaterial ? ClonedRevealState : SharedRevealState;
        if (revealMaterialState == wanted)
            return;

        revealMaterialState = wanted;

        // The clones keep whatever the reveal last wrote while they are parked,
        // so a line that starts revealing again must re-send its path values.
        appliedRevealDistance = float.NaN;
        appliedAngledLength = float.NaN;
        appliedHorizontalLength = float.NaN;

        if (horizontalImage != null && horizontalSourceMaterial != null)
            horizontalImage.material = useRevealMaterial ? horizontalMaterialInstance : null;

        if (angledImage != null && angledSourceMaterial != null)
            angledImage.material = useRevealMaterial ? angledMaterialInstance : null;
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

        // Focus, rail occupancy and direction each scale the line on their own,
        // so a reversed line still dims further when another card has focus.
        float lineOpacity = presentationLineOpacity * directionOpacity;

        if (horizontalImage != null)
        {
            Color color = lineColor;
            color.a *= lineOpacity;
            horizontalImage.color = color;
        }

        if (angledImage != null)
        {
            Color color = lineColor;
            color.a *= lineOpacity;
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

        // Writing localScale dirties the transform and the UI element under it,
        // and this runs every frame; the scale only moves while the anchor is
        // animating in.
        if (!Mathf.Approximately(appliedAnchorProgress, anchorProgress))
        {
            appliedAnchorProgress = anchorProgress;
            float eased = EaseOutBack(anchorProgress);
            float scale = Mathf.Lerp(0.65f, 1f, eased);
            anchorMarker.localScale = anchorOriginalScale * scale;
        }

        Color lineColor = matchCardStateColor && card != null
            ? card.StateColor
            : defaultColor;

        ApplyAnchorColor(lineColor);
    }

    /// <summary>
    /// A line leaves its card away from the rail: rightwards from the left rail,
    /// leftwards from the right, down from the top and up from the bottom. When
    /// the sensor sits behind the connection point the line runs back across the
    /// card, and fades so the card stays readable.
    /// </summary>
    private void UpdateDirectionPresentation(Vector2 cardPoint, Vector2 sensorPoint)
    {
        Vector2 outward = card.RailSide switch
        {
            StatRailSide.Left => Vector2.right,
            StatRailSide.Right => Vector2.left,
            StatRailSide.Top => Vector2.down,
            _ => Vector2.up
        };
        float travel = Vector2.Dot(sensorPoint - cardPoint, outward);

        if (!directionKnown)
            lineReversed = travel < 0f;
        else if (lineReversed && travel > reversedDirectionMargin)
            lineReversed = false;
        else if (!lineReversed && travel < -reversedDirectionMargin)
            lineReversed = true;

        crossingUi = fadeLinesCrossingOtherCards &&
                     CrossesOtherUi(cardPoint, lastBendPoint, sensorPoint, directionKnown && crossingUi);

        bool fade = (IsReversed || crossingUi) &&
                    !(keepAlarmLinesOpaqueWhenReversed && IsAlarmState());
        float target = fade ? FadedDirectionOpacity : 1f;

        if (!directionKnown)
        {
            // A freshly bound line takes its direction without animating.
            directionKnown = true;
            directionOpacity = directionStartOpacity = directionTargetOpacity = target;
            return;
        }

        if (!Mathf.Approximately(target, directionTargetOpacity))
        {
            directionStartOpacity = directionOpacity;
            directionTargetOpacity = target;
            directionTransitionStartedAt = Time.unscaledTime;
        }

        float progress = Mathf.Clamp01(
            (Time.unscaledTime - directionTransitionStartedAt) / reversedLineFadeDuration);
        directionOpacity = Mathf.Lerp(
            directionStartOpacity,
            directionTargetOpacity,
            Mathf.SmoothStep(0f, 1f, progress));
    }

    /// <summary>
    /// True when either segment runs through another visible card or a shown
    /// translucent control. Entering needs the line to cut
    /// <see cref="cardCrossingMargin"/> into it; leaving needs it to clear it
    /// by the same margin.
    /// </summary>
    private bool CrossesOtherUi(Vector2 cardPoint, Vector2 bendPoint, Vector2 sensorPoint, bool wasCrossing)
    {
        float grow = wasCrossing ? cardCrossingMargin : -cardCrossingMargin;
        if (cardsToAvoid != null)
        {
            foreach (PerformanceStatCardView other in cardsToAvoid)
            {
                if (other == null || other == card || !other.isActiveAndEnabled ||
                    (other.TryGetComponent(out CanvasGroup group) && group.alpha < 0.05f))
                    continue;
                if (Crosses((RectTransform)other.transform, cardPoint, bendPoint, sensorPoint, grow))
                    return true;
            }
        }

        if (obstaclesToAvoid != null)
        {
            foreach (LeaderLineObstacle obstacle in obstaclesToAvoid)
            {
                if (obstacle != null && obstacle.IsShown &&
                    Crosses(obstacle.Rect, cardPoint, bendPoint, sensorPoint, grow))
                    return true;
            }
        }

        return false;
    }

    private bool Crosses(RectTransform rect, Vector2 cardPoint, Vector2 bendPoint, Vector2 sensorPoint, float grow)
    {
        if (!TryGetLocalBounds(rect, grow, out Vector2 min, out Vector2 max))
            return false;

        return SegmentHitsRect(cardPoint, bendPoint, min, max) ||
               SegmentHitsRect(bendPoint, sensorPoint, min, max);
    }

    /// <summary>The rect's bounds in the line layer, grown by <paramref name="grow"/>. False when nothing is left of it.</summary>
    private bool TryGetLocalBounds(RectTransform rect, float grow, out Vector2 min, out Vector2 max)
    {
        rect.GetWorldCorners(CardCorners);
        min = lineLayer.InverseTransformPoint(CardCorners[0]);
        max = min;
        for (int i = 1; i < 4; i++)
        {
            Vector2 corner = lineLayer.InverseTransformPoint(CardCorners[i]);
            min = Vector2.Min(min, corner);
            max = Vector2.Max(max, corner);
        }
        min -= Vector2.one * grow;
        max += Vector2.one * grow;
        return min.x < max.x && min.y < max.y;
    }

    /// <summary>Liang-Barsky clip of a segment against an axis-aligned rectangle.</summary>
    private static bool SegmentHitsRect(Vector2 start, Vector2 end, Vector2 min, Vector2 max)
    {
        Vector2 delta = end - start;
        float enter = 0f;
        float exit = 1f;
        return Clip(-delta.x, start.x - min.x, ref enter, ref exit) &&
               Clip(delta.x, max.x - start.x, ref enter, ref exit) &&
               Clip(-delta.y, start.y - min.y, ref enter, ref exit) &&
               Clip(delta.y, max.y - start.y, ref enter, ref exit);
    }

    private static bool Clip(float direction, float distance, ref float enter, ref float exit)
    {
        if (Mathf.Approximately(direction, 0f))
            return distance >= 0f;

        float t = distance / direction;
        if (direction < 0f)
        {
            if (t > exit)
                return false;
            enter = Mathf.Max(enter, t);
        }
        else
        {
            if (t < enter)
                return false;
            exit = Mathf.Min(exit, t);
        }

        return true;
    }

    private void HandleFocusChanged(PerformanceStatCardView focusedCard)
    {
        bool focused = card != null && focusedCard == card;
        peerFade.Begin(CardFocus.PeerAlpha(card), Time.unscaledTime);
        if (card == null || focused == cardFocused)
            return;
        cardFocused = focused;

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

    private void UpdatePeerPresentation()
    {
        // The card's alarm state can change while the focus stays put.
        peerFade.Begin(CardFocus.PeerAlpha(card), Time.unscaledTime);
        float duration = card != null ? card.PeerFocusTransitionDuration : 0.23f;
        if (peerFade.Step(Time.unscaledTime, duration))
            ApplyPeerPresentationAlpha(peerFade.Value);
    }

    private void ApplyPeerPresentationInstant(float alpha)
    {
        peerFade.Snap(alpha);
        ApplyPeerPresentationAlpha(peerFade.Value);
    }

    private void ApplyPeerPresentationAlpha(float alpha)
    {
        CachePeerPresentationGroup();
        if (peerPresentationGroup != null)
            peerPresentationGroup.alpha = alpha;
    }

    private void CachePeerPresentationGroup()
    {
        if (peerPresentationGroup == null)
            peerPresentationGroup = GetComponent<CanvasGroup>();
    }

    /// <summary>
    /// The connector from the card's own edge to the detail panel showing it,
    /// while one does: straight across the rails between them, in the line's
    /// colour, fading in and out with the panel. The cards on the rails it
    /// crosses step aside for it; where one could not, it fades as a line
    /// crossing a card does.
    /// </summary>
    private void UpdateConnector()
    {
        if (!horizontalActive || card == null || lineLayer == null ||
            !CardFocus.TryGetDetailLink(card, out ICardDetailLink link) ||
            link.CardSidePoint == null || card.OwnConnectionPoint == null)
        {
            SetConnectorActive(false);
            return;
        }

        Camera uiCamera = GetUICamera();
        if (!ToLineLayer(card.OwnConnectionPoint.position, uiCamera, out Vector2 from) ||
            !ToLineLayer(link.CardSidePoint.position, uiCamera, out Vector2 to) ||
            !EnsureConnector())
        {
            SetConnectorActive(false);
            return;
        }

        if (!connectorActive)
        {
            connectorCrossing = false;
            connectorCrossingFade.Snap(1f);
        }
        SetLineSegment(connectorSegment, from, to);

        bool crossing = fadeLinesCrossingOtherCards && ConnectorCrossesCards(from, to, connectorCrossing);
        if (crossing != connectorCrossing)
        {
            connectorCrossing = crossing;
            connectorCrossingFade.Begin(crossing ? FadedDirectionOpacity : 1f, Time.unscaledTime);
        }
        connectorCrossingFade.Step(Time.unscaledTime, reversedLineFadeDuration);

        Color color = LineColor();
        color.a *= Mathf.Max(presentationLineOpacity, restingConnectorOpacity) *
                   connectorCrossingFade.Value * Mathf.Clamp01(link.Alpha);
        connectorImage.color = color;
        SetConnectorActive(true);
    }

    private bool EnsureConnector()
    {
        if (connectorSegment != null)
            return true;
        if (horizontalSegment == null)
            return false;

        // A copy of the line's first segment, so it draws exactly as the line does.
        GameObject copy = Instantiate(horizontalSegment.gameObject, horizontalSegment.parent, false);
        copy.name = "Panel Connector";
        copy.transform.SetSiblingIndex(horizontalSegment.GetSiblingIndex());
        connectorSegment = (RectTransform)copy.transform;
        PrepareLinePart(connectorSegment);
        connectorImage = copy.GetComponent<Image>();
        if (connectorImage != null)
            connectorImage.material = null;
        connectorActive = true;
        SetConnectorActive(false);
        return connectorImage != null;
    }

    private void SetConnectorActive(bool active)
    {
        if (connectorSegment == null)
            return;
        SetPartActive(connectorSegment, active, ref connectorActive);
    }

    private bool ConnectorCrossesCards(Vector2 from, Vector2 to, bool wasCrossing)
    {
        if (cardsToAvoid == null)
            return false;
        float grow = wasCrossing ? cardCrossingMargin : -cardCrossingMargin;
        foreach (PerformanceStatCardView other in cardsToAvoid)
        {
            if (other == null || other == card || !other.isActiveAndEnabled ||
                (other.TryGetComponent(out CanvasGroup group) && group.alpha < 0.05f))
                continue;
            if (TryGetLocalBounds((RectTransform)other.transform, grow, out Vector2 min, out Vector2 max) &&
                SegmentHitsRect(from, to, min, max))
                return true;
        }
        return false;
    }

    private bool ToLineLayer(Vector3 worldPoint, Camera uiCamera, out Vector2 local) =>
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            lineLayer, RectTransformUtility.WorldToScreenPoint(uiCamera, worldPoint), uiCamera, out local);

    private Color LineColor() => matchCardStateColor && card != null ? card.StateColor : defaultColor;

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
        if (!visible)
            SetConnectorActive(false);
        SetPartActive(horizontalSegment, visible, ref horizontalActive);
        SetPartActive(angledSegment, visible, ref angledActive);
        SetPartActive(anchorMarker, visible, ref anchorActive);
    }

    /// <summary>
    /// SetActive on an object already in that state still crosses into native
    /// code and re-walks the child hierarchy. These run every frame for every
    /// line, so the state is tracked here and only real changes are sent.
    /// </summary>
    private static void SetPartActive(RectTransform part, bool active, ref bool current)
    {
        if (part == null || current == active)
            return;

        current = active;
        part.gameObject.SetActive(active);
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
        Unregister();
        CardFocus.Changed -= HandleFocusChanged;
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
