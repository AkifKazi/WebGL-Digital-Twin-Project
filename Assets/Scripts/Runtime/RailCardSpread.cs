using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Places a rail's cards as close to their sensors as they can sit without
/// overlapping, keeping their order and staying inside the rail.
///
/// Cards that crowd together spread out evenly around their sensors, so a
/// group stays centred on what it points at and moves as those sensors move.
/// Pushing each card on from the one before, as the rails used to, slid every
/// crowded group to the far end of its rail, where it stayed put while the
/// camera turned.
///
/// This is the least-squares placement under the order and gap constraints,
/// found with the pool-adjacent-violators pass, then held inside the rail.
///
/// A rail can also have keep-outs: stretches no card may cover, where the
/// connector from a card on an outer rail to its detail panel crosses it.
/// Every way of sharing the cards out between the stretches around them is
/// tried, keeping their order, and the closest placement wins.
///
/// It allocates nothing once its buffers have grown to the largest rail.
/// </summary>
public static class RailCardSpread
{
    /// <summary>
    /// How much closer, as a share of its own distance, another sharing of
    /// the cards around the keep-outs has to come before it replaces the one
    /// they already have. Stops a card near a connector flicking from one
    /// side of it to the other.
    /// </summary>
    public const float SplitStickiness = 0.5f;

    private const float SplitStickinessFloor = 64f;

    private static float[] blockValues = new float[16];
    private static int[] blockSizes = new int[16];
    private static float[] trialTargets = new float[16];
    private static float[] bestTargets = new float[16];
    private static float[] keepStartBuffer = new float[8];
    private static float[] keepEndBuffer = new float[8];
    private static int[] cuts = new int[8];
    private static int[] bestCuts = new int[8];

    /// <summary>
    /// Writes each card's centre to <paramref name="targets"/>. Positions
    /// increase along the rail; <paramref name="desired"/> holds the sensors'
    /// positions in card order, <paramref name="extents"/> the cards' lengths
    /// along the rail, and <paramref name="start"/> and <paramref name="end"/>
    /// the rail's usable ends.
    /// </summary>
    public static void Solve(
        float[] desired,
        float[] extents,
        int count,
        float gap,
        float start,
        float end,
        float[] targets)
    {
        if (count <= 0)
            return;
        EnsureBuffer(ref blockValues, count);
        EnsureBuffer(ref blockSizes, count);
        SolveRange(desired, extents, 0, count, gap, start, end, targets);
    }

