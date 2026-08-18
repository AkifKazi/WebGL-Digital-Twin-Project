using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TelemetryEquipmentGroup : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string groupId = "EQUIPMENT-01";
    [SerializeField] private string displayName = "EQUIPMENT";

    [Header("Physical Mapping")]
    [SerializeField] private Transform worldAnchor;
    [SerializeField] private PerformanceStatSource[] members = Array.Empty<PerformanceStatSource>();
    [SerializeField, Min(0.01f)] private float maximumMemberDistance = 0.75f;

    [Header("Presentation")]
    [SerializeField, Range(1, 6)] private int maximumVisibleMetrics = 3;
    [SerializeField, Min(0f)] private float regroupStableSeconds = 12f;

    public event Action PresentationChanged;

    private readonly Dictionary<PerformanceStatSource, string> partitionKeys = new();
    private readonly Dictionary<PerformanceStatSource, StatVisualState> observedStates = new();
    private float stableSince;
    private bool pendingRegroup;
    private bool stateChangeAlreadyHandled;

    public string GroupId => groupId;
    public string DisplayName => displayName;
    public Transform WorldAnchor => worldAnchor != null ? worldAnchor : transform;
    public IReadOnlyList<PerformanceStatSource> Members => members;
    public int MaximumVisibleMetrics => maximumVisibleMetrics;
    public float MaximumMemberDistance => maximumMemberDistance;

    private void OnEnable()
    {
        InitialisePartitions();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Update()
    {
        if (!pendingRegroup || Time.unscaledTime - stableSince < regroupStableSeconds)
            return;

        pendingRegroup = false;
        MergeMatchingStates();
        PresentationChanged?.Invoke();
    }

    public IEnumerable<TelemetryCardPresentation> BuildPresentations()
    {
        EnsureMembersHaveKeys();

        foreach (IGrouping<string, PerformanceStatSource> partition in members
                     .Where(source => source != null && source.IsVisible)
                     .GroupBy(source => partitionKeys[source]))
        {
            PerformanceStatSource[] ordered = partition
                .OrderByDescending(source => source.GetDisplayRank())
                .ThenBy(source => source.StatId, StringComparer.Ordinal)
                .ToArray();

            if (ordered.Length == 0)
                continue;

            // A card is a presentation unit, not a storage limit. If an
            // equipment group has more readings than one card can display,
            // create additional cards so the rail allocator can use any
            // remaining outer/opposite/inner rail capacity before hiding data.
            int pageCount = Mathf.CeilToInt(ordered.Length / (float)maximumVisibleMetrics);
            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                PerformanceStatSource[] page = ordered
                    .Skip(pageIndex * maximumVisibleMetrics)
                    .Take(maximumVisibleMetrics)
                    .ToArray();

                yield return new TelemetryCardPresentation(
                    $"{groupId}:{partition.Key}:page-{pageIndex + 1:D2}",
                    displayName,
                    WorldAnchor,
                    page,
                    maximumVisibleMetrics);
            }
        }
    }

    public bool Contains(PerformanceStatSource source) =>
        source != null && Array.IndexOf(members, source) >= 0;

    private void HandleSourceChanged(PerformanceStatSource changed)
    {
        if (changed == null || !partitionKeys.ContainsKey(changed))
            return;

        if (observedStates.TryGetValue(changed, out StatVisualState previousState) &&
            previousState == changed.VisualState)
            return;
        observedStates[changed] = changed.VisualState;
        stateChangeAlreadyHandled = true;

        bool splitOccurred = SplitMixedPartitions();
        pendingRegroup = HasMergeCandidates();
        stableSince = Time.unscaledTime;

        if (splitOccurred)
            PresentationChanged?.Invoke();
    }

    private void HandleLayoutChanged(PerformanceStatSource source)
    {
        if (stateChangeAlreadyHandled)
        {
            stateChangeAlreadyHandled = false;
            return;
        }

        // Visibility and explicit layout-priority edits require a rebuild.
        PresentationChanged?.Invoke();
    }

    private bool SplitMixedPartitions()
    {
        bool changed = false;
        foreach (IGrouping<string, PerformanceStatSource> partition in members
                     .Where(source => source != null)
                     .GroupBy(source => partitionKeys[source])
                     .ToArray())
        {
            StatVisualState[] states = partition.Select(source => source.VisualState).Distinct().ToArray();
            if (states.Length <= 1)
                continue;

            foreach (PerformanceStatSource source in partition)
                partitionKeys[source] = $"{partition.Key}/{source.VisualState}";
            changed = true;
        }

        return changed;
    }

    private bool HasMergeCandidates()
    {
        return members
            .Where(source => source != null)
            .GroupBy(source => source.VisualState)
            .Any(stateGroup => stateGroup.Select(source => partitionKeys[source]).Distinct().Count() > 1);
    }

    private void MergeMatchingStates()
    {
        foreach (PerformanceStatSource source in members.Where(source => source != null))
            partitionKeys[source] = $"state:{source.VisualState}";
    }

    private void InitialisePartitions()
    {
        partitionKeys.Clear();
        observedStates.Clear();
        foreach (PerformanceStatSource source in members.Where(source => source != null))
        {
            partitionKeys[source] = $"state:{source.VisualState}";
            observedStates[source] = source.VisualState;
        }
    }

    private void EnsureMembersHaveKeys()
    {
        foreach (PerformanceStatSource source in members.Where(source => source != null))
        {
            if (!partitionKeys.ContainsKey(source))
                partitionKeys[source] = $"state:{source.VisualState}";
            if (!observedStates.ContainsKey(source))
                observedStates[source] = source.VisualState;
        }
    }

    private void Subscribe()
    {
        foreach (PerformanceStatSource source in members.Where(source => source != null))
        {
            source.Changed -= HandleSourceChanged;
            source.Changed += HandleSourceChanged;
            source.LayoutPriorityChanged -= HandleLayoutChanged;
            source.LayoutPriorityChanged += HandleLayoutChanged;
        }
    }

    private void Unsubscribe()
    {
        foreach (PerformanceStatSource source in members.Where(source => source != null))
        {
            source.Changed -= HandleSourceChanged;
            source.LayoutPriorityChanged -= HandleLayoutChanged;
        }
    }
}

