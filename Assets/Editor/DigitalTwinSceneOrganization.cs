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

        Parent("Bunker", hopperAssembly);
        Parent("Supports", hopperAssembly);

        // Every model step below is scoped to a known parent. The source files
        // reuse Blender names such as "Motor" and "Spring" across parts, so a
        // scene-wide name search would pick the wrong object.
        OrganizeSeparatorModels(equipment, hopperAssembly);
        OrganizeVibratoryDrive(equipment, hopperAssembly, driveAssembly);
        OrganizeConveyors(equipment);

        // Sensor categories and anchors are owned by the selected machine
        // configuration. Hierarchy cleanup must preserve that machine-defined layout.

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

    // -----------------------------------------------------------------------
    // Separator model (Blender object name -> what the part is)
    // -----------------------------------------------------------------------

    private static readonly (string Blender, string Name)[] SeparatorParts =
    {
        ("Cylinder.050", "Spring Seat - Upper"),
        ("Cylinder.051", "Spring Seat - Lower"),
        ("Cylinder.058", "Base Housing"),
        ("Right_O.002", "Separator Drum"),
        ("Right_O.005", "Screen Decks and Discharge"),
        ("Spring", "Isolation Springs")
    };

    private static readonly (string Blender, string Name)[] DriveMotorParts =
    {
        ("Motor", "Lower Gusset 01"), ("Motor.001", "Lower Gusset 02"), ("Motor.002", "Lower Gusset 03"),
        ("Motor.003", "Lower Gusset 04"), ("Motor.004", "Lower Gusset 05"), ("Motor.005", "Lower Gusset 06"),
        ("Motor.006", "Lower Gusset 07"), ("Motor.007", "Lower Gusset 08"),
        ("Motor.008", "Upper Gusset 01"), ("Motor.009", "Upper Gusset 02"), ("Motor.010", "Upper Gusset 03"),
        ("Motor.011", "Upper Gusset 04"), ("Motor.012", "Upper Gusset 05"), ("Motor.013", "Upper Gusset 06"),
        ("Motor.014", "Upper Gusset 07"), ("Motor.015", "Upper Gusset 08"),
        ("Motor.016", "Lower End Plate"),
        ("Motor.017", "Upper End Plate"),
        ("Motor.020", "Motor Body"),
        ("Cylinder.052", "Drive Shaft"),
        ("Cylinder.053", "Shaft Collar - Top"),
        ("Cylinder.055", "Shaft Collar - Middle"),
        ("Cylinder.054", "Rotor Core"),
        ("Cylinder.056", "Mounting Flange"),
        ("Cube.022", "Stator"),
        ("Cube.021", "Finned Housing")
    };

    // The filtered line runs under the Filtered Flow Outlet (+Z), the coarse
    // line under the Coarse Flow Outlet (-Z). Inner faces the machine.
    private static readonly (string Blender, string Name)[] FilteredConveyorParts =
    {
        ("Cylinder.057", "Belt"), ("Cylinder.059", "Idler Rollers"),
        ("Cube.025", "Side Guard - Inner"), ("Cube.024", "Side Guard - Outer"),
        ("Cube.028", "Leg Frame - Inner"), ("Cube.027", "Leg Frame - Outer"),
        ("Cube.029", "Cross Members"),
        ("Motor.018", "Foot Plates - Inner"), ("Motor.019", "Foot Plates - Outer")
    };

    private static readonly (string Blender, string Name)[] CoarseConveyorParts =
    {
        ("Cylinder.037", "Belt"), ("Cylinder.041", "Idler Rollers"),
        ("Cube.034", "Side Guard - Inner"), ("Cube.030", "Side Guard - Outer"),
        ("Cube.035", "Leg Frame - Inner"), ("Cube.031", "Leg Frame - Outer"),
        ("Cube.033", "Cross Members"),
        ("Motor.022", "Foot Plates - Inner"), ("Motor.021", "Foot Plates - Outer")
    };

    private static void OrganizeSeparatorModels(Transform equipment, Transform hopperAssembly)
    {
        Transform full = AdoptChild(hopperAssembly, equipment, "full model of hopper", "Full Model");
        Transform cut = AdoptChild(hopperAssembly, equipment, "Cut detailed hopper", "Cross-Section Model");

        foreach ((string blender, string name) in SeparatorParts)
        {
            RenameScoped(full, blender, name);
            RenameScoped(cut, blender, name);
        }

        MirrorSeparatorVibration(cut, full);
    }

    private static void OrganizeVibratoryDrive(Transform equipment, Transform hopperAssembly, Transform drive)
    {
        Transform motor = AdoptChild(drive, equipment, "motor", "Drive Motor");

        foreach ((string blender, string name) in DriveMotorParts)
            RenameScoped(motor, blender, name);

        // The source file puts the rotors inside the cutaway model, which is
        // switched off in the Full Body view. Grouping them with the drive
        // keeps them present in every view - hidden inside the closed drum
        // until it is cut away - so their mechanism can still be outlined.
        Transform cut = hopperAssembly.Find("Cross-Section Model");

        if (cut != null)
        {
            AdoptChild(drive, cut, "Rotor top", "Upper Rotor");
            AdoptChild(drive, cut, "Rotor bottom", "Lower Rotor");
        }
    }

    private static void OrganizeConveyors(Transform equipment)
    {
        Transform conveyors = GetOrCreateRenamedChild(equipment, "Conveyors", "Conveyor");
        Transform filtered = GetOrCreateChild(conveyors, "Filtered Material Conveyor");
        Transform coarse = GetOrCreateChild(conveyors, "Coarse Material Conveyor");

        foreach ((string blender, string name) in FilteredConveyorParts)
            AdoptChild(filtered, conveyors, blender, name);

        foreach ((string blender, string name) in CoarseConveyorParts)
            AdoptChild(coarse, conveyors, blender, name);
    }

    /// <summary>
    /// The cutaway model's separator parts vibrate; the full model's did not,
    /// so the drum stood still in the Full Body view. Copies each vibration
    /// setting onto the matching full-model part.
    /// </summary>
    private static void MirrorSeparatorVibration(Transform cut, Transform full)
    {
        if (cut == null || full == null)
            return;

        foreach (Transform cutPart in cut)
        {
            SeparatorVibration source = cutPart.GetComponent<SeparatorVibration>();
            Transform fullPart = full.Find(cutPart.name);

            if (source == null || fullPart == null || fullPart.GetComponent<SeparatorVibration>() != null)
                continue;

            SeparatorVibration copy = fullPart.gameObject.AddComponent<SeparatorVibration>();
            EditorUtility.CopySerialized(source, copy);
        }
    }

    /// <summary>
    /// Moves a child found under <paramref name="searchRoot"/> by its old name
    /// into <paramref name="parent"/> under its new name. Re-running finds it
    /// already in place.
    /// </summary>
    private static Transform AdoptChild(Transform parent, Transform searchRoot, string oldName, string newName)
    {
        if (parent == null)
            return null;

        Transform target = parent.Find(newName) ??
                           (searchRoot != null ? searchRoot.Find(oldName) : null) ??
                           parent.Find(oldName);

        if (target == null)
            return null;

        Rename(target, newName);

        if (target.parent != parent)
            target.SetParent(parent, true);

        return target;
    }

    private static void RenameScoped(Transform parent, string oldName, string newName)
    {
        if (parent == null)
            return;

        Transform target = parent.Find(newName) ?? parent.Find(oldName);

        if (target != null)
            Rename(target, newName);
    }

    private static void Rename(Transform target, string newName)
    {
        if (target.name == newName)
            return;

        target.name = newName;

        // Renaming inside a prefab instance is an override; recording it is
        // what makes it survive the scene being saved and reloaded.
        if (PrefabUtility.IsPartOfPrefabInstance(target))
            PrefabUtility.RecordPrefabInstancePropertyModifications(target.gameObject);
    }

    private static int CountMissingScripts(GameObject gameObject)
    {
        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
        foreach (Transform child in gameObject.transform)
            count += CountMissingScripts(child.gameObject);
        return count;
    }
}
