using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Answers how many operating hours a sensor's machine part has left before its
/// next service. A maintenance system can implement it later without touching
/// the detail panel.
/// </summary>
public interface ITelemetryServiceLifeProvider
{
    /// <summary>False when no service plan covers the source. Zero or less means overdue.</summary>
    bool TryGetHoursToService(PerformanceStatSource source, out double hours);
}

/// <summary>
/// A simple service plan: each machine part has a service interval and the
/// operating hours run since its last service. The panel shows the hours left
/// for the part a sensor is read from. While the scene runs, the hours count up
/// as the machine operates.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryServiceSchedule : MonoBehaviour, ITelemetryServiceLifeProvider
{
    [Serializable]
    public struct ServicePlan
    {
        [Tooltip("The machine part the plan covers. Every sensor listed on it shows these hours.")]
        public MachinePartGroup part;

        [Tooltip("Operating hours between services.")]
        [Min(1f)] public float serviceIntervalHours;

        [Tooltip("Operating hours run since the last service, when the scene starts.")]
        [Min(0f)] public float hoursSinceService;
    }

    [Tooltip("Service plans, one per machine part.")]
    [SerializeField] private List<ServicePlan> plans = new();

    [Tooltip("Operating hours counted per real hour while the scene runs. 1 is real time, " +
             "0 holds the hours still.")]
    [SerializeField, Min(0f)] private float operatingHoursPerHour = 1f;

    private float startedAt;

    public IReadOnlyList<ServicePlan> Plans => plans;

    // Awake, not OnEnable: disabling and re-enabling the component must not
    // wind the hours already run back to zero.
    private void Awake()
    {
        startedAt = Time.unscaledTime;
    }

    public bool TryGetHoursToService(PerformanceStatSource source, out double hours)
    {
        hours = 0d;
        if (source == null)
            return false;

        double runHours = (Time.unscaledTime - startedAt) / 3600d * operatingHoursPerHour;
        foreach (ServicePlan plan in plans)
        {
            if (plan.part == null || !Covers(plan.part, source))
                continue;
            hours = plan.serviceIntervalHours - plan.hoursSinceService - runHours;
            return true;
        }

        return false;
    }

    private static bool Covers(MachinePartGroup part, PerformanceStatSource source)
    {
        IReadOnlyList<PerformanceStatSource> sensors = part.BoundSensors;
        for (int i = 0; i < sensors.Count; i++)
        {
            if (sensors[i] == source)
                return true;
        }
        return false;
    }
}
