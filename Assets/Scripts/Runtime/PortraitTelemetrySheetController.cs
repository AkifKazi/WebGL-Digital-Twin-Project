using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The telemetry sheet on a phone held upright. A tap on a card focuses it; a
/// second tap on the focused card (see
/// <see cref="PerformanceStatCardView.DetailRequested"/>) brings that sensor's
/// detail panel up from the bottom of the screen as a sheet, the same panel
/// the wide layout shows beside its rails, drawn larger for a thumb, without
/// the pin and with a large close button. While it is open every other piece
/// of telemetry UI fades away and the model moves up into the space above the
/// sheet, so only the model and the readings are on screen.
///
/// The close button, a tap on the model, Esc, or switching to Service closes
/// the sheet; the cards come back as they were.
///
/// The first tap that focuses a card shows a hint that a second tap opens the
/// sheet, once per visit, until the person has opened one.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(45)]
public sealed class PortraitTelemetrySheetController : MonoBehaviour
{
    private const string HintLearnedKey = "DigitalTwin.TelemetrySheetTapLearned";

    [Header("References")]
    [Tooltip("The portrait layout's view controller: it hides the telemetry UI while the sheet is open.")]
    [SerializeField] private PortraitServiceModeController modeController;

    [Tooltip("Only cards under this root open the sheet: the portrait layout.")]
    [SerializeField] private RectTransform cardRoot;

    [Tooltip("The portrait bottom card rail. The hint sits just above it.")]
    [SerializeField] private RectTransform hintAnchor;

    [Tooltip("The portrait top bar. The model centres between it and the sheet.")]
    [SerializeField] private RectTransform topBar;

    [SerializeField] private TelemetryDetailPanel panelPrefab;
    [SerializeField] private Camera targetCamera;

    [Tooltip("Supplies the history the graphs draw: the Telemetry History Recorder.")]
    [SerializeField] private MonoBehaviour historyProvider;

    [Tooltip("Optional. Supplies text for the detail section.")]
    [SerializeField] private MonoBehaviour insightProvider;

    [Tooltip("Optional. Supplies the hours until a sensor's part is due: the Telemetry Service Schedule.")]
    [SerializeField] private MonoBehaviour serviceLifeProvider;

    [Header("Graphs")]
    [SerializeField] private List<TelemetryGraphKind> graphs = new()
    {
        TelemetryGraphKind.StatusTimeline,
        TelemetryGraphKind.KeyFigures,
        TelemetryGraphKind.LineChart
    };

    [SerializeField] private List<TelemetryKeyFigure> keyFigures = new()
    {
        TelemetryKeyFigure.Average,
        TelemetryKeyFigure.TimeToService
    };

    [SerializeField] private TelemetryKeyFigureLayout keyFigureLayout = TelemetryKeyFigureLayout.MakeRoomForLongValues;
    [SerializeField] private TelemetryHistoryRange defaultRange = TelemetryHistoryRange.Hour;
    [SerializeField] private TelemetryGraphStyle graphStyle = new();

    [Header("Sheet")]
    [Tooltip("Size the panel is drawn at. The portrait canvas is 1080 units across a phone, so 2.4 " +
             "draws the panel about 440 units wide, a little wider than beside the wide layout's rails.")]
    [SerializeField, Range(1f, 3f)] private float sheetScale = 2.4f;

    [Tooltip("Space between the sheet and the sides of the screen.")]
    [SerializeField, Min(0f)] private float sideMargin = 8f;

    [Tooltip("The close button's size in the sheet, and how far its touch area reaches beyond it. " +
             "44 + 2 x 10 units at 2.4 is about 55 points on a phone.")]
    [SerializeField, Min(24f)] private float closeSize = 44f;
    [SerializeField, Min(0f)] private float closeHitPadding = 10f;

    [SerializeField, Min(0.01f)] private float transitionSeconds = 0.28f;
    [SerializeField, Min(0.1f)] private float refreshInterval = 1f;

