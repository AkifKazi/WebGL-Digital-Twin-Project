using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The Ask window: a conversation with the <see cref="TwinAssistant"/> about
/// the machine.
///
/// In the wide layout it is opened from an Ask button beside the Telemetry /
/// Service switch, and does not block the twin: it is a window the viewer drags
/// by its header (the grip at its top edge says so) anywhere between the top
/// and bottom controls, over the rails or the service list if they like; the
/// cards, rows and model behind it stay usable, and leader lines fade where
/// they pass under it. Its place is kept in the browser.
///
/// On a phone held upright a window has no room, so Ask is a third tab of the
/// switch (TELEMETRY | SERVICE | ASK) and, like Service, takes the screen below
/// the switch on the same opaque backdrop. Choosing Telemetry or Service, or
/// pressing a reading or a service in an answer, leaves it for that tab.
///
/// A message sent is a speech bubble with a tail (the design system's Message
/// Sent); the assistant's reply is plain text across the window, so the two
/// sides read apart at a glance. A reply can carry the readings and services it
/// rests on, under TELEMETRY DATA and SERVICE DATA, as compact rows in the
/// neutral look: only a figure outside its normal band takes its state's
/// colour. A reading's row has its last hour as a line, its normal band and its
/// trend, which is most of what its detail panel would say; pressing it shows
/// it on the model. A service row opens the part in the Service view. Both end
/// in a chevron, as the service list's rows do. Suggested questions under the
/// latest reply send themselves when pressed.
///
/// Keyboard: Tab reaches the window after the rest of the interface; Enter in
/// the field sends; Esc, with the keyboard in the window, closes it.
///
/// Built in code; the scene only needs this component and its sprites.
/// </summary>
[DisallowMultipleComponent]
public sealed class AskTwinWindow : MonoBehaviour, IEscapeHandler
{
    [SerializeField] private Sprite askIcon;
    [SerializeField] private Sprite closeIcon;
    [SerializeField] private Sprite sendIcon;

    [Tooltip("Optional. Found, or added to the Digital Twin Runtime object, when empty.")]
    [SerializeField] private TwinAssistant assistant;

    [Header("Size (canvas units at 1920 x 1080)")]
    [SerializeField, Min(360f)] private float windowWidth = 560f;
    [SerializeField, Min(320f)] private float windowHeight = 700f;
    [Tooltip("Room kept between the window and the top and bottom controls, and the screen's sides.")]
    [SerializeField, Min(0f)] private float margin = 16f;
    [Tooltip("Where the window first opens: its left edge, as a share of the screen's width.")]
    [SerializeField, Range(0f, 1f)] private float defaultLeft = 0.17f;
    [Tooltip("On a phone held upright, the Ask tab lays out its content this many units wide, then scales " +
             "to the screen's width, as the Service tab's panel does (the portrait canvas is 1080 units across).")]
    [SerializeField, Min(320f)] private float tabContentWidth = 540f;

    private const float HeaderHeight = 64f;
    private const float ComposerHeight = 88f;
    private const float FieldHeight = 48f;
    // From the question field to the rule above it; the Ask tab keeps the same
    // room between the switch and its own rule, so the two ends match.
    private const float FieldGap = (ComposerHeight - FieldHeight) * 0.5f;
    private const float Padding = 20f;
    private const float BubblePaddingX = 16f;
    private const float BubblePaddingY = 11f;
    private const float BubbleShare = 0.82f;
    private const float TailRoom = 14f;
    private const float ReadingHeight = 76f;
    private const float RowHeight = 64f;
    private const float RowPadding = 16f;
    private const float ChevronSpace = 30f;
    private const float LauncherPadding = 20f;
    private const float LauncherIconGap = 10f;
    private const float LauncherIconSize = 24f;
    private const float TabWidth = 130f;
    private const float ViewTabWidth = 160f;
    private const float AppearSeconds = 0.18f;
    private const float GripWidth = 40f;
    private const int SortingOrder = 100;
    private const string PositionKeyX = "ask.window.x";
    private const string PositionKeyY = "ask.window.y";

    /// <summary>True while the window is open.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>The window, or the Ask tab on a phone held upright.</summary>
    public RectTransform Window => window;

    /// <summary>The question field.</summary>
    public TMP_InputField Field => field;

    /// <summary>Messages in the conversation, the greeting included.</summary>
    public int MessageCount => messageCount;

    /// <summary>The suggested questions under the latest reply.</summary>
    public IReadOnlyList<Button> Suggestions => suggestionButtons;

    /// <summary>The cards and rows of the latest reply.</summary>
    public IReadOnlyList<Button> Evidence => evidenceButtons;

    /// <summary>True while Ask is a tab of the switch (a phone held upright) rather than a window.</summary>
    public bool IsTab => tabbed;

    private readonly List<Launcher> launchers = new();
    private readonly List<Button> suggestionButtons = new();
    private readonly List<Button> evidenceButtons = new();
    private readonly List<Bubble> bubbles = new();
    private readonly List<TelemetryHistoryBucket> buckets = new();
    private readonly List<Vector2> sparkPoints = new();
    private readonly List<float> sparkLimits = new();
    private readonly List<Color> sparkLimitColors = new();
    private readonly List<CanvasGroup> appearing = new();
    private readonly Vector3[] corners = new Vector3[4];

    // The layouts' parts the window lays itself out by, found once rather than
    // searched for every frame; the layouts swap by turning on and off.
    private DigitalTwinModeSwitch[] switches;
    private ViewModeSegmentedControl[] viewControls;
    private PortraitServiceView[] portraitViews;

    private RectTransform canvasRect;
    private Canvas hostCanvas;
    private RectTransform window;
    private RectTransform header;
    private RectTransform headerRule;
    private RectTransform composer;
    private Graphic frame;                 // the window's panel
    private Image tabFill;                 // the Ask tab's opaque fill, in place of the panel
    private RectTransform tabBackdrop;     // the same fill behind the switch's bar, to the screen's edges
    private RectTransform thread;
    private RectTransform content;
    private ScrollRect scroll;
    private TMP_InputField field;
    private Button sendButton;
    private Graphic sendGlyph;
    private RectTransform typingRow;
    private TextMeshProUGUI typingText;
    private RectTransform suggestionStack;
    private Graphic[] grip;
    private ITelemetryHistoryProvider history;

    private Vector2 place;                 // the window's bottom-left corner, as shares of the room the free area leaves it
    private bool placed;                   // false until dragged (or a kept place loads): it opens in its first place
    private Vector2 grabOffset;            // from that corner to the pointer, while dragged
    private bool gripHovered;
    private bool dragging;
    private Rect area;                     // where the window may go
    private Vector2 laidOutFor;
    private bool tabbed;
    private float tabBottom;               // the safe area's bottom, in the tab's own units
    private bool attached;
    private bool scrollPending;
    private bool waiting;
    private int messageCount;
    private bool greeted;

    private sealed class Bubble
    {
        public RectTransform Rect;
        public TextMeshProUGUI Text;
        public LayoutElement Size;
    }

    /// <summary>An Ask button beside the wide layout's switch, or the Ask tab in the portrait one.</summary>
    private sealed class Launcher
    {
        public RectTransform Root;
        public UIControlState State;
        public Graphic Icon;
        public TextMeshProUGUI Label;
        public float Inset;
        public string FittedText;
        public Button Tab;                 // the portrait switch's third segment
        public DigitalTwinModeSwitch Switch;
    }

    /// <summary>Drags the window by its header, and lights the grip while the pointer is on it.</summary>
    private sealed class DragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerEnterHandler, IPointerExitHandler
    {
        public AskTwinWindow Owner;

        public void OnBeginDrag(PointerEventData eventData) => Owner.BeginDrag(eventData);

        public void OnDrag(PointerEventData eventData) => Owner.DragTo(eventData);

