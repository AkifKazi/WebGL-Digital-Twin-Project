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

    [Header("Operating Variation")]
    [Tooltip("How far production demand dips below the nominal feed, as a share of it. The " +
             "plant runs at or under its rated point, so the swing never raises a load alarm " +
             "on its own. 0 holds the process at its nominal point, which draws flat charts.")]
    [SerializeField, Range(0f, 0.3f)] private float demandVariation = 0.12f;

    [Tooltip("Seconds the discharge gate takes to follow a change in demand. The lag moves " +
             "the hopper level while the gate catches up.")]
    [SerializeField, Min(1f)] private float gateResponseSeconds = 90f;

    [Tooltip("Seconds for the motor temperature to settle after a change in load.")]
    [SerializeField, Min(1f)] private float motorThermalSeconds = 90f;

    [Tooltip("Minutes between screen cleaning cycles. Screen blinding builds up in between.")]
    [SerializeField, Min(1f)] private float screenCleaningMinutes = 20f;

    [SerializeField, Min(1f)] private float nominalRotorSpeedRpm = 980f;
    [SerializeField, Min(1f)] private float nominalRotorTorqueNm = 142f;

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
    private PerformanceStatSource rotorSpeed;
    private PerformanceStatSource rotorTorque;
    private PerformanceStatSource rotorImbalance;
    private PerformanceStatSource screenBlinding;

    private float accumulatedTime;
    private float fillPercent;
    private float speedRpm;
    private float temperatureC;
    private float powerKw;
    private float vibrationMmPerSecond;
    private float demand = 1f;
    private float gateDemand = 1f;
    private ulong sequenceNumber;

    private void Awake()
    {
        if (registry == null)
            registry = GetComponent<TelemetryRegistry>();

        CacheSources();
        ResetModel();
    }

    private const float MaximumStepSeconds = 2f;

    private void Update()
    {
        // Keep simulation publishing independent of Time.timeScale and stable on
        // slower desktop/WebGL machines.
        accumulatedTime += Time.unscaledDeltaTime;

        if (accumulatedTime < updateInterval)
            return;

        // A hidden browser tab or a paused editor delivers its whole absence as
        // one frame. Stepping the model through minutes at once would snap the
        // hopper empty or full and raise alarms the moment the viewer returns,
        // so the absence is skipped rather than simulated.
        float maximumStep = Mathf.Max(MaximumStepSeconds, updateInterval * 2f);
        float deltaTime = Mathf.Min(accumulatedTime, maximumStep) * processTimeScale;
        accumulatedTime = 0f;
        StepModel(deltaTime, Time.unscaledTime);
    }

    [ContextMenu("Reset Process Model")]
    public void ResetModel()
    {
        fillPercent = initialFillPercent;
        speedRpm = nominalSpeedRpm;
        temperatureC = 65f;
        powerKw = motorRatedPowerKw * 0.84f;
        vibrationMmPerSecond = 4.2f;
        demand = Demand(Time.unscaledTime + deterministicSeed);
        gateDemand = demand;
        accumulatedTime = 0f;
        Publish(nominalInletKgPerSecond, nominalDischargeKgPerSecond, Time.unscaledTime + deterministicSeed);
    }

    public void ResetSimulation()
    {
        ResetModel();
    }

    /// <summary>
    /// Advances the model by <paramref name="deltaTime"/> seconds at
    /// <paramref name="clockSeconds"/> on the unscaled clock. Taking the clock
    /// as an argument lets a test run hours of process in a moment.
    /// </summary>
    internal void StepModel(float deltaTime, float clockSeconds)
    {
        float time = clockSeconds + deterministicSeed;
        float slowNoise = SignedPerlin(time * 0.07f, deterministicSeed * 0.013f);
        float fastNoise = SignedPerlin(time * 0.31f, deterministicSeed * 0.029f);
        float visibleNoise = SignedPerlin(
            time * visibleVariationFrequency,
            deterministicSeed * 0.047f);

        // Production demand moves over minutes to an hour, as feed and trucks
        // come and go. Without it every signal settles on one value and the
        // history charts draw flat lines.
        demand = Demand(time);
        gateDemand = FirstOrder(gateDemand, demand, gateResponseSeconds, deltaTime);

        float inletKgPerSecond = nominalInletKgPerSecond * demand *
            (1f + (slowNoise + visibleNoise * 0.8f) * noiseAmount);

        float fillFactor = Mathf.Sqrt(Mathf.Max(0.05f, fillPercent / initialFillPercent));
        // The drive trims its speed a little with demand; drive frequency
        // limits sit only 2% above nominal, so the trim stays small.
        float targetSpeed = nominalSpeedRpm *
            (1f + (demand - 1f) * 0.12f + (fastNoise * 0.35f + visibleNoise * 0.2f) * noiseAmount);
        speedRpm = FirstOrder(speedRpm, targetSpeed, 1.5f, deltaTime);

        float speedFraction = speedRpm / nominalSpeedRpm;
        float dischargeKgPerSecond = nominalDischargeKgPerSecond * speedFraction * fillFactor * gateDemand;
        dischargeKgPerSecond *= 1f + fastNoise * noiseAmount * 0.5f;

        float capacityKg = hopperVolumeCubicMetres * bulkDensityKgPerCubicMetre;
        float massChangeKg = (inletKgPerSecond - dischargeKgPerSecond) * deltaTime;
        fillPercent = Mathf.Clamp(fillPercent + massChangeKg / capacityKg * 100f, 0f, 100f);

        float loadFraction = Mathf.Clamp01(0.45f + 0.40f *
            (dischargeKgPerSecond / Mathf.Max(0.01f, nominalDischargeKgPerSecond)));

        // Nameplate kW is shaft output. Active electrical input includes losses.
        float targetPowerKw = motorRatedPowerKw * loadFraction / motorEfficiency;
        powerKw = FirstOrder(powerKw, targetPowerKw, 2f, deltaTime);

        // Winding losses grow with the square of the current, so the rise
        // follows load more than linearly; the cube used here keeps a load
        // swing readable on the charts. 40 degC over ambient at the nominal
        // load point, close to the previous 40.8.
        const float nominalLoadFraction = 0.85f;
        float relativeLoad = loadFraction / nominalLoadFraction;
        float targetTemperatureC = ambientTemperatureC + 40f * relativeLoad * relativeLoad * relativeLoad;
        temperatureC = FirstOrder(temperatureC, targetTemperatureC, motorThermalSeconds, deltaTime);

        float targetVibration = 2.8f + 1.6f * loadFraction +
            fastNoise * 0.15f + visibleNoise * 0.18f;
        vibrationMmPerSecond = FirstOrder(vibrationMmPerSecond, targetVibration, 1f, deltaTime);

        Publish(inletKgPerSecond, dischargeKgPerSecond, time);
    }

    private void Publish(float inletKgPerSecond, float dischargeKgPerSecond, float time)
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
        // An induction motor's power factor improves with load and drops off
        // steeply below about 65 %.
        float simulatedPowerFactor = Mathf.Clamp(
            powerFactor + (loadPercent / 100f - 0.85f) * 0.3f + mechanicalNoise * 0.004f -
            Mathf.Max(0f, 0.65f - loadPercent / 100f) * 0.12f, 0.6f, 0.95f);

        // The separator's rotors take the discharged material.
        float throughput = dischargeKgPerSecond / Mathf.Max(0.01f, nominalDischargeKgPerSecond);
        float rotorSpeedRpm = nominalRotorSpeedRpm * (1f + (demand - 1f) * 0.25f + mechanicalNoise * 0.008f);
        float rotorTorqueNm = nominalRotorTorqueNm * Mathf.Clamp(throughput, 0.3f, 1.3f) *
            (1f + mechanicalNoise * 0.03f);
        float rotorImbalanceMmPerSecond = Mathf.Max(0f, 2.1f + mechanicalNoise * 0.25f +
            SignedPerlin(time * 0.004f, 12.7f) * 0.35f);
        // Blinding builds up between cleaning cycles, faster with wet feed.
        float cleaningCycle = Mathf.Repeat(time, screenCleaningMinutes * 60f) / (screenCleaningMinutes * 60f);
        float screenBlindingPercent = Mathf.Clamp(
            4.8f + 3.2f * cleaningCycle + (moisturePercent - 6.8f) * 2.5f + mechanicalNoise * 0.2f, 0f, 100f);

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
        Apply(rotorSpeed, rotorSpeedRpm, now);
        Apply(rotorTorque, rotorTorqueNm, now);
        Apply(rotorImbalance, rotorImbalanceMmPerSecond, now);
        Apply(screenBlinding, screenBlindingPercent, now);

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
        TryGet("MIX-01.ROTOR.SPEED", out rotorSpeed);
        TryGet("MIX-01.ROTOR.TORQUE", out rotorTorque);
        TryGet("MIX-01.ROTOR.IMBALANCE", out rotorImbalance);
        TryGet("MIX-01.SCREEN.BLINDING", out screenBlinding);
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

    /// <summary>
    /// Production demand as a share of nominal, between 1 - demandVariation
    /// and 1: swings of a few minutes, a quarter of an hour and most of an
    /// hour, layered.
    /// </summary>
    private float Demand(float time)
    {
        if (demandVariation <= 0f)
            return 1f;

        float swing =
            0.50f * SignedPerlin(time / 240f, deterministicSeed * 0.011f) +
            0.35f * SignedPerlin(time / 900f, deterministicSeed * 0.017f + 3.1f) +
            0.25f * SignedPerlin(time / 3000f, deterministicSeed * 0.023f + 7.3f);
        float share = Mathf.Clamp01(0.5f - swing);
        return 1f - demandVariation * share;
    }

    private static float SignedPerlin(float x, float y)
    {
        return Mathf.PerlinNoise(x, y) * 2f - 1f;
    }
}
