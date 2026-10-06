using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Batch-only play and stress test of the service view in the wide layout.
///
/// The main path: switches to Service and checks that every telemetry element
/// is hidden and inert, the panel runs down the right of the screen with the
/// viewport's controls beside it, the list is soonest first with a scrollbar
/// that scrolls it, the wheel over the panel leaves the model's zoom alone,
/// the parts due get pins, and the model is moved beside the panel. Hovering a
/// component card shows the hover look, pins and outlines its part and leaves
/// the camera alone; clicking it opens the part's detail, which the back arrow
/// closes. Then switches back and checks telemetry returns exactly as it was.
///
/// Edge cases: a part out of view when its card is clicked pulls the camera
/// back until it fits; a part that goes overdue; a part that leaves the
/// schedule while its detail is open; too few parts to scroll, which gives the
/// cards the scrollbar's room; switching views with the detail open; and a
/// window too narrow for the panel beside the viewport. No errors may be
/// logged throughout.
///
/// Saves a capture of each step, UI included, to Logs/ServiceModeTest.
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
    private const string FarPart = "Conveyor Belts";
    private const string DuePart = "Bearings";

    private static readonly FieldInfo PlansField =
        typeof(TelemetryServiceSchedule).GetField("plans", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo DesiredDistance =
        typeof(OrbitCameraController).GetField("desiredDistance", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int step;
    private static float stepTime;
    private static int failures;
    private static readonly List<string> errors = new();
    private static Camera mainCamera;
    private static Camera uiCamera;
    private static OrbitCameraController orbit;
    private static WideServiceView controller;
    private static TelemetryVisibility visibility;
    private static DigitalTwinModeSwitch modeSwitch;
    private static ServicePanelView panel;
    private static ServicePinLayer pinLayer;
    private static TelemetryServiceSchedule schedule;
    private static List<TelemetryServiceSchedule.ServicePlan> originalPlans;
    private static int telemetryCards;
    private static Rect telemetryContent;
    private static Rect telemetryZoom;
    private static ServiceHoverTarget hovered;
    private static float distanceBefore;

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
                Object.FindAnyObjectByType<ViewModeSegmentedControl>().SelectSection();
                Next(4.2f);
                break;

            case 1:
                telemetryCards = VisibleCards();
                telemetryContent = ScreenRect(ContentArea());
                telemetryZoom = ScreenRect(ZoomControls());
                Expect(visibility.CurrentMode == DigitalTwinMode.Telemetry, "the app starts in Telemetry");
                Expect(telemetryCards > 0, $"telemetry shows its cards ({telemetryCards})");
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
                // Back to the top, where the hovered card is in view.
                panel.List.verticalNormalizedPosition = 1f;
                hovered = Card(HoverPart);
                Expect(hovered != null, $"the '{HoverPart}' card exists");
                distanceBefore = Distance();
                if (hovered != null)
                    Pointer(hovered.gameObject, ExecuteEvents.pointerEnterHandler);
                Next(0.4f);
                break;

            case 4:
                Capture("04-hover");
                Expect(panel.HoveredPart != null && panel.HoveredPart.name == HoverPart, "hovering a card marks its part");
                Expect(hovered != null && hovered.GetComponent<UIHoverSprite>().IsShowingHover, "the hovered card shows the hover look");
                Expect(ActivePins() == DueCount() + 1, "the hovered part gets a pin too");
                Expect(SelectionOutline.Targets.Count > 0 && SelectionOutline.Strength > 0.9f, "the hovered part is outlined");
                Expect(Mathf.Abs(Distance() - distanceBefore) < 0.01f, "hovering leaves the camera alone");
                if (hovered != null)
                {
                    Pointer(hovered.gameObject, ExecuteEvents.pointerExitHandler);
                    Pointer(hovered.gameObject, ExecuteEvents.pointerClickHandler);
                }
                Next(0.4f);
                break;

            case 5:
                Capture("05-detail");
                Expect(panel.DetailOpen && panel.SelectedPart != null && panel.SelectedPart.name == HoverPart,
                    "clicking a card opens its part's detail");
                Expect(!panel.List.gameObject.activeInHierarchy, "the detail takes the list's place");
                Expect(panel.DetailStateText == "ON TRACK", $"the detail shows the part's state ({panel.DetailStateText})");
                Expect(ActivePins() == DueCount() + 1, "the open part keeps its pin");
                Expect(SelectionOutline.Targets.Count > 0 && SelectionOutline.Strength > 0.9f, "the open part stays outlined");
                CheckGutter(panel.Detail, panel.DetailRightMargin, "detail");
                panel.Detail.verticalNormalizedPosition = 0f;
                Pointer(panel.BackButton.gameObject, ExecuteEvents.pointerEnterHandler);
                Next(0.3f);
                break;

            case 6:
                Capture("06-detail-scrolled");
                Expect(panel.BackButton.GetComponent<UIHoverSprite>().IsShowingHover, "the back arrow shows the hover look");
                Pointer(panel.BackButton.gameObject, ExecuteEvents.pointerExitHandler);
                panel.BackButton.onClick.Invoke();
                Next(0.3f);
                break;

            case 7:
                Expect(!panel.DetailOpen && panel.List.gameObject.activeInHierarchy, "the back arrow returns to the list");
                Expect(panel.List.verticalNormalizedPosition > 0.99f, "the list is still where it was scrolled to");
                // Edge: close in, so a large part is out of view when opened.
                DesiredDistance.SetValue(orbit, orbit.minDistance);
                Next(2f);
                break;

            case 8:
                distanceBefore = Distance();
                MachinePartGroup far = Part(FarPart);
                Expect(far != null && !controller.IsPartInView(far), $"'{FarPart}' is out of view close in");
                Pointer(Card(FarPart).gameObject, ExecuteEvents.pointerClickHandler);
                Next(2.2f);
                break;

            case 9:
                Capture("09-framed-for-part");
                Expect(Distance() > distanceBefore + 0.5f,
                    $"opening a part out of view pulls the camera back ({distanceBefore:0.0} to {Distance():0.0})");
                Expect(controller.IsPartInView(Part(FarPart)) || Distance() >= orbit.maxDistance - 0.05f,
                    "the part now fits, or the camera is as far back as it goes");
                panel.ShowList();

                // Edge: a part goes overdue.
                SetPlan(DuePart, plan => plan.FindPropertyRelative("hoursSinceService").floatValue = 950f);
                panel.Refresh();
                panel.ShowDetail(Part(DuePart));
                Next(0.3f);
                break;

            case 10:
                Capture("10-overdue");
                Expect(panel.Items[0].Part.name == DuePart && panel.Items[0].State == ServiceState.Overdue,
                    "an overdue part goes to the top of the list");
                Expect(panel.DetailStateText == "OVERDUE", $"its detail says so ({panel.DetailStateText})");

                // Edge: the open part leaves the schedule.
                int before = panel.Items.Count;
                SetPlan(DuePart, plan => plan.FindPropertyRelative("part").objectReferenceValue = null);
                panel.Refresh();
                Expect(!panel.DetailOpen && panel.List.gameObject.activeInHierarchy, "a part leaving the schedule closes its detail");
                Expect(panel.Items.Count == before - 1, "and leaves the list");

                // Edge: few enough parts to fit without scrolling.
                SerializedObject data = new(schedule);
                data.FindProperty("plans").arraySize = 3;
                data.ApplyModifiedPropertiesWithoutUndo();
                panel.Refresh();
                Next(0.3f);
                break;

            case 11:
                Capture("11-few-parts");
                Expect(!panel.Scrollbar.gameObject.activeInHierarchy, "no scrollbar when every card fits");
                Expect(Mathf.Approximately(panel.ListRightMargin, 16f), $"and the cards take its room ({panel.ListRightMargin})");

                RestoreSchedule();
                panel.Refresh();
                Expect(panel.Items.Count == originalPlans.Count, $"the full schedule is back ({panel.Items.Count})");

                // Edge: switch views with a detail open.
                panel.ShowDetail(Part(HoverPart));
                modeSwitch.TelemetryButton.onClick.Invoke();
                Next(0.8f);
                break;

            case 12:
                modeSwitch.ServiceButton.onClick.Invoke();
                Next(0.8f);
                break;

            case 13:
                Expect(panel.DetailOpen, "the detail is still open on return");
                panel.ShowList();

                // Edge: a window too narrow for the panel beside the viewport.
                Resize(800, 1000);
                Next(0.8f);
                break;

            case 14:
                Capture("14-narrow");
                Rect narrowPanel = ScreenRect((RectTransform)panel.transform);
                Expect(!controller.PanelBesideViewport, "a narrow window puts the panel over the content");
                Expect(narrowPanel.xMin >= 0f && narrowPanel.xMax <= 800.5f && narrowPanel.yMin > 1f,
                    $"and it stays on screen, between the bars ({narrowPanel})");
                Resize(1920, 1080);
                Next(0.8f);
                break;

            case 15:
                Expect(controller.PanelBesideViewport, "the panel goes back beside the viewport when there is room");
                modeSwitch.TelemetryButton.onClick.Invoke();
                Next(0.8f);
                break;

            case 16:
                Capture("16-back-to-telemetry");
                Expect(visibility.CurrentMode == DigitalTwinMode.Telemetry, "Telemetry is selected again");
                Expect(Mathf.Abs(mainCamera.projectionMatrix.m02) < 1e-4f, "the model is centred again");
                Expect(ActivePins() == 0, "no pins in Telemetry");
                Expect(SelectionOutline.Strength <= 0f, "the service outline is gone");
                int cards = VisibleCards();
                Expect(cards == telemetryCards, $"the same cards are back ({cards} of {telemetryCards})");
                Expect(Same(ScreenRect(ZoomControls()), telemetryZoom), "the bottom controls are back in place");
                Expect(Same(ScreenRect(ContentArea()), telemetryContent), "the content area is unchanged");
                foreach (string error in errors.Take(10))
                    Debug.Log("SERVICE_MODE_TEST logged: " + error);
                Expect(errors.Count == 0, $"nothing logged an error ({errors.Count})");
                Finish(failures == 0 ? "passed" : $"failed={failures}");
                break;
        }
    }

    private static void CheckService()
    {
        Expect(visibility.CurrentMode == DigitalTwinMode.Service, "Service is selected");
        Expect(modeSwitch.TelemetryButton.interactable && !modeSwitch.ServiceButton.interactable, "the switch follows the mode");

        int plans = schedule.Plans.Count(plan => plan.part != null);
        Expect(panel.Items.Count == plans, $"the panel lists every plan ({panel.Items.Count} of {plans})");
        bool sorted = true;
        for (int i = 1; i < panel.Items.Count; i++)
            sorted &= panel.Items[i - 1].HoursLeft <= panel.Items[i].HoursLeft;
        Expect(sorted, "the list is soonest first");

        foreach (ServiceItemStatus item in panel.Items.Take(4))
            Debug.Log($"SERVICE_MODE_TEST card '{item.Part.DisplayName}' {item.Task} left={item.HoursLeft:0} state={item.State}");

        int due = DueCount();
        Expect(due > 0 && ActivePins() == due, $"a pin on each part due ({ActivePins()} of {due})");

        // The panel is a column of its own: full height at the right edge,
        // with the viewport's controls left of it and the hidden content
        // area kept at its full width.
        Rect panelRect = ScreenRect((RectTransform)panel.transform);
        Rect zoom = ScreenRect(ZoomControls());
        Expect(controller.PanelBesideViewport, "the panel sits beside the viewport at 1920x1080");
        Expect(panelRect.xMax >= 1919f && panelRect.yMin <= 1f && panelRect.yMax >= 1079f,
            $"the panel runs the full height at the right edge ({panelRect})");
        Expect(zoom.xMax <= panelRect.xMin - 20f && zoom.xMax < telemetryZoom.xMax - 500f,
            $"the zoom controls moved left of the panel ({telemetryZoom.xMax:0} to {zoom.xMax:0}, panel at {panelRect.xMin:0})");
        Expect(Same(ScreenRect(ContentArea()), telemetryContent), "the hidden content area keeps its layout");

        // The cards do not all fit, so the scrollbar shows, and moving it
        // scrolls the list.
        Expect(panel.Scrollbar != null && panel.Scrollbar.gameObject.activeInHierarchy &&
               panel.Scrollbar.size > 0.05f && panel.Scrollbar.size < 1f,
            $"the scrollbar shows the share of the list in view ({panel.Scrollbar?.size:0.00})");
        CheckGutter(panel.List, panel.ListRightMargin, "list");
        if (panel.Scrollbar != null)
        {
            float before = panel.List.verticalNormalizedPosition;
            panel.Scrollbar.value = 0.5f;
            Expect(Mathf.Abs(panel.List.verticalNormalizedPosition - 0.5f) < 0.01f && before > 0.99f,
                "the scrollbar scrolls the list");
        }

        // The panel takes the pointer, which is how the camera tells the wheel
        // over it from the wheel over the model.
        Vector2 inPanel = new(panelRect.center.x, panelRect.center.y);
        Expect(RaycastHitsPanel(inPanel), "the pointer over the panel lands on the panel, not the model");
    }

    /// <summary>The cards keep the scrollbar's room exactly while the view scrolls.</summary>
    private static void CheckGutter(ScrollRect view, float margin, string name)
    {
        bool scrolls = view.content.rect.height > view.viewport.rect.height + 0.5f;
        Expect(Mathf.Approximately(margin, scrolls ? 38f : 16f),
            $"the {name}'s cards leave room for the scrollbar only while it scrolls (scrolls={scrolls}, margin={margin})");
    }

    private static ServiceHoverTarget Card(string part) =>
        Object.FindObjectsByType<ServiceHoverTarget>(FindObjectsInactive.Include)
            .FirstOrDefault(target => target.Part != null && target.Part.name == part);

    private static MachinePartGroup Part(string name) =>
        Object.FindObjectsByType<MachinePartGroup>(FindObjectsInactive.Include).FirstOrDefault(part => part.name == name);

    private static float Distance() => (float)DesiredDistance.GetValue(orbit);

    private static void SetPlan(string part, System.Action<SerializedProperty> change)
    {
        SerializedObject data = new(schedule);
        SerializedProperty plans = data.FindProperty("plans");
        for (int i = 0; i < plans.arraySize; i++)
        {
            SerializedProperty plan = plans.GetArrayElementAtIndex(i);
            Object target = plan.FindPropertyRelative("part").objectReferenceValue;
            if (target != null && target.name == part)
                change(plan);
        }
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void RestoreSchedule() =>
        PlansField.SetValue(schedule, new List<TelemetryServiceSchedule.ServicePlan>(originalPlans));

    private static int DueCount() => panel.Items.Count(item => item.State != ServiceState.OnTrack);

    private static RectTransform ContentArea() =>
        Object.FindObjectsByType<RectTransform>().First(rect => rect.name == "Content area" && rect.parent.parent.name == "Wide layout");

    private static RectTransform ZoomControls() =>
        Object.FindObjectsByType<RectTransform>().First(rect => rect.name == "Camera Zoom Controls" && rect.gameObject.activeInHierarchy);

    private static Rect ScreenRect(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(uiCamera, corners[2]);
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool Same(Rect a, Rect b) =>
        Mathf.Abs(a.xMin - b.xMin) < 1f && Mathf.Abs(a.xMax - b.xMax) < 1f &&
        Mathf.Abs(a.yMin - b.yMin) < 1f && Mathf.Abs(a.yMax - b.yMax) < 1f;

    private static bool RaycastHitsPanel(Vector2 screenPoint)
    {
        List<RaycastResult> hits = new();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screenPoint }, hits);
        return hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(panel.transform);
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
        Application.logMessageReceived += RecordError;
        controller = Object.FindAnyObjectByType<WideServiceView>();
        visibility = controller.Visibility;
        modeSwitch = Object.FindAnyObjectByType<DigitalTwinModeSwitch>();
        panel = Object.FindAnyObjectByType<ServicePanelView>();
        pinLayer = Object.FindAnyObjectByType<ServicePinLayer>();
        schedule = Object.FindAnyObjectByType<TelemetryServiceSchedule>();
        originalPlans = new List<TelemetryServiceSchedule.ServicePlan>(schedule.Plans);

        // Screen-space-overlay UI is not part of a camera render. The model
        // and the UI are drawn into separate targets and composited, so the
        // model camera's off-centre projection, which the service view sets,
        // moves only the model, as it does on screen. The UI shaders write
        // premultiplied alpha, so the composite is exact.
        mainCamera = Camera.main;
        orbit = mainCamera.GetComponent<OrbitCameraController>();
        mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData mainData))
            mainData.renderPostProcessing = false; // UI colours as authored

        uiCamera = new GameObject("Service Test UI Camera").AddComponent<Camera>();
        uiCamera.enabled = false;
        uiCamera.clearFlags = CameraClearFlags.SolidColor;
        uiCamera.backgroundColor = Color.clear;
        uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
        Resize(1920, 1080);

        Canvas canvas = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas");
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera;
        canvas.planeDistance = 10f;

        // The rails lay out from a start-up coroutine that waits for the end of
        // a frame, which batch play mode never presents; build them directly,
        // as the detail panel test does.
        StatRailManagers.Wide()?.RebuildLayout();
    }

    private static void RecordError(string message, string stackTrace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            errors.Add(message);
    }

    /// <summary>Renders the model and the UI at a new size, as a resized window would.</summary>
    private static void Resize(int width, int height)
    {
        mainCamera.targetTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        uiCamera.targetTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
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
        Application.logMessageReceived -= RecordError;
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"SERVICE_MODE_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
