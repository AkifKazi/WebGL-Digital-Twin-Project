using System;

public readonly struct TelemetryReading
{
    public readonly string SensorId;
    public readonly double Value;
    public readonly TelemetryDataQuality Quality;
    public readonly long SourceTimestampUnixMs;
    public readonly long ReceiveTimestampUnixMs;
    public readonly uint StatusCode;
    public readonly ulong SequenceNumber;
    public readonly string ProviderId;

    public TelemetryReading(
        string sensorId,
        double value,
        TelemetryDataQuality quality,
        long sourceTimestampUnixMs,
        long receiveTimestampUnixMs,
        uint statusCode = 0,
        ulong sequenceNumber = 0,
        string providerId = null)
    {
        SensorId = sensorId;
        Value = value;
        Quality = quality;
        SourceTimestampUnixMs = sourceTimestampUnixMs;
        ReceiveTimestampUnixMs = receiveTimestampUnixMs;
        StatusCode = statusCode;
        SequenceNumber = sequenceNumber;
        ProviderId = providerId ?? string.Empty;
    }

    public static TelemetryReading Good(
        string sensorId,
        double value,
        long sourceTimestampUnixMs,
        string providerId,
        ulong sequenceNumber = 0)
    {
        return new TelemetryReading(
            sensorId,
            value,
            TelemetryDataQuality.Good,
            sourceTimestampUnixMs,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            0,
            sequenceNumber,
            providerId);
    }
}
