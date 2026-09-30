using System;
using System.Globalization;
using UnityEngine;

public enum PreferredStatRail
{
    Auto,
    Left,
    Right
}

public enum PreferredPortraitStatRail
{
    Auto,
    Top,
    Bottom
}

public enum TelemetryDataQuality
{
    Good,
    Uncertain,
    Bad,
    Stale
}

public enum TelemetryLimitMode
{
    HighOnly,
    LowOnly,
    OutsideRange,
    Disabled
}

[DisallowMultipleComponent]
public sealed class PerformanceStatSource : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string statId = "STAT-01";
    [SerializeField] private string metricName = "MOTOR / TEMP";
    [SerializeField] private string unit = "°C";
    [SerializeField] private string valueFormat = "0.0";

    [Header("Current Data")]
    [SerializeField] private float currentValue = 65f;
    [SerializeField] private StatVisualState visualState = StatVisualState.Normal;

    [Header("Automatic State Thresholds")]
    [Tooltip("When enabled, SetValue derives the visual state from these limits.")]
    [SerializeField] private bool useAutomaticThresholds;

    [SerializeField] private TelemetryLimitMode limitMode = TelemetryLimitMode.HighOnly;

    [SerializeField] private float warningBelow;
    [SerializeField] private float criticalBelow;
    [SerializeField] private float warningAbove = 80f;
    [SerializeField] private float criticalAbove = 95f;

    [Tooltip("How far past a limit, as a share of the gap between the warning and critical " +
             "limits, a reading must fall back before its alarm steps down. Stops a reading " +
             "hovering on a limit from flickering between states. 0 turns it off.")]
    [SerializeField, Range(0f, 0.5f)] private float alarmHysteresis = 0.1f;

    [Header("Data Health")]
    [SerializeField] private TelemetryDataQuality dataQuality = TelemetryDataQuality.Good;
    [SerializeField, Min(0f)] private float staleAfterSeconds = 5f;

    [Header("Layout")]
    [SerializeField] private PreferredStatRail preferredRail = PreferredStatRail.Auto;
    [SerializeField] private PreferredPortraitStatRail preferredPortraitRail = PreferredPortraitStatRail.Auto;

    [Tooltip("Higher values are retained first when a rail is full.")]
    [SerializeField] private int displayPriority;

    [SerializeField] private bool visible = true;

    private StatVisualState lastHealthyVisualState = StatVisualState.Normal;
    private string localizedName;
    private string localizedNameSource;
    private int localizedNameVersion = -1;

    public event Action<PerformanceStatSource> Changed;
    public event Action<PerformanceStatSource> LayoutPriorityChanged;

    public string StatId => statId;
    /// <summary>
    /// The sensor's label: the language table's copy for sensor.&lt;id&gt; when it
    /// has one, otherwise the authored label. Cached per table version, since
    /// the rails read it every frame.
    /// </summary>
    public string MetricName
    {
        get
        {
            if (localizedNameVersion != UIText.Version || localizedNameSource != metricName)
            {
                localizedNameSource = metricName;
                localizedNameVersion = UIText.Version;
                localizedName = UIText.Get(LabelKey, metricName);
            }
            return localizedName;
        }
    }

    /// <summary>The authored label, whatever the language.</summary>
    public string AuthoredMetricName => metricName;

    /// <summary>Language table key of the label: sensor. and the sensor ID.</summary>
    public string LabelKey => "sensor." + statId;
    public string Unit => unit;
    public string ValueFormat => valueFormat;
    public float CurrentValue => currentValue;
    public StatVisualState VisualState => visualState;
    public PreferredStatRail PreferredRail => preferredRail;
    public PreferredPortraitStatRail PreferredPortraitRail => preferredPortraitRail;
    public int DisplayPriority => displayPriority;
    public bool IsVisible => visible;
    public TelemetryDataQuality DataQuality => dataQuality;
    public long SourceTimestampUnixMs { get; private set; }
    public long ReceiveTimestampUnixMs { get; private set; }
    public uint StatusCode { get; private set; }
    public ulong SequenceNumber { get; private set; }
    public string ProviderId { get; private set; } = string.Empty;

    /// <summary>The limits the state is derived from; Disabled when this source's state is set externally.</summary>
    public TelemetryLimits Limits => new(
        useAutomaticThresholds ? limitMode : TelemetryLimitMode.Disabled,
        warningBelow,
        criticalBelow,
        warningAbove,
        criticalAbove);

    // The GameObject carrying this component is the sensor location.
    public Transform WorldAnchor => transform;

    public string FormattedValue => currentValue.ToString(
        string.IsNullOrWhiteSpace(valueFormat) ? "0.0" : valueFormat,
        CultureInfo.InvariantCulture
    );

    public string FormatValue(int decimalPlaces)
    {
        decimalPlaces = Mathf.Clamp(decimalPlaces, 0, 6);
        string format = decimalPlaces == 0 ? "0" : "0." + new string('0', decimalPlaces);
        return currentValue.ToString(format, CultureInfo.InvariantCulture);
    }

    public int PreferredDecimalPlaces
    {
        get
        {
            int separator = valueFormat?.IndexOf('.') ?? -1;
            return separator < 0 ? 0 : Mathf.Clamp(valueFormat.Length - separator - 1, 0, 6);
        }
    }

    private void Awake()
    {
        if (visualState != StatVisualState.Unavailable)
            lastHealthyVisualState = visualState;
    }

    public void SetValue(float value)
    {
        SetValueInternal(value, true);
    }

    private void SetValueInternal(float value, bool updateTimestamp)
    {
        bool valueChanged = !Mathf.Approximately(currentValue, value);
        StatVisualState nextState = useAutomaticThresholds
            ? EvaluateState(value)
            : visualState;
        bool stateChanged = nextState != visualState;

        if (!valueChanged && !stateChanged)
            return;

        currentValue = value;
        visualState = nextState;
        if (nextState != StatVisualState.Unavailable)
            lastHealthyVisualState = nextState;
        if (updateTimestamp)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            SourceTimestampUnixMs = now;
            ReceiveTimestampUnixMs = now;
        }
        Changed?.Invoke(this);

        if (stateChanged)
            LayoutPriorityChanged?.Invoke(this);
    }

    public void SetState(StatVisualState state)
    {
        if (visualState == state)
            return;

        visualState = state;
        if (state != StatVisualState.Unavailable)
            lastHealthyVisualState = state;
        Changed?.Invoke(this);
        LayoutPriorityChanged?.Invoke(this);
    }

    public void SetVisible(bool shouldBeVisible)
    {
        if (visible == shouldBeVisible)
            return;

        visible = shouldBeVisible;
        Changed?.Invoke(this);
        LayoutPriorityChanged?.Invoke(this);
    }

    public void SetDataQuality(TelemetryDataQuality quality)
    {
        if (dataQuality == quality)
            return;

        TelemetryDataQuality previousQuality = dataQuality;
        dataQuality = quality;

        if (quality == TelemetryDataQuality.Bad ||
            quality == TelemetryDataQuality.Stale)
        {
            // Bad to Stale keeps the Unavailable look, but listeners still
            // need to hear that the quality changed.
            if (visualState == StatVisualState.Unavailable)
                Changed?.Invoke(this);
            else
                SetState(StatVisualState.Unavailable);
            return;
        }

        if ((previousQuality == TelemetryDataQuality.Bad ||
             previousQuality == TelemetryDataQuality.Stale) &&
            visualState == StatVisualState.Unavailable)
        {
            StatVisualState recoveredState = useAutomaticThresholds
                ? EvaluateState(currentValue)
                : lastHealthyVisualState;
            SetState(recoveredState);
            return;
        }

        Changed?.Invoke(this);
    }

    public void SetReading(float value, TelemetryDataQuality quality)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        ApplyReading(new TelemetryReading(
            statId,
            value,
            quality,
            now,
            now));
    }

    public void ApplyReading(in TelemetryReading reading)
    {
        TryApplyReading(reading);
    }

    /// <summary>
    /// A value this source can hold: finite, and within float range so storing
    /// it cannot turn a very large finite reading into Infinity.
    /// </summary>
    public static bool IsUsableValue(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= float.MaxValue;

    public bool TryApplyReading(in TelemetryReading reading)
    {
        if (!IsUsableValue(reading.Value))
            return false;

        bool sameProvider = string.Equals(
            ProviderId,
            reading.ProviderId ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        // A zero sequence means the provider does not supply sequence numbers.
        // Otherwise, reject duplicates and delayed packets from the same stream.
        if (sameProvider && SequenceNumber > 0 && reading.SequenceNumber > 0 &&
            reading.SequenceNumber <= SequenceNumber)
        {
            return false;
        }

        SourceTimestampUnixMs = reading.SourceTimestampUnixMs;
        ReceiveTimestampUnixMs = reading.ReceiveTimestampUnixMs;
        StatusCode = reading.StatusCode;
        SequenceNumber = reading.SequenceNumber;
        ProviderId = reading.ProviderId ?? string.Empty;

        SetDataQuality(reading.Quality);

        // Bad/stale values are metadata only and must not replace the last usable value.
        if (reading.Quality == TelemetryDataQuality.Good ||
            reading.Quality == TelemetryDataQuality.Uncertain)
        {
            SetValueInternal((float)reading.Value, false);
        }

        return true;
    }

    public bool ShouldMarkStale(long nowUnixMs)
    {
        return staleAfterSeconds > 0f &&
               SourceTimestampUnixMs > 0 &&
               nowUnixMs - SourceTimestampUnixMs > staleAfterSeconds * 1000f;
    }

    public void Refresh()
    {
        Changed?.Invoke(this);
    }

    public int GetDisplayRank()
    {
        int stateRank = visualState switch
        {
            StatVisualState.Critical => 3,
            StatVisualState.Warning => 2,
            StatVisualState.Unavailable => 1,
            _ => 0
        };

        return stateRank * 100000 + displayPriority;
    }

    private StatVisualState EvaluateState(float value)
    {
        if (dataQuality == TelemetryDataQuality.Bad ||
            dataQuality == TelemetryDataQuality.Stale)
        {
            return StatVisualState.Unavailable;
        }

        return new TelemetryLimits(limitMode, warningBelow, criticalBelow, warningAbove, criticalAbove)
            .Evaluate(value, visualState, alarmHysteresis);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(valueFormat))
            valueFormat = "0.0";

        if (criticalAbove < warningAbove)
            criticalAbove = warningAbove;

        if (warningBelow < criticalBelow)
            warningBelow = criticalBelow;
    }
#endif
}
