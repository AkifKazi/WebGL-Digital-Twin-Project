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
        OrganizeBunker(equipment, hopperAssembly);
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
    // Model parts
    //
    // Original: the object's name in the FBX exports made before 2026-09-14.
    // Blender:  the name the object carries in the .blend since then, which
    //           the next export brings into Unity.
    // Name:     the object's name in the Unity hierarchy.
    //
    // Unity links a scene object to an FBX part by the part's name, so an
    // export under new names leaves every mesh reference missing. Accepting
    // both names and re-linking missing meshes by name (RelinkMesh) is what
    // lets the organiser adopt either export.
    // -----------------------------------------------------------------------

    private const string FullModelPath = "Assets/Models/Separator - Full Model.fbx";
    private const string CrossSectionModelPath = "Assets/Models/Separator - Cross-Section Assembly.fbx";
    private const string BunkerModelPath = "Assets/Models/Hopper - Bunker and Supports.fbx";
    private const string DriveModelPath = "Assets/Models/Vibratory Drive.fbx";
    private const string ConveyorModelPath = "Assets/Models/Conveyor.fbx";

    private static readonly (string Original, string Blender, string Name)[] BunkerParts =
    {
        ("Cube.023", "Bunker", "Bunker"),
        ("Cube.032", "Support Frame", "Support Frame")
    };

    private static readonly (string Original, string Blender, string Name)[] SeparatorParts =
    {
        ("Cylinder.050", "Spring Seat - Upper", "Spring Seat - Upper"),
        ("Cylinder.051", "Spring Seat - Lower", "Spring Seat - Lower"),
        ("Cylinder.058", "Base Housing", "Base Housing"),
        ("Right_O.002", "Separator Drum", "Separator Drum"),
        ("Right_O.005", "Screen Decks and Discharge", "Screen Decks and Discharge"),
        ("Spring", "Isolation Springs", "Isolation Springs")
    };

    private static readonly (string Original, string Blender, string Name)[] DriveMotorParts =
    {
        ("Motor", "Lower Gusset 01", "Lower Gusset 01"), ("Motor.001", "Lower Gusset 02", "Lower Gusset 02"),
        ("Motor.002", "Lower Gusset 03", "Lower Gusset 03"), ("Motor.003", "Lower Gusset 04", "Lower Gusset 04"),
        ("Motor.004", "Lower Gusset 05", "Lower Gusset 05"), ("Motor.005", "Lower Gusset 06", "Lower Gusset 06"),
        ("Motor.006", "Lower Gusset 07", "Lower Gusset 07"), ("Motor.007", "Lower Gusset 08", "Lower Gusset 08"),
        ("Motor.008", "Upper Gusset 01", "Upper Gusset 01"), ("Motor.009", "Upper Gusset 02", "Upper Gusset 02"),
        ("Motor.010", "Upper Gusset 03", "Upper Gusset 03"), ("Motor.011", "Upper Gusset 04", "Upper Gusset 04"),
        ("Motor.012", "Upper Gusset 05", "Upper Gusset 05"), ("Motor.013", "Upper Gusset 06", "Upper Gusset 06"),
        ("Motor.014", "Upper Gusset 07", "Upper Gusset 07"), ("Motor.015", "Upper Gusset 08", "Upper Gusset 08"),
        ("Motor.016", "Lower End Plate", "Lower End Plate"),
        ("Motor.017", "Upper End Plate", "Upper End Plate"),
        ("Motor.020", "Motor Body", "Motor Body"),
        ("Cylinder.052", "Drive Shaft", "Drive Shaft"),
        ("Cylinder.053", "Shaft Collar - Top", "Shaft Collar - Top"),
        // Moved to the lower bearing position in the scene; the .blend still
        // names it Shaft Collar - Middle and places it mid-shaft.
        ("Cylinder.055", "Shaft Collar - Middle", "Shaft Collar - Bottom"),
        ("Cylinder.054", "Rotor Core", "Rotor Core"),
        ("Cylinder.056", "Mounting Flange", "Mounting Flange"),
        ("Cube.022", "Stator", "Stator"),
        ("Cube.021", "Finned Housing", "Finned Housing")
    };

    private static readonly (string Original, string Blender, string Name)[] RotorParts =
    {
        ("Rotor top", "Upper Rotor", "Upper Rotor"),
        ("Rotor bottom", "Lower Rotor", "Lower Rotor")
    };

    // The filtered line runs under the Filtered Flow Outlet (+Z), the coarse
    // line under the Coarse Flow Outlet (-Z). Inner faces the machine.
    private static readonly (string Original, string Blender, string Name)[] FilteredConveyorParts =
    {
        ("Cylinder.057", "Filtered Conveyor - Belt", "Belt"),
        ("Cylinder.059", "Filtered Conveyor - Idler Rollers", "Idler Rollers"),
        ("Cube.025", "Filtered Conveyor - Side Guard Inner", "Side Guard - Inner"),
        ("Cube.024", "Filtered Conveyor - Side Guard Outer", "Side Guard - Outer"),
        ("Cube.028", "Filtered Conveyor - Leg Frame Inner", "Leg Frame - Inner"),
        ("Cube.027", "Filtered Conveyor - Leg Frame Outer", "Leg Frame - Outer"),
        ("Cube.029", "Filtered Conveyor - Cross Members", "Cross Members"),
        ("Motor.018", "Filtered Conveyor - Foot Plates Inner", "Foot Plates - Inner"),
        ("Motor.019", "Filtered Conveyor - Foot Plates Outer", "Foot Plates - Outer")
    };

    private static readonly (string Original, string Blender, string Name)[] CoarseConveyorParts =
    {
        ("Cylinder.037", "Coarse Conveyor - Belt", "Belt"),
        ("Cylinder.041", "Coarse Conveyor - Idler Rollers", "Idler Rollers"),
        ("Cube.034", "Coarse Conveyor - Side Guard Inner", "Side Guard - Inner"),
        ("Cube.030", "Coarse Conveyor - Side Guard Outer", "Side Guard - Outer"),
        ("Cube.035", "Coarse Conveyor - Leg Frame Inner", "Leg Frame - Inner"),
        ("Cube.031", "Coarse Conveyor - Leg Frame Outer", "Leg Frame - Outer"),
        ("Cube.033", "Coarse Conveyor - Cross Members", "Cross Members"),
        ("Motor.022", "Coarse Conveyor - Foot Plates Inner", "Foot Plates - Inner"),
        ("Motor.021", "Coarse Conveyor - Foot Plates Outer", "Foot Plates - Outer")
    };

    private static void OrganizeBunker(Transform equipment, Transform hopperAssembly)
    {
        // "Supports" is what the frame was called before the Blender rename.
        RenameScoped(hopperAssembly, "Support Frame", "Supports");

        foreach ((string original, string blender, string name) in BunkerParts)
            RelinkMesh(AdoptChild(hopperAssembly, equipment, name, original, blender), BunkerModelPath, original, blender);
    }

    private static void OrganizeSeparatorModels(Transform equipment, Transform hopperAssembly)
    {
        Transform full = AdoptChild(hopperAssembly, equipment, "Full Model", "full model of hopper");
        Transform cut = AdoptChild(hopperAssembly, equipment, "Cross-Section Model", "Cut detailed hopper");

        UnpackModelInstance(full);
        UnpackModelInstance(cut);

        foreach ((string original, string blender, string name) in SeparatorParts)
        {
            RelinkMesh(RenameScoped(full, name, original, blender), FullModelPath, original, blender);
            RelinkMesh(RenameScoped(cut, name, original, blender), CrossSectionModelPath, original, blender);
        }

        MirrorSeparatorVibration(cut, full);

        // Single-model section: the full model is clipped and the cut frame
        // fills the section faces, so a prepared cutaway model is retired.
        if (AdoptCutFrame(hopperAssembly, full) != null)
            RetireCrossSectionModel(cut);
    }

    private const string CutFrameName = "Hopper Cut Frame";

    private static Transform AdoptCutFrame(Transform hopperAssembly, Transform full)
    {
        Transform frame = hopperAssembly.Find(CutFrameName) ?? FindTopLevel(CutFrameName) ?? Find(CutFrameName);

        if (frame == null)
            return null;

        if (frame.parent != hopperAssembly)
            frame.SetParent(hopperAssembly, true);

        UnpackModelInstance(frame);

        Transform separatorFill = RenameScoped(frame, "Section Fill - Separator", "Screen Decks and Discharge.002");
        RenameScoped(frame, "Section Fill - Base Housing", "Screen Decks and Discharge.003");

        // The separator's section face moves with the vibrating drum, or the
        // clipped walls would shake behind a still face. The base housing,
        // like its model part, stays put.
        Transform drum = full != null ? full.Find("Separator Drum") : null;
        SeparatorVibration drumVibration = drum != null ? drum.GetComponent<SeparatorVibration>() : null;

        if (separatorFill != null && drumVibration != null && separatorFill.GetComponent<SeparatorVibration>() == null)
            EditorUtility.CopySerialized(drumVibration, separatorFill.gameObject.AddComponent<SeparatorVibration>());

        return frame;
    }

    private static void RetireCrossSectionModel(Transform cut)
    {
        HybridHopperClipController clip = UnityEngine.Object.FindAnyObjectByType<HybridHopperClipController>(
            FindObjectsInactive.Include);

        if (clip != null)
        {
            SerializedObject serialized = new(clip);
            serialized.FindProperty("cutHopper").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (cut != null)
            UnityEngine.Object.DestroyImmediate(cut.gameObject);
    }

    private static void OrganizeVibratoryDrive(Transform equipment, Transform hopperAssembly, Transform drive)
    {
        // The drive is its own model file with every part at the top level;
        // unpacking lets the motor parts be grouped under Drive Motor.
        UnpackModelInstance(drive);

        Transform motor = AdoptChild(drive, equipment, "Drive Motor", "motor") ??
                          GetOrCreateChild(drive, "Drive Motor");

        foreach ((string original, string blender, string name) in DriveMotorParts)
            RelinkMesh(AdoptChild(motor, drive, name, original, blender), DriveModelPath, original, blender);

        // Each rotor sits directly under the drive so it can be its own
        // mechanism (the upper one is the mixer). Older exports put them in
        // the cutaway model, which is switched off in the Full Body view.
        Transform cut = hopperAssembly.Find("Cross-Section Model");

        foreach ((string original, string blender, string name) in RotorParts)
        {
            Transform rotor = AdoptChild(drive, drive, name, original, blender) ??
                              AdoptChild(drive, cut, name, original, blender);
            RelinkMesh(rotor, DriveModelPath, original, blender);
        }
    }

    private static void OrganizeConveyors(Transform equipment)
    {
        Transform conveyors = GetOrCreateRenamedChild(equipment, "Conveyors", "Conveyor");

        // A newly placed conveyor model sits beside the organised group.
        Transform placedModel = equipment.Find("Conveyor");

        UnpackModelInstance(conveyors);
        UnpackModelInstance(placedModel);

        Transform filtered = GetOrCreateChild(conveyors, "Filtered Material Conveyor");
        Transform coarse = GetOrCreateChild(conveyors, "Coarse Material Conveyor");

        AdoptConveyorParts(filtered, FilteredConveyorParts, conveyors, placedModel);
        AdoptConveyorParts(coarse, CoarseConveyorParts, conveyors, placedModel);

        if (placedModel != null && placedModel.childCount == 0 &&
            placedModel.GetComponents<Component>().Length == 1)
        {
            UnityEngine.Object.DestroyImmediate(placedModel.gameObject);
        }
    }

    private static void AdoptConveyorParts(
        Transform line,
        (string Original, string Blender, string Name)[] parts,
        Transform conveyors,
        Transform placedModel)
    {
        foreach ((string original, string blender, string name) in parts)
        {
            Transform placed = FindChild(placedModel, original, blender);
            Transform existing = line.Find(name);

            // A newly placed model part carries the current geometry and the
            // materials chosen for it, so it replaces the part it stands for -
            // unless that part carries components of its own.
            if (placed != null && existing != null && existing != placed)
            {
                if (!HasOnlyMeshComponents(existing))
                {
                    Debug.LogWarning(
                        $"'{PathOf(existing)}' has components of its own and was kept; " +
                        $"the newly placed '{placed.name}' was left beside it.");
                    continue;
                }

                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            Transform part = placed != null
                ? Adopt(line, placed, name)
                : AdoptChild(line, conveyors, name, original, blender);

            RelinkMesh(part, ConveyorModelPath, original, blender);
        }
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
    /// Scene objects taken from a model file are kept unpacked. An unpacked
    /// object keeps its materials and components when the model is exported
    /// again; an instance's overrides are keyed to the part names and are
    /// dropped when those change.
    /// </summary>
    private static void UnpackModelInstance(Transform root)
    {
        if (root == null)
            return;

        GameObject gameObject = root.gameObject;

        if (PrefabUtility.IsPartOfPrefabInstance(gameObject) &&
            PrefabUtility.IsOutermostPrefabInstanceRoot(gameObject))
        {
            PrefabUtility.UnpackPrefabInstance(gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }
    }

    /// <summary>
    /// Points a part whose mesh reference went missing - its model was
    /// exported again under new part names - at the mesh of the same part.
    /// Parts whose mesh still resolves are left alone.
    /// </summary>
    private static void RelinkMesh(Transform part, string modelPath, params string[] meshNames)
    {
        MeshFilter filter = part != null ? part.GetComponent<MeshFilter>() : null;

        if (filter == null || filter.sharedMesh != null)
            return;

        Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(modelPath)
            .OfType<Mesh>()
            .FirstOrDefault(candidate => meshNames.Contains(candidate.name));

        if (mesh == null)
        {
            Debug.LogWarning($"'{PathOf(part)}' has no mesh, and '{modelPath}' has none named {string.Join(" or ", meshNames)}.");
            return;
        }

        filter.sharedMesh = mesh;
        EditorUtility.SetDirty(filter);
    }

    /// <summary>
    /// Moves a child found under <paramref name="searchRoot"/> by any of its
    /// previous names into <paramref name="parent"/> under its new name.
    /// Re-running finds it already in place.
    /// </summary>
    private static Transform AdoptChild(Transform parent, Transform searchRoot, string newName, params string[] oldNames)
    {
        if (parent == null)
            return null;

        Transform target = parent.Find(newName) ??
                           FindChild(searchRoot, oldNames) ??
                           FindChild(parent, oldNames);

        return target != null ? Adopt(parent, target, newName) : null;
    }

    private static Transform Adopt(Transform parent, Transform target, string newName)
    {
        Rename(target, newName);

        if (target.parent != parent)
            target.SetParent(parent, true);

        return target;
    }

    private static Transform RenameScoped(Transform parent, string newName, params string[] oldNames)
    {
        Transform target = FindChild(parent, newName) ?? FindChild(parent, oldNames);

        if (target != null)
            Rename(target, newName);

        return target;
    }

    private static Transform FindChild(Transform parent, params string[] names)
    {
        if (parent == null)
            return null;

        foreach (string name in names)
        {
            Transform child = parent.Find(name);

            if (child != null)
                return child;
        }

        return null;
    }

    private static bool HasOnlyMeshComponents(Transform target) =>
        target.GetComponents<Component>().All(component =>
            component is Transform || component is MeshFilter || component is MeshRenderer);

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

    private static string PathOf(Transform target) =>
        target.parent == null ? target.name : PathOf(target.parent) + "/" + target.name;

    private static int CountMissingScripts(GameObject gameObject)
    {
        int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
        foreach (Transform child in gameObject.transform)
            count += CountMissingScripts(child.gameObject);
        return count;
    }
}
