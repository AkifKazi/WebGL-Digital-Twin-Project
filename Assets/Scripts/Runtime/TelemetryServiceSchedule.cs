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

    /// <summary>The machine part whose service plan covers the source, so the telemetry sheet can open its service detail.</summary>
    bool TryGetServicedPart(PerformanceStatSource source, out MachinePartGroup part);
}

/// <summary>What a service plan asks for when it comes due.</summary>
public enum ServiceTask
{
    Inspect = 0,
    Regrease = 1,
    [InspectorName("Oil Change")] OilChange = 2,
    Replace = 3
}

/// <summary>What a part's failure would do to production.</summary>
public enum ServiceCriticality
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>How close a part is to its service, by the estimated interval.</summary>
public enum ServiceState
{
    [InspectorName("On Track")] OnTrack = 0,
    [InspectorName("Due Soon")] DueSoon = 1,
    Overdue = 2
}

/// <summary>One part's service position at a moment in time.</summary>
public readonly struct ServiceItemStatus
{
    public readonly MachinePartGroup Part;
    public readonly ServiceTask Task;

    /// <summary>The manufacturer's interval, in operating hours.</summary>
    public readonly double PlanHours;

    /// <summary>The interval adjusted for operating conditions, in operating hours.</summary>
    public readonly double EstimateHours;

    /// <summary>Operating hours run since the part was last serviced.</summary>
    public readonly double RunHours;

    public readonly ServiceState State;

    public ServiceItemStatus(
        MachinePartGroup part,
        ServiceTask task,
        double planHours,
        double estimateHours,
        double runHours,
        ServiceState state)
    {
        Part = part;
        Task = task;
        PlanHours = planHours;
        EstimateHours = estimateHours;
        RunHours = runHours;
        State = state;
    }

    /// <summary>Hours until the estimated service point. Negative once overdue.</summary>
    public double HoursLeft => EstimateHours - RunHours;
}

/// <summary>
/// A simple service plan: each machine part has a task, the manufacturer's
/// service interval, an estimate of that interval under the machine's actual
/// operating conditions, and the operating hours run since its last service.
/// While the scene runs, the hours count up as the machine operates.
///
/// The estimate is authored here for now. A condition model reading the part's
/// sensors can replace it later without touching the views.
/// </summary>
[DisallowMultipleComponent]
public sealed class TelemetryServiceSchedule : MonoBehaviour, ITelemetryServiceLifeProvider
{
    [Serializable]
    public struct ServicePlan
    {
        [Tooltip("The machine part the plan covers. Every sensor listed on it shows these hours.")]
        public MachinePartGroup part;

        [Tooltip("What the service is.")]
        public ServiceTask task;

        [Tooltip("The manufacturer's interval: operating hours between services.")]
        [Min(1f)] public float serviceIntervalHours;

        [Tooltip("The interval under this machine's operating conditions. Shorter than the " +
                 "plan when the part runs hot or heavily loaded, longer when it runs easy. " +
                 "0 uses the plan.")]
        [Min(0f)] public float estimatedIntervalHours;

        [Tooltip("Operating hours run since the last service, when the scene starts.")]
        [Min(0f)] public float hoursSinceService;

        [Header("Detail")]
        [Tooltip("What a failure of the part would do to production.")]
        public ServiceCriticality criticality;

        [Tooltip("Why the estimate sits where it does against the plan.")]
        public string likelyCause;

        [Tooltip("What the service involves, in a few words.")]
        public string suggestedFix;

        [Tooltip("Lubricant or spare parts the service needs, with amounts.")]
        public string materials;

        [Tooltip("Typical time the service takes, in minutes. 0 hides it.")]
        [Min(0f)] public float durationMinutes;

        [Tooltip("Operating hours the part ran before each of its last services, oldest first.")]
        public float[] pastIntervals;

        /// <summary>The interval the schedule works to: the estimate, or the plan without one.</summary>
        public float EffectiveIntervalHours =>
            estimatedIntervalHours > 0f ? estimatedIntervalHours : serviceIntervalHours;
    }

