using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Batch-only play test of the Ask tab on a phone held upright, with a notch
/// at the top and a browser toolbar at the bottom:
///
///   ASK is a third segment of the switch (TELEMETRY | SERVICE | ASK), clear
///   of the gear, and no other Ask button is in the bar. Choosing it takes the
///   screen below the switch on an opaque fill that reaches the screen's
///   edges; the switch shows ASK chosen and both views pressable, and the
///   question field clears the toolbar. An answer's reading leaves the tab for
///   Telemetry with its card focused; a service leaves it for Service with the
///   part's detail open. Pressing the view already on show also leaves the tab,
///   and the conversation is kept throughout. No errors may be logged.
///
/// Saves a capture of each step to Logs/AskTwinPortraitTest.
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class AskTwinPortraitPlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/AskTwinPortraitTest";
    private const string ActiveKey = "AskTwinPortraitPlayTest.Active";
    private const string StartKey = "AskTwinPortraitPlayTest.Start";
    // An iPhone 15 at 3x: 390 x 844 points.
    private const int Width = 1170;
    private const int Height = 2532;
    // Safari's status bar inset, and its bottom toolbar over the home indicator, as shares of the screen.
    private static readonly Vector4 PhoneInsets = new(59f / 844f, 0f, 112f / 844f, 0f);

    private static int step;
    private static float stepTime;
    private static int failures;
    private static readonly List<string> errors = new();
    private static Camera mainCamera;
    private static Camera uiCamera;
    private static Canvas canvas;
    private static GameObject portrait;
    private static TelemetryVisibility visibility;
    private static DigitalTwinModeSwitch modeSwitch;
    private static ServicePanelView panel;
    private static AskTwinWindow ask;
    private static int messages;

    static AskTwinPortraitPlayTest()
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
        float t = Time.time;
        if (step == 0 && t <= 3f)
            return;
        if (step > 0 && t < stepTime)
            return;

        switch (step)
        {
            case 0:
                Prepare();
                portrait.GetComponentInChildren<ViewModeSegmentedControl>().SelectSection();
                Next(4.2f);
                break;

            case 1:
            {
                StatRailManagers.Portrait()?.RebuildLayout();
                Button tab = Tab();
                Expect(tab != null, "ASK is a segment of the portrait switch");
                Expect(modeSwitch.transform.Cast<Transform>().Count(c => c.GetComponent<Button>() != null) == 3,
                    "the switch has three segments: TELEMETRY | SERVICE | ASK");
                Expect(tab != null && tab.GetComponentInChildren<TMP_Text>().text == "ASK", "the segment reads ASK");
                Expect(Bar().Cast<Transform>().All(c => c.name != "Ask Button"), "no other Ask button sits in the bar");
                RectTransform gear = Bar().Find("Settings Button") as RectTransform;
                Expect(gear == null || ScreenRect(Switch()).xMax < ScreenRect(gear).xMin,
                    $"the switch clears the gear ({ScreenRect(Switch())} / {(gear != null ? ScreenRect(gear) : default)})");
                Expect(Mathf.Abs(ScreenRect(Switch()).center.x - Width * 0.5f) < 2f, "the switch stays centred");
                Capture("01-telemetry");
                tab?.onClick.Invoke();
                Next(0.8f);
                break;
            }

            case 2:
            {
                Expect(ask.IsOpen && ask.IsTab, "ASK opens the Ask tab");
                Expect(!Tab().interactable && modeSwitch.TelemetryButton.interactable && modeSwitch.ServiceButton.interactable,
                    "the switch shows ASK chosen, and both views can be pressed");
                Rect window = ScreenRect(ask.Window);
                Expect(window.yMax <= ScreenRect(Bar()).yMin + 1f, "the tab starts below the switch's bar");
                Expect(window.xMin <= 1f && window.xMax >= Width - 1f && window.yMin <= 1f,
                    $"the tab fills the screen below the bar ({window})");
                Expect(ScreenRect((RectTransform)ask.Field.transform).yMin >= Height * PhoneInsets.z - 1f,
                    "the question field clears the browser toolbar");
                RectTransform backdrop = portrait.GetComponentsInChildren<RectTransform>().FirstOrDefault(r => r.name == "Ask Backdrop");
                Expect(backdrop != null && ScreenRect(backdrop).yMax >= Height - 1f,
                    "an opaque fill runs behind the bar to the screen's top, as the Service tab's");
                Expect(ask.Window.Find("Header") == null || !ask.Window.Find("Header").gameObject.activeSelf,
                    "the tab has no window header: the switch is its header");
                Capture("02-ask");
                ask.Field.text = "Why is belt slip red?";
                ask.Field.onSubmit.Invoke(ask.Field.text);
                Next(1.6f);
                break;
            }

            case 3:
                Expect(ask.Evidence.Count > 0, "the answer shows the reading it rests on");
                Rect row = ScreenRect((RectTransform)ask.Evidence[0].transform);
                Expect(row.xMin >= 0f && row.xMax <= Width, "the reading's row fits the screen's width");
                Capture("03-answer");
                messages = ask.MessageCount;
                ask.Evidence[0].onClick.Invoke();
                Next(1.2f);
                break;

            case 4:
                Expect(!ask.IsOpen, "pressing a reading leaves the Ask tab");
                Expect(visibility.CurrentMode == DigitalTwinMode.Telemetry && !modeSwitch.TelemetryButton.interactable,
                    "and shows Telemetry, chosen in the switch");
                Expect(CardFocus.Focused != null && CardFocus.Focused.BoundSource != null &&
                       CardFocus.Focused.BoundSource.StatId == "FEED-01.SLIP",
                    "with the reading's card focused");
                Capture("04-reading-shown");
                Tab().onClick.Invoke();
                ask.Send("What needs service next?");
                Next(1.6f);
                break;

            case 5:
            {
                Expect(ask.IsOpen && ask.MessageCount == messages + 2, "the conversation is kept when the tab comes back");
                Capture("05-service-answer");
                Button part = ask.Evidence.FirstOrDefault(b => b.name.StartsWith("part."));
                Expect(part != null, "a service answer carries its parts");
                part?.onClick.Invoke();
                Next(1.2f);
                break;
            }

            case 6:
                Expect(!ask.IsOpen, "pressing a service leaves the Ask tab");
                Expect(visibility.CurrentMode == DigitalTwinMode.Service && !modeSwitch.ServiceButton.interactable,
                    "and shows Service, chosen in the switch");
                Expect(panel.IsShowingDetail, "with the part's detail open");
                Capture("06-service-shown");
                Tab().onClick.Invoke();
                Next(0.6f);
                break;

            case 7:
                Expect(ask.IsOpen, "ASK opens over the Service view");
                // The view already on show: no change of view, but the tab still gives way.
                modeSwitch.ServiceButton.onClick.Invoke();
                Next(0.6f);
                break;

            case 8:
                Expect(!ask.IsOpen && visibility.CurrentMode == DigitalTwinMode.Service && Tab().interactable &&
                       !modeSwitch.ServiceButton.interactable,
                    "pressing the view already on show leaves the Ask tab for it");
                RectTransform backdropAfter = portrait.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(r => r.name == "Ask Backdrop");
                Expect(backdropAfter == null || !backdropAfter.gameObject.activeSelf, "the Ask tab's fill goes with it");
                Capture("07-back-to-service");
                Finish(failures == 0 && errors.Count == 0 ? "passed" : "failed");
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static RectTransform Switch() => (RectTransform)modeSwitch.transform;

    private static RectTransform Bar() => (RectTransform)modeSwitch.transform.parent;

    private static Button Tab()
    {
        Transform tab = modeSwitch.transform.Find("Ask Button");
        return tab != null ? tab.GetComponent<Button>() : null;
    }

    private static Rect ScreenRect(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 low = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
        Vector2 high = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
        return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
    }

    private static void Prepare()
    {
        Application.logMessageReceived += RecordError;

        // The portrait layout, as a phone shows it, with the page's insets.
        ResponsiveLayoutShell shell = Object.FindAnyObjectByType<ResponsiveLayoutShell>();
        shell.enabled = false;
        portrait = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .First(t => t.name == "Portrait layout").gameObject;
        GameObject wide = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .First(t => t.name == "Wide layout").gameObject;
        wide.SetActive(false);
        portrait.SetActive(true);
        DeviceSafeArea.EditorInsets = PhoneInsets;

        canvas = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas");
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;

        visibility = portrait.GetComponentInChildren<TelemetryVisibility>();
        modeSwitch = portrait.GetComponentInChildren<DigitalTwinModeSwitch>();
        panel = portrait.GetComponentInChildren<ServicePanelView>();
        ask = Object.FindAnyObjectByType<AskTwinWindow>();

        mainCamera = Camera.main;
        mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData mainData))
            mainData.renderPostProcessing = false;
        uiCamera = new GameObject("Ask Portrait Test UI Camera").AddComponent<Camera>();
        uiCamera.enabled = false;
        uiCamera.clearFlags = CameraClearFlags.SolidColor;
        uiCamera.backgroundColor = Color.clear;
        uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
        mainCamera.targetTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        uiCamera.targetTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);

        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera;
        canvas.planeDistance = 10f;
        Canvas.ForceUpdateCanvases();
    }

    private static void RecordError(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message);
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
        Texture2D image = new(Width, Height, TextureFormat.RGBA32, false);
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

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            failures++;
        Debug.Log($"ASK_PORTRAIT_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void Finish(string result)
    {
        DeviceSafeArea.EditorInsets = null;
        DigitalTwinSettings.ResetToDefaults();
        foreach (string error in errors)
            Debug.Log($"ASK_PORTRAIT_TEST error logged: {error}");
        Application.logMessageReceived -= RecordError;
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"ASK_PORTRAIT_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
