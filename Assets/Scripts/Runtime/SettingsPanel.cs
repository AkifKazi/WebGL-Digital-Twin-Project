using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The viewer's settings, opened from a gear button beside the Telemetry /
/// Service switch in either layout. A modal over the scene, in four tabs:
///
///   Display    interface scale, high contrast, reduce motion, edge shading
///   Telemetry  unit style, dimming the other cards while one is focused
///   Camera     orbit and zoom sensitivity, inverted tilt, smooth motion
///   Graphics   detail (SD / HD), frame rate limit, the frame rate readout
///
/// Every change applies at once and is kept in the browser, except the
/// interface scale, which waits until the slider has rested so the layout
/// does not jump while it is dragged. The panel sits on a canvas of its own, at
/// a fixed scale, so it holds still while the scale behind it changes.
///
/// Keyboard: Tab moves through the panel only; the arrow keys move between
/// rows and change sliders and choices; Q and E switch tabs; Esc closes. The
/// right-hand column describes the row the pointer or the keyboard is on.
///
/// Built in code; the scene only needs this component and its sprites.
/// </summary>
[DisallowMultipleComponent]
public sealed class SettingsPanel : MonoBehaviour
{
    [SerializeField] private Sprite settingsIcon;
    [SerializeField] private Sprite backIcon;
    [SerializeField] private Sprite closeIcon;

    [Tooltip("Optional. The SD / HD switch's controller; found in the scene when empty.")]
    [SerializeField] private AdaptiveQualityController quality;

    [Tooltip("Seconds the interface scale slider must rest before the new scale is applied.")]
    [SerializeField, Min(0f)] private float scaleApplyDelay = 0.6f;

    // Labels and icons of the controls take the design system's control
    // colours (UIControlColors): near white when chosen, a quiet cyan at rest.

    private const float WidePanelWidth = 1240f;
    private const float WidePanelHeight = 760f;
    private const float Padding = 40f;
    private const float RowHeight = 64f;
    private const float RowGap = 6f;
    private const float ControlWidth = 330f;
    private const float ControlHeight = 40f;

    /// <summary>True while the panel is open.</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>The tab shown: 0 Display, 1 Telemetry, 2 Camera, 3 Graphics.</summary>
    public int CurrentTab => currentTab;

    /// <summary>The modal, over the whole screen while open.</summary>
    public RectTransform Modal => modal;

    private readonly List<Tab> tabs = new();
    private readonly List<UIControlState> launchers = new();
    private readonly List<Graphic> launcherIcons = new();
    private readonly List<Action> refreshers = new();

    private Canvas canvas;
    private CanvasScaler scaler;
    private RectTransform canvasRect;
    private RectTransform modal;
    private RectTransform panel;
    private RectTransform header;
    private RectTransform tabBar;
    private RectTransform body;
    private RectTransform list;
    private RectTransform aside;
    private RectTransform footer;
    private Image divider;
    private TextMeshProUGUI asideTitle;
    private TextMeshProUGUI asideBody;
    private TextMeshProUGUI savedNote;
    private Button resetButton;
    private Button closeButton;
    private Button backButton;
    private TextMeshProUGUI frameRateLabel;
    private UIEdgeShade edgeShade;

    private int currentTab;
    private Row describedRow;
    // Portrait screens (phones) get a page of their own, laid out as the
    // phone's other pages are: MobileContentWidth units across, scaled to the screen.
    private bool mobile;
    private RectTransform page;
    private RectTransform pageContent;
    private ScrollRect pageScroll;
    private UnityEngine.Rect laidOutSafe;
    private const float MobileContentWidth = 540f;
    private const float MobileHeaderHeight = 72f;
    private const float MobileSide = 16f;
    // A thumb's target on the phone's page: 64 units is about 46 points on a
    // phone at this scale, as phones' own settings make theirs; a segmented
    // choice keeps 56 of them inside its frame, about 41 points.
    private const float MobileTarget = 64f;
    private Vector2 laidOutFor;
    private int initialTargetFrameRate = -1;
    private bool attached;

    private float pendingScale = float.NaN;
    private float pendingSince;
    private TextMeshProUGUI scaleValue;

    private sealed class Tab
    {
        public string Title;
        public string Description;
        public Button Button;
        public UIControlState State;
        public TextMeshProUGUI Label;
        public RectTransform Content;
        public readonly List<Row> Rows = new();
    }

    private sealed class Row
    {
        public RectTransform Rect;
        public string Title;
        public string Description;
        public readonly List<Selectable> Controls = new();
        // The phone's page: the line with the row's name, a control at its
        // end, and a control across the row under it. Unused ones stay off.
        public RectTransform TitleLine;
        public RectTransform Trailing;
        public RectTransform Across;
    }

    /// <summary>Tells the panel which row the pointer is over, for its description.</summary>
    private sealed class RowHover : MonoBehaviour, IPointerEnterHandler
    {
        public SettingsPanel Owner;
        public Row Row;

        public void OnPointerEnter(PointerEventData eventData) => Owner.Describe(Row);
    }