    [Tooltip("Service plans, one per machine part.")]
    [SerializeField] private List<ServicePlan> plans = new();

    [Tooltip("The machine's total operating hours when the scene starts.")]
    [SerializeField, Min(0f)] private float machineRunHours;

    [Tooltip("Operating hours counted per real hour while the scene runs. 1 is real time, " +
             "0 holds the hours still.")]
    [SerializeField, Min(0f)] private float operatingHoursPerHour = 1f;

    [Tooltip("Typical operating hours per calendar day, used to turn hours left into a date.")]
    [SerializeField, Range(1f, 24f)] private float operatingHoursPerDay = 16f;

    [Header("Due Soon")]
    [Tooltip("A part is due soon with this many operating hours or fewer left.")]
    [SerializeField, Min(0f)] private float dueSoonHours = 200f;

    [Tooltip("A part is also due soon with this share of its interval or less left, " +
             "whichever comes first.")]
    [SerializeField, Range(0f, 1f)] private float dueSoonShare = 0.2f;

    private float startedAt;

    public IReadOnlyList<ServicePlan> Plans => plans;

    public float OperatingHoursPerDay => operatingHoursPerDay;

    /// <summary>The machine's total operating hours now.</summary>
    public double MachineRunHours => machineRunHours + HoursRunThisSession;

    private double HoursRunThisSession =>
        (Time.unscaledTime - startedAt) / 3600d * operatingHoursPerHour;

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

        double session = HoursRunThisSession;
        foreach (ServicePlan plan in plans)
        {
            if (plan.part == null || !Covers(plan.part, source))
                continue;
            hours = plan.EffectiveIntervalHours - plan.hoursSinceService - session;
            return true;
        }

        return false;
    }

    public bool TryGetServicedPart(PerformanceStatSource source, out MachinePartGroup part)
    {
        part = null;
        if (source == null)
            return false;
        foreach (ServicePlan plan in plans)
        {
            if (plan.part != null && Covers(plan.part, source))
            {
                part = plan.part;
                return true;
            }
        }
        return false;
    }

    /// <summary>Fills <paramref name="statuses"/> with every plan's position now, in plan order.</summary>
    public void GetStatuses(List<ServiceItemStatus> statuses)
    {
        statuses.Clear();
        double session = HoursRunThisSession;
        foreach (ServicePlan plan in plans)
        {
            if (plan.part == null)
                continue;

            double estimate = plan.EffectiveIntervalHours;
            double run = plan.hoursSinceService + session;
            statuses.Add(new ServiceItemStatus(
                plan.part,
                plan.task,
                plan.serviceIntervalHours,
                estimate,
                run,
                StateFor(estimate, estimate - run)));
        }
    }

    /// <summary>The plan covering <paramref name="part"/>, if any.</summary>
    public bool TryGetPlan(MachinePartGroup part, out ServicePlan plan)
    {
        foreach (ServicePlan candidate in plans)
        {
            if (candidate.part != null && candidate.part == part)
            {
                plan = candidate;
                return true;
            }
        }
        plan = default;
        return false;
    }

    /// <summary>Calendar date the given operating hours before now fell on.</summary>
    public DateTime DateBefore(double operatingHours) =>
        DateTime.Now.AddDays(-Math.Max(0d, operatingHours) / operatingHoursPerDay);

    /// <summary>Calendar date the given operating hours from now fall on.</summary>
    public DateTime DateAfter(double operatingHours) =>
        DateTime.Now.AddDays(Math.Max(0d, operatingHours) / operatingHoursPerDay);

    private ServiceState StateFor(double interval, double hoursLeft)
    {
        if (hoursLeft <= 0d)
            return ServiceState.Overdue;
        if (hoursLeft <= dueSoonHours || hoursLeft <= interval * dueSoonShare)
            return ServiceState.DueSoon;
        return ServiceState.OnTrack;
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
