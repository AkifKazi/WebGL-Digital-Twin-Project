using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public enum ResponsiveDeviceOverride
{
    Auto,
    Desktop,
    Mobile
}

[DefaultExecutionOrder(-90)]
public class ResponsiveLayoutShell : MonoBehaviour
{
    private enum LayoutMode
    {
        Landscape,
        Portrait
    }

    [Header("Layout Roots")]
    [Tooltip("Use the existing Wide layout here. It is shared by wide and compact landscape screens.")]
    [SerializeField] private GameObject landscapeLayout;

    [SerializeField] private GameObject portraitLayout;

    [Header("Canvas")]
    [SerializeField] private CanvasScaler canvasScaler;

    [Header("Breakpoints")]
    [Tooltip("Switch to portrait below this safe-area aspect ratio.")]
    [SerializeField] private float portraitEnterAspect = 0.95f;

    [Tooltip("Remain portrait until the safe-area aspect exceeds this value.")]
    [SerializeField] private float portraitExitAspect = 1.05f;

    [Header("Device classification")]
    [Tooltip("Auto keeps tall desktop monitors on the desktop UI while mobile devices may switch orientation layouts.")]
    [SerializeField] private ResponsiveDeviceOverride deviceOverride = ResponsiveDeviceOverride.Auto;

    [Header("Reference Resolutions")]
    [SerializeField] private Vector2 landscapeReferenceResolution = new(1920f, 1080f);
    [SerializeField] private Vector2 portraitReferenceResolution = new(1080f, 1920f);
    [SerializeField, Range(0f, 1f)] private float landscapeMatch = 0.5f;
    [SerializeField, Range(0f, 1f)] private float portraitMatch = 0f;

    [Header("Vertical Desktop")]
    [SerializeField, Min(0f)] private float verticalDesktopControlMargin = 24f;
    [SerializeField, Min(0f)] private float verticalDesktopHorizontalControlMargin = 40f;
    [SerializeField, Min(48f)] private float verticalDesktopControlHeight = 88f;

    [Header("Mobile Landscape")]
    [Tooltip("Scales the mobile-landscape bottom control groups. Their layout spacing and occupied width follow this value automatically; desktop and portrait remain unchanged.")]
    [SerializeField, Range(1f, 1.5f)] private float mobileLandscapeBottomControlScale = 1.1f;

    [Header("Telemetry")]
    [Tooltip("Rail counts and the history panel for every screen. Empty uses the one on this " +
             "object, then the scene's.")]
    [SerializeField] private TelemetryLayoutSettings telemetrySettings;

    [Header("Resize resilience")]
    [SerializeField, Range(0.05f, 0.5f), Tooltip("Wait for browser and orientation resizing to settle before rebuilding telemetry cards.")]
    private float resizeSettleSeconds = 0.15f;

    [Header("Debug")]
    [SerializeField] private bool showDebugLogs;

    private LayoutMode currentMode;
    private bool initialized;
    private int previousWidth;
    private int previousHeight;
    private Rect previousSafeArea;
    private Coroutine telemetryRebuildRoutine;
    private Coroutine resizeRefreshRoutine;
    private StatRailManager wideRailManager;
    private StatRailManager portraitRailManager;
    private TelemetryDetailPanelController detailPanels;
    private PortraitTelemetrySheetController telemetrySheet;
    private bool telemetryLookupDone;
    private bool lastMobile;
    private bool lastVerticalDesktop;
    private readonly Dictionary<Transform, Vector3> bottomControlBaseScales = new();
    private float bottomControlsBaseHeight = -1f;
    private float bottomControlsBaseSpacing = -1f;
    private bool bottomControlsBasePaddingCaptured;
    private int bottomControlsBasePaddingLeft;
    private int bottomControlsBasePaddingRight;

    private void Start()
    {
        RefreshLayout(true);
    }

    private void OnEnable()
    {
        if (telemetrySettings == null)
            telemetrySettings = GetComponent<TelemetryLayoutSettings>();
        if (telemetrySettings == null)
            telemetrySettings = FindAnyObjectByType<TelemetryLayoutSettings>(FindObjectsInactive.Include);
        if (telemetrySettings != null)
            telemetrySettings.Changed += HandleTelemetrySettingsChanged;
        DigitalTwinSettings.Changed += HandleViewerSettingsChanged;
    }

