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
/// Batch-only play test of the design system's control colours (see
/// <see cref="UIControlColors"/>): in every state of both layouts, the label
/// and icon of every control on screen is one of Control (at rest), Control
/// Selected (chosen) or Neutral (unusable), and the chosen ones are near white.
///
/// Walks the wide layout at rest, with a pinned detail panel, with the Ask
/// window open, with the settings on two tabs, and in the Service view with a
/// part open; then the portrait layout in Telemetry, Ask and Service. Content
/// is not checked: cards, list rows and the readings in an answer keep their
/// own colours.
///
/// Saves a capture of each state to Logs/ControlColoursTest.
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class ControlColoursPlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/ControlColoursTest";
    private const string ActiveKey = "ControlColoursPlayTest.Active";
    private const string StartKey = "ControlColoursPlayTest.Start";
    private const float Tolerance = 0.01f;

    private static int step;
    private static float stepTime;
    private static int failures;
    private static int checkedGraphics;
    private static readonly List<string> errors = new();
    private static Camera mainCamera;
    private static Camera uiCamera;
    private static AskTwinWindow ask;
    private static SettingsPanel settings;

    static ControlColoursPlayTest()
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
                Next(1.5f);
                break;

            case 1:
                Audit("wide, telemetry at rest");
                ExpectChosen(ActiveSwitch().TelemetryButton, "TELEMETRY, the view on show");
                ExpectAtRest(ActiveSwitch().ServiceButton, "SERVICE");
                Capture("01-wide-rest");
                PerformanceStatCardView card = Object.FindObjectsByType<PerformanceStatCardView>()
                    .First(c => c.isActiveAndEnabled && c.BoundSource != null);
                CardFocus.Press(card, CardPressKind.Mouse);
                Next(1.2f);
                break;

            case 2:
            {
                TelemetryDetailPanel panel = Controller().OpenPanels.FirstOrDefault();
                Expect(panel != null, "a card opens its detail panel");
                Button pin = panel != null ? Find<Button>(panel.transform, "Pin") : null;
                pin?.onClick.Invoke();
                Next(0.6f);
                break;
            }

            case 3:
            {
                TelemetryDetailPanel panel = Controller().OpenPanels.FirstOrDefault();
                Audit("wide, pinned detail panel");
                Expect(panel != null && panel.IsPinned, "the panel is pinned");
                if (panel != null)
                {
                    ExpectChosen(Find<Button>(panel.transform, "H"), "the range on show");
                    ExpectAtRest(Find<Button>(panel.transform, "D"), "a range not on show");
                    ExpectChosen(Find<Button>(panel.transform, "Pin"), "the pin of a pinned panel");
                    ExpectAtRest(Find<Button>(panel.transform, "Close"), "the panel's close");
                }
                Capture("02-wide-pinned-panel");
                ask.SetOpen(true);
                Next(0.8f);
                break;
            }

            case 4:
                Audit("wide, Ask window open");
                Expect(ask.Suggestions.Count > 0, "the greeting suggests questions");
                foreach (Button suggestion in ask.Suggestions)
                    ExpectAtRest(suggestion, "a suggested question");
                Capture("03-wide-ask");
                ask.SetOpen(false);
                settings.SetOpen(true);
                Next(0.8f);
                break;

            case 5:
                Audit("wide, settings on Display");
                ExpectChosen(Find<Button>(settings.Modal, "DISPLAY"), "the settings tab on show");
                ExpectAtRest(Find<Button>(settings.Modal, "CAMERA"), "a settings tab not on show");
                Capture("04-wide-settings-display");
                Find<Button>(settings.Modal, "TELEMETRY")?.onClick.Invoke();
                Next(0.6f);
                break;

            case 6:
                Audit("wide, settings on Telemetry");
                Capture("05-wide-settings-telemetry");
                settings.SetOpen(false);
                ActiveSwitch().SelectService();
                Next(1.2f);
                break;

            case 7:
            {
                Audit("wide, service list");
                ExpectChosen(ActiveSwitch().ServiceButton, "SERVICE, the view on show");
                ExpectAtRest(ActiveSwitch().TelemetryButton, "TELEMETRY");
                Capture("06-wide-service");
                TelemetryServiceSchedule schedule = Object.FindAnyObjectByType<TelemetryServiceSchedule>();
                List<ServiceItemStatus> statuses = new();
                schedule.GetStatuses(statuses);
                ServicePartLink.Request(statuses[0].Part);
                Next(1.2f);
                break;
            }

            case 8:
                Audit("wide, a part's service detail");
                Capture("07-wide-service-detail");
                ActiveSwitch().SelectTelemetry();
                Portrait();
                Next(1.5f);
                break;

            case 9:
                StatRailManagers.Portrait()?.RebuildLayout();
                Audit("portrait, telemetry");
                Capture("08-portrait-telemetry");
                ask.SetOpen(true);
                Next(0.8f);
                break;

            case 10:
            {
                Audit("portrait, Ask tab");
                Button tab = ActiveSwitch().GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "Ask Button");
                ExpectChosen(tab, "ASK, the tab on show");
                ExpectAtRest(ActiveSwitch().TelemetryButton, "TELEMETRY beside the chosen ASK");
                ExpectAtRest(ActiveSwitch().ServiceButton, "SERVICE beside the chosen ASK");
                Capture("09-portrait-ask");
                ActiveSwitch().SelectService();
                Next(1.2f);
                break;
            }

            case 11:
                Audit("portrait, service");
                ExpectChosen(ActiveSwitch().ServiceButton, "SERVICE, the view on show");
                Capture("10-portrait-service");
                Debug.Log($"CONTROL_COLOURS_TEST checked {checkedGraphics} labels and icons");
                Finish(failures == 0 && errors.Count == 0 ? "passed" : "failed");
                break;
        }
    }

    // ---- the checks ------------------------------------------------------------------

    /// <summary>Every control on screen: its own labels and icons are in a control colour.</summary>
    private static void Audit(string where)
    {
        int before = failures;
        int count = 0;
        foreach (Selectable control in Object.FindObjectsByType<Selectable>())
        {
            if (!control.isActiveAndEnabled || IsContent(control) || !Shown(control.transform))
                continue;
            foreach (Graphic graphic in Marks(control))
            {
                count++;
                Color c = graphic.color;
                bool known = Same(c, UIControlColors.Idle) || Same(c, UIControlColors.Selected) ||
                             Same(c, UIControlColors.Disabled);
                if (!known)
                    Expect(false, $"{where}: {Path(graphic.transform)} is {Hex(c)}, not a control colour");
            }
        }
        checkedGraphics += count;
        Expect(count > 0 && failures == before, $"{where}: {count} labels and icons of controls in the control colours");
    }

    private static void ExpectChosen(Selectable control, string what) => ExpectColour(control, UIControlColors.Selected, what, "chosen");

    private static void ExpectAtRest(Selectable control, string what) => ExpectColour(control, UIControlColors.Idle, what, "at rest");

    private static void ExpectColour(Selectable control, Color expected, string what, string state)
    {
        List<Graphic> marks = control != null ? Marks(control).ToList() : new List<Graphic>();
        bool ok = marks.Count > 0 && marks.All(g => Same(g.color, expected));
        Expect(ok, $"{what} is {state}: {string.Join(", ", marks.Select(g => g.name + " " + Hex(g.color)))}");
    }

    /// <summary>A control's own label and icon: not those of a control inside it, nor its frame.</summary>
    private static IEnumerable<Graphic> Marks(Selectable control)
    {
        foreach (Graphic graphic in control.GetComponentsInChildren<Graphic>())
        {
            if (graphic == control.targetGraphic || !graphic.isActiveAndEnabled ||
                graphic.GetComponentInParent<Selectable>() != control)
                continue;
            if (graphic is TMP_Text text && !string.IsNullOrWhiteSpace(text.text))
                yield return graphic;
            else if (IsIcon(graphic))
                yield return graphic;
        }
    }

    private static bool IsIcon(Graphic graphic)
    {
        if (graphic is UIBackArrowGraphic)
            return true;
        if (graphic is UIShape shape && shape.Component != null)
            return shape.Component.name == "Icon";
        return graphic is Image image && image.sprite != null && image.sprite.name.EndsWith("Icon");
    }

    /// <summary>Things that show data rather than label a control: cards, rows, readings in an answer.</summary>
    private static bool IsContent(Selectable control)
    {
        if (control is TMP_InputField or Scrollbar or Slider)
            return true;
        if (control.GetComponentInParent<PerformanceStatCardView>() != null)
            return true;
        return control.targetGraphic is UIShape shape && shape.Component != null &&
               shape.Component.name is "List Row" or "Card";
    }

    /// <summary>On screen: no group above it fades it out.</summary>
    private static bool Shown(Transform transform)
    {
        float alpha = 1f;
        foreach (CanvasGroup group in transform.GetComponentsInParent<CanvasGroup>())
        {
            alpha *= group.alpha;
            if (group.ignoreParentGroups)
                break;
        }
        return alpha > 0.05f;
    }

    private static bool Same(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < Tolerance && Mathf.Abs(a.g - b.g) < Tolerance && Mathf.Abs(a.b - b.b) < Tolerance &&
        a.a > 0.99f;

    private static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGBA(c);

    private static string Path(Transform t)
    {
        string path = t.name;
        for (int i = 0; i < 4 && t.parent != null; i++)
        {
            t = t.parent;
            path = t.name + "/" + path;
        }
        return path;
    }

    // ---- the scene ---------------------------------------------------------------

    private static T Find<T>(Transform root, string name) where T : Component =>
        root.GetComponentsInChildren<T>(true).FirstOrDefault(c => c.name == name);

    private static DigitalTwinModeSwitch ActiveSwitch() =>
        Object.FindObjectsByType<DigitalTwinModeSwitch>().First(s => s.isActiveAndEnabled);

    private static TelemetryDetailPanelController Controller() =>
        Object.FindObjectsByType<TelemetryDetailPanelController>().First(c => c.isActiveAndEnabled);

    private static void Prepare()
    {
        Application.logMessageReceived += RecordError;
        ask = Object.FindAnyObjectByType<AskTwinWindow>();
        settings = Object.FindAnyObjectByType<SettingsPanel>();

        mainCamera = Camera.main;
        mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData mainData))
            mainData.renderPostProcessing = false;
        uiCamera = new GameObject("Control Colours Test UI Camera").AddComponent<Camera>();
        uiCamera.enabled = false;
        uiCamera.clearFlags = CameraClearFlags.SolidColor;
        uiCamera.backgroundColor = Color.clear;
        uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
        SetResolution(1920, 1080);
        StatRailManagers.Wide()?.RebuildLayout();
    }

    /// <summary>The portrait layout, as a phone shows it.</summary>
    private static void Portrait()
    {
        Object.FindAnyObjectByType<ResponsiveLayoutShell>().enabled = false;
        GameObject portrait = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .First(t => t.name == "Portrait layout").gameObject;
        GameObject wide = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .First(t => t.name == "Wide layout").gameObject;
        wide.SetActive(false);
        portrait.SetActive(true);
        CanvasScaler scaler = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas")
            .GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;
        SetResolution(1080, 1920);
    }

    private static void SetResolution(int width, int height)
    {
        mainCamera.targetTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        uiCamera.targetTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
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
        File.WriteAllBytes(System.IO.Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
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
        Debug.Log($"CONTROL_COLOURS_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void Finish(string result)
    {
        DigitalTwinSettings.ResetToDefaults();
        foreach (string error in errors)
            Debug.Log($"CONTROL_COLOURS_TEST error logged: {error}");
        Application.logMessageReceived -= RecordError;
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"CONTROL_COLOURS_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
