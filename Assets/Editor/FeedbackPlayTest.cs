using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Batch-only play test of the language table and the AI data table in the
/// sample scene. Loads the shipped table, then a pseudo-translation whose
/// every string is about half as long again, and checks the cards, controls,
/// detail panel and service panel show it and lay out without overlap; goes
/// back to English and checks the copy is exactly restored; feeds bad tables;
/// sends an AI insight to a detail panel; and checks the AI data table the
/// page receives. Saves a 1920x1080 capture of each step to
/// Logs/FeedbackPlayTest and exits. Never saves the scene.
///
///   Unity -batchmode -projectPath . -executeMethod FeedbackPlayTest.Run
///
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class FeedbackPlayTest
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string OutputDirectory = "Logs/FeedbackPlayTest";
    private const string ActiveKey = "FeedbackPlayTest.Active";
    private const string StartKey = "FeedbackPlayTest.Start";
    private const string Pseudo = "qa";
    private const string LongWordKey = "sensor.VIB-01.MOTOR.SPEED";
    private const string LongWord = "[MOTORDREHZAHLUEBERWACHUNGSMESSWERT]";

    private static int step;
    private static float stepTime;
    private static int failures;
    private static int errors;
    private static Camera captureCamera;
    private static UITextTableLoader loader;
    private static TelemetryAiDataTable aiTable;
    private static TelemetryDetailPanelController detailPanels;
    private static ServiceModeController serviceMode;
    private static ServicePanelView servicePanel;
    private static PerformanceStatCardView clickedCard;
    private static readonly List<string> overflowReport = new();
    private static Dictionary<string, string> englishSnapshot = new();
    private static List<string> englishCards = new();

    static FeedbackPlayTest()
    {
        if (!SessionState.GetBool(ActiveKey, false))
            return;
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        Application.logMessageReceived -= CountErrors;
        Application.logMessageReceived += CountErrors;
    }

    public static void Run()
    {
        Directory.CreateDirectory(OutputDirectory);
        foreach (string old in Directory.GetFiles(OutputDirectory))
            File.Delete(old);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void CountErrors(string message, string stackTrace, LogType type)
    {
        if (type is LogType.Error or LogType.Exception or LogType.Assert)
        {
            errors++;
            File.AppendAllText(Path.Combine(OutputDirectory, "errors.txt"), $"{type}: {message}\n{stackTrace}\n");
        }
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

        try
        {
            Advance();
        }
        catch (System.Exception exception)
        {
            Debug.Log($"FEEDBACK_PLAY_TEST FAIL: step {step} threw {exception}");
            failures++;
            Finish("failed");
        }
    }

    private static void Advance()
    {
        switch (step)
        {
            case 0:
                PrepareCapture();
                Object.FindAnyObjectByType<ViewModeSegmentedControl>().SelectSection();
                Next(4.5f);
                break;

            case 1:
            {
                Expect(loader != null, "the language table loader started by itself");
                Expect(loader != null && loader.IsLoaded && loader.LastError == null,
                    $"the shipped table loaded from StreamingAssets ({loader?.LastError})");
                Expect(UIText.Language == "en" && UIText.HasTable, "English is shown by default");
                Expect(aiTable != null, "the AI data table started by itself");
                englishSnapshot = SnapshotTexts();
                englishCards = CardLayout();
                Capture("01-english");

                clickedCard = Cards().FirstOrDefault(c => c.BoundSource != null && c.BoundSource.StatId == "VIB-01.MOTOR.TEMP")
                              ?? Cards().First();
                aiTable.PushAiInsight("{\"sensorId\":\"" + clickedCard.BoundSource.StatId +
                                      "\",\"text\":\"Temperature follows load; no action needed. <b>test</b>\"}");
                Click(clickedCard);
                Next(1.6f);
                break;
            }

            case 2:
            {
                Capture("02-english-detail-with-ai-insight");
                TelemetryDetailPanel panel = detailPanels.OpenPanels.FirstOrDefault();
                Expect(panel != null, "a detail panel opened on the card");
                TMP_Text detail = panel != null ? Field<TMP_Text>(panel, "detailText") : null;
                Expect(detail != null && detail.gameObject.activeInHierarchy && detail.text.StartsWith("AI · Temperature follows load"),
                    $"the AI insight shows in the detail panel ('{detail?.text}')");

                string pseudoTable = BuildPseudoTable(File.ReadAllText(LanguageTableTool.TablePath));
                File.WriteAllText(Path.Combine(OutputDirectory, "pseudo_table.csv"), pseudoTable);
                loader.LoadLanguageCsv(pseudoTable);
                loader.SetLanguage(Pseudo);
                Expect(UIText.Language == Pseudo, "the page switched to the pseudo language");
                Next(1.6f);
                break;
            }

            case 3:
            {
                Capture("03-pseudo-telemetry-detail");
                CheckPseudo("telemetry view");
                PerformanceStatCardView longCard = Cards().FirstOrDefault(c => c.BoundSource.StatId == "VIB-01.MOTOR.SPEED");
                TMP_Text longLabel = longCard != null
                    ? longCard.GetComponentsInChildren<TMP_Text>().FirstOrDefault(t => t.text == LongWord)
                    : null;
                Expect(longCard == null || (longLabel != null && longLabel.textWrappingMode == TextWrappingModes.NoWrap),
                    $"a single word wider than its card slides instead of breaking mid-word (card shown: {longCard != null})");
                ReportOverflow("pseudo telemetry");
                serviceMode.SetMode(DigitalTwinMode.Service);
                Next(1.4f);
                break;
            }

            case 4:
            {
                Capture("04-pseudo-service-list");
                CheckPseudo("service list");
                ReportOverflow("pseudo service list");

                // Wheel scrolling is 1:1: a given distance moves the list exactly that far, and stops at its ends.
                ScrollRect list = servicePanel.List;
                UIWheelScroll wheel = list != null ? list.GetComponent<UIWheelScroll>() : null;
                Expect(wheel != null, "the service list scrolls with the 1:1 wheel handler");
                if (wheel != null)
                {
                    float scale = list.GetComponentInParent<Canvas>().rootCanvas.scaleFactor;
                    float range = list.content.rect.height - list.viewport.rect.height;
                    list.content.anchoredPosition = Vector2.zero;
                    wheel.ScrollByScreenPixels(10f);
                    float moved = list.content.anchoredPosition.y;
                    Expect(Mathf.Abs(moved - 10f / scale) < 0.01f,
                        $"a 10 px scroll moves the list 10 px, not to its end (moved {moved * scale:0.##} px of a {range * scale:0} px list)");
                    wheel.ScrollByScreenPixels(1e6f);
                    Expect(Mathf.Abs(list.content.anchoredPosition.y - range) < 0.01f, "scrolling past the end stops at the end");
                    wheel.ScrollByScreenPixels(-1e6f);
                    Expect(Mathf.Abs(list.content.anchoredPosition.y) < 0.01f, "scrolling past the start stops at the start");
                }
                MachinePartGroup part = Object.FindObjectsByType<MachinePartGroup>()
                    .FirstOrDefault(p => p.DisplayName == "Bearings") ?? Object.FindAnyObjectByType<MachinePartGroup>();
                servicePanel.ShowDetail(part);
                Next(1.2f);
                break;
            }

            case 5:
            {
                Capture("05-pseudo-service-detail");
                Expect(servicePanel.DetailOpen && servicePanel.DetailStateText.StartsWith("["),
                    $"the part detail shows pseudo copy ('{servicePanel.DetailStateText}')");
                CheckPseudo("service detail");
                ReportOverflow("pseudo service detail");
                loader.SetLanguage("en");
                Next(1.4f);
                break;
            }

            case 6:
            {
                Capture("06-english-service-detail");
                Expect(!AllTexts().Any(t => t.text.Contains("[") && t.text.Contains("]") && t.text.Contains("~")),
                    "no pseudo copy is left after switching back to English");
                serviceMode.SetMode(DigitalTwinMode.Telemetry);
                Next(1.4f);
                break;
            }

            case 7:
            {
                Dictionary<string, string> restored = SnapshotTexts();
                List<string> changed = englishSnapshot.Where(pair => restored.TryGetValue(pair.Key, out string now) &&
                                                                     now != pair.Value && !IsLiveValue(pair.Key))
                    .Select(pair => $"{pair.Key}: '{pair.Value}' -> '{restored[pair.Key]}'").ToList();
                Expect(changed.Count == 0, $"English copy is exactly restored ({string.Join(" | ", changed.Take(6))})");
                List<string> cards = CardLayout();
                Expect(cards.SequenceEqual(englishCards),
                    $"the same cards sit in the same places as before the switch (now {string.Join(", ", cards)}; before {string.Join(", ", englishCards)})");
                List<string> shrunk = Object.FindObjectsByType<ViewModeSegmentedControl>()
                    .SelectMany(c => c.GetComponentsInChildren<TMP_Text>()).Where(t => t.isActiveAndEnabled)
                    .Where(t => t.fontSizeMax < 20f).Select(t => $"{t.text}={t.fontSizeMax}").ToList();
                Expect(shrunk.Count == 0, $"segment labels grow back to their authored size ({string.Join(", ", shrunk)})");
                Capture("07-english-restored");

                // Bad tables from the page are refused; the table in use stays.
                int keys = UIText.Keys.Count();
                foreach (string bad in new[] { "", "not,a,table", "key,en\na,\"open", "key,notes\na,b", null })
                    loader.LoadLanguageCsv(bad);
                Expect(UIText.Keys.Count() == keys && UIText.Language == "en", "bad tables from the page are refused");
                loader.SetLanguage("xx");
                Expect(UIText.Language == "en", "an unknown language keeps English");

                // Rapid switching in one frame must leave the last choice shown.
                string pseudoTable = BuildPseudoTable(File.ReadAllText(LanguageTableTool.TablePath));
                loader.LoadLanguageCsv(pseudoTable);
                for (int i = 0; i < 60; i++)
                    loader.SetLanguage(i % 2 == 0 ? Pseudo : "en");
                loader.ReloadLanguageTable();
                Next(1.5f);
                break;
            }

            case 8:
            {
                Expect(UIText.Language == "en" && UIText.Get("service.title", "") == "SERVICE SCHEDULE",
                    "reloading the table from StreamingAssets keeps the language and restores the shipped copy");
                Capture("08-after-rapid-switching");

                Expect(!string.IsNullOrEmpty(aiTable.LatestJson), "the AI data table published to the page by itself");
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                const int builds = 20;
                for (int i = 0; i < builds; i++)
                    aiTable.BuildJson(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), false);
                double perBuild = watch.Elapsed.TotalMilliseconds / builds;
                Debug.Log($"FEEDBACK_PLAY_TEST periodic AI publish costs {perBuild:0.00} ms per build");
                Expect(perBuild < 8d, $"the periodic AI publish stays under half a 60 fps frame ({perBuild:0.00} ms)");
                File.WriteAllText(Path.Combine(OutputDirectory, "ai_latest.json"), aiTable.LatestJson ?? string.Empty);
                string bundle = aiTable.BuildJson(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), true);
                File.WriteAllText(Path.Combine(OutputDirectory, "ai_full.json"), bundle);
                TelemetryRegistry registry = Object.FindAnyObjectByType<TelemetryRegistry>();
                TelemetryAiTable sensors = aiTable.BuildSensorTable(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                Expect(sensors.Rows.Count == registry.Sources.Count(s => s != null), $"one AI row per sensor ({sensors.Rows.Count})");
                int generated = aiTable.BuildSeriesTable(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Rows
                    .Count(r => r[r.Length - 1] is true);
                Expect(generated == 0, "the AI series leaves the generated backfill out");
                int withService = sensors.Rows.Count(r => r[sensors.IndexOf("hours_to_service")] != null);
                Expect(withService > 0, $"sensors carry their part's service position ({withService})");
                Expect(aiTable.BuildMaintenanceTable(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Rows.Count > 0,
                    "the maintenance table lists the service plans");
                foreach (string table in new[] { "sensors", "maintenance", "series", "all" })
                    aiTable.DownloadAiTable(table);
                aiTable.SetAiWindows("5,30");
                TelemetryAiTable rewindowed = aiTable.BuildSensorTable(System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                Expect(rewindowed.IndexOf("w5_mean") >= 0 && rewindowed.IndexOf("w30_slope_per_h") >= 0 && rewindowed.IndexOf("w15_mean") < 0,
                    "the page can change the windows at run time");
                Next(0.2f);
                break;
            }

            default:
                Expect(errors == 0, $"no errors or exceptions were logged ({errors}; see {OutputDirectory}/errors.txt)");
                File.WriteAllText(Path.Combine(OutputDirectory, "overflow_report.txt"), string.Join("\n", overflowReport));
                Finish(failures == 0 ? "passed" : "failed");
                break;
        }
    }

    // -----------------------------------------------------------------------
    // Pseudo-translation
    // -----------------------------------------------------------------------

    /// <summary>
    /// The shipped table with a qa column: every English cell bracketed and
    /// padded to about 1.5 times its length, placeholders and markup kept.
    /// </summary>
    private static string BuildPseudoTable(string csv)
    {
        List<List<string>> records = UIText.ParseCsv(csv);
        StringBuilder builder = new();
        int en = -1;
        foreach (List<string> record in records)
        {
            if (record.Count == 0 || record[0].TrimStart().StartsWith("#"))
                continue;
            if (record[0] == "key")
            {
                en = record.IndexOf("en");
                builder.Append("key,en,").Append(Pseudo).Append('\n');
                continue;
            }
            string english = en >= 0 && en < record.Count ? record[en] : string.Empty;
            // One label is a single word far wider than a card, as long compound words are.
            string pseudo = record[0] == LongWordKey ? LongWord : Expand(english);
            builder.Append(UIText.EscapeCsv(record[0])).Append(',')
                .Append(UIText.EscapeCsv(english)).Append(',')
                .Append(UIText.EscapeCsv(pseudo)).Append('\n');
        }
        return builder.ToString();
    }

    private static string Expand(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;
        int padding = Mathf.Max(2, Mathf.CeilToInt(text.Length * 0.5f));
        return "[" + text + " " + new string('~', padding) + "]";
    }

    private static void CheckPseudo(string where)
    {
        List<TMP_Text> keyed = AllTexts().Where(t => t.TryGetComponent(out UITextBinding _)).ToList();
        List<string> untranslated = keyed.Where(t => !t.text.StartsWith("[")).Select(t => $"{t.name}='{t.text}'").ToList();
        Expect(untranslated.Count == 0, $"{where}: every bound label shows the pseudo copy ({keyed.Count} labels; {string.Join(", ", untranslated.Take(6))})");

        List<PerformanceStatCardView> cards = Cards().ToList();
        if (serviceMode.CurrentMode == DigitalTwinMode.Telemetry)
        {
            List<string> cardLabels = cards.SelectMany(c => c.GetComponentsInChildren<TMP_Text>())
                .Where(t => t.name.Contains("Metric") || t.name.Contains("Label"))
                .Select(t => t.text).Where(text => text.Length > 0).ToList();
            Expect(cards.Count > 0 && cardLabels.Count > 0 && cardLabels.All(text => text.StartsWith("[")),
                $"{where}: every card's sensor label is translated ({cardLabels.Count} labels, e.g. '{cardLabels.FirstOrDefault()}')");

            List<string> segments = Object.FindObjectsByType<ViewModeSegmentedControl>()
                .SelectMany(c => c.GetComponentsInChildren<TMP_Text>()).Where(t => t.isActiveAndEnabled).Select(t => t.text).ToList();
            Expect(segments.Count > 0 && segments.All(text => text.StartsWith("[")), $"{where}: view mode segments are translated ({string.Join(", ", segments)})");

            ConnectionHealthView health = Object.FindAnyObjectByType<ConnectionHealthView>();
            if (health != null)
            {
                List<string> values = health.GetComponentsInChildren<TMP_Text>().Where(t => t.isActiveAndEnabled)
                    .Select(t => t.text).Where(text => text.Length > 0).ToList();
                Debug.Log($"FEEDBACK_PLAY_TEST health texts: {string.Join(" | ", values)}");
            }
        }
    }

    /// <summary>
    /// Labels whose text runs past their own box, or past a parent's box they
    /// are clipped to: listed for review, and a failure when a label set to
    /// overflow spills far beyond its parent.
    /// </summary>
    private static void ReportOverflow(string where)
    {
        int severe = 0;
        foreach (TMP_Text text in AllTexts())
        {
            if (string.IsNullOrEmpty(text.text) || text.GetComponentInParent<Canvas>() == null)
                continue;
            // A label inside a mask (a card's sliding label) is clipped and scrolls by design.
            if (text.GetComponentInParent<RectMask2D>() != null || text.GetComponentInParent<Mask>() != null)
                continue;
            text.ForceMeshUpdate();
            Rect box = text.rectTransform.rect;
            Bounds bounds = text.textBounds;
            float spill = Mathf.Max(bounds.max.x - box.xMax, box.xMin - bounds.min.x);
            if (spill > 2f && text.overflowMode == TextOverflowModes.Overflow && !text.enableAutoSizing)
            {
                overflowReport.Add($"{where}: '{HierarchyPath(text.transform)}' spills {spill:0}px ('{text.text}')");
                if (text.transform.parent is RectTransform parent && spill > parent.rect.width * 0.5f)
                    severe++;
            }
            if (text.isTextTruncated || text.firstOverflowCharacterIndex >= 0)
                overflowReport.Add($"{where}: '{HierarchyPath(text.transform)}' cut short ('{text.text}')");
        }
        Expect(severe == 0, $"{where}: no overflowing label spills past half its parent ({severe})");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool IsLiveValue(string path) =>
        path.Contains("Value") || path.Contains("Hours") || path.Contains("Run Hours") || path.Contains("Figure") ||
        path.Contains("/Y") || path.Contains("Date") || path.Contains("/X") || path.Contains("FPS") || path.Contains("Number");

    private static Dictionary<string, string> SnapshotTexts()
    {
        Dictionary<string, string> texts = new();
        // Cards are pooled and rebound as the rails lay out; they are compared
        // by position in CardLayout instead.
        foreach (TMP_Text text in AllTexts().Where(t => t.GetComponentInParent<PerformanceStatCardView>() == null))
            texts[HierarchyPath(text.transform) + "#" + text.GetEntityId()] = text.text;
        return texts;
    }

    /// <summary>Each shown card's rail and sensor, top to bottom.</summary>
    private static List<string> CardLayout()
    {
        WideStatRailManager rails = Object.FindAnyObjectByType<WideStatRailManager>();
        return Cards().Where(c => rails == null || rails.OwnsCard(c))
            .Select(c => (card: c, y: Mathf.Round(RectTransformUtility.WorldToScreenPoint(captureCamera, c.transform.position).y)))
            .OrderBy(e => e.card.RailSide).ThenByDescending(e => e.y)
            .Select(e => $"{e.card.RailSide}:{e.card.BoundSource.StatId}@{e.y}").ToList();
    }

    private static IEnumerable<TMP_Text> AllTexts() =>
        Object.FindObjectsByType<TMP_Text>().Where(t => t.isActiveAndEnabled);

    private static IEnumerable<PerformanceStatCardView> Cards() =>
        Object.FindObjectsByType<PerformanceStatCardView>().Where(c => c.isActiveAndEnabled && c.BoundSource != null);

    private static string HierarchyPath(Transform transform) =>
        transform.parent == null ? transform.name : HierarchyPath(transform.parent) + "/" + transform.name;

    private static T Field<T>(object target, string name) where T : class =>
        target.GetType().GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.GetValue(target) as T;

    private static void PrepareCapture()
    {
        captureCamera = Camera.main;
        captureCamera.targetTexture = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        if (captureCamera.TryGetComponent(out UniversalAdditionalCameraData cameraData))
            cameraData.renderPostProcessing = false;
        Canvas canvas = Object.FindObjectsByType<Canvas>().First(c => c.name == "Main UI Canvas");
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = captureCamera;
        canvas.planeDistance = captureCamera.nearClipPlane + 1f;

        loader = Object.FindAnyObjectByType<UITextTableLoader>();
        aiTable = Object.FindAnyObjectByType<TelemetryAiDataTable>();
        detailPanels = Object.FindAnyObjectByType<TelemetryDetailPanelController>();
        serviceMode = Object.FindAnyObjectByType<ServiceModeController>();
        // The wide layout's panel; the portrait layout has one of its own.
        servicePanel = Object.FindObjectsByType<ServicePanelView>(FindObjectsInactive.Include)
            .FirstOrDefault(panel => panel.transform.parent != null && panel.transform.parent.name == "Wide layout");
        SerializedObject data = new(detailPanels);
        data.FindProperty("secondaryRails").enumValueIndex = (int)SecondaryRailUse.DetailPanel;
        data.ApplyModifiedProperties();
        // The rails measured the batch window; lay them out for the capture size.
        Object.FindAnyObjectByType<WideStatRailManager>()?.RebuildLayout();
    }

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
        File.WriteAllBytes(System.IO.Path.Combine(OutputDirectory, name + ".png"), image.EncodeToPNG());
        Object.DestroyImmediate(image);
    }

    private static void Next(float delay)
    {
        step++;
        stepTime = Time.time + delay;
    }

    private static void Expect(bool condition, string description)
    {
        if (!condition)
            failures++;
        Debug.Log($"FEEDBACK_PLAY_TEST {(condition ? "ok" : "FAIL")}: {description}");
    }

    private static void Finish(string result)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= CountErrors;
        SessionState.EraseBool(ActiveKey);
        Debug.Log($"FEEDBACK_PLAY_TEST_RESULT {result}");
        EditorApplication.Exit(result == "passed" ? 0 : 1);
    }
}
