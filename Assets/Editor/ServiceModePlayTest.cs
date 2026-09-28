using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Batch-only play test of the service view in the wide layout. Switches to
/// Service and checks that every telemetry element is hidden and inert, the
/// panel lists every plan soonest first, the parts due get pins, the model is
/// moved beside the panel, and hovering a row pins and outlines its part. Then
/// switches back and checks telemetry returns exactly as it was. Saves a
/// 1920x1080 capture of each step, UI included, to Logs/ServiceModeTest.
///
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class ServiceModePlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/ServiceModeTest";
    private const string ActiveKey = "ServiceModePlayTest.Active";
    private const string StartKey = "ServiceModePlayTest.Start";
    private const string HoverPart = "Drive Roller";

    private static int step;
    private static float stepTime;
    private static int failures;
    private static Camera mainCamera;
    private static Camera uiCamera;
    private static ServiceModeController controller;
    private static DigitalTwinModeSwitch modeSwitch;
    private static ServicePanelView panel;
    private static ServicePinLayer pinLayer;
    private static TelemetryServiceSchedule schedule;
    private static int telemetryCards;
    private static ServiceHoverTarget hovered;

    static ServiceModePlayTest()
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
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f) > 240f)
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
                Object.FindAnyObjectByType<ViewModeSegmentedControl>().SelectSection();
                Next(4.2f);
                break;

            case 1:
                telemetryCards = VisibleCards();
                Expect(controller.CurrentMode == DigitalTwinMode.Telemetry, "the app starts in Telemetry");
                Expect(telemetryCards > 0, $"telemetry shows its cards ({telemetryCards})");
                Expect(TelemetryGroups().All(g => g.alpha > 0.99f && g.blocksRaycasts || !g.gameObject.activeInHierarchy),
                    "every telemetry element is visible");
                Expect(ServiceGroups().All(g => g.alpha < 0.01f && !g.blocksRaycasts), "the service view is hidden");
                Expect(!modeSwitch.TelemetryButton.interactable && modeSwitch.ServiceButton.interactable,
                    "the selected segment is locked, the other can be pressed");
                Capture("01-telemetry");
                modeSwitch.ServiceButton.onClick.Invoke();
                Next(0.15f);
                break;

            case 2:
                Capture("02-switching");
                Next(0.6f);
                break;

            case 3:
                Capture("03-service");
                CheckService();
                hovered = Object.FindObjectsByType<ServiceHoverTarget>()
                    .FirstOrDefault(target => target.Part != null && target.Part.name == HoverPart &&
                                              target.transform.parent.name == "Rows");
                Expect(hovered != null, $"the '{HoverPart}' row exists");
                if (hovered != null)
                    Pointer(hovered.gameObject, ExecuteEvents.pointerEnterHandler);
                Next(0.4f);
                break;

            case 4:
                Capture("04-hover");
                Expect(panel.HoveredPart != null && panel.HoveredPart.name == HoverPart, "hovering a row marks its part");
                Expect(ActivePins() == panel.AttentionCount + 1, "the hovered part gets a pin too");
                Expect(SelectionOutline.Targets.Count > 0 && SelectionOutline.Strength > 0.9f, "the hovered part is outlined");
                if (hovered != null)
                    Pointer(hovered.gameObject, ExecuteEvents.pointerExitHandler);
                modeSwitch.TelemetryButton.onClick.Invoke();
                Next(0.8f);
                break;

            case 5:
                Capture("05-back-to-telemetry");
                Expect(controller.CurrentMode == DigitalTwinMode.Telemetry, "Telemetry is selected again");
                Expect(TelemetryGroups().All(g => g.alpha > 0.99f && g.blocksRaycasts || !g.gameObject.activeInHierarchy),
                    "every telemetry element is back");
                Expect(ServiceGroups().All(g => g.alpha < 0.01f && !g.blocksRaycasts), "the service view is hidden again");
                Expect(Mathf.Abs(mainCamera.projectionMatrix.m02) < 1e-4f, "the model is centred again");
                Expect(ActivePins() == 0, "no pins in Telemetry");
                Expect(SelectionOutline.Strength <= 0f, "the service outline is gone");
                int cards = VisibleCards();
                Expect(cards == telemetryCards, $"the same cards are back ({cards} of {telemetryCards})");
                Finish(failures == 0 ? "passed" : $"failed={failures}");
                break;
        }
    }

    private static void CheckService()
    {
        Expect(controller.CurrentMode == DigitalTwinMode.Service, "Service is selected");
        Expect(TelemetryGroups().All(g => g.alpha < 0.01f && !g.blocksRaycasts), "every telemetry element is hidden and ignores the pointer");
        Expect(ServiceGroups().All(g => g.alpha > 0.99f), "the service view is shown");
        Expect(modeSwitch.TelemetryButton.interactable && !modeSwitch.ServiceButton.interactable, "the switch follows the mode");

        int plans = schedule.Plans.Count(plan => plan.part != null);
        Expect(panel.Items.Count == plans, $"the panel lists every plan ({panel.Items.Count} of {plans})");
        bool sorted = true;
        for (int i = 1; i < panel.Items.Count; i++)
            sorted &= panel.Items[i - 1].HoursLeft <= panel.Items[i].HoursLeft;
        Expect(sorted, "the list is soonest first");

        int due = panel.Items.Count(item => item.State != ServiceState.OnTrack);
        Expect(panel.AttentionCount == Mathf.Min(due, 3) && due > 0, $"Needs Attention lists the parts due ({panel.AttentionCount}, {due} due)");
        foreach (ServiceItemStatus item in panel.Items.Take(4))
            Debug.Log($"SERVICE_MODE_TEST row '{item.Part.DisplayName}' {item.Task} left={item.HoursLeft:0} state={item.State}");

        Expect(ActivePins() == panel.AttentionCount, $"a pin on each part due ({ActivePins()})");
        Expect(mainCamera.projectionMatrix.m02 > 0.2f, $"the model moved beside the panel (shift {mainCamera.projectionMatrix.m02:0.00})");
        Expect(VisibleCards() == telemetryCards, "the telemetry cards are kept, only hidden");
    }

    private static CanvasGroup[] TelemetryGroups() => Groups("telemetryGroups");

    private static CanvasGroup[] ServiceGroups() => Groups("serviceGroups");

    private static CanvasGroup[] Groups(string field)
    {
        SerializedProperty array = new SerializedObject(controller).FindProperty(field);
        return Enumerable.Range(0, array.arraySize)
            .Select(i => array.GetArrayElementAtIndex(i).objectReferenceValue as CanvasGroup)
            .Where(group => group != null)
            .ToArray();
    }

    private static int VisibleCards() =>
        Object.FindObjectsByType<PerformanceStatCardView>().Count(card => card.isActiveAndEnabled);

    private static int ActivePins() =>
        pinLayer.transform.Cast<Transform>().Count(pin => pin.gameObject.activeSelf);

    private static void Pointer<T>(GameObject target, ExecuteEvents.EventFunction<T> handler)
        where T : IEventSystemHandler
    {
        ExtendedPointerEventData data = new(EventSystem.current) { pointerType = UIPointerType.MouseOrPen };
        ExecuteEvents.Execute(target, data, handler);
    }

    private static void Prepare()
    {
        controller = Object.FindAnyObjectByType<ServiceModeController>();
        modeSwitch = Object.FindAnyObjectByType<DigitalTwinModeSwitch>();
        panel = Object.FindAnyObjectByType<ServicePanelView>();
        pinLayer = Object.FindAnyObjectByType<ServicePinLayer>();
        schedule = Object.FindAnyObjectByType<TelemetryServiceSchedule>();

        // Screen-space-overlay UI is not part of a camera render. The model
        // and the UI are drawn into separate targets and composited, so the
        // model camera's off-centre projection, which the service view sets,
        // moves only the model, as it does on screen. The UI shaders write
        // premultiplied alpha, so the composite is exact.
        mainCamera = Camera.main;
        mainCamera.targetTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData mainData))
            mainData.renderPostProcessing = false; // UI colours as authored

        uiCamera = new GameObject("Service Test UI Camera").AddComponent<Camera>();
        uiCamera.enabled = false;
        uiCamera.targetTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        uiCamera.clearFlags = CameraClearFlags.SolidColor;
        uiCamera.backgroundColor = Color.clear;
        uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");

        Canvas canvas = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas");
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera;
        canvas.planeDistance = 10f;

        // The rails lay out from a start-up coroutine that waits for the end of
        // a frame, which batch play mode never presents; build them directly,
        // as the detail panel test does.
        Object.FindAnyObjectByType<WideStatRailManager>()?.RebuildLayout();
    }

    private static void Capture(string name)
    {
        // The model camera renders every frame; the UI camera on demand.
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

        Texture2D image = new(1920, 1080, TextureFormat.RGBA32, false);
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
        Debug.Log($"SERVICE_MODE_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"SERVICE_MODE_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