    private sealed class BackdropClick : MonoBehaviour, IPointerClickHandler
    {
        public SettingsPanel Owner;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (UIPointerUtility.IsCleanClick(eventData))
                Owner.SetOpen(false);
        }
    }

    // -----------------------------------------------------------------------
    // Life cycle
    // -----------------------------------------------------------------------

    private void Awake()
    {
        if (quality == null)
            quality = FindAnyObjectByType<AdaptiveQualityController>(FindObjectsInactive.Include);
        BuildCanvas();
        BuildFrameRateLabel();
        BuildModal();
        SetOpen(false);
    }

    private void Start()
    {
        initialTargetFrameRate = Application.targetFrameRate;
        AttachLaunchers();
        AttachEdgeShade();
        if (quality != null)
            quality.QualityChanged += RefreshAll;
        ApplyRuntimeSettings();
        RefreshAll();
    }

    private void OnEnable() => DigitalTwinSettings.Changed += HandleSettingsChanged;

    private void OnDisable()
    {
        DigitalTwinSettings.Changed -= HandleSettingsChanged;
        if (IsOpen)
            SetOpen(false);
    }

    private void OnDestroy()
    {
        if (canvas != null)
            Destroy(canvas.gameObject);
    }

    private void HandleSettingsChanged()
    {
        ApplyRuntimeSettings();
        RefreshAll();
    }

    /// <summary>Settings that no other view owns: the frame rate limit and readout.</summary>
    private void ApplyRuntimeSettings()
    {
        int limit = DigitalTwinSettings.TargetFrameRate;
        Application.targetFrameRate = limit > 0 ? limit : initialTargetFrameRate;
        if (frameRateLabel != null)
            frameRateLabel.gameObject.SetActive(DigitalTwinSettings.ShowFrameRate);
        // Shaders that animate on their own (the service run bars' pulse) read this.
        Shader.SetGlobalFloat(ReduceMotionProperty, DigitalTwinSettings.ReduceMotion ? 1f : 0f);
    }

    private static readonly int ReduceMotionProperty = Shader.PropertyToID("_DigitalTwinReduceMotion");

    private void Update()
    {
        FitCanvas();
        ApplyPendingScale();
        if (!IsOpen)
            return;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                SetOpen(false);
                return;
            }
            // Q and E step through the tabs; the phone's page has none.
            if (!mobile && keyboard.qKey.wasPressedThisFrame)
                SelectTab((currentTab + tabs.Count - 1) % tabs.Count, true);
            else if (!mobile && keyboard.eKey.wasPressedThisFrame)
                SelectTab((currentTab + 1) % tabs.Count, true);
        }

        // The keyboard's row is described, as the pointer's is.
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (!mobile && selected != null && KeyboardNavigator.KeyboardMode)
        {
            foreach (Row row in tabs[currentTab].Rows)
            {
                if (selected.transform.IsChildOf(row.Rect) && row != describedRow)
                    Describe(row);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Opening and closing
    // -----------------------------------------------------------------------

    public void ToggleOpen() => SetOpen(!IsOpen);

    public void SetOpen(bool open)
    {
        bool wasOpen = IsOpen;
        IsOpen = open;
        if (modal != null)
            modal.gameObject.SetActive(open);
        foreach (UIControlState launcher in launchers)
        {
            if (launcher != null)
                launcher.Selected = open;
        }
        foreach (Graphic icon in launcherIcons)
        {
            if (icon != null)
                icon.color = UIControlColors.For(open);
        }

        if (open)
        {
            KeyboardNavigator.Scope = modal;
            SelectTab(currentTab, false);
            if (pageScroll != null)
                pageScroll.verticalNormalizedPosition = 1f;
            // The keyboard starts on the tabs (on a phone's page, its back
            // button), so the arrow keys work at once.
            if (KeyboardNavigator.KeyboardMode)
                KeyboardNavigator.Focus(mobile ? backButton : tabs[currentTab].Button);
            else
                KeyboardNavigator.ClearFocus();
        }
        else if (wasOpen)
        {
            CommitPendingScale();
            KeyboardNavigator.Scope = null;
            // The keyboard goes back to the gear it opened the panel from.
            UIControlState launcher = ActiveLauncher();
            if (KeyboardNavigator.KeyboardMode && launcher != null && launcher.TryGetComponent(out Selectable button))
                KeyboardNavigator.Focus(button);
            else
                KeyboardNavigator.ClearFocus();
        }
    }

    private UIControlState ActiveLauncher()
    {
        foreach (UIControlState launcher in launchers)
        {
            if (launcher != null && launcher.isActiveAndEnabled)
                return launcher;
        }
        return null;
    }

    // -----------------------------------------------------------------------
    // Interface scale: applied once the slider rests
    // -----------------------------------------------------------------------

    private void QueueScale(float value)
    {
        pendingScale = value;
        pendingSince = Time.unscaledTime;
        UpdateScaleValue();
    }

    private void ApplyPendingScale()
    {
        if (float.IsNaN(pendingScale) || Time.unscaledTime - pendingSince < scaleApplyDelay)
            return;
        // Not while a finger or the mouse still holds the slider.
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
            return;
        CommitPendingScale();
    }

    private void CommitPendingScale()
    {
        if (float.IsNaN(pendingScale))
            return;
        float value = pendingScale;
        pendingScale = float.NaN;
        DigitalTwinSettings.InterfaceScale = value;
        UpdateScaleValue();
    }

    private void UpdateScaleValue()
    {
        if (scaleValue == null)
            return;
        float shown = float.IsNaN(pendingScale) ? DigitalTwinSettings.InterfaceScale : pendingScale;
        scaleValue.text = Mathf.RoundToInt(shown * 100f).ToString(CultureInfo.InvariantCulture) + "%";
        // Dimmed while it waits to be applied.
        scaleValue.alpha = float.IsNaN(pendingScale) ? 1f : 0.55f;
    }

    // -----------------------------------------------------------------------
    // Canvas, launchers, edge shading, frame rate
    // -----------------------------------------------------------------------

    private void BuildCanvas()
    {
        GameObject canvasObject = new("Settings", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
            typeof(GraphicRaycaster));
        canvasObject.layer = gameObject.layer;
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;
        scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasRect = (RectTransform)canvasObject.transform;
        FitCanvas();
    }

    /// <summary>The panel's canvas scales like the interface at 100%, whatever the viewer's scale.</summary>
    private void FitCanvas()
    {
        if (scaler == null)
            return;
        Vector2 pixels = canvas.pixelRect.size;
        bool portrait = pixels.y > pixels.x;
        scaler.referenceResolution = portrait ? new Vector2(1080f, 1920f) : new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = portrait ? 0f : 0.5f;
        if (modal == null)
            return;
        // Turned between a phone's page and the desktop's panel: built anew.
        if (portrait != mobile)
        {
            Rebuild();
            return;
        }
        Vector2 size = canvasRect.rect.size;
        if (size.x > 1f && (size != laidOutFor || (mobile && SafeRect(size) != laidOutSafe)))
            Layout();
    }

    /// <summary>The panel's contents built again, for the other kind of screen, keeping what is open.</summary>
    private void Rebuild()
    {
        bool wasOpen = IsOpen;
        int shownTab = currentTab;
        Destroy(modal.gameObject);
        modal = page = pageContent = header = tabBar = body = list = aside = footer = panel = null;
        pageScroll = null;
        divider = null;
        asideTitle = asideBody = savedNote = scaleValue = null;
        resetButton = closeButton = backButton = null;
        describedRow = null;
        tabs.Clear();
        refreshers.Clear();
        BuildModal();
        currentTab = Mathf.Clamp(shownTab, 0, tabs.Count - 1);
        modal.gameObject.SetActive(wasOpen);
        if (wasOpen)
        {
            KeyboardNavigator.Scope = modal;
            SelectTab(currentTab, false);
        }
        RefreshAll();
    }

    private static bool PortraitCanvas(Canvas canvas)
    {
        Vector2 pixels = canvas.pixelRect.size;
        return pixels.y > pixels.x;
    }

    /// <summary>A gear beside each Telemetry / Service switch, in the switch's own frame.</summary>
    private void AttachLaunchers()
    {
        if (attached)
            return;
        attached = true;
        foreach (DigitalTwinModeSwitch modeSwitch in FindObjectsByType<DigitalTwinModeSwitch>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            RectTransform source = (RectTransform)modeSwitch.transform;
            RectTransform parent = source.parent as RectTransform;
            if (parent == null)
                continue;

            RectTransform button = Rect("Settings Button", parent);
            button.SetSiblingIndex(source.GetSiblingIndex() + 1);
            // Translucent, like the switch: leader lines fade where they cross it.
            button.gameObject.AddComponent<LeaderLineObstacle>();
            float height = source.sizeDelta.y;
            button.localScale = source.localScale;
            button.sizeDelta = new Vector2(height, height);
            bool leftAnchored = source.anchorMin.x < 0.01f && source.anchorMax.x < 0.01f;
            if (leftAnchored)
            {
                // Right after the switch, the same gap as between its segments.
                button.anchorMin = button.anchorMax = source.anchorMin;
                button.pivot = new Vector2(0f, source.pivot.y);
                float switchRight = source.anchoredPosition.x + source.sizeDelta.x * source.localScale.x * (1f - source.pivot.x);
                button.anchoredPosition = new Vector2(switchRight + 12f * source.localScale.x, source.anchoredPosition.y);
            }
            else
            {
                // A centred switch (portrait) leaves the bar's right end free.
                button.anchorMin = button.anchorMax = new Vector2(1f, source.anchorMin.y);
                button.pivot = new Vector2(1f, source.pivot.y);
                button.anchoredPosition = new Vector2(0f, source.anchoredPosition.y);
            }

            // The frame, drawn like the switch's.
            Image frame = button.gameObject.AddComponent<UIShape>();
            if (modeSwitch.TryGetComponent(out Image switchFrame))
                UIShape.CopyLook(switchFrame, frame);
            frame.raycastTarget = false;
            float inset = modeSwitch.TryGetComponent(out HorizontalLayoutGroup stack) ? stack.padding.left : 6f;

            RectTransform face = Rect("Face", button);
            Stretch(face, inset);
            Shape(face, "Segment", true);
            Button press = face.gameObject.AddComponent<Button>();
            press.transition = Selectable.Transition.None;
            press.onClick.AddListener(ToggleOpen);
            UIControlState state = face.gameObject.AddComponent<UIControlState>();
            launchers.Add(state);

            RectTransform glyph = Rect("Icon", face);
            glyph.anchorMin = glyph.anchorMax = new Vector2(0.5f, 0.5f);
            glyph.sizeDelta = new Vector2(26f, 26f);
            launcherIcons.Add(UIBuilder.Icon(glyph, settingsIcon));
        }
    }

    /// <summary>The edge shading, behind everything on the main canvas.</summary>
    private void AttachEdgeShade()
    {
        DigitalTwinModeSwitch any = FindAnyObjectByType<DigitalTwinModeSwitch>(FindObjectsInactive.Include);
        Canvas main = any != null ? any.GetComponentInParent<Canvas>(true) : null;
        if (main == null)
            return;
        RectTransform shade = Rect("Edge Shade", (RectTransform)main.rootCanvas.transform);
        shade.SetAsFirstSibling();
        Stretch(shade, 0f);
        edgeShade = shade.gameObject.AddComponent<UIEdgeShade>();
    }

    private void BuildFrameRateLabel()
    {
        RectTransform rect = Rect("Frame Rate", canvasRect);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(28f, -116f);
        rect.sizeDelta = new Vector2(240f, 70f);
        frameRateLabel = Text("Text", rect, "Caption", TextAlignmentOptions.TopLeft);
        Stretch(frameRateLabel.rectTransform, 0f);
        frameRateLabel.textWrappingMode = TextWrappingModes.NoWrap;
        frameRateLabel.gameObject.AddComponent<FpsDisplay>();
        frameRateLabel.gameObject.SetActive(false);
    }

    // -----------------------------------------------------------------------
    // The modal
    // -----------------------------------------------------------------------

    private void BuildModal()
    {
        mobile = PortraitCanvas(canvas);
        modal = Rect("Modal", canvasRect);
        Stretch(modal, 0f);
        if (mobile)
            BuildMobileFrame();
        else
            BuildDesktopFrame();

        BuildDisplayTab();
        BuildTelemetryTab();
        BuildCameraTab();
        BuildGraphicsTab();
        BuildFooter();
        if (mobile)
        {
            WireMobileNavigation();
        }
        else
        {
            foreach (Tab tab in tabs)
                WireNavigation(tab);
            Navigation intoTabs = new() { mode = Navigation.Mode.Explicit, selectOnDown = tabs[0].Button };
            closeButton.navigation = intoTabs;
            backButton.navigation = intoTabs;
        }
        Layout();
    }

    /// <summary>The desktop's panel: a popover over the dimmed twin, tabs across its top, a description beside the rows.</summary>
    private void BuildDesktopFrame()
    {
        Image backdrop = modal.gameObject.AddComponent<Image>();
        backdrop.color = new Color(0f, 0.024f, 0.031f, 0.7f);
        modal.gameObject.AddComponent<BackdropClick>().Owner = this;

        panel = Rect("Panel", modal);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        Shape(panel, "Detail Panel", true);

        BuildHeader();
        BuildTabs();

        body = Rect("Body", panel);
        // The dark well the telemetry detail sets its content in.
        RectTransform well = Rect("Well", body);
        Stretch(well, -20f);
        Shape(well, "Inset", false);
        list = Rect("List", body);
        aside = Rect("About", body);
        divider = Rect("Divider", body).gameObject.AddComponent<Image>();
        divider.color = UIBuilder.Token("Accent", 0.16f, new Color(0.21f, 0.93f, 1f, 0.16f));
        divider.raycastTarget = false;
        asideTitle = Text("Title", aside, "Label", TextAlignmentOptions.TopLeft);
        asideBody = Text("Text", aside, "Body", TextAlignmentOptions.TopLeft);
        asideBody.alpha = 0.78f;
        asideBody.textWrappingMode = TextWrappingModes.Normal;
    }

    /// <summary>
    /// A phone's settings page, as a phone's own settings are: the whole
    /// screen, on the opaque fill the phone's other pages share; a back arrow
    /// and the title in a header, as the service detail has; then one
    /// scrolling list of groups (Display, Telemetry, Camera, Graphics), each
    /// a dark well of rows with every setting's description under its name,
    /// since a finger cannot hover to read it. Controls sit at the row's end
    /// or, when they need the width, across it; a toggle's whole row is its
    /// touch target.
    /// </summary>
    private void BuildMobileFrame()
    {
        Image fill = modal.gameObject.AddComponent<UIShape>();
        PortraitServiceView.CopyBackdropLook(fill);
        fill.raycastTarget = true;

        page = Rect("Page", modal);
        page.anchorMin = page.anchorMax = page.pivot = new Vector2(0f, 1f);

        header = Rect("Header", page);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = Vector2.one;
        header.pivot = new Vector2(0.5f, 1f);
        header.offsetMin = new Vector2(0f, -MobileHeaderHeight);
        header.offsetMax = Vector2.zero;

        RectTransform back = Rect("Back", header);
        back.anchorMin = back.anchorMax = back.pivot = new Vector2(0f, 0.5f);
        back.anchoredPosition = new Vector2(MobileSide - 12f, 0f);
        back.sizeDelta = new Vector2(MobileTarget, MobileTarget);
        backButton = IconButton(back, backIcon, () => SetOpen(false));
        // The back arrow is the page's only way out, so it is the close button too.
        closeButton = backButton;

        TextMeshProUGUI title = Text("Title", header, "Title", TextAlignmentOptions.MidlineLeft);
        title.text = UIText.Get("settings.title", "SETTINGS");
        title.rectTransform.anchorMin = Vector2.zero;
        title.rectTransform.anchorMax = Vector2.one;
        title.rectTransform.offsetMin = new Vector2(MobileSide + 54f, 0f);
        title.rectTransform.offsetMax = new Vector2(-MobileSide, 0f);

        Image rule = Rect("Rule", page).gameObject.AddComponent<Image>();
        rule.color = UIBuilder.Token("Accent", 0.16f, new Color(0.21f, 0.93f, 1f, 0.16f));
        rule.raycastTarget = false;
        RectTransform ruleRect = rule.rectTransform;
        ruleRect.anchorMin = new Vector2(0f, 1f);
        ruleRect.anchorMax = Vector2.one;
        ruleRect.pivot = new Vector2(0.5f, 1f);
        ruleRect.offsetMin = new Vector2(0f, -MobileHeaderHeight - 1f);
        ruleRect.offsetMax = new Vector2(0f, -MobileHeaderHeight);

        RectTransform scrollRect = Rect("List", page);
        scrollRect.anchorMin = Vector2.zero;
        scrollRect.anchorMax = Vector2.one;
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = new Vector2(0f, -MobileHeaderHeight - 1f);
        RectTransform viewport = Rect("Viewport", scrollRect);
        Stretch(viewport, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        // The space between groups still scrolls.
        Image catcher = viewport.gameObject.AddComponent<Image>();
        catcher.color = Color.clear;

        pageContent = Rect("Groups", viewport);
        pageContent.anchorMin = new Vector2(0f, 1f);
        pageContent.anchorMax = Vector2.one;
        pageContent.pivot = new Vector2(0.5f, 1f);
        pageContent.offsetMin = pageContent.offsetMax = Vector2.zero;
        VerticalLayoutGroup stack = pageContent.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.padding = new RectOffset((int)MobileSide, (int)MobileSide, 8, 40);
        stack.spacing = 10f;
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        pageContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        pageScroll = scrollRect.gameObject.AddComponent<ScrollRect>();
        pageScroll.horizontal = false;
        pageScroll.vertical = true;
        pageScroll.movementType = ScrollRect.MovementType.Clamped;
        pageScroll.viewport = viewport;
        pageScroll.content = pageContent;
        scrollRect.gameObject.AddComponent<UIWheelScroll>();

        foreach ((string title, string description) group in TabCopy())
            AddGroup(group.title, group.description);
    }

    /// <summary>A group of the phone's page: its name over a dark well that holds its rows.</summary>
    private void AddGroup(string title, string description)
    {
        TextMeshProUGUI heading = Text(title + " Heading", pageContent, "Label", TextAlignmentOptions.BottomLeft);
        heading.text = title;
        heading.margin = new Vector4(2f, 0f, 0f, 2f);
        LayoutElement headingSize = heading.gameObject.AddComponent<LayoutElement>();
        headingSize.minHeight = headingSize.preferredHeight = 44f;

        RectTransform well = Rect(title, pageContent);
        Shape(well, "Inset", false);
        VerticalLayoutGroup rows = well.gameObject.AddComponent<VerticalLayoutGroup>();
        rows.childControlWidth = rows.childControlHeight = true;
        rows.childForceExpandWidth = true;
        rows.childForceExpandHeight = false;
        tabs.Add(new Tab { Title = title, Description = description, Content = well });
    }

    private void BuildHeader()
    {
        header = Rect("Header", panel);

        RectTransform back = Rect("Back", header);
        back.anchorMin = back.anchorMax = back.pivot = new Vector2(0f, 0.5f);
        back.sizeDelta = new Vector2(44f, 44f);
        backButton = IconButton(back, backIcon, () => SetOpen(false));

        RectTransform esc = Keycap(header, "ESC", 46f);
        esc.anchorMin = esc.anchorMax = esc.pivot = new Vector2(0f, 0.5f);
        esc.anchoredPosition = new Vector2(56f, 0f);

        TextMeshProUGUI trail = Text("Breadcrumb", header, "Title", TextAlignmentOptions.MidlineLeft);
        trail.richText = true;
        trail.rectTransform.anchorMin = new Vector2(0f, 0f);
        trail.rectTransform.anchorMax = new Vector2(1f, 1f);
        trail.rectTransform.offsetMin = new Vector2(118f, 0f);
        trail.rectTransform.offsetMax = new Vector2(-60f, 0f);
        string place = UIText.Get("settings.breadcrumb", "DIGITAL TWIN");
        string title = UIText.Get("settings.title", "SETTINGS");
        trail.text = $"<alpha=#8C>{place}  /  <alpha=#FF>{title}";

        RectTransform close = Rect("Close", header);
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 0.5f);
        close.sizeDelta = new Vector2(44f, 44f);
        closeButton = IconButton(close, closeIcon, () => SetOpen(false));
    }

    private void BuildTabs()
    {
        tabBar = Rect("Tabs", panel);

        RectTransform q = Keycap(tabBar, "Q", 30f);
        q.anchorMin = q.anchorMax = q.pivot = new Vector2(0f, 0.5f);

        RectTransform frame = Rect("Frame", tabBar);
        frame.anchorMin = new Vector2(0f, 0f);
        frame.anchorMax = new Vector2(0f, 1f);
        frame.pivot = new Vector2(0f, 0.5f);
        frame.anchoredPosition = new Vector2(42f, 0f);
        Shape(frame, "Panel", false);
        HorizontalLayoutGroup row = frame.gameObject.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(6, 6, 6, 6);
        row.spacing = 4f;
        row.childControlWidth = row.childControlHeight = true;
        row.childForceExpandWidth = row.childForceExpandHeight = true;

        foreach ((string title, string description) tab in TabCopy())
            AddTab(frame, tab.title, tab.description);
        // As tall as the bar; as wide as its tabs.
        frame.sizeDelta = new Vector2(tabs.Count * 170f + 12f + (tabs.Count - 1) * 4f, 0f);

        RectTransform e = Keycap(tabBar, "E", 30f);
        e.anchorMin = e.anchorMax = e.pivot = new Vector2(0f, 0.5f);
        e.anchoredPosition = new Vector2(42f + frame.sizeDelta.x + 12f, 0f);
    }

    /// <summary>The tabs' names and what each is for: the desktop's tabs, the phone's groups.</summary>
    private static (string title, string description)[] TabCopy() => new[]
    {
        (UIText.Get("settings.tab.display", "DISPLAY"),
            UIText.Get("settings.tab.display.about", "How the interface is drawn: its size, its contrast and motion, and the shading behind the controls at the top and bottom of the screen.")),
        (UIText.Get("settings.tab.telemetry", "TELEMETRY"),
            UIText.Get("settings.tab.telemetry.about", "How readings are written on the cards, how many rails they sit on, and how the cards behave while one of them is focused.")),
        (UIText.Get("settings.tab.camera", "CAMERA"),
            UIText.Get("settings.tab.camera.about", "Drag, or use A and D and the arrow keys, to turn round the model. W and S, + and -, or the wheel zoom. R goes back to the opening view.")),
        (UIText.Get("settings.tab.graphics", "GRAPHICS"),
            UIText.Get("settings.tab.graphics.about", "Rendering detail and frame rate. A lower limit keeps a laptop cooler and its battery going longer."))
    };

    private void AddTab(RectTransform frame, string title, string description)
    {
        int index = tabs.Count;
        RectTransform rect = Rect(title, frame);
        Shape(rect, "Segment", true);
        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => SelectTab(index, false));
        UIControlState state = rect.gameObject.AddComponent<UIControlState>();
        TextMeshProUGUI label = Text("Label", rect, "Button", TextAlignmentOptions.Center);
        label.text = title;
        Stretch(label.rectTransform, 0f);

        RectTransform content = Rect(title + " Settings", null);
        tabs.Add(new Tab
        {
            Title = title,
            Description = description,
            Button = button,
            State = state,
            Label = label,
            Content = content
        });
    }

    private void SelectTab(int index, bool fromKeyboard)
    {
        currentTab = Mathf.Clamp(index, 0, tabs.Count - 1);
        // The phone's page shows every group at once.
        if (mobile)
            return;
        for (int i = 0; i < tabs.Count; i++)
        {
            bool chosen = i == currentTab;
            tabs[i].State.Selected = chosen;
            tabs[i].Label.color = UIControlColors.For(chosen);
            tabs[i].Content.gameObject.SetActive(chosen);
        }
        Describe(null);
        if (fromKeyboard)
            KeyboardNavigator.Focus(tabs[currentTab].Button);
    }

    private void Describe(Row row)
    {
        describedRow = row;
        // The phone's page writes each description under its row.
        if (asideTitle == null)
            return;
        Tab tab = tabs[currentTab];
        asideTitle.text = row != null ? row.Title : tab.Title;
        asideBody.text = row != null ? row.Description : tab.Description;
    }

    private void BuildFooter()
    {
        // On a phone's page, under the last group, the width of the groups.
        footer = Rect("Footer", mobile ? pageContent : panel);
        if (mobile)
        {
            LayoutElement room = footer.gameObject.AddComponent<LayoutElement>();
            room.minHeight = room.preferredHeight = 144f;
        }

        RectTransform reset = Rect("Reset", footer);
        if (mobile)
        {
            reset.anchorMin = new Vector2(0f, 1f);
            reset.anchorMax = Vector2.one;
            reset.pivot = new Vector2(0.5f, 1f);
            reset.offsetMin = new Vector2(0f, -24f - MobileTarget);
            reset.offsetMax = new Vector2(0f, -24f);
        }
        else
        {
            reset.anchorMin = reset.anchorMax = reset.pivot = new Vector2(0f, 0.5f);
            reset.sizeDelta = new Vector2(260f, 48f);
        }
        Shape(reset, "Button", true);
        resetButton = reset.gameObject.AddComponent<Button>();
        resetButton.transition = Selectable.Transition.None;
        resetButton.onClick.AddListener(ResetToDefaults);
        reset.gameObject.AddComponent<UIControlState>();
        TextMeshProUGUI label = Text("Label", reset, "Button", TextAlignmentOptions.Center);
        label.color = UIControlColors.Idle;
        label.text = UIText.Get("settings.reset", "RESET TO DEFAULTS");
        Stretch(label.rectTransform, 0f);

        savedNote = Text("Saved", footer, "Caption", mobile ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineRight);
        if (mobile)
        {
            savedNote.rectTransform.anchorMin = Vector2.zero;
            savedNote.rectTransform.anchorMax = new Vector2(1f, 0f);
            savedNote.rectTransform.pivot = new Vector2(0.5f, 0f);
            savedNote.rectTransform.offsetMin = new Vector2(0f, 8f);
            savedNote.rectTransform.offsetMax = new Vector2(0f, 44f);
        }
        else
        {
            savedNote.rectTransform.anchorMin = new Vector2(0.4f, 0f);
            savedNote.rectTransform.anchorMax = Vector2.one;
            savedNote.rectTransform.offsetMin = savedNote.rectTransform.offsetMax = Vector2.zero;
        }
        savedNote.text = UIText.Get("settings.saved", "SAVED IN THIS BROWSER");
    }

    private void ResetToDefaults()
    {
        pendingScale = float.NaN;
        DigitalTwinSettings.ResetToDefaults();
        if (quality != null && quality.IsDesktop)
            quality.SetHighQuality(false);
    }

    // -----------------------------------------------------------------------
    // Tabs' contents
    // -----------------------------------------------------------------------

    private void BuildDisplayTab()
    {
        Tab tab = tabs[0];
        Row scale = AddRow(tab, UIText.Get("settings.scale", "Interface scale"),
            UIText.Get("settings.scale.about", "Makes every panel, card and control larger or smaller. The new size is applied a moment after you stop moving the slider, so the layout does not jump while you drag."));
        scaleValue = AddSlider(scale, DigitalTwinSettings.MinimumInterfaceScale, DigitalTwinSettings.MaximumInterfaceScale, 0.05f,
            () => float.IsNaN(pendingScale) ? DigitalTwinSettings.InterfaceScale : pendingScale,
            QueueScale, null);
        refreshers.Add(UpdateScaleValue);

        Row contrast = AddRow(tab, UIText.Get("settings.contrast", "High contrast"),
            UIText.Get("settings.contrast.about", "Panels become near opaque and their edges firmer, so the interface reads over a bright scene, on a projector or in a bright room."));
        AddToggle(contrast, () => DigitalTwinSettings.HighContrast, v => DigitalTwinSettings.HighContrast = v);

        Row motion = AddRow(tab, UIText.Get("settings.motion", "Reduce motion"),
            UIText.Get("settings.motion.about", "Stops glows breathing and pulses travelling along the bars, and switches looks at once instead of blending them."));
        AddToggle(motion, () => DigitalTwinSettings.ReduceMotion, v => DigitalTwinSettings.ReduceMotion = v);

        Row shade = AddRow(tab, UIText.Get("settings.shade", "Edge shading"),
            UIText.Get("settings.shade.about", "Darkens the top and bottom of the screen behind the controls there, so they stand out against a bright sky or a pale floor."));
        AddChoice(shade, new[]
            {
                UIText.Get("settings.shade.off", "OFF"),
                UIText.Get("settings.shade.subtle", "SUBTLE"),
                UIText.Get("settings.shade.strong", "STRONG")
            },
            () => (int)DigitalTwinSettings.EdgeShade,
            i => DigitalTwinSettings.EdgeShade = (DigitalTwinSettings.EdgeShading)i, null);
    }

    private void BuildTelemetryTab()
    {
        Tab tab = tabs[1];
        Row units = AddRow(tab, UIText.Get("settings.units", "Units"),
            UIText.Get("settings.units.about", "Engineering writes units as they appear on data sheets: mm/s, kW, °C. Capitals matches the rest of the interface: MM/S, KW."));
        AddChoice(units, new[]
            {
                UIText.Get("settings.units.engineering", "mm/s"),
                UIText.Get("settings.units.capitals", "MM/S")
            },
            () => (int)DigitalTwinSettings.Units,
            i => DigitalTwinSettings.Units = (DigitalTwinSettings.UnitStyle)i, null);

        // Rails in all, two to a step: one each side of the model per step. A
        // phone offers only as many as it can run.
        int railChoices = DeviceKind.IsMobile
            ? TelemetryLayoutSettings.MaximumMobileRailsPerSide
            : TelemetryLayoutSettings.MaximumRailsPerSide;
        string[] railCounts = new string[railChoices];
        for (int i = 0; i < railChoices; i++)
            railCounts[i] = ((i + 1) * 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Row rails = AddRow(tab, UIText.Get("settings.rails", "Card rails"),
            UIText.Get("settings.rails.about", "How many rails of cards run beside the model, in all. More show more readings at once; fewer keep the model and its leader lines clear. A screen without room for them runs fewer, and the other cards wait on the next page."));
        AddChoice(rails, railCounts,
            () => Mathf.Min(DigitalTwinSettings.RailsPerSide, railChoices) - 1,
            i => DigitalTwinSettings.RailsPerSide = i + 1, null);

        Row dim = AddRow(tab, UIText.Get("settings.dim", "Dim other cards"),
            UIText.Get("settings.dim.about", "While a card is focused, the others fade back so its reading and leader line stand out. Alarm cards never fade."));
        AddToggle(dim, () => DigitalTwinSettings.DimOtherCards, v => DigitalTwinSettings.DimOtherCards = v);
    }

    private void BuildCameraTab()
    {
        Tab tab = tabs[2];
        Row orbit = AddRow(tab, UIText.Get("settings.orbit", "Orbit sensitivity"),
            UIText.Get("settings.orbit.about", "How far the camera turns for a drag, a swipe or a key held down."));
        AddSlider(orbit, DigitalTwinSettings.MinimumSensitivity, DigitalTwinSettings.MaximumSensitivity, 0.05f,
            () => DigitalTwinSettings.OrbitSensitivity, v => DigitalTwinSettings.OrbitSensitivity = v, Multiplier);

        Row zoom = AddRow(tab, UIText.Get("settings.zoom", "Zoom sensitivity"),
            UIText.Get("settings.zoom.about", "How far the camera moves in or out for a turn of the wheel, a pinch or a key held down."));
        AddSlider(zoom, DigitalTwinSettings.MinimumSensitivity, DigitalTwinSettings.MaximumSensitivity, 0.05f,
            () => DigitalTwinSettings.ZoomSensitivity, v => DigitalTwinSettings.ZoomSensitivity = v, Multiplier);

        Row invert = AddRow(tab, UIText.Get("settings.invert", "Invert tilt"),
            UIText.Get("settings.invert.about", "Dragging up tilts the camera down, as some flight controls do."));
        AddToggle(invert, () => DigitalTwinSettings.InvertVertical, v => DigitalTwinSettings.InvertVertical = v);

        Row smooth = AddRow(tab, UIText.Get("settings.smooth", "Smooth camera"),
            UIText.Get("settings.smooth.about", "The camera eases to where it is sent. Off, it follows the pointer exactly."));
        AddToggle(smooth, () => DigitalTwinSettings.SmoothCamera, v => DigitalTwinSettings.SmoothCamera = v);
    }

    private void BuildGraphicsTab()
    {
        Tab tab = tabs[3];
        Row detail = AddRow(tab, UIText.Get("settings.detail", "Detail"),
            UIText.Get("settings.detail.about", "HD renders sharper shadows and more effects. Phones stay on SD to keep the frame rate up."));
        AddChoice(detail, new[] { "SD", "HD" },
            () => quality != null && quality.HighQualityEnabled ? 1 : 0,
            i =>
            {
                if (quality != null)
                    quality.SetHighQuality(i == 1);
            },
            () => quality != null && quality.IsDesktop);
        Row rate = AddRow(tab, UIText.Get("settings.rate", "Frame rate limit"),
            UIText.Get("settings.rate.about", "Display matches the screen's refresh rate. A limit uses less power."));
        AddChoice(rate, new[]
            {
                UIText.Get("settings.rate.display", "DISPLAY"),
                "60",
                "30"
            },
            () => (int)DigitalTwinSettings.FrameRate,
            i => DigitalTwinSettings.FrameRate = (DigitalTwinSettings.FrameRateLimit)i, null);

        Row fps = AddRow(tab, UIText.Get("settings.fps", "Show frame rate"),
            UIText.Get("settings.fps.about", "Shows frames per second, frame time and the detail level in the top left corner."));
        AddToggle(fps, () => DigitalTwinSettings.ShowFrameRate, v => DigitalTwinSettings.ShowFrameRate = v);
    }

    private static string Multiplier(float value) => value.ToString("0.00", CultureInfo.InvariantCulture) + "×";

    private void RefreshAll()
    {
        foreach (Action refresh in refreshers)
            refresh();
    }

    // -----------------------------------------------------------------------
    // Rows and controls
    // -----------------------------------------------------------------------

    private Row AddRow(Tab tab, string title, string description)
    {
        if (mobile)
            return AddMobileRow(tab, title, description);
        RectTransform rect = Rect(title, tab.Content);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        float top = tab.Rows.Count * (RowHeight + RowGap);
        rect.offsetMin = new Vector2(0f, -(top + RowHeight));
        rect.offsetMax = new Vector2(0f, -top);

        // A faint light under the pointer, as a hovered segment has.
        Shape(rect, "Segment", true);
        rect.gameObject.AddComponent<UIControlState>();

        TextMeshProUGUI label = Text("Label", rect, "Body", TextAlignmentOptions.MidlineLeft);
        label.text = title;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(20f, 0f);
        label.rectTransform.offsetMax = new Vector2(-(ControlWidth + 24f), 0f);
        label.overflowMode = TextOverflowModes.Ellipsis;

        Row row = new() { Rect = rect, Title = title, Description = description };
        RowHover hover = rect.gameObject.AddComponent<RowHover>();
        hover.Owner = this;
        hover.Row = row;
        tab.Rows.Add(row);
        return row;
    }

    /// <summary>
    /// A row of the phone's page: its name, with a control at its end or
    /// across the row below, and its description under it all. Rows in a
    /// group are parted by a faint rule.
    /// </summary>
    private Row AddMobileRow(Tab tab, string title, string description)
    {
        if (tab.Rows.Count > 0)
        {
            Image rule = Rect("Rule", tab.Content).gameObject.AddComponent<Image>();
            rule.color = UIBuilder.Token("Accent", 0.1f, new Color(0.21f, 0.93f, 1f, 0.1f));
            rule.raycastTarget = false;
            LayoutElement thin = rule.gameObject.AddComponent<LayoutElement>();
            thin.minHeight = thin.preferredHeight = 1f;
        }

        RectTransform rect = Rect(title, tab.Content);
        VerticalLayoutGroup stack = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.padding = new RectOffset(16, 16, 10, 16);
        stack.spacing = 6f;
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        RectTransform line = Rect("Title Line", rect);
        HorizontalLayoutGroup across = line.gameObject.AddComponent<HorizontalLayoutGroup>();
        across.spacing = 12f;
        across.childAlignment = TextAnchor.MiddleLeft;
        across.childControlWidth = across.childControlHeight = true;
        across.childForceExpandWidth = across.childForceExpandHeight = false;
        LayoutElement lineSize = line.gameObject.AddComponent<LayoutElement>();
        lineSize.minHeight = MobileTarget;

        TextMeshProUGUI label = Text("Label", line, "Body", TextAlignmentOptions.MidlineLeft);
        label.text = title;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        RectTransform trailing = Rect("Trailing", line);
        trailing.gameObject.AddComponent<LayoutElement>();
        trailing.gameObject.SetActive(false);

        RectTransform below = Rect("Control", rect);
        below.gameObject.AddComponent<LayoutElement>();
        below.gameObject.SetActive(false);

        TextMeshProUGUI about = Text("About", rect, "Body", TextAlignmentOptions.TopLeft);
        about.text = description;
        about.fontSize = 16f;
        about.alpha = 0.62f;
        about.textWrappingMode = TextWrappingModes.Normal;

        Row row = new()
        {
            Rect = rect, Title = title, Description = description,
            TitleLine = line, Trailing = trailing, Across = below
        };
        tab.Rows.Add(row);
        return row;
    }

    /// <summary>The phone's page: the room at the end of a row's name line, <paramref name="width"/> wide.</summary>
    private static RectTransform TrailingSlot(Row row, float width)
    {
        LayoutElement size = row.Trailing.GetComponent<LayoutElement>();
        size.minWidth = size.preferredWidth = width;
        size.minHeight = size.preferredHeight = MobileTarget;
        row.Trailing.gameObject.SetActive(true);
        return row.Trailing;
    }

    /// <summary>The phone's page: the room across a row under its name, <paramref name="height"/> tall.</summary>
    private static RectTransform AcrossSlot(Row row, float height)
    {
        LayoutElement size = row.Across.GetComponent<LayoutElement>();
        size.minHeight = size.preferredHeight = height;
        row.Across.gameObject.SetActive(true);
        return row.Across;
    }

    private RectTransform ControlArea(Row row, float width)
    {
        RectTransform area = Rect("Control", row.Rect);
        area.anchorMin = area.anchorMax = area.pivot = new Vector2(1f, 0.5f);
        area.anchoredPosition = new Vector2(-14f, 0f);
        area.sizeDelta = new Vector2(width, ControlHeight);
        return area;
    }

    private void AddToggle(Row row, Func<bool> get, Action<bool> set)
    {
        RectTransform area = mobile ? TrailingSlot(row, 112f) : ControlArea(row, ControlWidth);

        RectTransform box = Rect("Box", area);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(1f, 0.5f);
        box.sizeDelta = new Vector2(34f, 34f);
        Shape(box, "Button", true);
        box.gameObject.AddComponent<UIControlState>();
        Toggle toggle = box.gameObject.AddComponent<Toggle>();
        toggle.transition = Selectable.Transition.None;
        toggle.toggleTransition = Toggle.ToggleTransition.None;

        RectTransform mark = Rect("Check", box);
        Stretch(mark, 9f);
        Graphic check = Shape(mark, "Accent Bar", false);
        check.color = UIControlColors.Selected;
        toggle.graphic = check;

        TextMeshProUGUI state = Text("State", area, "Caption", TextAlignmentOptions.MidlineRight);
        state.rectTransform.anchorMin = Vector2.zero;
        state.rectTransform.anchorMax = Vector2.one;
        state.rectTransform.offsetMin = Vector2.zero;
        state.rectTransform.offsetMax = new Vector2(-48f, 0f);

        void Refresh()
        {
            bool on = get();
            toggle.SetIsOnWithoutNotify(on);
            check.enabled = on;
            state.text = on ? UIText.Get("settings.on", "ON") : UIText.Get("settings.off", "OFF");
            // The word is the toggle's label: near white when on, as a chosen segment's.
            state.color = UIControlColors.For(on);
        }

        toggle.onValueChanged.AddListener(value =>
        {
            set(value);
            Refresh();
        });
        // On a phone the whole name line flips it, not only the box.
        if (mobile)
        {
            Image hit = row.TitleLine.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            Button line = row.TitleLine.gameObject.AddComponent<Button>();
            line.transition = Selectable.Transition.None;
            line.navigation = new Navigation { mode = Navigation.Mode.None };
            line.onClick.AddListener(() => toggle.isOn = !toggle.isOn);
        }
        refreshers.Add(Refresh);
        row.Controls.Add(toggle);
        Refresh();
    }

    private void AddChoice(Row row, string[] options, Func<int> get, Action<int> set, Func<bool> available)
    {
        // On a phone, across the row, so each choice is wide enough for a thumb.
        RectTransform area = mobile ? AcrossSlot(row, MobileTarget) : ControlArea(row, ControlWidth);
        Shape(area, "Panel", false);
        HorizontalLayoutGroup stack = area.gameObject.AddComponent<HorizontalLayoutGroup>();
        stack.padding = new RectOffset(4, 4, 4, 4);
        stack.spacing = 4f;
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = stack.childForceExpandHeight = true;

        var buttons = new List<Button>();
        var states = new List<UIControlState>();
        var labels = new List<TextMeshProUGUI>();
        for (int i = 0; i < options.Length; i++)
        {
            int index = i;
            RectTransform rect = Rect(options[i], area);
            Shape(rect, "Segment", true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            UIControlState state = rect.gameObject.AddComponent<UIControlState>();
            TextMeshProUGUI label = Text("Label", rect, "Caption", TextAlignmentOptions.Center);
            label.text = options[i];
            // Unit samples keep their own case.
            label.fontStyle &= ~FontStyles.UpperCase;
            Stretch(label.rectTransform, 0f);
            buttons.Add(button);
            states.Add(state);
            labels.Add(label);
            row.Controls.Add(button);
            button.onClick.AddListener(() =>
            {
                set(index);
                RefreshAll();
            });
        }

        void Refresh()
        {
            int chosen = get();
            bool usable = available == null || available();
            for (int i = 0; i < buttons.Count; i++)
            {
                bool selected = i == chosen;
                buttons[i].interactable = usable;
                states[i].Selected = selected;
                labels[i].color = UIControlColors.For(selected, usable);
            }
        }

        refreshers.Add(Refresh);
        Refresh();
    }

    private TextMeshProUGUI AddSlider(Row row, float min, float max, float step, Func<float> get, Action<float> set,
        Func<float, string> format)
    {
        // Desktop: the track and its value side by side. A phone's page: the
        // value at the end of the name line, the track across the row, so a
        // thumb has its whole width to drag along.
        RectTransform area = mobile ? AcrossSlot(row, MobileTarget) : ControlArea(row, ControlWidth);
        RectTransform valueArea = mobile ? TrailingSlot(row, 84f) : area;

        // On a phone the track is drawn at a control's usual height, and its
        // touch area reaches the row's full target above and below it.
        float trackInset = mobile ? (MobileTarget - ControlHeight) * 0.5f : 0f;
        RectTransform track = Rect("Track", area);
        track.anchorMin = new Vector2(0f, 0f);
        track.anchorMax = new Vector2(1f, 1f);
        track.offsetMin = new Vector2(0f, trackInset);
        track.offsetMax = new Vector2(mobile ? 0f : -96f, -trackInset);
        Graphic trackShape = Shape(track, "Button", true);
        trackShape.raycastPadding = new Vector4(0f, -trackInset, 0f, -trackInset);
        track.gameObject.AddComponent<UIControlState>();
        Slider slider = track.gameObject.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;

        RectTransform fillArea = Rect("Fill Area", track);
        Stretch(fillArea, 6f);
        RectTransform fill = Rect("Fill", fillArea);
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = new Vector2(0f, 1f);
        fill.offsetMin = fill.offsetMax = Vector2.zero;
        Graphic fillGraphic = Shape(fill, "Accent Bar", false);
        // The value's bar is a mark, not a label: the accent, faint.
        fillGraphic.color = UIBuilder.Token("Accent", 0.35f, new Color(0.21f, 0.93f, 1f, 0.35f));
        slider.fillRect = fill;

        RectTransform handleArea = Rect("Handle Area", track);
        Stretch(handleArea, 6f);
        RectTransform handle = Rect("Handle", handleArea);
        handle.anchorMin = new Vector2(0f, 0f);
        handle.anchorMax = new Vector2(0f, 1f);
        handle.sizeDelta = new Vector2(4f, 6f);
        Graphic handleGraphic = Shape(handle, "Accent Bar", false);
        handleGraphic.color = new Color(0.92f, 0.99f, 1f, 1f);
        slider.handleRect = handle;
        slider.targetGraphic = handleGraphic;

        RectTransform box = Rect("Value", valueArea);
        box.anchorMin = new Vector2(1f, 0f);
        box.anchorMax = new Vector2(1f, 1f);
        box.pivot = new Vector2(1f, 0.5f);
        box.sizeDelta = new Vector2(84f, mobile ? ControlHeight - MobileTarget : 0f);
        Shape(box, "Inset", false);
        TextMeshProUGUI value = Text("Text", box, "Button", TextAlignmentOptions.Center);
        // A readout, not a control's label.
        value.color = UIBuilder.Token("Text", 1f, Color.white);
        Stretch(value.rectTransform, 0f);

        void Refresh()
        {
            float current = get();
            slider.SetValueWithoutNotify(current);
            if (format != null)
                value.text = format(current);
        }

        slider.onValueChanged.AddListener(raw =>
        {
            float snapped = Mathf.Clamp(Mathf.Round(raw / step) * step, min, max);
            slider.SetValueWithoutNotify(snapped);
            set(snapped);
            if (format != null)
                value.text = format(snapped);
        });
        refreshers.Add(Refresh);
        row.Controls.Add(slider);
        Refresh();
        return value;
    }

    // -----------------------------------------------------------------------
    // Keyboard navigation inside the panel
    // -----------------------------------------------------------------------

    /// <summary>
    /// Arrow keys move between the panel's rows only, never out to the
    /// interface behind it: up and down between rows (the first control of
    /// each), left and right between the choices of a row. A slider keeps left
    /// and right for its value.
    /// </summary>
    private void WireNavigation(Tab tab)
    {
        var chain = new List<List<Selectable>>();
        var tabButtons = new List<Selectable>();
        foreach (Tab t in tabs)
            tabButtons.Add(t.Button);
        chain.Add(tabButtons);
        foreach (Row row in tab.Rows)
            chain.Add(row.Controls);
        chain.Add(new List<Selectable> { resetButton });

        for (int r = 0; r < chain.Count; r++)
        {
            List<Selectable> row = chain[r];
            Selectable up = r > 0 ? First(chain[r - 1]) : closeButton;
            Selectable down = r < chain.Count - 1 ? First(chain[r + 1]) : null;
            for (int i = 0; i < row.Count; i++)
            {
                // The tab row is shared: its buttons lead down into the tab shown.
                if (r == 0 && tabs.IndexOf(tab) != i)
                    continue;
                Navigation nav = new()
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = up,
                    selectOnDown = down,
                    selectOnLeft = row[i] is Slider ? null : i > 0 ? row[i - 1] : null,
                    selectOnRight = row[i] is Slider ? null : i < row.Count - 1 ? row[i + 1] : null
                };
                row[i].navigation = nav;
            }
        }
    }

    private static Selectable First(List<Selectable> row) => row.Count > 0 ? row[0] : null;

    /// <summary>
    /// The phone's page, for a keyboard attached to a tablet: up and down
    /// through every row of every group, from the back arrow to Reset.
    /// </summary>
    private void WireMobileNavigation()
    {
        var chain = new List<List<Selectable>> { new() { backButton } };
        foreach (Tab tab in tabs)
        {
            foreach (Row row in tab.Rows)
                chain.Add(row.Controls);
        }
        chain.Add(new List<Selectable> { resetButton });
        for (int r = 0; r < chain.Count; r++)
        {
            List<Selectable> row = chain[r];
            Selectable up = r > 0 ? First(chain[r - 1]) : null;
            Selectable down = r < chain.Count - 1 ? First(chain[r + 1]) : null;
            for (int i = 0; i < row.Count; i++)
            {
                row[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = up,
                    selectOnDown = down,
                    selectOnLeft = row[i] is Slider ? null : i > 0 ? row[i - 1] : null,
                    selectOnRight = row[i] is Slider ? null : i < row.Count - 1 ? row[i + 1] : null
                };
            }
        }
    }

    // -----------------------------------------------------------------------
    // Layout: the desktop's panel, or the phone's page
    // -----------------------------------------------------------------------

    private void Layout()
    {
        Vector2 size = canvasRect.rect.size;
        laidOutFor = size;
        if (mobile)
        {
            LayoutMobile(size);
            return;
        }

        float width = Mathf.Min(WidePanelWidth, size.x - 96f);
        float height = Mathf.Min(WidePanelHeight, size.y - 120f);
        panel.sizeDelta = new Vector2(width, height);
        panel.anchoredPosition = Vector2.zero;

        PlaceRow(header, Padding - 8f, 48f, Padding);
        PlaceRow(tabBar, Padding + 64f, 56f, Padding);
        PlaceRow(footer, height - Padding - 48f, 48f, Padding);

        // Room for the well's edge, which reaches 20 past the body.
        float bodyTop = Padding + 64f + 56f + 40f;
        float bodyBottom = Padding + 48f + 40f;
        body.anchorMin = Vector2.zero;
        body.anchorMax = Vector2.one;
        body.offsetMin = new Vector2(Padding, bodyBottom);
        body.offsetMax = new Vector2(-Padding, -bodyTop);

        // The rows on the left, the description of the one under the pointer on the right.
        float listWidth = Mathf.Min(660f, (width - Padding * 2f) * 0.58f);
        list.anchorMin = Vector2.zero;
        list.anchorMax = new Vector2(0f, 1f);
        list.pivot = new Vector2(0f, 0.5f);
        list.offsetMin = Vector2.zero;
        list.offsetMax = new Vector2(listWidth, 0f);
        aside.anchorMin = Vector2.zero;
        aside.anchorMax = Vector2.one;
        aside.offsetMin = new Vector2(listWidth + 56f, 0f);
        aside.offsetMax = Vector2.zero;
        RectTransform line = divider.rectTransform;
        line.anchorMin = new Vector2(0f, 0f);
        line.anchorMax = new Vector2(0f, 1f);
        line.pivot = new Vector2(0.5f, 0.5f);
        line.offsetMin = new Vector2(listWidth + 28f, 0f);
        line.offsetMax = new Vector2(listWidth + 29f, 0f);

        foreach (Tab tab in tabs)
        {
            tab.Content.SetParent(list, false);
            Stretch(tab.Content, 0f);
        }

        asideTitle.rectTransform.anchorMin = new Vector2(0f, 1f);
        asideTitle.rectTransform.anchorMax = new Vector2(1f, 1f);
        asideTitle.rectTransform.pivot = new Vector2(0.5f, 1f);
        asideTitle.rectTransform.offsetMin = new Vector2(0f, -36f);
        asideTitle.rectTransform.offsetMax = Vector2.zero;
        asideBody.rectTransform.anchorMin = Vector2.zero;
        asideBody.rectTransform.anchorMax = Vector2.one;
        asideBody.rectTransform.offsetMin = Vector2.zero;
        asideBody.rectTransform.offsetMax = new Vector2(0f, -48f);
    }

    /// <summary>
    /// The phone's page inside the safe area (clear of the notch and the
    /// browser's toolbar), laid out MobileContentWidth units across and scaled
    /// to its width, as the phone's Service and Ask tabs are. The fill behind
    /// it reaches the screen's edges.
    /// </summary>
    private void LayoutMobile(Vector2 size)
    {
        UnityEngine.Rect safe = SafeRect(size);
        laidOutSafe = safe;
        float scale = Mathf.Max(0.5f, safe.width / MobileContentWidth);
        page.localScale = new Vector3(scale, scale, 1f);
        page.anchoredPosition = new Vector2(safe.xMin, -(size.y - safe.yMax));
        page.sizeDelta = safe.size / scale;
    }

    /// <summary>The safe area in this canvas's units, from its bottom left.</summary>
    private static UnityEngine.Rect SafeRect(Vector2 size)
    {
        UnityEngine.Rect safe = DeviceSafeArea.Current;
        float width = Mathf.Max(1f, Screen.width);
        float height = Mathf.Max(1f, Screen.height);
        return UnityEngine.Rect.MinMaxRect(safe.xMin / width * size.x, safe.yMin / height * size.y,
            safe.xMax / width * size.x, safe.yMax / height * size.y);
    }

    private static void PlaceRow(RectTransform rect, float top, float height, float sides)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(sides, -(top + height));
        rect.offsetMax = new Vector2(-sides, -top);
    }

    // -----------------------------------------------------------------------
    // Building blocks
    // -----------------------------------------------------------------------

    private RectTransform Rect(string name, RectTransform parent) => UIBuilder.Rect(name, parent, gameObject.layer);

    private static void Stretch(RectTransform rect, float inset) => UIBuilder.Stretch(rect, inset);

    private static Graphic Shape(RectTransform rect, string component, bool raycast) =>
        UIBuilder.Shape(rect, component, raycast);

    private static void Icon(RectTransform rect, Sprite sprite) => UIBuilder.Icon(rect, sprite);

    private Button IconButton(RectTransform rect, Sprite sprite, Action onClick)
    {
        Shape(rect, "Icon Button", true);
        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => onClick());
        rect.gameObject.AddComponent<UIControlState>();
        RectTransform glyph = Rect("Icon", rect);
        glyph.anchorMin = glyph.anchorMax = new Vector2(0.5f, 0.5f);
        glyph.sizeDelta = new Vector2(24f, 24f);
        Icon(glyph, sprite);
        return button;
    }

    /// <summary>A key's name in a small box, as a hint: ESC, Q, E.</summary>
    private RectTransform Keycap(RectTransform parent, string key, float width)
    {
        RectTransform cap = Rect(key + " Key", parent);
        cap.sizeDelta = new Vector2(width, 28f);
        Shape(cap, "Inset", false);
        TextMeshProUGUI label = Text("Label", cap, "Caption", TextAlignmentOptions.Center);
        label.text = key;
        label.fontSize = 14f;
        label.characterSpacing = 1f;
        Stretch(label.rectTransform, 0f);
        return cap;
    }

    /// <summary>A text in one of the theme's text styles.</summary>
    private TextMeshProUGUI Text(string name, RectTransform parent, string style, TextAlignmentOptions alignment) =>
        UIBuilder.Text(name, parent, style, alignment, gameObject.layer);
}