    /// <summary>
    /// The viewer's interface scale and card rails. The settings panel only
    /// sets the scale once the slider has rested, so the layout is rebuilt
    /// once, as after a resize; a new rail count rebuilds the rails.
    /// </summary>
    private void HandleViewerSettingsChanged()
    {
        if (!initialized)
            return;
        if (!Mathf.Approximately(appliedInterfaceScale, DigitalTwinSettings.InterfaceScale))
            RefreshLayout(false);
        if (appliedRailsPerSide != DigitalTwinSettings.RailsPerSide)
            HandleTelemetrySettingsChanged();
    }

    private float appliedInterfaceScale = 1f;
    private int appliedRailsPerSide;

    private void Update()
    {
        bool screenChanged =
            Screen.width != previousWidth ||
            Screen.height != previousHeight ||
            DeviceSafeArea.Current != previousSafeArea;

        if (screenChanged && resizeRefreshRoutine == null)
            resizeRefreshRoutine = StartCoroutine(RefreshAfterResizeSettles());
    }

    [ContextMenu("Refresh Layout")]
    public void RefreshLayoutFromInspector()
    {
        RefreshLayout(true);
    }

    private void RefreshLayout(bool force)
    {
        bool wasInitialized = initialized;
        previousWidth = Screen.width;
        previousHeight = Screen.height;
        previousSafeArea = DeviceSafeArea.Current;

        if (previousSafeArea.width <= 0f || previousSafeArea.height <= 0f)
            return;

        float aspect = previousSafeArea.width / previousSafeArea.height;
        bool mobile = IsMobileDevice();
        LayoutMode newMode = DetermineMode(aspect, mobile);

        bool modeChanged = !initialized || newMode != currentMode;

        currentMode = newMode;
        initialized = true;

        if (modeChanged || force)
        {
            if (landscapeLayout != null)
                landscapeLayout.SetActive(currentMode == LayoutMode.Landscape);

            if (portraitLayout != null)
                portraitLayout.SetActive(currentMode == LayoutMode.Portrait);
        }

        ApplyCanvasSettings();
        ApplyVerticalDesktopControlSafety(!mobile && aspect < 1f);
        ApplyTelemetryProfiles(mobile, !mobile && aspect < 1f);
        ApplyMobileLandscapeControlScale(
            mobile && currentMode == LayoutMode.Landscape);
        Canvas.ForceUpdateCanvases();
        if (wasInitialized)
        {
            if (telemetryRebuildRoutine != null)
                StopCoroutine(telemetryRebuildRoutine);
            telemetryRebuildRoutine = StartCoroutine(RebuildActiveTelemetryLayoutDeferred());
        }

        if (showDebugLogs)
        {
            Debug.Log(
                $"Responsive layout: {currentMode}, " +
                $"aspect: {aspect:F2}, " +
                $"device: {(mobile ? "Mobile" : "Desktop")}, " +
                $"screen: {Screen.width}x{Screen.height}",
                this
            );
        }
    }

    private IEnumerator RebuildActiveTelemetryLayoutDeferred()
    {
        yield return new WaitForEndOfFrame();
        yield return null;
        Canvas.ForceUpdateCanvases();
        RebuildActiveTelemetryLayout();
        telemetryRebuildRoutine = null;
    }

    private IEnumerator RefreshAfterResizeSettles()
    {
        int observedWidth;
        int observedHeight;
        Rect observedSafeArea;
        do
        {
            observedWidth = Screen.width;
            observedHeight = Screen.height;
            observedSafeArea = DeviceSafeArea.Current;
            yield return new WaitForSecondsRealtime(resizeSettleSeconds);
        }
        while (observedWidth != Screen.width ||
               observedHeight != Screen.height ||
               !ApproximatelySameRect(observedSafeArea, DeviceSafeArea.Current));

        RefreshLayout(false);
        resizeRefreshRoutine = null;
    }

    private static bool ApproximatelySameRect(Rect first, Rect second) =>
        Mathf.Abs(first.x - second.x) <= 0.5f &&
        Mathf.Abs(first.y - second.y) <= 0.5f &&
        Mathf.Abs(first.width - second.width) <= 0.5f &&
        Mathf.Abs(first.height - second.height) <= 0.5f;

    private void OnDisable()
    {
        if (telemetrySettings != null)
            telemetrySettings.Changed -= HandleTelemetrySettingsChanged;
        DigitalTwinSettings.Changed -= HandleViewerSettingsChanged;
        if (telemetryRebuildRoutine != null)
        {
            StopCoroutine(telemetryRebuildRoutine);
            telemetryRebuildRoutine = null;
        }
        if (resizeRefreshRoutine != null)
        {
            StopCoroutine(resizeRefreshRoutine);
            resizeRefreshRoutine = null;
        }
    }

