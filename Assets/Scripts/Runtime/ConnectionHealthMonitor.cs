using System;
using UnityEngine;

public enum GatewayHealthState
{
    LocalSimulation,
    WaitingForData,
    Connected,
    Degraded,
    Offline
}

public readonly struct ConnectionHealthSnapshot : IEquatable<ConnectionHealthSnapshot>
{
    public readonly TelemetryOperatingMode Mode;
    public readonly GatewayHealthState GatewayState;
    public readonly long LastUpdateUnixMs;
    public readonly int GoodCount;
    public readonly int UncertainCount;
    public readonly int BadCount;
    public readonly int StaleCount;
    public readonly int TotalCount;
    public readonly string ProviderId;

    public ConnectionHealthSnapshot(
        TelemetryOperatingMode mode,
        GatewayHealthState gatewayState,
        long lastUpdateUnixMs,
        int goodCount,
        int uncertainCount,
        int badCount,
        int staleCount,
        int totalCount,
        string providerId)
    {
        Mode = mode;
        GatewayState = gatewayState;
        LastUpdateUnixMs = lastUpdateUnixMs;
        GoodCount = goodCount;
        UncertainCount = uncertainCount;
        BadCount = badCount;
        StaleCount = staleCount;
        TotalCount = totalCount;
        ProviderId = providerId ?? string.Empty;
    }

    public bool Equals(ConnectionHealthSnapshot other) =>
        Mode == other.Mode &&
        GatewayState == other.GatewayState &&
        LastUpdateUnixMs / 1000 == other.LastUpdateUnixMs / 1000 &&
        GoodCount == other.GoodCount &&
        UncertainCount == other.UncertainCount &&
        BadCount == other.BadCount &&
        StaleCount == other.StaleCount &&
        TotalCount == other.TotalCount &&
        string.Equals(ProviderId, other.ProviderId, StringComparison.Ordinal);

    public override bool Equals(object obj) =>
        obj is ConnectionHealthSnapshot other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(Mode);
        hash.Add(GatewayState);
        hash.Add(LastUpdateUnixMs / 1000);
        hash.Add(GoodCount);
        hash.Add(UncertainCount);
        hash.Add(BadCount);
        hash.Add(StaleCount);
        hash.Add(TotalCount);
        hash.Add(ProviderId);
        return hash.ToHashCode();
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(TelemetryRegistry))]
[RequireComponent(typeof(TelemetryOperatingModeController))]
public sealed class ConnectionHealthMonitor : MonoBehaviour
{
    public event Action<ConnectionHealthSnapshot> Changed;

    [SerializeField] private TelemetryRegistry registry;
    [SerializeField] private TelemetryOperatingModeController modeController;

    [Header("Live Gateway Timeouts")]
    [SerializeField, Min(0.5f)] private float degradedAfterSeconds = 3f;
    [SerializeField, Min(1f)] private float offlineAfterSeconds = 10f;
    [SerializeField, Range(0.1f, 2f)] private float refreshInterval = 0.25f;

    private long lastLiveMessageUnixMs;
    private string liveProviderId = string.Empty;
    private bool explicitlyDisconnected;
    private float nextRefreshTime;
    private ConnectionHealthSnapshot current;
    private bool hasSnapshot;

    public ConnectionHealthSnapshot Current => current;

    private void Awake()
    {
        if (registry == null)
            registry = GetComponent<TelemetryRegistry>();
        if (modeController == null)
            modeController = GetComponent<TelemetryOperatingModeController>();
    }

    private void OnEnable()
    {
        if (modeController != null)
            modeController.OperatingModeChanged += HandleModeChanged;
        SubscribeToSources();
        Refresh(true);
    }

    private void OnDisable()
    {
        if (modeController != null)
            modeController.OperatingModeChanged -= HandleModeChanged;
        UnsubscribeFromSources();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshTime)
            return;

