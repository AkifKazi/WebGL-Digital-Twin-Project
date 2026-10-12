using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Something the keyboard can reach that is not a Selectable, such as a
/// telemetry card. Enter activates it as a click would.
/// </summary>
public interface IKeyboardTarget
{
    /// <summary>The rect the focus ring is drawn round.</summary>
    RectTransform FocusRect { get; }

    /// <summary>False while it is hidden, faded out or otherwise not there to use.</summary>
    bool CanTakeKeyboardFocus { get; }

    void ActivateFromKeyboard();

    /// <summary>The keyboard reached it (true) or left it (false): show it as hovered meanwhile.</summary>
    void SetKeyboardFocus(bool focused);
}

/// <summary>
/// Keyboard use of the interface, as a web page has it. Tab and Shift+Tab move
/// through everything that can be used, in the order the interface is built;
/// the arrow keys move between controls and adjust sliders (the EventSystem's
/// own navigation); Enter presses what is focused; Esc steps back: it closes
/// the open service detail, releases a focused card (see
/// <see cref="TelemetryCardFocusReleaser"/>) and drops the keyboard focus.
///
/// What the keyboard has reached gets a focus ring, a bright outline a gap
/// outside it, but only while the keyboard is in use: the pointer moving or
/// pressing hides it, so a click never leaves a ring behind.
///
/// A modal (the settings panel) sets <see cref="Scope"/> while it is open: Tab
/// then stays inside it, and the modal handles its own Esc.
///
/// Lives on the EventSystem.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class KeyboardNavigator : MonoBehaviour
{
    [Tooltip("The gap between what is focused and its ring.")]
    [SerializeField, Min(0f)] private float ringGap = 6f;

    [Tooltip("How far the pointer must move, in pixels, to count as using the pointer.")]
    [SerializeField, Min(0f)] private float pointerMoveThreshold = 3f;

    private static readonly HashSet<IKeyboardTarget> Targets = new();
    private static KeyboardNavigator instance;

    private readonly List<Candidate> candidates = new();
    private readonly List<int> pathA = new();
    private readonly List<int> pathB = new();
    private readonly Vector3[] corners = new Vector3[4];
    private Selectable[] selectableBuffer = new Selectable[64];

    private IKeyboardTarget customTarget;
    private RectTransform ringCanvasRect;
    private Canvas ringCanvas;
    private RectTransform ring;
    private bool keyboardMode;
    private ServicePanelView[] servicePanels;

    private readonly struct Candidate
    {
        public readonly Transform Transform;
        public readonly Selectable Selectable;
        public readonly IKeyboardTarget Target;

        public Candidate(Selectable selectable)
        {
            Transform = selectable.transform;
            Selectable = selectable;
            Target = null;
        }

        public Candidate(IKeyboardTarget target)
        {
            Transform = target.FocusRect;
            Selectable = null;
            Target = target;
        }
    }

    /// <summary>While set, Tab only reaches what is inside it, and Esc is left to its owner.</summary>
    public static RectTransform Scope { get; set; }

    /// <summary>True while the keyboard, not the pointer, was used last.</summary>
    public static bool KeyboardMode => instance != null && instance.keyboardMode;

    /// <summary>The non-Selectable target the keyboard is on, such as a card; null on a control or nothing.</summary>
    public static IKeyboardTarget FocusedTarget => instance != null ? instance.customTarget : null;

    /// <summary>True while a control has the keyboard's focus, so the arrow keys belong to it.</summary>
    public static bool HasFocus
    {
        get
        {
            EventSystem events = EventSystem.current;
            return (events != null && events.currentSelectedGameObject != null &&
                    events.currentSelectedGameObject.activeInHierarchy) ||
                   (instance != null && instance.customTarget != null);
        }
    }

    public static void Register(IKeyboardTarget target) => Targets.Add(target);

    public static void Unregister(IKeyboardTarget target)
    {
        Targets.Remove(target);
        if (instance != null && instance.customTarget == target)
            instance.SetCustomTarget(null);
    }

    /// <summary>Moves the keyboard to a non-Selectable target, telling the old and the new one.</summary>
    private void SetCustomTarget(IKeyboardTarget target)
    {
        if (customTarget == target)
            return;
        IKeyboardTarget previous = customTarget;
        customTarget = target;
        previous?.SetKeyboardFocus(false);
        target?.SetKeyboardFocus(true);
    }

    /// <summary>Gives <paramref name="selectable"/> the keyboard's focus, as Tab would.</summary>
    public static void Focus(Selectable selectable)
    {
        if (instance == null || selectable == null)
            return;
        instance.SetCustomTarget(null);
        EventSystem.current?.SetSelectedGameObject(selectable.gameObject);
    }

    /// <summary>Drops the keyboard's focus; the arrow keys go back to the camera.</summary>
    public static void ClearFocus()
    {
        if (instance == null)
            return;
        instance.SetCustomTarget(null);
        EventSystem events = EventSystem.current;
        if (events != null && !events.alreadySelecting)
            events.SetSelectedGameObject(null);
    }

    private void Awake()
    {
        instance = this;
        BuildRing();
    }

    private void OnDestroy()
    {
        if (instance == this)
            instance = null;
        if (ringCanvas != null)
            Destroy(ringCanvas.gameObject);
    }

    private void Update()
    {
        TrackPointer();
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.tabKey.wasPressedThisFrame)
        {
            keyboardMode = true;
            bool back = keyboard.shiftKey.isPressed;
            Move(back ? -1 : 1);
        }

        bool arrows = keyboard.upArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame ||
                      keyboard.leftArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame;
        if (arrows && HasFocus)
            keyboardMode = true;

        bool enter = keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame;
        if (enter && HasFocus)
        {
            keyboardMode = true;
            // A Selectable is pressed by the EventSystem's own Submit.
            if (customTarget != null && customTarget.CanTakeKeyboardFocus)
                customTarget.ActivateFromKeyboard();
        }

        if (keyboard.escapeKey.wasPressedThisFrame && Scope == null)
            StepBack();
    }

    private void LateUpdate()
    {
        if (customTarget != null && !customTarget.CanTakeKeyboardFocus)
            SetCustomTarget(null);
        PlaceRing();
    }

    // -----------------------------------------------------------------------
    // Pointer: using it hides the ring
    // -----------------------------------------------------------------------

    private void TrackPointer()
    {
        if (!keyboardMode)
            return;
        Mouse mouse = Mouse.current;
        bool pointer = mouse != null &&
                       (mouse.delta.ReadValue().sqrMagnitude > pointerMoveThreshold * pointerMoveThreshold ||
                        mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame ||
                        mouse.scroll.ReadValue().sqrMagnitude > 0f);
        Touchscreen touch = Touchscreen.current;
        pointer |= touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        if (!pointer)
            return;
        keyboardMode = false;
        // A custom target is only ever reached by the keyboard.
        SetCustomTarget(null);
    }

    // -----------------------------------------------------------------------
    // Esc
    // -----------------------------------------------------------------------

    private void StepBack()
    {
        // The service detail goes back to its list, as its back arrow does.
        servicePanels ??= FindObjectsByType<ServicePanelView>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (ServicePanelView panel in servicePanels)
        {
            if (panel != null && panel.isActiveAndEnabled && panel.IsShowingDetail)
                panel.ShowList();
        }
        // A focused card is released by its own Esc handler.
        ClearFocus();
    }

    // -----------------------------------------------------------------------
    // Tab order
    // -----------------------------------------------------------------------

    private void Move(int direction)
    {
        Gather();
        if (candidates.Count == 0)
            return;
        candidates.Sort(CompareOrder);

        int current = -1;
        GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate candidate = candidates[i];
            if ((customTarget != null && candidate.Target == customTarget) ||
                (customTarget == null && candidate.Selectable != null && candidate.Selectable.gameObject == selected))
            {
                current = i;
                break;
            }
        }

        int next = current < 0
            ? (direction > 0 ? 0 : candidates.Count - 1)
            : (current + direction + candidates.Count) % candidates.Count;
        Candidate chosen = candidates[next];
        if (chosen.Selectable != null)
        {
            SetCustomTarget(null);
            EventSystem.current?.SetSelectedGameObject(chosen.Selectable.gameObject);
        }
        else
        {
            EventSystem.current?.SetSelectedGameObject(null);
            SetCustomTarget(chosen.Target);
        }
    }

    private void Gather()
    {
        candidates.Clear();
        int count = Selectable.allSelectableCount;
        if (selectableBuffer.Length < count)
            selectableBuffer = new Selectable[Mathf.NextPowerOfTwo(count)];
        Selectable.AllSelectablesNoAlloc(selectableBuffer);
        for (int i = 0; i < count; i++)
        {
            Selectable selectable = selectableBuffer[i];
            selectableBuffer[i] = null;
            // Scrollbars are reached through what they scroll.
            if (selectable == null || selectable is Scrollbar || !selectable.IsInteractable() ||
                selectable.navigation.mode == Navigation.Mode.None)
                continue;
            if (Usable(selectable.transform as RectTransform))
                candidates.Add(new Candidate(selectable));
        }

        foreach (IKeyboardTarget target in Targets)
        {
            if (target != null && target.CanTakeKeyboardFocus && Usable(target.FocusRect))
                candidates.Add(new Candidate(target));
        }
    }

    /// <summary>On screen, inside the scope, and not faded out.</summary>
    private bool Usable(RectTransform rect)
    {
        if (rect == null || !rect.gameObject.activeInHierarchy)
            return false;
        if (Scope != null && !rect.IsChildOf(Scope))
            return false;
        if (!TryGetScreenRect(rect, out Rect screen) || screen.width < 2f || screen.height < 2f)
            return false;
        // The canvas's own pixels: the screen, or the camera's target it draws to.
        Rect view = rect.GetComponentInParent<Canvas>().rootCanvas.pixelRect;
        if (!screen.Overlaps(view))
            return false;
        float alpha = 1f;
        for (Transform t = rect; t != null; t = t.parent)
        {
            if (t.TryGetComponent(out CanvasGroup group))
            {
                alpha *= group.alpha;
                if (group.ignoreParentGroups)
                    break;
            }
        }
        return alpha > 0.05f;
    }

    /// <summary>The order the interface is built in: depth-first through the hierarchy.</summary>
    private int CompareOrder(Candidate a, Candidate b)
    {
        Path(a.Transform, pathA);
        Path(b.Transform, pathB);
        int length = Mathf.Min(pathA.Count, pathB.Count);
        for (int i = 0; i < length; i++)
        {
            if (pathA[i] != pathB[i])
                return pathA[i].CompareTo(pathB[i]);
        }
        return pathA.Count.CompareTo(pathB.Count);
    }

    private static void Path(Transform t, List<int> path)
    {
        path.Clear();
        for (; t != null; t = t.parent)
            path.Add(t.GetSiblingIndex());
        path.Reverse();
    }

    // -----------------------------------------------------------------------
    // Focus ring
    // -----------------------------------------------------------------------

    private void BuildRing()
    {
        GameObject canvasObject = new("Keyboard Focus", typeof(RectTransform), typeof(Canvas));
        canvasObject.layer = Mathf.Max(0, LayerMask.NameToLayer("UI"));
        DontDestroyOnLoad(canvasObject);
        ringCanvas = canvasObject.GetComponent<Canvas>();
        ringCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above every panel, the settings included.
        ringCanvas.sortingOrder = 500;
        ringCanvasRect = (RectTransform)canvasObject.transform;

        GameObject ringObject = new("Focus Ring", typeof(RectTransform));
        ringObject.layer = canvasObject.layer;
        ring = (RectTransform)ringObject.transform;
        ring.SetParent(ringCanvasRect, false);
        ring.anchorMin = ring.anchorMax = Vector2.zero;
        ring.pivot = Vector2.zero;

        UIComponentStyle style = UITheme.Active != null ? UITheme.Active.Component("Focus Ring") : null;
        if (style != null)
        {
            UIShape shape = ringObject.AddComponent<UIShape>();
            shape.Component = style;
            shape.raycastTarget = false;
        }
        else
        {
            UIFrameGraphic frame = ringObject.AddComponent<UIFrameGraphic>();
            frame.SetThickness(2f);
            frame.SetColors(new Color(0.55f, 0.96f, 1f, 1f), Color.clear);
            frame.raycastTarget = false;
        }
        ringObject.SetActive(false);
    }

    private void PlaceRing()
    {
        if (ring == null)
            return;
        RectTransform target = null;
        if (keyboardMode)
        {
            if (customTarget != null)
                target = customTarget.FocusRect;
            else if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
                target = EventSystem.current.currentSelectedGameObject.transform as RectTransform;
        }

        if (target == null || !target.gameObject.activeInHierarchy || !TryGetScreenRect(target, out Rect screen))
        {
            if (ring.gameObject.activeSelf)
                ring.gameObject.SetActive(false);
            return;
        }

        // The ring's canvas follows the target's scale, so its stroke and gap
        // are the same canvas units as the rest of the interface.
        Canvas owner = target.GetComponentInParent<Canvas>();
        float scale = owner != null ? Mathf.Max(owner.rootCanvas.scaleFactor, 0.01f) : 1f;
        if (!Mathf.Approximately(ringCanvas.scaleFactor, scale))
            ringCanvas.scaleFactor = scale;

        float gap = ringGap;
        ring.anchoredPosition = screen.position / scale - new Vector2(gap, gap);
        ring.sizeDelta = screen.size / scale + new Vector2(gap * 2f, gap * 2f);
        if (!ring.gameObject.activeSelf)
            ring.gameObject.SetActive(true);
    }

    private bool TryGetScreenRect(RectTransform rect, out Rect screen)
    {
        screen = default;
        Canvas canvas = rect.GetComponentInParent<Canvas>();
        if (canvas == null || !canvas.isActiveAndEnabled)
            return false;
        Camera camera = canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.rootCanvas.worldCamera;
        rect.GetWorldCorners(corners);
        Vector2 min = new(float.MaxValue, float.MaxValue);
        Vector2 max = new(float.MinValue, float.MinValue);
        for (int i = 0; i < 4; i++)
        {
            Vector2 point = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        screen = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        return true;
    }
}
