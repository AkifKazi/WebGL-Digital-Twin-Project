using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DigitalTwinSceneOrganization
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform sceneRoot = GetOrCreate("Scene");
        Transform cameras = GetOrCreate("01 - Cameras", sceneRoot);
        Transform lighting = GetOrCreate("02 - Lighting", sceneRoot);
        Transform equipment = GetOrCreate("03 - Equipment", sceneRoot);
        Transform environment = GetOrCreate("04 - Environment", sceneRoot);
        Transform effects = GetOrCreate("05 - Effects", sceneRoot);
        Transform userInterface = GetOrCreate("06 - User Interface", sceneRoot);

        Transform digitalTwin = GetOrCreate("Digital Twin");
        Transform sensors = GetOrCreateRenamedChild(
            digitalTwin,
            "Telemetry Sources",
            "Digital Twin Sensors");
        Transform runtime = GetOrCreate("Digital Twin Runtime", digitalTwin);
        GetOrCreateRenamedChild(
            digitalTwin,
            "Presentation Groups",
            "Telemetry Presentation Groups");

        Parent("Main Camera", cameras);
        RenameAndParent("Veiwer", "Camera Focus Target", cameras);
        Parent("Directional Light", lighting);
        Parent("Global Volume", lighting);

        RenameAndParent("Canvas", "Main UI Canvas", userInterface);
        Parent("Main UI Canvas", userInterface);
        Parent("EventSystem", userInterface);
        RenameAndParent("Performance Display", "FPS Diagnostics", userInterface);
        Parent("FPS Diagnostics", userInterface);

        RenameAndParent("Particle_system", "Material Flow Effects", effects);
        Parent("Material Flow Effects", effects);
        RenameAndParent("Hopper - Particles Collider", "Material Flow Collider", effects);
        RenameAndParent("background new 2", "Site Environment", environment);
        RenameAndParent("Plane (1)", "Ground Plane", environment);
        RenameAndParent("heap of rock", "Material Stockpile", environment);

        Transform hopperAssembly = GetOrCreateRenamedChild(
            equipment,
            "Hopper Assembly",
            "Bunker Components");
        Transform driveAssembly = GetOrCreateRenamedChild(
            equipment,
            "Vibratory Drive",
            "Motor Components");
        Transform conveyorAssembly = GetOrCreateRenamedChild(
            equipment,
            "Conveyor Assembly",
            "Conveyor Belts");

        RenameAndParent("Rotor top", "Upper Rotor", driveAssembly);
        RenameAndParent("Rotor bottom", "Lower Rotor", driveAssembly);
        Parent("Upper Rotor", driveAssembly);
        Parent("Lower Rotor", driveAssembly);
        Parent("Motor", driveAssembly);

        RenameAndParent("Akif's final hopper copy", "Full Model", hopperAssembly);
        RenameAndParent("Akif's final cut hopper copy", "Cross-Section Model", hopperAssembly);
        RenameAndParent("Hopper - Full Model", "Full Model", hopperAssembly);
        RenameAndParent("Hopper - Cross Section Model", "Cross-Section Model", hopperAssembly);
        RenameAndParent("Akif's hopper collider", "Physics Collider", hopperAssembly);
        RenameAndParent("Hopper - Physics Collider", "Physics Collider", hopperAssembly);

        Parent("Bunker", hopperAssembly);
        Parent("Supports", hopperAssembly);
        Parent("Roller", conveyorAssembly);
        Parent("Roller 2", conveyorAssembly);
        Parent("Belt", conveyorAssembly);
        Parent("Belt 2", conveyorAssembly);
        Parent("conveyor", conveyorAssembly);

        foreach (PerformanceStatSource source in UnityEngine.Object.FindObjectsByType<PerformanceStatSource>(
                     FindObjectsInactive.Include))
        {
            source.transform.SetParent(
                GetOrCreateChild(sensors, GetTelemetryGroupName(source.StatId)),
                true);

        }

        AdaptiveQualityController quality = UnityEngine.Object.FindAnyObjectByType<AdaptiveQualityController>(
            FindObjectsInactive.Include);
        if (quality != null)
        {
            quality.transform.SetParent(runtime, true);
            quality.enabled = true;
        }

        RemoveDisabledColliderRenderers();
        OrganizeParticleChildren();
        ClarifyUserInterfaceNames();
        RemoveInactiveLegacyViewButton();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Digital twin hierarchy organization completed successfully.");
    }

    public static void Validate()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string[] rootNames = scene.GetRootGameObjects().Select(root => root.name).ToArray();
        string[] expectedRoots = { "Scene", "Digital Twin" };

        if (!rootNames.OrderBy(name => name).SequenceEqual(expectedRoots.OrderBy(name => name)))
        {
            throw new InvalidOperationException(
                $"Unexpected scene roots: {string.Join(", ", rootNames)}");
        }

        Transform sensorRoot = Find("Telemetry Sources");
        PerformanceStatSource[] sources = UnityEngine.Object.FindObjectsByType<PerformanceStatSource>(
            FindObjectsInactive.Include);

        if (sensorRoot == null || sources.Length == 0 ||
            sources.Any(source => !source.transform.IsChildOf(sensorRoot) ||
                                  source.transform.parent == sensorRoot))
        {
            throw new InvalidOperationException(
                "Every telemetry source must be inside a functional Telemetry Sources group.");
        }

        int missingScripts = scene.GetRootGameObjects().Sum(CountMissingScripts);

        if (missingScripts != 0)
        {
            throw new InvalidOperationException(
                $"Hierarchy validation failed: missingScripts={missingScripts}");
        }

        Debug.Log(
            $"HIERARCHY_VALIDATION roots={rootNames.Length} sensors={sources.Length} " +
            $"missingScripts={missingScripts}");
    }

    private static void OrganizeParticleChildren()
    {
        Transform particleRoot = Find("Material Flow Effects") ?? Find("Particle_system");
        if (particleRoot == null)
            return;

        particleRoot.name = "Material Flow Effects";

        List<FilteredParticleFlow> filteredFlows = particleRoot
            .GetComponentsInChildren<FilteredParticleFlow>(true)
            .OrderBy(flow => flow.transform.GetSiblingIndex())
            .ToList();

        for (int i = 0; i < filteredFlows.Count; i++)
            filteredFlows[i].gameObject.name = $"Filtered Particle Flow {i + 1:00}";

        ControlledRockFlow coarseFlow = particleRoot.GetComponentInChildren<ControlledRockFlow>(true);
        if (coarseFlow != null)
            coarseFlow.gameObject.name = "Coarse Rock Flow";

        RenameChild(particleRoot, "RockFlow_Center ", "Coarse Flow Center");
        RenameChild(particleRoot, "RockFlow_Outlet", "Coarse Flow Outlet");
        RenameChild(particleRoot, "RockFlow_Emitter", "Coarse Flow Emitter");
        RenameChild(particleRoot, "FilteredFlow_RightOutlet", "Filtered Flow Outlet");
        RenameChild(particleRoot, "SmallRock_SlopeStart_Left", "Slope Start - Left");
        RenameChild(particleRoot, "SmallRock_SlopeStart_right", "Slope Start - Right");
        RenameChild(particleRoot, "SmallRock_SlopeEnd_Left", "Slope End - Left");
        RenameChild(particleRoot, "SmallRock_SlopeEnd_Right", "Slope End - Right");
        RenameChild(particleRoot, "kill", "Particle Kill Volume 01");
        RenameChild(particleRoot, "kill (1)", "Particle Kill Volume 02");
        RenameChild(particleRoot, "Smoke Killer", "Smoke Kill Volume");
        RenameChild(particleRoot, "Smoke", "Dust Plume 01");
        RenameChild(particleRoot, "Smoke (1)", "Dust Plume 02");
    }

    private static void ClarifyUserInterfaceNames()
    {
        RenameAll("Portait stat rail manager", "Portrait Rail Manager");
        RenameAll("Portrait stat leader line manager", "Portrait Leader Line Manager");
        RenameAll("Portrait telemetry sequence", "Portrait Presentation Sequence");
        RenameAll("Wide stat rail manager", "Wide Rail Manager");
        RenameAll("Wide stat leader line manager", "Wide Leader Line Manager");
        RenameAll("Telemetry sequence controller", "Wide Presentation Sequence");
        RenameAll("hopper fade (1)", "Hopper View Controller");
        RenameAll("Movement", "Camera Zoom Controls");
        RenameAll("Button_plus", "Zoom In Button");
        RenameAll("Button_minus", "Zoom Out Button");
        RenameAll("View mode control", "Hopper View Mode Control");
        RenameAll("Exterior button", "Full Model Button");
        RenameAll("Section button", "Cross-Section Button");
        RenameAll("Leader line layer", "Runtime Leader Line Layer");
        RenameAll("Leader layer", "Runtime Leader Line Layer");
    }

    private static void RemoveInactiveLegacyViewButton()
    {
        Transform wideLayout = Find("Wide layout");
        if (wideLayout == null)
            return;

        Transform legacyButton = wideLayout
            .GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate.name == "Button" && !candidate.gameObject.activeSelf);
        if (legacyButton != null)
            UnityEngine.Object.DestroyImmediate(legacyButton.gameObject);
    }

    private static void RemoveDisabledColliderRenderers()
    {
        foreach (MeshCollider collider in UnityEngine.Object.FindObjectsByType<MeshCollider>(
                     FindObjectsInactive.Include))
        {
            MeshRenderer renderer = collider.GetComponent<MeshRenderer>();
            if (renderer != null && !renderer.enabled)
                UnityEngine.Object.DestroyImmediate(renderer);
        }
    }

    private static Transform GetOrCreateRenamedChild(
        Transform parent,
        string name,
        string previousName)
    {
        Transform existing = parent.Find(name) ?? parent.Find(previousName);
        if (existing == null)
        {
            existing = new GameObject(name).transform;
            existing.SetParent(parent, false);
        }

        existing.name = name;
        if (existing.parent != parent)
            existing.SetParent(parent, true);
        return existing;
    }

    private static Transform GetOrCreateChild(Transform parent, string name)
    {
        Transform existing = parent.Find(name);
        if (existing != null)
            return existing;

        existing = new GameObject(name).transform;
        existing.SetParent(parent, false);
        return existing;
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

    private static void RenameAll(string oldName, string newName)
    {
        foreach (Transform candidate in UnityEngine.Object.FindObjectsByType<Transform>(
                     FindObjectsInactive.Include))
        {
            if (candidate.gameObject.scene == SceneManager.GetActiveScene() &&
                string.Equals(candidate.name, oldName, StringComparison.Ordinal))
            {
                candidate.name = newName;
            }
        }
    }

    private static Transform GetOrCreate(string name, Transform parent = null)
    {
        Transform existing = Find(name);
        if (existing == null)
            existing = new GameObject(name).transform;

        if (parent != null && existing.parent != parent)
            existing.SetParent(parent, true);

        return existing;
    }

    private static Transform Find(string name)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return UnityEngine.Object
            .FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate =>
                candidate.gameObject.scene == activeScene &&
                string.Equals(candidate.name, name, StringComparison.Ordinal));
    }

    private static void Parent(string name, Transform parent)
    {
        Transform target = FindTopLevel(name) ?? Find(name);
        if (target != null && target != parent)
            target.SetParent(parent, true);
    }

    private static void RenameAndParent(string oldName, string newName, Transform parent)
    {
        Transform target = FindTopLevel(oldName) ?? FindTopLevel(newName) ??
                           Find(oldName) ?? Find(newName);
        if (target == null)
            return;

        target.name = newName;
        target.SetParent(parent, true);
    }

    private static Transform FindTopLevel(string name)
    {
        Scene activeScene = SceneManager.GetActiveScene();
        return activeScene.GetRootGameObjects()
            .Select(root => root.transform)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.name, name, StringComparison.Ordinal));
    }

    private static void RenameChild(Transform parent, string oldName, string newName)
    {
        Transform target = parent.Cast<Transform>()
            .FirstOrDefault(child => child.name == oldName || child.name == newName);
        if (target != null)
            target.name = newName;
    }

    private static int CountMissingScripts(GameObject gameObject)
    {
        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
        foreach (Transform child in gameObject.transform)
            count += CountMissingScripts(child.gameObject);
        return count;
    }
}
