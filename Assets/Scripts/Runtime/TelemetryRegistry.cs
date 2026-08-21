using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TelemetryRegistry : MonoBehaviour
{
    [Header("Source Selection")]
    [Tooltip("Scene Discovery is recommended: sensors packaged with a new machine " +
             "are registered automatically. Explicit List is available for controlled scenes.")]
    [SerializeField] private TelemetrySourceSelectionMode sourceSelectionMode =
        TelemetrySourceSelectionMode.SceneDiscovery;
    [SerializeField] private PerformanceStatSource[] sources = Array.Empty<PerformanceStatSource>();

    private readonly Dictionary<string, PerformanceStatSource> sourcesById =
        new(StringComparer.OrdinalIgnoreCase);

    [SerializeField, Min(0.25f)] private float staleCheckInterval = 1f;
    private float nextStaleCheckTime;

    public IReadOnlyList<PerformanceStatSource> Sources => sources;
    public TelemetrySourceSelectionMode SourceSelectionMode => sourceSelectionMode;

    private void Awake()
    {
        RebuildIndex();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextStaleCheckTime)
            return;

        nextStaleCheckTime = Time.unscaledTime + staleCheckInterval;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        foreach (PerformanceStatSource source in sources)
        {
            if (source != null && source.ShouldMarkStale(now))
                source.SetDataQuality(TelemetryDataQuality.Stale);
        }
    }

    public bool TryGetSource(string statId, out PerformanceStatSource source)
    {
        if (sourcesById.Count == 0)
            RebuildIndex();

        return sourcesById.TryGetValue(statId ?? string.Empty, out source);
    }

    public bool SetReading(
        string statId,
        float value,
        TelemetryDataQuality quality = TelemetryDataQuality.Good)
    {
        if (!TryGetSource(statId, out PerformanceStatSource source))
            return false;

        source.SetReading(value, quality);
        return true;
    }

    public bool ApplyReading(in TelemetryReading reading)
    {
        if (!TryGetSource(reading.SensorId, out PerformanceStatSource source))
            return false;

        return source.TryApplyReading(reading);
    }

    [ContextMenu("Rebuild Sensor Index")]
    public void RebuildIndex()
    {
        sources = TelemetrySourceResolver.Resolve(sourceSelectionMode, sources);
        sourcesById.Clear();

        foreach (PerformanceStatSource source in sources)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.StatId))
                continue;

            if (!sourcesById.TryAdd(source.StatId, source))
            {
                Debug.LogWarning(
                    $"Duplicate telemetry ID '{source.StatId}' on '{source.name}'.",
                    source);
            }
        }
    }
}
