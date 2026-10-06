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
/// It allocates nothing once its buffers have grown to the largest rail.
/// </summary>
public static class RailCardSpread
{
    private static float[] blockValues = new float[16];
    private static int[] blockSizes = new int[16];

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

        EnsureCapacity(count);

        // Measured from the first card, the spacing rule becomes "never move
        // backwards": a card's offset is the room the cards before it need.
        float offset = 0f;
        for (int i = 0; i < count; i++)
        {
            if (i > 0)
                offset += (extents[i - 1] + extents[i]) * 0.5f + gap;
            targets[i] = desired[i] - offset;
        }

        // Pool adjacent violators: a value lower than the one before merges
        // with it at their mean, which is where the crowded cards balance.
        int blocks = 0;
        for (int i = 0; i < count; i++)
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
        float lowest = start + extents[0] * 0.5f;
        float highest = end - extents[count - 1] * 0.5f - offset;
        if (highest < lowest)
            highest = lowest;

        int index = 0;
        offset = 0f;
        for (int block = 0; block < blocks; block++)
        {
            float value = Mathf.Clamp(blockValues[block], lowest, highest);
            for (int k = 0; k < blockSizes[block]; k++, index++)
            {
                if (index > 0)
                    offset += (extents[index - 1] + extents[index]) * 0.5f + gap;
                targets[index] = value + offset;
            }
        }
    }

    /// <summary>Grows a caller's buffer to hold <paramref name="count"/> values.</summary>
    public static void EnsureBuffer(ref float[] buffer, int count)
    {
        if (buffer == null || buffer.Length < count)
            buffer = new float[Mathf.Max(count, buffer == null ? 16 : buffer.Length * 2)];
    }

    private static void EnsureCapacity(int count)
    {
        if (blockValues.Length >= count)
            return;
        int size = Mathf.Max(count, blockValues.Length * 2);
        blockValues = new float[size];
        blockSizes = new int[size];
    }
}
