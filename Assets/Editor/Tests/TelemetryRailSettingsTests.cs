using NUnit.Framework;
using UnityEngine;

/// <summary>
/// How many card rails a layout asks for: the viewer's choice by default,
/// two a side at most on a phone. Run with <c>unity test --mode EditMode</c>.
/// </summary>
public sealed class TelemetryRailSettingsTests
{
    private GameObject host;
    private TelemetryLayoutSettings settings;
    private int railsBefore;

    [SetUp]
    public void SetUp()
    {
        railsBefore = DigitalTwinSettings.RailsPerSide;
        host = new GameObject("Telemetry Layout Settings");
        settings = host.AddComponent<TelemetryLayoutSettings>();
    }

    [TearDown]
    public void TearDown()
    {
        DigitalTwinSettings.RailsPerSide = railsBefore;
        Object.DestroyImmediate(host);
    }

    [Test]
    public void EveryLayoutRunsTheViewersChoiceByDefault()
    {
        DigitalTwinSettings.RailsPerSide = 3;
        Assert.AreEqual(TelemetryRailCount.Three, settings.Resolve(false, false).Rails);
        Assert.AreEqual(TelemetryRailCount.Three, settings.Resolve(false, true).Rails);
        Assert.AreEqual(TelemetryRailCount.Three, settings.Resolve(true, true).Rails);
    }

    [Test]
    public void APhoneRunsAtMostTwoRailsASide()
    {
        settings.SetRails(true, true, TelemetryRailCount.Four);
        settings.SetRails(false, false, TelemetryRailCount.Four);
        Assert.AreEqual(2, settings.Resolve(true, true).MaximumRails);
        Assert.AreEqual(2, settings.Resolve(true, false).MaximumRails);
        Assert.AreEqual(4, settings.Resolve(false, false).MaximumRails);
    }

    [Test]
    public void TheViewersChoiceStaysWithinOneToFourASide()
    {
        DigitalTwinSettings.RailsPerSide = 9;
        Assert.AreEqual(DigitalTwinSettings.MaximumRailsPerSide, DigitalTwinSettings.RailsPerSide);
        DigitalTwinSettings.RailsPerSide = 0;
        Assert.AreEqual(DigitalTwinSettings.MinimumRailsPerSide, DigitalTwinSettings.RailsPerSide);
    }
}
