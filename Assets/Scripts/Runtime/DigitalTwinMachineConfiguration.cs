using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class MachineSensorDefinition
{
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
}

[CreateAssetMenu(
    fileName = "Machine Configuration",
    menuName = "Digital Twin/Machine Configuration")]
public sealed class DigitalTwinMachineConfiguration : ScriptableObject
{
    [Header("Identity")]
    public string machineId;
    public string displayName;
    [Tooltip("Exactly one configuration should be the default for automated scene setup.")]
    public bool isDefault;

    [Header("Machine-specific scene behavior")]
    [Tooltip("Optional editor configurator type implementing IDigitalTwinMachineSceneConfigurator.")]
    public string editorConfiguratorType;

    [Header("Telemetry sensors")]
    public List<MachineSensorDefinition> sensors = new();
}
