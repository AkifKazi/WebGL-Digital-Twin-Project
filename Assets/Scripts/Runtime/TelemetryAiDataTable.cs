using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>Figures computed for each sensor over each time window of the AI data table.</summary>
[Flags]
public enum TelemetryAiWindowFeature
{
    None = 0,
    Mean = 1 << 0,
    Minimum = 1 << 1,
    Maximum = 1 << 2,
    StandardDeviation = 1 << 3,
    SlopePerHour = 1 << 4,
    WarningSeconds = 1 << 5,
    CriticalSeconds = 1 << 6,
    Coverage = 1 << 7,
    All = Mean | Minimum | Maximum | StandardDeviation | SlopePerHour | WarningSeconds | CriticalSeconds | Coverage
}

/// <summary>
/// One table of the AI data set: named columns with a description each, and
/// rows of numbers, text or empty cells. Writes itself as CSV or JSON.
/// </summary>
public sealed class TelemetryAiTable
{
    public readonly struct Column
    {
        public readonly string Name;
        public readonly string Description;

        public Column(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    public readonly string Name;
    public readonly List<Column> Columns = new();
    public readonly List<object[]> Rows = new();

    public TelemetryAiTable(string name)
    {
        Name = name;
    }

    public void AddColumn(string name, string description) => Columns.Add(new Column(name, description));

    public object[] AddRow()
    {
        object[] row = new object[Columns.Count];
        Rows.Add(row);
        return row;
    }

    public int IndexOf(string column) => Columns.FindIndex(c => c.Name == column);

    public string ToCsv()
    {
        StringBuilder builder = new();
        for (int i = 0; i < Columns.Count; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append(UIText.EscapeCsv(Columns[i].Name));
        }
        builder.Append('\n');

        foreach (object[] row in Rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0)
                    builder.Append(',');
                builder.Append(UIText.EscapeCsv(CellText(row[i])));
            }
            builder.Append('\n');
        }
        return builder.ToString();
    }

    /// <summary>{"columns":[{name,description}],"rows":[{column:value}]}: rows as objects, for readers that want names.</summary>
    public void WriteJson(StringBuilder builder)
    {
        builder.Append("{\"columns\":[");
        for (int i = 0; i < Columns.Count; i++)
        {
            if (i > 0)
                builder.Append(',');
            builder.Append("{\"name\":");
            AppendJsonString(builder, Columns[i].Name);
            builder.Append(",\"description\":");
            AppendJsonString(builder, Columns[i].Description);
            builder.Append('}');
        }
        builder.Append("],\"rows\":[");
        for (int r = 0; r < Rows.Count; r++)
        {
            if (r > 0)
                builder.Append(',');
            builder.Append('{');
            object[] row = Rows[r];
            for (int i = 0; i < row.Length; i++)
            {
                if (i > 0)
                    builder.Append(',');
                AppendJsonString(builder, Columns[i].Name);
                builder.Append(':');
                AppendJsonValue(builder, row[i]);
            }
            builder.Append('}');
        }
        builder.Append("]}");
    }

    public static string CellText(object value) => value switch
    {
        null => string.Empty,
        double d => double.IsNaN(d) || double.IsInfinity(d) ? string.Empty : d.ToString("0.######", CultureInfo.InvariantCulture),
        float f => float.IsNaN(f) || float.IsInfinity(f) ? string.Empty : f.ToString("0.######", CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static void AppendJsonValue(StringBuilder builder, object value)
    {
        switch (value)
        {
            case null:
                builder.Append("null");
                return;
            case bool b:
                builder.Append(b ? "true" : "false");
                return;
            case double or float or long or int:
                string text = CellText(value);
                builder.Append(text.Length == 0 ? "null" : text);
                return;
            default:
                AppendJsonString(builder, CellText(value));
                return;
        }
    }

    public static void AppendJsonString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (char c in value ?? string.Empty)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else
                        builder.Append(c);
                    break;
            }
        }
        builder.Append('"');
    }
}

