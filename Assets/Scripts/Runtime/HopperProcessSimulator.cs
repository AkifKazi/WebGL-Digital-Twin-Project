using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HopperProcessSimulator : MonoBehaviour, ITelemetrySimulationProvider
{
    [Header("Connections")]
    [SerializeField] private TelemetryRegistry registry;
    [SerializeField] private ParticleFlowController particleFlowController;
    [SerializeField] private bool driveParticleControlsFromProcess;

    [Header("Equipment Nameplate (editable)")]
    [SerializeField, Min(1f)] private float hopperVolumeCubicMetres = 80f;
    [SerializeField, Min(1f)] private float bulkDensityKgPerCubicMetre = 1600f;
    [SerializeField, Min(1f)] private float motorRatedPowerKw = 22f;
    [SerializeField, Min(1f)] private float lineVoltage = 400f;
    [SerializeField, Range(0.1f, 1f)] private float powerFactor = 0.82f;
    [SerializeField, Range(0.1f, 1f)] private float motorEfficiency = 0.90f;
    [SerializeField, Min(1f)] private float nominalSpeedRpm = 1480f;

    [Header("Nominal Process Point")]
    [SerializeField, Min(0f)] private float nominalInletKgPerSecond = 12.5f;
    [SerializeField, Min(0f)] private float nominalDischargeKgPerSecond = 12.3f;
    [SerializeField, Range(0f, 100f)] private float initialFillPercent = 68f;
    [SerializeField] private float ambientTemperatureC = 25f;

    [Header("Simulation")]
    [SerializeField, Min(0.1f)] private float updateInterval = 0.5f;
    [SerializeField, Min(0.01f)] private float processTimeScale = 1f;
    [SerializeField, Min(0f)] private float noiseAmount = 0.015f;
    [SerializeField, Min(0.05f)] private float visibleVariationFrequency = 0.65f;
    [SerializeField] private int deterministicSeed = 1731;

    private PerformanceStatSource inletFlow;
    private PerformanceStatSource dischargeFlow;
    private PerformanceStatSource motorTemperature;
    private PerformanceStatSource motorSpeed;
    private PerformanceStatSource motorVibration;
    private PerformanceStatSource motorPower;
    private PerformanceStatSource motorCurrent;
    private PerformanceStatSource hopperLevel;
    private PerformanceStatSource bearingTemperature;
    private PerformanceStatSource driveFrequency;
    private PerformanceStatSource motorLoad;
    private PerformanceStatSource beltSpeed;
    private PerformanceStatSource hopperMass;
    private PerformanceStatSource materialMoisture;
    private PerformanceStatSource materialTemperature;
    private PerformanceStatSource loadCellImbalance;
    private PerformanceStatSource gatePosition;
    private PerformanceStatSource beltTension;
    private PerformanceStatSource feederBearingTemperature;
    private PerformanceStatSource feederDriveCurrent;
    private PerformanceStatSource beltSlip;
    private PerformanceStatSource frameVibration;
    private PerformanceStatSource ambientTemperature;
    private PerformanceStatSource ambientHumidity;
    private PerformanceStatSource supplyVoltage;
    private PerformanceStatSource supplyPowerFactor;

    private float accumulatedTime;
    private float fillPercent;
    private float speedRpm;
    private float temperatureC;
    private float powerKw;
    private float vibrationMmPerSecond;
    private ulong sequenceNumber;

    private void Awake()
    {
        if (registry == null)
            registry = GetComponent<TelemetryRegistry>();

        CacheSources();
        ResetModel();
    }

    private void Update()
    {
        // Keep simulation publishing independent of Time.timeScale and stable on
        // slower desktop/WebGL machines.
        accumulatedTime += Time.unscaledDeltaTime;

        if (accumulatedTime < updateInterval)
            return;

        float deltaTime = accumulatedTime * processTimeScale;
        accumulatedTime = 0f;
        StepModel(deltaTime);
    }

    [ContextMenu("Reset Process Model")]
    public void ResetModel()
    {
        fillPercent = initialFillPercent;
        speedRpm = nominalSpeedRpm;
        temperatureC = 65f;
        powerKw = motorRatedPowerKw * 0.84f;
        vibrationMmPerSecond = 4.2f;
        accumulatedTime = 0f;
        Publish(nominalInletKgPerSecond, nominalDischargeKgPerSecond);
    }

    public void ResetSimulation()
    {
        ResetModel();
    }

    private void StepModel(float deltaTime)
    {
        float time = Time.unscaledTime + deterministicSeed;
        float slowNoise = SignedPerlin(time * 0.07f, deterministicSeed * 0.013f);
        float fastNoise = SignedPerlin(time * 0.31f, deterministicSeed * 0.029f);
        float visibleNoise = SignedPerlin(
            time * visibleVariationFrequency,
            deterministicSeed * 0.047f);

        float inletKgPerSecond = nominalInletKgPerSecond *
            (1f + (slowNoise + visibleNoise * 0.8f) * noiseAmount);

        float fillFactor = Mathf.Sqrt(Mathf.Max(0.05f, fillPercent / initialFillPercent));
        float targetSpeed = nominalSpeedRpm *
            (1f + (fastNoise * 0.35f + visibleNoise * 0.2f) * noiseAmount);
        speedRpm = FirstOrder(speedRpm, targetSpeed, 1.5f, deltaTime);

        float speedFraction = speedRpm / nominalSpeedRpm;
        float dischargeKgPerSecond = nominalDischargeKgPerSecond * speedFraction * fillFactor;
        dischargeKgPerSecond *= 1f + fastNoise * noiseAmount * 0.5f;

        float capacityKg = hopperVolumeCubicMetres * bulkDensityKgPerCubicMetre;
        float massChangeKg = (inletKgPerSecond - dischargeKgPerSecond) * deltaTime;
        fillPercent = Mathf.Clamp(fillPercent + massChangeKg / capacityKg * 100f, 0f, 100f);

        float loadFraction = Mathf.Clamp01(0.45f + 0.40f *
            (dischargeKgPerSecond / Mathf.Max(0.01f, nominalDischargeKgPerSecond)));

        // Nameplate kW is shaft output. Active electrical input includes losses.
        float targetPowerKw = motorRatedPowerKw * loadFraction / motorEfficiency;
        powerKw = FirstOrder(powerKw, targetPowerKw, 2f, deltaTime);

        float targetTemperatureC = ambientTemperatureC + 48f * loadFraction;
        temperatureC = FirstOrder(temperatureC, targetTemperatureC, 120f, deltaTime);

        float targetVibration = 2.8f + 1.6f * loadFraction +
            fastNoise * 0.15f + visibleNoise * 0.18f;
        vibrationMmPerSecond = FirstOrder(vibrationMmPerSecond, targetVibration, 1f, deltaTime);

        Publish(inletKgPerSecond, dischargeKgPerSecond);
    }

    private void Publish(float inletKgPerSecond, float dischargeKgPerSecond)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        sequenceNumber++;
        float speedFraction = speedRpm / nominalSpeedRpm;

        float lineCurrentA = powerKw * 1000f /
            (Mathf.Sqrt(3f) * lineVoltage * powerFactor);
        float loadPercent = Mathf.Clamp(powerKw * motorEfficiency / motorRatedPowerKw * 100f, 0f, 120f);
        float frequencyHz = speedFraction * 50f;
        float bearingTemperatureC = ambientTemperatureC +
            (temperatureC - ambientTemperatureC) * 0.80f;
        float beltSpeedMetresPerSecond = 0.80f * speedFraction;
        float hopperMassTonnes = hopperVolumeCubicMetres * bulkDensityKgPerCubicMetre *
            (fillPercent / 100f) / 1000f;
        float time = Time.unscaledTime + deterministicSeed;
        float environmentalDrift = SignedPerlin(time * 0.015f, 4.17f);
        float mechanicalNoise = SignedPerlin(time * 0.19f, 8.31f);
        float simulatedAmbientC = ambientTemperatureC + environmentalDrift * 1.2f;
        float relativeHumidity = Mathf.Clamp(48f - environmentalDrift * 4f, 20f, 90f);
        float moisturePercent = Mathf.Clamp(6.8f + environmentalDrift * 0.35f, 1f, 18f);
        float materialTemperatureC = simulatedAmbientC + 6.4f + environmentalDrift * 0.5f;
        float gatePositionPercent = Mathf.Clamp(
            74f * dischargeKgPerSecond / Mathf.Max(0.01f, nominalDischargeKgPerSecond), 0f, 100f);
        float loadCellImbalancePercent = Mathf.Max(0f, 2.1f + mechanicalNoise * 0.35f);
        float beltTensionKn = 18.2f + (speedFraction - 1f) * 2.5f + mechanicalNoise * 0.2f;
        float feederBearingC = simulatedAmbientC + 24f * loadPercent / 100f;
        float feederCurrentA = 14.8f * Mathf.Clamp(loadPercent / 84f, 0.25f, 1.35f);
        float slipPercent = Mathf.Max(0f, 1.2f + Mathf.Abs(mechanicalNoise) * 0.45f +
            Mathf.Max(0f, loadPercent - 90f) * 0.08f);
        float structureVibration = Mathf.Max(0f, 1.1f + vibrationMmPerSecond * 0.16f +
            mechanicalNoise * 0.08f);
        float simulatedVoltage = 400f + environmentalDrift * 2.5f;
        float simulatedPowerFactor = Mathf.Clamp(
            powerFactor - Mathf.Max(0f, 0.65f - loadPercent / 100f) * 0.12f, 0.6f, 0.95f);

        Apply(inletFlow, inletKgPerSecond, now);
        Apply(dischargeFlow, dischargeKgPerSecond, now);
        Apply(motorTemperature, temperatureC, now);
        Apply(motorSpeed, speedRpm, now);
        Apply(motorVibration, vibrationMmPerSecond, now);
        Apply(motorPower, powerKw, now);
        Apply(motorCurrent, lineCurrentA, now);
        Apply(hopperLevel, fillPercent, now);
        Apply(bearingTemperature, bearingTemperatureC, now);
        Apply(driveFrequency, frequencyHz, now);
        Apply(motorLoad, loadPercent, now);
        Apply(beltSpeed, beltSpeedMetresPerSecond, now);
        Apply(hopperMass, hopperMassTonnes, now);
        Apply(materialMoisture, moisturePercent, now);
        Apply(materialTemperature, materialTemperatureC, now);
        Apply(loadCellImbalance, loadCellImbalancePercent, now);
        Apply(gatePosition, gatePositionPercent, now);
        Apply(beltTension, beltTensionKn, now);
        Apply(feederBearingTemperature, feederBearingC, now);
        Apply(feederDriveCurrent, feederCurrentA, now);
        Apply(beltSlip, slipPercent, now);
        Apply(frameVibration, structureVibration, now);
        Apply(ambientTemperature, simulatedAmbientC, now);
        Apply(ambientHumidity, relativeHumidity, now);
        Apply(supplyVoltage, simulatedVoltage, now);
        Apply(supplyPowerFactor, simulatedPowerFactor, now);

        if (driveParticleControlsFromProcess && particleFlowController != null)
        {
            particleFlowController.SetFlowSpeed(speedRpm / nominalSpeedRpm);
            particleFlowController.SetQuantity(
                dischargeKgPerSecond / Mathf.Max(0.01f, nominalDischargeKgPerSecond));
        }
    }

    private void Apply(PerformanceStatSource source, float value, long timestamp)
    {
        if (source == null)
            return;

        source.ApplyReading(new TelemetryReading(
            source.StatId,
            value,
            TelemetryDataQuality.Good,
            timestamp,
            timestamp,
            0,
            sequenceNumber,
            "sim.process-model"));
    }

    private void CacheSources()
    {
        TryGet("FEED-01.FLOW.IN", out inletFlow);
        TryGet("FEED-01.FLOW.OUT", out dischargeFlow);
        TryGet("VIB-01.MOTOR.TEMP", out motorTemperature);
        TryGet("VIB-01.MOTOR.SPEED", out motorSpeed);
        TryGet("VIB-01.BEARING.VEL_RMS", out motorVibration);
        TryGet("VIB-01.MOTOR.POWER", out motorPower);
        TryGet("VIB-01.MOTOR.CURRENT", out motorCurrent);
        TryGet("HOP-01.LEVEL", out hopperLevel);
        TryGet("VIB-01.BEARING.TEMP", out bearingTemperature);
        TryGet("VIB-01.DRIVE.FREQUENCY", out driveFrequency);
        TryGet("VIB-01.MOTOR.LOAD", out motorLoad);
        TryGet("FEED-01.BELT.SPEED", out beltSpeed);
        TryGet("HOP-01.MASS", out hopperMass);
        TryGet("HOP-01.MATERIAL.MOISTURE", out materialMoisture);
        TryGet("HOP-01.MATERIAL.TEMP", out materialTemperature);
        TryGet("HOP-01.LOADCELL.IMBALANCE", out loadCellImbalance);
        TryGet("HOP-01.OUTLET.GATE.POSITION", out gatePosition);
        TryGet("FEED-01.BELT.TENSION", out beltTension);
        TryGet("FEED-01.BEARING.TEMP", out feederBearingTemperature);
        TryGet("FEED-01.DRIVE.CURRENT", out feederDriveCurrent);
        TryGet("FEED-01.SLIP", out beltSlip);
        TryGet("STRUCT-01.FRAME.VIBRATION", out frameVibration);
        TryGet("ENV-01.AMBIENT.TEMP", out ambientTemperature);
        TryGet("ENV-01.RELATIVE.HUMIDITY", out ambientHumidity);
        TryGet("POWER-01.SUPPLY.VOLTAGE", out supplyVoltage);
        TryGet("POWER-01.POWER.FACTOR", out supplyPowerFactor);
    }

    private void TryGet(string id, out PerformanceStatSource source)
    {
        source = null;

        if (registry != null)
            registry.TryGetSource(id, out source);
    }

    private static float FirstOrder(float current, float target, float timeConstant, float deltaTime)
    {
        float blend = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.001f, timeConstant));
        return Mathf.Lerp(current, target, blend);
    }

    private static float SignedPerlin(float x, float y)
    {
        return Mathf.PerlinNoise(x, y) * 2f - 1f;
    }
}
