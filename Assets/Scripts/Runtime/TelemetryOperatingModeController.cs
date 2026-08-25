using System;
using System.Linq;
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
    [Tooltip("Optional machine-specific component implementing ITelemetrySimulationProvider.")]
    [SerializeField] private MonoBehaviour simulator;

    public TelemetryOperatingMode OperatingMode => operatingMode;
    public bool IsLive => operatingMode == TelemetryOperatingMode.Live;

    private void Awake()
    {
        if (registry == null)
            registry = GetComponent<TelemetryRegistry>();

        if (simulator == null)
            simulator = GetComponents<MonoBehaviour>()
                .FirstOrDefault(component => component is ITelemetrySimulationProvider);

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
        UseLiveData(null);
    }

    public void UseLiveData(PerformanceStatSource acceptedSource)
    {
        if (operatingMode == TelemetryOperatingMode.Live)
            return;

        operatingMode = TelemetryOperatingMode.Live;
        ApplyMode(true, acceptedSource);
    }

    private void ApplyMode(bool reset, PerformanceStatSource acceptedSource = null)
    {
        if (simulator != null)
            simulator.enabled = operatingMode == TelemetryOperatingMode.Simulation;

        if (operatingMode == TelemetryOperatingMode.Simulation)
        {
            if (reset && simulator is ITelemetrySimulationProvider simulationProvider)
                simulationProvider.ResetSimulation();

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
            if (source != null && source != acceptedSource)
                source.SetDataQuality(TelemetryDataQuality.Stale);
        }

        OperatingModeChanged?.Invoke(operatingMode);
    }
}
