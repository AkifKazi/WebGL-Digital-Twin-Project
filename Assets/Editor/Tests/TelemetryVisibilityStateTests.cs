using NUnit.Framework;

/// <summary>
/// The rules of a layout's telemetry visibility, without a scene. Run with
/// <c>unity test --mode EditMode</c>.
/// </summary>
public sealed class TelemetryVisibilityStateTests
{
    private const float Transition = 0.35f;

    [Test]
    public void StartsOnTheChosenView()
    {
        TelemetryVisibilityState telemetry = new(DigitalTwinMode.Telemetry, true);
        Assert.AreEqual(1f, telemetry.TelemetryAlpha);
        Assert.IsTrue(telemetry.TelemetryBlocksPointer);
        Assert.AreEqual(0f, telemetry.ServiceAlpha);
        Assert.IsFalse(telemetry.ServiceBlocksPointer);

        TelemetryVisibilityState service = new(DigitalTwinMode.Service, true);
        Assert.AreEqual(0f, service.TelemetryAlpha);
        Assert.IsFalse(service.TelemetryInteractive);
        Assert.AreEqual(1f, service.ServiceAlpha);
        Assert.IsTrue(service.ServiceBlocksPointer);
    }

    [Test]
    public void SwitchingBlendsOverTheTransitionTime()
    {
        TelemetryVisibilityState state = new(DigitalTwinMode.Telemetry, true);
        state.SetMode(DigitalTwinMode.Service);

        Assert.IsTrue(state.Step(Transition * 0.5f, Transition));
        Assert.AreEqual(0.5f, state.ServiceBlend, 1e-4f);
        Assert.AreEqual(0.5f, state.TelemetryAlpha, 1e-4f, "eased, the two views cross at half way");

        Assert.IsTrue(state.Step(Transition, Transition));
        Assert.AreEqual(1f, state.ServiceBlend);
        Assert.IsFalse(state.Step(Transition, Transition), "settled: nothing more to apply");
    }

    [Test]
    public void EasingCrossesTheViewsOverRatherThanBothAtHalf()
    {
        TelemetryVisibilityState state = new(DigitalTwinMode.Telemetry, true);
        state.SetMode(DigitalTwinMode.Service);
        state.Step(Transition * 0.25f, Transition);

        Assert.Less(state.ServiceAlpha, 0.25f);
        Assert.Greater(state.TelemetryAlpha, 0.75f);
    }

    [Test]
    public void TelemetryStopsTakingThePointerAsSoonAsServiceIsChosen()
    {
        TelemetryVisibilityState state = new(DigitalTwinMode.Telemetry, true);
        state.SetMode(DigitalTwinMode.Service);

        Assert.Greater(state.TelemetryAlpha, 0.99f, "still fully drawn before the blend moves");
        Assert.IsFalse(state.TelemetryInteractive);
        Assert.IsFalse(state.TelemetryBlocksPointer);
    }

    [Test]
    public void ServiceTakesThePointerOnlyOnceMostlyShown()
    {
        TelemetryVisibilityState state = new(DigitalTwinMode.Telemetry, true);
        state.SetMode(DigitalTwinMode.Service);
        state.Step(Transition * 0.2f, Transition);
        Assert.IsTrue(state.ServiceInteractive);
        Assert.IsFalse(state.ServiceBlocksPointer);

        state.Step(Transition, Transition);
        Assert.IsTrue(state.ServiceBlocksPointer);
    }

    [Test]
    public void SnapLandsOnTheChosenViewStraightAway()
    {
        TelemetryVisibilityState state = new(DigitalTwinMode.Telemetry, true);
        state.SetMode(DigitalTwinMode.Service);

        Assert.IsTrue(state.Snap());
        Assert.AreEqual(1f, state.ServiceBlend);
        Assert.IsFalse(state.Snap());
    }

    [Test]
    public void TheSheetsCoverHidesTelemetryAndMakesItInertPastHalf()
    {
        TelemetryVisibilityState state = new(DigitalTwinMode.Telemetry, true);

        state.SetCover(0.4f);
        Assert.AreEqual(0.6f, state.TelemetryAlpha, 1e-4f);
        Assert.IsTrue(state.TelemetryInteractive);

        state.SetCover(0.6f);
        Assert.AreEqual(0.4f, state.TelemetryAlpha, 1e-4f);
        Assert.IsFalse(state.TelemetryInteractive);
        Assert.AreEqual(0f, state.ServiceAlpha, "the cover never shows the service view");
    }

    [Test]
    public void CoverAndServiceBlendMultiply()
    {
        TelemetryVisibilityState state = new(DigitalTwinMode.Telemetry, true);
        state.SetCover(0.5f);
        state.SetMode(DigitalTwinMode.Service);
        state.Step(Transition * 0.5f, Transition);

        Assert.AreEqual(0.25f, state.TelemetryAlpha, 1e-4f);
    }

    [Test]
    public void CoverIsClampedAndReportsChangesOnlyOnce()
    {
        TelemetryVisibilityState state = new(DigitalTwinMode.Telemetry, true);
        Assert.IsTrue(state.SetCover(2f));
        Assert.AreEqual(1f, state.Cover);
        Assert.IsFalse(state.SetCover(1f));
        Assert.AreEqual(0f, state.TelemetryAlpha);
    }

    [Test]
    public void TheInspectionViewIsPassedOnWithoutFadingTheRegions()
    {
        TelemetryVisibilityState state = new(DigitalTwinMode.Telemetry, true);

        Assert.IsTrue(state.SetInspectionShowsTelemetry(false));
        Assert.IsFalse(state.InspectionShowsTelemetry);
        Assert.AreEqual(1f, state.TelemetryAlpha, "the cards hide one by one; the regions stay");
        Assert.IsFalse(state.SetInspectionShowsTelemetry(false));
    }

    [Test]
    public void ChoosingTheSameViewIsNotAChange()
    {
        TelemetryVisibilityState state = new(DigitalTwinMode.Service, true);
        Assert.IsFalse(state.SetMode(DigitalTwinMode.Service));
        Assert.IsTrue(state.SetMode(DigitalTwinMode.Telemetry));
    }
}
