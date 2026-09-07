using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks one mechanism of the machine — the vibratory drive, the conveyor, the
/// hopper — and reports the worst alarm state of the sensors that belong to it.
///
/// The group does not invent a second health model. It reads the existing
/// <see cref="PerformanceStatSource"/> alarm evaluation, so a sensor crossing a
/// configured threshold colours its mechanism with no extra authoring.
/// </summary>
[DisallowMultipleComponent]
public sealed class MachinePartGroup : MonoBehaviour
{
    [Header("Identity")]
    [Tooltip("Name shown when this mechanism is selected. Defaults to the object name.")]
    [SerializeField] private string displayName = string.Empty;

    [Header("Sensor binding")]
    [Tooltip("Telemetry categories owned by this mechanism, matching the category names in the machine configuration.")]
    [SerializeField] private string[] sensorCategories = Array.Empty<string>();

    [Tooltip("Optional explicit sensors. When set, these are used instead of category matching.")]
    [SerializeField] private PerformanceStatSource[] explicitSensors = Array.Empty<PerformanceStatSource>();

    [Header("Renderers")]
    [Tooltip("Left empty, every renderer under this transform is used.")]
    [SerializeField] private Renderer[] targetRenderers = Array.Empty<Renderer>();

    private static readonly int StatusColorId = Shader.PropertyToID("_StatusColor");
    private static readonly int StatusBlendId = Shader.PropertyToID("_StatusBlend");
    private static readonly int SelectionBlendId = Shader.PropertyToID("_SelectionBlend");
    private static readonly int FocusDimId = Shader.PropertyToID("_FocusDim");

    private readonly List<PerformanceStatSource> boundSensors = new();

    private MaterialPropertyBlock propertyBlock;
    private Renderer[] resolvedRenderers = Array.Empty<Renderer>();
    private StatVisualState worstState = StatVisualState.Normal;

    private Color appliedColor = Color.clear;
    private float appliedStatusBlend = -1f;
    private float appliedSelectionBlend = -1f;
    private float appliedFocusDim = -1f;

    /// <summary>Raised when the worst alarm state of this mechanism changes.</summary>
    public event Action<MachinePartGroup> StateChanged;

    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    public StatVisualState WorstState => worstState;

    /// <summary>
    /// True while a bound sensor is in warning or critical. Alarmed mechanisms
    /// keep their colour and are never dimmed by hover isolation, matching how
    /// the telemetry cards hold their alarm state.
    /// </summary>
    public bool IsAlarmed =>
        worstState == StatVisualState.Warning || worstState == StatVisualState.Critical;

    /// <summary>Current hover-isolation dim, 0 lit through 1 ghosted.</summary>
    public float CurrentFocusDim => Mathf.Max(appliedFocusDim, 0f);

    public IReadOnlyList<PerformanceStatSource> BoundSensors => boundSensors;

