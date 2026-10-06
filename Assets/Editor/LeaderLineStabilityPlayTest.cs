using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Batch play test of the leader lines and the cards they leave from. The
/// camera orbits while the rails are rebuilt under it (as an alarm or a new
/// reading would), first with card columns, then with a detail panel open,
/// then in rows in a window taller than wide, then close in, where sensors
/// slip off screen and behind the camera. Every rendered frame it checks that
/// each line starts exactly at its card (or the panel opened from it) and ends
/// exactly at its sensor's placement point, in that same frame, and that no
/// card, line end or card object jumps. Also sweeps a point through the camera
/// plane to check the placement point runs on smoothly. Captures a few frames
/// to Logs/LeaderLineTest and exits the editor with the verdict.
///
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class LeaderLineStabilityPlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/LeaderLineTest";
    private const string ActiveKey = "LeaderLineStabilityPlayTest.Active";
    private const string StartKey = "LeaderLineStabilityPlayTest.Start";

    // A line end may lag its target by no more than this, in canvas units.
    private const float EndTolerance = 1f;
    // A sensor's placement point moving further than this in one frame is a jump.
    private const float SensorJump = 150f;
    private const float OrbitDegreesPerSecond = 30f;

    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly FieldInfo LastCardPoint = typeof(StatLeaderLineView).GetField("lastCardPoint", Private);
    private static readonly FieldInfo LastSensorPoint = typeof(StatLeaderLineView).GetField("lastSensorPoint", Private);
    private static readonly FieldInfo LineLayer = typeof(StatLeaderLineView).GetField("lineLayer", Private);
    private static readonly FieldInfo GeometryValid = typeof(StatLeaderLineView).GetField("geometryValid", Private);
    private static readonly FieldInfo DesiredYaw = typeof(OrbitCameraController).GetField("desiredYaw", Private);
    private static readonly FieldInfo DesiredPitch = typeof(OrbitCameraController).GetField("desiredPitch", Private);
    private static readonly FieldInfo DesiredDistance = typeof(OrbitCameraController).GetField("desiredDistance", Private);

    private static int step;
    private static float stepTime;
    private static int failures;
    private static Camera captureCamera;
    private static StatRailManager rails;
    private static TelemetryDetailPanelController panels;
    private static OrbitCameraController orbit;
    private static bool measuring;
    private static bool orbiting;
    private static float orbitStarted;
    private static float nextRebuild;
    private static int lastFrame = -1;
    private static int settleFrames;
    private static string phase = "";
    private static readonly Dictionary<string, PhaseStats> stats = new();
    private static readonly Dictionary<PerformanceStatSource, Sample> previous = new();
    private static int rebuiltFrame = -1;
    private static int snapshotFrame = -1;
    private static readonly List<(RectTransform rect, Vector3 position, Vector2 size)> snapshot = new();
    private static int movedReports;

    static LeaderLineStabilityPlayTest()
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

    // -----------------------------------------------------------------------
    // Steps
    // -----------------------------------------------------------------------

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f) > 420f)
        {
            Finish("timeout");
            return;
        }
        if (!EditorApplication.isPlaying)
            return;
        // A batch editor opens behind other windows; keep play mode running
        // there. Only this session's flag, not the project setting.
        Application.runInBackground = true;

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
                Expect(!rails.RailsAreRows && rails.DetailRailActive, "a wide window runs columns with the detail rail");
                Begin("columns", 10f);
                break;

            case 2:
                Capture("01-columns-orbited");
                End();
                // A panel open on a left card: its line leaves from the panel.
                PerformanceStatCardView card = OwnedCards().Where(c => c.RailSide == StatRailSide.Left)
                    .OrderByDescending(c => c.transform.position.y).FirstOrDefault();
                if (card != null)
                    Click(card);
                Next(0.8f);
                break;

            case 3:
                Expect(panels.FloatingPanel != null, "a panel opened for the orbit with a panel");
                Begin("columns with panel", 6f);
                break;

            case 4:
                Capture("02-panel-orbited");
                End();
                panels.DismissUnpinned();
                // A window taller than wide: the shell turns the rails into rows.
                SetWindow(1080, 1920);
                Next(1.5f);
                break;

            case 5:
                Begin("rows", 10f);
                break;

            case 6:
                Capture("03-rows-orbited");
                End();
                Expect(rails.RailsAreRows, "the shell ran the rails as rows in the tall window");
                SetWindow(1920, 1080);
                // Close in, so sensors leave the screen and pass behind the camera.
                if (orbit != null)
                    DesiredDistance.SetValue(orbit, orbit.minDistance);
                Next(2f);
                break;

            case 7:
                Begin("close in", 12f);
                break;

            case 8:
            {
                Capture("04-close-in");
                End();
                SweepThroughCameraPlane();
                // As if changed in the inspector while playing: the shell passes it on.
                TelemetryLayoutSettings settings = Object.FindAnyObjectByType<TelemetryLayoutSettings>();
                Expect(settings != null, "the scene has Telemetry Layout Settings");
                settings?.SetRails(false, false, TelemetryRailCount.One);
                settings?.SetDetailPanel(false, false);
                Next(1f);
                break;
            }

            case 9:
            {
                Expect(rails.RailsPerSide == 1, $"one column a side from the settings (now {rails.RailsPerSide})");
                Expect(!rails.DetailRailActive && !panels.FeatureEnabled && !panels.IsReady,
                    "the history panel switched off from the settings");
                TelemetryLayoutSettings settings = Object.FindAnyObjectByType<TelemetryLayoutSettings>();
                settings.SetRails(false, false, TelemetryRailCount.Four);
                settings.SetDetailPanel(false, true);
                Next(1f);
                break;
            }

            case 10:
                Debug.Log($"LEADER_LINE_TEST four asked: perSide={rails.RailsPerSide} detail={rails.DetailRailActive}");
                Expect(rails.RailsPerSide < 4 && rails.RailsPerSide >= 1 && rails.DetailRailActive,
                    "four columns a side do not fit beside the panel's rail, so fewer run");
                Report();
                Finish(failures == 0 ? "passed" : $"failed={failures}");
                break;
        }
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void Begin(string name, float seconds)
    {
        phase = name;
        stats[name] = new PhaseStats();
        previous.Clear();
        measuring = true;
        orbiting = true;
        orbitStarted = Time.unscaledTime;
        nextRebuild = Time.unscaledTime + 1.3f;
        settleFrames = 3;
        Next(seconds);
    }

    private static void End()
    {
        measuring = false;
        orbiting = false;
    }

    // -----------------------------------------------------------------------
    // Every rendered frame
    // -----------------------------------------------------------------------

    private static void OnFrameRendered(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!EditorApplication.isPlaying || Time.frameCount == lastFrame)
            return;
        lastFrame = Time.frameCount;

        if (orbiting && orbit != null)
        {
            // Turns at a steady rate, tilting up and down, for the next frame.
            float dt = Time.unscaledDeltaTime;
            DesiredYaw.SetValue(orbit, (float)DesiredYaw.GetValue(orbit) + OrbitDegreesPerSecond * dt);
            float elapsed = Time.unscaledTime - orbitStarted;
            DesiredPitch.SetValue(orbit, Mathf.Lerp(orbit.minPitch, orbit.maxPitch, 0.5f + 0.5f * Mathf.Sin(elapsed * 0.9f)));

            // Rebuilds land mid-motion, as an alarm or a page change would.
            if (Time.unscaledTime >= nextRebuild && rails != null)
            {
                nextRebuild = Time.unscaledTime + 1.3f;
                rails.RequestRebuild();
                stats[phase].Rebuilds++;
            }
        }

        if (!measuring || captureCamera == null)
            return;
        if (settleFrames > 0)
        {
            settleFrames--;
            return;
        }
        Measure(stats[phase]);
    }

    /// <summary>Where the line layer and its parents stand when the lines have drawn, before the canvas updates.</summary>
    private static void SnapshotLayout()
    {
        if (!measuring || snapshotFrame == Time.frameCount)
            return;
        snapshotFrame = Time.frameCount;
        snapshot.Clear();
        StatLeaderLineView line = Object.FindObjectsByType<StatLeaderLineView>().FirstOrDefault(l => l.Card != null);
        if (line == null)
            return;
        for (Transform level = (RectTransform)LineLayer.GetValue(line); level != null; level = level.parent)
        {
            if (level is RectTransform rect)
                snapshot.Add((rect, rect.position, rect.rect.size));
        }
        // And one card's rail chain.
        for (Transform level = line.Card.transform; level != null; level = level.parent)
        {
            if (level is RectTransform rect)
                snapshot.Add((rect, rect.position, rect.rect.size));
        }
    }

    private static void ReportLayoutMoves()
    {
        if (snapshotFrame != Time.frameCount || movedReports >= 6)
            return;
        foreach ((RectTransform rect, Vector3 position, Vector2 size) in snapshot)
        {
            if (rect == null)
                continue;
            if ((rect.position - position).sqrMagnitude > 1e-6f || (rect.rect.size - size).sqrMagnitude > 0.01f)
            {
                movedReports++;
                Debug.Log($"LEADER_LINE_TEST moved after the lines drew (frame {Time.frameCount}): '{rect.name}' " +
                          $"position {position} -> {rect.position}, size {size} -> {rect.rect.size}");
                return;
            }
        }
    }

    private static void Measure(PhaseStats phaseStats)
    {
        ReportLayoutMoves();
        phaseStats.Frames++;
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
        HashSet<PerformanceStatSource> seen = new();

        foreach (StatLeaderLineView line in Object.FindObjectsByType<StatLeaderLineView>())
        {
            PerformanceStatCardView card = line.Card;
            PerformanceStatSource source = line.Source;
            if (card == null || source == null || !line.isActiveAndEnabled || !(bool)GeometryValid.GetValue(line))
                continue;
            RectTransform layer = (RectTransform)LineLayer.GetValue(line);
            if (layer == null || !rails.OwnsCard(card))
                continue;

            RectTransform connection = card.ActiveConnectionPoint;
            Vector2 cardPoint = ToLayer(layer, RectTransformUtility.WorldToScreenPoint(UICamera, connection.position));
            Vector2 sensorPoint = ToLayer(layer, TelemetryPresentationBuilder.PlacementScreenPoint(
                captureCamera, card.WorldAnchor.position));
            float cardError = (cardPoint - (Vector2)LastCardPoint.GetValue(line)).magnitude;
            float sensorError = (sensorPoint - (Vector2)LastSensorPoint.GetValue(line)).magnitude;
            if (sensorError > EndTolerance && !phaseStats.Dumped)
            {
                phaseStats.Dumped = true;
                Vector3 raw = captureCamera.WorldToScreenPoint(card.WorldAnchor.position);
                Debug.Log($"LEADER_LINE_TEST dump '{source.MetricName}' frame={Time.frameCount} rebuilt={rebuiltFrame == Time.frameCount} " +
                          $"line={(Vector2)LastSensorPoint.GetValue(line)} measured={sensorPoint} raw={raw} " +
                          $"pixelRect={captureCamera.pixelRect} camPos={captureCamera.transform.position} " +
                          $"proj02={captureCamera.projectionMatrix.m02:F3} proj12={captureCamera.projectionMatrix.m12:F3}");
            }
            phaseStats.Note(source, "card end", cardError, EndTolerance);
            phaseStats.Note(source, "sensor end", sensorError, EndTolerance);

            seen.Add(source);
            if (previous.TryGetValue(source, out Sample last))
            {
                if (last.Card != card)
                    phaseStats.Fail(source, "its card was swapped for another");
                else if (last.Connection == connection)
                {
                    // Cards glide at the rails' top speed at most; collisions push
                    // neighbours along with them, never further than one card's move.
                    float cardMove = (cardPoint - last.CardPoint).magnitude;
                    float allowed = 1500f * dt * 1.5f + 4f;
                    if (cardMove > allowed && phaseStats.JumpDetails < 6)
                    {
                        phaseStats.JumpDetails++;
                        RectTransform cardRect = (RectTransform)card.transform;
                        Debug.Log($"LEADER_LINE_TEST jump '{source.MetricName}' frame={Time.frameCount} " +
                                  $"rebuilt={rebuiltFrame == Time.frameCount} rail '{last.Rail}' -> '{cardRect.parent.parent.name}' " +
                                  $"anchored {last.Anchored} -> {cardRect.anchoredPosition} " +
                                  $"connection {last.CardPoint} -> {cardPoint} side={card.RailSide}");
                    }
                    phaseStats.Note(source, "card move per frame", cardMove, allowed);
                }
                phaseStats.Note(source, "sensor move per frame", (sensorPoint - last.SensorPoint).magnitude, SensorJump);
            }
            RectTransform rect = (RectTransform)card.transform;
            previous[source] = new Sample(card, connection, cardPoint, sensorPoint, rect.parent.parent.name, rect.anchoredPosition);
        }

        foreach (PerformanceStatSource gone in previous.Keys.Where(source => !seen.Contains(source)).ToList())
            previous.Remove(gone);
    }

    private static Vector2 ToLayer(RectTransform layer, Vector2 screen)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(layer, screen, UICamera, out Vector2 local);
        return local;
    }

    /// <summary>
    /// Moves a point in front of a camera, out past the screen edge, through
    /// its plane and behind it. The placement point must move a little each
    /// time, never leap.
    /// </summary>
    private static void SweepThroughCameraPlane()
    {
        GameObject probe = new("Placement Probe Camera");
        Camera camera = probe.AddComponent<Camera>();
        camera.enabled = false;
        camera.fieldOfView = 50f;
        camera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        float worst = 0f;
        foreach (Vector2 sideways in new[] { new Vector2(1f, 0f), new Vector2(0.6f, 0.8f), new Vector2(-0.3f, -1f) })
        {
            Vector2 last = default;
            bool first = true;
            for (float depth = 6f; depth >= -6f; depth -= 0.01f)
            {
                Vector3 world = new(sideways.x * 1.5f, sideways.y * 1.5f, depth);
                Vector2 point = TelemetryPresentationBuilder.PlacementScreenPoint(camera, world);
                if (!first)
                    worst = Mathf.Max(worst, (point - last).magnitude);
                last = point;
                first = false;
            }
        }
        Object.DestroyImmediate(probe);
        Debug.Log($"LEADER_LINE_TEST sweep through the camera plane: largest step {worst:F1} px");
        Expect(worst < 40f, "the placement point runs on smoothly past the screen edge and behind the camera");
    }

    // -----------------------------------------------------------------------
    // Setup and helpers
    // -----------------------------------------------------------------------

    private static void Prepare()
    {
        // Measured as the app runs: the camera draws to the window and the UI
        // is a screen-space overlay. Captures switch to a camera canvas for a moment.
        captureCamera = Camera.main;
        if (captureCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            cameraData.renderPostProcessing = false;
        mainCanvas = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas");
        SetWindow(1920, 1080);

        rails = StatRailManagers.Wide();
        panels = Object.FindAnyObjectByType<TelemetryDetailPanelController>();
        orbit = Object.FindAnyObjectByType<OrbitCameraController>();
        rails.LayoutRebuilt += () => rebuiltFrame = Time.frameCount;
        Canvas.willRenderCanvases -= SnapshotLayout;
        Canvas.willRenderCanvases += SnapshotLayout;
        RenderPipelineManager.endContextRendering -= OnFrameRendered;
        RenderPipelineManager.endContextRendering += OnFrameRendered;
        Debug.Log($"LEADER_LINE_TEST rails={(rails != null)} panels={(panels != null)} orbit={(orbit != null)}");
    }

    private static Canvas mainCanvas;

    private static Camera UICamera =>
        mainCanvas == null || mainCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : mainCanvas.worldCamera;

    /// <summary>Sizes the Game view, as a browser window would be; the responsive shell follows.</summary>
    private static void SetWindow(int width, int height)
    {
        PlayModeWindow.SetCustomRenderingResolution((uint)width, (uint)height, "Leader line test");
    }

    private static IEnumerable<PerformanceStatCardView> OwnedCards() =>
        Object.FindObjectsByType<PerformanceStatCardView>().Where(card => card.isActiveAndEnabled && rails.OwnsCard(card));

    private static void Click(PerformanceStatCardView card)
    {
        Vector2 position = RectTransformUtility.WorldToScreenPoint(UICamera, card.transform.position);
        PointerEventData data = new(EventSystem.current)
        {
            button = PointerEventData.InputButton.Left,
            position = position,
            pressPosition = position,
            clickCount = 1
        };
        ExecuteEvents.Execute(card.gameObject, data, ExecuteEvents.pointerClickHandler);
    }

    private static void Capture(string name)
    {
        // The camera's own size: outside the player loop Screen reports the editor window.
        RenderTexture target = new(captureCamera.pixelWidth, captureCamera.pixelHeight, 24, RenderTextureFormat.ARGB32);
        RenderMode mode = mainCanvas.renderMode;
        mainCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        mainCanvas.worldCamera = captureCamera;
        mainCanvas.planeDistance = captureCamera.nearClipPlane + 1f;
        captureCamera.targetTexture = target;
        Canvas.ForceUpdateCanvases();
        captureCamera.Render();
        RenderTexture.active = target;
        Texture2D image = new(target.width, target.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
        Object.DestroyImmediate(image);
        captureCamera.targetTexture = null;
        target.Release();
        mainCanvas.renderMode = mode;
        Canvas.ForceUpdateCanvases();
    }

    private static void Report()
    {
        foreach (KeyValuePair<string, PhaseStats> entry in stats)
        {
            PhaseStats phaseStats = entry.Value;
            Debug.Log($"LEADER_LINE_TEST phase '{entry.Key}': frames={phaseStats.Frames} rebuilds={phaseStats.Rebuilds} " +
                      string.Join(" ", phaseStats.Worst.Select(w => $"{w.Key}={w.Value:F2}")));
            Debug.Log($"LEADER_LINE_TEST   counts: " + string.Join(" ", phaseStats.Counts.Select(c => $"{c.Key}={c.Value}")));
            foreach (string problem in phaseStats.Problems.Where(p => p != null))
                Debug.Log($"LEADER_LINE_TEST   {problem}");
            Expect(phaseStats.Frames > 30, $"'{entry.Key}' measured enough frames");
            Expect(phaseStats.Problems.Count == 0,
                $"'{entry.Key}': every line meets its card and sensor in the same frame, and nothing jumps " +
                $"({phaseStats.Problems.Count} problems)");
        }
    }

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            failures++;
        Debug.Log($"LEADER_LINE_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        RenderPipelineManager.endContextRendering -= OnFrameRendered;
        Canvas.willRenderCanvases -= SnapshotLayout;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"LEADER_LINE_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }

    private readonly struct Sample
    {
        public readonly PerformanceStatCardView Card;
        public readonly RectTransform Connection;
        public readonly Vector2 CardPoint;
        public readonly Vector2 SensorPoint;
        public readonly string Rail;
        public readonly Vector2 Anchored;

        public Sample(PerformanceStatCardView card, RectTransform connection, Vector2 cardPoint, Vector2 sensorPoint,
            string rail, Vector2 anchored)
        {
            Rail = rail;
            Anchored = anchored;
            Card = card;
            Connection = connection;
            CardPoint = cardPoint;
            SensorPoint = sensorPoint;
        }
    }

    private sealed class PhaseStats
    {
        public int Frames;
        public int Rebuilds;
        public readonly Dictionary<string, float> Worst = new();
        public readonly List<string> Problems = new();

        public bool Dumped;
        public int JumpDetails;
        public readonly Dictionary<string, int> Counts = new();

        public void Note(PerformanceStatSource source, string measure, float value, float limit)
        {
            Worst[measure] = Mathf.Max(Worst.TryGetValue(measure, out float worst) ? worst : 0f, value);
            if (value <= limit)
                return;
            Counts[measure] = (Counts.TryGetValue(measure, out int count) ? count : 0) + 1;
            if (Counts[measure] <= 4)
                Problems.Add($"frame {Time.frameCount}{(rebuiltFrame == Time.frameCount ? " (rebuilt)" : "")} " +
                             $"'{source.MetricName}' {measure} {value:F1} > {limit:F1}");
            else
                Problems.Add(null);
        }

        public void Fail(PerformanceStatSource source, string what) =>
            Problems.Add($"frame {Time.frameCount} '{source.MetricName}' {what}");
    }
}
