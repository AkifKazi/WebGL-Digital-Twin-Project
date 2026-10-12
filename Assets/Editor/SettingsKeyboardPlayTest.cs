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
/// Batch-only play test of the settings panel and keyboard use, in the wide
/// layout, driven by a virtual keyboard:
///
///   Tab reaches the controls and the telemetry cards, with a focus ring and
///   the hover look; Enter on a card focuses it (its selected look), Enter
///   again opens its detail; Esc releases it. The gear opens the settings,
///   which hold the keyboard (Tab stays inside, Q and E switch tabs, Esc
///   closes). Settings apply: units, edge shading, high contrast, and the
///   interface scale only once its slider has rested. The arrow keys and A, D
///   turn the camera, but not while a control has the keyboard. In the
///   service view, Tab reaches the rows and Enter opens a part's detail, which
///   Esc closes. No errors may be logged; the viewer's settings are put back
///   to their defaults at the end.
///
/// Saves a capture of each step to Logs/SettingsKeyboardTest.
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class SettingsKeyboardPlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/SettingsKeyboardTest";
    private const string ActiveKey = "SettingsKeyboardPlayTest.Active";
    private const string StartKey = "SettingsKeyboardPlayTest.Start";

    private static readonly FieldInfo DesiredYaw =
        typeof(OrbitCameraController).GetField("desiredYaw", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int step;
    private static float stepTime;
    private static int failures;
    private static int tabs;
    private static readonly List<string> errors = new();
    private static readonly List<Key> held = new();
    private static Camera mainCamera;
    private static Camera uiCamera;
    private static Keyboard keyboard;
    private static SettingsPanel settings;
    private static CanvasScaler mainScaler;
    private static OrbitCameraController orbit;
    private static DigitalTwinModeSwitch modeSwitch;
    private static ServicePanelView servicePanel;
    private static Vector2 referenceBefore;
    private static float scaleBefore;
    private static InputSettings.EditorInputBehaviorInPlayMode inputBehaviour;
    private static InputSettings.BackgroundBehavior backgroundBehaviour;
    private static bool inputChanged;
    private static float yawBefore;

    static SettingsKeyboardPlayTest()
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

        // Keys pressed last frame are released, so each press is one press.
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
                Expect(Object.FindObjectsByType<UIEdgeShade>().Length == 1, "the edge shading is behind the interface");
                Expect(GameObject.Find("Settings Button") != null, "a gear sits beside the Telemetry / Service switch");
                Capture("01-telemetry");
                Press(Key.Tab);
                Next(0.3f);
                break;

            case 2:
                Expect(KeyboardNavigator.KeyboardMode && KeyboardNavigator.HasFocus, "Tab gives something the keyboard's focus");
                Expect(FocusRing() != null && FocusRing().activeInHierarchy, "the focus ring shows round it");
                Capture("02-tab");
                tabs = 0;
                Next(0.1f);
                break;

            case 3:
                // Tab on until a telemetry card has the keyboard.
                if (KeyboardNavigator.FocusedTarget is PerformanceStatCardView || tabs > 60)
                {
                    Expect(KeyboardNavigator.FocusedTarget is PerformanceStatCardView, $"Tab reaches the telemetry cards ({tabs} presses)");
                    Next(0.3f);
                    break;
                }
                tabs++;
                Press(Key.Tab);
                stepTime = Time.time + 0.06f;
                break;

            case 4:
                Capture("03-card-ring");
                Press(Key.Enter);
                Next(0.5f);
                break;

            case 5:
                Expect(CardFocus.Focused != null, "Enter on a card focuses it");
                UIShape background = CardFocus.Focused != null
                    ? CardFocus.Focused.GetComponentsInChildren<UIShape>().FirstOrDefault(s => s.Emphasised)
                    : null;
                Expect(background != null, "the focused card shows its selected look");
                Capture("04-card-focused");
                Press(Key.Enter);
                Next(0.8f);
                break;

            case 6:
                Capture("05-card-detail");
                Press(Key.Escape);
                Next(0.5f);
                break;

            case 7:
                Expect(CardFocus.Focused == null, "Esc releases the card");
                Expect(!KeyboardNavigator.HasFocus, "Esc drops the keyboard's focus");
                yawBefore = Yaw();
                Hold(Key.D);
                Next(0.5f);
                break;

            case 8:
                Release(Key.D);
                Expect(Mathf.Abs(Mathf.DeltaAngle(Yaw(), yawBefore)) > 5f, $"D turns the camera ({Mathf.DeltaAngle(Yaw(), yawBefore):0.0} degrees)");
                yawBefore = Yaw();
                Hold(Key.LeftArrow);
                Next(0.5f);
                break;

            case 9:
                Release(Key.LeftArrow);
                Expect(Mathf.Abs(Mathf.DeltaAngle(Yaw(), yawBefore)) > 5f, "the left arrow turns the camera while nothing has the keyboard");
                // Open the settings from the gear.
                GameObject.Find("Settings Button").GetComponentInChildren<Button>().onClick.Invoke();
                Next(0.5f);
                break;

            case 10:
                Expect(SettingsPanel.IsOpen && KeyboardNavigator.Scope == settings.Modal, "the gear opens the settings, which hold the keyboard");
                Capture("06-settings-display");
                Press(Key.E);
                Next(0.4f);
                break;

            case 11:
                Expect(settings.CurrentTab == 1, "E moves to the next tab");
                Capture("07-settings-telemetry");
                Press(Key.E);
                Next(0.4f);
                break;

            case 12:
                Capture("08-settings-camera");
                Press(Key.E);
                Next(0.4f);
                break;

            case 13:
                Capture("09-settings-graphics");
                Press(Key.Q);
                Next(0.3f);
                break;

            case 14:
                Expect(settings.CurrentTab == 2, "Q moves to the previous tab");
                tabs = 0;
                Next(0.1f);
                break;

            case 15:
                // Tab round the panel: it never leaves it.
                GameObject selected = EventSystem.current.currentSelectedGameObject;
                if (tabs > 0)
                    Expect(selected != null && selected.transform.IsChildOf(settings.Modal), $"Tab {tabs} stays in the settings ({(selected != null ? selected.name : "nothing")})");
                if (tabs >= 14)
                {
                    Capture("10-settings-keyboard");
                    Next(0.1f);
                    break;
                }
                tabs++;
                Press(Key.Tab);
                stepTime = Time.time + 0.08f;
                break;

            case 16:
                // Units, through the setting the Telemetry tab's choice sets.
                DigitalTwinSettings.Units = DigitalTwinSettings.UnitStyle.Engineering;
                yawBefore = Yaw();
                Hold(Key.LeftArrow);
                Next(0.4f);
                break;

            case 17:
                Release(Key.LeftArrow);
                Expect(Mathf.Abs(Mathf.DeltaAngle(Yaw(), yawBefore)) < 0.5f, "the camera holds still while the settings are open");
                Expect(CardTexts().Any(text => text.Contains("mm/s")), "engineering units show on the cards (mm/s)");
                // The interface scale waits for the slider to rest.
                referenceBefore = mainScaler.referenceResolution;
                scaleBefore = DigitalTwinSettings.InterfaceScale;
                SetScaleSlider(1.1f);
                Next(0.2f);
                break;

            case 18:
                Expect(mainScaler.referenceResolution == referenceBefore, "the scale is not applied while the slider moves");
                Next(1f);
                break;

            case 19:
                Expect(Mathf.Abs(DigitalTwinSettings.InterfaceScale - 1.1f) < 0.01f &&
                       Mathf.Abs(mainScaler.referenceResolution.x - referenceBefore.x * scaleBefore / 1.1f) < 1f,
                    $"the scale applies once the slider rests ({mainScaler.referenceResolution})");
                Press(Key.Escape);
                Next(0.5f);
                break;

            case 20:
                Expect(!SettingsPanel.IsOpen && KeyboardNavigator.Scope == null, "Esc closes the settings");
                StatRailManagers.Wide()?.RebuildLayout();
                // The cards glide to their new places.
                Next(1.5f);
                break;

            case 21:
                Capture("11-scaled-engineering-units");
                DigitalTwinSettings.InterfaceScale = 1f;
                DigitalTwinSettings.HighContrast = true;
                DigitalTwinSettings.EdgeShade = DigitalTwinSettings.EdgeShading.Strong;
                Next(0.6f);
                break;

            case 22:
                StatRailManagers.Wide()?.RebuildLayout();
                Next(1.5f);
                break;

            case 23:
                Capture("12-high-contrast-strong-shading");
                DigitalTwinSettings.ResetToDefaults();
                modeSwitch.ServiceButton.onClick.Invoke();
                Next(1.2f);
                break;

            case 24:
                // The wide layout's panel; the portrait one is switched off.
                servicePanel = Object.FindAnyObjectByType<ServicePanelView>();
                tabs = 0;
                Next(0.1f);
                break;

            case 25:
                // Tab to a service row.
                if (KeyboardNavigator.FocusedTarget is ServiceHoverTarget || tabs > 40)
                {
                    Expect(KeyboardNavigator.FocusedTarget is ServiceHoverTarget, $"Tab reaches the service rows ({tabs} presses)");
                    Next(0.4f);
                    break;
                }
                tabs++;
                Press(Key.Tab);
                stepTime = Time.time + 0.06f;
                break;

            case 26:
                Expect(servicePanel.HoveredPart != null, "the keyboard on a row marks its part, as hovering does");
                Capture("13-service-row-ring");
                Press(Key.Enter);
                Next(0.5f);
                break;

            case 27:
                Expect(servicePanel.IsShowingDetail, "Enter on a row opens the part's detail");
                Capture("14-service-detail");
                Press(Key.Escape);
                Next(0.4f);
                break;

            case 28:
                Expect(!servicePanel.IsShowingDetail, "Esc goes back to the list");
                Expect(errors.Count == 0, $"nothing logged an error ({errors.Count})");
                foreach (string error in errors.Take(10))
                    Debug.Log($"SETTINGS_KEYBOARD_TEST error: {error}");
                Finish(failures == 0 ? "passed" : $"failed ({failures})");
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Keyboard
    // -----------------------------------------------------------------------

    private static void Press(Key key)
    {
        held.Add(key);
        Queue();
    }

    private static void Hold(Key key)
    {
        held.Add(key);
        Queue();
        // Not released on the next frame: a separate list would; Release ends it.
        holding.Add(key);
    }

    private static readonly List<Key> holding = new();

    private static void Release(Key key)
    {
        holding.Remove(key);
        held.Remove(key);
        Queue();
    }

    private static void ReleaseHeld()
    {
        if (held.Count == 0 || held.All(holding.Contains))
            return;
        held.RemoveAll(key => !holding.Contains(key));
        Queue();
    }

    private static void Queue()
    {
        if (keyboard == null)
            return;
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(held.ToArray()));
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static float Yaw() => (float)DesiredYaw.GetValue(orbit);

    private static GameObject FocusRing()
    {
        GameObject canvas = GameObject.Find("Keyboard Focus");
        return canvas != null ? canvas.transform.Find("Focus Ring")?.gameObject : null;
    }

    private static IEnumerable<string> CardTexts() =>
        Object.FindObjectsByType<PerformanceStatCardView>()
            .SelectMany(card => card.GetComponentsInChildren<TMP_Text>())
            .Select(text => text.text);

    private static void SetScaleSlider(float value)
    {
        // The Display tab's first slider; the panel snaps and queues it.
        foreach (Slider slider in settings.Modal.GetComponentsInChildren<Slider>(true))
        {
            if (Mathf.Approximately(slider.minValue, DigitalTwinSettings.MinimumInterfaceScale))
            {
                slider.value = value;
                return;
            }
        }
        Expect(false, "the interface scale slider exists");
    }

    private static void Prepare()
    {
        Application.logMessageReceived += RecordError;
        // Batch play mode has no focused game view; send every device's input
        // to the game, as a focused browser tab does.
        inputBehaviour = InputSystem.settings.editorInputBehaviorInPlayMode;
        backgroundBehaviour = InputSystem.settings.backgroundBehavior;
        InputSystem.settings.editorInputBehaviorInPlayMode =
            InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        inputChanged = true;
        keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        settings = Object.FindAnyObjectByType<SettingsPanel>();
        modeSwitch = Object.FindAnyObjectByType<DigitalTwinModeSwitch>();

        mainCamera = Camera.main;
        orbit = mainCamera.GetComponent<OrbitCameraController>();
        mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData mainData))
            mainData.renderPostProcessing = false;

        uiCamera = new GameObject("Settings Test UI Camera").AddComponent<Camera>();
        uiCamera.enabled = false;
        uiCamera.clearFlags = CameraClearFlags.SolidColor;
        uiCamera.backgroundColor = Color.clear;
        uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
        mainCamera.targetTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        uiCamera.targetTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);

        // Overlay canvases are not part of a camera render: every canvas is
        // drawn by the UI camera, in its sorting order.
        Canvas main = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas");
        mainScaler = main.GetComponent<CanvasScaler>();
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
        Debug.Log($"SETTINGS_KEYBOARD_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void Finish(string result)
    {
        DigitalTwinSettings.ResetToDefaults();
        if (inputChanged)
        {
            InputSystem.settings.editorInputBehaviorInPlayMode = inputBehaviour;
            InputSystem.settings.backgroundBehavior = backgroundBehaviour;
        }
        Application.logMessageReceived -= RecordError;
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"SETTINGS_KEYBOARD_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
