using System;
using UnityEngine;

public enum TelemetryOperatingMode
{
    Simulation,
    Live
}

[DisallowMultipleComponent]
[RequireComponent(typeof(TelemetryRegistry))]
public sealed class TelemetryOperatingModeController : MonoBehaviour
{
    public event Action<TelemetryOperatingMode> OperatingModeChanged;

    [SerializeField] private TelemetryOperatingMode operatingMode =
        TelemetryOperatingMode.Simulation;
    [SerializeField] private TelemetryRegistry registry;
    [SerializeField] private HopperProcessSimulator simulator;

    public TelemetryOperatingMode OperatingMode => operatingMode;
    public bool IsLive => operatingMode == TelemetryOperatingMode.Live;

    private void Awake()
    {
        if (registry == null)
            registry = GetComponent<TelemetryRegistry>();

        if (simulator == null)
            simulator = GetComponent<HopperProcessSimulator>();

        ApplyMode(false);
    }

    public void UseSimulation()
    {
        if (operatingMode == TelemetryOperatingMode.Simulation)
            return;

        operatingMode = TelemetryOperatingMode.Simulation;
        ApplyMode(true);
    }

    public void UseLiveData()
    {
        if (operatingMode == TelemetryOperatingMode.Live)
            return;

        operatingMode = TelemetryOperatingMode.Live;
        ApplyMode(true);
    }

    private void ApplyMode(bool reset)
    {
        if (simulator != null)
            simulator.enabled = operatingMode == TelemetryOperatingMode.Simulation;

        if (operatingMode == TelemetryOperatingMode.Simulation)
        {
            if (reset && simulator != null)
                simulator.ResetModel();

            OperatingModeChanged?.Invoke(operatingMode);
            return;
        }

        if (!reset || registry == null)
        {
            OperatingModeChanged?.Invoke(operatingMode);
            return;
        }

        // Do not present the last simulation sample as live plant data.
        foreach (PerformanceStatSource source in registry.Sources)
        {
            if (source != null)
                source.SetDataQuality(TelemetryDataQuality.Stale);
        }

        OperatingModeChanged?.Invoke(operatingMode);
    }
}
