public interface ITelemetryReadingSink
{
    TelemetryIngestionResult SubmitReading(in TelemetryReading reading);
}

public interface ITelemetrySimulationProvider
{
    void ResetSimulation();
}

public enum TelemetryIngestionResult
{
    Accepted,
    UnknownSensor,
    InvalidValue,
    DuplicateOrOutOfOrder
}
