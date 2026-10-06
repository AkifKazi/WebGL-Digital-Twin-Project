using UnityEngine;

/// <summary>
/// What a layout's telemetry visibility adds up to, with no scene behind it:
/// the view (and the blend while switching), the sheet's cover and the
/// inspection view go in; how far telemetry and the service view show, and
/// whether each takes the pointer, come out. <see cref="TelemetryVisibility"/>
/// keeps one per layout and applies it to that layout's telemetry regions.
/// </summary>
public sealed class TelemetryVisibilityState
{
    private float blend;
    private float cover;

    public TelemetryVisibilityState(DigitalTwinMode mode, bool inspectionShowsTelemetry)
    {
        Mode = mode;
        InspectionShowsTelemetry = inspectionShowsTelemetry;
        blend = Target;
    }

    public DigitalTwinMode Mode { get; private set; }

    /// <summary>0 in Telemetry, 1 in Service, between the two while switching.</summary>
    public float ServiceBlend => blend;

    /// <summary>The blend eased, so the two views cross over rather than both sitting at half.</summary>
    public float EasedServiceBlend => Ease(blend);

    /// <summary>How far something over the model (the portrait sheet) hides telemetry: 0 not at all, 1 fully.</summary>
    public float Cover => cover;

    /// <summary>
    /// Whether the inspection view shows telemetry: Cross Section and X-Ray
    /// do, Full Model does not. The cards reveal and hide one by one on their
    /// own, so this does not fade the regions; it is passed on to what reveals them.
    /// </summary>
    public bool InspectionShowsTelemetry { get; private set; }

    public float TelemetryAlpha => (1f - EasedServiceBlend) * (1f - cover);

    public bool TelemetryInteractive => Mode == DigitalTwinMode.Telemetry && cover < 0.5f;

    public bool TelemetryBlocksPointer => TelemetryInteractive && TelemetryAlpha > 0.5f;

    public float ServiceAlpha => EasedServiceBlend;

    public bool ServiceInteractive => Mode == DigitalTwinMode.Service;

    public bool ServiceBlocksPointer => ServiceInteractive && ServiceAlpha > 0.5f;

    private float Target => Mode == DigitalTwinMode.Service ? 1f : 0f;

    /// <summary>Chooses the view; the blend moves towards it in <see cref="Step"/>. True when it changed.</summary>
    public bool SetMode(DigitalTwinMode mode)
    {
        if (mode == Mode)
            return false;
        Mode = mode;
        return true;
    }

    /// <summary>Lands on the chosen view straight away, as on returning to a layout. True when the blend moved.</summary>
    public bool Snap()
    {
        if (Mathf.Approximately(blend, Target))
            return false;
        blend = Target;
        return true;
    }

    /// <summary>Moves the blend towards the chosen view. True when it moved.</summary>
    public bool Step(float deltaTime, float transitionSeconds)
    {
        float next = Mathf.MoveTowards(blend, Target, deltaTime / Mathf.Max(0.0001f, transitionSeconds));
        if (Mathf.Approximately(next, blend))
        {
            blend = next;
            return false;
        }
        blend = next;
        return true;
    }

    /// <summary>True when the cover changed.</summary>
    public bool SetCover(float value)
    {
        value = Mathf.Clamp01(value);
        if (Mathf.Approximately(value, cover))
            return false;
        cover = value;
        return true;
    }

    /// <summary>True when it changed.</summary>
    public bool SetInspectionShowsTelemetry(bool shows)
    {
        if (shows == InspectionShowsTelemetry)
            return false;
        InspectionShowsTelemetry = shows;
        return true;
    }

    public static float Ease(float t) => t * t * (3f - 2f * t);
}
