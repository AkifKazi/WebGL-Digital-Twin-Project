using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

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
            FindObjectsInactive.Include);

        ValidateSensors(sources, result);
        ValidateMachineConfiguration(sources, result);
        ValidateEnvironmentModelSize(result);
        ValidateRegistry(sources, result);
        ValidateTelemetryArchitecture(sources, result);
        ValidateManagerSources<StatRailManager>(sources, result);
        ValidateOverflowRails(result);
        ValidatePortraitPagination(result);
        ValidateSharedMachineViewController(result);
        ValidateNoTelemetryGrouping(result);
        ValidateTelemetryVisibility(result);
        ValidateLeaderLineObstacles(result);
        ValidateParticleController(result);
        ValidateCamera(result);
        ValidateAdaptiveQuality(result);
        ValidateConnectionHealth(result);
        ValidateDetailPanel(result);
        ValidateControlStates(result);
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
                     FindObjectsInactive.Include))
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

        ValidateMachineParts(configuration, configuredIds, result);

        try
        {
            DigitalTwinMachineConfigurationLoader.CreateSceneConfigurator(configuration);
        }
        catch (Exception exception)
        {
            result.Errors.Add(exception.Message);
        }
    }

    /// <summary>
    /// A card's highlight comes from the machine parts that list its sensor, so
    /// a mistyped sensor ID or object path silently leaves a card outlining
    /// nothing.
    /// </summary>
    private static void ValidateMachineParts(
        DigitalTwinMachineConfiguration configuration,
        HashSet<string> configuredIds,
        ValidationResult result)
    {
        Transform equipment = FindTransform("03 - Equipment");
        HashSet<string> linkedIds = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> linkedCategories = new(StringComparer.OrdinalIgnoreCase);

        foreach (MachinePartDefinition part in configuration.machineParts ?? new List<MachinePartDefinition>())
        {
            bool listsIds = part.sensorIds != null && part.sensorIds.Count > 0;

            foreach (string sensorId in part.sensorIds ?? new List<string>())
            {
                if (configuredIds.Contains(sensorId))
                    linkedIds.Add(sensorId);
                else
                    result.Errors.Add($"Machine part '{part.id}' lists unknown sensor '{sensorId}'.");
            }

            // Categories only bind when a part lists no sensor IDs.
            if (!listsIds && part.sensorCategories != null)
                linkedCategories.UnionWith(part.sensorCategories);

            if (equipment == null)
                continue;

            foreach (string path in part.objectPaths ?? new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(path) && equipment.Find(path.Trim()) == null)
                    result.Errors.Add($"Machine part '{part.id}' path '03 - Equipment/{path}' does not exist.");
            }
        }

        foreach (MachineSensorDefinition sensor in configuration.sensors)
        {
            if (sensor.enabled && !linkedIds.Contains(sensor.id) && !linkedCategories.Contains(sensor.category))
                result.Warnings.Add($"Card '{sensor.label}' ({sensor.id}) highlights no machine part.");
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
        TelemetryRegistry[] registries = UnityEngine.Object.FindObjectsByType<TelemetryRegistry>(
            FindObjectsInactive.Include);
        if (registries.Length == 0)
        {
            result.Errors.Add("Telemetry Registry is missing.");
            return;
        }
        if (registries.Length != 1)
            result.Errors.Add($"Exactly one Telemetry Registry is required; found {registries.Length}.");

        TelemetryRegistry registry = registries[0];

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
                     FindObjectsInactive.Include))
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
            UnityEngine.Object.FindAnyObjectByType<TelemetryJsonIngestor>(
                FindObjectsInactive.Include);
        if (ingestor == null)
            result.Errors.Add("The generic telemetry JSON ingestor is missing.");

        TelemetryOperatingModeController modeController =
            UnityEngine.Object.FindAnyObjectByType<TelemetryOperatingModeController>(
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
            UnityEngine.Object.FindAnyObjectByType<ParticleFlowController>(FindObjectsInactive.Include);
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
        string[] innerRails = { "innerFirstCardContainer", "innerSecondCardContainer" };
        ValidateRailReferences(StatRailManagers.Wide(FindObjectsInactive.Include), innerRails, result);
        ValidateRailReferences(StatRailManagers.Portrait(FindObjectsInactive.Include), innerRails, result);
    }

    private static void ValidatePortraitPagination(ValidationResult result)
    {
        PortraitTelemetryPager[] pagers = UnityEngine.Object.FindObjectsByType<PortraitTelemetryPager>(
            FindObjectsInactive.Include);
        if (pagers.Length != 2)
        {
            result.Errors.Add($"Expected portrait and wide telemetry pagers; found {pagers.Length}.");
            return;
        }

        foreach (PortraitTelemetryPager pager in pagers)
        {
            string path = GetPath(pager.transform);
            if (pager.RailManager == null || pager.Visibility == null ||
                pager.PreviousButton == null || pager.NextButton == null ||
                pager.PreviousIcon == null || pager.NextIcon == null)
            {
                result.Errors.Add($"Telemetry pager '{path}' has incomplete references.");
            }
            RectTransform row = pager.GetComponent<RectTransform>();
            if (pager.RailManager != null && pager.RailManager.AlwaysRows)
            {
                RectTransform bottomControls = row?.parent as RectTransform;
                RectTransform viewControls = bottomControls?
                    .GetComponentsInChildren<ViewModeSegmentedControl>(true)
                    .Select(control => control.transform as RectTransform)
                    .FirstOrDefault();
                if (row == null || viewControls == null ||
                    !Approximately(row.sizeDelta, viewControls.sizeDelta) ||
                    !Approximately(row.localScale, viewControls.localScale))
                {
                    result.Errors.Add(
                        $"Portrait telemetry pager '{path}' must match its view-mode control.");
                }

                HorizontalLayoutGroup rowLayout = pager.GetComponent<HorizontalLayoutGroup>();
                if (rowLayout == null || !Mathf.Approximately(rowLayout.spacing, 26f) ||
                    !rowLayout.childControlWidth)
                {
                    result.Errors.Add(
                        $"Portrait pager '{path}' must retain 26 spacing and flexible arrows.");
                }
            }
            else if (pager.PaginationCanvasGroup == null || !IsInTopBar(row) ||
                     !Mathf.Approximately(row.anchorMin.x, 1f) ||
                     !Mathf.Approximately(row.pivot.x, 1f))
            {
                result.Errors.Add(
                    $"Wide pager '{path}' must be a standalone, right-anchored top-bar control.");
            }

            ValidatePaginationButton(pager.PreviousButton, "previous", result);
            ValidatePaginationButton(pager.NextButton, "next", result);
        }
    }

    private static void ValidateSharedMachineViewController(ValidationResult result)
    {
        HybridHopperClipController[] controllers = UnityEngine.Object
            .FindObjectsByType<HybridHopperClipController>(
                FindObjectsInactive.Include);
        if (controllers.Length != 1)
        {
            result.Errors.Add(
                $"Wide and portrait UI must share one machine view controller; found {controllers.Length}.");
            return;
        }

        HybridHopperClipController shared = controllers[0];
        if (!shared.enabled)
            result.Errors.Add("The shared machine view controller is disabled.");

        foreach (ViewModeSegmentedControl control in UnityEngine.Object
                     .FindObjectsByType<ViewModeSegmentedControl>(
                         FindObjectsInactive.Include))
        {
            SerializedProperty reference = new SerializedObject(control).FindProperty("hopperController");
            if (reference?.objectReferenceValue != shared)
                result.Errors.Add($"View control '{GetPath(control.transform)}' is not using the shared controller.");
        }
    }

    /// <summary>
    /// Directly in the top bar, or in the full-size wrapper the service view
    /// fades telemetry controls through.
    /// </summary>
    private static bool IsInTopBar(RectTransform rect)
    {
        Transform parent = rect != null ? rect.parent : null;
        if (parent == null)
            return false;
        if (parent.name == "Top bar")
            return true;
        return parent.name == "Telemetry Top Controls" &&
               parent.parent != null && parent.parent.name == "Top bar" &&
               parent is RectTransform wrapper &&
               wrapper.anchorMin == Vector2.zero && wrapper.anchorMax == Vector2.one;
    }

    private static void ValidateRailReferences(
        StatRailManager manager,
        string[] propertyNames,
        ValidationResult result)
    {
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

    /// <summary>
    /// Each layout has one Telemetry Visibility with its regions, and
    /// everything that switches, hides or reveals telemetry in the layout
    /// points at it rather than at another layout's or at the machine.
    /// </summary>
    private static void ValidateTelemetryVisibility(ValidationResult result)
    {
        foreach (string layoutName in new[] { "Wide layout", "Portrait layout" })
        {
            Transform layout = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .FirstOrDefault(candidate => candidate.name == layoutName);
            if (layout == null)
                continue;

            TelemetryVisibility[] found = layout.GetComponentsInChildren<TelemetryVisibility>(true);
            if (found.Length != 1)
            {
                result.Errors.Add($"'{layoutName}' needs exactly one Telemetry Visibility; found {found.Length}.");
                continue;
            }

            TelemetryVisibility visibility = found[0];
            SerializedObject data = new(visibility);
            SerializedProperty regions = data.FindProperty("telemetryRegions");
            SerializedProperty serviceGroups = data.FindProperty("serviceGroups");
            if (regions.arraySize == 0 || Enumerable.Range(0, regions.arraySize)
                    .Any(i => regions.GetArrayElementAtIndex(i).objectReferenceValue == null))
                result.Errors.Add($"'{layoutName}' Telemetry Visibility has missing telemetry regions.");
            if (serviceGroups.arraySize == 0 || Enumerable.Range(0, serviceGroups.arraySize)
                    .Any(i => serviceGroups.GetArrayElementAtIndex(i).objectReferenceValue == null))
                result.Errors.Add($"'{layoutName}' Telemetry Visibility has missing service groups.");
            if (!(data.FindProperty("inspectionView").objectReferenceValue is IInspectionView))
                result.Errors.Add($"'{layoutName}' Telemetry Visibility has no inspection view.");

            CheckPointsAt<WideServiceView>(layout, "visibility", visibility, result);
            CheckPointsAt<PortraitServiceView>(layout, "visibility", visibility, result);
            CheckPointsAt<DigitalTwinModeSwitch>(layout, "controller", visibility, result);
            CheckPointsAt<ServicePinLayer>(layout, "visibility", visibility, result);
            CheckPointsAt<PortraitTelemetrySheetController>(layout, "visibility", visibility, result);
            CheckPointsAt<PortraitTelemetryPager>(layout, "visibility", visibility, result);
            CheckPointsAt<TelemetrySequenceController>(layout, "visibility", visibility, result);
        }
    }

    /// <summary>Leader lines fade over every translucent control: each one carries the marker.</summary>
    private static void ValidateLeaderLineObstacles(ValidationResult result)
    {
        IEnumerable<Component> controls = UnityEngine.Object.FindObjectsByType<ViewModeSegmentedControl>(FindObjectsInactive.Include)
            .Cast<Component>()
            .Concat(UnityEngine.Object.FindObjectsByType<QualityToggleControl>(FindObjectsInactive.Include))
            .Concat(UnityEngine.Object.FindObjectsByType<ZoomControlAvailabilityView>(FindObjectsInactive.Include))
            .Concat(UnityEngine.Object.FindObjectsByType<DigitalTwinModeSwitch>(FindObjectsInactive.Include));
        foreach (Component control in controls)
        {
            if (control.GetComponent<LeaderLineObstacle>() == null)
                result.Errors.Add($"'{GetPath(control.transform)}' is not marked as a leader line obstacle.");
        }
    }

    private static void CheckPointsAt<T>(Transform layout, string field, TelemetryVisibility visibility, ValidationResult result)
        where T : Component
    {
        foreach (T component in layout.GetComponentsInChildren<T>(true))
        {
            if (new SerializedObject(component).FindProperty(field)?.objectReferenceValue != visibility)
                result.Errors.Add($"'{GetPath(component.transform)}' does not use its layout's Telemetry Visibility.");
        }
    }

    private static void ValidateNoTelemetryGrouping(ValidationResult result)
    {
        foreach (Transform candidate in UnityEngine.Object.FindObjectsByType<Transform>(
                     FindObjectsInactive.Include))
        {
            if (candidate.name == "Presentation Groups" ||
                candidate.name == "Telemetry Presentation Groups")
            {
                result.Errors.Add(
                    $"Legacy telemetry grouping object '{candidate.name}' must be removed.");
            }
        }
    }

    private static void ValidateCamera(ValidationResult result)
    {
        OrbitCameraController camera =
            UnityEngine.Object.FindAnyObjectByType<OrbitCameraController>(FindObjectsInactive.Include);
        if (camera == null)
            result.Errors.Add("Orbit Camera Controller is missing.");
        else if (!camera.enablePinchZoom)
            result.Warnings.Add("Mobile pinch zoom is disabled.");
    }

    private static void ValidateAdaptiveQuality(ValidationResult result)
    {
        AdaptiveQualityController quality =
            UnityEngine.Object.FindAnyObjectByType<AdaptiveQualityController>(FindObjectsInactive.Include);
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
            UnityEngine.Object.FindAnyObjectByType<ConnectionHealthMonitor>(FindObjectsInactive.Include);
        if (monitor == null)
            result.Errors.Add("Connection Health Monitor is missing.");

        ConnectionHealthView[] views = UnityEngine.Object.FindObjectsByType<ConnectionHealthView>(
            FindObjectsInactive.Include);
        if (views.Length > 0)
            result.Errors.Add(
                $"Connection-health top-bar UI should remain absent; found {views.Length} view(s).");
    }

    private static void ValidateDetailPanel(ValidationResult result)
    {
        TelemetryDetailPanelController[] controllers =
            UnityEngine.Object.FindObjectsByType<TelemetryDetailPanelController>(
                FindObjectsInactive.Include);
        if (controllers.Length == 0)
        {
            result.Warnings.Add(
                "The telemetry detail panel is not set up (Tools/Digital Twin/Set Up Telemetry Detail Panel).");
            return;
        }

        foreach (TelemetryDetailPanelController controller in controllers)
        {
            SerializedObject data = new(controller);
            if (data.FindProperty("railManager").objectReferenceValue == null)
                result.Errors.Add("The telemetry detail panel has no rail manager.");
            if (data.FindProperty("panelPrefab").objectReferenceValue == null)
                result.Errors.Add("The telemetry detail panel has no panel prefab.");
            if (data.FindProperty("historyProvider").objectReferenceValue is not ITelemetryHistoryProvider)
                result.Errors.Add("The telemetry detail panel needs a history provider (Telemetry History Recorder).");
            UnityEngine.Object insight = data.FindProperty("insightProvider").objectReferenceValue;
            if (insight != null && insight is not ITelemetryInsightProvider)
                result.Errors.Add("The detail panel's insight provider does not implement ITelemetryInsightProvider.");
        }
    }

    /// <summary>
    /// Outside clicks must be able to release a focused card, and every bottom
    /// control and pagination button keeps its idle, hover and pressed looks apart.
    /// </summary>
    private static void ValidateControlStates(ValidationResult result)
    {
        UnityEngine.EventSystems.EventSystem system =
            UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>(FindObjectsInactive.Include);
        if (system != null && !system.TryGetComponent(out TelemetryCardFocusReleaser _))
            result.Errors.Add("The EventSystem has no Telemetry Card Focus Releaser, so an outside click would leave a card focused.");

        foreach (Button button in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include))
        {
            if (!HasAncestor(button.transform, "Bottom controls") &&
                !HasAncestor(button.transform, "Telemetry Pagination Controls"))
                continue;

            string path = GetPath(button.transform);
            if (!button.TryGetComponent(out UIHoverSprite hover))
            {
                result.Warnings.Add($"Control '{path}' has no hover state (UI Hover Sprite).");
                continue;
            }
            if (new SerializedObject(hover).FindProperty("hoverSprite").objectReferenceValue == null)
                result.Errors.Add($"Control '{path}' has a hover state with no hover sprite.");

            SpriteState state = button.spriteState;
            if (button.transition == Selectable.Transition.SpriteSwap &&
                state.highlightedSprite != null && state.highlightedSprite == state.pressedSprite)
                result.Errors.Add($"Control '{path}' shows its pressed sprite on hover.");
        }
    }

    private static void ValidatePaginationButton(
        Button button,
        string direction,
        ValidationResult result)
    {
        if (button == null)
            return;

        Image image = button.targetGraphic as Image;
        string normalPath = image != null ? AssetDatabase.GetAssetPath(image.sprite) : string.Empty;
        string pressedPath = AssetDatabase.GetAssetPath(button.spriteState.pressedSprite);
        if (button.transition != Selectable.Transition.SpriteSwap ||
            normalPath != "Assets/UI/Sprites/Solid UI Fill.png" ||
            pressedPath != "Assets/UI/Sprites/Compact Button Background.png")
        {
            result.Errors.Add(
                $"The {direction}-page button must use the solid/compact interaction sprites.");
        }
    }

    private static bool Approximately(Vector2 first, Vector2 second) =>
        Mathf.Approximately(first.x, second.x) && Mathf.Approximately(first.y, second.y);

    private static bool Approximately(Vector3 first, Vector3 second) =>
        Mathf.Approximately(first.x, second.x) &&
        Mathf.Approximately(first.y, second.y) &&
        Mathf.Approximately(first.z, second.z);

    private static Transform FindTransform(string objectName) =>
        UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include)
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
