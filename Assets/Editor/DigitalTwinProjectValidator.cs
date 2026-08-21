using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DigitalTwinProjectValidator
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [MenuItem("Tools/Digital Twin/Validate Project")]
    public static void ValidateFromMenu()
    {
        ValidationResult result = ValidateInternal();
        EditorUtility.DisplayDialog(
            "Digital Twin Validation",
            result.Errors.Count == 0
                ? $"Validation passed with {result.Warnings.Count} warning(s).\n\n" +
                  "See the Console for details."
                : $"Validation found {result.Errors.Count} error(s) and " +
                  $"{result.Warnings.Count} warning(s).\n\nSee the Console for details.",
            "OK");
    }

    public static void Validate()
    {
        ValidationResult result = ValidateInternal();
        if (result.Errors.Count > 0)
            throw new InvalidOperationException("Digital twin project validation failed.");
    }

    private static ValidationResult ValidateInternal()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ValidationResult result = new();
        GameObject[] roots = scene.GetRootGameObjects();

        ValidateRoots(roots, result);
        ValidateMissingScripts(roots, result);

        PerformanceStatSource[] sources = UnityEngine.Object.FindObjectsByType<PerformanceStatSource>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);

        ValidateSensors(sources, result);
        ValidateMachineConfiguration(sources, result);
        ValidateEnvironmentModelSize(result);
        ValidateRegistry(sources, result);
        ValidateTelemetryArchitecture(sources, result);
        ValidateManagerSources<WideStatRailManager>(sources, result);
        ValidateManagerSources<PortraitStatRailManager>(sources, result);
        ValidateOverflowRails(result);
        ValidateEquipmentGroups(sources, result);
        ValidateParticleController(result);
        ValidateCamera(result);
        ValidateAdaptiveQuality(result);
        ValidateConnectionHealth(result);
        ValidateUiFonts(result);

        foreach (string warning in result.Warnings)
            Debug.LogWarning("DIGITAL_TWIN_VALIDATION: " + warning);
        foreach (string error in result.Errors)
            Debug.LogError("DIGITAL_TWIN_VALIDATION: " + error);

        Debug.Log(
            $"DIGITAL_TWIN_VALIDATION_RESULT sensors={sources.Length} " +
            $"errors={result.Errors.Count} warnings={result.Warnings.Count}");
        return result;
    }

    private static void ValidateRoots(GameObject[] roots, ValidationResult result)
    {
        string[] expected = { "Scene", "Digital Twin" };
        string[] actual = roots.Select(root => root.name).ToArray();
        if (!actual.OrderBy(value => value).SequenceEqual(expected.OrderBy(value => value)))
            result.Errors.Add("The scene must have only the Scene and Digital Twin roots.");
    }

    private static void ValidateMissingScripts(GameObject[] roots, ValidationResult result)
    {
        int missing = roots.Sum(CountMissingScripts);
        if (missing > 0)
            result.Errors.Add($"The scene contains {missing} missing script component(s).");
    }

    private static void ValidateUiFonts(ValidationResult result)
    {
        TMP_FontAsset medium = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/UI/Fonts/Rajdhani-Medium SDF.asset");
        TMP_FontAsset semiBold = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
            "Assets/UI/Fonts/Rajdhani-SemiBold SDF.asset");
        if (medium == null || semiBold == null)
        {
            result.Errors.Add("Rajdhani Medium or SemiBold production font is missing.");
            return;
        }

        foreach (TMP_Text text in UnityEngine.Object.FindObjectsByType<TMP_Text>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (text.font != medium && text.font != semiBold)
                result.Errors.Add($"UI text '{GetPath(text.transform)}' is not using a production Rajdhani font.");

            if (HasAncestor(text.transform, "Bottom controls") && text.font != medium)
                result.Errors.Add($"Bottom-control text '{GetPath(text.transform)}' must use Rajdhani Medium.");
        }

        GameObject cardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Prefabs/Performance Stat Card.prefab");
        if (cardPrefab == null)
        {
            result.Errors.Add("Performance Stat Card prefab is missing.");
            return;
        }

        foreach (TMP_Text text in cardPrefab.GetComponentsInChildren<TMP_Text>(true))
        {
            bool reading = text.name == "Value text" || text.name == "Unit text";
            TMP_FontAsset expected = reading ? semiBold : medium;
            if (text.font != expected)
                result.Errors.Add($"Card prefab text '{text.name}' has the wrong Rajdhani weight.");
        }

        PerformanceStatCardView card = cardPrefab.GetComponent<PerformanceStatCardView>();
        SerializedProperty casing = card == null
            ? null
            : new SerializedObject(card).FindProperty("useAccurateUnitCasing");
        if (casing == null || casing.boolValue)
            result.Errors.Add("Card prefab must default to uppercase engineering units.");
    }

    private static bool HasAncestor(Transform transform, string expectedName)
    {
        for (Transform current = transform.parent; current != null; current = current.parent)
        {
            if (current.name.Equals(expectedName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string GetPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }

    private static void ValidateSensors(
        PerformanceStatSource[] sources,
        ValidationResult result)
    {
        if (sources.Length == 0)
            result.Errors.Add("No telemetry sensors were found.");

        Transform sensorRoot = FindTransform("Telemetry Sources");
        if (sensorRoot == null)
            result.Errors.Add("Telemetry Sources hierarchy group is missing.");

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (PerformanceStatSource source in sources)
        {
            if (sensorRoot != null &&
                (!source.transform.IsChildOf(sensorRoot) || source.transform.parent == sensorRoot))
            {
                result.Errors.Add(
                    $"Sensor '{source.name}' is outside a functional Telemetry Sources group.");
            }

            if (string.IsNullOrWhiteSpace(source.StatId))
                result.Errors.Add($"Sensor '{source.name}' has no telemetry ID.");
            else if (!ids.Add(source.StatId))
                result.Errors.Add($"Duplicate telemetry ID '{source.StatId}'.");

            SerializedObject data = new(source);
            TelemetryLimitMode mode = (TelemetryLimitMode)data.FindProperty("limitMode").intValue;
            float warningBelow = data.FindProperty("warningBelow").floatValue;
            float criticalBelow = data.FindProperty("criticalBelow").floatValue;
            float warningAbove = data.FindProperty("warningAbove").floatValue;
            float criticalAbove = data.FindProperty("criticalAbove").floatValue;

            if ((mode == TelemetryLimitMode.HighOnly || mode == TelemetryLimitMode.OutsideRange) &&
                criticalAbove < warningAbove)
            {
                result.Errors.Add($"Sensor '{source.StatId}' has critical-high below warning-high.");
            }

            if ((mode == TelemetryLimitMode.LowOnly || mode == TelemetryLimitMode.OutsideRange) &&
                criticalBelow > warningBelow)
            {
                result.Errors.Add($"Sensor '{source.StatId}' has critical-low above warning-low.");
            }
        }
    }

    private static void ValidateMachineConfiguration(
        PerformanceStatSource[] sources,
        ValidationResult result)
    {
        DigitalTwinMachineConfiguration configuration;
        try
        {
            configuration = DigitalTwinMachineConfigurationLoader.LoadDefault();
        }
        catch (Exception exception)
        {
            result.Errors.Add(exception.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(configuration.machineId))
            result.Errors.Add("The default machine configuration has no stable machine ID.");
        if (configuration.sensors == null || configuration.sensors.Count == 0)
        {
            result.Errors.Add(
                $"Machine configuration '{configuration.machineId}' has no sensors.");
            return;
        }

        HashSet<string> configuredIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (MachineSensorDefinition sensor in configuration.sensors)
        {
            if (string.IsNullOrWhiteSpace(sensor.id))
                result.Errors.Add("A configured machine sensor has no stable ID.");
            else if (!configuredIds.Add(sensor.id))
                result.Errors.Add($"Machine configuration duplicates sensor ID '{sensor.id}'.");
            if (string.IsNullOrWhiteSpace(sensor.category))
                result.Errors.Add($"Configured sensor '{sensor.id}' has no hierarchy category.");
            if (sensor.staleAfterSeconds <= 0f)
                result.Errors.Add($"Configured sensor '{sensor.id}' has an invalid stale timeout.");
        }

        HashSet<string> sceneIds = new(
            sources.Where(source => !string.IsNullOrWhiteSpace(source.StatId))
                .Select(source => source.StatId),
            StringComparer.OrdinalIgnoreCase);
        if (!configuredIds.SetEquals(sceneIds))
        {
            result.Errors.Add(
                $"Scene sensors do not match default machine configuration " +
                $"'{configuration.machineId}'.");
        }

        HashSet<string> groupIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (MachineEquipmentGroupDefinition group in configuration.equipmentGroups)
        {
            if (string.IsNullOrWhiteSpace(group.groupId) || !groupIds.Add(group.groupId))
                result.Errors.Add("Machine equipment groups require unique stable IDs.");
            foreach (string memberId in group.memberSensorIds)
            {
                if (!configuredIds.Contains(memberId))
                {
                    result.Errors.Add(
                        $"Equipment group '{group.groupId}' references unknown sensor '{memberId}'.");
                }
            }
        }

        try
        {
            DigitalTwinMachineConfigurationLoader.CreateSceneConfigurator(configuration);
        }
        catch (Exception exception)
        {
            result.Errors.Add(exception.Message);
        }
    }

    private static void ValidateEnvironmentModelSize(ValidationResult result)
    {
        const long maximumBytes = 10L * 1024L * 1024L;
        string[] candidates =
        {
            "Assets/Models/Construction Site.fbx",
            "Assets/Models/Site Environment.fbx"
        };
        string assetPath = candidates.FirstOrDefault(File.Exists);
        if (assetPath == null)
        {
            result.Warnings.Add("No site-environment FBX was found at the expected model paths.");
            return;
        }

        long bytes = new FileInfo(assetPath).Length;
        if (bytes > maximumBytes)
        {
            result.Errors.Add(
                $"Site environment model is {bytes / (1024f * 1024f):0.0} MB; " +
                "the project limit is 10 MB.");
        }
    }

    private static void ValidateRegistry(
        PerformanceStatSource[] sources,
        ValidationResult result)
    {
        TelemetryRegistry registry = UnityEngine.Object.FindFirstObjectByType<TelemetryRegistry>(
            FindObjectsInactive.Include);
        if (registry == null)
        {
            result.Errors.Add("Telemetry Registry is missing.");
            return;
        }

        registry.RebuildIndex();
        HashSet<PerformanceStatSource> registered = new(registry.Sources.Where(source => source != null));
        if (registered.Count != sources.Length || sources.Any(source => !registered.Contains(source)))
            result.Errors.Add("Telemetry Registry does not contain every scene sensor exactly once.");
    }

    private static void ValidateManagerSources<T>(
        PerformanceStatSource[] sources,
        ValidationResult result) where T : MonoBehaviour
    {
        foreach (T manager in UnityEngine.Object.FindObjectsByType<T>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            SerializedObject serializedManager = new(manager);
            SerializedProperty selectionMode = serializedManager.FindProperty("sourceSelectionMode");
            if (selectionMode != null &&
                selectionMode.enumValueIndex == (int)TelemetrySourceSelectionMode.SceneDiscovery)
            {
                continue;
            }

            SerializedProperty array = serializedManager.FindProperty("sources");
            HashSet<PerformanceStatSource> assigned = new();
            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue is PerformanceStatSource source)
                    assigned.Add(source);
            }

            if (assigned.Count != sources.Length || sources.Any(source => !assigned.Contains(source)))
                result.Errors.Add($"'{manager.name}' does not reference every sensor exactly once.");
        }
    }

    private static void ValidateTelemetryArchitecture(
        PerformanceStatSource[] sources,
        ValidationResult result)
    {
        TelemetryJsonIngestor ingestor =
            UnityEngine.Object.FindFirstObjectByType<TelemetryJsonIngestor>(
                FindObjectsInactive.Include);
        if (ingestor == null)
            result.Errors.Add("The generic telemetry JSON ingestor is missing.");

        TelemetryOperatingModeController modeController =
            UnityEngine.Object.FindFirstObjectByType<TelemetryOperatingModeController>(
                FindObjectsInactive.Include);
        if (modeController == null)
        {
            result.Errors.Add("Telemetry Operating Mode Controller is missing.");
        }
        else
        {
            SerializedProperty provider = new SerializedObject(modeController)
                .FindProperty("simulator");
            if (provider?.objectReferenceValue is MonoBehaviour behaviour &&
                behaviour is not ITelemetrySimulationProvider)
            {
                result.Errors.Add(
                    $"Simulation provider '{behaviour.name}' does not implement " +
                    "ITelemetrySimulationProvider.");
            }
        }

    }

    private static void ValidateParticleController(ValidationResult result)
    {
        ParticleFlowController controller =
            UnityEngine.Object.FindFirstObjectByType<ParticleFlowController>(FindObjectsInactive.Include);
        if (controller == null)
        {
            result.Errors.Add("Particle Flow Controller is missing.");
            return;
        }

        SerializedObject data = new(controller);
        if (data.FindProperty("particleSystems").arraySize == 0 ||
            data.FindProperty("filteredFlows").arraySize == 0 ||
            data.FindProperty("rockFlows").arraySize == 0)
        {
            result.Errors.Add("Particle Flow Controller has incomplete managed targets.");
        }
    }

    private static void ValidateOverflowRails(ValidationResult result)
    {
        ValidateRailReferences<WideStatRailManager>(
            new[] { "innerLeftCardContainer", "innerRightCardContainer" }, result);
        ValidateRailReferences<PortraitStatRailManager>(
            new[] { "innerTopCardContainer", "innerBottomCardContainer" }, result);
    }

    private static void ValidateRailReferences<T>(string[] propertyNames, ValidationResult result)
        where T : MonoBehaviour
    {
        T manager = UnityEngine.Object.FindFirstObjectByType<T>(FindObjectsInactive.Include);
        if (manager == null)
            return;
        SerializedObject data = new(manager);
        foreach (string propertyName in propertyNames)
        {
            SerializedProperty property = data.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue == null)
                result.Errors.Add($"'{manager.name}' is missing overflow rail '{propertyName}'.");
        }
    }

    private static void ValidateEquipmentGroups(
        PerformanceStatSource[] sources,
        ValidationResult result)
    {
        TelemetryEquipmentGroup[] groups = UnityEngine.Object.FindObjectsByType<TelemetryEquipmentGroup>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        HashSet<PerformanceStatSource> knownSources = new(sources);
        HashSet<PerformanceStatSource> assigned = new();

        foreach (TelemetryEquipmentGroup group in groups)
        {
            if (string.IsNullOrWhiteSpace(group.GroupId))
                result.Errors.Add($"Equipment group '{group.name}' has no stable group ID.");

            foreach (PerformanceStatSource member in group.Members)
            {
                if (member == null)
                {
                    result.Errors.Add($"Equipment group '{group.name}' has a missing member reference.");
                    continue;
                }
                if (!knownSources.Contains(member))
                    result.Errors.Add($"Equipment group '{group.name}' references an unregistered sensor.");
                if (!assigned.Add(member))
                    result.Errors.Add($"Sensor '{member.StatId}' belongs to more than one presentation group.");
                if (Vector3.Distance(member.WorldAnchor.position, group.WorldAnchor.position) >
                    group.MaximumMemberDistance)
                {
                    result.Warnings.Add(
                        $"Sensor '{member.StatId}' is farther from '{group.GroupId}' than its grouping limit.");
                }
            }
        }
    }

    private static void ValidateCamera(ValidationResult result)
    {
        OrbitCameraController camera =
            UnityEngine.Object.FindFirstObjectByType<OrbitCameraController>(FindObjectsInactive.Include);
        if (camera == null)
            result.Errors.Add("Orbit Camera Controller is missing.");
        else if (!camera.enablePinchZoom)
            result.Warnings.Add("Mobile pinch zoom is disabled.");
    }

    private static void ValidateAdaptiveQuality(ValidationResult result)
    {
        AdaptiveQualityController quality =
            UnityEngine.Object.FindFirstObjectByType<AdaptiveQualityController>(FindObjectsInactive.Include);
        if (quality == null)
            result.Warnings.Add("Adaptive Quality Controller is missing.");
        else if (!quality.enabled)
            result.Warnings.Add("Adaptive Quality Controller is disabled.");

        string[] names = QualitySettings.names;
        foreach (string required in new[] { "Mobile", "PC", "WebGL" })
        {
            if (!names.Any(name => string.Equals(name, required, StringComparison.OrdinalIgnoreCase)))
                result.Errors.Add($"Required quality level '{required}' is unavailable to WebGL.");
        }
    }

    private static void ValidateConnectionHealth(ValidationResult result)
    {
        ConnectionHealthMonitor monitor =
            UnityEngine.Object.FindFirstObjectByType<ConnectionHealthMonitor>(FindObjectsInactive.Include);
        if (monitor == null)
            result.Errors.Add("Connection Health Monitor is missing.");

        ConnectionHealthView[] views = UnityEngine.Object.FindObjectsByType<ConnectionHealthView>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        if (views.Length != 2)
            result.Errors.Add($"Expected wide and portrait connection-health views; found {views.Length}.");
    }

    private static Transform FindTransform(string objectName) =>
        UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None)
            .FirstOrDefault(transform => transform.name == objectName);

    private static int CountMissingScripts(GameObject gameObject)
    {
        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
        foreach (Transform child in gameObject.transform)
            count += CountMissingScripts(child.gameObject);
        return count;
    }

    private sealed class ValidationResult
    {
        public readonly List<string> Errors = new();
        public readonly List<string> Warnings = new();
    }
}
