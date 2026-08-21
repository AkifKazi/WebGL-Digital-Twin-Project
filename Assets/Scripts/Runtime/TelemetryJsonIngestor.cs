using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TelemetryRegistry))]
public sealed class TelemetryJsonIngestor : MonoBehaviour, ITelemetryReadingSink
{
    [SerializeField] private TelemetryRegistry registry;
    [SerializeField] private TelemetryOperatingModeController operatingModeController;
    [SerializeField] private ConnectionHealthMonitor connectionHealthMonitor;
    [SerializeField] private string providerId = "edge-gateway";
    [SerializeField] private bool logRejectedReadings;

    private void Awake()
    {
        if (registry == null)
            registry = GetComponent<TelemetryRegistry>();

        if (operatingModeController == null)
            operatingModeController = GetComponent<TelemetryOperatingModeController>();

        if (connectionHealthMonitor == null)
            connectionHealthMonitor = GetComponent<ConnectionHealthMonitor>();
    }

    // WebGL integrations can call this with:
    // unityInstance.SendMessage("Digital Twin Runtime", "PushJson", json)
    public void PushJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        if (!TelemetryJsonContract.TryParseReading(json, out TelemetryReadingDto dto, out string error))
        {
            Debug.LogWarning($"Rejected telemetry JSON: {error}", this);
            return;
        }

        Apply(dto);
    }

    public void PushBatchJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        if (!TelemetryJsonContract.TryParseBatch(json, out TelemetryBatchDto batch, out string error))
        {
            Debug.LogWarning($"Rejected telemetry batch JSON: {error}", this);
            return;
        }

        foreach (TelemetryReadingDto reading in batch.readings)
            Apply(reading);
    }

    private void Apply(TelemetryReadingDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.sensorId))
            return;

        long receiveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long sourceTimestamp = dto.sourceTimestampUnixMs > 0
            ? dto.sourceTimestampUnixMs
            : receiveTimestamp;

        TelemetryDataQuality quality = Enum.IsDefined(typeof(TelemetryDataQuality), dto.quality)
            ? (TelemetryDataQuality)dto.quality
            : TelemetryDataQuality.Bad;

        SubmitReading(new TelemetryReading(
            dto.sensorId,
            dto.value,
            quality,
            sourceTimestamp,
            receiveTimestamp,
            dto.statusCode,
            dto.sequenceNumber,
            string.IsNullOrWhiteSpace(dto.providerId) ? providerId : dto.providerId));
    }

    public TelemetryIngestionResult SubmitReading(in TelemetryReading reading)
    {
        if (registry == null || !registry.TryGetSource(reading.SensorId, out _))
        {
            if (logRejectedReadings)
                Debug.LogWarning($"Unknown telemetry sensor ID '{reading.SensorId}'.", this);

            return TelemetryIngestionResult.UnknownSensor;
        }

        if (!registry.ApplyReading(reading))
        {
            if (logRejectedReadings)
            {
                Debug.LogWarning(
                    $"Rejected invalid, duplicate, or out-of-order reading for '{reading.SensorId}'.",
                    this);
            }

            return double.IsNaN(reading.Value) || double.IsInfinity(reading.Value)
                ? TelemetryIngestionResult.InvalidValue
                : TelemetryIngestionResult.DuplicateOrOutOfOrder;
        }

        if (operatingModeController != null)
            operatingModeController.UseLiveData();

        connectionHealthMonitor?.ReportReading(reading.ProviderId);
        return TelemetryIngestionResult.Accepted;
    }
}
