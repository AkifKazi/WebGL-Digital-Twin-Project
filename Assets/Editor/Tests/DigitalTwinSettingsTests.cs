using NUnit.Framework;

/// <summary>Defaults that depend on the device. Run with <c>unity test --mode EditMode</c>.</summary>
public sealed class DigitalTwinSettingsTests
{
    [TearDown]
    public void TearDown() => DeviceKind.EditorOverride = null;

    [Test]
    public void APhoneStartsAtTheDesignedSizeADesktopALittleSmaller()
    {
        DeviceKind.EditorOverride = true;
        Assert.AreEqual(1f, DigitalTwinSettings.DefaultInterfaceScale, 1e-4f);
        DeviceKind.EditorOverride = false;
        Assert.AreEqual(0.9f, DigitalTwinSettings.DefaultInterfaceScale, 1e-4f);
    }
}