    public Renderer[] Renderers
    {
        get
        {
            if (resolvedRenderers.Length == 0)
                ResolveRenderers();

            return resolvedRenderers;
        }
    }

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        ResolveRenderers();
    }

    private void OnEnable()
    {
        BindSensors();
        RecalculateWorstState();
    }

    private void OnDisable()
    {
        UnbindSensors();
    }

    private void ResolveRenderers()
    {
        if (targetRenderers != null && targetRenderers.Length > 0)
        {
            resolvedRenderers = targetRenderers;
            return;
        }

        // Hover overlay proxies live under the meshes they draw over, so they
        // are excluded from the mechanism's own renderer list.
        List<Renderer> found = new();

        foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
        {
            if (candidate != null && candidate.GetComponent<MachineOverlayProxy>() == null)
                found.Add(candidate);
        }

        resolvedRenderers = found.ToArray();
    }

    /// <summary>Re-reads the sensor binding, e.g. after scene setup regenerates sources.</summary>
    public void Rebind()
    {
        UnbindSensors();
        BindSensors();
        RecalculateWorstState();
    }

    private void BindSensors()
    {
        boundSensors.Clear();

        if (explicitSensors != null && explicitSensors.Length > 0)
        {
            foreach (PerformanceStatSource sensor in explicitSensors)
            {
                if (sensor != null)
                    boundSensors.Add(sensor);
            }
        }
        else if (sensorCategories != null && sensorCategories.Length > 0)
        {
            PerformanceStatSource[] all = FindObjectsByType<PerformanceStatSource>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            foreach (PerformanceStatSource sensor in all)
            {
                if (sensor != null && MatchesCategory(sensor))
                    boundSensors.Add(sensor);
            }
        }

        foreach (PerformanceStatSource sensor in boundSensors)
            sensor.Changed += HandleSensorChanged;
    }

    private void UnbindSensors()
    {
        foreach (PerformanceStatSource sensor in boundSensors)
        {
            if (sensor != null)
                sensor.Changed -= HandleSensorChanged;
        }

        boundSensors.Clear();
    }

    /// <summary>
    /// Scene setup parents each sensor under "Telemetry Sources/&lt;category&gt;",
    /// so the parent object name is the category.
    /// </summary>
    private bool MatchesCategory(PerformanceStatSource sensor)
    {
        Transform parent = sensor.transform.parent;

        if (parent == null)
            return false;

        foreach (string category in sensorCategories)
        {
            if (!string.IsNullOrWhiteSpace(category) &&
                string.Equals(parent.name, category.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void HandleSensorChanged(PerformanceStatSource sensor)
    {
        RecalculateWorstState();
    }

    private void RecalculateWorstState()
    {
        StatVisualState worst = StatVisualState.Normal;

        foreach (PerformanceStatSource sensor in boundSensors)
        {
            if (sensor == null || !sensor.IsVisible)
                continue;

            if (Severity(sensor.VisualState) > Severity(worst))
                worst = sensor.VisualState;
        }

        if (worst == worstState)
            return;

        worstState = worst;
        StateChanged?.Invoke(this);
    }

    // Critical outranks Warning, which outranks a missing reading. Unavailable
    // is ranked lowest so a stale sensor never masks a real alarm.
    private static int Severity(StatVisualState state) => state switch
    {
        StatVisualState.Critical => 3,
        StatVisualState.Warning => 2,
        StatVisualState.Unavailable => 1,
        _ => 0
    };

    /// <summary>Pushes the alarm tint onto this mechanism's renderers.</summary>
    public void ApplyStatusVisual(Color color, float blend)
    {
        if (color == appliedColor && Mathf.Approximately(blend, appliedStatusBlend))
            return;

        appliedColor = color;
        appliedStatusBlend = blend;

        WriteBlock(block =>
        {
            block.SetColor(StatusColorId, color);
            block.SetFloat(StatusBlendId, blend);
        });
    }

    /// <summary>Pushes the selection highlight onto this mechanism's renderers.</summary>
    public void ApplySelectionVisual(float blend)
    {
        if (Mathf.Approximately(blend, appliedSelectionBlend))
            return;

        appliedSelectionBlend = blend;

        WriteBlock(block => block.SetFloat(SelectionBlendId, blend));
    }

    /// <summary>
    /// Fades this mechanism back to an outline while another one is inspected.
    /// 0 is fully lit, 1 is the edges-only ghost.
    /// </summary>
    public void ApplyFocusDim(float dim)
    {
        if (Mathf.Approximately(dim, appliedFocusDim))
            return;

        appliedFocusDim = dim;

        WriteBlock(block => block.SetFloat(FocusDimId, dim));
    }

    public void ClearVisuals()
    {
        ApplyStatusVisual(Color.clear, 0f);
        ApplySelectionVisual(0f);
        ApplyFocusDim(0f);
    }

    /// <summary>True when this mechanism owns the given sensor.</summary>
    public bool Owns(PerformanceStatSource sensor)
    {
        if (sensor == null)
            return false;

        for (int i = 0; i < boundSensors.Count; i++)
        {
            if (boundSensors[i] == sensor)
                return true;
        }

        return false;
    }

    private void WriteBlock(Action<MaterialPropertyBlock> write)
    {
        propertyBlock ??= new MaterialPropertyBlock();

        foreach (Renderer target in Renderers)
        {
            if (target == null)
                continue;

            // Read first so clip values written by HybridHopperClipController,
            // and the other X-Ray properties, are preserved.
            target.GetPropertyBlock(propertyBlock);
            write(propertyBlock);
            target.SetPropertyBlock(propertyBlock);
        }
    }

    /// <summary>World-space bounds of the mechanism, used for callouts and picking.</summary>
    public bool TryGetWorldBounds(out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        foreach (Renderer target in Renderers)
        {
            if (target == null || !target.enabled)
                continue;

            if (!found)
            {
                bounds = target.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(target.bounds);
            }
        }

        return found;
    }
}