    /// <summary>
    /// As <see cref="Solve(float[],float[],int,float,float,float,float[])"/>,
    /// with no card covering any of the <paramref name="keepOutCount"/>
    /// stretches from <paramref name="keepOutStarts"/> to
    /// <paramref name="keepOutEnds"/> (in any order; overlapping ones merge).
    /// <paramref name="split"/> carries, from one call to the next, how many
    /// cards sat before each keep-out, so the cards stay on their side of a
    /// connector until the other side is clearly closer. False when the cards
    /// cannot all clear the keep-outs; they are then placed as if there were none.
    /// </summary>
    public static bool Solve(
        float[] desired,
        float[] extents,
        int count,
        float gap,
        float start,
        float end,
        float[] keepOutStarts,
        float[] keepOutEnds,
        int keepOutCount,
        List<int> split,
        float[] targets)
    {
        int keeps = MergeKeepOuts(keepOutStarts, keepOutEnds, keepOutCount, start, end);
        if (count <= 0 || keeps == 0)
        {
            split?.Clear();
            Solve(desired, extents, count, gap, start, end, targets);
            return true;
        }

        EnsureBuffer(ref blockValues, count);
        EnsureBuffer(ref blockSizes, count);
        EnsureBuffer(ref trialTargets, count);
        EnsureBuffer(ref bestTargets, count);
        EnsureBuffer(ref cuts, keeps);
        EnsureBuffer(ref bestCuts, keeps);

        float bestCost = float.PositiveInfinity;
        float previousCost = float.PositiveInfinity;
        bool previousUsable = split != null && split.Count == keeps;

        // Each sharing is a run of cut points: cuts[k] cards sit before keep-out k.
        Search(0, 0);

        if (float.IsPositiveInfinity(bestCost))
        {
            split?.Clear();
            Solve(desired, extents, count, gap, start, end, targets);
            return false;
        }

        // The sharing already in use stays while it is about as close.
        if (previousUsable && previousCost <= bestCost * (1f + SplitStickiness) + SplitStickinessFloor)
        {
            for (int k = 0; k < keeps; k++)
                cuts[k] = split[k];
            Evaluate(keeps);
            for (int i = 0; i < count; i++)
                targets[i] = trialTargets[i];
            return true;
        }

        for (int i = 0; i < count; i++)
            targets[i] = bestTargets[i];
        if (split != null)
        {
            split.Clear();
            for (int k = 0; k < keeps; k++)
                split.Add(bestCuts[k]);
        }
        return true;

        void Search(int keep, int from)
        {
            if (keep == keeps)
            {
                float cost = Evaluate(keeps);
                if (float.IsPositiveInfinity(cost))
                    return;
                if (previousUsable && SameAsPrevious())
                    previousCost = cost;
                if (cost < bestCost)
                {
                    bestCost = cost;
                    for (int i = 0; i < count; i++)
                        bestTargets[i] = trialTargets[i];
                    for (int k = 0; k < keeps; k++)
                        bestCuts[k] = cuts[k];
                }
                return;
            }

            // Cards from 'from' up to the cut sit in the stretch before this keep-out;
            // once they no longer fit there, more never will.
            float room = SegmentEnd(keep) - SegmentStart(keep);
            float used = 0f;
            for (int cut = from; cut <= count; cut++)
            {
                if (cut > from)
                {
                    used += extents[cut - 1] + (cut - 1 > from ? gap : 0f);
                    if (used > room + 0.01f)
                        break;
                }
                cuts[keep] = cut;
                Search(keep + 1, cut);
            }
        }

        bool SameAsPrevious()
        {
            for (int k = 0; k < keeps; k++)
            {
                if (cuts[k] != split[k])
                    return false;
            }
            return true;
        }

        float Evaluate(int keepCount)
        {
            float cost = 0f;
            int first = 0;
            for (int segment = 0; segment <= keepCount; segment++)
            {
                int last = segment < keepCount ? cuts[segment] : count;
                if (last < first)
                    return float.PositiveInfinity;
                int cards = last - first;
                if (cards > 0)
                {
                    float segmentStart = SegmentStart(segment);
                    float segmentEnd = SegmentEnd(segment);
                    float length = gap * (cards - 1);
                    for (int i = first; i < last; i++)
                        length += extents[i];
                    if (length > segmentEnd - segmentStart + 0.01f)
                        return float.PositiveInfinity;

                    SolveRange(desired, extents, first, cards, gap, segmentStart, segmentEnd, trialTargets);
                    for (int i = first; i < last; i++)
                    {
                        float offset = trialTargets[i] - desired[i];
                        cost += offset * offset;
                    }
                }
                first = last;
            }
            return cost;
        }

        float SegmentStart(int segment) => segment == 0 ? start : keepEndBuffer[segment - 1];
        float SegmentEnd(int segment) => segment == keeps ? end : keepStartBuffer[segment];
    }

    /// <summary>Grows a caller's buffer to hold <paramref name="count"/> values.</summary>
    public static void EnsureBuffer(ref float[] buffer, int count)
    {
        if (buffer == null || buffer.Length < count)
            buffer = new float[Mathf.Max(count, buffer == null ? 16 : buffer.Length * 2)];
    }

    private static void EnsureBuffer(ref int[] buffer, int count)
    {
        if (buffer == null || buffer.Length < count)
            buffer = new int[Mathf.Max(count, buffer == null ? 16 : buffer.Length * 2)];
    }

