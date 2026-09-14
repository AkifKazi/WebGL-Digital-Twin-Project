using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class MachineSensorDefinition
{
    [Tooltip("Stable telemetry identifier used by the registry and live data.")]
    public string id;
    public string legacyId;
    public string objectName;
    public string category = "Telemetry";
    public string label;
    public string unit;
    public string valueFormat = "0.0";
    public float initialValue;
    public TelemetryLimitMode limitMode = TelemetryLimitMode.HighOnly;
    public float warningBelow;
    public float criticalBelow;
    public float warningAbove;
    public float criticalAbove;
    [Min(0f)] public float staleAfterSeconds = 5f;
    public Vector3 localPosition;
    public PreferredStatRail wideRail = PreferredStatRail.Auto;
    public PreferredPortraitStatRail portraitRail = PreferredPortraitStatRail.Auto;
    public int displayPriority;

    [Tooltip("Disabled sensors are still created and still receive data, but are hidden from the rails. " +
             "Use this to narrow the display without losing the definition.")]
    public bool enabled = true;
}

/// <summary>
/// One instrumented part of the machine - a bearing, the stator, the conveyor
/// belts - with the geometry it owns and the sensors read from it. Hovering a
/// card outlines every part that lists its sensor. A part with no sensors
/// still takes part in X-Ray hover isolation, so a mechanism fades as a whole.
/// </summary>
[Serializable]
public sealed class MachinePartDefinition
{
    [Tooltip("Stable identifier for this part.")]
    public string id;

    [Tooltip("Name shown when the part is selected or isolated.")]
    public string displayName;

    [Tooltip("Object paths under the equipment root holding this part's renderers, for " +
             "example 'Vibratory Drive/Drive Motor/Stator'. Several paths may make up one part, " +
             "such as the same component in the full and cross-section models.")]
    public List<string> objectPaths = new();

    [Tooltip("Sensors read from this part, by sensor ID. Hovering one of their cards outlines " +
             "this part. A sensor may be listed on several parts, and a part may list none.")]
    public List<string> sensorIds = new();

    [Tooltip("Coarse fallback used only when no sensor IDs are listed: every sensor in these " +
             "telemetry categories highlights the whole part.")]
    public List<string> sensorCategories = new();
}

[CreateAssetMenu(
    fileName = "Machine Configuration",
    menuName = "Digital Twin/Machine Configuration")]
public sealed class DigitalTwinMachineConfiguration : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable machine identifier used in logs and telemetry provenance.")]
    public string machineId;

    [Tooltip("Human-readable machine name.")]
    public string displayName;
    [Tooltip("Exactly one configuration should be the default for automated scene setup.")]
    public bool isDefault;

    [Header("Machine-specific scene behavior")]
    [Tooltip("Optional editor configurator type implementing IDigitalTwinMachineSceneConfigurator.")]
    public string editorConfiguratorType;

    [Header("Telemetry sensors")]
    public List<MachineSensorDefinition> sensors = new();

    [Header("X-Ray mechanisms")]
    [Tooltip("Mechanisms shown in the X-Ray view. Each binds geometry to telemetry categories, " +
             "so alarm colouring and hover isolation work on any machine without scene wiring.")]
    public List<MachinePartDefinition> machineParts = new();
}