/// <summary>
/// The data set an AI or automation service reads: telemetry and maintenance
/// joined into tables whose values move with time. Three tables:
///
/// <list type="bullet">
/// <item><b>sensors</b>: one row per sensor, rebuilt every
/// <see cref="publishIntervalSeconds"/>: the reading, its quality and alarm
/// state, its limits and margin, figures over each configured time window
/// (mean, minimum, maximum, spread, trend per hour, time in warning and
/// critical, coverage), a projection of when the trend reaches a warning
/// limit, and the service position of the part it is read from.</item>
/// <item><b>maintenance</b>: one row per machine part with a service plan.</item>
/// <item><b>series</b>: the recorded history of every sensor, one row per
/// sensor and time bucket, for training and trend models.</item>
/// </list>
///
/// The windows and figures are parameters (<see cref="windowMinutes"/>,
/// <see cref="windowFeatures"/>), so the columns follow what the model needs;
/// the page can change the windows at run time. Generated backfill history is
/// left out unless <see cref="includeGeneratedHistory"/> is on: a model must
/// never learn from invented data.
///
/// The hosting page receives the sensors and maintenance tables as a
/// <c>digitaltwin:ai-table</c> window event every publish, and can call
/// through <c>unityInstance.SendMessage("Digital Twin Runtime", ...)</c>:
/// <c>RequestAiTable()</c> (all three tables, as the same event),
/// <c>DownloadAiTable("sensors" | "maintenance" | "series" | "all")</c>,
/// <c>SetAiWindows("15,60,1440")</c>, and, to send results back,
/// <c>PushAiInsight(json)</c> and <c>ClearAiInsights()</c>: an insight shows in
/// the sensor's detail panel.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryAiDataTable : MonoBehaviour, ITelemetryInsightProvider
{
    public const int SchemaVersion = 1;
    public const string EventName = "digitaltwin:ai-table";

    [Header("Sources (found in the scene when empty)")]
    [SerializeField] private TelemetryRegistry registry;
    [Tooltip("Any component implementing ITelemetryHistoryProvider, normally the Telemetry History Recorder.")]
    [SerializeField] private MonoBehaviour historyProvider;
    [SerializeField] private TelemetryServiceSchedule schedule;

    [Header("Dynamic Parameters")]
    [Tooltip("Time windows, in minutes, each sensor's figures are computed over. Each adds a " +
             "set of columns named w<minutes>_<figure>, as w60_mean.")]
    [SerializeField] private List<int> windowMinutes = new() { 15, 60, 1440 };

    [Tooltip("Figures computed for each window.")]
    [SerializeField] private TelemetryAiWindowFeature windowFeatures = TelemetryAiWindowFeature.All;

    [Tooltip("Window, in minutes, whose trend projects the time to a warning limit.")]
    [SerializeField, Min(1)] private int projectionWindowMinutes = 60;

    [Tooltip("Longest projection reported, in hours. Further out it is left empty.")]
    [SerializeField, Min(1f)] private float projectionHorizonHours = 168f;

    [Tooltip("Range of the series table: its bucket size and span.")]
    [SerializeField] private TelemetryHistoryRange seriesRange = TelemetryHistoryRange.Hour;

    [Tooltip("Off, generated backfill history is left out of every figure and the series. " +
             "Turn on only to test a pipeline before real history exists.")]
    [SerializeField] private bool includeGeneratedHistory;

    [Header("Publishing")]
    [Tooltip("Seconds between rebuilds of the sensors and maintenance tables. 0 builds only on request.")]
    [SerializeField, Min(0f)] private float publishIntervalSeconds = 10f;

    [Tooltip("Sends each rebuild to the hosting page as a window event.")]
    [SerializeField] private bool publishToPage = true;

    [Header("AI Insights")]
    [Tooltip("Shows insights sent back by the page in the sensor's detail panel.")]
    [SerializeField] private bool showInsightsInDetailPanel = true;

    [Tooltip("Seconds an insight stays shown when it does not say.")]
    [SerializeField, Min(1f)] private float insightLifetimeSeconds = 900f;

    private const int MaximumWindows = 8;
    private const int MaximumWindowMinutes = 366 * 24 * 60;

    private readonly Dictionary<(PerformanceStatSource, TelemetryHistoryRange), List<TelemetryHistoryBucket>> historyCache = new();
    private readonly Stack<List<TelemetryHistoryBucket>> bucketListPool = new();
    private readonly Dictionary<string, Insight> insights = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ServiceItemStatus> statuses = new();
    private readonly List<int> activeWindows = new();
    private MachinePartGroup[] parts = Array.Empty<MachinePartGroup>();
    private ITelemetryHistoryProvider injectedHistory;
    private float nextPublishTime;
    private bool sourcesResolved;

    private readonly struct Insight
    {
        public readonly string Text;
        public readonly float ExpiresAt;

        public Insight(string text, float expiresAt)
        {
            Text = text;
            ExpiresAt = expiresAt;
        }
    }

    [Serializable]
    private sealed class InsightDto
    {
        public string sensorId;
        public string text;
        public float ttlSeconds;
        public InsightDto[] insights;
    }

    /// <summary>The last sensors and maintenance tables published, as JSON.</summary>
    public string LatestJson { get; private set; }

    /// <summary>The windows in use, in minutes, after validation.</summary>
    public IReadOnlyList<int> Windows
    {
        get
        {
            if (activeWindows.Count == 0)
                SetWindows(windowMinutes);
            return activeWindows;
        }
    }

    public event Action<TelemetryAiDataTable> Published;

    /// <summary>
    /// Reads history from <paramref name="provider"/> instead of the assigned
    /// component: a backend history service, for example. Null returns to the
    /// assigned component.
    /// </summary>
    public void UseHistoryProvider(ITelemetryHistoryProvider provider) => injectedHistory = provider;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureTable()
    {
        if (FindAnyObjectByType<TelemetryAiDataTable>(FindObjectsInactive.Include) != null)
            return;

        TelemetryRegistry sceneRegistry = FindAnyObjectByType<TelemetryRegistry>();
        if (sceneRegistry == null)
            return;

        GameObject host = GameObject.Find(UITextTableLoader.RuntimeObjectName);
        (host != null ? host : sceneRegistry.gameObject).AddComponent<TelemetryAiDataTable>();
    }

    private void OnEnable()
    {
        SetWindows(windowMinutes);
        nextPublishTime = Time.unscaledTime + Mathf.Max(1f, publishIntervalSeconds);
    }

    private void Update()
    {
        if (publishIntervalSeconds <= 0f || Time.unscaledTime < nextPublishTime)
            return;

        nextPublishTime = Time.unscaledTime + publishIntervalSeconds;
        Publish(includeSeries: false);
    }

    // -----------------------------------------------------------------------
    // Page API (unityInstance.SendMessage)
    // -----------------------------------------------------------------------

    /// <summary>Publishes all three tables to the page now.</summary>
    public void RequestAiTable() => Publish(includeSeries: true);

    /// <summary>Saves a table as CSV ("sensors", "maintenance", "series"), or all three as JSON ("all").</summary>
    public void DownloadAiTable(string table)
    {
        string name = string.IsNullOrWhiteSpace(table) ? "sensors" : table.Trim().ToLowerInvariant();
        string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        long now = UtcNow();

        if (name == "all")
        {
            SaveText($"digital-twin-ai-{stamp}.json", "application/json", BuildJson(now, true));
            return;
        }

        TelemetryAiTable built = name switch
        {
            "maintenance" => BuildMaintenanceTable(now),
            "series" => BuildSeriesTable(now),
            _ => BuildSensorTable(now)
        };
        SaveText($"digital-twin-{built.Name}-{stamp}.csv", "text/csv", built.ToCsv());
    }

    /// <summary>Replaces the windows with a comma-separated list of minutes, as "15,60,1440".</summary>
    public void SetAiWindows(string minutes)
    {
        List<int> parsed = new();
        foreach (string part in (minutes ?? string.Empty).Split(',', ';', ' '))
        {
            if (int.TryParse(part.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                parsed.Add(value);
        }

        if (!SetWindows(parsed))
            Debug.LogWarning($"AI data table: no usable windows in '{minutes}'; keeping {string.Join(",", activeWindows)}.", this);
    }

    /// <summary>
    /// Shows an AI result in a sensor's detail panel:
    /// {"sensorId":"VIB-01.MOTOR.TEMP","text":"...","ttlSeconds":600},
    /// or several as {"insights":[...]}. Empty text removes the sensor's insight.
    /// </summary>
    public void PushAiInsight(string json)
    {
        InsightDto dto;
        try
        {
            dto = JsonUtility.FromJson<InsightDto>(json ?? string.Empty);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"AI data table: rejected insight JSON: {exception.Message}", this);
            return;
        }

        if (dto == null)
            return;
        AddInsight(dto);
        if (dto.insights != null)
        {
            foreach (InsightDto item in dto.insights)
                AddInsight(item);
        }
    }

    public void ClearAiInsights() => insights.Clear();

    // -----------------------------------------------------------------------
    // Insight provider
    // -----------------------------------------------------------------------

    public bool TryGetInsight(
        PerformanceStatSource source,
        TelemetryHistoryRange range,
        IReadOnlyList<TelemetryHistoryBucket> buckets,
        out string insight)
    {
        insight = null;
        if (!showInsightsInDetailPanel || source == null || !insights.TryGetValue(source.StatId, out Insight entry))
            return false;

        if (Time.unscaledTime > entry.ExpiresAt)
        {
            insights.Remove(source.StatId);
            return false;
        }

        insight = UIText.Format("ai.insight", "AI · {0}", entry.Text);
        return true;
    }

    private void AddInsight(InsightDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.sensorId))
            return;

        string id = dto.sensorId.Trim();
        if (string.IsNullOrWhiteSpace(dto.text))
        {
            insights.Remove(id);
            return;
        }

        // Plain text only: rich-text tags from a model are shown as written.
        string text = dto.text.Trim().Replace("<", "<noparse><</noparse>");
        if (text.Length > 600)
            text = text.Substring(0, 600) + "...";
        float ttl = dto.ttlSeconds > 0f && float.IsFinite(dto.ttlSeconds) ? dto.ttlSeconds : insightLifetimeSeconds;
        insights[id] = new Insight(text, Time.unscaledTime + ttl);
    }

    // -----------------------------------------------------------------------
    // Building
    // -----------------------------------------------------------------------

    /// <summary>Builds the tables now. The series table is the large one; leave it out of frequent updates.</summary>
    public string BuildJson(long nowUnixMs, bool includeSeries)
    {
        StringBuilder builder = new();
        builder.Append("{\"schemaVersion\":").Append(SchemaVersion);
        builder.Append(",\"generatedAtUtc\":");
        TelemetryAiTable.AppendJsonString(builder, IsoTime(nowUnixMs));
        builder.Append(",\"generatedAtUnixMs\":").Append(nowUnixMs.ToString(CultureInfo.InvariantCulture));
        builder.Append(",\"windowsMinutes\":[").Append(string.Join(",", Windows)).Append(']');
        builder.Append(",\"includesGeneratedHistory\":").Append(includeGeneratedHistory ? "true" : "false");
        builder.Append(",\"tables\":{\"sensors\":");
        BuildSensorTable(nowUnixMs).WriteJson(builder);
        builder.Append(",\"maintenance\":");
        BuildMaintenanceTable(nowUnixMs).WriteJson(builder);
        if (includeSeries)
        {
            builder.Append(",\"series\":");
            BuildSeriesTable(nowUnixMs).WriteJson(builder);
        }
        builder.Append("}}");
        return builder.ToString();
    }

    public TelemetryAiTable BuildSensorTable(long nowUnixMs)
    {
        ResolveSources();
        BeginHistoryFrame();

        TelemetryAiTable table = new("sensors");
        table.AddColumn("timestamp_utc", "When the row was built (ISO 8601, UTC).");
        table.AddColumn("sensor_id", "Stable sensor identifier.");
        table.AddColumn("label", "Sensor name as authored (English).");
        table.AddColumn("unit", "Engineering unit of every value in the row.");
        table.AddColumn("value", "Latest reading.");
        table.AddColumn("quality", "Data quality of the latest reading: Good, Uncertain, Bad or Stale.");
        table.AddColumn("state", "Alarm state: Normal, Warning, Critical or Unavailable.");
        table.AddColumn("age_s", "Seconds since the latest reading was taken.");
        table.AddColumn("limit_mode", "Which limits apply: HighOnly, LowOnly, OutsideRange or Disabled.");
        table.AddColumn("warning_low", "Warning at or below this value (empty when not used).");
        table.AddColumn("critical_low", "Critical at or below this value (empty when not used).");
        table.AddColumn("warning_high", "Warning at or above this value (empty when not used).");
        table.AddColumn("critical_high", "Critical at or above this value (empty when not used).");
        table.AddColumn("margin_to_warning_pct",
            "Distance from the latest reading to the nearest warning limit, as a percent of the normal band. Negative once past it.");

        foreach (int minutes in Windows)
        {
            string p = "w" + minutes.ToString(CultureInfo.InvariantCulture) + "_";
            string span = WindowText(minutes);
            if (Has(TelemetryAiWindowFeature.Mean)) table.AddColumn(p + "mean", $"Mean over the last {span}.");
            if (Has(TelemetryAiWindowFeature.Minimum)) table.AddColumn(p + "min", $"Lowest reading over the last {span}.");
            if (Has(TelemetryAiWindowFeature.Maximum)) table.AddColumn(p + "max", $"Highest reading over the last {span}.");
            if (Has(TelemetryAiWindowFeature.StandardDeviation)) table.AddColumn(p + "std", $"Standard deviation of the bucket means over the last {span}.");
            if (Has(TelemetryAiWindowFeature.SlopePerHour)) table.AddColumn(p + "slope_per_h", $"Trend over the last {span}, in units per hour (least squares).");
            if (Has(TelemetryAiWindowFeature.WarningSeconds)) table.AddColumn(p + "warning_s", $"Seconds in warning over the last {span}.");
            if (Has(TelemetryAiWindowFeature.CriticalSeconds)) table.AddColumn(p + "critical_s", $"Seconds in critical over the last {span}.");
            if (Has(TelemetryAiWindowFeature.Coverage)) table.AddColumn(p + "coverage", $"Share (0 to 1) of the last {span} with recorded data.");
        }

        table.AddColumn("minutes_to_warning",
            $"Minutes until the {WindowText(ProjectionWindow)} trend reaches a warning limit; 0 when already past; empty when moving away or beyond {projectionHorizonHours:0} h.");
        table.AddColumn("part", "Machine part the sensor is read from with the soonest service.");
        table.AddColumn("service_task", "The part's next service task.");
        table.AddColumn("hours_to_service", "Operating hours until that service; negative when overdue.");
        table.AddColumn("service_state", "OnTrack, DueSoon or Overdue.");

        string timestamp = IsoTime(nowUnixMs);
        if (registry == null)
        {
            EndHistoryFrame();
            return table;
        }

        foreach (PerformanceStatSource source in registry.Sources)
        {
            if (source == null)
                continue;

            object[] row = table.AddRow();
            int c = 0;
            TelemetryLimits limits = source.Limits;
            row[c++] = timestamp;
            row[c++] = source.StatId;
            row[c++] = source.AuthoredMetricName;
            row[c++] = source.Unit;
            row[c++] = (double)source.CurrentValue;
            row[c++] = source.DataQuality.ToString();
            row[c++] = source.VisualState.ToString();
            row[c++] = source.SourceTimestampUnixMs > 0 ? Math.Max(0d, (nowUnixMs - source.SourceTimestampUnixMs) / 1000d) : null;
            row[c++] = limits.Mode.ToString();
            row[c++] = limits.HasLow ? limits.WarningBelow : null;
            row[c++] = limits.HasLow ? limits.CriticalBelow : null;
            row[c++] = limits.HasHigh ? limits.WarningAbove : null;
            row[c++] = limits.HasHigh ? limits.CriticalAbove : null;
            row[c++] = MarginToWarningPercent(limits, source.CurrentValue);

            foreach (int minutes in Windows)
            {
                WindowStats stats = ComputeWindow(source, minutes, nowUnixMs);
                if (Has(TelemetryAiWindowFeature.Mean)) row[c++] = stats.Samples > 0 ? stats.Mean : null;
                if (Has(TelemetryAiWindowFeature.Minimum)) row[c++] = stats.Samples > 0 ? stats.Min : null;
                if (Has(TelemetryAiWindowFeature.Maximum)) row[c++] = stats.Samples > 0 ? stats.Max : null;
                if (Has(TelemetryAiWindowFeature.StandardDeviation)) row[c++] = stats.Buckets > 0 ? stats.StandardDeviation : null;
                if (Has(TelemetryAiWindowFeature.SlopePerHour)) row[c++] = stats.HasSlope ? stats.SlopePerHour : null;
                if (Has(TelemetryAiWindowFeature.WarningSeconds)) row[c++] = (double)stats.WarningSeconds;
                if (Has(TelemetryAiWindowFeature.CriticalSeconds)) row[c++] = (double)stats.CriticalSeconds;
                if (Has(TelemetryAiWindowFeature.Coverage)) row[c++] = stats.Coverage;
            }

            WindowStats trend = ComputeWindow(source, ProjectionWindow, nowUnixMs);
            row[c++] = trend.HasSlope ? MinutesToWarning(limits, source.CurrentValue, trend.SlopePerHour) : null;

            if (TryGetSoonestService(source, out ServiceItemStatus status))
            {
                row[c++] = status.Part.DisplayName;
                row[c++] = status.Task.ToString();
                row[c++] = Math.Round(status.HoursLeft, 2);
                row[c++] = status.State.ToString();
            }
        }

        EndHistoryFrame();
        return table;
    }

    public TelemetryAiTable BuildMaintenanceTable(long nowUnixMs)
    {
        ResolveSources();
        TelemetryAiTable table = new("maintenance");
        table.AddColumn("timestamp_utc", "When the row was built (ISO 8601, UTC).");
        table.AddColumn("part", "Machine part.");
        table.AddColumn("task", "Service task: Inspect, Regrease, OilChange or Replace.");
        table.AddColumn("criticality", "Effect of a failure on production: Low, Medium or High.");
        table.AddColumn("plan_hours", "Manufacturer's service interval, operating hours.");
        table.AddColumn("estimate_hours", "Interval adjusted for operating conditions, operating hours.");
        table.AddColumn("run_hours", "Operating hours since the last service.");
        table.AddColumn("hours_left", "Operating hours to the estimated service point; negative when overdue.");
        table.AddColumn("state", "OnTrack, DueSoon or Overdue.");
        table.AddColumn("due_date_utc", "Estimated calendar date of the service (ISO 8601).");
        table.AddColumn("past_intervals_h", "Operating hours run before each earlier service, oldest first, separated by |.");
        table.AddColumn("sensor_ids", "Sensors read from the part, separated by |.");
        table.AddColumn("worst_sensor_state", "Most serious alarm state of those sensors now.");
        table.AddColumn("machine_run_hours", "Machine's total operating hours.");

        if (schedule == null)
            return table;

        string timestamp = IsoTime(nowUnixMs);
        schedule.GetStatuses(statuses);
        foreach (ServiceItemStatus status in statuses)
        {
            schedule.TryGetPlan(status.Part, out TelemetryServiceSchedule.ServicePlan plan);
            object[] row = table.AddRow();
            int c = 0;
            row[c++] = timestamp;
            row[c++] = status.Part.DisplayName;
            row[c++] = status.Task.ToString();
            row[c++] = plan.criticality.ToString();
            row[c++] = Math.Round(status.PlanHours, 2);
            row[c++] = Math.Round(status.EstimateHours, 2);
            row[c++] = Math.Round(status.RunHours, 2);
            row[c++] = Math.Round(status.HoursLeft, 2);
            row[c++] = status.State.ToString();
            row[c++] = status.HoursLeft > 0d
                ? schedule.DateAfter(status.HoursLeft).ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : schedule.DateBefore(-status.HoursLeft).ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            row[c++] = JoinIntervals(plan.pastIntervals);
            row[c++] = JoinSensorIds(status.Part);
            row[c++] = WorstState(status.Part).ToString();
            row[c++] = Math.Round(schedule.MachineRunHours, 2);
        }
        return table;
    }

    public TelemetryAiTable BuildSeriesTable(long nowUnixMs)
    {
        ResolveSources();
        BeginHistoryFrame();

        TelemetryAiTable table = new("series");
        long bucketMs = TelemetryHistoryRanges.BucketMilliseconds(seriesRange);
        table.AddColumn("bucket_start_utc", $"Start of the bucket (ISO 8601, UTC); each covers {bucketMs / 60000} minutes.");
        table.AddColumn("bucket_start_unix_ms", "Start of the bucket, Unix milliseconds.");
        table.AddColumn("sensor_id", "Stable sensor identifier.");
        table.AddColumn("mean", "Mean reading in the bucket.");
        table.AddColumn("min", "Lowest reading in the bucket.");
        table.AddColumn("max", "Highest reading in the bucket.");
        table.AddColumn("last", "Last reading in the bucket.");
        table.AddColumn("samples", "Readings recorded in the bucket.");
        table.AddColumn("normal_s", "Seconds in the normal state.");
        table.AddColumn("warning_s", "Seconds in warning.");
        table.AddColumn("critical_s", "Seconds in critical.");
        table.AddColumn("unavailable_s", "Seconds without usable data.");
        table.AddColumn("generated", "True for generated backfill (only present when it is included).");

        if (registry == null)
        {
            EndHistoryFrame();
            return table;
        }

        foreach (PerformanceStatSource source in registry.Sources)
        {
            if (source == null || !TryGetBuckets(source, seriesRange, nowUnixMs, out List<TelemetryHistoryBucket> buckets))
                continue;

            foreach (TelemetryHistoryBucket bucket in buckets)
            {
                if (!Usable(bucket))
                    continue;
                object[] row = table.AddRow();
                int c = 0;
                row[c++] = IsoTime(bucket.StartUnixMs);
                row[c++] = bucket.StartUnixMs;
                row[c++] = source.StatId;
                row[c++] = bucket.HasValue ? bucket.Mean : null;
                row[c++] = bucket.HasValue ? bucket.Min : null;
                row[c++] = bucket.HasValue ? bucket.Max : null;
                row[c++] = bucket.HasValue ? bucket.Last : null;
                row[c++] = bucket.SampleCount;
                row[c++] = (double)bucket.NormalSeconds;
                row[c++] = (double)bucket.WarningSeconds;
                row[c++] = (double)bucket.CriticalSeconds;
                row[c++] = (double)bucket.UnavailableSeconds;
                row[c++] = bucket.Generated;
            }
        }

        EndHistoryFrame();
        return table;
    }

    // -----------------------------------------------------------------------
    // Figures
    // -----------------------------------------------------------------------

    public struct WindowStats
    {
        public long Samples;
        public int Buckets;
        public double Mean;
        public double Min;
        public double Max;
        public double StandardDeviation;
        public double SlopePerHour;
        public bool HasSlope;
        public float WarningSeconds;
        public float CriticalSeconds;
        public double Coverage;
    }

    /// <summary>The figures of one sensor over the last <paramref name="minutes"/>.</summary>
    public WindowStats ComputeWindow(PerformanceStatSource source, int minutes, long nowUnixMs)
    {
        WindowStats stats = default;
        TelemetryHistoryRange range = RangeFor(minutes);
        if (source == null || !TryGetBuckets(source, range, nowUnixMs, out List<TelemetryHistoryBucket> buckets) || buckets.Count == 0)
            return stats;

        long bucketMs = TelemetryHistoryRanges.BucketMilliseconds(range);
        int wanted = Mathf.Clamp(Mathf.CeilToInt(minutes * 60000f / bucketMs), 1, buckets.Count);
        int first = buckets.Count - wanted;

        double sum = 0d;
        double weightedX = 0d, weightedY = 0d, weightTotal = 0d;
        stats.Min = double.MaxValue;
        stats.Max = double.MinValue;

        for (int i = first; i < buckets.Count; i++)
        {
            TelemetryHistoryBucket bucket = buckets[i];
            if (bucket.Generated && !includeGeneratedHistory)
                continue;

            stats.WarningSeconds += bucket.WarningSeconds;
            stats.CriticalSeconds += bucket.CriticalSeconds;
            if (!bucket.HasValue)
                continue;

            stats.Buckets++;
            stats.Samples += bucket.SampleCount;
            sum += bucket.Sum;
            stats.Min = Math.Min(stats.Min, bucket.Min);
            stats.Max = Math.Max(stats.Max, bucket.Max);
        }

        stats.Coverage = Math.Round((double)stats.Buckets / wanted, 4);
        if (stats.Samples == 0)
        {
            stats.Min = stats.Max = 0d;
            return stats;
        }
        stats.Mean = sum / stats.Samples;

        // Spread and trend of the bucket means, each weighted by its samples.
        double variance = 0d;
        double covariance = 0d;
        double xVariance = 0d;
        for (int i = first; i < buckets.Count; i++)
        {
            TelemetryHistoryBucket bucket = buckets[i];
            if (!bucket.HasValue || (bucket.Generated && !includeGeneratedHistory))
                continue;
            double hours = (bucket.StartUnixMs + bucketMs * 0.5d - nowUnixMs) / 3_600_000d;
            weightedX += hours * bucket.SampleCount;
            weightedY += bucket.Mean * (double)bucket.SampleCount;
            weightTotal += bucket.SampleCount;
        }
        double meanX = weightedX / weightTotal;
        double meanY = weightedY / weightTotal;
        for (int i = first; i < buckets.Count; i++)
        {
            TelemetryHistoryBucket bucket = buckets[i];
            if (!bucket.HasValue || (bucket.Generated && !includeGeneratedHistory))
                continue;
            double hours = (bucket.StartUnixMs + bucketMs * 0.5d - nowUnixMs) / 3_600_000d;
            double dx = hours - meanX;
            double dy = bucket.Mean - meanY;
            variance += bucket.SampleCount * dy * dy;
            covariance += bucket.SampleCount * dx * dy;
            xVariance += bucket.SampleCount * dx * dx;
        }

        stats.StandardDeviation = Math.Sqrt(Math.Max(0d, variance / weightTotal));
        // Two buckets at least, and not all at one instant.
        if (stats.Buckets >= 2 && xVariance > 1e-12)
        {
            stats.SlopePerHour = covariance / xVariance;
            stats.HasSlope = !double.IsNaN(stats.SlopePerHour) && !double.IsInfinity(stats.SlopePerHour);
        }
        return stats;
    }

    /// <summary>
    /// Minutes until a trend of <paramref name="slopePerHour"/> takes
    /// <paramref name="value"/> to a warning limit. 0 when already past one;
    /// null when moving away or further than the horizon.
    /// </summary>
    public object MinutesToWarning(TelemetryLimits limits, float value, double slopePerHour)
    {
        if (limits.Mode == TelemetryLimitMode.Disabled)
            return null;
        if ((limits.HasHigh && value >= limits.WarningAbove) || (limits.HasLow && value <= limits.WarningBelow))
            return 0d;

        double best = double.PositiveInfinity;
        if (limits.HasHigh && slopePerHour > 0d)
            best = Math.Min(best, (limits.WarningAbove - value) / slopePerHour);
        if (limits.HasLow && slopePerHour < 0d)
            best = Math.Min(best, (value - limits.WarningBelow) / -slopePerHour);

        if (double.IsInfinity(best) || double.IsNaN(best) || best > projectionHorizonHours)
            return null;
        return Math.Round(best * 60d, 1);
    }

    public static object MarginToWarningPercent(TelemetryLimits limits, float value)
    {
        if (limits.Mode == TelemetryLimitMode.Disabled)
            return null;

        double band;
        if (limits.HasHigh && limits.HasLow)
            band = limits.WarningAbove - limits.WarningBelow;
        else if (limits.HasHigh)
            band = Math.Abs(limits.WarningAbove);
        else
            band = Math.Abs(limits.WarningBelow);
        if (band <= 1e-9)
            band = 1d;

        double margin = double.PositiveInfinity;
        if (limits.HasHigh)
            margin = Math.Min(margin, limits.WarningAbove - value);
        if (limits.HasLow)
            margin = Math.Min(margin, value - limits.WarningBelow);
        return Math.Round(margin / band * 100d, 2);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private int ProjectionWindow => Mathf.Clamp(projectionWindowMinutes, 1, MaximumWindowMinutes);

    private bool Has(TelemetryAiWindowFeature feature) => (windowFeatures & feature) != 0;

    private bool Usable(TelemetryHistoryBucket bucket) =>
        bucket.HasData && (includeGeneratedHistory || !bucket.Generated);

    /// <summary>Keeps valid, distinct windows in order; false (and no change) when none are valid.</summary>
    private bool SetWindows(IEnumerable<int> minutes)
    {
        List<int> valid = new();
        if (minutes != null)
        {
            foreach (int value in minutes)
            {
                if (value >= 1 && value <= MaximumWindowMinutes && !valid.Contains(value) && valid.Count < MaximumWindows)
                    valid.Add(value);
            }
        }

        if (valid.Count == 0)
        {
            if (activeWindows.Count == 0)
                activeWindows.Add(60);
            return false;
        }

        valid.Sort();
        activeWindows.Clear();
        activeWindows.AddRange(valid);
        return true;
    }

    /// <summary>The finest range whose span covers <paramref name="minutes"/>.</summary>
    public static TelemetryHistoryRange RangeFor(int minutes)
    {
        long wanted = minutes * 60_000L;
        foreach (TelemetryHistoryRange range in TelemetryHistoryRanges.All)
        {
            if (TelemetryHistoryRanges.SpanMilliseconds(range) >= wanted)
                return range;
        }
        return TelemetryHistoryRange.Year;
    }

    private bool TryGetBuckets(PerformanceStatSource source, TelemetryHistoryRange range, long now, out List<TelemetryHistoryBucket> buckets)
    {
        if (historyCache.TryGetValue((source, range), out buckets))
            return buckets.Count > 0;

        buckets = bucketListPool.Count > 0 ? bucketListPool.Pop() : new List<TelemetryHistoryBucket>(64);
        buckets.Clear();
        ITelemetryHistoryProvider history = injectedHistory ?? historyProvider as ITelemetryHistoryProvider;
        if (history != null && history.IsReady)
            history.TryGetHistory(source, range, now, buckets);
        historyCache[(source, range)] = buckets;
        return buckets.Count > 0;
    }

    // One query per sensor and range per build, however many windows share it.
    private void BeginHistoryFrame() => EndHistoryFrame();

    private void EndHistoryFrame()
    {
        foreach (List<TelemetryHistoryBucket> list in historyCache.Values)
            bucketListPool.Push(list);
        historyCache.Clear();
    }

    private void ResolveSources()
    {
        if (registry == null)
            registry = FindAnyObjectByType<TelemetryRegistry>();
        if (historyProvider == null)
            historyProvider = FindAnyObjectByType<TelemetryHistoryRecorder>();
        if (schedule == null)
            schedule = FindAnyObjectByType<TelemetryServiceSchedule>();
        if (!sourcesResolved || parts.Length == 0)
        {
            parts = FindObjectsByType<MachinePartGroup>(FindObjectsInactive.Include);
            sourcesResolved = true;
        }
    }

    private bool TryGetSoonestService(PerformanceStatSource source, out ServiceItemStatus soonest)
    {
        soonest = default;
        if (schedule == null)
            return false;

        schedule.GetStatuses(statuses);
        bool found = false;
        foreach (ServiceItemStatus status in statuses)
        {
            if (status.Part == null || !Lists(status.Part, source))
                continue;
            if (!found || status.HoursLeft < soonest.HoursLeft)
            {
                soonest = status;
                found = true;
            }
        }
        return found;
    }

    private static bool Lists(MachinePartGroup part, PerformanceStatSource source)
    {
        IReadOnlyList<PerformanceStatSource> sensors = part.BoundSensors;
        for (int i = 0; i < sensors.Count; i++)
        {
            if (sensors[i] == source)
                return true;
        }
        return false;
    }

    private static string JoinSensorIds(MachinePartGroup part)
    {
        if (part == null)
            return string.Empty;
        List<string> ids = new();
        foreach (PerformanceStatSource sensor in part.BoundSensors)
        {
            if (sensor != null)
                ids.Add(sensor.StatId);
        }
        return string.Join("|", ids);
    }

    private static string JoinIntervals(float[] intervals)
    {
        if (intervals == null || intervals.Length == 0)
            return string.Empty;
        string[] parts = new string[intervals.Length];
        for (int i = 0; i < intervals.Length; i++)
            parts[i] = intervals[i].ToString("0.##", CultureInfo.InvariantCulture);
        return string.Join("|", parts);
    }

    private static StatVisualState WorstState(MachinePartGroup part)
    {
        StatVisualState worst = StatVisualState.Normal;
        if (part == null)
            return worst;
        foreach (PerformanceStatSource sensor in part.BoundSensors)
        {
            if (sensor == null)
                continue;
            StatVisualState state = sensor.VisualState;
            if (Rank(state) > Rank(worst))
                worst = state;
        }
        return worst;
    }

    private static int Rank(StatVisualState state) => state switch
    {
        StatVisualState.Critical => 3,
        StatVisualState.Warning => 2,
        StatVisualState.Unavailable => 1,
        _ => 0
    };

    private static string WindowText(int minutes)
    {
        if (minutes % 1440 == 0)
            return (minutes / 1440).ToString(CultureInfo.InvariantCulture) + " d";
        if (minutes % 60 == 0)
            return (minutes / 60).ToString(CultureInfo.InvariantCulture) + " h";
        return minutes.ToString(CultureInfo.InvariantCulture) + " min";
    }

    private static string IsoTime(long unixMs) =>
        DateTimeOffset.FromUnixTimeMilliseconds(unixMs).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static long UtcNow() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // -----------------------------------------------------------------------
    // Page
    // -----------------------------------------------------------------------

    private void Publish(bool includeSeries)
    {
        string json = BuildJson(UtcNow(), includeSeries);
        if (!includeSeries)
            LatestJson = json;
        if (publishToPage)
            SendToPage(json);
        Published?.Invoke(this);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void DigitalTwin_PublishAiTable(string eventName, string json);

    [DllImport("__Internal")]
    private static extern void DigitalTwin_SaveText(string fileName, string mimeType, string text);

    private static void SendToPage(string json) => DigitalTwin_PublishAiTable(EventName, json);

    private static void SaveText(string fileName, string mimeType, string text) =>
        DigitalTwin_SaveText(fileName, mimeType, text);
#else
    private static void SendToPage(string json)
    {
    }

    // Outside the browser the file lands in the project's Logs folder (or the player's data folder).
    private void SaveText(string fileName, string mimeType, string text)
    {
        string directory = Application.isEditor
            ? System.IO.Path.Combine(Application.dataPath, "..", "Logs", "AiTable")
            : System.IO.Path.Combine(Application.persistentDataPath, "AiTable");
        System.IO.Directory.CreateDirectory(directory);
        string path = System.IO.Path.Combine(directory, fileName);
        System.IO.File.WriteAllText(path, text);
        Debug.Log($"AI data table saved to {System.IO.Path.GetFullPath(path)}", this);
    }
#endif
}
