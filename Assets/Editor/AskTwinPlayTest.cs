using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Batch-only play test of the Ask window in the wide layout:
///
///   An Ask button sits beside the Telemetry / Service switch, its label set as
///   SERVICE's and the button no wider than its icon and label need; pressing
///   it opens the window, between the top and bottom controls, with a greeting
///   and suggested questions, and turns the button's label to the selected
///   colour. A typed question shows as a sent message, typing dots while the
///   assistant answers, then a reply in plain text (no bubble) with the readings
///   it rests on under TELEMETRY DATA, in neutral rows with a chevron; pressing
///   a reading focuses its card. A suggested question sends itself, and a
///   service answer carries the parts as rows under SERVICE DATA. Dragging the
///   header by its grip moves the window with the pointer but never past the
///   top and bottom controls. Leader lines of every layout treat the window as
///   an obstacle. Letters typed into the field do not turn the camera, and Esc
///   closes the window. The window also opens over the Service view. No errors
///   may be logged.
///
/// Saves a capture of each step to Logs/AskTwinTest.
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class AskTwinPlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/AskTwinTest";
    private const string ActiveKey = "AskTwinPlayTest.Active";
    private const string StartKey = "AskTwinPlayTest.Start";

    private static readonly FieldInfo DesiredYaw =
        typeof(OrbitCameraController).GetField("desiredYaw", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int step;
    private static float stepTime;
    private static int failures;
    private static readonly List<string> errors = new();
    private static readonly List<Key> held = new();
    private static Camera mainCamera;
    private static Camera uiCamera;
    private static Keyboard keyboard;
    private static AskTwinWindow ask;
    private static OrbitCameraController orbit;
    private static DigitalTwinModeSwitch modeSwitch;
    private static InputSettings.EditorInputBehaviorInPlayMode inputBehaviour;
    private static InputSettings.BackgroundBehavior backgroundBehaviour;
    private static bool inputChanged;
    private static float yawBefore;
    private static Vector2 placedAt;

    static AskTwinPlayTest()
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    public static void Run()
    {
        Directory.CreateDirectory(OutputDirectory);
        foreach (string old in Directory.GetFiles(OutputDirectory, "*.png"))
            File.Delete(old);
        DigitalTwinSettings.ResetToDefaults();
        PlayerPrefs.DeleteKey("ask.window.x");
        PlayerPrefs.DeleteKey("ask.window.y");
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f) > 300f)
        {
            Finish("timeout");
            return;
        }
        if (!EditorApplication.isPlaying)
            return;

        ReleaseHeld();

        float t = Time.time;
        if (step == 0 && t <= 3f)
            return;
        if (step > 0 && t < stepTime)
            return;

        switch (step)
        {
            case 0:
                Prepare();
                Object.FindAnyObjectByType<ViewModeSegmentedControl>().SelectSection();
                Next(4.2f);
                break;

            case 1:
                Expect(ask != null, "the scene has an Ask window");
                Expect(Launcher() != null, "an Ask button sits beside the Telemetry / Service switch");
                Expect(!ask.IsOpen, "the window starts closed");
                CheckLauncherLabel();
                Capture("01-launcher");
                Launcher().onClick.Invoke();
                Next(0.8f);
                break;

            case 2:
                Expect(ask.IsOpen && ask.Window.gameObject.activeInHierarchy, "the Ask button opens the window");
                Expect(LauncherState() != null && LauncherState().Selected, "the Ask button shows Selected while it is open");
                Expect(LauncherLabel() != null && LauncherLabel().color == SwitchLabel(true).color,
                    $"the open Ask button's label takes the chosen segment's colour ({LauncherLabel()?.color} / {SwitchLabel(true)?.color})");
                Expect(LauncherLabel().color == UIControlColors.Selected, "that colour is the design system's Control Selected");
                Expect(ask.Window.Find("Header/Grip") != null, "the header has a grip to drag it by");
                Expect(ask.MessageCount == 1, "the window greets the viewer");
                Expect(ask.Suggestions.Count > 0, "the greeting suggests questions");
                Expect(InsideControls(ask.Window), "the window opens between the top and bottom controls");
                Expect(LeaderLineObstacle.EveryLayoutObstacles.Any(o => o.transform == ask.Window),
                    "leader lines of every layout fade under the window");
                Capture("02-open");
                ask.Field.text = "Why is belt slip red?";
                Capture("03-typed");
                ask.Field.onSubmit.Invoke(ask.Field.text);
                // Checked at once: a batch frame is slower than the answer.
                Expect(ask.MessageCount == 2, "the question shows as a sent message");
                Expect(ask.Window.GetComponentsInChildren<Transform>().Any(c => c.name == "Typing"),
                    "a typing bubble shows while the assistant answers");
                Expect(ask.Field.text.Length == 0, "the field empties once sent");
                Capture("04-typing");
                Next(0.1f);
                break;

            case 3:
                Next(1.2f);
                break;

            case 4:
                Expect(ask.MessageCount == 3, "the assistant replies");
                Expect(!ask.Window.GetComponentsInChildren<Transform>().Any(c => c.name == "Typing"),
                    "the typing bubble goes once the reply is in");
                Expect(LastReplyText().Contains("Belt slip"), $"the reply is about belt slip: \"{LastReplyText()}\"");
                Expect(ask.Evidence.Count > 0, "the reply shows the reading it rests on");
                Expect(LastReply() != null && LastReply().GetComponentsInChildren<UIShape>()
                        .All(shape => shape.Component == null || shape.Component.name != "Message Sent"),
                    "the reply is plain text, not a bubble");
                Transform readings = LastReply()?.Find("Telemetry");
                Expect(readings != null && readings.Find("Heading/Title").GetComponent<TMP_Text>().text == "TELEMETRY DATA",
                    "the readings sit under TELEMETRY DATA");
                Expect(readings != null && readings.Find("FEED-01.SLIP/Last Hour") != null &&
                       readings.Find("FEED-01.SLIP/Chevron") != null,
                    "a reading's row has its last hour and a chevron");
                Expect(readings != null && readings.Find("FEED-01.SLIP").GetComponent<UIControlState>().Tone == UITone.Normal,
                    "a critical reading's row stays neutral; only its figure is coloured");
                Capture("05-answer");
                ask.Evidence[0].onClick.Invoke();
                Next(1.2f);
                break;

            case 5:
                Expect(CardFocus.Focused != null && CardFocus.Focused.BoundSource != null &&
                       CardFocus.Focused.BoundSource.StatId == "FEED-01.SLIP",
                    "pressing the reading focuses its card");
                Capture("06-shown-on-model");
                Button service = ask.Suggestions.FirstOrDefault(b =>
                    b.GetComponentInChildren<TMP_Text>().text.ToLowerInvariant().Contains("service"));
                Expect(service != null, "the reply suggests asking about services");
                service?.onClick.Invoke();
                Next(1.5f);
                break;

            case 6:
                Expect(ask.MessageCount == 5, "a suggested question sends itself and is answered");
                Expect(ask.Evidence.Any(b => b.GetComponent<UIShape>()?.Component?.name == "List Row"),
                    "a service answer shows its parts as rows");
                Expect(LastReply()?.Find("Service") != null, "the parts sit under SERVICE DATA");
                Capture("07-service-answer");
                placedAt = ask.Window.anchoredPosition;
                Drag(new Vector2(500f, 120f));
                Next(0.3f);
                break;

            case 7:
                Expect(ask.Window.anchoredPosition.x > placedAt.x + 100f, "dragging the header moves the window");
                Capture("08-dragged");
                Drag(new Vector2(-6000f, -6000f));
                Expect(InsideControls(ask.Window), "the window cannot be dragged past the bottom controls");
                Drag(new Vector2(0f, 6000f));
                Expect(InsideControls(ask.Window), "the window cannot be dragged past the top controls");
                Drag(new Vector2(200f, -6000f));
                // The keyboard into the field, then letters that would turn the camera.
                KeyboardNavigator.Focus(ask.Field);
                ask.Field.ActivateInputField();
                Next(0.3f);
                break;

            case 8:
                yawBefore = Yaw();
                Hold(Key.A);
                Next(0.6f);
                break;

            case 9:
                Expect(Mathf.Abs(Mathf.DeltaAngle(yawBefore, Yaw())) < 0.5f,
                    $"letters typed into the field leave the camera alone ({yawBefore:0.0} → {Yaw():0.0})");
                Press(Key.Escape);
                Next(0.4f);
                break;

            case 10:
                Expect(!ask.IsOpen, "Esc closes the window while the keyboard is in it");
                Expect(LauncherState() != null && !LauncherState().Selected, "the Ask button goes back to idle");
                Expect(LauncherLabel() != null && LauncherLabel().color == SwitchLabel(false).color,
                    $"its label goes back to the unchosen segment's colour ({LauncherLabel()?.color} / {SwitchLabel(false)?.color})");
                Expect(LauncherLabel().color == UIControlColors.Idle, "that colour is the design system's Control");
                // The control: with no field to type in, the same key does turn the camera.
                yawBefore = Yaw();
                Hold(Key.A);
                Next(0.6f);
                break;

            case 11:
                Expect(Mathf.Abs(Mathf.DeltaAngle(yawBefore, Yaw())) > 0.5f,
                    $"with no field in use, A turns the camera ({yawBefore:0.0} → {Yaw():0.0})");
                modeSwitch.SelectService();
                Next(1.6f);
                break;

            case 12:
                Launcher().onClick.Invoke();
                Next(0.8f);
                break;

            case 13:
                Expect(ask.IsOpen, "the window opens over the Service view");
                Expect(ask.MessageCount == 5, "the conversation is kept while the window is closed");
                Capture("09-over-service");
                Finish(failures == 0 && errors.Count == 0 ? "passed" : "failed");
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Input
    // -----------------------------------------------------------------------

    private static void Press(Key key)
    {
        held.Add(key);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(held.ToArray()));
    }

    /// <summary>A key held down until released, over several frames.</summary>
    private static void Hold(Key key)
    {
        holding = key;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
    }

    private static Key? holding;

    private static void ReleaseHeld()
    {
        if (holding.HasValue && Time.time >= stepTime)
            holding = null;
        if (held.Count == 0 && !holding.HasValue)
            return;
        held.Clear();
        InputSystem.QueueStateEvent(keyboard,
            holding.HasValue ? new KeyboardState(holding.Value) : new KeyboardState());
    }

    /// <summary>Drags the header from its middle by <paramref name="delta"/> screen pixels.</summary>
    private static void Drag(Vector2 delta)
    {
        Transform header = ask.Window.Find("Header");
        Vector2 start = RectTransformUtility.WorldToScreenPoint(uiCamera, header.position);
        PointerEventData data = new(EventSystem.current) { position = start, pressPosition = start };
        ExecuteEvents.Execute(header.gameObject, data, ExecuteEvents.beginDragHandler);
        data.position = start + delta;
        data.delta = delta;
        ExecuteEvents.Execute(header.gameObject, data, ExecuteEvents.dragHandler);
        ExecuteEvents.Execute(header.gameObject, data, ExecuteEvents.endDragHandler);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static float Yaw() => (float)DesiredYaw.GetValue(orbit);

    private static Button Launcher()
    {
        foreach (Transform t in Object.FindObjectsByType<Transform>())
        {
            if (t.name == "Ask Button" && t.gameObject.activeInHierarchy)
                return t.GetComponentInChildren<Button>();
        }
        return null;
    }

    private static UIControlState LauncherState() => Launcher() != null ? Launcher().GetComponent<UIControlState>() : null;

    private static TMP_Text LauncherLabel() => Launcher() != null ? Launcher().GetComponentInChildren<TMP_Text>() : null;

    /// <summary>The switch's label of the view on show (chosen) or of the other one.</summary>
    private static TMP_Text SwitchLabel(bool chosen)
    {
        bool telemetry = DigitalTwinModeState.Get(DigitalTwinMode.Telemetry) == DigitalTwinMode.Telemetry;
        Button button = telemetry == chosen ? modeSwitch.TelemetryButton : modeSwitch.ServiceButton;
        return button != null ? button.GetComponentInChildren<TMP_Text>() : null;
    }

    /// <summary>ASK is set as SERVICE beside it, and the button leaves no more room after it than before the icon.</summary>
    private static void CheckLauncherLabel()
    {
        TMP_Text label = LauncherLabel();
        TMP_Text service = modeSwitch.ServiceButton.GetComponentInChildren<TMP_Text>();
        Expect(label != null && Mathf.Approximately(label.fontSize, service.fontSize) && label.font == service.font &&
               Mathf.Approximately(label.characterSpacing, service.characterSpacing),
            $"ASK is set as SERVICE ({label?.font?.name} {label?.fontSize} / {service.font.name} {service.fontSize})");
        if (label == null)
            return;
        float after = label.rectTransform.rect.width - label.GetPreferredValues(label.text).x;
        Transform icon = label.transform.parent.Find("Icon");
        float before = ((RectTransform)icon).anchoredPosition.x;
        Expect(Mathf.Abs(after - before) < 2f, $"ASK has even room either side ({before:0.0} before, {after:0.0} after)");
    }

    private static Transform LastReply()
    {
        Transform reply = null;
        foreach (Transform child in ask.Window.GetComponentsInChildren<Transform>())
        {
            if (child.name == "Reply")
                reply = child;
        }
        return reply;
    }

    private static string LastReplyText()
    {
        Transform reply = LastReply();
        return reply != null ? reply.Find("Text").GetComponent<TMP_Text>().text : string.Empty;
    }

    /// <summary>Below the Telemetry / Service switch and above the view-mode control, in screen space.</summary>
    private static bool InsideControls(RectTransform window)
    {
        Rect box = ScreenRect(window);
        Rect bar = ScreenRect((RectTransform)modeSwitch.transform);
        ViewModeSegmentedControl control = Object.FindObjectsByType<ViewModeSegmentedControl>().First(c => c.isActiveAndEnabled);
        Rect controls = ScreenRect((RectTransform)control.transform);
        bool inside = box.yMax <= bar.yMin + 0.5f && box.yMin >= controls.yMax - 0.5f && box.xMin >= -0.5f &&
                      box.xMax <= uiCamera.pixelWidth + 0.5f;
        if (!inside)
            Debug.Log($"ASK_TWIN_TEST window {box} bar {bar} controls {controls}");
        return inside;
    }

    private static Rect ScreenRect(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
        return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
    }

    private static void Prepare()
    {
        Application.logMessageReceived += RecordError;
        inputBehaviour = InputSystem.settings.editorInputBehaviorInPlayMode;
        backgroundBehaviour = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        inputChanged = true;
        keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        ask = Object.FindAnyObjectByType<AskTwinWindow>();
        modeSwitch = Object.FindObjectsByType<DigitalTwinModeSwitch>().First(s => s.isActiveAndEnabled);

        mainCamera = Camera.main;
        orbit = mainCamera.GetComponent<OrbitCameraController>();
        mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData mainData))
            mainData.renderPostProcessing = false;

        uiCamera = new GameObject("Ask Test UI Camera").AddComponent<Camera>();
        uiCamera.enabled = false;
        uiCamera.clearFlags = CameraClearFlags.SolidColor;
        uiCamera.backgroundColor = Color.clear;
        uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
        mainCamera.targetTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        uiCamera.targetTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);

        // Overlay canvases are not part of a camera render: every canvas is
        // drawn by the UI camera, in its sorting order.
        Canvas main = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas");
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas))
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCamera;
            canvas.planeDistance = canvas == main ? 10f : 5f;
            canvas.gameObject.layer = LayerMask.NameToLayer("UI");
            foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = LayerMask.NameToLayer("UI");
        }
        StatRailManagers.Wide()?.RebuildLayout();
    }

    private static void Capture(string name)
    {
        Color32[] model = Read(mainCamera.targetTexture);
        Canvas.ForceUpdateCanvases();
        uiCamera.Render();
        Color32[] ui = Read(uiCamera.targetTexture);
        for (int i = 0; i < model.Length; i++)
        {
            float cover = ui[i].a / 255f;
            model[i] = new Color32(
                (byte)Mathf.Min(255f, ui[i].r + model[i].r * (1f - cover)),
                (byte)Mathf.Min(255f, ui[i].g + model[i].g * (1f - cover)),
                (byte)Mathf.Min(255f, ui[i].b + model[i].b * (1f - cover)),
                255);
        }
        RenderTexture target = mainCamera.targetTexture;
        Texture2D image = new(target.width, target.height, TextureFormat.RGBA32, false);
        image.SetPixels32(model);
        image.Apply();
        File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
        Object.DestroyImmediate(image);
    }

    private static Color32[] Read(RenderTexture target)
    {
        RenderTexture.active = target;
        Texture2D image = new(target.width, target.height, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        Color32[] pixels = image.GetPixels32();
        Object.DestroyImmediate(image);
        return pixels;
    }

    private static void RecordError(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message);
    }

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            failures++;
        Debug.Log($"ASK_TWIN_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void Finish(string result)
    {
        DigitalTwinSettings.ResetToDefaults();
        PlayerPrefs.DeleteKey("ask.window.x");
        PlayerPrefs.DeleteKey("ask.window.y");
        if (inputChanged)
        {
            InputSystem.settings.editorInputBehaviorInPlayMode = inputBehaviour;
            InputSystem.settings.backgroundBehavior = backgroundBehaviour;
        }
        foreach (string error in errors)
            Debug.Log($"ASK_TWIN_TEST error logged: {error}");
        Application.logMessageReceived -= RecordError;
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"ASK_TWIN_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
