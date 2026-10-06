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

    // How far past the screen edge a sensor off screen is placed, as a share
    // of the screen's shorter side: enough to hide a leader line's anchor.
    private const float OffscreenOvershoot = 0.1f;

    /// <summary>
    /// Viewport position that places a sensor's card: <see cref="PlacementScreenPoint"/>
    /// over the camera's view.
    /// </summary>
    public static Vector2 PlacementViewport(Camera camera, Vector3 worldPosition)
    {
        Rect screen = camera.pixelRect;
        Vector2 point = PlacementScreenPoint(camera, worldPosition);
        return new Vector2(
            (point.x - screen.x) / Mathf.Max(1f, screen.width),
            (point.y - screen.y) / Mathf.Max(1f, screen.height));
    }

    /// <summary>
    /// Where a sensor sits on screen, in the camera's pixels. Cards and their
    /// leader lines both aim at this point.
    ///
    /// A sensor on screen, or just past its edge, is where it projects. One
    /// further out, or behind the camera, is held just past the screen edge,
    /// in the direction it lies from the middle of the view. A projection
    /// grows without bound as a sensor nears the camera's plane and flips
    /// behind it; the held point does neither. It runs on smoothly from the
    /// true projection at the edge and through the camera's plane, so a card
    /// and its line never jump as the camera turns, and a sensor behind the
    /// camera keeps its card on the side it lies.
    /// </summary>
    public static Vector2 PlacementScreenPoint(Camera camera, Vector3 worldPosition)
    {
        Rect screen = camera.pixelRect;
        float overshoot = Mathf.Min(screen.width, screen.height) * OffscreenOvershoot;
        Rect bounds = new(
            screen.xMin - overshoot, screen.yMin - overshoot,
            screen.width + overshoot * 2f, screen.height + overshoot * 2f);

        Vector3 projected = camera.WorldToScreenPoint(worldPosition);
        if (projected.z > 0f && float.IsFinite(projected.x) && float.IsFinite(projected.y) &&
            bounds.Contains(new Vector2(projected.x, projected.y)))
        {
            return projected;
        }

        // Off screen, a point's offset from where the view's axis lands is
        // along its camera-space x and y, in front of the camera or behind it.
        Vector3 local = camera.transform.InverseTransformPoint(worldPosition);
        Vector2 direction = new(local.x, local.y);
        // Straight behind the camera has no side; it is placed below.
        if (direction.sqrMagnitude < 0.000001f)
            direction = Vector2.down;
        direction.Normalize();

        Vector2 origin = ViewAxisPoint(camera, bounds);
        float distance = float.MaxValue;
        if (Mathf.Abs(direction.x) > 0.0001f)
            distance = Mathf.Min(distance, ((direction.x > 0f ? bounds.xMax : bounds.xMin) - origin.x) / direction.x);
        if (Mathf.Abs(direction.y) > 0.0001f)
            distance = Mathf.Min(distance, ((direction.y > 0f ? bounds.yMax : bounds.yMin) - origin.y) / direction.y);
        return origin + direction * Mathf.Max(0f, distance);
    }

    /// <summary>Where the camera's forward axis lands on screen; off centre while the view is shifted.</summary>
    private static Vector2 ViewAxisPoint(Camera camera, Rect bounds)
    {
        Transform view = camera.transform;
        Vector3 axis = camera.WorldToScreenPoint(view.position + view.forward * Mathf.Max(1f, camera.nearClipPlane * 2f));
        Vector2 point = float.IsFinite(axis.x) && float.IsFinite(axis.y)
            ? new Vector2(axis.x, axis.y)
            : camera.pixelRect.center;
        return new Vector2(
            Mathf.Clamp(point.x, bounds.xMin, bounds.xMax),
            Mathf.Clamp(point.y, bounds.yMin, bounds.yMax));
    }
}