        public void OnEndDrag(PointerEventData eventData) => Owner.EndDrag();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!UIPointerUtility.IsTouch(eventData))
                Owner.SetGripHovered(true);
        }

        public void OnPointerExit(PointerEventData eventData) => Owner.SetGripHovered(false);
    }

    // -----------------------------------------------------------------------
    // Life cycle
    // -----------------------------------------------------------------------

    private IEnumerator Start()
    {
        if (assistant == null)
            assistant = TwinAssistant.Find();
        // The settings panel puts its gear beside each switch in its own Start;
        // the Ask button goes after it.
        yield return null;
        AttachLaunchers();
        if (FindHost())
        {
            Build();
            // Esc with the keyboard in the window closes it, and only that.
            KeyboardNavigator.EscapeHandler = this;
        }
    }

    private void OnDisable()
    {
        if (IsOpen)
            SetOpen(false);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(KeyboardNavigator.EscapeHandler, this))
            KeyboardNavigator.EscapeHandler = null;
        if (window != null)
            Destroy(window.gameObject);
    }

    private void LateUpdate()
    {
        FitLaunchers();
        if (window == null)
            return;
        // The layouts swap, and with them the canvas the window belongs on.
        if (FindHost() && window.parent != canvasRect)
        {
            window.SetParent(canvasRect, false);
            laidOutFor = Vector2.zero;
        }
        if (!IsOpen)
            return;

        Layout(false);
        Animate();
        if (scrollPending)
        {
            scrollPending = false;
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scroll.verticalNormalizedPosition = 0f;
        }
    }

    // The keyboard navigator hands Esc here while the keyboard is in the
    // window, and to nothing else: the focused card and the service detail
    // stay as they are, and the keyboard goes back to the Ask button.
    RectTransform IEscapeHandler.EscapeArea => IsOpen ? window : null;

    void IEscapeHandler.HandleEscape() => SetOpen(false);

    // -----------------------------------------------------------------------
    // Opening and closing
    // -----------------------------------------------------------------------

    public void ToggleOpen() => SetOpen(!IsOpen);

    public void SetOpen(bool open)
    {
        if (window == null)
        {
            IsOpen = false;
            return;
        }
        bool wasOpen = IsOpen;
        IsOpen = open;
        window.gameObject.SetActive(open);
        foreach (Launcher launcher in launchers)
            ShowLauncher(launcher, open);
        if (!open)
        {
            dragging = false;
            gripHovered = false;
            if (tabBackdrop != null)
                tabBackdrop.gameObject.SetActive(false);
        }

        if (open)
        {
            window.SetAsLastSibling();
            Layout(true);
            if (!greeted)
            {
                greeted = true;
                AddReply(assistant.Greeting());
            }
            scrollPending = true;
            if (KeyboardNavigator.KeyboardMode)
                KeyboardNavigator.Focus(field);
        }
        else if (wasOpen)
        {
            // The keyboard goes back to the button it opened the window from.
            Selectable launcher = ActiveLauncher();
            if (KeyboardNavigator.KeyboardMode && launcher != null)
                KeyboardNavigator.Focus(launcher);
            else if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null &&
                     EventSystem.current.currentSelectedGameObject.transform.IsChildOf(window))
                KeyboardNavigator.ClearFocus();
        }
    }

    private Selectable ActiveLauncher()
    {
        foreach (Launcher launcher in launchers)
        {
            Selectable button = launcher.Tab != null ? launcher.Tab
                : launcher.State != null ? launcher.State.GetComponent<Selectable>() : null;
            if (button != null && button.isActiveAndEnabled)
                return button;
        }
        return null;
    }

    /// <summary>Leaves the Ask tab, before an answer's reading or service takes the viewer to its own.</summary>
    private void LeaveTab()
    {
        if (IsOpen && tabbed)
            SetOpen(false);
    }

    // -----------------------------------------------------------------------
    // Asking
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sends <paramref name="question"/> as the viewer's message and waits for
    /// the reply; <paramref name="ruleQuestion"/> is what the twin's own rules
    /// read instead, for a suggestion shown in another language.
    /// </summary>
    public void Send(string question, string ruleQuestion = null)
    {
        if (waiting || string.IsNullOrWhiteSpace(question) || window == null)
            return;
        if (!IsOpen)
            SetOpen(true);
        ClearSuggestions();
        AddSent(question.Trim());
        field.SetTextWithoutNotify(string.Empty);
        waiting = true;
        SetSendable(false);
        ShowTyping(true);
        assistant.Ask(question.Trim(), reply =>
        {
            // The answer can come after the window is gone (the scene closing).
            if (this == null || content == null)
                return;
            waiting = false;
            SetSendable(true);
            ShowTyping(false);
            AddReply(reply);
        }, ruleQuestion);
    }

    /// <summary>Send can be pressed, or not while an answer is awaited: its icon says which.</summary>
    private void SetSendable(bool usable)
    {
        if (sendButton != null)
            sendButton.interactable = usable;
        if (sendGlyph != null)
            sendGlyph.color = UIControlColors.For(false, usable);
    }

    private void SendFromField(string text)
    {
        Send(text);
        // On a desktop the field stays ready for the next question; on a phone
        // the keyboard goes away so the answer can be read.
        if (!DeviceKind.IsMobile && field != null)
            field.ActivateInputField();
    }

    // -----------------------------------------------------------------------
    // The canvas, the launchers
    // -----------------------------------------------------------------------

    /// <summary>The root canvas of the layout on show: the window belongs on it.</summary>
    private bool FindHost()
    {
        DigitalTwinModeSwitch active = ActiveSwitch();
        Canvas canvas = active != null ? active.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
            return canvasRect != null;
        hostCanvas = canvas.rootCanvas;
        canvasRect = (RectTransform)hostCanvas.transform;
        return true;
    }

    private DigitalTwinModeSwitch ActiveSwitch()
    {
        switches ??= FindObjectsByType<DigitalTwinModeSwitch>(FindObjectsInactive.Include);
        foreach (DigitalTwinModeSwitch candidate in switches)
        {
            if (candidate != null && candidate.isActiveAndEnabled)
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// An Ask button beside the wide switch, after the settings gear; and ASK
    /// as a third segment of the portrait one.
    /// </summary>
    private void AttachLaunchers()
    {
        if (attached)
            return;
        attached = true;
        switches = FindObjectsByType<DigitalTwinModeSwitch>(FindObjectsInactive.Include);
        foreach (DigitalTwinModeSwitch modeSwitch in switches)
        {
            RectTransform source = (RectTransform)modeSwitch.transform;
            RectTransform parent = source.parent as RectTransform;
            if (parent == null)
                continue;
            bool leftAnchored = source.anchorMin.x < 0.01f && source.anchorMax.x < 0.01f;
            if (!leftAnchored)
            {
                // A centred switch (portrait): Ask is one of its tabs.
                AttachTab(modeSwitch);
                continue;
            }

            RectTransform gear = parent.Find("Settings Button") as RectTransform;
            RectTransform button = UIBuilder.Rect("Ask Button", parent, gameObject.layer);
            button.SetSiblingIndex((gear != null ? gear : source).GetSiblingIndex() + 1);
            // Translucent, like the switch: leader lines fade where they cross it.
            button.gameObject.AddComponent<LeaderLineObstacle>();
            float height = source.sizeDelta.y;
            float scale = source.localScale.x;
            button.localScale = source.localScale;
            button.sizeDelta = new Vector2(height, height);
            // After the gear, the same gap as the gear keeps from the switch.
            RectTransform before = gear != null ? gear : source;
            button.anchorMin = button.anchorMax = source.anchorMin;
            button.pivot = new Vector2(0f, source.pivot.y);
            float beforeRight = before.anchoredPosition.x +
                                before.sizeDelta.x * before.localScale.x * (1f - before.pivot.x);
            button.anchoredPosition = new Vector2(beforeRight + 12f * scale, source.anchoredPosition.y);

            // The frame, drawn like the switch's.
            Image frame = button.gameObject.AddComponent<UIShape>();
            if (modeSwitch.TryGetComponent(out Image switchFrame))
                UIShape.CopyLook(switchFrame, frame);
            frame.raycastTarget = false;
            float inset = modeSwitch.TryGetComponent(out HorizontalLayoutGroup stack) ? stack.padding.left : 6f;

            RectTransform face = UIBuilder.Rect("Face", button, gameObject.layer);
            UIBuilder.Stretch(face, inset);
            UIBuilder.Shape(face, "Segment", true);
            Button press = face.gameObject.AddComponent<Button>();
            press.transition = Selectable.Transition.None;
            press.onClick.AddListener(ToggleOpen);
            Launcher launcher = new()
            {
                Root = button,
                State = face.gameObject.AddComponent<UIControlState>(),
                Inset = inset
            };

            RectTransform glyph = UIBuilder.Rect("Icon", face, gameObject.layer);
            glyph.anchorMin = glyph.anchorMax = new Vector2(0f, 0.5f);
            glyph.pivot = new Vector2(0f, 0.5f);
            glyph.sizeDelta = new Vector2(LauncherIconSize, LauncherIconSize);
            glyph.anchoredPosition = new Vector2(LauncherPadding, 0f);
            launcher.Icon = UIBuilder.Icon(glyph, askIcon);

            {
                TextMeshProUGUI label = UIBuilder.Text("Label", face, "Button", TextAlignmentOptions.MidlineLeft);
                // Set as the switch's own labels are, so ASK reads like SERVICE beside it.
                TMP_Text twin = modeSwitch.ServiceButton != null
                    ? modeSwitch.ServiceButton.GetComponentInChildren<TMP_Text>(true)
                    : null;
                if (twin != null)
                {
                    label.font = twin.font;
                    label.fontSharedMaterial = twin.fontSharedMaterial;
                    label.fontSize = twin.fontSize;
                    label.characterSpacing = twin.characterSpacing;
                    label.fontStyle = twin.fontStyle | FontStyles.UpperCase;
                    label.fontWeight = twin.fontWeight;
                }
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = new Vector2(LauncherPadding + LauncherIconSize + LauncherIconGap, 0f);
                label.rectTransform.offsetMax = Vector2.zero;
                UITextBinding.Bind(label, "ask.launcher", "ASK");
                launcher.Label = label;
            }
            launchers.Add(launcher);
            ShowLauncher(launcher, false);
            FitLauncher(launcher);
        }
    }

    /// <summary>
    /// ASK as the portrait switch's third segment, made from its Service
    /// segment so it is drawn, lit and labelled as the others are. The three
    /// share a little more than the two had, so the bar stays clear of the gear:
    /// ASK is narrower, its label being short, and the views give up some room.
    /// </summary>
    private void AttachTab(DigitalTwinModeSwitch modeSwitch)
    {
        Button service = modeSwitch.ServiceButton;
        if (service == null)
            return;
        Button tab = Instantiate(service, modeSwitch.transform);
        tab.name = "Ask Button";
        tab.onClick = new Button.ButtonClickedEvent();
        tab.onClick.AddListener(() => SetOpen(true));
        UITextBinding.Bind(tab.GetComponentInChildren<TMP_Text>(true), "ask.launcher", "ASK");
        if (tab.TryGetComponent(out LayoutElement size))
            size.preferredWidth = TabWidth;
        foreach (Button view in new[] { modeSwitch.TelemetryButton, service })
        {
            if (view != null && view.TryGetComponent(out LayoutElement viewSize))
                viewSize.preferredWidth = Mathf.Min(viewSize.preferredWidth, ViewTabWidth);
        }
        // The bar as wide as its segments ask for.
        if (modeSwitch.TryGetComponent(out HorizontalLayoutGroup stack))
        {
            float width = stack.padding.horizontal;
            int count = 0;
            foreach (Transform child in modeSwitch.transform)
            {
                if (child.TryGetComponent(out LayoutElement element))
                {
                    width += element.preferredWidth;
                    count++;
                }
            }
            width += stack.spacing * Mathf.Max(0, count - 1);
            RectTransform bar = (RectTransform)modeSwitch.transform;
            bar.sizeDelta = new Vector2(width, bar.sizeDelta.y);
        }
        // Telemetry or Service, pressed or chosen by a script, leaves Ask.
        modeSwitch.ViewChosen += _ => LeaveTab();
        Launcher launcher = new() { Tab = tab, Switch = modeSwitch };
        launchers.Add(launcher);
        ShowLauncher(launcher, false);
    }

    /// <summary>The selected look, as the chosen segment beside it has: its label and icon near white (UIControlColors).</summary>
    private void ShowLauncher(Launcher launcher, bool open)
    {
        if (launcher.Tab != null)
        {
            launcher.Switch.ApplyTab(launcher.Tab, open);
            launcher.Switch.OtherTabChosen = open;
            return;
        }
        if (launcher.State != null)
            launcher.State.Selected = open;
        Color color = UIControlColors.For(open);
        if (launcher.Icon != null)
            launcher.Icon.color = color;
        if (launcher.Label != null)
            launcher.Label.color = color;
    }

    /// <summary>A labelled button is as wide as its icon and label need, with even room either side.</summary>
    private static void FitLauncher(Launcher launcher)
    {
        TextMeshProUGUI label = launcher.Label;
        if (label == null || launcher.Root == null || label.text == launcher.FittedText)
            return;
        launcher.FittedText = label.text;
        float text = Mathf.Ceil(label.GetPreferredValues(label.text).x);
        float width = launcher.Inset * 2f + LauncherPadding + LauncherIconSize + LauncherIconGap + text + LauncherPadding;
        launcher.Root.sizeDelta = new Vector2(width, launcher.Root.sizeDelta.y);
    }

    // The label's copy can arrive after the button is built (the language table loads late).
    private void FitLaunchers()
    {
        foreach (Launcher launcher in launchers)
            FitLauncher(launcher);
    }

    // -----------------------------------------------------------------------
    // The window
    // -----------------------------------------------------------------------

    private void Build()
    {
        window = UIBuilder.Rect("Ask Window", canvasRect, gameObject.layer);
        window.anchorMin = window.anchorMax = Vector2.zero;
        window.pivot = Vector2.zero;
        // Above everything in the layouts, the cards' history panels (10) among
        // them, and below the settings (300): the viewer can park it anywhere.
        Canvas layer = window.gameObject.AddComponent<Canvas>();
        layer.overrideSorting = true;
        layer.sortingOrder = SortingOrder;
        window.gameObject.AddComponent<GraphicRaycaster>();
        frame = UIBuilder.Shape(window, "Detail Panel", true);

        // As a tab, an opaque fill takes the panel's place: given the Service
        // backdrop's look when the tab first shows.
        RectTransform fill = UIBuilder.Rect("Tab Fill", window, gameObject.layer);
        UIBuilder.Stretch(fill, 0f);
        tabFill = fill.gameObject.AddComponent<UIShape>();
        tabFill.enabled = false;
        // Translucent over the scene: every layout's leader lines fade under it.
        window.gameObject.AddComponent<LeaderLineObstacle>().EveryLayout = true;

        BuildHeader();
        BuildThread();
        BuildComposer();
        window.gameObject.SetActive(false);
        LoadPosition();
    }

    private void BuildHeader()
    {
        header = UIBuilder.Rect("Header", window, gameObject.layer);
        header.anchorMin = new Vector2(0f, 1f);
        header.anchorMax = Vector2.one;
        header.pivot = new Vector2(0.5f, 1f);
        header.offsetMin = new Vector2(0f, -HeaderHeight);
        header.offsetMax = Vector2.zero;
        // A clear surface the pointer can take hold of.
        Image hold = header.gameObject.AddComponent<Image>();
        hold.color = Color.clear;
        header.gameObject.AddComponent<DragHandle>().Owner = this;

        // The grip: two short rules at the top edge, as a sheet's handle, lit
        // while the pointer is on the header or drags it.
        RectTransform handle = UIBuilder.Rect("Grip", header, gameObject.layer);
        handle.anchorMin = handle.anchorMax = handle.pivot = new Vector2(0.5f, 1f);
        handle.anchoredPosition = new Vector2(0f, -9f);
        handle.sizeDelta = new Vector2(GripWidth, 6f);
        grip = new Graphic[2];
        for (int i = 0; i < grip.Length; i++)
        {
            RectTransform stroke = UIBuilder.Rect("Rule", handle, gameObject.layer);
            stroke.anchorMin = new Vector2(0f, 1f);
            stroke.anchorMax = Vector2.one;
            stroke.pivot = new Vector2(0.5f, 1f);
            stroke.offsetMin = new Vector2(0f, -2f - i * 4f);
            stroke.offsetMax = new Vector2(0f, -i * 4f);
            Image fill = stroke.gameObject.AddComponent<Image>();
            fill.raycastTarget = false;
            grip[i] = fill;
        }
        ShowGrip();

        TextMeshProUGUI title = UIBuilder.Text("Title", header, "Label", TextAlignmentOptions.MidlineLeft);
        title.rectTransform.anchorMin = Vector2.zero;
        title.rectTransform.anchorMax = Vector2.one;
        // A little below the middle, clear of the grip.
        title.rectTransform.offsetMin = new Vector2(Padding + 2f, -4f);
        title.rectTransform.offsetMax = new Vector2(-64f, -4f);
        UITextBinding.Bind(title, "ask.title", "TWIN ASSISTANT");

        RectTransform close = UIBuilder.Rect("Close", header, gameObject.layer);
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 0.5f);
        close.anchoredPosition = new Vector2(-12f, -4f);
        close.sizeDelta = new Vector2(44f, 44f);
        IconButton(close, closeIcon, () => SetOpen(false));

        RectTransform line = UIBuilder.Rect("Divider", window, gameObject.layer);
        headerRule = line;
        line.anchorMin = new Vector2(0f, 1f);
        line.anchorMax = Vector2.one;
        line.pivot = new Vector2(0.5f, 1f);
        line.offsetMin = new Vector2(0f, -HeaderHeight - 1f);
        line.offsetMax = new Vector2(0f, -HeaderHeight);
        Image rule = line.gameObject.AddComponent<Image>();
        rule.color = UIBuilder.Token("Accent", 0.16f, new Color(0.21f, 0.93f, 1f, 0.16f));
        rule.raycastTarget = false;
    }

    private void BuildThread()
    {
        thread = UIBuilder.Rect("Conversation", window, gameObject.layer);
        thread.anchorMin = Vector2.zero;
        thread.anchorMax = Vector2.one;
        thread.offsetMin = new Vector2(0f, ComposerHeight);
        thread.offsetMax = new Vector2(0f, -HeaderHeight - 1f);

        RectTransform viewport = UIBuilder.Rect("Viewport", thread, gameObject.layer);
        UIBuilder.Stretch(viewport, 0f);
        viewport.gameObject.AddComponent<RectMask2D>();
        // The empty space between messages still scrolls.
        Image catcher = viewport.gameObject.AddComponent<Image>();
        catcher.color = Color.clear;

        content = UIBuilder.Rect("Messages", viewport, gameObject.layer);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = Vector2.one;
        content.pivot = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        VerticalLayoutGroup stack = content.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.padding = new RectOffset((int)Padding, (int)Padding, 18, 18);
        stack.spacing = 12f;
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        scroll = thread.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.viewport = viewport;
        scroll.content = content;
        thread.gameObject.AddComponent<UIWheelScroll>();
    }

    private void BuildComposer()
    {
        composer = UIBuilder.Rect("Composer", window, gameObject.layer);
        composer.anchorMin = Vector2.zero;
        composer.anchorMax = new Vector2(1f, 0f);
        composer.pivot = new Vector2(0.5f, 0f);
        composer.offsetMin = Vector2.zero;
        composer.offsetMax = new Vector2(0f, ComposerHeight);

        RectTransform line = UIBuilder.Rect("Divider", composer, gameObject.layer);
        line.anchorMin = new Vector2(0f, 1f);
        line.anchorMax = Vector2.one;
        line.offsetMin = new Vector2(0f, -1f);
        line.offsetMax = Vector2.zero;
        Image rule = line.gameObject.AddComponent<Image>();
        rule.color = UIBuilder.Token("Accent", 0.16f, new Color(0.21f, 0.93f, 1f, 0.16f));
        rule.raycastTarget = false;

        const float Control = FieldHeight;
        RectTransform well = UIBuilder.Rect("Field", composer, gameObject.layer);
        well.anchorMin = new Vector2(0f, 0.5f);
        well.anchorMax = new Vector2(1f, 0.5f);
        well.pivot = new Vector2(0f, 0.5f);
        well.offsetMin = new Vector2(Padding, -Control * 0.5f);
        well.offsetMax = new Vector2(-(Padding + Control + 10f), Control * 0.5f);
        UIBuilder.Shape(well, "Button", true);
        well.gameObject.AddComponent<UIControlState>();

        RectTransform area = UIBuilder.Rect("Text Area", well, gameObject.layer);
        area.anchorMin = Vector2.zero;
        area.anchorMax = Vector2.one;
        area.offsetMin = new Vector2(14f, 4f);
        area.offsetMax = new Vector2(-14f, -4f);
        area.gameObject.AddComponent<RectMask2D>();

        TextMeshProUGUI placeholder = UIBuilder.Text("Placeholder", area, "Body", TextAlignmentOptions.MidlineLeft);
        UIBuilder.Stretch(placeholder.rectTransform, 0f);
        placeholder.alpha = 0.45f;
        UITextBinding.Bind(placeholder, "ask.placeholder", "Ask about this machine");
        TextMeshProUGUI typed = UIBuilder.Text("Text", area, "Body", TextAlignmentOptions.MidlineLeft);
        UIBuilder.Stretch(typed.rectTransform, 0f);

        field = well.gameObject.AddComponent<TMP_InputField>();
        field.transition = Selectable.Transition.None;
        field.textViewport = area;
        field.textComponent = typed;
        field.placeholder = placeholder;
        field.fontAsset = typed.font;
        field.pointSize = typed.fontSize;
        field.lineType = TMP_InputField.LineType.SingleLine;
        field.characterLimit = 240;
        field.caretColor = typed.color;
        field.customCaretColor = true;
        field.selectionColor = UIBuilder.Token("Accent", 0.3f, new Color(0.21f, 0.93f, 1f, 0.3f));
        field.onSubmit.AddListener(SendFromField);

        RectTransform send = UIBuilder.Rect("Send", composer, gameObject.layer);
        send.anchorMin = send.anchorMax = send.pivot = new Vector2(1f, 0.5f);
        send.anchoredPosition = new Vector2(-Padding, 0f);
        send.sizeDelta = new Vector2(Control, Control);
        UIBuilder.Shape(send, "Button", true);
        sendButton = send.gameObject.AddComponent<Button>();
        sendButton.transition = Selectable.Transition.None;
        sendButton.onClick.AddListener(() => Send(field.text));
        send.gameObject.AddComponent<UIControlState>();
        RectTransform glyph = UIBuilder.Rect("Icon", send, gameObject.layer);
        glyph.anchorMin = glyph.anchorMax = new Vector2(0.5f, 0.5f);
        glyph.sizeDelta = new Vector2(24f, 24f);
        sendGlyph = UIBuilder.Icon(glyph, sendIcon);
    }

    private Button IconButton(RectTransform rect, Sprite sprite, System.Action onClick)
    {
        UIBuilder.Shape(rect, "Icon Button", true);
        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => onClick());
        rect.gameObject.AddComponent<UIControlState>();
        RectTransform glyph = UIBuilder.Rect("Icon", rect, gameObject.layer);
        glyph.anchorMin = glyph.anchorMax = new Vector2(0.5f, 0.5f);
        glyph.sizeDelta = new Vector2(22f, 22f);
        UIBuilder.Icon(glyph, sprite);
        return button;
    }

    // -----------------------------------------------------------------------
    // Messages
    // -----------------------------------------------------------------------

    private float ThreadWidth => window.rect.width - Padding * 2f;

    private void AddSent(string text)
    {
        RectTransform row = Row("Sent", TextAnchor.UpperRight, new RectOffset(0, (int)TailRoom, 0, 0));
        // What the viewer typed is shown as typed: tags in it are not read as tags.
        AddBubble(row, "Message Sent", TwinAssistant.EscapeRichText(text));
        messageCount++;
        Appear(row);
    }

    private void AddReply(TwinAssistantReply reply)
    {
        evidenceButtons.Clear();
        RectTransform group = UIBuilder.Rect("Reply", content, gameObject.layer);
        VerticalLayoutGroup stack = group.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 16f;
        stack.padding = new RectOffset(0, 0, 2, 4);
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        // Plain text across the window: only the viewer's own messages are bubbles.
        TextMeshProUGUI text = UIBuilder.Text("Text", group, "Body", TextAlignmentOptions.TopLeft);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.richText = true;
        text.lineSpacing = 6f;
        text.text = reply.Text;

        AddReadings(group, reply.SensorIds);
        AddServiceRows(group, reply.PartKeys);
        if (!string.IsNullOrWhiteSpace(reply.Check))
            AddCheck(group, reply.Check);
        if (reply.FollowUps.Count > 0)
            AddSuggestions(group, reply.FollowUps);

        messageCount++;
        Appear(group);
    }

    /// <summary>A full-width row in the conversation that places its bubble left or right.</summary>
    private RectTransform Row(string name, TextAnchor alignment, RectOffset padding, RectTransform parent = null)
    {
        RectTransform row = UIBuilder.Rect(name, parent != null ? parent : content, gameObject.layer);
        HorizontalLayoutGroup line = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        line.childAlignment = alignment;
        line.padding = padding;
        line.childControlWidth = line.childControlHeight = true;
        line.childForceExpandWidth = line.childForceExpandHeight = false;
        return row;
    }

    private Bubble AddBubble(RectTransform row, string component, string text)
    {
        RectTransform rect = UIBuilder.Rect("Bubble", row, gameObject.layer);
        UIBuilder.Shape(rect, component, false);
        TextMeshProUGUI label = UIBuilder.Text("Text", rect, "Body", TextAlignmentOptions.TopLeft);
        label.textWrappingMode = TextWrappingModes.Normal;
        label.richText = true;
        label.text = text;
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(BubblePaddingX, BubblePaddingY);
        label.rectTransform.offsetMax = new Vector2(-BubblePaddingX, -BubblePaddingY);
        Bubble bubble = new() { Rect = rect, Text = label, Size = rect.gameObject.AddComponent<LayoutElement>() };
        bubbles.Add(bubble);
        FitBubble(bubble);
        return bubble;
    }

    /// <summary>A bubble as wide as its text needs, up to most of the conversation's width.</summary>
    private void FitBubble(Bubble bubble)
    {
        float most = Mathf.Max(120f, ThreadWidth * BubbleShare - TailRoom) - BubblePaddingX * 2f;
        Vector2 single = bubble.Text.GetPreferredValues(bubble.Text.text);
        float width = Mathf.Min(single.x + 1f, most);
        float height = bubble.Text.GetPreferredValues(bubble.Text.text, width, 0f).y;
        bubble.Size.preferredWidth = width + BubblePaddingX * 2f;
        bubble.Size.preferredHeight = height + BubblePaddingY * 2f;
    }

    /// <summary>Three dots where the reply will be, in the reply's place and manner.</summary>
    private void ShowTyping(bool show)
    {
        if (show)
        {
            typingRow = UIBuilder.Rect("Typing", content, gameObject.layer);
            typingRow.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
            typingText = UIBuilder.Text("Dots", typingRow, "Body", TextAlignmentOptions.MidlineLeft);
            UIBuilder.Stretch(typingText.rectTransform, 0f);
            typingText.color = UIBuilder.Token("Accent", 1f, new Color(0.21f, 0.93f, 1f));
            typingText.text = "•";
            Appear(typingRow);
        }
        else if (typingRow != null)
        {
            Destroy(typingRow.gameObject);
            typingRow = null;
            typingText = null;
        }
        scrollPending = true;
    }

    /// <summary>A block under a reply: a heading (TELEMETRY DATA) with a rule to the edge, then its rows.</summary>
    private RectTransform Section(RectTransform group, string name, string key, string fallback)
    {
        RectTransform section = UIBuilder.Rect(name, group, gameObject.layer);
        VerticalLayoutGroup stack = section.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 6f;
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        RectTransform heading = UIBuilder.Rect("Heading", section, gameObject.layer);
        heading.gameObject.AddComponent<LayoutElement>().preferredHeight = 22f;
        TextMeshProUGUI title = UIBuilder.Text("Title", heading, "Caption", TextAlignmentOptions.MidlineLeft);
        title.fontSize = 15f;
        title.text = UIText.Get(key, fallback);
        float width = Mathf.Ceil(title.GetPreferredValues(title.text).x);
        title.rectTransform.anchorMin = new Vector2(0f, 0f);
        title.rectTransform.anchorMax = new Vector2(0f, 1f);
        title.rectTransform.pivot = new Vector2(0f, 0.5f);
        title.rectTransform.sizeDelta = new Vector2(width, 0f);
        title.rectTransform.anchoredPosition = new Vector2(2f, 0f);

        RectTransform line = UIBuilder.Rect("Rule", heading, gameObject.layer);
        line.anchorMin = new Vector2(0f, 0.5f);
        line.anchorMax = new Vector2(1f, 0.5f);
        line.offsetMin = new Vector2(width + 14f, 0f);
        line.offsetMax = new Vector2(0f, 1f);
        Image rule = line.gameObject.AddComponent<Image>();
        rule.color = UIBuilder.Token("Accent", 0.16f, new Color(0.21f, 0.93f, 1f, 0.16f));
        rule.raycastTarget = false;
        return section;
    }

    /// <summary>
    /// The readings an answer rests on, as rows: the reading, its last hour,
    /// its normal band and its trend. Pressing one shows it on the model.
    /// </summary>
    private void AddReadings(RectTransform group, List<string> ids)
    {
        RectTransform section = null;
        foreach (string id in ids)
        {
            if (!assistant.TryGetSource(id, out PerformanceStatSource source) ||
                !assistant.TryGetFact(id, out TwinSensorFact fact))
                continue;
            if (section == null)
                section = Section(group, "Telemetry", "ask.section.telemetry", "TELEMETRY DATA");
            AddReading(section, source, fact);
        }
    }

    private void AddReading(RectTransform section, PerformanceStatSource source, in TwinSensorFact fact)
    {
        RectTransform row = UIBuilder.Rect(source.StatId, section, gameObject.layer);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = ReadingHeight;
        Button press = PressableRow(row);
        PerformanceStatSource shown = source;
        press.onClick.AddListener(() =>
        {
            LeaveTab();
            ShowOnModel(shown);
        });

        // Left: what it is, then the figure, the one thing in colour when it is out of band.
        TextMeshProUGUI name = UIBuilder.Text("Name", row, "Caption", TextAlignmentOptions.TopLeft);
        name.fontSize = 15f;
        name.text = source.MetricName;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.rectTransform.anchorMin = new Vector2(0f, 1f);
        name.rectTransform.anchorMax = new Vector2(0.44f, 1f);
        name.rectTransform.pivot = new Vector2(0f, 1f);
        name.rectTransform.offsetMin = new Vector2(RowPadding, -34f);
        name.rectTransform.offsetMax = new Vector2(0f, -13f);

        TextMeshProUGUI value = UIBuilder.Text("Value", row, "Value", TextAlignmentOptions.BottomLeft);
        value.fontSize = 28f;
        value.text = Reading(source);
        value.overflowMode = TextOverflowModes.Ellipsis;
        if (source.VisualState != StatVisualState.Normal)
            value.color = ToneColor(source.VisualState);
        value.rectTransform.anchorMin = Vector2.zero;
        value.rectTransform.anchorMax = new Vector2(0.44f, 0f);
        value.rectTransform.pivot = new Vector2(0f, 0f);
        value.rectTransform.offsetMin = new Vector2(RowPadding, 9f);
        value.rectTransform.offsetMax = new Vector2(0f, 45f);

        // Right: the last hour as a line, and in words where it should be and where it is going.
        AddSparkline(row, source);

        List<string> notes = new(2);
        if (TwinAssistantAnswers.TryNormalBand(fact, out string band))
            notes.Add(band);
        if (TwinAssistantAnswers.TryShortTrend(fact, out string trend))
            notes.Add(trend);
        TextMeshProUGUI note = UIBuilder.Text("Note", row, "Caption", TextAlignmentOptions.BottomRight);
        note.text = string.Join("  ·  ", notes);
        note.fontSize = 14f;
        note.characterSpacing = 2f;
        note.enableAutoSizing = true;
        note.fontSizeMin = 11f;
        note.fontSizeMax = 14f;
        note.overflowMode = TextOverflowModes.Ellipsis;
        note.rectTransform.anchorMin = new Vector2(0.44f, 0f);
        note.rectTransform.anchorMax = new Vector2(1f, 0f);
        note.rectTransform.pivot = new Vector2(1f, 0f);
        note.rectTransform.offsetMin = new Vector2(12f, 11f);
        note.rectTransform.offsetMax = new Vector2(-(RowPadding + ChevronSpace), 29f);

        AddChevron(row);
        evidenceButtons.Add(press);
    }

    /// <summary>
    /// The reading's last hour, a line with its latest point marked, and the
    /// edges of its normal band as faint rules when they are near the line.
    /// Left out when there is no history yet.
    /// </summary>
    private void AddSparkline(RectTransform row, PerformanceStatSource source)
    {
        history ??= FindAnyObjectByType<TelemetryHistoryRecorder>();
        long now = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (history == null || !history.IsReady || !history.TryGetHistory(source, TelemetryHistoryRange.Hour, now, buckets))
            return;

        sparkPoints.Clear();
        float low = float.MaxValue;
        float high = float.MinValue;
        for (int i = 0; i < buckets.Count; i++)
        {
            float x = (i + 0.5f) / buckets.Count;
            if (!buckets[i].HasValue)
            {
                sparkPoints.Add(new Vector2(x, float.NaN));
                continue;
            }
            float mean = buckets[i].Mean;
            sparkPoints.Add(new Vector2(x, mean));
            low = Mathf.Min(low, mean);
            high = Mathf.Max(high, mean);
        }
        if (low > high)
            return;

        sparkLimits.Clear();
        sparkLimitColors.Clear();
        float span = Mathf.Max(high - low, Mathf.Abs(high) * 0.1f, 1e-3f);
        TelemetryLimits limits = source.Limits;
        Color edge = UIBuilder.Token("Text", 0.22f, new Color(0.92f, 0.99f, 1f, 0.22f));
        if (limits.HasHigh)
            AddSparkLimit(limits.WarningAbove, edge, low, high, span);
        if (limits.HasLow)
            AddSparkLimit(limits.WarningBelow, edge, low, high, span);
        foreach (float limit in sparkLimits)
        {
            low = Mathf.Min(low, limit);
            high = Mathf.Max(high, limit);
        }
        float pad = Mathf.Max(high - low, span) * 0.12f;

        RectTransform rect = UIBuilder.Rect("Last Hour", row, gameObject.layer);
        rect.anchorMin = new Vector2(0.44f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(12f, -40f);
        rect.offsetMax = new Vector2(-(RowPadding + ChevronSpace), -12f);
        UILineChartGraphic line = rect.gameObject.AddComponent<UILineChartGraphic>();
        line.raycastTarget = false;
        line.lineColor = UIBuilder.Token("Accent", 0.85f, new Color(0.21f, 0.93f, 1f, 0.85f));
        line.fillArea = false;
        line.gridColor = line.guideColor = line.nowLineColor = Color.clear;
        line.markerRingColor = UIBuilder.Token("Surface Inset", 1f, new Color(0.008f, 0.027f, 0.04f));
        line.lineWidth = 1.6f;
        line.markerRadius = 3.5f;
        line.ringWidth = 1.5f;
        line.bandStrength = 0f;
        line.SetData(sparkPoints, low - pad, high + pad, null, null, sparkLimits, sparkLimitColors);
    }

    private void AddSparkLimit(float value, Color color, float low, float high, float span)
    {
        // Limits far outside the hour would only squash the line flat.
        if (value < low - span * 0.6f || value > high + span * 0.6f)
            return;
        sparkLimits.Add(value);
        sparkLimitColors.Add(color);
    }

    /// <summary>The services an answer rests on, as list rows: pressing one opens it in the Service view.</summary>
    private void AddServiceRows(RectTransform group, List<string> partKeys)
    {
        RectTransform section = null;
        foreach (string key in partKeys)
        {
            if (!assistant.TryGetService(key, out ServiceItemStatus status))
                continue;
            if (section == null)
                section = Section(group, "Service", "ask.section.service", "SERVICE DATA");
            RectTransform row = UIBuilder.Rect(key, section, gameObject.layer);
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
            Button press = PressableRow(row);
            MachinePartGroup part = status.Part;
            press.onClick.AddListener(() =>
            {
                LeaveTab();
                ServicePartLink.Request(part);
            });
            evidenceButtons.Add(press);

            TextMeshProUGUI name = UIBuilder.Text("Part", row, "Title", TextAlignmentOptions.TopLeft);
            name.fontSize *= 0.8f;
            name.text = ServicePanelView.PartName(part);
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            name.rectTransform.anchorMax = new Vector2(1f, 1f);
            name.rectTransform.offsetMin = new Vector2(RowPadding, -2f);
            name.rectTransform.offsetMax = new Vector2(-(RowPadding + ChevronSpace + 100f), -9f);

            TextMeshProUGUI task = UIBuilder.Text("Task", row, "Caption", TextAlignmentOptions.TopLeft);
            task.fontSize = 15f;
            task.text = ServicePanelView.TaskLabel(status.Task);
            task.rectTransform.anchorMin = Vector2.zero;
            task.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            task.rectTransform.offsetMin = new Vector2(RowPadding, 6f);
            task.rectTransform.offsetMax = new Vector2(-(RowPadding + ChevronSpace + 100f), -2f);

            // The hours are the figure; only one that is due soon or overdue takes a colour.
            TextMeshProUGUI hours = UIBuilder.Text("Hours", row, "Value", TextAlignmentOptions.MidlineRight);
            hours.fontSize *= 0.8f;
            hours.richText = true;
            if (status.State != ServiceState.OnTrack)
                hours.color = ToneColor(status.State == ServiceState.Overdue ? StatVisualState.Critical : StatVisualState.Warning);
            double left = status.HoursLeft;
            string number = System.Math.Ceiling(System.Math.Abs(left)).ToString("#,0", CultureInfo.InvariantCulture);
            hours.text = (left < 0d ? "+" : string.Empty) + number + "<size=60%> " + UIText.Get("unit.hours", "H") + "</size>";
            hours.rectTransform.anchorMin = new Vector2(1f, 0f);
            hours.rectTransform.anchorMax = Vector2.one;
            hours.rectTransform.pivot = new Vector2(1f, 0.5f);
            hours.rectTransform.offsetMin = new Vector2(-(RowPadding + ChevronSpace + 96f), 0f);
            hours.rectTransform.offsetMax = new Vector2(-(RowPadding + ChevronSpace), 0f);

            AddChevron(row);
        }
    }

    /// <summary>
    /// A row in the neutral List Row look, whatever the state of what it shows:
    /// the figure on it carries the state, so a reply is not a wall of alarm frames.
    /// </summary>
    private static Button PressableRow(RectTransform row)
    {
        UIBuilder.Shape(row, "List Row", true);
        row.gameObject.AddComponent<UIControlState>();
        Button press = row.gameObject.AddComponent<Button>();
        press.transition = Selectable.Transition.None;
        return press;
    }

    /// <summary>The chevron the service list's rows end in: this opens or shows more.</summary>
    private void AddChevron(RectTransform row)
    {
        RectTransform rect = UIBuilder.Rect("Chevron", row, gameObject.layer);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0.5f);
        rect.anchoredPosition = new Vector2(-(RowPadding - 4f), 0f);
        rect.sizeDelta = new Vector2(22f, 22f);
        UIIconGraphic chevron = rect.gameObject.AddComponent<UIIconGraphic>();
        chevron.IconShape = UIIconGraphic.Shape.Chevron;
        chevron.color = UIBuilder.Token("Text", 0.62f, new Color(0.92f, 0.99f, 1f, 0.62f));
        chevron.raycastTarget = false;
    }

    private void AddCheck(RectTransform group, string check)
    {
        RectTransform well = UIBuilder.Rect("Check", group, gameObject.layer);
        UIBuilder.Shape(well, "Inset", false);
        VerticalLayoutGroup stack = well.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.padding = new RectOffset(18, 18, 12, 14);
        stack.spacing = 4f;
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;

        TextMeshProUGUI title = UIBuilder.Text("Title", well, "Caption", TextAlignmentOptions.TopLeft);
        title.fontSize = 15f;
        title.text = UIText.Get("ask.check", "SUGGESTED CHECK");
        TextMeshProUGUI body = UIBuilder.Text("Text", well, "Body", TextAlignmentOptions.TopLeft);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.text = check;
    }

    /// <summary>Questions worth asking next, under the latest reply: pressing one sends it.</summary>
    private void AddSuggestions(RectTransform group, List<TwinFollowUp> questions)
    {
        suggestionStack = UIBuilder.Rect("Suggestions", group, gameObject.layer);
        VerticalLayoutGroup stack = suggestionStack.gameObject.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 8f;
        stack.padding = new RectOffset(0, 0, 4, 0);
        stack.childAlignment = TextAnchor.UpperRight;
        stack.childControlWidth = stack.childControlHeight = true;
        stack.childForceExpandWidth = stack.childForceExpandHeight = false;

        foreach (TwinFollowUp question in questions)
        {
            RectTransform rect = UIBuilder.Rect(question.Text, suggestionStack, gameObject.layer);
            UIBuilder.Shape(rect, "Button", true);
            rect.gameObject.AddComponent<UIControlState>();
            Button button = rect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            TwinFollowUp asked = question;
            button.onClick.AddListener(() => Send(asked.Text, asked.Question));
            suggestionButtons.Add(button);

            TextMeshProUGUI label = UIBuilder.Text("Label", rect, "Body", TextAlignmentOptions.MidlineRight);
            // A button's label: the control colour at rest, as every other button's.
            label.color = UIControlColors.Idle;
            label.text = TwinAssistant.EscapeRichText(question.Text);
            label.overflowMode = TextOverflowModes.Ellipsis;
            UIBuilder.Stretch(label.rectTransform, 0f);
            label.rectTransform.offsetMin = new Vector2(16f, 0f);
            label.rectTransform.offsetMax = new Vector2(-16f, 0f);
            float width = Mathf.Min(label.GetPreferredValues(label.text).x + 34f, ThreadWidth);
            LayoutElement size = rect.gameObject.AddComponent<LayoutElement>();
            size.preferredWidth = width;
            size.preferredHeight = 44f;
        }
    }

    private void ClearSuggestions()
    {
        suggestionButtons.Clear();
        if (suggestionStack != null)
            Destroy(suggestionStack.gameObject);
        suggestionStack = null;
    }

    private void Appear(RectTransform rect)
    {
        scrollPending = true;
        if (DigitalTwinSettings.ReduceMotion)
            return;
        CanvasGroup fade = rect.gameObject.AddComponent<CanvasGroup>();
        fade.alpha = 0f;
        appearing.Add(fade);
    }

    private void Animate()
    {
        float step = Time.unscaledDeltaTime / AppearSeconds;
        for (int i = appearing.Count - 1; i >= 0; i--)
        {
            CanvasGroup fade = appearing[i];
            if (fade == null)
            {
                appearing.RemoveAt(i);
                continue;
            }
            fade.alpha = Mathf.Min(1f, fade.alpha + step);
            if (fade.alpha >= 1f)
                appearing.RemoveAt(i);
        }

        if (typingText != null)
        {
            int dots = DigitalTwinSettings.ReduceMotion ? 3 : 1 + Mathf.FloorToInt(Time.unscaledTime * 3f) % 3;
            string shown = dots switch { 1 => "•", 2 => "•  •", _ => "•  •  •" };
            if (typingText.text != shown)
                typingText.text = shown;
        }
    }

    // -----------------------------------------------------------------------
    // Showing what an answer refers to
    // -----------------------------------------------------------------------

    /// <summary>
    /// Focuses the reading's card in the layout on show, switching to Telemetry
    /// first, and inside the machine (Cross Section) when the view on show is
    /// its outside, where the cards are hidden: the card lights, its line draws
    /// and the model shows the part. Only focuses, as a tap does, so its history
    /// does not open over the conversation.
    /// </summary>
    private void ShowOnModel(PerformanceStatSource source) => StartCoroutine(ShowOnModelRoutine(source));

    private IEnumerator ShowOnModelRoutine(PerformanceStatSource source)
    {
        DigitalTwinModeSwitch modeSwitch = ActiveSwitch();
        if (modeSwitch != null)
            modeSwitch.SelectTelemetry();

        MachineViewModeController views = FindAnyObjectByType<MachineViewModeController>();
        if (views != null)
        {
            // A view change already under way finishes first; a new one is refused until then.
            float giveUp = Time.unscaledTime + 4f;
            while (views.IsTransitioning && Time.unscaledTime < giveUp)
                yield return null;
            if (!views.ShowsTelemetry)
                views.SelectCrossSection();
        }

        foreach (PerformanceStatCardView card in FindObjectsByType<PerformanceStatCardView>())
        {
            if (card.isActiveAndEnabled && card.BoundSource == source)
            {
                if (!card.IsFocused)
                    CardFocus.Press(card, CardPressKind.Touch);
                yield break;
            }
        }
    }

    private static string Reading(PerformanceStatSource source)
    {
        string unit = source.Unit ?? string.Empty;
        if (DigitalTwinSettings.Units == DigitalTwinSettings.UnitStyle.Capitals)
            unit = unit.ToUpperInvariant();
        return string.IsNullOrEmpty(unit) ? source.FormattedValue : source.FormattedValue + " " + unit;
    }

    private static Color ToneColor(StatVisualState state) => state switch
    {
        StatVisualState.Critical => UIBuilder.Token("Critical", 1f, new Color(1f, 0.22f, 0.2f)),
        StatVisualState.Warning => UIBuilder.Token("Warning", 1f, new Color(1f, 0.7f, 0.1f)),
        StatVisualState.Unavailable => UIBuilder.Token("Neutral", 1f, new Color(0.45f, 0.5f, 0.55f)),
        _ => UIBuilder.Token("Label", 1f, new Color(0f, 0.92f, 1f))
    };

    // -----------------------------------------------------------------------
    // Where the window goes
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sizes and places the window: on a portrait screen the Ask tab, below the
    /// switch's bar; else a window inside the area between the top and bottom
    /// controls, where it was last left.
    /// </summary>
    private void Layout(bool force)
    {
        if (canvasRect == null)
            return;
        Vector2 size = canvasRect.rect.size;
        bool portrait = size.y > size.x;
        Rect free = portrait ? TabArea(size) : FreeArea(size);
        if (portrait)
            PlaceTabBackdrop();
        if (!force && size == laidOutFor && free == area && portrait == tabbed)
            return;
        bool resized = size != laidOutFor || portrait != tabbed;
        laidOutFor = size;
        area = free;
        tabbed = portrait;
        ShowChrome();

        if (tabbed)
        {
            // The portrait canvas is narrower in units; the tab is laid out at
            // a window's width and scaled up to fill the screen's.
            float scale = size.x / tabContentWidth;
            window.localScale = new Vector3(scale, scale, 1f);
            window.sizeDelta = new Vector2(tabContentWidth, area.yMax / scale);
            window.anchoredPosition = Vector2.zero;
            tabBottom = area.yMin / scale;
        }
        else
        {
            window.localScale = Vector3.one;
            float width = Mathf.Min(windowWidth, area.width);
            float height = Mathf.Min(windowHeight, area.height);
            window.sizeDelta = new Vector2(width, height);
            window.anchoredPosition = Corner();
            tabBottom = 0f;
        }
        composer.offsetMin = new Vector2(0f, tabBottom);
        composer.offsetMax = new Vector2(0f, tabBottom + ComposerHeight);
        thread.offsetMin = new Vector2(0f, tabBottom + ComposerHeight);
        thread.offsetMax = new Vector2(0f, tabbed ? 0f : -HeaderHeight - 1f);

        if (resized || force)
        {
            foreach (Bubble bubble in bubbles)
                FitBubble(bubble);
            scrollPending = true;
        }
    }

    /// <summary>
    /// As a window, the panel with its header; as a tab, an opaque fill in the
    /// Service backdrop's look and no header: the switch above is its header.
    /// </summary>
    private void ShowChrome()
    {
        header.gameObject.SetActive(!tabbed);
        // The rule under the header; as a tab, along its top, under the switch.
        float ruleTop = tabbed ? 0f : -HeaderHeight;
        headerRule.offsetMin = new Vector2(0f, ruleTop - 1f);
        headerRule.offsetMax = new Vector2(0f, ruleTop);
        frame.enabled = !tabbed;
        tabFill.enabled = tabbed;
        if (tabbed && TabBackdropSource() is Image source)
            CopyLook(source, tabFill);
        if (tabBackdrop != null)
            tabBackdrop.gameObject.SetActive(tabbed && IsOpen);
        ShowGrip();
    }

    /// <summary>
    /// The Ask tab's room: from its rule, as far below the switch as the
    /// question field is below the rule above it, down to the bottom of the
    /// safe area, the screen's full width.
    /// </summary>
    private Rect TabArea(Vector2 size)
    {
        float top = size.y;
        float bottom = 0f;
        DigitalTwinModeSwitch modeSwitch = ActiveSwitch();
        if (modeSwitch != null && TryGetCanvasBounds((RectTransform)modeSwitch.transform, out Rect switchBounds))
            top = switchBounds.yMin - FieldGap * size.x / tabContentWidth;
        SafeAreaFitter safe = modeSwitch != null ? modeSwitch.GetComponentInParent<SafeAreaFitter>() : null;
        if (safe != null && TryGetCanvasBounds((RectTransform)safe.transform, out Rect safeBounds))
            bottom = Mathf.Max(0f, safeBounds.yMin);
        return Rect.MinMaxRect(0f, bottom, size.x, Mathf.Max(bottom + 1f, top));
    }

    /// <summary>The portrait Service tab's backdrop, whose look the Ask tab borrows.</summary>
    private Image TabBackdropSource()
    {
        portraitViews ??= FindObjectsByType<PortraitServiceView>(FindObjectsInactive.Include);
        foreach (PortraitServiceView view in portraitViews)
        {
            if (view != null && view.isActiveAndEnabled && view.Backdrop != null && view.Backdrop.TryGetComponent(out Image image))
                return image;
        }
        return null;
    }

    /// <summary>
    /// The Ask tab's fill behind the switch's bar too, to the screen's edges, as
    /// the Service tab's: laid just above that backdrop, under the bar.
    /// </summary>
    private void PlaceTabBackdrop()
    {
        Image source = TabBackdropSource();
        if (source == null || !(source.transform.parent is RectTransform parent))
            return;
        if (tabBackdrop == null || tabBackdrop.parent != parent)
        {
            if (tabBackdrop != null)
                Destroy(tabBackdrop.gameObject);
            tabBackdrop = UIBuilder.Rect("Ask Backdrop", parent, gameObject.layer);
            tabBackdrop.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            CopyLook(source, tabBackdrop.gameObject.AddComponent<UIShape>());
            tabBackdrop.gameObject.SetActive(IsOpen);
        }
        // Over the whole screen, past the safe area's edges.
        ((RectTransform)hostCanvas.transform).GetWorldCorners(corners);
        Vector3 low = parent.InverseTransformPoint(corners[0]);
        Vector3 high = parent.InverseTransformPoint(corners[2]);
        Rect rect = parent.rect;
        tabBackdrop.anchorMin = Vector2.zero;
        tabBackdrop.anchorMax = Vector2.one;
        Vector2 min = new(low.x - rect.xMin, low.y - rect.yMin);
        Vector2 max = new(high.x - rect.xMax, high.y - rect.yMax);
        if (tabBackdrop.offsetMin != min)
            tabBackdrop.offsetMin = min;
        if (tabBackdrop.offsetMax != max)
            tabBackdrop.offsetMax = max;
    }

    private static void CopyLook(Image from, Image to)
    {
        UIShape.CopyLook(from, to);
        to.raycastTarget = true;
    }

    /// <summary>The space between the top and bottom controls of the layout on show, in canvas units from its bottom left.</summary>
    private Rect FreeArea(Vector2 size)
    {
        float top = size.y - 110f;
        float bottom = 110f;
        DigitalTwinModeSwitch modeSwitch = ActiveSwitch();
        if (modeSwitch != null && TryGetCanvasBounds((RectTransform)modeSwitch.transform, out Rect bar))
            top = bar.yMin - margin;
        viewControls ??= FindObjectsByType<ViewModeSegmentedControl>(FindObjectsInactive.Include);
        foreach (ViewModeSegmentedControl control in viewControls)
        {
            if (control != null && control.isActiveAndEnabled && TryGetCanvasBounds((RectTransform)control.transform, out Rect controls))
            {
                bottom = controls.yMax + margin;
                break;
            }
        }
        if (top - bottom < 200f)
        {
            top = size.y - margin;
            bottom = margin;
        }
        return Rect.MinMaxRect(margin, bottom, size.x - margin, top);
    }

    private bool TryGetCanvasBounds(RectTransform rect, out Rect bounds)
    {
        bounds = default;
        if (rect == null || canvasRect == null)
            return false;
        rect.GetWorldCorners(corners);
        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);
        Vector2 origin = canvasRect.rect.min;
        for (int i = 0; i < 4; i++)
        {
            Vector2 local = (Vector2)canvasRect.InverseTransformPoint(corners[i]) - origin;
            min = Vector2.Min(min, local);
            max = Vector2.Max(max, local);
        }
        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }

    private Vector2 Clamp(Vector2 corner)
    {
        Vector2 size = window.sizeDelta;
        float x = Mathf.Clamp(corner.x, area.xMin, Mathf.Max(area.xMin, area.xMax - size.x));
        float y = Mathf.Clamp(corner.y, area.yMin, Mathf.Max(area.yMin, area.yMax - size.y));
        return new Vector2(x, y);
    }

    private void BeginDrag(PointerEventData eventData)
    {
        window.SetAsLastSibling();
        dragging = PointerInCanvas(eventData, out Vector2 pointer);
        grabOffset = pointer - window.anchoredPosition;
        ShowGrip();
    }

    /// <summary>The header stays under the pointer, as far as the free area allows.</summary>
    private void DragTo(PointerEventData eventData)
    {
        if (!dragging || tabbed || !PointerInCanvas(eventData, out Vector2 pointer))
            return;
        Vector2 corner = Clamp(pointer - grabOffset);
        window.anchoredPosition = corner;
        Vector2 room = Room();
        place = new Vector2(room.x > 0f ? (corner.x - area.xMin) / room.x : 0f,
            room.y > 0f ? (corner.y - area.yMin) / room.y : 0f);
        placed = true;
    }

    private void EndDrag()
    {
        if (!dragging)
            return;
        dragging = false;
        SavePosition();
        ShowGrip();
    }

    /// <summary>The pointer in canvas units from the canvas's bottom left, where the window's corner is placed.</summary>
    private bool PointerInCanvas(PointerEventData eventData, out Vector2 point)
    {
        point = default;
        if (canvasRect == null || hostCanvas == null)
            return false;
        Camera view = hostCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hostCanvas.worldCamera;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, eventData.position, view, out Vector2 local))
            return false;
        point = local - canvasRect.rect.min;
        return true;
    }

    private void SetGripHovered(bool hovered)
    {
        gripHovered = hovered;
        ShowGrip();
    }

    private void ShowGrip()
    {
        if (grip == null)
            return;
        bool lit = gripHovered || dragging;
        Color color = lit
            ? UIBuilder.Token("Accent", 0.9f, new Color(0.21f, 0.93f, 1f, 0.9f))
            : UIBuilder.Token("Text", 0.28f, new Color(0.92f, 0.99f, 1f, 0.28f));
        foreach (Graphic rule in grip)
        {
            rule.color = color;
        }
    }

    /// <summary>How far the window can move across and up the free area at its size.</summary>
    private Vector2 Room() =>
        new(Mathf.Max(0f, area.width - window.sizeDelta.x), Mathf.Max(0f, area.height - window.sizeDelta.y));

    /// <summary>
    /// The window's bottom-left corner in the free area as it is now. Its place
    /// is kept as shares of the room, so it holds its side through a resize.
    /// </summary>
    private Vector2 Corner()
    {
        if (!placed)
        {
            // First opened beside the model, clear of the left rail, resting on the bottom controls.
            return Clamp(new Vector2(canvasRect.rect.width * defaultLeft, area.yMin));
        }
        Vector2 room = Room();
        return new Vector2(area.xMin + Mathf.Clamp01(place.x) * room.x, area.yMin + Mathf.Clamp01(place.y) * room.y);
    }

    private void SavePosition()
    {
        if (tabbed || !placed)
            return;
        PlayerPrefs.SetFloat(PositionKeyX, Mathf.Clamp01(place.x));
        PlayerPrefs.SetFloat(PositionKeyY, Mathf.Clamp01(place.y));
        PlayerPrefs.Save();
    }

    private void LoadPosition()
    {
        placed = PlayerPrefs.HasKey(PositionKeyX) && PlayerPrefs.HasKey(PositionKeyY);
        place = placed ? new Vector2(PlayerPrefs.GetFloat(PositionKeyX), PlayerPrefs.GetFloat(PositionKeyY)) : Vector2.zero;
    }

    /// <summary>Forgets where the window was left, so it opens in its first place.</summary>
    public void ResetPosition()
    {
        PlayerPrefs.DeleteKey(PositionKeyX);
        PlayerPrefs.DeleteKey(PositionKeyY);
        LoadPosition();
        if (canvasRect != null && window != null)
            Layout(true);
    }
}
