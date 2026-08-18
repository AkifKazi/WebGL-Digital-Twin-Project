using System;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TelemetryRegistry))]
public sealed class TelemetryJsonIngestor : MonoBehaviour
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

        TelemetryReadingDto dto;

        try
        {
            dto = JsonUtility.FromJson<TelemetryReadingDto>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Rejected telemetry JSON: {exception.Message}", this);
            return;
        }

        Apply(dto);
    }

    public void PushBatchJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        TelemetryBatchDto batch;

        try
        {
            batch = JsonUtility.FromJson<TelemetryBatchDto>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Rejected telemetry batch JSON: {exception.Message}", this);
            return;
        }

        if (batch?.readings == null)
            return;

        foreach (TelemetryReadingDto reading in batch.readings)
            Apply(reading);
    }

    private void Apply(TelemetryReadingDto dto)
    {
        if (dto == null || string.IsNullOrWhiteSpace(dto.sensorId) || registry == null)
            return;

        long receiveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long sourceTimestamp = dto.sourceTimestampUnixMs > 0
            ? dto.sourceTimestampUnixMs
            : receiveTimestamp;

        TelemetryDataQuality quality = Enum.IsDefined(typeof(TelemetryDataQuality), dto.quality)
            ? (TelemetryDataQuality)dto.quality
            : TelemetryDataQuality.Bad;

        TelemetryReading reading = new(
            dto.sensorId,
            dto.value,
            quality,
            sourceTimestamp,
            receiveTimestamp,
            dto.statusCode,
            dto.sequenceNumber,
            string.IsNullOrWhiteSpace(dto.providerId) ? providerId : dto.providerId);

        if (!registry.TryGetSource(dto.sensorId, out _))
        {
            if (logRejectedReadings)
                Debug.LogWarning($"Unknown telemetry sensor ID '{dto.sensorId}'.", this);

            return;
        }

        if (!registry.ApplyReading(reading))
        {
            if (logRejectedReadings)
            {
                Debug.LogWarning(
                    $"Rejected invalid, duplicate, or out-of-order reading for '{dto.sensorId}'.",
                    this);
            }
            return;
        }

        if (operatingModeController != null)
            operatingModeController.UseLiveData();

        connectionHealthMonitor?.ReportReading(reading.ProviderId);
    }

    [Serializable]
    private sealed class TelemetryBatchDto
    {
        public TelemetryReadingDto[] readings;
    }

    [Serializable]
    private sealed class TelemetryReadingDto
    {
        public string sensorId;
        public double value;
        public int quality;
        public long sourceTimestampUnixMs;
        public uint statusCode;
        public ulong sequenceNumber;
        public string providerId;
    }
}
