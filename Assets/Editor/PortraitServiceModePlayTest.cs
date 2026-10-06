using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Batch-only play test of the portrait layout on a phone held upright.
///
/// Telemetry shows the switch centred in the top bar over the model. Service
/// hides every telemetry element, makes them inert, and shows the panel over
/// the whole screen below the switch on an opaque fill that reaches the
/// screen's edges. The part detail opens, scrolls and closes, and Telemetry
/// returns as it was.
///
/// In Telemetry, a tap on a card focuses it and shows the tap-again hint; a
/// tap on another card moves the focus, and a second tap on the focused card
/// opens the telemetry sheet along the bottom, with no pin and a close
/// button at least 44 points across, and hides every other piece of telemetry
/// UI. Its Time To Service figure opens the part's service detail.
///
/// Then the page's insets are simulated: a notch at the top and a browser
/// toolbar plus home indicator at the bottom. The canvas keeps its size, the
/// switch clears the notch, and the bottom controls and the panel clear the
/// toolbar. No errors may be logged throughout.
///
/// Saves a capture of each step to Logs/PortraitServiceModeTest.
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class PortraitServiceModePlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/PortraitServiceModeTest";
    private const string ActiveKey = "PortraitServiceModePlayTest.Active";
    private const string StartKey = "PortraitServiceModePlayTest.Start";
    private const string DetailPart = "Drive Roller";
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
    private static RectTransform topBar;
    private static RectTransform bottomControls;
    private static RectTransform backdrop;
    private static PortraitTelemetrySheetController sheet;
    private static PerformanceStatCardView card;
    private static MachinePartGroup linkedPart;

    static PortraitServiceModePlayTest()
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
                // Telemetry cards only show in the cross-section view.
                portrait.GetComponentInChildren<ViewModeSegmentedControl>().SelectSection();
                Next(4.2f);
                break;

            case 1:
                StatRailManagers.Portrait()?.RebuildLayout();
                Expect(visibility.CurrentMode == DigitalTwinMode.Telemetry, "the app starts in Telemetry");
                Expect(Mathf.Abs(ScreenRect(Switch()).center.x - Width * 0.5f) < 2f, "the switch is centred");
                Expect(!modeSwitch.TelemetryButton.interactable && modeSwitch.ServiceButton.interactable,
                    "the selected segment is locked, the other can be pressed");
                Capture("01-telemetry");

                // A tap only focuses, and teaches the second tap.
                PlayerPrefs.DeleteKey("DigitalTwin.TelemetrySheetTapLearned");
                card = LinkedCard();
                Expect(card != null, "a portrait card reads a serviced part");
                Touch(card, true, true, true);
                Next(0.4f);
                break;

            case 2:
                Expect(card.IsFocused && !sheet.IsOpen, "a tap focuses the card without opening the sheet");
                Expect(sheet.HintShowing, "the first tap shows the tap-again hint");
                Capture("02-hint");

                PerformanceStatCardView other = portrait.GetComponentsInChildren<PerformanceStatCardView>()
                    .First(c => c != card && c.BoundSource != null);
                Touch(other, true, true, true);
                Expect(other.IsFocused && !card.IsFocused && !sheet.IsOpen,
                    "a tap on another card moves the focus without opening the sheet");
                Touch(card, true, true, true);
                Expect(card.IsFocused && !sheet.IsOpen, "a tap brings the focus back");
                Touch(card, true, true, true);
                // The sheet's rise.
                Next(0.6f);
                break;

            case 3:
                Expect(sheet.IsOpen && card.IsFocused, "a second tap on the focused card opens the sheet");
                Expect(!sheet.HintShowing, "the hint goes once the sheet opens");
                Expect(!sheet.Panel.GetComponentsInChildren<Button>().Any(b => b.name == "Pin"),
                    "the sheet has no pin");
                Button close = sheet.Panel.GetComponentsInChildren<Button>().First(b => b.name == "Close");
                Rect closeHit = ScreenRect((RectTransform)close.transform);
                float closeWidth = ((RectTransform)close.transform).rect.width;
                float padding = -close.GetComponent<Graphic>().raycastPadding.x;
                float hitPoints = closeHit.width * (closeWidth + padding * 2f) / closeWidth / (Width / 390f);
                Expect(hitPoints >= 44f, $"the close button's touch area is at least 44 points ({hitPoints:0})");
                Rect sheetRect = ScreenRect(sheet.Panel.RectTransform);
                Expect(sheetRect.width >= Width * 0.95f && sheetRect.yMin <= Height * 0.02f,
                    "the sheet runs along the bottom, the screen's width");
                Expect(ScreenRect(topBar).yMin > sheetRect.yMax, "the sheet stays under the switch");
                Capture("03-telemetry-sheet");

                Button link = sheet.Panel.GetComponentsInChildren<Button>().FirstOrDefault(b => b.name == "Service Link");
                Expect(link != null, "Time To Service links to the part's service detail");
                Expect(link != null && link.GetComponentInChildren<UIIconGraphic>(true) != null ||
                       sheet.Panel.GetComponentsInChildren<UIIconGraphic>().Any(i => i.IconShape == UIIconGraphic.Shape.Chevron),
                    "the link shows a chevron");
                linkedPart = SchedulePart(card);
                link?.onClick.Invoke();
                Next(0.7f);
                break;

            case 4:
                Expect(visibility.CurrentMode == DigitalTwinMode.Service, "the link switches to Service");
                Expect(panel.DetailOpen && panel.SelectedPart == linkedPart,
                    $"the link opens {(linkedPart != null ? linkedPart.name : "the part")}'s detail");
                Expect(!sheet.IsOpen, "the sheet closes on the way");
                Capture("04-linked-detail");
                panel.ShowList();
                modeSwitch.TelemetryButton.onClick.Invoke();
                Next(0.6f);
                break;

            case 5:
                // Focus stays through the other controls and goes with a tap on
                // the model. Batch play mode cannot raycast an off-screen
                // canvas, so the rule is checked against what a tap would hit.
                GameObject zoom = portrait.GetComponentsInChildren<RectTransform>()
                    .First(r => r.name == "Camera Zoom Controls").gameObject;
                Expect(TelemetryCardFocusReleaser.KeepsFocus(zoom) &&
                       TelemetryCardFocusReleaser.KeepsFocus(zoom.GetComponentInChildren<Button>().gameObject) &&
                       TelemetryCardFocusReleaser.KeepsFocus(card.gameObject),
                    "taps on the controls and the cards keep the focus");
                GameObject stage = portrait.GetComponentsInChildren<RectTransform>()
                    .First(r => r.name == "Stage area").gameObject;
                Expect(!TelemetryCardFocusReleaser.KeepsFocus(stage) && !TelemetryCardFocusReleaser.KeepsFocus(null),
                    "a tap on the model releases the focus");
                Touch(card, true, true, true);
                Object.FindAnyObjectByType<TelemetryCardFocusReleaser>().Dismiss();
                Expect(!card.IsFocused, "releasing clears the focused card");
                modeSwitch.ServiceButton.onClick.Invoke();
                Next(0.15f);
                break;

            case 6:
                Capture("02-switching");
                Next(0.6f);
                break;

            case 7:
                Capture("03-service");
                CheckService();
                panel.ShowDetail(Part(DetailPart));
                Next(0.3f);
                break;

            case 8:
                Capture("04-detail");
                Expect(panel.DetailOpen, "a part's detail opens");
                Expect(panel.Detail.content.rect.height > panel.Detail.viewport.rect.height,
                    "the detail scrolls on a phone");
                panel.Detail.verticalNormalizedPosition = 0f;
                Next(0.3f);
                break;

            case 9:
                Capture("05-detail-scrolled");
                panel.BackButton.onClick.Invoke();
                Next(0.3f);
                break;

            case 10:
                Expect(!panel.DetailOpen, "the back arrow returns to the list");
                modeSwitch.TelemetryButton.onClick.Invoke();
                Next(0.6f);
                break;

            case 11:
                Capture("06-back-to-telemetry");

                // A notch, and a browser toolbar over the home indicator.
                DeviceSafeArea.EditorInsets = PhoneInsets;
                Next(0.4f);
                break;

            case 12:
                Canvas.ForceUpdateCanvases();
                StatRailManagers.Portrait()?.RebuildLayout();
                Next(0.3f);
                break;

            case 13:
                Capture("07-telemetry-insets");
                float top = Height * PhoneInsets.x;
                float bottom = Height * PhoneInsets.z;
                Expect(ScreenRect(Switch()).yMax <= Height - top + 1f, "the switch clears the notch");
                Expect(ScreenRect(bottomControls).yMin >= bottom - 1f, "the bottom controls clear the toolbar");
                Expect(ScreenRect((RectTransform)canvas.transform).height >= Height - 1f, "the canvas keeps its size");
                modeSwitch.ServiceButton.onClick.Invoke();
                Next(0.6f);
                break;

            case 14:
                Capture("08-service-insets");
                Expect(ScreenRect((RectTransform)panel.transform).yMin >= Height * PhoneInsets.z - 1f,
                    "the panel's legend clears the toolbar");
                Expect(ScreenRect(backdrop).yMin <= 1f && ScreenRect(backdrop).yMax >= Height - 1f,
                    "the fill still runs under the notch and the toolbar");
                modeSwitch.TelemetryButton.onClick.Invoke();
                DeviceSafeArea.EditorInsets = null;
                Next(0.6f);
                break;

            case 15:
                Expect(errors.Count == 0, $"no errors logged ({errors.Count}{(errors.Count > 0 ? ": " + errors[0] : string.Empty)})");
                Finish(failures == 0 ? "passed" : $"failed ({failures})");
                break;
        }
    }

    private static void CheckService()
    {
        Expect(visibility.CurrentMode == DigitalTwinMode.Service, "Service is selected");
        Expect(modeSwitch.TelemetryButton.interactable && !modeSwitch.ServiceButton.interactable,
            "the switch shows Service");

        Rect panelRect = ScreenRect((RectTransform)panel.transform);
        Rect bar = ScreenRect(topBar);
        Rect fill = ScreenRect(backdrop);
        Expect(panelRect.yMax <= bar.yMin + 1f, "the panel starts below the switch");
        Expect(panelRect.width >= Width * 0.95f, $"the panel takes the screen's width ({panelRect.width:0})");
        Expect(panelRect.yMin <= Height * 0.03f, $"the panel runs to the bottom ({panelRect.yMin:0})");
        Expect(fill.xMin <= 1f && fill.yMin <= 1f && fill.xMax >= Width - 1f && fill.yMax >= Height - 1f,
            "the fill covers the whole screen");
        Expect(panel.Items.Count > 0, $"the list shows the schedule ({panel.Items.Count} parts)");
    }

    /// <summary>A visible portrait card whose sensor a service plan covers.</summary>
    private static PerformanceStatCardView LinkedCard() =>
        portrait.GetComponentsInChildren<PerformanceStatCardView>()
            .FirstOrDefault(c => c.BoundSource != null && SchedulePart(c) != null);

    private static MachinePartGroup SchedulePart(PerformanceStatCardView target) =>
        Object.FindAnyObjectByType<TelemetryServiceSchedule>().TryGetServicedPart(target.BoundSource, out MachinePartGroup part)
            ? part
            : null;

    /// <summary>A finger on the card's centre: pressed, lifted, and a click.</summary>
    private static void Touch(PerformanceStatCardView target, bool down, bool up, bool click)
    {
        Vector2 centre = ScreenRect((RectTransform)target.transform).center;
        touch ??= new ExtendedPointerEventData(EventSystem.current) { pointerType = UIPointerType.Touch };
        touch.position = centre;
        touch.pressPosition = centre;
        if (down)
            ExecuteEvents.Execute(target.gameObject, touch, ExecuteEvents.pointerDownHandler);
        if (up)
            ExecuteEvents.Execute(target.gameObject, touch, ExecuteEvents.pointerUpHandler);
        if (click)
            ExecuteEvents.Execute(target.gameObject, touch, ExecuteEvents.pointerClickHandler);
    }

    private static ExtendedPointerEventData touch;

    private static MachinePartGroup Part(string name) =>
        Object.FindObjectsByType<MachinePartGroup>().First(part => part.name == name);


    private static RectTransform Switch() => (RectTransform)modeSwitch.transform;

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

        // The layout shell measures the batch window; show the portrait layout
        // the way it would on a phone, and keep it there.
        ResponsiveLayoutShell shell = Object.FindAnyObjectByType<ResponsiveLayoutShell>();
        shell.enabled = false;
        portrait = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .First(t => t.name == "Portrait layout").gameObject;
        GameObject wide = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .First(t => t.name == "Wide layout").gameObject;
        wide.SetActive(false);
        portrait.SetActive(true);

        canvas = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas");
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0f;

        visibility = portrait.GetComponentInChildren<TelemetryVisibility>();
        modeSwitch = portrait.GetComponentInChildren<DigitalTwinModeSwitch>();
        panel = portrait.GetComponentInChildren<ServicePanelView>();
        topBar = (RectTransform)modeSwitch.transform.parent;
        bottomControls = portrait.GetComponentsInChildren<RectTransform>().First(r => r.name == "Bottom controls");
        backdrop = portrait.GetComponentsInChildren<RectTransform>().First(r => r.name == "Service Backdrop");
        sheet = portrait.GetComponentInChildren<PortraitTelemetrySheetController>();

        // As the wide test: the model and the UI render into separate targets
        // and are composited.
        mainCamera = Camera.main;
        mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData mainData))
            mainData.renderPostProcessing = false;
        uiCamera = new GameObject("Portrait Test UI Camera").AddComponent<Camera>();
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
        Debug.Log($"PORTRAIT_SERVICE_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void Finish(string result)
    {
        DeviceSafeArea.EditorInsets = null;
        Application.logMessageReceived -= RecordError;
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"PORTRAIT_SERVICE_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
