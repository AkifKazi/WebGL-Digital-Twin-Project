using System;
using UnityEngine;

[Serializable]
public sealed class TelemetryReadingDto
{
    public int schemaVersion = TelemetryJsonContract.CurrentSchemaVersion;
    public string sensorId;
    public double value;
    public int quality;
    public long sourceTimestampUnixMs;
    public uint statusCode;
    public ulong sequenceNumber;
    public string providerId;
}

[Serializable]
public sealed class TelemetryBatchDto
{
    public int schemaVersion = TelemetryJsonContract.CurrentSchemaVersion;
    public TelemetryReadingDto[] readings;
}

public static class TelemetryJsonContract
{
    public const int CurrentSchemaVersion = 1;

    public static bool TryParseReading(
        string json,
        out TelemetryReadingDto reading,
        out string error)
    {
        reading = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Payload is empty.";
            return false;
        }

        try
        {
            reading = JsonUtility.FromJson<TelemetryReadingDto>(json);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }

        if (reading == null || string.IsNullOrWhiteSpace(reading.sensorId))
        {
            error = "sensorId is required.";
            return false;
        }

        if (reading.schemaVersion != 0 &&
            reading.schemaVersion != CurrentSchemaVersion)
        {
            error = $"Unsupported schemaVersion {reading.schemaVersion}.";
            return false;
        }

        return true;
    }

    public static bool TryParseBatch(
        string json,
        out TelemetryBatchDto batch,
        out string error)
    {
        batch = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Payload is empty.";
            return false;
        }

        try
        {
            batch = JsonUtility.FromJson<TelemetryBatchDto>(json);
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }

        if (batch == null || batch.readings == null)
        {
            error = "readings is required.";
            return false;
        }

        // Version 0 represents legacy payloads created before an explicit
        // schemaVersion field was introduced.
        if (batch.schemaVersion != 0 &&
            batch.schemaVersion != CurrentSchemaVersion)
        {
            error = $"Unsupported schemaVersion {batch.schemaVersion}.";
            return false;
        }

        return true;
    }
}
