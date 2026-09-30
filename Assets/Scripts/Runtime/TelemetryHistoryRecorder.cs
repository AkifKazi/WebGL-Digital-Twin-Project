using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps a rolling history of every registered telemetry source for the detail
/// panel: one ring of buckets per time range. Each source is sampled at a fixed
/// interval, so a flat signal still fills its buckets, and the time spent in
/// each state accrues continuously while the page runs. The history lives in
/// memory and restarts with the page.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryHistoryRecorder : MonoBehaviour, ITelemetryHistoryProvider
{
    [Header("Sources")]
    [SerializeField] private TelemetryRegistry registry;

    [Header("Sampling")]
    [Tooltip("Seconds between samples of each source's current value.")]
    [SerializeField, Min(0.1f)] private float sampleInterval = 1f;

    [Tooltip("A pause longer than this (a hidden browser tab, for example) is left " +
             "empty instead of being counted as time in the last known state.")]
    [SerializeField, Min(1f)] private float maximumGapSeconds = 10f;

    [Header("Prototype")]
    [Tooltip("Prototype only. Fills the time before this session began, and any gap in it " +
             "(a paused editor or a hidden browser tab), with generated history, so the graphs " +
             "never show empty stretches. Turn this off once a real history source is " +
             "connected: real gaps should show as gaps.")]
    [SerializeField] private bool backfillWithGeneratedHistory = true;

    [SerializeField] private int generatedHistorySeed = 1731;

    [Tooltip("Share of generated buckets that reach the warning limit.")]
    [SerializeField, Range(0f, 0.3f)] private float generatedWarningShare = 0.06f;

    [Tooltip("Share of generated buckets that reach the critical limit.")]
    [SerializeField, Range(0f, 0.2f)] private float generatedCriticalShare = 0.02f;

    // Buckets per drift lattice cell. 14 reproduces the feature size of the
    // Perlin term this replaced (its 0.071 step was one cell per ~14 buckets),
    // so generated history keeps the same shape.
    private const int DriftCellBuckets = 14;

    private readonly Dictionary<PerformanceStatSource, SourceHistory> histories = new();
    private float nextSampleTime;

    public bool IsReady => isActiveAndEnabled && registry != null;

    private void Update()
    {
        if (Time.unscaledTime < nextSampleTime)
            return;

        nextSampleTime = Time.unscaledTime + sampleInterval;
        Sample(UtcNow());
    }

    private void Sample(long now)
    {
        if (registry == null)
            return;

        foreach (PerformanceStatSource source in registry.Sources)
        {
            if (source == null)
                continue;

            if (!histories.TryGetValue(source, out SourceHistory history))
            {
                history = new SourceHistory(now, source.CurrentValue);
                histories.Add(source, history);
            }

            history.Record(source, now, (long)(maximumGapSeconds * 1000f));
        }
    }

    public bool TryGetHistory(
        PerformanceStatSource source,
        TelemetryHistoryRange range,
        long endUnixMs,
        List<TelemetryHistoryBucket> buckets)
    {
        buckets.Clear();
        if (source == null)
            return false;

        histories.TryGetValue(source, out SourceHistory history);
        int count = TelemetryHistoryRanges.BucketCount(range);
        long bucketMs = TelemetryHistoryRanges.BucketMilliseconds(range);
        long lastStart = TelemetryHistoryRanges.AlignDown(endUnixMs, bucketMs);
        bool any = false;

        for (int i = count - 1; i >= 0; i--)
        {
            long start = lastStart - i * bucketMs;
            TelemetryHistoryBucket bucket =
                history != null && history.TryGet(range, start, out TelemetryHistoryBucket recorded)
                    ? recorded
                    : new TelemetryHistoryBucket { StartUnixMs = start };

            // Generated history stands in for time before recording began and
            // for empty buckets after it (a paused editor, a hidden tab); it never
            // overwrites a bucket with recorded data.
            if (!bucket.HasData && backfillWithGeneratedHistory)
                bucket = Generate(source, history, start, bucketMs);

            any |= bucket.HasData;
            buckets.Add(bucket);
        }

        return any;
    }

    private static long UtcNow() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // -----------------------------------------------------------------------
    // Generated history (prototype backfill)
    // -----------------------------------------------------------------------

    private TelemetryHistoryBucket Generate(
        PerformanceStatSource source,
        SourceHistory history,
        long start,
        long bucketMs)
    {
        TelemetryLimits limits = source.Limits;
        float anchor = NormalAnchor(limits, history?.FirstValue ?? source.CurrentValue);
        float span = TypicalSpan(limits, anchor);
        uint key = HashString(source.StatId) ^ (uint)generatedHistorySeed;
        long index = start / bucketMs;

        float drift = (SmoothNoise(key, index, 7u) - 0.5f) * span * 0.5f;
        float jitter = (Hash01(key, index, 1u) - 0.5f) * span * 0.15f;
        float value = anchor + drift + jitter;
        float seconds = bucketMs / 1000f;
        float warningSeconds = 0f;
        float criticalSeconds = 0f;
        float spread = Mathf.Abs(span) * 0.08f;
        bool excursion = false;

        if (limits.Mode != TelemetryLimitMode.Disabled)
        {
            // Excursions come in short runs of three buckets, as real ones do.
            float roll = Hash01(key, index / 3, 2u);
            float share = 0.25f + 0.5f * Hash01(key, index, 3u);
            bool high = limits.HasHigh && (!limits.HasLow || Hash01(key, index / 3, 4u) < 0.5f);

            if (roll < generatedCriticalShare)
            {
                value = high
                    ? limits.CriticalAbove + Mathf.Abs(span) * 0.05f * (1f + Hash01(key, index, 5u))
                    : limits.CriticalBelow - Mathf.Abs(span) * 0.05f * (1f + Hash01(key, index, 5u));
                criticalSeconds = seconds * share;
                excursion = true;
            }
            else if (roll < generatedCriticalShare + generatedWarningShare)
            {
                value = high
                    ? Mathf.Lerp(limits.WarningAbove, limits.CriticalAbove, 0.35f + 0.3f * Hash01(key, index, 6u))
                    : Mathf.Lerp(limits.WarningBelow, limits.CriticalBelow, 0.35f + 0.3f * Hash01(key, index, 6u));
                warningSeconds = seconds * share;
                excursion = true;
            }
        }

        // A bucket generated as normal must read as normal: a value past a
        // limit with no alarm time would contradict the status timeline.
        if (!excursion)
            KeepInsideNormalBand(limits, ref value, ref spread);

        return new TelemetryHistoryBucket
        {
            StartUnixMs = start,
            SampleCount = 1,
            Sum = value,
            Min = value - spread,
            Max = value + spread,
            Last = value,
            NormalSeconds = seconds - warningSeconds - criticalSeconds,
            WarningSeconds = warningSeconds,
            CriticalSeconds = criticalSeconds,
            Generated = true
        };
    }

    /// <summary>
    /// Folds <paramref name="value"/> back inside the normal band, a little
    /// clear of its limits, and narrows the spread to fit. Reflecting rather
    /// than clamping keeps the drift's movement: a source running close to a
    /// limit would otherwise draw a flat line along it.
    /// </summary>
    private static void KeepInsideNormalBand(TelemetryLimits limits, ref float value, ref float spread)
    {
        if (limits.Mode == TelemetryLimitMode.Disabled)
            return;

        float low = limits.HasLow ? limits.WarningBelow : float.NegativeInfinity;
        float high = limits.HasHigh ? limits.WarningAbove : float.PositiveInfinity;
        if (!(high > low))
            return;

        float width = float.IsInfinity(high - low) ? Mathf.Max(Mathf.Abs(value), 1f) : high - low;
        float margin = width * 0.04f;
        float top = high - margin;
        float bottom = low + margin;
        if (!(top > bottom))
        {
            top = bottom = (low + high) * 0.5f;
        }

        if (value > top)
            value = top - (value - top);
        if (value < bottom)
            value = bottom + (bottom - value);
        value = Mathf.Clamp(value, bottom, top);

        spread = Mathf.Max(0f, Mathf.Min(spread, top - value, value - bottom));
    }

    /// <summary>A value inside the normal band, near where the source started.</summary>
    private static float NormalAnchor(TelemetryLimits limits, float value)
    {
        if (limits.Mode == TelemetryLimitMode.Disabled || limits.Evaluate(value) == StatVisualState.Normal)
            return value;
        if (limits.HasHigh && limits.HasLow)
            return (limits.WarningAbove + limits.WarningBelow) * 0.5f;
        if (limits.HasHigh)
            return limits.WarningAbove - Mathf.Abs(limits.WarningAbove) * 0.2f;
        return limits.WarningBelow + Mathf.Abs(limits.WarningBelow) * 0.2f;
    }

    private static float TypicalSpan(TelemetryLimits limits, float anchor)
    {
        if (limits.HasHigh && limits.HasLow)
            return limits.WarningAbove - limits.WarningBelow;
        if (limits.HasHigh)
            return Mathf.Max(Mathf.Abs(limits.WarningAbove - anchor) * 2f, Mathf.Abs(anchor) * 0.2f, 0.1f);
        if (limits.HasLow)
            return Mathf.Max(Mathf.Abs(anchor - limits.WarningBelow) * 2f, Mathf.Abs(anchor) * 0.2f, 0.1f);
        return Mathf.Abs(anchor) * 0.2f + 0.1f;
    }

    private static uint HashString(string text)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in text ?? string.Empty)
                hash = (hash ^ c) * 16777619u;
            return hash;
        }
    }

    /// <summary>
    /// Smooth value noise over the bucket index, one lattice cell per
    /// <see cref="DriftCellBuckets"/> buckets.
    ///
    /// Perlin noise cannot be used here. A bucket index is absolute, so an hour
    /// bucket sits near 3e7 today, and at that magnitude a float cannot hold the
    /// 0.071 step between neighbouring buckets: every bucket on screen sampled
    /// the same coordinate, the drift term came out constant, and the line chart
    /// drew a flat line. Hashing the integer index keeps full precision at any
    /// date, for every range.
    /// </summary>
    private static float SmoothNoise(uint key, long index, uint salt)
    {
        long cell = index >= 0
            ? index / DriftCellBuckets
            : (index - DriftCellBuckets + 1) / DriftCellBuckets;
        float t = (index - cell * DriftCellBuckets) / (float)DriftCellBuckets;
        return Mathf.Lerp(
            Hash01(key, cell, salt),
            Hash01(key, cell + 1, salt),
            t * t * (3f - 2f * t));
    }

    private static float Hash01(uint key, long index, uint salt)
    {
        unchecked
        {
            uint h = key ^ ((uint)index * 0x9E3779B1u) ^ ((uint)(index >> 32) * 0x85EBCA77u) ^ (salt * 0xC2B2AE3Du);
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }

    // -----------------------------------------------------------------------
    // Storage
    // -----------------------------------------------------------------------

    private sealed class SourceHistory
    {
        public readonly long FirstUnixMs;
        public readonly float FirstValue;

        private readonly RangeRing[] rings;
        private long previousUnixMs;
        private StatVisualState previousState;
        private bool hasPrevious;

        public SourceHistory(long now, float firstValue)
        {
            FirstUnixMs = now;
            FirstValue = firstValue;
            rings = new RangeRing[TelemetryHistoryRanges.All.Length];
            for (int i = 0; i < rings.Length; i++)
                rings[i] = new RangeRing(TelemetryHistoryRanges.All[i]);
        }

        public void Record(PerformanceStatSource source, long now, long maximumGapMs)
        {
            if (hasPrevious && now > previousUnixMs && now - previousUnixMs <= maximumGapMs)
            {
                foreach (RangeRing ring in rings)
                    ring.AddStateTime(previousState, previousUnixMs, now);
            }

            bool usable = source.DataQuality == TelemetryDataQuality.Good ||
                          source.DataQuality == TelemetryDataQuality.Uncertain;
            if (usable)
            {
                foreach (RangeRing ring in rings)
                    ring.AddSample(now, source.CurrentValue);
            }

            previousUnixMs = now;
            previousState = source.VisualState;
            hasPrevious = true;
        }

        public bool TryGet(TelemetryHistoryRange range, long start, out TelemetryHistoryBucket bucket) =>
            rings[(int)range].TryGet(start, out bucket);
    }

    private sealed class RangeRing
    {
        private readonly TelemetryHistoryBucket[] buckets;
        private readonly long bucketMs;
        private long headStart = long.MinValue;

        public RangeRing(TelemetryHistoryRange range)
        {
            buckets = new TelemetryHistoryBucket[TelemetryHistoryRanges.BucketCount(range)];
            bucketMs = TelemetryHistoryRanges.BucketMilliseconds(range);
        }

        public void AddSample(long unixMs, float value)
        {
            long start = TelemetryHistoryRanges.AlignDown(unixMs, bucketMs);
            if (!Advance(start))
                return;

            ref TelemetryHistoryBucket bucket = ref buckets[IndexOf(start)];
            if (bucket.SampleCount == 0)
            {
                bucket.Min = value;
                bucket.Max = value;
            }
            else
            {
                bucket.Min = Math.Min(bucket.Min, value);
                bucket.Max = Math.Max(bucket.Max, value);
            }

            bucket.Sum += value;
            bucket.SampleCount++;
            bucket.Last = value;
        }

        /// <summary>Adds [from, to) spent in one state, split across bucket edges.</summary>
        public void AddStateTime(StatVisualState state, long fromMs, long toMs)
        {
            while (fromMs < toMs)
            {
                long start = TelemetryHistoryRanges.AlignDown(fromMs, bucketMs);
                long end = Math.Min(toMs, start + bucketMs);
                if (Advance(start))
                {
                    ref TelemetryHistoryBucket bucket = ref buckets[IndexOf(start)];
                    float seconds = (end - fromMs) / 1000f;
                    switch (state)
                    {
                        case StatVisualState.Critical: bucket.CriticalSeconds += seconds; break;
                        case StatVisualState.Warning: bucket.WarningSeconds += seconds; break;
                        case StatVisualState.Unavailable: bucket.UnavailableSeconds += seconds; break;
                        default: bucket.NormalSeconds += seconds; break;
                    }
                }

                fromMs = end;
            }
        }

        public bool TryGet(long start, out TelemetryHistoryBucket bucket)
        {
            bucket = default;
            if (headStart == long.MinValue || start > headStart ||
                start <= headStart - buckets.Length * bucketMs)
                return false;

            bucket = buckets[IndexOf(start)];
            return bucket.StartUnixMs == start;
        }

        /// <summary>
        /// Moves the ring forward to <paramref name="start"/>, clearing buckets it
        /// passes. False when <paramref name="start"/> is older than the ring holds.
        /// </summary>
        private bool Advance(long start)
        {
            if (headStart == long.MinValue)
            {
                headStart = start;
                buckets[IndexOf(start)] = new TelemetryHistoryBucket { StartUnixMs = start };
                return true;
            }

            if (start > headStart)
            {
                long steps = Math.Min((start - headStart) / bucketMs, buckets.Length);
                for (long k = steps - 1; k >= 0; k--)
                {
                    long cleared = start - k * bucketMs;
                    buckets[IndexOf(cleared)] = new TelemetryHistoryBucket { StartUnixMs = cleared };
                }

                headStart = start;
                return true;
            }

            return start > headStart - buckets.Length * bucketMs;
        }

        private int IndexOf(long start)
        {
            long slot = (start / bucketMs) % buckets.Length;
            return (int)(slot < 0 ? slot + buckets.Length : slot);
        }
    }
}