    [Header("Hint")]
    [SerializeField] private TMP_FontAsset hintFont;
    [SerializeField, Min(0.5f)] private float hintSeconds = 2.6f;
    [SerializeField] private Color hintFill = new(0.031f, 0.086f, 0.118f, 0.94f);
    [SerializeField] private Color hintEdge = new(0f, 0.922f, 1f, 0.6f);
    [SerializeField] private Color hintInk = new(0.918f, 0.992f, 1f, 1f);

    private readonly List<PerformanceStatCardView> hintCards = new();
    private TelemetryDetailPanel panel;
    private RectTransform hint;
    private CanvasGroup hintGroup;
    private float hintUntil;
    private bool hintShownThisVisit;
    private bool open;
    private float blend;
    private float nextRefresh;
    private bool projectionShifted;
    private Canvas rootCanvas;
    private bool searchedForInsight;
    private static readonly Vector3[] Corners = new Vector3[4];

    public bool IsOpen => open;
    public TelemetryDetailPanel Panel => panel;
    public bool HintShowing => hint != null && hintGroup.alpha > 0.01f;

    private ITelemetryHistoryProvider History => historyProvider as ITelemetryHistoryProvider;

    private ITelemetryInsightProvider Insight
    {
        get
        {
            // As the wide layout: AI insights sent by the page fill the detail section.
            if (insightProvider == null && !searchedForInsight)
            {
                searchedForInsight = true;
                insightProvider = FindAnyObjectByType<TelemetryAiDataTable>();
            }
            return insightProvider as ITelemetryInsightProvider;
        }
    }

    private RectTransform Layer => (RectTransform)transform;

