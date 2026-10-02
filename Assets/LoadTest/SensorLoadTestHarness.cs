#if DIGITAL_TWIN_LOAD_TEST || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using Debug = UnityEngine.Debug;

/// <summary>
/// Sensor-count load test. Compiled into load-test builds (the
/// DIGITAL_TWIN_LOAD_TEST define) and the editor, never into the release
/// build. In the editor it stays inert unless -loadtest is passed.
///
/// Opt in with a query string on the WebGL page, for example
///   index.html?loadtest=30,60,120,250,500,1000&amp;hz=2
/// or, in the editor, the command-line argument -loadtest 30,60.
///
/// For each level it clones the machine's real sensors up to that count
/// (clones keep their template's machine part as their anchor), rebuilds
/// the layout, then feeds every sensor through the production JSON
/// ingestion path (TelemetryJsonIngestor.PushBatchJson) at hz readings per
/// sensor per second while a rotating 5% of sensors go into alarm and the
/// camera orbits slowly. After a warm-up it records frame times.
///
/// Results are logged as one "LOADTEST {json}" line per level.
/// Startup timing is logged as "LOADTEST_STARTUP" on every load, with or
/// without the query string.
/// </summary>
public sealed class SensorLoadTestHarness : MonoBehaviour
{
    [Serializable]
    private sealed class LevelResult
    {
        public int sensors;
        public int visibleCards;
        public int pages;
        public string layout;
        public string quality;
        public int screenWidth;
        public int screenHeight;
        public int targetFrameRate;
        public float readingsPerSecond;
        public float rebuildMs;
        public int frames;
        public float averageFps;
        public float medianFrameMs;
        public float p95FrameMs;
        public float p99FrameMs;
        public float worstFrameMs;
        public float onePercentLowFps;
        public float ingestMsPerSecond;
        public float harnessJsonMsPerSecond;
        public int acceptedSample;
        public int gcCollections;
        public long managedHeapBytes;
        public bool orbiting;
    }

    private static long sceneLoadedUnixMs;

    private readonly List<PerformanceStatSource> templates = new();
    private readonly List<PerformanceStatSource> sensors = new();
    private readonly List<float> normalValues = new();
    private readonly List<float> alarmValues = new();
    private readonly List<float> frameTimes = new(8192);
    private readonly StringBuilder json = new(256 * 1024);

    private int[] levels;
    private float readingsHz = 2f;
    private float warmupSeconds = 4f;
    private float measureSeconds = 10f;
    private float alarmFraction = 0.05f;
    private float orbitDegreesPerSecond = 10f;

    private TelemetryRegistry registry;
    private TelemetryJsonIngestor ingestor;
    private WideStatRailManager wide;
    private PortraitStatRailManager portrait;
    private OrbitCameraController orbit;
    private FieldInfo desiredYaw;
    private bool orbitActive;

