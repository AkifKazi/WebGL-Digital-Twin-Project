using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// How a rail spreads its cards, and how they clear the connectors that run
/// across it. Run with <c>unity test --mode EditMode</c>.
/// </summary>
public sealed class RailCardSpreadTests
{
    private const float Gap = 8f;
    private const float Extent = 100f;

    private static float[] Solve(float[] desired, float start, float end, float[] keepStarts, float[] keepEnds,
        List<int> split, out bool cleared)
    {
        int count = desired.Length;
        float[] extents = new float[count];
        for (int i = 0; i < count; i++)
            extents[i] = Extent;
        float[] targets = new float[count];
        cleared = RailCardSpread.Solve(desired, extents, count, Gap, start, end,
            keepStarts, keepEnds, keepStarts?.Length ?? 0, split, targets);
        return targets;
    }

    private static void AssertClear(float[] targets, float keepStart, float keepEnd)
    {
        foreach (float centre in targets)
        {
            bool before = centre + Extent * 0.5f <= keepStart + 0.01f;
            bool after = centre - Extent * 0.5f >= keepEnd - 0.01f;
            Assert.IsTrue(before || after, $"card at {centre} covers the keep-out {keepStart}..{keepEnd}");
        }
    }

    private static void AssertNoOverlap(float[] targets)
    {
        for (int i = 1; i < targets.Length; i++)
            Assert.GreaterOrEqual(targets[i] - targets[i - 1], Extent + Gap - 0.01f, "cards overlap or swap");
    }

    [Test]
    public void WithoutKeepOutsCardsSitOnTheirSensors()
    {
        float[] targets = Solve(new[] { 100f, 400f }, 0f, 1000f, null, null, null, out bool cleared);
        Assert.IsTrue(cleared);
        Assert.AreEqual(100f, targets[0], 1e-3f);
        Assert.AreEqual(400f, targets[1], 1e-3f);
    }

    [Test]
    public void ACardOnAConnectorMovesToTheNearerSide()
    {
        // The card's sensor sits just above the connector's middle.
        float[] targets = Solve(new[] { 520f }, 0f, 1000f, new[] { 490f }, new[] { 510f }, new List<int>(), out bool cleared);
        Assert.IsTrue(cleared);
        AssertClear(targets, 490f, 510f);
        Assert.AreEqual(560f, targets[0], 1e-3f, "just clear of the connector, on its own side");
    }

    [Test]
    public void AFullRailLessOneCardStillClearsAnyConnector()
    {
        // Room for five cards; four are on the rail, as the inner rails are filled.
        float length = 5 * Extent + 4 * Gap + 21f;
        float[] desired = { 50f, 160f, 270f, 380f };
        for (float line = 10f; line < length - 10f; line += 7f)
        {
            float[] targets = Solve(desired, 0f, length, new[] { line - 10.5f }, new[] { line + 10.5f },
                new List<int>(), out bool cleared);
            Assert.IsTrue(cleared, $"connector at {line}");
            AssertClear(targets, line - 10.5f, line + 10.5f);
            AssertNoOverlap(targets);
        }
    }

    [Test]
    public void CardsKeepTheirOrderAroundAConnector()
    {
        float[] targets = Solve(new[] { 300f, 310f, 320f }, 0f, 1000f, new[] { 300f }, new[] { 320f },
            new List<int>(), out _);
        AssertClear(targets, 300f, 320f);
        AssertNoOverlap(targets);
    }

    [Test]
    public void SeveralConnectorsAreAllCleared()
    {
        float[] targets = Solve(new[] { 100f, 300f, 500f }, 0f, 1000f,
            new[] { 290f, 90f }, new[] { 310f, 110f }, new List<int>(), out bool cleared);
        Assert.IsTrue(cleared);
        AssertClear(targets, 90f, 110f);
        AssertClear(targets, 290f, 310f);
        AssertNoOverlap(targets);
    }

    [Test]
    public void OverlappingKeepOutsMerge()
    {
        float[] targets = Solve(new[] { 500f }, 0f, 1000f, new[] { 480f, 500f }, new[] { 510f, 530f },
            new List<int>(), out bool cleared);
        Assert.IsTrue(cleared);
        AssertClear(targets, 480f, 530f);
    }

    [Test]
    public void ARailThatCannotClearTheConnectorPlacesCardsAsBefore()
    {
        // Exactly full: no card can step aside.
        float length = 3 * Extent + 2 * Gap;
        float[] targets = Solve(new[] { 50f, 158f, 266f }, 0f, length, new[] { 150f }, new[] { 170f },
            new List<int>(), out bool cleared);
        Assert.IsFalse(cleared);
        Assert.AreEqual(50f, targets[0], 1e-3f);
        Assert.AreEqual(158f, targets[1], 1e-3f);
    }

    [Test]
    public void ACardStaysOnItsSideUntilTheOtherIsClearlyCloser()
    {
        List<int> split = new();
        // Sensor above the connector: the card sits above it.
        Solve(new[] { 530f }, 0f, 1000f, new[] { 490f }, new[] { 510f }, split, out _);
        Assert.AreEqual(0, split[0], "no card before the connector");

        // The sensor drifts just past the middle: the card stays put.
        float[] targets = Solve(new[] { 497f }, 0f, 1000f, new[] { 490f }, new[] { 510f }, split, out _);
        Assert.AreEqual(560f, targets[0], 1e-3f);

        // Well past it, the card goes over, back onto its sensor.
        targets = Solve(new[] { 420f }, 0f, 1000f, new[] { 490f }, new[] { 510f }, split, out _);
        Assert.AreEqual(1, split[0]);
        Assert.AreEqual(420f, targets[0], 1e-3f);
    }
}
