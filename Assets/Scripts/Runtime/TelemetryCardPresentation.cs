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

    /// <summary>
    /// Viewport position that places a sensor's card. A sensor behind the
    /// camera keeps a card, so it stays reachable instead of dropping off the
    /// rails until another rebuild: its projection is mirrored, which puts the
    /// card on the side the camera would turn towards, and clamped, because
    /// a point near the camera plane projects to huge or undefined values. The
    /// leader line hides itself while the sensor is behind the camera.
    /// </summary>
    public static Vector2 PlacementViewport(Camera camera, Vector3 worldPosition)
    {
        Vector3 viewport = camera.WorldToViewportPoint(worldPosition);
        if (viewport.z > 0f)
            return viewport;

        return new Vector2(Mirror(viewport.x), Mirror(viewport.y));
    }

    /// <summary><see cref="PlacementViewport"/> in the camera's screen pixels.</summary>
    public static Vector2 PlacementScreenPoint(Camera camera, Vector3 worldPosition)
    {
        Vector2 viewport = PlacementViewport(camera, worldPosition);
        return camera.ViewportToScreenPoint(new Vector3(viewport.x, viewport.y, 1f));
    }

    private static float Mirror(float value) =>
        float.IsFinite(value) ? Mathf.Clamp(1f - value, -1f, 2f) : 0.5f;
}