    private ulong sequence;
    private float nextFeedTime;
    private int alarmEpoch;
    private float nextAlarmRotation;
    private readonly Stopwatch ingestWatch = new();
    private readonly Stopwatch jsonWatch = new();
    private string status = "LOAD TEST: waiting for intro";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
#if UNITY_EDITOR && !DIGITAL_TWIN_LOAD_TEST
        if (ReadOption("loadtest") == null)
            return;
#endif
        sceneLoadedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        GameObject host = new("Sensor Load Test");
        DontDestroyOnLoad(host);
        SensorLoadTestHarness harness = host.AddComponent<SensorLoadTestHarness>();
        harness.levels = ParseLevels(ReadOption("loadtest"));
        harness.readingsHz = ReadFloat("hz", harness.readingsHz);
        harness.warmupSeconds = ReadFloat("warmup", harness.warmupSeconds);
        harness.measureSeconds = ReadFloat("measure", harness.measureSeconds);
        harness.alarmFraction = ReadFloat("alarms", harness.alarmFraction);
        harness.orbitDegreesPerSecond = ReadFloat("orbit", harness.orbitDegreesPerSecond);
    }

    private IEnumerator Start()
    {
        // Batch-mode editors render nothing, so end-of-frame never arrives there.
        if (Application.isBatchMode)
            yield return null;
        else
            yield return new WaitForEndOfFrame();
        long firstFrame = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Debug.Log(
            $"LOADTEST_STARTUP sceneLoadedUnixMs={sceneLoadedUnixMs} firstFrameUnixMs={firstFrame}");

        if (levels == null || levels.Length == 0)
        {
            Destroy(gameObject);
            yield break;
        }

        // Let the camera intro and the first layout pass settle.
        yield return new WaitForSecondsRealtime(8f);

        if (!Resolve())
        {
            Debug.LogError("LOADTEST_ERROR missing registry, ingestor or rail managers");
            yield break;
        }

        foreach (int level in levels)
        {
            float rebuildMs = GrowTo(level);
            status = $"LOAD TEST: {sensors.Count} sensors, warming up";
            yield return Feed(warmupSeconds, false);
            status = $"LOAD TEST: {sensors.Count} sensors, measuring";
            int gcBefore = GC.CollectionCount(0);
            ingestWatch.Reset();
            jsonWatch.Reset();
            frameTimes.Clear();
            yield return Feed(measureSeconds, true);
            LevelResult result = Summarise(rebuildMs, GC.CollectionCount(0) - gcBefore);
            Debug.Log("LOADTEST " + JsonUtility.ToJson(result));
        }

        status = "LOAD TEST: done";
        Debug.Log("LOADTEST_DONE");
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(12, 12, 600, 24), status);
    }

    private bool Resolve()
    {
        registry = FindAnyObjectByType<TelemetryRegistry>(FindObjectsInactive.Include);
        ingestor = FindAnyObjectByType<TelemetryJsonIngestor>(FindObjectsInactive.Include);
        wide = FindAnyObjectByType<WideStatRailManager>(FindObjectsInactive.Include);
        portrait = FindAnyObjectByType<PortraitStatRailManager>(FindObjectsInactive.Include);
        orbit = FindAnyObjectByType<OrbitCameraController>();
        if (registry == null || ingestor == null || (wide == null && portrait == null))
            return false;

        registry.RebuildIndex();
        foreach (PerformanceStatSource source in registry.Sources)
        {
            if (source == null || !source.IsVisible)
                continue;
            templates.Add(source);
            Track(source);
        }

        desiredYaw = typeof(OrbitCameraController).GetField(
            "desiredYaw", BindingFlags.Instance | BindingFlags.NonPublic);
        orbitActive = orbit != null && desiredYaw != null && orbitDegreesPerSecond > 0f;
        return templates.Count > 0;
    }

    private void Track(PerformanceStatSource source)
    {
        sensors.Add(source);
        normalValues.Add(source.CurrentValue);
        alarmValues.Add(AlarmValue(source));
    }

    private static float AlarmValue(PerformanceStatSource source)
    {
        TelemetryLimits limits = source.Limits;
        return limits.Mode switch
        {
            TelemetryLimitMode.LowOnly => limits.CriticalBelow - Mathf.Abs(limits.CriticalBelow) * 0.05f - 0.1f,
            TelemetryLimitMode.Disabled => source.CurrentValue,
            _ => limits.CriticalAbove + Mathf.Abs(limits.CriticalAbove) * 0.05f + 0.1f
        };
    }

    private float GrowTo(int target)
    {
        int index = sensors.Count;
        while (sensors.Count < target)
        {
            PerformanceStatSource template = templates[index % templates.Count];
            GameObject clone = Instantiate(template.gameObject, template.transform.parent);
            clone.name = $"Load Test Sensor {index + 1:0000}";
            clone.transform.position = template.transform.position +
                UnityEngine.Random.insideUnitSphere * 0.12f;
            PerformanceStatSource source = clone.GetComponent<PerformanceStatSource>();
            source.ConfigureLoadTestIdentity(
                $"LT-{index + 1:0000}", template.AuthoredMetricName);
            Track(source);
            index++;
        }

        Stopwatch watch = Stopwatch.StartNew();
        registry.RebuildIndex();
        if (wide != null && wide.isActiveAndEnabled)
            wide.RebuildLayout();
        if (portrait != null && portrait.isActiveAndEnabled)
            portrait.RebuildLayout();
        watch.Stop();
        return (float)watch.Elapsed.TotalMilliseconds;
    }

    private IEnumerator Feed(float seconds, bool record)
    {
        float end = Time.unscaledTime + seconds;
        nextFeedTime = Time.unscaledTime;
        while (Time.unscaledTime < end)
        {
            if (Time.unscaledTime >= nextAlarmRotation)
            {
                alarmEpoch++;
                nextAlarmRotation = Time.unscaledTime + 5f;
            }

            if (Time.unscaledTime >= nextFeedTime)
            {
                nextFeedTime += 1f / Mathf.Max(0.1f, readingsHz);
                PushBatch();
            }

            if (orbitActive)
            {
                float yaw = (float)desiredYaw.GetValue(orbit);
                desiredYaw.SetValue(orbit, yaw + orbitDegreesPerSecond * Time.unscaledDeltaTime);
            }

            yield return null;
            if (record)
                frameTimes.Add(Time.unscaledDeltaTime * 1000f);
        }
    }

    private void PushBatch()
    {
        jsonWatch.Start();
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        sequence++;
        int alarmModulo = alarmFraction > 0f ? Mathf.Max(1, Mathf.RoundToInt(1f / alarmFraction)) : int.MaxValue;
        json.Clear();
        json.Append("{\"schemaVersion\":1,\"readings\":[");
        for (int i = 0; i < sensors.Count; i++)
        {
            bool alarm = (i + alarmEpoch * 7) % alarmModulo == 0;
            float noise = 1f + (Mathf.PerlinNoise(i * 0.37f, Time.unscaledTime * 0.5f) - 0.5f) * 0.01f;
            float value = alarm ? alarmValues[i] : normalValues[i] * noise;
            if (i > 0)
                json.Append(',');
            json.Append("{\"schemaVersion\":1,\"sensorId\":\"").Append(sensors[i].StatId)
                .Append("\",\"value\":").Append(value.ToString("R", CultureInfo.InvariantCulture))
                .Append(",\"quality\":0,\"sourceTimestampUnixMs\":").Append(now)
                .Append(",\"statusCode\":0,\"sequenceNumber\":").Append(sequence)
                .Append(",\"providerId\":\"load-test\"}");
        }
        json.Append("]}");
        string payload = json.ToString();
        jsonWatch.Stop();

        ingestWatch.Start();
        ingestor.PushBatchJson(payload);
        ingestWatch.Stop();
    }

    private LevelResult Summarise(float rebuildMs, int gcCollections)
    {
        List<float> sorted = new(frameTimes);
        sorted.Sort();
        int n = sorted.Count;
        float total = 0f;
        foreach (float t in sorted)
            total += t;

        int worstCount = Mathf.Max(1, n / 100);
        float worstTotal = 0f;
        for (int i = n - worstCount; i < n; i++)
            worstTotal += sorted[i];

        int visible = 0;
        foreach (PerformanceStatCardView card in FindObjectsByType<PerformanceStatCardView>())
        {
            if (card.isActiveAndEnabled)
                visible++;
        }

        int accepted = 0;
        for (int i = 0; i < sensors.Count; i += Mathf.Max(1, sensors.Count / 50))
        {
            if (sensors[i].SequenceNumber == sequence || sensors[i].SequenceNumber + 1 == sequence)
                accepted++;
        }

        bool wideActive = wide != null && wide.isActiveAndEnabled;
        float seconds = Mathf.Max(0.001f, total / 1000f);
        return new LevelResult
        {
            sensors = sensors.Count,
            visibleCards = visible,
            pages = wideActive ? wide.PageCount : portrait != null ? portrait.PageCount : 0,
            layout = wideActive ? "wide" : "portrait",
            quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
            screenWidth = Screen.width,
            screenHeight = Screen.height,
            targetFrameRate = Application.targetFrameRate,
            readingsPerSecond = sensors.Count * readingsHz,
            rebuildMs = rebuildMs,
            frames = n,
            averageFps = n / seconds,
            medianFrameMs = Percentile(sorted, 0.5f),
            p95FrameMs = Percentile(sorted, 0.95f),
            p99FrameMs = Percentile(sorted, 0.99f),
            worstFrameMs = n > 0 ? sorted[n - 1] : 0f,
            onePercentLowFps = worstTotal > 0f ? 1000f / (worstTotal / worstCount) : 0f,
            ingestMsPerSecond = (float)ingestWatch.Elapsed.TotalMilliseconds / seconds,
            harnessJsonMsPerSecond = (float)jsonWatch.Elapsed.TotalMilliseconds / seconds,
            acceptedSample = accepted,
            gcCollections = gcCollections,
            managedHeapBytes = GC.GetTotalMemory(false),
            orbiting = orbitActive
        };
    }

    private static float Percentile(List<float> sorted, float p) =>
        sorted.Count == 0 ? 0f : sorted[Mathf.Clamp(Mathf.CeilToInt(p * sorted.Count) - 1, 0, sorted.Count - 1)];

    private static int[] ParseLevels(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<int>();
        List<int> parsed = new();
        foreach (string part in value.Split(','))
        {
            if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) && level > 0)
                parsed.Add(level);
        }
        parsed.Sort();
        return parsed.ToArray();
    }

    private static float ReadFloat(string key, float fallback) =>
        float.TryParse(ReadOption(key), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : fallback;

    private static string ReadOption(string key)
    {
        string url = Application.absoluteURL ?? string.Empty;
        int query = url.IndexOf('?');
        if (query >= 0)
        {
            foreach (string pair in url.Substring(query + 1).Split('&'))
            {
                int equals = pair.IndexOf('=');
                if (equals > 0 && pair.Substring(0, equals) == key)
                    return Uri.UnescapeDataString(pair.Substring(equals + 1));
            }
        }

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-" + key)
                return args[i + 1];
        }
        return null;
    }
}
#endif