    private void Awake()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;
    }

    private void OnEnable()
    {
        PerformanceStatCardView.DetailRequested += HandleDetailRequested;
        PerformanceStatCardView.Clicked += HandleClicked;
        TelemetryCardFocusReleaser.Dismissed += Close;
    }

    private void OnDisable()
    {
        PerformanceStatCardView.DetailRequested -= HandleDetailRequested;
        PerformanceStatCardView.Clicked -= HandleClicked;
        TelemetryCardFocusReleaser.Dismissed -= Close;

        // The wide layout takes over: nothing of the sheet stays behind.
        open = false;
        blend = 0f;
        if (panel != null)
        {
            panel.Unbind();
            panel.SetAlpha(0f);
        }
        if (hintGroup != null)
            hintGroup.alpha = 0f;
        hintUntil = 0f;
        modeController?.SetTelemetryCover(0f);
        ResetProjection();
    }

    // -----------------------------------------------------------------------
    // Cards
    // -----------------------------------------------------------------------

    private bool IsOwnCard(PerformanceStatCardView card) =>
        card != null && cardRoot != null && card.transform.IsChildOf(cardRoot) &&
        (modeController == null || modeController.CurrentMode == DigitalTwinMode.Telemetry);

    private void HandleDetailRequested(PerformanceStatCardView card)
    {
        if (!IsOwnCard(card) || panelPrefab == null || History == null || !History.IsReady)
            return;

        Open(card);
        // Learned: the hint never shows again.
        hintUntil = 0f;
        try
        {
            PlayerPrefs.SetInt(HintLearnedKey, 1);
            PlayerPrefs.Save();
        }
        catch (System.Exception)
        {
            // Storage the browser refuses only means the hint may show again.
        }
    }

    private void HandleClicked(PerformanceStatCardView card)
    {
        // Any pointer: this layout is a phone's, and the hint must not hang on
        // telling a finger from a mouse.
        if (!IsOwnCard(card) || !card.IsFocused || open || hintShownThisVisit)
            return;
        if (PlayerPrefs.GetInt(HintLearnedKey, 0) == 1)
            return;
        hintShownThisVisit = true;
        ShowHint();
    }

    // -----------------------------------------------------------------------
    // Opening and closing
    // -----------------------------------------------------------------------

    private void Open(PerformanceStatCardView card)
    {
        EnsurePanel();
        bool wasOpen = open;
        open = true;
        panel.Bind(card);
        panel.SelectRange(defaultRange);
        nextRefresh = Time.unscaledTime + refreshInterval;
        if (!wasOpen)
            blend = Mathf.Min(blend, 0.999f);
    }

    /// <summary>Closes the sheet; the card it showed is released.</summary>
    public void Close()
    {
        if (!open)
            return;
        open = false;
        if (panel != null && panel.Card != null && panel.Card.IsFocused)
            panel.Card.ReleaseFocus();
    }

    private void EnsurePanel()
    {
        if (panel != null)
            return;

        panel = Instantiate(panelPrefab, Layer);
        panel.name = "Telemetry Sheet";
        panel.Initialise(graphs, keyFigures, keyFigureLayout, graphStyle, defaultRange,
            History, Insight, serviceLifeProvider as ITelemetryServiceLifeProvider);
        panel.UseAsSheet(closeSize, closeHitPadding);
        panel.CloseRequested += _ => Close();

        RectTransform rect = panel.RectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.localScale = new Vector3(sheetScale, sheetScale, 1f);
    }

    // -----------------------------------------------------------------------
    // Every frame
    // -----------------------------------------------------------------------

    private void LateUpdate()
    {
        float target = open ? 1f : 0f;
        blend = Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime / transitionSeconds);
        float eased = blend * blend * (3f - 2f * blend);

        modeController?.SetTelemetryCover(eased);

        if (panel != null)
        {
            if (open && Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + refreshInterval;
                panel.Refresh();
            }
            PlaceSheet(eased);
            if (!open && blend <= 0f && panel.Card != null)
                panel.Unbind();
        }

        ApplyProjection(eased);
        UpdateHint();
    }

    /// <summary>Along the bottom of the safe area, the full width less the margins, rising as it fades in.</summary>
    private void PlaceSheet(float eased)
    {
        // Never more than three quarters of the space under the top bar: a
        // long detail text draws the sheet smaller rather than over the switch.
        float scale = sheetScale;
        float available = Layer.rect.height - TopBand();
        if (panel.Height > 1f && available > 1f)
            scale = Mathf.Clamp(available * 0.75f / panel.Height, 1f, sheetScale);
        Vector3 scaleVector = new(scale, scale, 1f);
        if (panel.RectTransform.localScale != scaleVector)
            panel.RectTransform.localScale = scaleVector;

        float width = Mathf.Max(1f, (Layer.rect.width - sideMargin * 2f) / scale);
        panel.SetWidth(width);
        float rise = (1f - eased) * panel.Height * scale * 0.25f;
        panel.RectTransform.anchoredPosition = new Vector2(0f, -rise);
        panel.SetAlpha(eased);
    }

    /// <summary>Height from the top of the layer to the bottom of the top bar.</summary>
    private float TopBand()
    {
        if (topBar == null)
            return 0f;
        topBar.GetWorldCorners(Corners);
        return Mathf.Max(0f, Layer.rect.yMax - Layer.InverseTransformPoint(Corners[0]).y);
    }

    /// <summary>
    /// Moves the picture up so the model centres between the top bar and the
    /// sheet, by an off-centre projection as the service view does sideways:
    /// orbiting, zoom and screen-point conversions behave as before.
    /// </summary>
    private void ApplyProjection(float eased)
    {
        if (targetCamera == null || panel == null || eased <= 0.0001f)
        {
            ResetProjection();
            return;
        }

        float screenHeight = ScreenHeight(out Camera uiCamera);
        if (screenHeight <= 0f)
            return;

        panel.RectTransform.GetWorldCorners(Corners);
        float sheetTop = RectTransformUtility.WorldToScreenPoint(uiCamera, Corners[1]).y;
        float barBottom = screenHeight;
        if (topBar != null)
        {
            topBar.GetWorldCorners(Corners);
            barBottom = RectTransformUtility.WorldToScreenPoint(uiCamera, Corners[0]).y;
        }

        // The centre of the space left, in normalised device coordinates (-1 to 1).
        float centre = (sheetTop + barBottom) / screenHeight - 1f;
        Matrix4x4 projection = Matrix4x4.Perspective(
            targetCamera.fieldOfView, targetCamera.aspect, targetCamera.nearClipPlane, targetCamera.farClipPlane);
        // A point straight ahead lands at y = -m12.
        projection.m12 = -centre * eased;
        targetCamera.projectionMatrix = projection;
        projectionShifted = true;
    }

    private void ResetProjection()
    {
        if (!projectionShifted || targetCamera == null)
            return;
        targetCamera.ResetProjectionMatrix();
        projectionShifted = false;
    }

    private float ScreenHeight(out Camera uiCamera)
    {
        if (rootCanvas == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            rootCanvas = canvas != null ? canvas.rootCanvas : null;
        }
        // Overlay canvases draw in screen pixels; a camera canvas (as the
        // capture tests use) is measured through its own camera.
        uiCamera = rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? rootCanvas.worldCamera
            : null;
        return uiCamera != null ? uiCamera.pixelHeight : Screen.height;
    }

    // -----------------------------------------------------------------------
    // Hint
    // -----------------------------------------------------------------------

    private void ShowHint()
    {
        EnsureHint();
        hintUntil = Time.unscaledTime + hintSeconds;
    }

    private void UpdateHint()
    {
        if (hintGroup == null)
            return;

        float target = Time.unscaledTime < hintUntil && !open ? 1f : 0f;
        hintGroup.alpha = Mathf.MoveTowards(hintGroup.alpha, target, Time.unscaledDeltaTime / 0.2f);
        if (hintGroup.alpha <= 0f)
            return;

        // Just above the highest card in the lower half of the screen,
        // centred, so it never covers a reading.
        Rect area = Layer.rect;
        float top = area.yMin;
        if (hintAnchor != null)
        {
            hintAnchor.GetWorldCorners(Corners);
            top = Layer.InverseTransformPoint(Corners[1]).y;
        }
        if (cardRoot != null)
        {
            cardRoot.GetComponentsInChildren(false, hintCards);
            foreach (PerformanceStatCardView card in hintCards)
            {
                ((RectTransform)card.transform).GetWorldCorners(Corners);
                float cardTop = Layer.InverseTransformPoint(Corners[1]).y;
                float cardBottom = Layer.InverseTransformPoint(Corners[0]).y;
                if ((cardTop + cardBottom) * 0.5f < area.center.y)
                    top = Mathf.Max(top, cardTop);
            }
        }
        hint.anchoredPosition = new Vector2(0f, top - area.yMin + 24f);
    }

    private void EnsureHint()
    {
        if (hint != null)
            return;

        hint = new GameObject("Hold Hint", typeof(RectTransform)).GetComponent<RectTransform>();
        hint.gameObject.layer = gameObject.layer;
        hint.SetParent(Layer, false);
        hint.anchorMin = hint.anchorMax = new Vector2(0.5f, 0f);
        hint.pivot = new Vector2(0.5f, 0f);
        hintGroup = hint.gameObject.AddComponent<CanvasGroup>();
        hintGroup.alpha = 0f;
        hintGroup.interactable = false;
        hintGroup.blocksRaycasts = false;

        UIFrameGraphic frame = hint.gameObject.AddComponent<UIFrameGraphic>();
        frame.SetThickness(2f);
        frame.SetColors(hintEdge, hintFill);
        frame.raycastTarget = false;

        HorizontalLayoutGroup row = hint.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(40, 40, 22, 22);
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = false;
        ContentSizeFitter fit = hint.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI text = new GameObject("Label", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        text.gameObject.layer = gameObject.layer;
        text.rectTransform.SetParent(hint, false);
        if (hintFont != null)
            text.font = hintFont;
        text.fontSize = 34f;
        text.characterSpacing = 4f;
        text.color = hintInk;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        UITextBinding.Bind(text, "hint.tap_again", "TAP AGAIN FOR DETAILS");
    }
}
