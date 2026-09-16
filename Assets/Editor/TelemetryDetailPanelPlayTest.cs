using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Batch-only play test of the telemetry detail panel and the control hover
/// states. Clicks through the behaviour (open, glide, release on an outside
/// click, pin into a second panel, a camera drag that must change nothing, the
/// range buttons, hover on idle, selected and pressed controls, handing the
/// secondary rails back to cards) and saves a 1920x1080 capture of each step,
/// UI included, to Logs/DetailPanelTest. Exits the editor when done. Never
/// saves the scene.
///
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class TelemetryDetailPanelPlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/DetailPanelTest";
    private const string ActiveKey = "TelemetryDetailPanelPlayTest.Active";
    private const string StartKey = "TelemetryDetailPanelPlayTest.Start";

    // Empty scene over the conveyor, clear of every rail, panel and control.
    private static readonly Vector2 OutsidePoint = new(1200f, 300f);

    private static readonly FieldInfo EmphasisField = typeof(PerformanceStatCardView)
        .GetField("connectionEmphasis", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int step;
    private static float stepTime;
    private static Camera captureCamera;
    private static TelemetryDetailPanelController controller;
    private static TelemetryCardFocusReleaser releaser;
    private static WideStatRailManager rails;
    private static List<PerformanceStatCardView> leftCards = new();
    private static List<PerformanceStatCardView> rightCards = new();
    private static Button xray;
    private static Button section;
    private static int failures;

    // Entering play mode reloads scripts; this re-attaches the test afterwards.
    static TelemetryDetailPanelPlayTest()
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
                PrepareCapture();
                // Telemetry cards only show in the cross-section view.
                Object.FindFirstObjectByType<ViewModeSegmentedControl>().SelectSection();
                Next(4.2f);
                break;

            case 1:
                FindCards();
                Expect(rails.SecondaryRailsReserved && !rails.HasSecondaryRailCards,
                    "the secondary rails are kept for the panel and hold no cards");
                Capture("01-rest");
                Click(leftCards[0]);
                Next(1.2f);
                break;

            case 2:
                Capture("02-open");
                Expect(controller.OpenPanels.Count == 1 && controller.FloatingPanel != null, "one unpinned panel after the first click");
                Click(leftCards[Mathf.Min(3, leftCards.Count - 1)]);
                Next(0.12f);
                break;

            case 3:
                Capture("03-gliding");
                Next(0.9f);
                break;

            case 4:
                Capture("04-glided");
                Expect(controller.OpenPanels.Count == 1, "the click on another card moved the panel instead of opening a second");
                Expect(!leftCards[0].IsFocused && leftCards[Mathf.Min(3, leftCards.Count - 1)].IsFocused,
                    "only the newly clicked card is focused");
                // Queued input events do not reach input actions in this batch
                // play mode, so the gesture is classified directly here. The
                // real click is checked in the browser build instead.
                releaser.HandlePointerGesture(OutsidePoint, OutsidePoint);
                Expect(controller.OpenPanels.Count == 0, "an outside click closed the panel");
                Expect(PerformanceStatCardView.ActiveFocusedCard == null && AllCards().All(card => !card.IsFocused),
                    "an outside click released every card (none left in the focused state)");
                Next(1f);
                break;

            case 5:
                Capture("05-after-dismiss");
                Expect(AllCards().Where(card => card.VisualState is StatVisualState.Normal or StatVisualState.Unavailable)
                        .All(card => (float)EmphasisField.GetValue(card) < 0.05f),
                    "every resting card's trail faded back to rest");
                Click(leftCards[0]);
                Next(0.8f);
                break;

            case 6:
                PressButton(controller.FloatingPanel, "Pin");
                Click(leftCards[1]);
                Next(1.2f);
                break;

            case 7:
                Capture("06-pinned-and-new");
                Expect(controller.OpenPanels.Count == 2 && controller.OpenPanels.Count(p => p.IsPinned) == 1,
                    "pinning kept the panel and the next click opened a new one");
                releaser.HandlePointerGesture(OutsidePoint, OutsidePoint + new Vector2(140f, 30f));
                Expect(controller.OpenPanels.Count == 2, "a camera drag closed nothing");
                releaser.HandlePointerGesture(OutsidePoint, OutsidePoint);
                Expect(controller.OpenPanels.Count == 1 && controller.OpenPanels[0].IsPinned,
                    "an outside click closed only the unpinned panel");
                Next(1f);
                break;

            case 8:
                Capture("07-after-outside-click");
                if (rightCards.Count > 0)
                    Click(rightCards[0]);
                Next(1.2f);
                break;

            case 9:
                Capture("08-right-side");
                PressButton(controller.OpenPanels[0], "D");
                Next(0.6f);
                break;

            case 10:
            {
                TelemetryDetailPanel panel = controller.OpenPanels[0];
                Enter(Control(panel, "W"));
                Enter(Control(panel, "D"));
                Enter(Control(panel, "Pin"));
                Next(0.3f);
                break;
            }

            case 11:
            {
                TelemetryDetailPanel panel = controller.OpenPanels[0];
                Capture("09-panel-hover");
                Expect(IsHovering(Control(panel, "W")), "an idle range button shows the hover");
                Expect(!IsHovering(Control(panel, "D")), "the selected range button never shows the hover");
                Expect(!IsHovering(Control(panel, "Pin")), "the pinned pin never shows the hover");
                Exit(Control(panel, "W"));
                Exit(Control(panel, "D"));
                Exit(Control(panel, "Pin"));
                PressButton(panel, "Y");
                Next(0.8f);
                break;
            }

            case 12:
                Capture("10-year-range");
                Expect(!IsHovering(Control(controller.OpenPanels[0], "W")), "the hover goes when the pointer leaves");
                xray = FindControl("X-Ray Button");
                section = FindControl("Cross-Section Button");
                Enter(xray.gameObject);
                Enter(section.gameObject);
                Next(0.3f);
                break;

            case 13:
                Capture("11-hover-xray");
                Expect(IsHovering(xray.gameObject) && Shown(xray) == HoverSprite(xray),
                    "hovering the idle X-Ray segment shows the hover sprite");
                Expect(!IsHovering(section.gameObject), "hovering the selected Cross Section segment shows no hover");
                Down(xray.gameObject);
                Next(0.2f);
                break;

            case 14:
                Capture("12-pressed-xray");
                Expect(!IsHovering(xray.gameObject) && Shown(xray) != HoverSprite(xray) && Shown(xray) != IdleSprite(xray),
                    "pressing a segment swaps the hover for the pressed look");
                Up(xray.gameObject);
                Exit(xray.gameObject);
                Exit(section.gameObject);
                CheckOptionalHover(FindControl("Zoom Out Button", required: false), "zoom out");
                CheckOptionalHover(FindControl("Zoom In Button", required: false), "zoom in");
                Next(0.3f);
                break;

            case 15:
            {
                Expect(!IsHovering(xray.gameObject) && Shown(xray) == IdleSprite(xray), "the X-Ray segment is idle again");
                Button zoom = FindControl("Zoom Out Button", required: false);
                if (zoom != null && zoom.IsInteractable())
                {
                    Capture("13-hover-zoom");
                    Expect(IsHovering(zoom.gameObject), "hovering an available zoom button shows the hover sprite");
                }
                Exit(zoom != null ? zoom.gameObject : null);
                Exit(FindControl("Zoom In Button", required: false)?.gameObject);
                Button next = FindControl("Next Telemetry Page", required: false);
                Debug.Log($"DETAIL_PANEL_TEST pages={rails.PageCount} nextInteractable={(next != null && next.IsInteractable())}");
                if (next != null && next.IsInteractable())
                    Enter(next.gameObject);
                Next(0.3f);
                break;
            }

            case 16:
            {
                Button next = FindControl("Next Telemetry Page", required: false);
                if (next != null && next.IsInteractable())
                {
                    Capture("14-hover-pagination");
                    Expect(IsHovering(next.gameObject), "hovering the next-page button shows the hover sprite");
                    Exit(next.gameObject);
                }
                SetSecondaryRails(SecondaryRailUse.TelemetryCards);
                Next(1.5f);
                break;
            }

            case 17:
                Capture("15-secondary-rails-for-cards");
                Expect(!rails.SecondaryRailsReserved && controller.OpenPanels.Count == 0,
                    "choosing Telemetry Cards hands the rails back and closes the panels");
                Debug.Log($"DETAIL_PANEL_TEST cards mode: secondaryCards={rails.HasSecondaryRailCards} pages={rails.PageCount}");
                FindCards();
                Click(leftCards[0]);
                Expect(controller.OpenPanels.Count == 0, "with the rails used for cards a click opens no panel");
                releaser.HandlePointerGesture(OutsidePoint, OutsidePoint);
                SetSecondaryRails(SecondaryRailUse.DetailPanel);
                Next(1.5f);
                break;

            case 18:
                Capture("16-back-to-detail-panel");
                Expect(rails.SecondaryRailsReserved && !rails.HasSecondaryRailCards,
                    "switching back keeps the rails for the panel again");
                Finish(failures == 0 ? "passed" : $"failed={failures}");
                break;
        }
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void PrepareCapture()
    {
        captureCamera = Camera.main;
        RenderTexture target = new(1920, 1080, 24, RenderTextureFormat.ARGB32);
        captureCamera.targetTexture = target;
        if (captureCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            cameraData.renderPostProcessing = false; // UI colours as authored

        // Screen-space-overlay UI is not part of a camera render; move the main
        // canvas into the camera so the captures include it.
        Canvas canvas = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)
            .First(c => c.name == "Main UI Canvas");
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = captureCamera;
        canvas.planeDistance = captureCamera.nearClipPlane + 1f;

        controller = Object.FindFirstObjectByType<TelemetryDetailPanelController>();
        releaser = Object.FindFirstObjectByType<TelemetryCardFocusReleaser>();
        rails = Object.FindFirstObjectByType<WideStatRailManager>();
        Debug.Log($"DETAIL_PANEL_TEST screen={Screen.width}x{Screen.height} controller={(controller != null)} " +
                  $"ready={(controller != null && controller.IsReady)} releaser={(releaser != null)}");
        if (rails != null)
            rails.RebuildLayout();
    }

    private static void FindCards()
    {
        IEnumerable<PerformanceStatCardView> cards = AllCards()
            .Where(card => rails.OwnsCard(card))
            .OrderByDescending(card => card.transform.position.y);
        leftCards = cards.Where(card => card.RailSide == StatRailSide.Left).ToList();
        rightCards = cards.Where(card => card.RailSide == StatRailSide.Right).ToList();
        Debug.Log($"DETAIL_PANEL_TEST cards left={leftCards.Count} right={rightCards.Count} " +
                  $"reserved={rails.SecondaryRailsReserved} secondaryCards={rails.HasSecondaryRailCards} pages={rails.PageCount}");
    }

    private static IEnumerable<PerformanceStatCardView> AllCards() =>
        Object.FindObjectsByType<PerformanceStatCardView>(FindObjectsSortMode.None)
            .Where(card => card.isActiveAndEnabled);

    private static void Click(PerformanceStatCardView card)
    {
        Vector2 position = RectTransformUtility.WorldToScreenPoint(captureCamera, card.transform.position);
        PointerEventData data = new(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = position,
            pressPosition = position,
            clickCount = 1
        };
        ExecuteEvents.Execute(card.gameObject, data, ExecuteEvents.pointerClickHandler);
        Debug.Log($"DETAIL_PANEL_TEST click '{card.BoundSource?.MetricName}' focused={card.IsFocused} panels={controller.OpenPanels.Count}");
    }

    private static void PressButton(TelemetryDetailPanel panel, string name)
    {
        Button button = panel.GetComponentsInChildren<Button>(true).First(b => b.name == name);
        button.onClick.Invoke();
    }

    private static GameObject Control(TelemetryDetailPanel panel, string name) =>
        panel.GetComponentsInChildren<Button>(true).First(b => b.name == name).gameObject;

    private static Button FindControl(string name, bool required = true)
    {
        Button button = Object.FindObjectsByType<Button>(FindObjectsSortMode.None)
            .FirstOrDefault(b => b.name == name && b.isActiveAndEnabled);
        if (button == null && required)
            Expect(false, $"control '{name}' exists");
        return button;
    }

    private static void CheckOptionalHover(Button button, string label)
    {
        if (button == null)
            return;
        Debug.Log($"DETAIL_PANEL_TEST {label} interactable={button.IsInteractable()} hover={(button.TryGetComponent(out UIHoverSprite hover) && hover != null)}");
        if (button.IsInteractable())
            Enter(button.gameObject);
    }

    private static void SetSecondaryRails(SecondaryRailUse use)
    {
        SerializedObject data = new(controller);
        data.FindProperty("secondaryRails").enumValueIndex = (int)use;
        data.ApplyModifiedProperties();
    }

    private static bool IsHovering(GameObject control) =>
        control != null && control.TryGetComponent(out UIHoverSprite hover) && hover.IsShowingHover;

    private static Sprite Shown(Button button) => ((Image)button.targetGraphic).overrideSprite;

    private static Sprite HoverSprite(Button button) => Field<Sprite>(button, "hoverSprite");

    private static Sprite IdleSprite(Button button) => Field<Sprite>(button, "idleSprite");

    private static T Field<T>(Button button, string name) where T : Object =>
        new SerializedObject(button.GetComponent<UIHoverSprite>()).FindProperty(name).objectReferenceValue as T;

    private static PointerEventData Pointer() =>
        new(EventSystem.current) { button = PointerEventData.InputButton.Left };

    private static void Enter(GameObject target)
    {
        if (target != null)
            ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerEnterHandler);
    }

    private static void Exit(GameObject target)
    {
        if (target != null)
            ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerExitHandler);
    }

    private static void Down(GameObject target) =>
        ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerDownHandler);

    private static void Up(GameObject target) =>
        ExecuteEvents.Execute(target, Pointer(), ExecuteEvents.pointerUpHandler);

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            failures++;
        Debug.Log($"DETAIL_PANEL_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Capture(string name)
    {
        RenderTexture target = captureCamera.targetTexture;
        Canvas.ForceUpdateCanvases();
        captureCamera.Render();
        RenderTexture.active = target;
        Texture2D image = new(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
        Object.DestroyImmediate(image);
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"DETAIL_PANEL_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
