using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Batch play test of how strong leader lines are drawn and where alarm cards
/// sit, in the wide layout at 1920x1080, in Cross Section:
///
/// - Warning and critical cards are in the innermost rail shown on their side.
/// - Clicking each card in turn (as a mouse does, which opens its detail
///   panel), the clicked card's line is at full strength unless it runs
///   across translucent UI (another card or a control). A line that turns
///   back from its panel runs behind the opaque panel and stays at full
///   strength; it used to fade to 2 % as if it doubled back across its card.
/// - A line pulled across a translucent control (the zoom) fades, and stops
///   counting as crossing once clear of it.
///
/// Lines are measured as a frame finishes, as in the leader line stability
/// test: outside the player loop the editor reports its own window's size.
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class LeaderLineOpacityPlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ActiveKey = "LeaderLineOpacityPlayTest.Active";
    private const string StartKey = "LeaderLineOpacityPlayTest.Start";

    private static readonly FieldInfo HorizontalImage =
        typeof(StatLeaderLineView).GetField("horizontalImage", BindingFlags.Instance | BindingFlags.NonPublic);

    private static int step;
    private static float stepTime;
    private static int failures;
    private static StatRailManager rails;
    private static List<PerformanceStatCardView> cards = new();
    private static int cardIndex;
    private static PerformanceStatCardView pending;
    private static StatLeaderLineView pulledLine;
    private static Vector3 pulledHome;

    static LeaderLineOpacityPlayTest()
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        RenderPipelineManager.endContextRendering -= OnFrameRendered;
        RenderPipelineManager.endContextRendering += OnFrameRendered;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        RenderPipelineManager.endContextRendering -= OnFrameRendered;
        RenderPipelineManager.endContextRendering += OnFrameRendered;
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
        Application.runInBackground = true;

        float t = Time.time;
        if (step == 0 && t <= 3f)
            return;
        if (step > 0 && t < stepTime)
            return;

        switch (step)
        {
            case 0:
                PlayModeWindow.SetCustomRenderingResolution(1920, 1080, "Leader line opacity test");
                rails = StatRailManagers.Wide();
                Object.FindAnyObjectByType<ViewModeSegmentedControl>().SelectSection();
                Next(4.5f);
                break;

            case 1:
                CheckAlarmPlacement();
                cards = Object.FindObjectsByType<PerformanceStatCardView>()
                    .Where(card => card.isActiveAndEnabled && rails.OwnsCard(card))
                    .OrderBy(card => card.RailSide).ThenByDescending(card => card.transform.position.y)
                    .ToList();
                Debug.Log($"LINE_OPACITY_TEST cards={cards.Count}");
                cardIndex = 0;
                Next(0f);
                break;

            case 2:
                if (cardIndex >= cards.Count)
                {
                    step = 5;
                    stepTime = t;
                    break;
                }
                Click(cards[cardIndex]);
                Next(1.2f); // longer than the 0.7 s direction fade
                break;

            case 3:
                pending = cards[cardIndex];
                Next(0.3f);
                break;

            case 4:
                cardIndex++;
                step = 2;
                stepTime = t;
                break;

            case 5:
                PullLineAcrossZoom();
                Next(1.2f);
                break;

            case 6:
                Expect(pulledLine.IsCrossingUi &&
                       Mathf.Abs(pulledLine.DirectionOpacity - pulledLine.FadedDirectionOpacity) < 0.001f,
                    $"a line running across the zoom control fades (crossing={pulledLine.IsCrossingUi} " +
                    $"opacity={pulledLine.DirectionOpacity:0.000})");
                pulledLine.Card.WorldAnchor.position = pulledHome;
                Next(1.2f);
                break;

            case 7:
                Expect(!pulledLine.IsCrossingUi, "clear of the zoom control the line no longer counts as crossing it");
                Finish(failures == 0 ? "passed" : $"failed ({failures})");
                break;
        }
    }

    private static void OnFrameRendered(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (pending == null || !EditorApplication.isPlaying)
            return;
        PerformanceStatCardView card = pending;
        pending = null;
        MeasureClicked(card);
    }

    /// <summary>On each side, no normal card sits in the innermost rail shown while an alarm card sits outside it.</summary>
    private static void CheckAlarmPlacement()
    {
        int checkedAlarms = 0;
        foreach (StatRailSide side in new[] { StatRailSide.Left, StatRailSide.Right })
        {
            int innermost = 0;
            while (rails.GetRail(side, innermost + 1) != null)
                innermost++;

            List<(PerformanceStatCardView card, int depth)> placed = rails.ActiveCards.Values
                .Where(card => card.RailSide == side)
                .Select(card => (card, Depth(card, side)))
                .ToList();
            List<string> outerAlarms = placed.Where(p => IsAlarm(p.card) && p.depth < innermost)
                .Select(p => Name(p.card)).ToList();
            List<string> innerNormals = placed.Where(p => !IsAlarm(p.card) && p.depth == innermost)
                .Select(p => Name(p.card)).ToList();
            checkedAlarms += placed.Count(p => IsAlarm(p.card) && innermost > 0);

            Debug.Log($"LINE_OPACITY_TEST {side} rails={innermost + 1} " + string.Join(", ",
                placed.OrderBy(p => p.depth).Select(p => $"{Name(p.card)}@{p.depth}{(IsAlarm(p.card) ? "!" : "")}")));
            Expect(innermost == 0 || outerAlarms.Count == 0 || innerNormals.Count == 0,
                $"{side} alarm cards take the innermost rail before normal cards " +
                $"(alarms outside it: {string.Join(", ", outerAlarms)}; normal cards in it: {string.Join(", ", innerNormals)})");
        }
        Expect(checkedAlarms > 0, "an alarm card shares a side with an inner rail, so the placement was checked");
    }

    private static int Depth(PerformanceStatCardView card, StatRailSide side)
    {
        for (int depth = 0; ; depth++)
        {
            RectTransform rail = rails.GetRail(side, depth);
            if (rail == null)
                return -1;
            if (card.transform.IsChildOf(rail))
                return depth;
        }
    }

    private static void MeasureClicked(PerformanceStatCardView clicked)
    {
        StatLeaderLineView line = Object.FindObjectsByType<StatLeaderLineView>()
            .FirstOrDefault(candidate => candidate.Card == clicked);
        if (line == null)
        {
            Expect(false, $"'{Name(clicked)}' has a line");
            return;
        }

        Image image = (Image)HorizontalImage.GetValue(line);
        float drawn = image.color.a / Mathf.Max(0.0001f, clicked.StateColor.a);
        float peer = line.TryGetComponent(out CanvasGroup group) ? group.alpha : 1f;
        float effective = drawn * peer;
        string what = $"'{Name(clicked)}' state={clicked.VisualState} panel={clicked.ConnectsFromPanel} " +
                      $"effective={effective:0.000} reversed={line.IsReversed} crossing={line.IsCrossingUi}";
        Debug.Log($"LINE_OPACITY_TEST {what}");

        Expect(clicked.IsFocused, $"the clicked card is focused: {what}");
        Expect(!(clicked.ConnectsFromPanel && line.IsReversed), $"a line leaving its panel is never reversed: {what}");
        if (line.IsCrossingUi)
            Expect(Mathf.Abs(effective - line.FadedDirectionOpacity) < 0.002f, $"a clicked line across translucent UI fades: {what}");
        else
            Expect(effective >= 0.99f, $"a clicked line clear of translucent UI is at full strength: {what}");
    }

    /// <summary>Moves a left card's sensor into the zoom control, so its line has to run across it (play mode only).</summary>
    private static void PullLineAcrossZoom()
    {
        Object.FindAnyObjectByType<TelemetryDetailPanelController>().DismissUnpinned();
        CardFocus.Release();

        RectTransform zoom = (RectTransform)Object.FindObjectsByType<ZoomControlAvailabilityView>()
            .First(view => view.isActiveAndEnabled).transform;
        pulledLine = Object.FindObjectsByType<StatLeaderLineView>()
            .First(line => line.Card != null && line.Card.RailSide == StatRailSide.Left && !IsAlarm(line.Card));
        Camera camera = Camera.main;
        pulledHome = pulledLine.Card.WorldAnchor.position;
        Vector2 zoomCentre = RectTransformUtility.WorldToScreenPoint(null, zoom.TransformPoint(zoom.rect.center));
        float depth = camera.WorldToScreenPoint(pulledHome).z;
        pulledLine.Card.WorldAnchor.position = camera.ScreenToWorldPoint(new Vector3(zoomCentre.x, zoomCentre.y, depth));
        Debug.Log($"LINE_OPACITY_TEST pulled '{Name(pulledLine.Card)}' to the zoom at {zoomCentre}");
    }

    private static bool IsAlarm(PerformanceStatCardView card) =>
        card.VisualState is StatVisualState.Warning or StatVisualState.Critical;

    private static string Name(PerformanceStatCardView card) =>
        card.BoundSource != null ? card.BoundSource.StatId : card.name;

    private static void Click(PerformanceStatCardView card)
    {
        Vector2 position = RectTransformUtility.WorldToScreenPoint(null, card.transform.position);
        PointerEventData data = new(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = position,
            pressPosition = position,
            clickCount = 1
        };
        ExecuteEvents.Execute(card.gameObject, data, ExecuteEvents.pointerClickHandler);
    }

    private static void Expect(bool condition, string message)
    {
        if (condition)
            return;
        failures++;
        Debug.LogWarning($"LINE_OPACITY_TEST FAIL {message}");
    }

    private static void Next(float seconds)
    {
        step++;
        stepTime = Time.time + seconds;
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        RenderPipelineManager.endContextRendering -= OnFrameRendered;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"LINE_OPACITY_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
