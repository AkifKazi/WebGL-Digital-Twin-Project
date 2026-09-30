using System;
using System.Collections.Generic;

/// <summary>
/// Test double for <see cref="ITelemetryHistoryProvider"/>: answers every query
/// from a function of the bucket start, so a test knows each figure exactly.
/// Editor only; used by <see cref="FeedbackStressTest"/>.
/// </summary>
public sealed class FeedbackTestHistoryProvider : ITelemetryHistoryProvider
{
    /// <summary>(source, range, bucket start, bucket length) to the bucket; null leaves the bucket empty.</summary>
    public Func<PerformanceStatSource, TelemetryHistoryRange, long, long, TelemetryHistoryBucket?> Bucket;

    public bool IsReady => true;

    public bool TryGetHistory(
        PerformanceStatSource source,
        TelemetryHistoryRange range,
        long endUnixMs,
        List<TelemetryHistoryBucket> buckets)
    {
        buckets.Clear();
        int count = TelemetryHistoryRanges.BucketCount(range);
        long bucketMs = TelemetryHistoryRanges.BucketMilliseconds(range);
        long last = TelemetryHistoryRanges.AlignDown(endUnixMs, bucketMs);
        bool any = false;
        for (int i = count - 1; i >= 0; i--)
        {
            long start = last - i * bucketMs;
            TelemetryHistoryBucket bucket = Bucket?.Invoke(source, range, start, bucketMs)
                                            ?? new TelemetryHistoryBucket { StartUnixMs = start };
            bucket.StartUnixMs = start;
            any |= bucket.HasData;
            buckets.Add(bucket);
        }
        return any;
    }
}
