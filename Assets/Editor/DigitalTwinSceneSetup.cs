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

    [MenuItem("Tools/Digital Twin/Set Up Sensors And Flow Controls")]
    public static void SetUpFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        DigitalTwinMachineConfiguration configuration = SetUpScene();
        EditorUtility.DisplayDialog(
            "Digital Twin Setup",
            $"'{configuration.displayName}' is configured with " +
            $"{configuration.sensors.Count} sensors.",
            "OK");
    }

    // Entry point used by headless verification/setup.
    public static DigitalTwinMachineConfiguration SetUpScene()
    {
        DigitalTwinMachineConfiguration configuration =
            DigitalTwinMachineConfigurationLoader.LoadDefault();
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        PerformanceStatSource[] existingSources = UnityEngine.Object
            .FindObjectsByType<PerformanceStatSource>(FindObjectsInactive.Include);
        Dictionary<string, PerformanceStatSource> existing = FindSourcesById(existingSources);
        GameObject sensorRoot = GetOrCreateTelemetryRoot(configuration);

        List<PerformanceStatSource> orderedSources = new();

        foreach (MachineSensorDefinition sensor in configuration.sensors)
        {
            PerformanceStatSource source;
            bool created = false;

            if (!existing.TryGetValue(sensor.id, out source) &&
                (string.IsNullOrWhiteSpace(sensor.legacyId) ||
                 !existing.TryGetValue(sensor.legacyId, out source)))
            {
                source = CreateSensor(sensorRoot.transform);
                created = true;
            }

            ConfigureSource(source, sensor, sensorRoot.transform, created);
            orderedSources.Add(source);
        }

        RemoveUnconfiguredSources(existingSources, orderedSources);
        RemoveEmptyTelemetryCategories(sensorRoot.transform);

        AssignSourcesToAll<WideStatRailManager>(orderedSources);
        AssignSourcesToAll<PortraitStatRailManager>(orderedSources);
        RemoveLegacyPresentationGroups();
        ConfigureRegistry(orderedSources);
        ConfigureLiveDataAndMachineBehavior(configuration);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log(
            $"Digital twin scene setup completed for '{configuration.machineId}' " +
            $"with {orderedSources.Count} sensors.");
        return configuration;
    }

    // Applies this implementation phase in a deterministic order for CI and
    // one-click project updates without requiring a WebGL build.
    public static void ApplyCurrentPhase()
    {
        SetUpScene();
        MachineViewRuntimeSetup.Apply();
        ConnectionHealthUISetup.Apply();
        PortraitPaginationUISetup.Apply();
        DigitalTwinSceneOrganization.Apply();
        DigitalTwinProjectValidator.Validate();
    }

    public static void ApplyResponsiveRailPhase()
    {
        TelemetryRailExpansionSetup.Apply();
        DigitalTwinProjectValidator.Validate();
    }

    private static Dictionary<string, PerformanceStatSource> FindSourcesById(
        IEnumerable<PerformanceStatSource> sources)
    {
        return sources
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
        MachineSensorDefinition sensor,
        Transform telemetryRoot,
        bool initializeRuntimeState)
    {
        source.gameObject.name = sensor.objectName;
        source.transform.SetParent(
            GetOrCreateChild(telemetryRoot, NormalizeCategory(sensor.category)),
            false);
        source.transform.localPosition = sensor.localPosition;

        SerializedObject serialized = new(source);
        Set(serialized, "statId", sensor.id);
        Set(serialized, "metricName", sensor.label);
        Set(serialized, "unit", sensor.unit);
        Set(serialized, "valueFormat", sensor.valueFormat);
        if (initializeRuntimeState)
        {
            Set(serialized, "currentValue", sensor.initialValue);
            Set(serialized, "visualState", (int)StatVisualState.Normal);
            Set(serialized, "dataQuality", (int)TelemetryDataQuality.Good);
        }
        Set(serialized, "useAutomaticThresholds", true);
        Set(serialized, "limitMode", (int)sensor.limitMode);
        Set(serialized, "warningBelow", sensor.warningBelow);
        Set(serialized, "criticalBelow", sensor.criticalBelow);
        Set(serialized, "warningAbove", sensor.warningAbove);
        Set(serialized, "criticalAbove", sensor.criticalAbove);
        Set(serialized, "staleAfterSeconds", sensor.staleAfterSeconds);
        Set(serialized, "preferredRail", (int)sensor.wideRail);
        Set(serialized, "preferredPortraitRail", (int)sensor.portraitRail);
        Set(serialized, "displayPriority", sensor.displayPriority);
        Set(serialized, "visible", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void RemoveUnconfiguredSources(
        IEnumerable<PerformanceStatSource> existing,
        IReadOnlyCollection<PerformanceStatSource> configured)
    {
        HashSet<PerformanceStatSource> retained = new(configured);
        foreach (PerformanceStatSource source in existing.Distinct().Where(source => !retained.Contains(source)))
            UnityEngine.Object.DestroyImmediate(source.gameObject);
    }

    private static void RemoveEmptyTelemetryCategories(Transform telemetryRoot)
    {
        foreach (Transform child in telemetryRoot.Cast<Transform>().ToArray())
        {
            if (child.childCount == 0)
                UnityEngine.Object.DestroyImmediate(child.gameObject);
        }
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

    private static void RemoveLegacyPresentationGroups()
    {
        foreach (Transform candidate in UnityEngine.Object
                     .FindObjectsByType<Transform>(FindObjectsInactive.Include))
        {
            if (candidate.name == "Presentation Groups" ||
                candidate.name == "Telemetry Presentation Groups")
            {
                UnityEngine.Object.DestroyImmediate(candidate.gameObject);
            }
        }
    }

    private static void ConfigureLiveDataAndMachineBehavior(
        DigitalTwinMachineConfiguration configuration)
    {
        GameObject runtimeRoot = GetOrCreateRoot("Digital Twin Runtime");
        TelemetryRegistry registry = runtimeRoot.GetComponent<TelemetryRegistry>();

        TelemetryJsonIngestor ingestor = runtimeRoot.GetComponent<TelemetryJsonIngestor>();

        if (ingestor == null)
            ingestor = runtimeRoot.AddComponent<TelemetryJsonIngestor>();

        SerializedObject ingestorData = new(ingestor);
        ingestorData.FindProperty("registry").objectReferenceValue = registry;
        ingestorData.ApplyModifiedPropertiesWithoutUndo();

        Transform machineRuntime = GetOrCreateChild(runtimeRoot.transform, "Machine Runtime");
        foreach (MonoBehaviour component in machineRuntime.GetComponents<MonoBehaviour>())
            UnityEngine.Object.DestroyImmediate(component);

        IDigitalTwinMachineSceneConfigurator configurator =
            DigitalTwinMachineConfigurationLoader.CreateSceneConfigurator(configuration);
        MonoBehaviour simulationProvider =
            configurator?.Configure(machineRuntime.gameObject, registry);
        if (simulationProvider != null && simulationProvider is not ITelemetrySimulationProvider)
        {
            throw new InvalidOperationException(
                $"Machine configurator returned '{simulationProvider.GetType().Name}', which does " +
                "not implement ITelemetrySimulationProvider.");
        }

        TelemetryOperatingModeController modeController =
            runtimeRoot.GetComponent<TelemetryOperatingModeController>();

        if (modeController == null)
            modeController = runtimeRoot.AddComponent<TelemetryOperatingModeController>();

        SerializedObject modeData = new(modeController);
        modeData.FindProperty("operatingMode").intValue =
            (int)TelemetryOperatingMode.Simulation;
        modeData.FindProperty("registry").objectReferenceValue = registry;
        modeData.FindProperty("simulator").objectReferenceValue = simulationProvider;
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

    private static GameObject GetOrCreateTelemetryRoot(
        DigitalTwinMachineConfiguration configuration)
    {
        GameObject digitalTwin = GetOrCreateRoot("Digital Twin");
        GameObject telemetryRoot = GetOrCreateRenamedRoot(
            "Telemetry Sources",
            "Digital Twin Sensors");
        telemetryRoot.transform.SetParent(digitalTwin.transform, true);

        string[] orderedGroups = configuration.sensors
            .Select(sensor => NormalizeCategory(sensor.category))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

    private static string NormalizeCategory(string category) =>
        string.IsNullOrWhiteSpace(category) ? "Telemetry" : category.Trim();

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