public sealed class TelemetryCardPresentation
{
    public string Key { get; }
    public string DisplayName { get; }
    public Transform WorldAnchor { get; }
    public IReadOnlyList<PerformanceStatSource> Sources { get; }
    public int MaximumVisibleMetrics { get; }
    public PerformanceStatSource PrimarySource { get; }
    public StatVisualState VisualState => PrimarySource.VisualState;
    public int DisplayRank => Sources.Max(source => source.GetDisplayRank());
    public PreferredStatRail PreferredRail => PrimarySource.PreferredRail;
    public PreferredPortraitStatRail PreferredPortraitRail => PrimarySource.PreferredPortraitRail;

    public TelemetryCardPresentation(
        string key,
        string displayName,
        Transform worldAnchor,
        IReadOnlyList<PerformanceStatSource> sources,
        int maximumVisibleMetrics)
    {
        Key = key;
        DisplayName = displayName;
        WorldAnchor = worldAnchor;
        Sources = sources;
        MaximumVisibleMetrics = Mathf.Max(1, maximumVisibleMetrics);
        PrimarySource = sources.OrderByDescending(source => source.GetDisplayRank()).First();
    }

    public static TelemetryCardPresentation Single(PerformanceStatSource source) =>
        new(source.StatId, source.MetricName, source.WorldAnchor,
            new[] { source }, 1);
}

public static class TelemetryPresentationBuilder
{
    public static List<TelemetryCardPresentation> Build(IEnumerable<PerformanceStatSource> sources)
    {
        PerformanceStatSource[] sourceArray = sources?.Where(source => source != null).ToArray()
            ?? Array.Empty<PerformanceStatSource>();
        TelemetryEquipmentGroup[] groups = UnityEngine.Object.FindObjectsByType<TelemetryEquipmentGroup>(
            FindObjectsInactive.Exclude);
        HashSet<PerformanceStatSource> grouped = new();
        List<TelemetryCardPresentation> result = new();

        foreach (TelemetryEquipmentGroup group in groups)
        {
            foreach (PerformanceStatSource member in group.Members.Where(member => member != null))
                grouped.Add(member);
            result.AddRange(group.BuildPresentations());
        }

        foreach (PerformanceStatSource source in sourceArray)
        {
            if (!grouped.Contains(source) && source.IsVisible)
                result.Add(TelemetryCardPresentation.Single(source));
        }

        return result;
    }
}
