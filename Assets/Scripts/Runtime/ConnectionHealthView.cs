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

        UIText.Changed -= HandleLanguageChanged;
        UIText.Changed += HandleLanguageChanged;
    }

    private void OnDisable()
    {
        if (monitor != null)
            monitor.Changed -= Render;
        UIText.Changed -= HandleLanguageChanged;
    }

    private void HandleLanguageChanged()
    {
        if (monitor != null)
            Render(monitor.Current);
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
            modeValue.text = snapshot.Mode == TelemetryOperatingMode.Live
                ? UIText.Get("health.mode.live", "LIVE DATA")
                : UIText.Get("health.mode.simulation", "SIMULATION");

        if (gatewayValue != null)
        {
            gatewayValue.text = snapshot.GatewayState switch
            {
                GatewayHealthState.LocalSimulation => UIText.Get("health.gateway.local_model", "LOCAL MODEL"),
                GatewayHealthState.WaitingForData => UIText.Get("health.gateway.waiting", "WAITING"),
                GatewayHealthState.Connected => UIText.Get("health.gateway.connected", "CONNECTED"),
                GatewayHealthState.Degraded => UIText.Get("health.gateway.degraded", "DEGRADED"),
                _ => UIText.Get("health.gateway.offline", "OFFLINE")
            };
        }

        if (lastUpdateValue != null)
            lastUpdateValue.text = FormatAge(snapshot.LastUpdateUnixMs);

        int issues = snapshot.UncertainCount + snapshot.BadCount + snapshot.StaleCount;
        if (qualityValue != null)
        {
            qualityValue.text = snapshot.TotalCount == 0
                ? UIText.Get("health.quality.no_sensors", "NO SENSORS")
                : issues == 0
                    ? UIText.Format("health.quality.all_good", "{0} / {1} GOOD", snapshot.GoodCount, snapshot.TotalCount)
                    : UIText.Format("health.quality.issues", "{0} GOOD · {1} ISSUE", snapshot.GoodCount, issues);
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
            return UIText.Get("health.age.no_data", "NO DATA");

        long ageSeconds = Math.Max(
            0,
            (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestampUnixMs) / 1000);

        if (ageSeconds < 2)
            return UIText.Get("health.age.just_now", "JUST NOW");
        if (ageSeconds < 60)
            return UIText.Format("health.age.seconds", "{0}s AGO", ageSeconds);
        if (ageSeconds < 3600)
            return UIText.Format("health.age.minutes", "{0}m AGO", ageSeconds / 60);
        return UIText.Format("health.age.hours", "{0}h AGO", ageSeconds / 3600);
    }
}