    private void RebuildActiveTelemetryLayout()
    {
        FindTelemetry();
        StatRailManager manager = currentMode == LayoutMode.Landscape
            ? wideRailManager
            : portraitRailManager;
        if (manager != null && manager.isActiveAndEnabled)
            manager.RebuildLayout();
    }

    private LayoutMode DetermineMode(float aspect, bool mobile)
    {
        // Desktop and laptop screens always retain the wide controls and rail
        // system, including physically portrait monitors such as 1080x1920.
        if (!mobile)
            return LayoutMode.Landscape;

        if (!initialized)
            return aspect < 1f ? LayoutMode.Portrait : LayoutMode.Landscape;

        if (currentMode == LayoutMode.Portrait)
        {
            return aspect < portraitExitAspect
                ? LayoutMode.Portrait
                : LayoutMode.Landscape;
        }

        return aspect < portraitEnterAspect
            ? LayoutMode.Portrait
            : LayoutMode.Landscape;
    }

    private bool IsMobileDevice()
    {
        if (deviceOverride == ResponsiveDeviceOverride.Desktop)
            return false;
        if (deviceOverride == ResponsiveDeviceOverride.Mobile)
            return true;

        return DeviceKind.IsMobile;
    }

    private void ApplyCanvasSettings()
    {
        if (canvasScaler == null)
            return;

        bool portrait = currentMode == LayoutMode.Portrait;

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        // A smaller reference resolution draws everything larger.
        appliedInterfaceScale = DigitalTwinSettings.InterfaceScale;
        canvasScaler.referenceResolution = (portrait
            ? portraitReferenceResolution
            : landscapeReferenceResolution) / appliedInterfaceScale;

        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = portrait ? portraitMatch : landscapeMatch;
    }