    /// <summary>
    /// Places cards <paramref name="from"/> to <paramref name="from"/> +
    /// <paramref name="count"/> between <paramref name="start"/> and
    /// <paramref name="end"/>, writing their centres to the same indices of
    /// <paramref name="targets"/>.
    /// </summary>
    private static void SolveRange(
        float[] desired,
        float[] extents,
        int from,
        int count,
        float gap,
        float start,
        float end,
        float[] targets)
    {
        int stop = from + count;

        // Measured from the first card, the spacing rule becomes "never move
        // backwards": a card's offset is the room the cards before it need.
        float offset = 0f;
        for (int i = from; i < stop; i++)
        {
            if (i > from)
                offset += (extents[i - 1] + extents[i]) * 0.5f + gap;
            targets[i] = desired[i] - offset;
        }

        // Pool adjacent violators: a value lower than the one before merges
        // with it at their mean, which is where the crowded cards balance.
        int blocks = 0;
        for (int i = from; i < stop; i++)
        {
            blockValues[blocks] = targets[i];
            blockSizes[blocks] = 1;
            blocks++;

            while (blocks > 1 && blockValues[blocks - 2] > blockValues[blocks - 1])
            {
                int size = blockSizes[blocks - 2] + blockSizes[blocks - 1];
                blockValues[blocks - 2] =
                    (blockValues[blocks - 2] * blockSizes[blocks - 2] +
                     blockValues[blocks - 1] * blockSizes[blocks - 1]) / size;
                blockSizes[blocks - 2] = size;
                blocks--;
            }
        }

        // Inside the rail: the first card may not start before its start, the
        // last may not finish past its end. A rail too short for its cards
        // keeps them packed from the start.
        float lowest = start + extents[from] * 0.5f;
        float highest = end - extents[stop - 1] * 0.5f - offset;
        if (highest < lowest)
            highest = lowest;

        int index = from;
        offset = 0f;
        for (int block = 0; block < blocks; block++)
        {
            float value = Mathf.Clamp(blockValues[block], lowest, highest);
            for (int k = 0; k < blockSizes[block]; k++, index++)
            {
                if (index > from)
                    offset += (extents[index - 1] + extents[index]) * 0.5f + gap;
                targets[index] = value + offset;
            }
        }
    }

    /// <summary>
    /// Sorts the keep-outs into the buffers, clipped to the rail, merging any
    /// that overlap. Returns how many there are.
    /// </summary>
    private static int MergeKeepOuts(float[] starts, float[] ends, int count, float railStart, float railEnd)
    {
        if (starts == null || ends == null || count <= 0)
            return 0;

        EnsureBuffer(ref keepStartBuffer, count);
        EnsureBuffer(ref keepEndBuffer, count);
        int kept = 0;
        for (int i = 0; i < count; i++)
        {
            float a = Mathf.Min(starts[i], ends[i]);
            float b = Mathf.Max(starts[i], ends[i]);
            if (b <= railStart || a >= railEnd)
                continue;

            // Insertion sort by start: there are only ever a few.
            int at = kept;
            while (at > 0 && keepStartBuffer[at - 1] > a)
            {
                keepStartBuffer[at] = keepStartBuffer[at - 1];
                keepEndBuffer[at] = keepEndBuffer[at - 1];
                at--;
            }
            keepStartBuffer[at] = Mathf.Max(a, railStart);
            keepEndBuffer[at] = Mathf.Min(b, railEnd);
            kept++;
        }

        int merged = 0;
        for (int i = 0; i < kept; i++)
        {
            if (merged > 0 && keepStartBuffer[i] <= keepEndBuffer[merged - 1])
            {
                keepEndBuffer[merged - 1] = Mathf.Max(keepEndBuffer[merged - 1], keepEndBuffer[i]);
                continue;
            }
            keepStartBuffer[merged] = keepStartBuffer[i];
            keepEndBuffer[merged] = keepEndBuffer[i];
            merged++;
        }
        return merged;
    }
}