        nextRefreshTime = Time.unscaledTime + refreshInterval;
        Refresh(false);
    }

    public void ReportReading(string providerId)
    {
        lastLiveMessageUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        liveProviderId = string.IsNullOrWhiteSpace(providerId) ? "edge-gateway" : providerId;
        explicitlyDisconnected = false;
        Refresh(true);
    }

    // Browser integrations may call SendMessage on Digital Twin Runtime with this method.
    public void ReportGatewayConnected(string providerId)
    {
        lastLiveMessageUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        liveProviderId = string.IsNullOrWhiteSpace(providerId) ? "edge-gateway" : providerId;
        explicitlyDisconnected = false;
        Refresh(true);
    }

    public void ReportGatewayHeartbeat(string providerId)
    {
        ReportReading(providerId);
    }

    public void ReportGatewayDisconnected(string reason)
    {
        explicitlyDisconnected = true;
        Refresh(true);
    }

    private void HandleModeChanged(TelemetryOperatingMode mode)
    {
        if (mode == TelemetryOperatingMode.Simulation)
        {
            explicitlyDisconnected = false;
            lastLiveMessageUnixMs = 0;
            liveProviderId = string.Empty;
        }
        Refresh(true);
    }

    private void HandleSourceChanged(PerformanceStatSource source)
    {
        Refresh(false);
    }

    private void Refresh(bool force)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        TelemetryOperatingMode mode = modeController != null
            ? modeController.OperatingMode
            : TelemetryOperatingMode.Simulation;

        int good = 0;
        int uncertain = 0;
        int bad = 0;
        int stale = 0;
        int total = 0;
        long latestSimulationUpdate = 0;

        if (registry != null)
        {
            foreach (PerformanceStatSource source in registry.Sources)
            {
                if (source == null)
                    continue;

                total++;
                latestSimulationUpdate = Math.Max(latestSimulationUpdate, source.ReceiveTimestampUnixMs);
                switch (source.DataQuality)
                {
                    case TelemetryDataQuality.Good: good++; break;
                    case TelemetryDataQuality.Uncertain: uncertain++; break;
                    case TelemetryDataQuality.Bad: bad++; break;
                    case TelemetryDataQuality.Stale: stale++; break;
                }
            }
        }

        GatewayHealthState state;
        long lastUpdate;
        string provider;

        if (mode == TelemetryOperatingMode.Simulation)
        {
            state = GatewayHealthState.LocalSimulation;
            lastUpdate = latestSimulationUpdate;
            provider = "local-process-model";
        }
        else
        {
            lastUpdate = lastLiveMessageUnixMs;
            provider = liveProviderId;
            double ageSeconds = lastUpdate > 0
                ? Math.Max(0d, (now - lastUpdate) / 1000d)
                : double.PositiveInfinity;

            if (explicitlyDisconnected || ageSeconds >= offlineAfterSeconds)
                state = lastUpdate == 0 && !explicitlyDisconnected
                    ? GatewayHealthState.WaitingForData
                    : GatewayHealthState.Offline;
            else if (ageSeconds >= degradedAfterSeconds || bad > 0 || stale > 0 || uncertain > 0)
                state = GatewayHealthState.Degraded;
            else
                state = GatewayHealthState.Connected;
        }

        ConnectionHealthSnapshot next = new(
            mode,
            state,
            lastUpdate,
            good,
            uncertain,
            bad,
            stale,
            total,
            provider);

        if (!force && hasSnapshot && next.Equals(current))
            return;

        current = next;
        hasSnapshot = true;
        Changed?.Invoke(current);
    }

    private void SubscribeToSources()
    {
        if (registry == null)
            return;
        foreach (PerformanceStatSource source in registry.Sources)
        {
            if (source == null)
                continue;
            source.Changed -= HandleSourceChanged;
            source.Changed += HandleSourceChanged;
        }
    }

    private void UnsubscribeFromSources()
    {
        if (registry == null)
            return;
        foreach (PerformanceStatSource source in registry.Sources)
        {
            if (source != null)
                source.Changed -= HandleSourceChanged;
        }
    }
}
