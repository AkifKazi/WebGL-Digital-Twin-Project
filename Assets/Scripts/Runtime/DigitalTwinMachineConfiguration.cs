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
/// One mechanism of the machine for X-Ray inspection: the geometry it owns and
/// the telemetry categories that describe it.
/// </summary>
[Serializable]
public sealed class MachinePartDefinition
{
    [Tooltip("Stable identifier for this mechanism.")]
    public string id;

    [Tooltip("Name shown when the mechanism is selected or isolated.")]
    public string displayName;

    [Tooltip("Object paths under the equipment root holding this mechanism's renderers, for " +
             "example 'Conveyor Assembly' or 'Vibratory Drive/Motor'. Several paths may make up " +
             "one mechanism; the first one found hosts the component.")]
    public List<string> objectPaths = new();

    [Tooltip("Telemetry categories owned by this mechanism. Hovering a card in one of these " +
             "categories isolates this mechanism in the X-Ray view.")]
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
