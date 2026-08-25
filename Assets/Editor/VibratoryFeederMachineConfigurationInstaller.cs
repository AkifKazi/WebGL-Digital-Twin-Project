using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class VibratoryFeederMachineConfigurationInstaller
{
    private const string FolderPath = "Assets/Machine Configurations";
    private const string AssetPath = FolderPath + "/Vibratory Feeder.asset";

    [InitializeOnLoadMethod]
    private static void EnsureReferenceConfigurationExists()
    {
        if (AssetDatabase.FindAssets("t:DigitalTwinMachineConfiguration").Length == 0)
            EditorApplication.delayCall += Install;
    }

    [MenuItem("Tools/Digital Twin/Machine Configurations/Install Vibratory Feeder")]
    public static void Install()
    {
        if (!AssetDatabase.IsValidFolder(FolderPath))
            AssetDatabase.CreateFolder("Assets", "Machine Configurations");

        DigitalTwinMachineConfiguration configuration =
            AssetDatabase.LoadAssetAtPath<DigitalTwinMachineConfiguration>(AssetPath);
        if (configuration == null)
        {
            configuration = ScriptableObject.CreateInstance<DigitalTwinMachineConfiguration>();
            AssetDatabase.CreateAsset(configuration, AssetPath);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:DigitalTwinMachineConfiguration"))
        {
            DigitalTwinMachineConfiguration other =
                AssetDatabase.LoadAssetAtPath<DigitalTwinMachineConfiguration>(
                    AssetDatabase.GUIDToAssetPath(guid));
            if (other != null && other != configuration && other.isDefault)
            {
                other.isDefault = false;
                EditorUtility.SetDirty(other);
            }
        }

        DigitalTwinMachineConfiguration template =
            new VibratoryFeederMachineConfigurationProvider().CreateConfiguration();
        configuration.machineId = template.machineId;
        configuration.displayName = template.displayName;
        configuration.isDefault = template.isDefault;
        configuration.editorConfiguratorType = template.editorConfiguratorType;
        configuration.sensors = template.sensors;
        Object.DestroyImmediate(template);

        EditorUtility.SetDirty(configuration);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(
            $"MACHINE_CONFIGURATION_INSTALLED id={configuration.machineId} " +
            $"sensors={configuration.sensors.Count} path='{AssetPath}'");
    }

    internal static List<MachineSensorDefinition> BuildSensors() => new()
    {
        Sensor("FEED-01.FLOW.IN", "STAT-01", "Inlet Rate", "Material Feed", "INLET RATE", "kg/s", 12.5f,
            8f, 0.5f, 17f, 20f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 2.571f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Auto, 40),
        Sensor("FEED-01.FLOW.OUT", "STAT-02", "Discharge Rate", "Material Feed", "DISCHARGE RATE", "kg/s", 12.3f,
            8f, 0.5f, 17f, 20f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 1.278f, 2f), PreferredStatRail.Auto, PreferredPortraitStatRail.Auto, 30),
        Sensor("VIB-01.MOTOR.TEMP", "STAT-03", "Motor Temperature", "Vibratory Drive", "MOTOR TEMPERATURE", "°C", 65f,
            0f, 0f, 80f, 95f, TelemetryLimitMode.HighOnly, new Vector3(0f, 0.697f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Auto, 80),
        Sensor("VIB-01.MOTOR.SPEED", "STAT-04", "Motor Speed", "Vibratory Drive", "MOTOR SPEED", "rpm", 1480f,
            1350f, 1200f, 1550f, 1650f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 0.697f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Auto, 70),
        Sensor("VIB-01.BEARING.VEL_RMS", "STAT-05", "Bearing Vibration", "Vibratory Drive", "BEARING VIBRATION", "mm/s RMS", 4.2f,
            0f, 0f, 7.1f, 11f, TelemetryLimitMode.HighOnly, new Vector3(0f, 0.82f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 100),
        Sensor("VIB-01.MOTOR.POWER", "STAT-06", "Motor Power", "Vibratory Drive", "MOTOR POWER", "kW", 18.6f,
            8f, 4f, 21f, 23f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 0.61f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Bottom, 60),
        Sensor("VIB-01.MOTOR.CURRENT", "STAT-07", "Motor Current", "Vibratory Drive", "MOTOR CURRENT", "A", 36.4f,
            0f, 0f, 42f, 48f, TelemetryLimitMode.HighOnly, new Vector3(0f, 0.58f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 50),
        Sensor("HOP-01.LEVEL", "STAT-08", "Fill Level", "Hopper", "FILL LEVEL", "%", 68f,
            20f, 10f, 85f, 95f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 2.05f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Top, 90),
        Sensor("VIB-01.BEARING.TEMP", "", "Bearing Temperature", "Vibratory Drive", "BEARING TEMPERATURE", "°C", 57f,
            0f, 0f, 75f, 90f, TelemetryLimitMode.HighOnly, new Vector3(0f, 0.79f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 85),
        Sensor("VIB-01.DRIVE.FREQUENCY", "", "Drive Frequency", "Vibratory Drive", "DRIVE FREQUENCY", "Hz", 50f,
            47f, 45f, 51f, 53f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 0.65f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Bottom, 45),
        Sensor("VIB-01.MOTOR.LOAD", "", "Motor Load", "Vibratory Drive", "MOTOR LOAD", "%", 84f,
            0f, 0f, 90f, 100f, TelemetryLimitMode.HighOnly, new Vector3(0f, 0.63f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 65),
        Sensor("FEED-01.BELT.SPEED", "", "Belt Speed", "Material Feed", "BELT SPEED", "m/s", 0.8f,
            0.55f, 0.35f, 1f, 1.15f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 0.38f, -2f), PreferredStatRail.Left, PreferredPortraitStatRail.Auto, 35),
        Sensor("HOP-01.MASS", "", "Material Mass", "Hopper", "MATERIAL MASS", "t", 87f,
            25.6f, 12.8f, 108.8f, 121.6f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 2.04f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Top, 75),
        Sensor("HOP-01.MATERIAL.MOISTURE", "", "Material Moisture", "Hopper", "MATERIAL MOISTURE", "%", 6.8f,
            2f, 1f, 10f, 14f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 2.18f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Top, 58),
        Sensor("HOP-01.MATERIAL.TEMP", "", "Material Temperature", "Hopper", "MATERIAL TEMPERATURE", "°C", 31.4f,
            -5f, -15f, 45f, 55f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 2.20f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Top, 52),
        Sensor("HOP-01.LOADCELL.IMBALANCE", "", "Load Imbalance", "Hopper", "LOAD IMBALANCE", "%", 2.1f,
            0f, 0f, 8f, 15f, TelemetryLimitMode.HighOnly, new Vector3(0f, 1.45f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Top, 82),
        Sensor("HOP-01.OUTLET.GATE.POSITION", "", "Gate Position", "Hopper", "GATE POSITION", "%", 74f,
            25f, 10f, 95f, 99f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 1.22f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Top, 68),
        Sensor("FEED-01.BELT.TENSION", "", "Belt Tension", "Material Feed", "BELT TENSION", "kN", 18.2f,
            14f, 11f, 22f, 25f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 0.42f, 2f), PreferredStatRail.Left, PreferredPortraitStatRail.Bottom, 73),
        Sensor("FEED-01.BEARING.TEMP", "", "Conveyor Bearing Temperature", "Material Feed", "CONVEYOR BEARING TEMP", "°C", 49f,
            0f, 0f, 70f, 85f, TelemetryLimitMode.HighOnly, new Vector3(0f, 0.43f, 2f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 78),
        Sensor("FEED-01.DRIVE.CURRENT", "", "Conveyor Drive Current", "Material Feed", "CONVEYOR CURRENT", "A", 14.8f,
            3f, 1f, 19f, 23f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 0.48f, 2f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 62),
        Sensor("FEED-01.SLIP", "", "Belt Slip", "Material Feed", "BELT SLIP", "%", 1.2f,
            0f, 0f, 4f, 7f, TelemetryLimitMode.HighOnly, new Vector3(0f, 0.38f, -2f), PreferredStatRail.Left, PreferredPortraitStatRail.Bottom, 88),
        Sensor("STRUCT-01.FRAME.VIBRATION", "", "Frame Vibration", "Structure", "FRAME VIBRATION", "mm/s RMS", 1.8f,
            0f, 0f, 4.5f, 7.1f, TelemetryLimitMode.HighOnly, new Vector3(0f, 0.55f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Bottom, 76),
        Sensor("ENV-01.AMBIENT.TEMP", "", "Ambient Temperature", "Environment", "AMBIENT TEMPERATURE", "°C", 25f,
            -5f, -15f, 40f, 48f, TelemetryLimitMode.OutsideRange, new Vector3(-2f, 1.75f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Top, 20),
        Sensor("ENV-01.RELATIVE.HUMIDITY", "", "Ambient Humidity", "Environment", "AMBIENT HUMIDITY", "% RH", 48f,
            15f, 5f, 75f, 90f, TelemetryLimitMode.OutsideRange, new Vector3(-2f, 1.75f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Top, 18),
        Sensor("POWER-01.SUPPLY.VOLTAGE", "", "Supply Voltage", "Electrical Supply", "SUPPLY VOLTAGE", "V", 400f,
            380f, 360f, 420f, 440f, TelemetryLimitMode.OutsideRange, new Vector3(0f, 0.66f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 66),
        Sensor("POWER-01.POWER.FACTOR", "", "Power Factor", "Electrical Supply", "POWER FACTOR", "PF", 0.82f,
            0.75f, 0.65f, 1f, 1f, TelemetryLimitMode.LowOnly, new Vector3(0f, 0.72f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 54)
    };

    private static MachineSensorDefinition Sensor(
        string id, string legacyId, string objectName, string category,
        string label, string unit, float initialValue,
        float warningBelow, float criticalBelow, float warningAbove, float criticalAbove,
        TelemetryLimitMode limitMode, Vector3 localPosition,
        PreferredStatRail wideRail, PreferredPortraitStatRail portraitRail, int priority)
    {
        return new MachineSensorDefinition
        {
            id = id,
            legacyId = legacyId,
            objectName = objectName,
            category = category,
            label = label,
            unit = unit,
            valueFormat = "0.0",
            initialValue = initialValue,
            warningBelow = warningBelow,
            criticalBelow = criticalBelow,
            warningAbove = warningAbove,
            criticalAbove = criticalAbove,
            limitMode = limitMode,
            staleAfterSeconds = 5f,
            localPosition = localPosition,
            wideRail = wideRail,
            portraitRail = portraitRail,
            displayPriority = priority
        };
    }
}

public sealed class VibratoryFeederMachineConfigurationProvider :
    IDigitalTwinMachineConfigurationProvider
{
    public bool IsDefault => true;

    public DigitalTwinMachineConfiguration CreateConfiguration()
    {
        DigitalTwinMachineConfiguration configuration =
            ScriptableObject.CreateInstance<DigitalTwinMachineConfiguration>();
        configuration.hideFlags = HideFlags.HideAndDontSave;
        configuration.machineId = "VIBRATORY-FEEDER-01";
        configuration.displayName = "Vibratory Feeder and Hopper";
        configuration.isDefault = true;
        configuration.editorConfiguratorType = nameof(VibratoryFeederSceneConfigurator);
        configuration.sensors = VibratoryFeederMachineConfigurationInstaller.BuildSensors();
        return configuration;
    }
}
