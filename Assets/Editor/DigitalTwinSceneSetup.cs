using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DigitalTwinSceneSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    private readonly struct SensorSpec
    {
        public readonly string Id;
        public readonly string LegacyId;
        public readonly string ObjectName;
        public readonly string Label;
        public readonly string Unit;
        public readonly float Value;
        public readonly float Warning;
        public readonly float Critical;
        public readonly float WarningBelow;
        public readonly float CriticalBelow;
        public readonly TelemetryLimitMode LimitMode;
        public readonly Vector3 Position;
        public readonly PreferredStatRail WideRail;
        public readonly PreferredPortraitStatRail PortraitRail;
        public readonly int Priority;

        public SensorSpec(
            string id,
            string legacyId,
            string objectName,
            string label,
            string unit,
            float value,
            float warning,
            float critical,
            float warningBelow,
            float criticalBelow,
            TelemetryLimitMode limitMode,
            Vector3 position,
            PreferredStatRail wideRail,
            PreferredPortraitStatRail portraitRail,
            int priority)
        {
            Id = id;
            LegacyId = legacyId;
            ObjectName = objectName;
            Label = label;
            Unit = unit;
            Value = value;
            Warning = warning;
            Critical = critical;
            WarningBelow = warningBelow;
            CriticalBelow = criticalBelow;
            LimitMode = limitMode;
            Position = position;
            WideRail = wideRail;
            PortraitRail = portraitRail;
            Priority = priority;
        }
    }

    private static readonly SensorSpec[] Specs =
    {
        new("FEED-01.FLOW.IN", "STAT-01", "Inlet Rate", "INLET RATE", "kg/s", 12.5f, 17f, 20f, 8f, 0.5f, TelemetryLimitMode.OutsideRange,
            new Vector3(0f, 2.571f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Auto, 40),
        new("FEED-01.FLOW.OUT", "STAT-02", "Discharge Rate", "DISCHARGE RATE", "kg/s", 12.3f, 17f, 20f, 8f, 0.5f, TelemetryLimitMode.OutsideRange,
            new Vector3(0f, 1.278f, 1.963f), PreferredStatRail.Auto, PreferredPortraitStatRail.Auto, 30),
        new("VIB-01.MOTOR.TEMP", "STAT-03", "Motor Temperature", "MOTOR TEMPERATURE", "°C", 65f, 80f, 95f, 0f, 0f, TelemetryLimitMode.HighOnly,
            new Vector3(0f, 0.697f, 0f), PreferredStatRail.Right, PreferredPortraitStatRail.Auto, 80),
        new("VIB-01.MOTOR.SPEED", "STAT-04", "Motor Speed", "MOTOR SPEED", "rpm", 1480f, 1550f, 1650f, 1350f, 1200f, TelemetryLimitMode.OutsideRange,
            new Vector3(0f, 0.697f, 0f), PreferredStatRail.Left, PreferredPortraitStatRail.Auto, 70),
        new("VIB-01.BEARING.VEL_RMS", "STAT-05", "Bearing Vibration", "BEARING VIBRATION", "mm/s RMS", 4.2f, 7.1f, 11f, 0f, 0f, TelemetryLimitMode.HighOnly,
            new Vector3(0.16f, 0.82f, 0.08f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 100),
        new("VIB-01.MOTOR.POWER", "STAT-06", "Motor Power", "MOTOR POWER", "kW", 18.6f, 21f, 23f, 8f, 4f, TelemetryLimitMode.OutsideRange,
            new Vector3(-0.16f, 0.61f, 0.08f), PreferredStatRail.Left, PreferredPortraitStatRail.Bottom, 60),
        new("VIB-01.MOTOR.CURRENT", "STAT-07", "Motor Current", "MOTOR CURRENT", "A", 36.4f, 42f, 48f, 0f, 0f, TelemetryLimitMode.HighOnly,
            new Vector3(0.18f, 0.58f, -0.08f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 50),
        new("HOP-01.LEVEL", "STAT-08", "Fill Level", "FILL LEVEL", "%", 68f, 85f, 95f, 20f, 10f, TelemetryLimitMode.OutsideRange,
            new Vector3(-0.2f, 2.05f, 0.25f), PreferredStatRail.Left, PreferredPortraitStatRail.Top, 90),
        new("VIB-01.BEARING.TEMP", "", "Bearing Temperature", "BEARING TEMPERATURE", "°C", 57f, 75f, 90f, 0f, 0f, TelemetryLimitMode.HighOnly,
            new Vector3(0.14f, 0.79f, 0.06f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 85),
        new("VIB-01.DRIVE.FREQUENCY", "", "Drive Frequency", "DRIVE FREQUENCY", "Hz", 50f, 51f, 53f, 47f, 45f, TelemetryLimitMode.OutsideRange,
            new Vector3(-0.1f, 0.65f, 0.04f), PreferredStatRail.Left, PreferredPortraitStatRail.Bottom, 45),
        new("VIB-01.MOTOR.LOAD", "", "Motor Load", "MOTOR LOAD", "%", 84f, 90f, 100f, 0f, 0f, TelemetryLimitMode.HighOnly,
            new Vector3(0.08f, 0.63f, -0.05f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 65),
        new("FEED-01.BELT.SPEED", "", "Belt Speed", "BELT SPEED", "m/s", 0.8f, 1.0f, 1.15f, 0.55f, 0.35f, TelemetryLimitMode.OutsideRange,
            new Vector3(0f, 0.38f, 1.72f), PreferredStatRail.Left, PreferredPortraitStatRail.Auto, 35),
        new("HOP-01.MASS", "", "Material Mass", "MATERIAL MASS", "t", 87f, 108.8f, 121.6f, 25.6f, 12.8f, TelemetryLimitMode.OutsideRange,
            new Vector3(0.2f, 2.04f, 0.2f), PreferredStatRail.Right, PreferredPortraitStatRail.Top, 75),

        // Condition and process measurements expected on a production feeder.
        // These are intentionally separate presentations: each belongs to a
        // distinct physical sensing point rather than the drive-motor anchor.
        new("HOP-01.MATERIAL.MOISTURE", "", "Material Moisture", "MATERIAL MOISTURE", "%", 6.8f, 10f, 14f, 2f, 1f, TelemetryLimitMode.OutsideRange,
            new Vector3(-0.34f, 2.18f, 0.38f), PreferredStatRail.Left, PreferredPortraitStatRail.Top, 58),
        new("HOP-01.MATERIAL.TEMP", "", "Material Temperature", "MATERIAL TEMPERATURE", "°C", 31.4f, 45f, 55f, -5f, -15f, TelemetryLimitMode.OutsideRange,
            new Vector3(0.34f, 2.20f, 0.34f), PreferredStatRail.Right, PreferredPortraitStatRail.Top, 52),
        new("HOP-01.LOADCELL.IMBALANCE", "", "Load Imbalance", "LOAD IMBALANCE", "%", 2.1f, 8f, 15f, 0f, 0f, TelemetryLimitMode.HighOnly,
            new Vector3(-0.52f, 1.45f, 0.10f), PreferredStatRail.Left, PreferredPortraitStatRail.Top, 82),
        new("HOP-01.OUTLET.GATE.POSITION", "", "Gate Position", "GATE POSITION", "%", 74f, 95f, 99f, 25f, 10f, TelemetryLimitMode.OutsideRange,
            new Vector3(0f, 1.22f, 0.18f), PreferredStatRail.Right, PreferredPortraitStatRail.Top, 68),
        new("FEED-01.BELT.TENSION", "", "Belt Tension", "BELT TENSION", "kN", 18.2f, 22f, 25f, 14f, 11f, TelemetryLimitMode.OutsideRange,
            new Vector3(-0.38f, 0.42f, 1.46f), PreferredStatRail.Left, PreferredPortraitStatRail.Bottom, 73),
        new("FEED-01.BEARING.TEMP", "", "Conveyor Bearing Temperature", "CONVEYOR BEARING TEMP", "°C", 49f, 70f, 85f, 0f, 0f, TelemetryLimitMode.HighOnly,
            new Vector3(0.34f, 0.43f, 1.58f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 78),
        new("FEED-01.DRIVE.CURRENT", "", "Conveyor Drive Current", "CONVEYOR CURRENT", "A", 14.8f, 19f, 23f, 3f, 1f, TelemetryLimitMode.OutsideRange,
            new Vector3(0.42f, 0.48f, 1.28f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 62),
        new("FEED-01.SLIP", "", "Belt Slip", "BELT SLIP", "%", 1.2f, 4f, 7f, 0f, 0f, TelemetryLimitMode.HighOnly,
            new Vector3(-0.18f, 0.38f, 1.90f), PreferredStatRail.Left, PreferredPortraitStatRail.Bottom, 88),
        new("STRUCT-01.FRAME.VIBRATION", "", "Frame Vibration", "FRAME VIBRATION", "mm/s RMS", 1.8f, 4.5f, 7.1f, 0f, 0f, TelemetryLimitMode.HighOnly,
            new Vector3(-0.72f, 0.55f, 0.05f), PreferredStatRail.Left, PreferredPortraitStatRail.Bottom, 76),
        new("ENV-01.AMBIENT.TEMP", "", "Ambient Temperature", "AMBIENT TEMPERATURE", "°C", 25f, 40f, 48f, -5f, -15f, TelemetryLimitMode.OutsideRange,
            new Vector3(-1.05f, 1.75f, -0.30f), PreferredStatRail.Left, PreferredPortraitStatRail.Top, 20),
        new("ENV-01.RELATIVE.HUMIDITY", "", "Ambient Humidity", "AMBIENT HUMIDITY", "% RH", 48f, 75f, 90f, 15f, 5f, TelemetryLimitMode.OutsideRange,
            new Vector3(1.05f, 1.75f, -0.30f), PreferredStatRail.Right, PreferredPortraitStatRail.Top, 18),
        new("POWER-01.SUPPLY.VOLTAGE", "", "Supply Voltage", "SUPPLY VOLTAGE", "V", 400f, 420f, 440f, 380f, 360f, TelemetryLimitMode.OutsideRange,
            new Vector3(0.58f, 0.66f, -0.42f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 66),
        new("POWER-01.POWER.FACTOR", "", "Power Factor", "POWER FACTOR", "PF", 0.82f, 1f, 1f, 0.75f, 0.65f, TelemetryLimitMode.LowOnly,
            new Vector3(0.62f, 0.72f, -0.38f), PreferredStatRail.Right, PreferredPortraitStatRail.Bottom, 54)
    };

    [MenuItem("Tools/Digital Twin/Set Up Sensors And Flow Controls")]
    public static void SetUpFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        SetUpScene();
        EditorUtility.DisplayDialog(
            "Digital Twin Setup",
            $"{Specs.Length} sensors, equipment grouping, rail connections, registry, and process controls are configured.",
            "OK");
    }

    // Entry point used by headless verification/setup.
    public static void SetUpScene()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Dictionary<string, PerformanceStatSource> existing = FindSourcesById();
        GameObject sensorRoot = GetOrCreateTelemetryRoot();

        List<PerformanceStatSource> orderedSources = new();

        foreach (SensorSpec spec in Specs)
        {
            PerformanceStatSource source;

            if (!existing.TryGetValue(spec.Id, out source) &&
                !existing.TryGetValue(spec.LegacyId, out source))
            {
                source = CreateSensor(sensorRoot.transform);
            }

            ConfigureSource(source, spec, sensorRoot.transform);
            orderedSources.Add(source);
        }

        AssignSourcesToAll<WideStatRailManager>(orderedSources);
        AssignSourcesToAll<PortraitStatRailManager>(orderedSources);
        ConfigurePresentationGroups(orderedSources);
        ConfigureRegistry(orderedSources);
        ConfigureParticleController();
        ConfigureLiveDataAndProcessModel();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Digital twin scene setup completed successfully.");
    }

    // Applies this implementation phase in a deterministic order for CI and
    // one-click project updates without requiring a WebGL build.
    public static void ApplyCurrentPhase()
    {
        SetUpScene();
        ConnectionHealthUISetup.Apply();
        DigitalTwinSceneOrganization.Apply();
        DigitalTwinProjectValidator.Validate();
    }

    public static void ApplyResponsiveRailPhase()
    {
        TelemetryRailExpansionSetup.Apply();
        DigitalTwinProjectValidator.Validate();
    }

    private static Dictionary<string, PerformanceStatSource> FindSourcesById()
    {
        return UnityEngine.Object
            .FindObjectsByType<PerformanceStatSource>(FindObjectsInactive.Include)
            .Where(source => !string.IsNullOrWhiteSpace(source.StatId))
            .GroupBy(source => source.StatId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    private static PerformanceStatSource CreateSensor(Transform parent)
    {
        GameObject sensorObject = new("Telemetry Sensor");
        Undo.RegisterCreatedObjectUndo(sensorObject, "Create telemetry sensor");
        sensorObject.transform.SetParent(parent, true);
        return sensorObject.AddComponent<PerformanceStatSource>();
    }

    private static void ConfigureSource(
        PerformanceStatSource source,
        SensorSpec spec,
        Transform telemetryRoot)
    {
        source.gameObject.name = spec.ObjectName;
        source.transform.SetParent(
            GetOrCreateChild(telemetryRoot, GetTelemetryGroupName(spec.Id)),
            true);
        source.transform.position = spec.Position;

        SerializedObject serialized = new(source);
        Set(serialized, "statId", spec.Id);
        Set(serialized, "metricName", spec.Label);
        Set(serialized, "unit", spec.Unit);
        Set(serialized, "valueFormat", "0.0");
        Set(serialized, "currentValue", spec.Value);
        Set(serialized, "visualState", (int)StatVisualState.Normal);
        Set(serialized, "useAutomaticThresholds", true);
        Set(serialized, "limitMode", (int)spec.LimitMode);
        Set(serialized, "warningBelow", spec.WarningBelow);
        Set(serialized, "criticalBelow", spec.CriticalBelow);
        Set(serialized, "warningAbove", spec.Warning);
        Set(serialized, "criticalAbove", spec.Critical);
        Set(serialized, "dataQuality", (int)TelemetryDataQuality.Good);
        Set(serialized, "preferredRail", (int)spec.WideRail);
        Set(serialized, "preferredPortraitRail", (int)spec.PortraitRail);
        Set(serialized, "displayPriority", spec.Priority);
        Set(serialized, "visible", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssignSourcesToAll<T>(IReadOnlyList<PerformanceStatSource> sources)
        where T : MonoBehaviour
    {
        foreach (T manager in UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include))
        {
            SerializedObject serialized = new(manager);
            SerializedProperty array = serialized.FindProperty("sources");

            if (array == null)
                continue;

            array.arraySize = sources.Count;

            for (int i = 0; i < sources.Count; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = sources[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void ConfigureRegistry(IReadOnlyList<PerformanceStatSource> sources)
    {
        GameObject runtimeRoot = GetOrCreateRoot("Digital Twin Runtime");
        TelemetryRegistry registry = runtimeRoot.GetComponent<TelemetryRegistry>();

        if (registry == null)
            registry = runtimeRoot.AddComponent<TelemetryRegistry>();

        SerializedObject serialized = new(registry);
        SerializedProperty array = serialized.FindProperty("sources");
        array.arraySize = sources.Count;

        for (int i = 0; i < sources.Count; i++)
            array.GetArrayElementAtIndex(i).objectReferenceValue = sources[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigurePresentationGroups(IReadOnlyList<PerformanceStatSource> sources)
    {
        Transform digitalTwin = UnityEngine.Object
            .FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.name == "Digital Twin");
        GameObject groupRootObject = GetOrCreateRenamedRoot(
            "Presentation Groups",
            "Telemetry Presentation Groups");
        if (digitalTwin != null)
            groupRootObject.transform.SetParent(digitalTwin, true);

        Transform groupTransform = groupRootObject.transform.Find("Drive Motor");
        if (groupTransform == null)
            groupTransform = groupRootObject.transform.Find("Drive Motor Group");
        if (groupTransform == null)
        {
            groupTransform = new GameObject("Drive Motor").transform;
            groupTransform.SetParent(groupRootObject.transform, false);
        }
        groupTransform.name = "Drive Motor";
        groupTransform.position = new Vector3(0f, 0.70f, 0f);

        TelemetryEquipmentGroup group = groupTransform.GetComponent<TelemetryEquipmentGroup>();
        if (group == null)
            group = groupTransform.gameObject.AddComponent<TelemetryEquipmentGroup>();

        string[] memberIds =
        {
            "VIB-01.MOTOR.TEMP",
            "VIB-01.MOTOR.SPEED",
            "VIB-01.BEARING.VEL_RMS",
            "VIB-01.MOTOR.POWER",
            "VIB-01.MOTOR.CURRENT",
            "VIB-01.BEARING.TEMP",
            "VIB-01.DRIVE.FREQUENCY",
            "VIB-01.MOTOR.LOAD"
        };
        PerformanceStatSource[] members = sources
            .Where(source => memberIds.Contains(source.StatId, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        SerializedObject serialized = new(group);
        Set(serialized, "groupId", "VIB-01.DRIVE");
        Set(serialized, "displayName", "DRIVE MOTOR");
        serialized.FindProperty("worldAnchor").objectReferenceValue = groupTransform;
        SetObjectArray(serialized.FindProperty("members"), members);
        Set(serialized, "maximumMemberDistance", 0.75f);
        Set(serialized, "maximumVisibleMetrics", 3);
        Set(serialized, "regroupStableSeconds", 12f);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureParticleController()
    {
        GameObject runtimeRoot = GetOrCreateRoot("Digital Twin Runtime");
        ParticleFlowController controller = runtimeRoot.GetComponent<ParticleFlowController>();

        if (controller == null)
            controller = runtimeRoot.AddComponent<ParticleFlowController>();

        SerializedObject serialized = new(controller);
        Set(serialized, "flowSpeed", 1f);
        Set(serialized, "quantity", 1f);
        Set(serialized, "discoverInChildren", false);
        SetObjectArray(serialized.FindProperty("particleSystems"),
            UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include));
        SetObjectArray(serialized.FindProperty("rockFlows"),
            UnityEngine.Object.FindObjectsByType<ControlledRockFlow>(FindObjectsInactive.Include));
        SetObjectArray(serialized.FindProperty("filteredFlows"),
            UnityEngine.Object.FindObjectsByType<FilteredParticleFlow>(FindObjectsInactive.Include));
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureLiveDataAndProcessModel()
    {
        GameObject runtimeRoot = GetOrCreateRoot("Digital Twin Runtime");
        TelemetryRegistry registry = runtimeRoot.GetComponent<TelemetryRegistry>();
        ParticleFlowController particleController = runtimeRoot.GetComponent<ParticleFlowController>();

        TelemetryJsonIngestor ingestor = runtimeRoot.GetComponent<TelemetryJsonIngestor>();

        if (ingestor == null)
            ingestor = runtimeRoot.AddComponent<TelemetryJsonIngestor>();

        SerializedObject ingestorData = new(ingestor);
        ingestorData.FindProperty("registry").objectReferenceValue = registry;
        ingestorData.ApplyModifiedPropertiesWithoutUndo();

        HopperProcessSimulator simulator = runtimeRoot.GetComponent<HopperProcessSimulator>();

        if (simulator == null)
            simulator = runtimeRoot.AddComponent<HopperProcessSimulator>();

        SerializedObject simulatorData = new(simulator);
        simulatorData.FindProperty("registry").objectReferenceValue = registry;
        simulatorData.FindProperty("particleFlowController").objectReferenceValue = particleController;
        simulatorData.FindProperty("driveParticleControlsFromProcess").boolValue = false;
        simulatorData.ApplyModifiedPropertiesWithoutUndo();

        TelemetryOperatingModeController modeController =
            runtimeRoot.GetComponent<TelemetryOperatingModeController>();

        if (modeController == null)
            modeController = runtimeRoot.AddComponent<TelemetryOperatingModeController>();

        SerializedObject modeData = new(modeController);
        modeData.FindProperty("operatingMode").intValue =
            (int)TelemetryOperatingMode.Simulation;
        modeData.FindProperty("registry").objectReferenceValue = registry;
        modeData.FindProperty("simulator").objectReferenceValue = simulator;
        modeData.ApplyModifiedPropertiesWithoutUndo();

        ingestorData = new SerializedObject(ingestor);
        ingestorData.FindProperty("operatingModeController").objectReferenceValue = modeController;
        ingestorData.ApplyModifiedPropertiesWithoutUndo();
    }

    private static GameObject GetOrCreateRoot(string name)
    {
        GameObject existing = FindSceneObject(name);
        return existing != null ? existing : new GameObject(name);
    }

    private static GameObject FindSceneObject(string name)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return UnityEngine.Object
            .FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .Where(transform => transform.gameObject.scene == activeScene)
            .Select(transform => transform.gameObject)
            .FirstOrDefault(candidate => candidate.name == name);
    }

    private static GameObject GetOrCreateRenamedRoot(string name, string previousName)
    {
        GameObject existing = FindSceneObject(name) ?? FindSceneObject(previousName);
        if (existing == null)
            existing = new GameObject(name);
        existing.name = name;
        return existing;
    }

    private static GameObject GetOrCreateTelemetryRoot()
    {
        GameObject digitalTwin = GetOrCreateRoot("Digital Twin");
        GameObject telemetryRoot = GetOrCreateRenamedRoot(
            "Telemetry Sources",
            "Digital Twin Sensors");
        telemetryRoot.transform.SetParent(digitalTwin.transform, true);

        string[] orderedGroups =
        {
            "Hopper",
            "Vibratory Drive",
            "Material Feed",
            "Structure",
            "Electrical Supply",
            "Environment"
        };

        for (int i = 0; i < orderedGroups.Length; i++)
            GetOrCreateChild(telemetryRoot.transform, orderedGroups[i]).SetSiblingIndex(i);

        return telemetryRoot;
    }

    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child != null)
            return child;

        child = new GameObject(name).transform;
        child.SetParent(parent, false);
        return child;
    }

    private static string GetTelemetryGroupName(string statId)
    {
        if (string.IsNullOrWhiteSpace(statId))
            return "Environment";

        if (statId.StartsWith("HOP-", StringComparison.OrdinalIgnoreCase))
            return "Hopper";
        if (statId.StartsWith("VIB-", StringComparison.OrdinalIgnoreCase))
            return "Vibratory Drive";
        if (statId.StartsWith("FEED-", StringComparison.OrdinalIgnoreCase))
            return "Material Feed";
        if (statId.StartsWith("STRUCT-", StringComparison.OrdinalIgnoreCase))
            return "Structure";
        if (statId.StartsWith("POWER-", StringComparison.OrdinalIgnoreCase))
            return "Electrical Supply";
        return "Environment";
    }

    private static void SetObjectArray<T>(SerializedProperty property, IReadOnlyList<T> values)
        where T : UnityEngine.Object
    {
        property.arraySize = values.Count;

        for (int i = 0; i < values.Count; i++)
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }

    private static void Set(SerializedObject target, string name, string value) =>
        target.FindProperty(name).stringValue = value;

    private static void Set(SerializedObject target, string name, float value) =>
        target.FindProperty(name).floatValue = value;

    private static void Set(SerializedObject target, string name, int value) =>
        target.FindProperty(name).intValue = value;

    private static void Set(SerializedObject target, string name, bool value) =>
        target.FindProperty(name).boolValue = value;
}
