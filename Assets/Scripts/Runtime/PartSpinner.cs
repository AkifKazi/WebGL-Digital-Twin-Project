using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spins any number of parts around their own axes: rotors, rollers, fans,
/// shafts, a gate wheel. Each entry carries its own axis, direction and speed
/// share, so one component can drive a whole mechanism, and the speed can
/// follow a telemetry reading instead of a fixed value.
/// </summary>
[DisallowMultipleComponent]
public sealed class PartSpinner : MonoBehaviour
{
    [Serializable]
    public struct SpinningPart
    {
        [Tooltip("The part to spin. It turns around its own axis, in its own space.")]
        public Transform target;

        [Tooltip("Axis to spin around, in the part's own space. Empty means up.")]
        public Vector3 axis;

        [Tooltip("Share of the speed below. 1 is full speed, 0.5 turns half as fast.")]
        public float speedShare;

        [Tooltip("Turns the other way, for a counter-rotating pair.")]
        public bool reverse;

        public Vector3 ResolvedAxis => axis.sqrMagnitude < 0.0001f ? Vector3.up : axis.normalized;
        public float ResolvedShare => Mathf.Approximately(speedShare, 0f) ? 1f : speedShare;
    }

    [Tooltip("Parts this component spins. Add or remove rows as the mechanism needs.")]
    [SerializeField] private List<SpinningPart> parts = new();

    [Header("Speed")]
    [Tooltip("Revolutions per minute when no sensor drives the speed.")]
    [SerializeField] private float revolutionsPerMinute = 1000f;

    [Tooltip("Optional. The speed follows this sensor's reading instead of the value above.")]
    [SerializeField] private PerformanceStatSource speedSource;

    [Tooltip("Revolutions per minute per unit of the sensor's reading. 1 when the sensor already reads RPM.")]
    [SerializeField] private float revolutionsPerReadingUnit = 1f;

    [Tooltip("Seconds the speed takes to catch up with a changed reading, so it never jumps.")]
    [SerializeField, Min(0f)] private float speedSmoothing = 0.6f;

    [Tooltip("Keeps spinning while the game is paused (time scale 0).")]
    [SerializeField] private bool ignoreTimeScale;

    private float currentRpm;
    private bool started;

    /// <summary>The speed the parts are turning at, in revolutions per minute.</summary>
    public float CurrentRevolutionsPerMinute => currentRpm;

    /// <summary>Sets the speed from code, for mechanisms driven by something other than a sensor.</summary>
    public void SetRevolutionsPerMinute(float value) => revolutionsPerMinute = value;

    private void OnEnable()
    {
        currentRpm = TargetRpm();
        started = true;
    }

    private void Update()
    {
        float delta = ignoreTimeScale ? Time.unscaledDeltaTime : Time.deltaTime;
        float target = TargetRpm();
        currentRpm = !started || speedSmoothing <= 0f
            ? target
            : Mathf.Lerp(currentRpm, target, 1f - Mathf.Exp(-delta / speedSmoothing));
        started = true;

        float degrees = currentRpm * 6f * delta;
        if (Mathf.Approximately(degrees, 0f))
            return;

        foreach (SpinningPart part in parts)
        {
            if (part.target == null)
                continue;
            float direction = part.reverse ? -1f : 1f;
            part.target.Rotate(part.ResolvedAxis, degrees * part.ResolvedShare * direction, Space.Self);
        }
    }

    private float TargetRpm() =>
        speedSource != null
            ? speedSource.CurrentValue * revolutionsPerReadingUnit
            : revolutionsPerMinute;
}
