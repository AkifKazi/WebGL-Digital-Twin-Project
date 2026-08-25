using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// A presentation is deliberately one sensor and one card. This type retains
// the presentation boundary used by the rail allocators without supporting
// equipment grouping, partitioning, or multi-metric cards. Portrait paging
// operates on these independent presentations without changing sensor identity.
public sealed class TelemetryCardPresentation
{
    public string Key { get; }
    public string DisplayName { get; }
    public Transform WorldAnchor { get; }
    public PerformanceStatSource Source { get; }
    public StatVisualState VisualState => Source.VisualState;
    public int DisplayRank => Source.GetDisplayRank();
    public PreferredStatRail PreferredRail => Source.PreferredRail;
    public PreferredPortraitStatRail PreferredPortraitRail =>
        Source.PreferredPortraitRail;

    private TelemetryCardPresentation(PerformanceStatSource source)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Key = source.StatId;
        DisplayName = source.MetricName;
        WorldAnchor = source.WorldAnchor;
    }

    public static TelemetryCardPresentation Single(PerformanceStatSource source) =>
        new(source);
}

public static class TelemetryPresentationBuilder
{
    public static List<TelemetryCardPresentation> Build(
        IEnumerable<PerformanceStatSource> sources) =>
        sources?
            .Where(source => source != null && source.IsVisible)
            .Distinct()
            .Select(TelemetryCardPresentation.Single)
            .ToList()
        ?? new List<TelemetryCardPresentation>();
}
