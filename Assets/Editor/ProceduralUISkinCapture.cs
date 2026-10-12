using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Batch captures of the procedural UI skin, to Logs/ProceduralUISkin:
/// the wide layout over the scene, over a bright and a white background
/// (text and lines must stay readable), with a control and a card hovered
/// (as the light turns on and once it is on), with alarm states and the detail panel, the
/// same frame drawn with the sprite skin for comparison, and the portrait
/// layout. Checks that every shape draws procedurally and nothing logs errors.
///
/// Run with -executeMethod ProceduralUISkinCapture.Run and without -quit.
/// </summary>
[InitializeOnLoad]
public static class ProceduralUISkinCapture
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/ProceduralUISkin";
    private const string ActiveKey = "ProceduralUISkinCapture.Active";
    private const string StartKey = "ProceduralUISkinCapture.Start";

    private static int step;
    private static float stepTime;
    private static int failures;
    private static readonly List<string> errors = new();
    private static Canvas canvas;
    private static Camera mainCamera;
    private static Camera uiCamera;
    private static int width = 1920;
    private static int height = 1080;
    private static GameObject hovered;
    private static PerformanceStatCardView hoveredCard;
    private static ServicePanelView servicePanel;

    static ProceduralUISkinCapture()
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    public static void Run()
    {
        Directory.CreateDirectory(OutputDirectory);
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
        if (!EditorApplication.isPlaying || Time.time < stepTime)
            return;
        Application.runInBackground = true;

        switch (step)
        {
            case 0:
                Application.logMessageReceived += RecordError;
                // Let the intro reveal and the first readings settle.
                Next(7f);
                break;
            case 1:
                PrepareWide();
                // Telemetry shows in the cross section.
                Object.FindAnyObjectByType<ViewModeSegmentedControl>().SelectSection();
                Next(4.5f);
                break;
            case 2:
                CheckShapes();
                Capture("01_wide_scene");
                Capture("02_wide_bright", new Color32(214, 221, 216, 255));
                Capture("03_wide_white", new Color32(255, 255, 255, 255));
                HoverControl("Full Model Button");
                Next(0.3f);
                break;
            case 3:
                ExpectHover(hovered, "the hovered control");
                UIShape icon = hovered != null
                    ? hovered.GetComponentsInChildren<UIShape>().FirstOrDefault(s => s.ShownStyle != null && s.ShownStyle.kind == UIShapeKind.Sprite)
                    : null;
                Expect(icon != null && icon.ShownStyle.name == "Icon Hover",
                    $"the hovered control's icon glows ({(icon != null ? icon.ShownStyle.name : "no icon")})");
                Capture("04_hover_control_light_on");
                Next(0.6f);
                break;
            case 4:
                Capture("05_hover_control_settled");
                Exit(hovered);
                hoveredCard = Cards().FirstOrDefault(c => c.VisualState == StatVisualState.Normal);
                if (hoveredCard != null)
                    Enter(hoveredCard.gameObject);
                Next(0.3f);
                break;
            case 5:
                ExpectHover(hoveredCard != null ? hoveredCard.gameObject : null, "the hovered card");
                Capture("06_hover_card_light_on");
                Next(0.6f);
                break;
            case 6:
                Capture("07_hover_card_settled");
                if (hoveredCard != null)
                    Exit(hoveredCard.gameObject);
                ForceAlarmStates();
                Next(0.8f);
                break;
            case 7:
                Capture("08_alarm_states");
                hoveredCard = Cards().FirstOrDefault(c => c.VisualState == StatVisualState.Warning);
                if (hoveredCard != null)
                    Enter(hoveredCard.gameObject);
                OpenDetailPanel();
                Next(1.2f);
                break;
            case 8:
                UIShape warningFace = hoveredCard != null ? hoveredCard.GetComponentsInChildren<UIShape>()
                    .FirstOrDefault(shape => shape.name == "Background") : null;
                Expect(warningFace != null && warningFace.ShownStyle != null && warningFace.ShownStyle.name == "Card Warning Hover",
                    $"a warning card answers the pointer ({warningFace?.ShownStyle?.name})");
                Capture("09_detail_panel");
                if (hoveredCard != null)
                    Exit(hoveredCard.gameObject);
                Capture("10_detail_panel_white", new Color32(255, 255, 255, 255));
                UITheme.Active.proceduralShapes = false;
                UITheme.RaiseChanged();
                Next(0.3f);
                break;
            case 9:
                Capture("11_sprite_skin_for_comparison");
                UITheme.Active.proceduralShapes = true;
                UITheme.RaiseChanged();
                ServiceMode(true);
                Next(1.5f);
                break;
            case 10:
                Capture("12_service_list");
                hovered = servicePanel.GetComponentsInChildren<UIControlState>()
                    .FirstOrDefault(c => c.Tone == UITone.Warning)?.gameObject;
                Expect(hovered != null, "the service list has a row due soon");
                if (hovered != null)
                    Enter(hovered);
                Next(0.9f);
                break;
            case 11:
                Expect(hovered != null && hovered.GetComponent<UIControlState>().IsHovered &&
                       hovered.GetComponent<UIShape>().ShownStyle.name == "List Row Warning Hover",
                    $"a row due soon answers the pointer ({hovered?.GetComponent<UIShape>().ShownStyle?.name})");
                Capture("13_service_row_hover");
                Exit(hovered);
                servicePanel.ShowDetail(servicePanel.Items[6].Part);
                Next(1.2f);
                break;
            case 12:
                Enter(servicePanel.BackButton.gameObject);
                Next(0.9f);
                break;
            case 13:
                UIShape face = servicePanel.BackButton.GetComponentsInChildren<UIShape>().FirstOrDefault();
                Expect(face != null && face.ShownStyle != null && face.ShownStyle.name == "Icon Button Hover",
                    $"the back button shows its hover face ({face?.ShownStyle?.name})");
                Capture("14_service_detail_back_hover");
                Exit(servicePanel.BackButton.gameObject);
                ServiceMode(false);
                PrepareCanvasForPortrait();
                Next(1.2f);
                break;
            case 14:
                StatRailManagers.Portrait()?.RebuildLayout();
                Next(0.8f);
                break;
            case 15:
                Capture("15_portrait_scene");
                Capture("16_portrait_white", new Color32(255, 255, 255, 255));
                Expect(errors.Count == 0, $"no errors logged ({string.Join(" | ", errors.Take(3))})");
                Finish(failures == 0 ? "passed" : "failed");
                break;
        }
    }

    private static void PrepareWide()
    {
        ResponsiveLayoutShell shell = Object.FindAnyObjectByType<ResponsiveLayoutShell>();
        if (shell != null)
            shell.enabled = false;
        canvas = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas");
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        mainCamera = Camera.main;
        mainCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        if (mainCamera.TryGetComponent(out UniversalAdditionalCameraData mainData))
            mainData.renderPostProcessing = false;
        uiCamera = new GameObject("Skin Capture UI Camera").AddComponent<Camera>();
        uiCamera.enabled = false;
        uiCamera.clearFlags = CameraClearFlags.SolidColor;
        uiCamera.backgroundColor = Color.clear;
        uiCamera.cullingMask = 1 << LayerMask.NameToLayer("UI");
        SetTargets(1920, 1080);
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = uiCamera;
        canvas.planeDistance = 10f;
        Canvas.ForceUpdateCanvases();
        StatRailManagers.Wide()?.RebuildLayout();
    }

    /// <summary>Switches the wide layout to Service, or back to Telemetry.</summary>
    private static void ServiceMode(bool on)
    {
        DigitalTwinModeSwitch modeSwitch = canvas.GetComponentsInChildren<DigitalTwinModeSwitch>().First();
        (on ? modeSwitch.ServiceButton : modeSwitch.TelemetryButton).onClick.Invoke();
        servicePanel = canvas.GetComponentsInChildren<ServicePanelView>(true)
            .First(panel => panel.transform.parent != null && panel.transform.parent.name == "Wide layout");
    }

    private static void PrepareCanvasForPortrait()
    {
        GameObject Layout(string name) => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .First(t => t.name == name).gameObject;
        Layout("Wide layout").SetActive(false);
        Layout("Portrait layout").SetActive(true);
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight = 0f;
        SetTargets(1080, 1920);
        Canvas.ForceUpdateCanvases();
    }

    private static void SetTargets(int w, int h)
    {
        width = w;
        height = h;
        mainCamera.targetTexture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        uiCamera.targetTexture = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
    }

    /// <summary>Every visible styled image must be a shape that draws procedurally.</summary>
    private static void CheckShapes()
    {
        Image[] images = canvas.GetComponentsInChildren<Image>().Where(i => i.isActiveAndEnabled).ToArray();
        string[] plain = images.Where(i => i is not UIShape && i.sprite != null).Select(i => i.name).ToArray();
        Expect(plain.Length == 0, $"no sprite-drawn images left ({string.Join(", ", plain.Take(5))})");
        int styled = images.OfType<UIShape>().Count(s => s.ShownStyle != null);
        Expect(styled > 20, $"shapes draw procedurally ({styled} styled)");
        foreach (UIShape icon in images.OfType<UIShape>().Where(s => s.ShownStyle != null && s.ShownStyle.kind == UIShapeKind.Sprite))
            Debug.Log($"PROCEDURAL_UI_ICON {icon.transform.parent.name}/{icon.name} rect={icon.rectTransform.rect.size} " +
                      $"sprite={icon.overrideSprite?.name} texture={icon.mainTexture?.name} style={icon.ShownStyle.name} color={icon.color}");
        Expect(images.OfType<UIShape>().Where(s => s.ShownStyle != null)
                .All(s => s.materialForRendering.shader.name == "UI/Digital Twin/Shape"),
            "styled shapes render with the shape shader");
    }

    private static void HoverControl(string name)
    {
        hovered = canvas.GetComponentsInChildren<UIHoverSprite>().Select(h => h.gameObject)
            .FirstOrDefault(g => g.name == name && g.GetComponent<Selectable>().IsInteractable());
        Expect(hovered != null, $"{name} can be hovered");
        if (hovered != null)
            Enter(hovered);
    }

    private static void ExpectHover(GameObject target, string what)
    {
        UIHoverSprite hover = target != null ? target.GetComponent<UIHoverSprite>() : null;
        Image image = hover != null ? new SerializedObject(hover).FindProperty("target").objectReferenceValue as Image : null;
        UIShapeStyle shown = (image as UIShape)?.ShownStyle;
        Expect(hover != null && hover.IsShowingHover && shown != null && shown.name.Contains("Hover"),
            $"{what} shows the hover look (showing {hover?.IsShowingHover}, style {(shown != null ? shown.name : "none")}, " +
            $"sprite {image?.sprite?.name}, override {image?.overrideSprite?.name})");
    }

    private static void Enter(GameObject target)
    {
        var data = new PointerEventData(EventSystem.current) { pointerId = -1 };
        ExecuteEvents.Execute(target, data, ExecuteEvents.pointerEnterHandler);
    }

    private static void Exit(GameObject target)
    {
        if (target == null)
            return;
        var data = new PointerEventData(EventSystem.current) { pointerId = -1 };
        ExecuteEvents.Execute(target, data, ExecuteEvents.pointerExitHandler);
    }

    private static void ForceAlarmStates()
    {
        // Hold the readings still so the forced states are not recomputed.
        foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>())
        {
            if (behaviour is ITelemetrySimulationProvider)
                behaviour.enabled = false;
        }
        StatVisualState[] states = { StatVisualState.Warning, StatVisualState.Critical, StatVisualState.Unavailable };
        int i = 0;
        foreach (PerformanceStatCardView card in Cards().Skip(1).Take(3))
            card.BoundSource.SetState(states[i++]);
        foreach (PerformanceStatCardView card in Cards())
            card.RefreshBoundSources();
    }

    private static void OpenDetailPanel()
    {
        PerformanceStatCardView card = Cards().FirstOrDefault();
        if (card == null)
            return;
        Click(card);
        Click(card);
    }

    private static void Click(PerformanceStatCardView card)
    {
        Vector2 position = RectTransformUtility.WorldToScreenPoint(uiCamera, card.transform.position);
        PointerEventData data = new(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = position,
            pressPosition = position,
            clickCount = 1
        };
        ExecuteEvents.Execute(card.gameObject, data, ExecuteEvents.pointerClickHandler);
    }

    private static IEnumerable<PerformanceStatCardView> Cards() =>
        Object.FindObjectsByType<PerformanceStatCardView>()
            .Where(c => c.isActiveAndEnabled && c.BoundSource != null)
            .OrderBy(c => c.RailSide).ThenByDescending(c => c.transform.position.y);

    /// <summary>The UI composited over the scene, or over a flat colour.</summary>
    private static void Capture(string name, Color32? background = null)
    {
        Color32[] under;
        if (background is Color32 flat)
            under = Enumerable.Repeat(flat, width * height).ToArray();
        else
        {
            mainCamera.Render();
            under = Read(mainCamera.targetTexture);
        }
        Canvas.ForceUpdateCanvases();
        uiCamera.Render();
        Color32[] ui = Read(uiCamera.targetTexture);
        for (int i = 0; i < under.Length; i++)
        {
            float cover = ui[i].a / 255f;
            under[i] = new Color32(
                (byte)Mathf.Min(255f, ui[i].r + under[i].r * (1f - cover)),
                (byte)Mathf.Min(255f, ui[i].g + under[i].g * (1f - cover)),
                (byte)Mathf.Min(255f, ui[i].b + under[i].b * (1f - cover)),
                255);
        }
        Texture2D image = new(width, height, TextureFormat.RGBA32, false);
        image.SetPixels32(under);
        image.Apply();
        File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
        Object.DestroyImmediate(image);
        Debug.Log($"PROCEDURAL_UI_CAPTURE saved {name}");
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
        Debug.Log($"PROCEDURAL_UI_CAPTURE {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= RecordError;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"PROCEDURAL_UI_CAPTURE finished: {result}");
        EditorApplication.ExitPlaymode();
        EditorApplication.delayCall += () => EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
