using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum TelemetrySourceSelectionMode
{
    SceneDiscovery,
    ExplicitList
}

/// <summary>
/// Central source-selection rule shared by the registry and presentation layers.
/// Scene discovery is the default so a machine prefab can bring its own sensors.
/// </summary>
public static class TelemetrySourceResolver
{
    public static PerformanceStatSource[] Resolve(
        TelemetrySourceSelectionMode mode,
        IEnumerable<PerformanceStatSource> configuredSources)
    {
        IEnumerable<PerformanceStatSource> candidates = mode ==
            TelemetrySourceSelectionMode.SceneDiscovery
            ? UnityEngine.Object.FindObjectsByType<PerformanceStatSource>(FindObjectsInactive.Include)
            : configuredSources ?? Array.Empty<PerformanceStatSource>();

        return candidates
            .Where(source => source != null &&
                             source.gameObject.scene.IsValid())
            .Distinct()
            .OrderBy(source => source.StatId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
