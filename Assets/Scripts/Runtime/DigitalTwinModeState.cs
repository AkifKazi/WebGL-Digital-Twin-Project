using System;
using UnityEngine;

/// <summary>The two top-level views of the digital twin.</summary>
public enum DigitalTwinMode
{
    Telemetry = 0,
    Service = 1
}

/// <summary>
/// A layout's switch between the Telemetry and Service views: each layout's
/// <see cref="TelemetryVisibility"/>.
/// </summary>
public interface IDigitalTwinModeController
{
    DigitalTwinMode CurrentMode { get; }

    event Action<DigitalTwinMode> ModeChanged;

    void SetMode(DigitalTwinMode mode);
}

/// <summary>
/// The view chosen last, shared by both layouts' telemetry visibility, so turning a
/// phone between portrait and landscape keeps the view on show.
/// </summary>
public static class DigitalTwinModeState
{
    private static bool chosen;
    private static DigitalTwinMode current;

    /// <summary>The view chosen last, or <paramref name="fallback"/> before any choice.</summary>
    public static DigitalTwinMode Get(DigitalTwinMode fallback) => chosen ? current : fallback;

    public static void Set(DigitalTwinMode mode)
    {
        chosen = true;
        current = mode;
    }

    // Play mode may start without a domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => chosen = false;
}

/// <summary>
/// Asks for a machine part's service detail from anywhere (the telemetry
/// sheet's Time To Service figure). The active layout's service view switches to
/// Service and opens the part.
/// </summary>
public static class ServicePartLink
{
    public static event Action<MachinePartGroup> Requested;

    public static void Request(MachinePartGroup part)
    {
        if (part != null)
            Requested?.Invoke(part);
    }
}
