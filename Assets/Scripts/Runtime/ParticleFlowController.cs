using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ParticleFlowController : MonoBehaviour
{
    [Header("Simple Authoring Controls")]
    [Tooltip("Scales the complete flow timeline, including particle lifetime and scripted movement.")]
    [SerializeField, Range(0f, 3f)] private float flowSpeed = 1f;

    [Tooltip("Scales emission quantity without changing rock size or trajectories.")]
    [SerializeField, Range(0f, 3f)] private float quantity = 1f;

    [Header("Targets")]
    [Tooltip("When enabled, all child particle systems and flow scripts are managed automatically.")]
    [SerializeField] private bool discoverInChildren = true;
    [SerializeField] private ParticleSystem[] particleSystems = Array.Empty<ParticleSystem>();
    [SerializeField] private ControlledRockFlow[] rockFlows = Array.Empty<ControlledRockFlow>();
    [SerializeField] private FilteredParticleFlow[] filteredFlows = Array.Empty<FilteredParticleFlow>();

    private readonly List<ParticleBaseline> baselines = new();

    public float FlowSpeed => flowSpeed;
    public float Quantity => quantity;

    private void Awake()
    {
        RefreshTargets();
        CaptureBaselines();
        Apply();
    }

    public void SetFlowSpeed(float value)
    {
        flowSpeed = Mathf.Clamp(value, 0f, 3f);
        Apply();
    }

    public void SetQuantity(float value)
    {
        quantity = Mathf.Clamp(value, 0f, 3f);
        Apply();
    }

    [ContextMenu("Refresh Particle Targets")]
    public void RefreshTargets()
    {
        if (!discoverInChildren)
            return;

        particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        rockFlows = GetComponentsInChildren<ControlledRockFlow>(true);
        filteredFlows = GetComponentsInChildren<FilteredParticleFlow>(true);
        baselines.Clear();
    }

    [ContextMenu("Reapply Flow Controls")]
    public void Apply()
    {
        if (baselines.Count != particleSystems.Length)
            CaptureBaselines();

        foreach (ControlledRockFlow flow in rockFlows)
        {
            if (flow != null)
                flow.SimulationSpeedMultiplier = flowSpeed;
        }

        foreach (FilteredParticleFlow flow in filteredFlows)
        {
            if (flow != null)
                flow.SimulationSpeedMultiplier = flowSpeed;
        }

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem system = particleSystems[i];

            if (system == null)
                continue;

            ParticleBaseline baseline = baselines[i];
            ParticleSystem.MainModule main = system.main;
            main.simulationSpeed = baseline.SimulationSpeed * flowSpeed;

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTimeMultiplier = baseline.RateOverTimeMultiplier * quantity;
            emission.rateOverDistanceMultiplier = baseline.RateOverDistanceMultiplier * quantity;
        }
    }

    private void CaptureBaselines()
    {
        baselines.Clear();

        foreach (ParticleSystem system in particleSystems)
        {
            if (system == null)
            {
                baselines.Add(default);
                continue;
            }

            ParticleSystem.MainModule main = system.main;
            ParticleSystem.EmissionModule emission = system.emission;
            baselines.Add(new ParticleBaseline(
                main.simulationSpeed,
                emission.rateOverTimeMultiplier,
                emission.rateOverDistanceMultiplier));
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        flowSpeed = Mathf.Clamp(flowSpeed, 0f, 3f);
        quantity = Mathf.Clamp(quantity, 0f, 3f);

        if (Application.isPlaying)
            Apply();
    }
#endif

    private readonly struct ParticleBaseline
    {
        public readonly float SimulationSpeed;
        public readonly float RateOverTimeMultiplier;
        public readonly float RateOverDistanceMultiplier;

        public ParticleBaseline(
            float simulationSpeed,
            float rateOverTimeMultiplier,
            float rateOverDistanceMultiplier)
        {
            SimulationSpeed = simulationSpeed;
            RateOverTimeMultiplier = rateOverTimeMultiplier;
            RateOverDistanceMultiplier = rateOverDistanceMultiplier;
        }
    }
}
