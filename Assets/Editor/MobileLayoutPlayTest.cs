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
/// Batch-only play test of the phone's pages, in the portrait layout at an
/// iPhone's size with Safari's insets:
///
///   The Ask tab's rule is as far below the switch as the question field is
///   below the rule above it. A reading pressed in an answer, asked while the
///   model shows its outside, goes inside (Cross Section) and focuses its card.
///   The telemetry sheet is drawn at the tabs' scale, edge to edge on an
///   opaque fill past the screen's sides and bottom, without the floating
///   panel's frame, and takes at most two thirds of the room under the top
///   bar. The Service tab draws no second line under the switch. The settings
///   are a page of their own: the whole screen, a back arrow, every group in
///   one list, each setting's description under it, touch targets close to 44
///   points, and a toggle's whole row flips it. The cards run one row a side
///   by default, and at most two a side however many the viewer asks for.
///   No errors may be logged.
///
/// Saves a capture of each step to Logs/MobileLayoutTest.
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class MobileLayoutPlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/MobileLayoutTest";
    private const string ActiveKey = "MobileLayoutPlayTest.Active";
    private const string StartKey = "MobileLayoutPlayTest.Start";
    private const int Width = 1170;
    private const int Height = 2532;
    // Safari's status bar inset, and its bottom toolbar over the home indicator, as shares of the screen.
    private static readonly Vector4 PhoneInsets = new(59f / 844f, 0f, 112f / 844f, 0f);
    // An iPhone 14's width in points: the screen is 3 pixels to a point.
    private const float PixelsPerPoint = Width / 390f;

    private static int step;
    private static float stepTime;
    private static int failures;
    private static readonly List<string> errors = new();
    private static Camera mainCamera;
    private static Camera uiCamera;
    private static GameObject portrait;
    private static DigitalTwinModeSwitch modeSwitch;
    private static AskTwinWindow ask;
    private static SettingsPanel settings;
    private static PortraitTelemetrySheetController sheet;
    private static MachineViewModeController views;
    private static PerformanceStatSource asked;

    static MobileLayoutPlayTest()
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
                // The model's outside: no cards on show.
                portrait.GetComponentInChildren<ViewModeSegmentedControl>().SelectExterior();
                Next(4.2f);
                break;

            case 1:
                StatRailManagers.Portrait()?.RebuildLayout();
                Expect(views.CurrentMode == MachineViewMode.FullModel && !views.ShowsTelemetry,
                    "the model starts on its outside, where no cards show");
                ask.SetOpen(true);
                Next(0.8f);
                break;

            case 2:
            {
                Expect(ask.IsOpen && ask.IsTab, "ASK opens the Ask tab");
                RectTransform rule = ask.Window.Find("Divider") as RectTransform;
                RectTransform composerRule = ask.Window.Find("Composer/Divider") as RectTransform;
                float above = ScreenRect((RectTransform)modeSwitch.transform).yMin - ScreenRect(rule).yMax;
                float below = ScreenRect(composerRule).yMin - ScreenRect((RectTransform)ask.Field.transform).yMax;
                Expect(rule != null && rule.gameObject.activeInHierarchy && Mathf.Abs(above - below) < 3f,
                    $"the tab's rule is as far under the switch as the field is under its rule ({above:0} / {below:0} px)");
                Capture("01-ask-tab");
                ask.Field.text = "Why is belt slip red?";
                ask.Field.onSubmit.Invoke(ask.Field.text);
                Next(1.6f);
                break;
            }

            case 3:
                Expect(ask.Evidence.Count > 0, "the answer shows the reading it rests on");
                Capture("02-ask-answer");
                asked = Object.FindAnyObjectByType<TwinAssistant>().TryGetSource("FEED-01.SLIP", out PerformanceStatSource s) ? s : null;
                ask.Evidence[0].onClick.Invoke();
                // The view changes inside, then the card focuses.
                Next(4f);
                break;

            case 4:
            {
                Expect(!ask.IsOpen, "pressing the reading leaves the Ask tab");
                Expect(views.CurrentMode == MachineViewMode.CrossSection,
                    $"pressing the reading goes inside the machine, where the cards are ({views.CurrentMode})");
                Expect(CardFocus.Focused != null && CardFocus.Focused.BoundSource == asked,
                    "and focuses the reading's card");
                Capture("03-reading-shown");
                if (CardFocus.Focused != null)
                    CardFocus.Press(CardFocus.Focused, CardPressKind.Touch);
                Next(0.8f);
                break;
            }

            case 5:
            {
                Expect(sheet.IsOpen, "a second tap on the focused card opens the sheet");
                RectTransform layer = (RectTransform)sheet.transform;
                float tabScale = layer.rect.width / 540f;
                float scale = sheet.Panel.RectTransform.localScale.x;
                Rect panelRect = ScreenRect(sheet.Panel.RectTransform);
                Rect barRect = ScreenRect((RectTransform)modeSwitch.transform.parent);
                float share = panelRect.height / Mathf.Max(1f, barRect.yMin - panelRect.yMin);
                Expect(Mathf.Abs(scale - tabScale) < 0.01f || share > 0.66f,
                    $"the sheet is drawn at the tabs' scale ({scale:0.00} / {tabScale:0.00})");
                Expect(share <= 0.67f, $"the sheet takes at most two thirds of the room under the bar ({share:0.00})");
                Expect(panelRect.xMin <= 1f && panelRect.xMax >= Width - 1f, "the sheet runs the screen's width");
                Graphic frame = sheet.Panel.transform.Find("Background")?.GetComponent<Graphic>();
                Expect(frame != null && frame.color.a < 0.01f, "the floating panel's frame and glow are gone");
                RectTransform fill = layer.Find("Sheet Backdrop") as RectTransform;
                Rect fillRect = fill != null ? ScreenRect(fill) : default;
                Expect(fill != null && fillRect.xMin <= 0.5f && fillRect.xMax >= Width - 0.5f && fillRect.yMin <= 0.5f &&
                       Mathf.Abs(fillRect.yMax - panelRect.yMax) < 2f,
                    $"an opaque fill runs from the sheet's top past the screen's sides and bottom ({fillRect})");
                Capture("04-telemetry-sheet");
                sheet.Close();
                modeSwitch.SelectService();
                Next(1.2f);
                break;
            }

            case 6:
            {
                ServicePanelView panel = portrait.GetComponentInChildren<ServicePanelView>();
                Graphic sheetFrame = panel.transform.Find("Backdrop").GetComponent<Graphic>();
                Expect(sheetFrame.color.a < 0.01f && sheetFrame.raycastTarget,
                    "the Service tab draws no line of its own under the switch, and still takes the pointer");
                Capture("05-service");
                modeSwitch.SelectTelemetry();
                settings.SetOpen(true);
                Next(0.8f);
                break;
            }

            case 7:
            {
                Rect modal = ScreenRect(settings.Modal);
                Expect(modal.xMin <= 0.5f && modal.xMax >= Width - 0.5f && modal.yMin <= 0.5f && modal.yMax >= Height - 0.5f,
                    "the settings are a page of their own, the whole screen");
                Button back = Find<Button>(settings.Modal, "Back");
                Expect(back != null && ScreenRect((RectTransform)back.transform).yMax <= Height * (1f - PhoneInsets.x) + 1f,
                    "the back arrow sits under the status bar");
                Expect(back != null && ScreenRect((RectTransform)back.transform).height / PixelsPerPoint >= 42f,
                    "the back arrow is a thumb's size");
                List<TMP_Text> about = settings.Modal.GetComponentsInChildren<TMP_Text>(true).Where(x => x.name == "About").ToList();
                Expect(about.Count >= 13 && about.All(x => x.gameObject.activeInHierarchy && x.text.Length > 10),
                    $"every setting's description is written under it ({about.Count})");
                Expect(settings.Modal.GetComponentsInChildren<TMP_Text>(true).Any(x => x.text == "CAMERA"),
                    "every group is in the one list");
                foreach (Selectable control in settings.Modal.GetComponentsInChildren<Selectable>())
                {
                    if (control is Toggle)
                        continue;
                    // The touch area: the control's rect and any padding its graphic adds around it.
                    RectTransform rect = (RectTransform)control.transform;
                    Vector4 pad = control.targetGraphic != null && control.targetGraphic.transform == rect
                        ? control.targetGraphic.raycastPadding
                        : rect.TryGetComponent(out Graphic own) ? own.raycastPadding : Vector4.zero;
                    float local = Mathf.Max(1f, rect.rect.height);
                    float points = ScreenRect(rect).height * (local - pad.y - pad.w) / local / PixelsPerPoint;
                    if (points < 40f)
                        Expect(false, $"{control.name} is {points:0} points tall, too small for a thumb");
                }
                Transform contrast = Find<Transform>(settings.Modal, "High contrast");
                Button line = contrast != null ? contrast.Find("Title Line")?.GetComponent<Button>() : null;
                Expect(line != null, "a toggle's row is a button");
                line?.onClick.Invoke();
                Expect(DigitalTwinSettings.HighContrast, "pressing the toggle's row turns it on");
                DigitalTwinSettings.HighContrast = false;
                Capture("06-settings-top");
                Next(0.4f);
                break;
            }

            case 8:
                settings.Modal.GetComponentInChildren<ScrollRect>().verticalNormalizedPosition = 0f;
                Next(0.4f);
                break;

            case 9:
            {
                Capture("07-settings-bottom");
                Expect(Find<Button>(settings.Modal, "Reset") != null, "Reset closes the list");
                settings.SetOpen(false);
                CardFocus.Release();
                StatRailManager rails = StatRailManagers.Portrait();
                Expect(rails != null && rails.RailsPerSide == 1,
                    $"by default a phone runs one row a side, two in all (now {rails?.RailsPerSide})");
                // The most rails a viewer can ask for: a phone still runs two a
                // side, and the leader-line steps below need the inner rows.
                DigitalTwinSettings.RailsPerSide = DigitalTwinSettings.MaximumRailsPerSide;
                // The shell is off in this test: hand the rails the phone's
                // profile as it would.
                if (rails != null)
                {
                    rails.SetLayoutProfile(Object.FindAnyObjectByType<TelemetryLayoutSettings>().Resolve(true, true));
                    rails.RequestRebuild();
                }
                Next(1.5f);
                break;
            }

            case 10:
            {
                StatRailManager rails = StatRailManagers.Portrait();
                Expect(rails != null && rails.RailsPerSide == TelemetryLayoutSettings.MaximumMobileRailsPerSide,
                    $"asked for {DigitalTwinSettings.MaximumRailsPerSide} a side, a phone runs " +
                    $"{TelemetryLayoutSettings.MaximumMobileRailsPerSide} (now {rails?.RailsPerSide})");
                // An outer row's card whose resting line runs across a card on an inner row.
                lineCard = Cards().Where(c => c.RailSide == StatRailSide.Top)
                    .OrderByDescending(c => ScreenRect((RectTransform)c.transform).yMax)
                    .FirstOrDefault(c => LineCrossesCard(c) != null);
                Expect(lineCard != null, "an outer card's resting line runs across an inner card");
                Capture("08-resting-lines");
                Expect(RestingLineAlpha() >= 0.1f,
                    $"resting lines clear of other cards stay faintly drawn ({RestingLineAlpha():0.00})");
                if (lineCard != null)
                    CardFocus.Press(lineCard, CardPressKind.Touch);
                // The inner cards glide aside.
                Next(2f);
                break;
            }

            case 11:
            {
                PerformanceStatCardView blocking = lineCard != null ? LineCrossesCard(lineCard) : null;
                Expect(lineCard != null && lineCard.IsFocused && blocking == null,
                    $"the inner cards step aside for the focused card's line ({(blocking != null ? blocking.BoundSource?.MetricName : "clear")})");
                Capture("09-focused-line-clear");
                CardFocus.Release();
                Finish(failures == 0 && errors.Count == 0 ? "passed" : "failed");
                break;
            }
        }
    }

    // ---- leader lines --------------------------------------------------------------

    private static PerformanceStatCardView lineCard;
    private static readonly Vector3[] LinePath = new Vector3[3];

    /// <summary>The portrait layout's cards on show.</summary>
    private static IEnumerable<PerformanceStatCardView> Cards() =>
        portrait.GetComponentsInChildren<PerformanceStatCardView>()
            .Where(c => c.BoundSource != null && (!c.TryGetComponent(out CanvasGroup g) || g.alpha > 0.05f));

    /// <summary>A card on show the card's line runs through, more than grazing it; null when none.</summary>
    private static PerformanceStatCardView LineCrossesCard(PerformanceStatCardView card)
    {
        if (!StatLeaderLineView.TryGetPath(card, LinePath))
            return null;
        Vector2 a = RectTransformUtility.WorldToScreenPoint(uiCamera, LinePath[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(uiCamera, LinePath[1]);
        Vector2 c = RectTransformUtility.WorldToScreenPoint(uiCamera, LinePath[2]);
        foreach (PerformanceStatCardView other in Cards())
        {
            if (other == card)
                continue;
            Rect r = ScreenRect((RectTransform)other.transform);
            Rect inner = Rect.MinMaxRect(r.xMin + 8f, r.yMin + 8f, r.xMax - 8f, r.yMax - 8f);
            if (Hits(a, b, inner) || Hits(b, c, inner))
                return other;
        }
        return null;
    }

    private static bool Hits(Vector2 from, Vector2 to, Rect rect)
    {
        for (int i = 0; i <= 64; i++)
        {
            if (rect.Contains(Vector2.Lerp(from, to, i / 64f)))
                return true;
        }
        return false;
    }

    /// <summary>The strongest line segment drawn by a resting line that crosses no card.</summary>
    private static float RestingLineAlpha()
    {
        float best = 0f;
        foreach (StatLeaderLineView line in Object.FindObjectsByType<StatLeaderLineView>())
        {
            if (line.Card == null || line.Card.IsFocused || line.Card.VisualState != StatVisualState.Normal ||
                !line.Card.transform.IsChildOf(portrait.transform) || LineCrossesCard(line.Card) != null)
                continue;
            foreach (Image image in line.GetComponentsInChildren<Image>())
            {
                if (!image.name.Contains("Anchor") && !image.name.Contains("Connector"))
                    best = Mathf.Max(best, image.color.a);
            }
        }
        return best;
    }

    // ---- the scene ---------------------------------------------------------------

    private static T Find<T>(Transform root, string name) where T : Component =>
        root.GetComponentsInChildren<T>(true).FirstOrDefault(c => c.name == name);

    private static void Prepare()
    {
        Application.logMessageReceived += RecordError;
        Object.FindAnyObjectByType<ResponsiveLayoutShell>().enabled = false;
        portrait = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .First(t => t.name == "Portrait layout").gameObject;
        GameObject wide = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .First(t => t.name == "Wide layout").gameObject;
        wide.SetActive(false);
        portrait.SetActive(true);
        DeviceSafeArea.EditorInsets = PhoneInsets;

        CanvasScaler scaler = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas")
            .GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;

        modeSwitch = portrait.GetComponentInChildren<DigitalTwinModeSwitch>();
        ask = Object.FindAnyObjectByType<AskTwinWindow>();
        settings = Object.FindAnyObjectByType<SettingsPanel>();
        sheet = portrait.GetComponentInChildren<PortraitTelemetrySheetController>();
        views = Object.FindAnyObjectByType<MachineViewModeController>();

        mainCamera = Camera.main;
        mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData mainData))
            mainData.renderPostProcessing = false;
        uiCamera = new GameObject("Mobile Test UI Camera").AddComponent<Camera>();
        uiCamera.enabled = false;
        uiCamera.clearFlags = CameraClearFlags.SolidColor;
        uiCamera.backgroundColor = Color.clear;
        uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
        mainCamera.targetTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        uiCamera.targetTexture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        // Overlay canvases are not part of a camera render: every canvas is
        // drawn by the UI camera, in its sorting order.
        Canvas main = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas");
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include).Where(c => c.isRootCanvas))
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = uiCamera;
            canvas.planeDistance = canvas == main ? 10f : 5f;
            canvas.gameObject.layer = LayerMask.NameToLayer("UI");
            foreach (Transform child in canvas.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = LayerMask.NameToLayer("UI");
        }
        Canvas.ForceUpdateCanvases();
    }

    private static Rect ScreenRect(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 a = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
        Vector2 b = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
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

    private static void RecordError(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message);
    }

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            failures++;
        Debug.Log($"MOBILE_LAYOUT_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void Finish(string result)
    {
        DigitalTwinSettings.ResetToDefaults();
        DeviceSafeArea.EditorInsets = null;
        foreach (string error in errors)
            Debug.Log($"MOBILE_LAYOUT_TEST error logged: {error}");
        Application.logMessageReceived -= RecordError;
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"MOBILE_LAYOUT_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
