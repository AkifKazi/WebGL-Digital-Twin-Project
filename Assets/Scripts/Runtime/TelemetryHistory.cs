using System;
using System.Collections.Generic;

/// <summary>Time spans the telemetry detail panel can show.</summary>
public enum TelemetryHistoryRange
{
    Hour,
    Day,
    Week,
    Month,
    Year
}

/// <summary>
/// The bucket layout of every range. Graphs and history providers share it, so
/// one bar of a status timeline and one point of a line chart always cover the
/// same stretch of time.
/// </summary>
public static class TelemetryHistoryRanges
{
    public static readonly TelemetryHistoryRange[] All =
    {
        TelemetryHistoryRange.Hour,
        TelemetryHistoryRange.Day,
        TelemetryHistoryRange.Week,
        TelemetryHistoryRange.Month,
        TelemetryHistoryRange.Year
    };

    public static int BucketCount(TelemetryHistoryRange range) => range switch
    {
        TelemetryHistoryRange.Hour => 60,   // 1 minute each
        TelemetryHistoryRange.Day => 48,    // 30 minutes
        TelemetryHistoryRange.Week => 56,   // 3 hours
        TelemetryHistoryRange.Month => 30,  // 1 day
        TelemetryHistoryRange.Year => 52,   // 1 week
        _ => 60
    };

    public static long BucketMilliseconds(TelemetryHistoryRange range) => range switch
    {
        TelemetryHistoryRange.Hour => 60_000L,
        TelemetryHistoryRange.Day => 1_800_000L,
        TelemetryHistoryRange.Week => 10_800_000L,
        TelemetryHistoryRange.Month => 86_400_000L,
        TelemetryHistoryRange.Year => 604_800_000L,
        _ => 60_000L
    };

    public static long SpanMilliseconds(TelemetryHistoryRange range) =>
        BucketCount(range) * BucketMilliseconds(range);

    public static string ShortLabel(TelemetryHistoryRange range) => range switch
    {
        TelemetryHistoryRange.Hour => "H",
        TelemetryHistoryRange.Day => "D",
        TelemetryHistoryRange.Week => "W",
        TelemetryHistoryRange.Month => "M",
        TelemetryHistoryRange.Year => "Y",
        _ => "?"
    };

    /// <summary>Start of the bucket containing <paramref name="unixMs"/>.</summary>
    public static long AlignDown(long unixMs, long bucketMs)
    {
        long remainder = unixMs % bucketMs;
        if (remainder < 0)
            remainder += bucketMs;
        return unixMs - remainder;
    }
}

/// <summary>What one source did during one bucket of time.</summary>
[Serializable]
public struct TelemetryHistoryBucket
{
    public long StartUnixMs;
    public int SampleCount;
    public float Min;
    public float Max;
    public float Sum;
    public float Last;
    public float NormalSeconds;
    public float WarningSeconds;
    public float CriticalSeconds;
    public float UnavailableSeconds;

    public bool HasValue => SampleCount > 0;
    public float Mean => SampleCount > 0 ? Sum / SampleCount : 0f;
    public float CoveredSeconds => NormalSeconds + WarningSeconds + CriticalSeconds + UnavailableSeconds;
    public bool HasData => HasValue || CoveredSeconds > 0f;

    /// <summary>The most serious state the source reached in this bucket.</summary>
    public StatVisualState WorstState
    {
        get
        {
            if (CriticalSeconds > 0f)
                return StatVisualState.Critical;
            if (WarningSeconds > 0f)
                return StatVisualState.Warning;
            if (UnavailableSeconds > 0f && NormalSeconds <= 0f)
                return StatVisualState.Unavailable;
            return StatVisualState.Normal;
        }
    }
}

/// <summary>A source's warning and critical limits, read-only.</summary>
public readonly struct TelemetryLimits
{
    public readonly TelemetryLimitMode Mode;
    public readonly float WarningBelow;
    public readonly float CriticalBelow;
    public readonly float WarningAbove;
    public readonly float CriticalAbove;

    public TelemetryLimits(
        TelemetryLimitMode mode,
        float warningBelow,
        float criticalBelow,
        float warningAbove,
        float criticalAbove)
    {
        Mode = mode;
        WarningBelow = warningBelow;
        CriticalBelow = criticalBelow;
        WarningAbove = warningAbove;
        CriticalAbove = criticalAbove;
    }

    public bool HasHigh => Mode == TelemetryLimitMode.HighOnly || Mode == TelemetryLimitMode.OutsideRange;
    public bool HasLow => Mode == TelemetryLimitMode.LowOnly || Mode == TelemetryLimitMode.OutsideRange;

    public StatVisualState Evaluate(float value)
    {
        if ((HasHigh && value >= CriticalAbove) || (HasLow && value <= CriticalBelow))
            return StatVisualState.Critical;
        if ((HasHigh && value >= WarningAbove) || (HasLow && value <= WarningBelow))
            return StatVisualState.Warning;
        return StatVisualState.Normal;
    }
}

/// <summary>
/// Supplies history to the detail panel. The in-page recorder implements it
/// today; a backend history service can implement it later without touching
/// the panel or its graphs.
/// </summary>
public interface ITelemetryHistoryProvider
{
    /// <summary>False while the provider cannot answer (not configured, not connected).</summary>
    bool IsReady { get; }

    /// <summary>
    /// Fills <paramref name="buckets"/> oldest first with exactly
    /// <see cref="TelemetryHistoryRanges.BucketCount"/> entries, ending with the
    /// bucket that contains <paramref name="endUnixMs"/>. Buckets without data
    /// are included empty. Returns false when nothing is known for the source.
    /// </summary>
    bool TryGetHistory(
        PerformanceStatSource source,
        TelemetryHistoryRange range,
        long endUnixMs,
        List<TelemetryHistoryBucket> buckets);
}

/// <summary>
/// Optional text for the panel's detail area (a recommendation, for example).
/// When no provider is assigned, or it has nothing to say, the area stays hidden.
/// </summary>
public interface ITelemetryInsightProvider
{
    bool TryGetInsight(
        PerformanceStatSource source,
        TelemetryHistoryRange range,
        IReadOnlyList<TelemetryHistoryBucket> buckets,
        out string insight);
}
