using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class ConnectionHealthView : MonoBehaviour
{
    [SerializeField] private ConnectionHealthMonitor monitor;
    [SerializeField] private TMP_Text modeValue;
    [SerializeField] private TMP_Text gatewayValue;
    [SerializeField] private TMP_Text lastUpdateValue;
    [SerializeField] private TMP_Text qualityValue;
    [SerializeField] private Image gatewayDot;
    [SerializeField] private Image[] accentImages = Array.Empty<Image>();
    [SerializeField] private TMP_Text[] stateValueTexts = Array.Empty<TMP_Text>();

    [Header("Existing Telemetry Palette")]
    [SerializeField] private Color healthyColor = new(0.08f, 0.9f, 1f, 1f);
    [SerializeField] private Color warningColor = new(1f, 0.7f, 0.1f, 1f);
    [SerializeField] private Color criticalColor = new(1f, 0.22f, 0.2f, 1f);
    [SerializeField] private Color unavailableColor = new(0.45f, 0.5f, 0.55f, 1f);

    private void OnEnable()
    {
        if (monitor != null)
        {
            monitor.Changed -= Render;
            monitor.Changed += Render;
            Render(monitor.Current);
        }
    }

    private void OnDisable()
    {
        if (monitor != null)
            monitor.Changed -= Render;
    }

    public void Bind(ConnectionHealthMonitor target)
    {
        if (monitor != null)
            monitor.Changed -= Render;

        monitor = target;

        if (isActiveAndEnabled && monitor != null)
        {
            monitor.Changed += Render;
            Render(monitor.Current);
        }
    }

    private void Render(ConnectionHealthSnapshot snapshot)
    {
        if (modeValue != null)
            modeValue.text = snapshot.Mode == TelemetryOperatingMode.Live ? "LIVE DATA" : "SIMULATION";

        if (gatewayValue != null)
        {
            gatewayValue.text = snapshot.GatewayState switch
            {
                GatewayHealthState.LocalSimulation => "LOCAL MODEL",
                GatewayHealthState.WaitingForData => "WAITING",
                GatewayHealthState.Connected => "CONNECTED",
                GatewayHealthState.Degraded => "DEGRADED",
                _ => "OFFLINE"
            };
        }

        if (lastUpdateValue != null)
            lastUpdateValue.text = FormatAge(snapshot.LastUpdateUnixMs);

        int issues = snapshot.UncertainCount + snapshot.BadCount + snapshot.StaleCount;
        if (qualityValue != null)
        {
            qualityValue.text = snapshot.TotalCount == 0
                ? "NO SENSORS"
                : issues == 0
                    ? $"{snapshot.GoodCount} / {snapshot.TotalCount} GOOD"
                    : $"{snapshot.GoodCount} GOOD · {issues} ISSUE";
        }

        Color stateColor = ResolveColor(snapshot);
        if (gatewayDot != null)
            gatewayDot.color = stateColor;
        foreach (Image accent in accentImages)
        {
            if (accent != null)
                accent.color = stateColor;
        }
        foreach (TMP_Text value in stateValueTexts)
        {
            if (value != null)
                value.color = stateColor;
        }
    }

    private Color ResolveColor(ConnectionHealthSnapshot snapshot)
    {
        if (snapshot.GatewayState == GatewayHealthState.Offline)
            return criticalColor;
        if (snapshot.GatewayState == GatewayHealthState.WaitingForData)
            return unavailableColor;
        if (snapshot.GatewayState == GatewayHealthState.Degraded ||
            snapshot.UncertainCount > 0 || snapshot.BadCount > 0 || snapshot.StaleCount > 0)
            return warningColor;
        return healthyColor;
    }

    private static string FormatAge(long timestampUnixMs)
    {
        if (timestampUnixMs <= 0)
            return "NO DATA";

        long ageSeconds = Math.Max(
            0,
            (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestampUnixMs) / 1000);

        if (ageSeconds < 2)
            return "JUST NOW";
        if (ageSeconds < 60)
            return $"{ageSeconds}s AGO";
        if (ageSeconds < 3600)
            return $"{ageSeconds / 60}m AGO";
        return $"{ageSeconds / 3600}h AGO";
    }
}