    private void ApplyVerticalDesktopControlSafety(bool verticalDesktop)
    {
        if (landscapeLayout == null)
            return;

        RectTransform baseLayout = FindChildRect(landscapeLayout.transform, "Base layout");
        RectTransform bottomControls = FindChildRect(landscapeLayout.transform, "Bottom controls");
        if (baseLayout == null || bottomControls == null ||
            bottomControls.parent != baseLayout)
            return;

        VerticalLayoutGroup baseGroup = baseLayout.GetComponent<VerticalLayoutGroup>();
        LayoutElement controlsLayout = bottomControls.GetComponent<LayoutElement>();
        HorizontalLayoutGroup controlsGroup = bottomControls.GetComponent<HorizontalLayoutGroup>();
        if (baseGroup == null || controlsLayout == null || controlsGroup == null)
            return;

        if (!bottomControlsBasePaddingCaptured)
        {
            bottomControlsBasePaddingLeft = controlsGroup.padding.left;
            bottomControlsBasePaddingRight = controlsGroup.padding.right;
            bottomControlsBasePaddingCaptured = true;
        }

        if (verticalDesktop)
        {
            controlsLayout.ignoreLayout = true;
            bottomControls.anchorMin = new Vector2(0f, 0f);
            bottomControls.anchorMax = new Vector2(1f, 0f);
            bottomControls.pivot = new Vector2(0.5f, 0f);
            bottomControls.anchoredPosition = new Vector2(0f, verticalDesktopControlMargin);
            bottomControls.sizeDelta = new Vector2(0f, verticalDesktopControlHeight);
            int horizontalMargin = Mathf.RoundToInt(verticalDesktopHorizontalControlMargin);
            controlsGroup.padding.left = horizontalMargin;
            controlsGroup.padding.right = horizontalMargin;
            baseGroup.padding.bottom = Mathf.CeilToInt(
                verticalDesktopControlHeight + verticalDesktopControlMargin);
        }
        else
        {
            controlsLayout.ignoreLayout = false;
            controlsLayout.preferredHeight = verticalDesktopControlHeight;
            controlsGroup.padding.left = bottomControlsBasePaddingLeft;
            controlsGroup.padding.right = bottomControlsBasePaddingRight;
            baseGroup.padding.bottom = Mathf.RoundToInt(verticalDesktopControlMargin);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(baseLayout);
    }

    /// <summary>
    /// Hands each telemetry layout what the <see cref="TelemetryLayoutSettings"/>
    /// ask of this screen: how many rails, rows or columns, and whether the
    /// history panel opens. A desktop window taller than it is wide keeps the
    /// desktop controls but runs the cards in rows above and below the model,
    /// as on a phone held upright, instead of squeezing them into columns.
    /// The rails take it up at the rebuild that follows every layout change.
    /// </summary>
    private void ApplyTelemetryProfiles(bool mobile, bool verticalDesktop)
    {
        lastMobile = mobile;
        lastVerticalDesktop = verticalDesktop;
        appliedRailsPerSide = DigitalTwinSettings.RailsPerSide;
        FindTelemetry();

        TelemetryLayoutProfile wide = Resolve(mobile, verticalDesktop);
        TelemetryLayoutProfile upright = Resolve(true, true);
        if (wideRailManager != null)
            wideRailManager.SetLayoutProfile(wide);
        if (portraitRailManager != null)
            portraitRailManager.SetLayoutProfile(upright);
        if (detailPanels != null)
            detailPanels.SetFeatureEnabled(wide.DetailPanel);
        if (telemetrySheet != null)
            telemetrySheet.SetFeatureEnabled(upright.DetailPanel);
    }

    private TelemetryLayoutProfile Resolve(bool mobile, bool rows) => telemetrySettings != null
        ? telemetrySettings.Resolve(mobile, rows)
        : new TelemetryLayoutProfile(rows, TelemetryRailCount.Auto, true);

    // Changed in the inspector while playing, or the viewer's rail count in
    // the settings: the rails follow at once.
    private void HandleTelemetrySettingsChanged()
    {
        if (!initialized)
            return;
        ApplyTelemetryProfiles(lastMobile, lastVerticalDesktop);
        if (wideRailManager != null && wideRailManager.isActiveAndEnabled)
            wideRailManager.RequestRebuild();
        if (portraitRailManager != null && portraitRailManager.isActiveAndEnabled)
            portraitRailManager.RequestRebuild();
    }

    private void FindTelemetry()
    {
        if (telemetryLookupDone)
            return;
        telemetryLookupDone = true;
        if (landscapeLayout != null)
        {
            wideRailManager = landscapeLayout.GetComponentInChildren<StatRailManager>(true);
            detailPanels = landscapeLayout.GetComponentInChildren<TelemetryDetailPanelController>(true);
        }
        if (portraitLayout != null)
        {
            portraitRailManager = portraitLayout.GetComponentInChildren<StatRailManager>(true);
            telemetrySheet = portraitLayout.GetComponentInChildren<PortraitTelemetrySheetController>(true);
        }
    }

    private void ApplyMobileLandscapeControlScale(bool enlarge)
    {
        if (landscapeLayout == null)
            return;

        RectTransform bottomControls =
            FindChildRect(landscapeLayout.transform, "Bottom controls");
        if (bottomControls == null)
            return;

        LayoutElement controlsLayout = bottomControls.GetComponent<LayoutElement>();
        HorizontalLayoutGroup controlsGroup =
            bottomControls.GetComponent<HorizontalLayoutGroup>();
        if (bottomControlsBaseHeight < 0f)
        {
            bottomControlsBaseHeight = controlsLayout != null &&
                                       controlsLayout.preferredHeight > 0f
                ? controlsLayout.preferredHeight
                : verticalDesktopControlHeight;
        }

        if (bottomControlsBaseSpacing < 0f && controlsGroup != null)
            bottomControlsBaseSpacing = controlsGroup.spacing;

        foreach (Transform child in bottomControls)
        {
            // Service/controller objects can also live under this hierarchy but do
            // not participate in UI layout. Only scale direct RectTransform groups.
            if (child is RectTransform && !bottomControlBaseScales.ContainsKey(child))
                bottomControlBaseScales.Add(child, child.localScale);
        }

        float scale = enlarge ? mobileLandscapeBottomControlScale : 1f;
        foreach (KeyValuePair<Transform, Vector3> entry in bottomControlBaseScales)
        {
            if (entry.Key != null)
                entry.Key.localScale = entry.Value * scale;
        }

        if (controlsGroup != null)
        {
            // HorizontalLayoutGroup otherwise measures the unscaled RectTransforms,
            // allowing enlarged controls to consume the intended visual gap.
            controlsGroup.childScaleWidth = true;
            controlsGroup.childScaleHeight = true;
            controlsGroup.spacing = Mathf.Max(0f, bottomControlsBaseSpacing) * scale;
        }

        if (controlsLayout != null && !controlsLayout.ignoreLayout)
            controlsLayout.preferredHeight = bottomControlsBaseHeight * scale;

        LayoutRebuilder.ForceRebuildLayoutImmediate(bottomControls);
        if (bottomControls.parent is RectTransform parent)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
    }

    private static RectTransform FindChildRect(Transform root, string objectName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == objectName)
                return child as RectTransform;
        }
        return null;
    }
}
