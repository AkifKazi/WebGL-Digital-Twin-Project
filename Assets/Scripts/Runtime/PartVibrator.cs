using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shakes any number of parts around their resting position: a vibrating screen
/// deck, springs, a loose guard, a motor housing. Each entry carries its own
/// amplitude and share of the speed, so one component can drive a whole
/// assembly, and the shake can follow a vibration reading instead of a fixed
/// amplitude.
/// </summary>
[DisallowMultipleComponent]
public sealed class PartVibrator : MonoBehaviour
{
    [Serializable]
    public struct VibratingPart
    {
        [Tooltip("The part to shake. It moves around wherever it rests when play starts.")]
        public Transform target;

        [Tooltip("How far it travels on each axis, in metres, in its own space.")]
        public Vector3 amplitude;

        [Tooltip("How far it rocks on each axis, in degrees.")]
        public Vector3 rotationAmplitude;

        [Tooltip("Share of the speed below. 1 is full speed.")]
        public float speedShare;

        [Tooltip("Offsets this part in the cycle, 0 to 1, so parts do not move as one block.")]
        public float phase;

        public float ResolvedShare => Mathf.Approximately(speedShare, 0f) ? 1f : speedShare;
    }

    [Tooltip("Parts this component shakes. Add or remove rows as the assembly needs.")]
    [SerializeField] private List<VibratingPart> parts = new();

    [Header("Speed")]
    [Tooltip("Shake speed in radians per second. 75 matches the separator's original shake.")]
    [SerializeField] private float angularSpeed = 75f;

    [Header("Intensity")]
    [Tooltip("Scales every amplitude above. 0 holds the parts still.")]
    [SerializeField, Min(0f)] private float intensity = 1f;

    [Tooltip("Optional. Intensity follows this sensor's reading instead of the value above.")]
    [SerializeField] private PerformanceStatSource intensitySource;

    [Tooltip("Intensity per unit of the sensor's reading.")]
    [SerializeField] private float intensityPerReadingUnit = 1f;

    [Tooltip("Seconds the intensity takes to catch up with a changed reading.")]
    [SerializeField, Min(0f)] private float intensitySmoothing = 0.4f;

    // Each axis runs at a slightly different rate, so the movement never reads
    // as one clean sine. These ratios match the separator's original shake.
    private static readonly Vector3 AxisRates = new(1f, 1.3f, 0.7f);
    private const float RotationRate = 0.8f;

    private readonly List<Vector3> restPositions = new();
    private readonly List<Quaternion> restRotations = new();
    private float currentIntensity;

    private void OnEnable()
    {
        CaptureRest();
        currentIntensity = TargetIntensity();
    }

    private void OnDisable()
    {
        // Parts go back to rest, so a disabled vibrator never leaves the
        // assembly frozen mid-shake.
        for (int i = 0; i < parts.Count && i < restPositions.Count; i++)
        {
            if (parts[i].target == null)
                continue;
            parts[i].target.localPosition = restPositions[i];
            parts[i].target.localRotation = restRotations[i];
        }
    }

    private void CaptureRest()
    {
        restPositions.Clear();
        restRotations.Clear();
        foreach (VibratingPart part in parts)
        {
            restPositions.Add(part.target != null ? part.target.localPosition : Vector3.zero);
            restRotations.Add(part.target != null ? part.target.localRotation : Quaternion.identity);
        }
    }

    private void Update()
    {
        if (restPositions.Count != parts.Count)
            CaptureRest();

        float target = TargetIntensity();
        currentIntensity = intensitySmoothing <= 0f
            ? target
            : Mathf.Lerp(currentIntensity, target, 1f - Mathf.Exp(-Time.deltaTime / intensitySmoothing));

        float time = Time.time * angularSpeed;

        for (int i = 0; i < parts.Count; i++)
        {
            VibratingPart part = parts[i];
            if (part.target == null)
                continue;

            float t = time * part.ResolvedShare + part.phase * Mathf.PI * 2f;
            Vector3 offset = new(
                Mathf.Sin(t * AxisRates.x) * part.amplitude.x,
                Mathf.Sin(t * AxisRates.y) * part.amplitude.y,
                Mathf.Sin(t * AxisRates.z) * part.amplitude.z);
            Vector3 rock = Mathf.Sin(t * RotationRate) * part.rotationAmplitude;

            part.target.localPosition = restPositions[i] + offset * currentIntensity;
            part.target.localRotation = restRotations[i] * Quaternion.Euler(rock * currentIntensity);
        }
    }

    private float TargetIntensity() =>
        intensitySource != null
            ? intensitySource.CurrentValue * intensityPerReadingUnit
            : intensity;
}
