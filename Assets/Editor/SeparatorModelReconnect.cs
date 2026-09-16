using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Reconnects the separator after the full model is re-exported with its screen
/// deck and discharge split into separate parts. The new model instance takes
/// over from the old "Full Model" (whose meshes the re-export removed), its
/// parts get descriptive names, and the clip controller, the vibration, the
/// machine configuration and the part groups all point at the new geometry.
///
/// Telemetry anchors, conveyor rollers and every other transform stay exactly
/// where they are: the anchor positions are copied into the configuration, not
/// the other way round. Safe to run again.
/// </summary>
public static class SeparatorModelReconnect
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string ModelPath = "Assets/Models/Separator - Full Model.fbx";
    private const string AssemblyPath = "03 - Equipment/Hopper Assembly";
    private const string ModelName = "Full Model";

    // Parts are matched by their mesh, which survives renaming in the scene.
    private static readonly (string Mesh, string Name)[] PartNames =
    {
        ("Base Housing", "Base Housing"),
        ("Isolation Springs", "Isolation Springs"),
        ("Spring Seat - Lower", "Spring Seat - Lower"),
        ("Spring Seat - Upper", "Spring Seat - Upper"),
        ("Separator Drum", "Separator Drum"),
        ("Screen Decks and Discharge", "Motor Mount"),
        ("Screen Decks and Discharge.003", "Outlet Chute - Filtered"),
        ("Screen Decks and Discharge.005", "Outlet Rim - Filtered"),
        ("Screen Decks and Discharge.006", "Outlet Rim - Coarse"),
        ("Screen Decks and Discharge.007", "Outlet Chute - Coarse"),
        ("Screen Decks and Discharge.008", "Base Plate"),
        ("Screen Decks and Discharge.009", "Top Dome"),
        ("Screen Decks and Discharge.010", "Sliding Disk"),
        ("Screen Decks and Discharge.011", "Screen Mesh"),
        ("Screen Decks and Discharge.012", "Screen Mesh Frame"),
        ("Screen Decks and Discharge.013", "Top Disk")
    };

    // The base housing and the lower spring seat stand on the ground; everything
    // resting on the springs shakes, as the joined screen decks did before.
    private static readonly string[] StaticParts = { "Base Housing", "Spring Seat - Lower" };

    private static readonly string[] ScreenDeckParts = { "Screen Mesh", "Screen Mesh Frame" };

    private static readonly string[] DischargeParts =
    {
        "Outlet Chute - Filtered", "Outlet Rim - Filtered", "Outlet Chute - Coarse", "Outlet Rim - Coarse"
    };

    private static readonly string[] RotorSensorIds =
    {
        "MIX-01.ROTOR.SPEED", "MIX-01.ROTOR.TORQUE", "MIX-01.ROTOR.IMBALANCE"
    };

    [MenuItem("Tools/Digital Twin/Reconnect Separator Model")]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject equipment = scene.GetRootGameObjects()
            .Select(root => root.transform.Find("03 - Equipment"))
            .FirstOrDefault(found => found != null)?.gameObject;
        Transform assembly = equipment != null ? equipment.transform.parent.Find(AssemblyPath) : null;
        if (assembly == null)
        {
            Debug.LogError($"SEPARATOR_RECONNECT '{AssemblyPath}' was not found.");
            return;
        }

        Transform model = AdoptModel(assembly);
        if (model == null)
            return;

        int renamed = RenameParts(model);
        ConnectClipController(model);
        int vibrating = ConnectVibration(assembly, model);

        DigitalTwinMachineConfiguration configuration = LoadDefaultConfiguration();
        int anchors = 0;
        if (configuration != null)
        {
            UpdateConfiguration(configuration);
            anchors = SyncAnchorPositions(configuration);
            EditorUtility.SetDirty(configuration);
            RebuildPartGroups(configuration, equipment);
        }

        RemoveEmptyLeftovers(assembly);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"SEPARATOR_RECONNECT model='{Path(model)}' renamed={renamed} vibrating={vibrating} " +
                  $"anchorsSynced={anchors}");
    }

    /// <summary>
    /// The instance of the re-exported model becomes the Full Model, in the old
    /// one's place. The old copy's meshes no longer exist, so it is removed.
    /// </summary>
    private static Transform AdoptModel(Transform assembly)
    {
        GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        List<Transform> instances = assembly.Cast<Transform>()
            .Where(child => PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject) &&
                            PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == source)
            .ToList();
        if (instances.Count != 1)
        {
            Debug.LogError($"SEPARATOR_RECONNECT expected one instance of '{ModelPath}' under the hopper " +
                           $"assembly, found {instances.Count}.");
            return null;
        }

        Transform model = instances[0];
        int siblingIndex = model.GetSiblingIndex();
        foreach (Transform old in assembly.Cast<Transform>().Where(child => child != model && child.name == ModelName).ToList())
        {
            siblingIndex = Mathf.Min(siblingIndex, old.GetSiblingIndex());
            Debug.Log($"SEPARATOR_RECONNECT removed the old '{Path(old)}'");
            Object.DestroyImmediate(old.gameObject);
        }

        model.name = ModelName;
        model.SetSiblingIndex(siblingIndex);
        return model;
    }

    private static int RenameParts(Transform model)
    {
        int renamed = 0;
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            string mesh = filter.sharedMesh != null ? filter.sharedMesh.name : null;
            string name = PartNames.FirstOrDefault(entry => entry.Mesh == mesh).Name;
            if (name == null)
            {
                Debug.LogWarning($"SEPARATOR_RECONNECT no name for mesh '{mesh}' on '{Path(filter.transform)}'");
                continue;
            }
            if (filter.name == name)
                continue;
            filter.gameObject.name = name;
            renamed++;
        }

        return renamed;
    }

    private static void ConnectClipController(Transform model)
    {
        HybridHopperClipController clip = Object.FindAnyObjectByType<HybridHopperClipController>(FindObjectsInactive.Include);
        if (clip == null)
            return;

        SerializedObject data = new(clip);
        data.FindProperty("fullHopper").objectReferenceValue = model.gameObject;

        // An explicit list would still name the old renderers; filled, it names the new ones.
        SerializedProperty renderers = data.FindProperty("fullHopperRenderers");
        if (renderers.arraySize > 0)
        {
            Renderer[] found = model.GetComponentsInChildren<Renderer>(true);
            renderers.arraySize = found.Length;
            for (int i = 0; i < found.Length; i++)
                renderers.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }

        data.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Keeps the vibrator's other rows (the section fill) and gives every part
    /// that rests on the springs a row with the same shake as before.
    /// </summary>
    private static int ConnectVibration(Transform assembly, Transform model)
    {
        if (!assembly.TryGetComponent(out PartVibrator vibrator))
            return 0;

        SerializedObject data = new(vibrator);
        SerializedProperty parts = data.FindProperty("parts");

        Vector3 amplitude = new(0.005f, 0.002f, 0f);
        Vector3 rotation = Vector3.zero;
        float share = 1f;
        float phase = 0f;
        List<Transform> kept = new();
        for (int i = 0; i < parts.arraySize; i++)
        {
            SerializedProperty row = parts.GetArrayElementAtIndex(i);
            Transform target = row.FindPropertyRelative("target").objectReferenceValue as Transform;
            if (i == 0 || target != null)
            {
                amplitude = row.FindPropertyRelative("amplitude").vector3Value;
                rotation = row.FindPropertyRelative("rotationAmplitude").vector3Value;
                share = row.FindPropertyRelative("speedShare").floatValue;
                phase = row.FindPropertyRelative("phase").floatValue;
            }
            if (target != null && !target.IsChildOf(model))
                kept.Add(target);
        }

        List<Transform> targets = kept
            .Concat(model.Cast<Transform>().Where(part => !StaticParts.Contains(part.name)))
            .ToList();
        parts.arraySize = targets.Count;
        for (int i = 0; i < targets.Count; i++)
        {
            SerializedProperty row = parts.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("target").objectReferenceValue = targets[i];
            row.FindPropertyRelative("amplitude").vector3Value = amplitude;
            row.FindPropertyRelative("rotationAmplitude").vector3Value = rotation;
            row.FindPropertyRelative("speedShare").floatValue = share;
            row.FindPropertyRelative("phase").floatValue = phase;
        }

        data.ApplyModifiedPropertiesWithoutUndo();
        return targets.Count;
    }

    private static DigitalTwinMachineConfiguration LoadDefaultConfiguration()
    {
        List<DigitalTwinMachineConfiguration> defaults = AssetDatabase
            .FindAssets("t:DigitalTwinMachineConfiguration")
            .Select(guid => AssetDatabase.LoadAssetAtPath<DigitalTwinMachineConfiguration>(AssetDatabase.GUIDToAssetPath(guid)))
            .Where(configuration => configuration != null && configuration.isDefault)
            .ToList();
        if (defaults.Count == 1)
            return defaults[0];

        Debug.LogError($"SEPARATOR_RECONNECT expected one default machine configuration, found {defaults.Count}.");
        return null;
    }

    /// <summary>
    /// The split model lets the screen blinding card light only the screen,
    /// and the discharge card only the outlets.
    /// </summary>
    private static void UpdateConfiguration(DigitalTwinMachineConfiguration configuration)
    {
        List<MachinePartDefinition> parts = configuration.machineParts;
        int index = parts.FindIndex(part => part.id == "SCREEN_DECKS" || part.id == "SCREEN_DECK");
        if (index < 0)
            index = parts.Count;
        parts.RemoveAll(part => part.id is "SCREEN_DECKS" or "SCREEN_DECK" or "DISCHARGE_OUTLETS");

        parts.Insert(Mathf.Min(index, parts.Count), new MachinePartDefinition
        {
            id = "DISCHARGE_OUTLETS",
            displayName = "Discharge Outlets",
            objectPaths = DischargeParts.Select(ModelChildPath).ToList(),
            sensorIds = new List<string> { "FEED-01.FLOW.OUT" }
        });
        parts.Insert(Mathf.Min(index, parts.Count), new MachinePartDefinition
        {
            id = "SCREEN_DECK",
            displayName = "Screen Deck",
            objectPaths = ScreenDeckParts.Select(ModelChildPath).ToList(),
            sensorIds = new List<string> { "MIX-01.SCREEN.BLINDING" }
        });

        // Both rotors answer the rotor sensors in the scene; the configuration
        // said only the upper one did, so a rebuild would have lost that.
        MachinePartDefinition lower = parts.FirstOrDefault(part => part.id == "LOWER_ROTOR");
        if (lower != null)
            lower.sensorIds = RotorSensorIds.ToList();
    }

    private static string ModelChildPath(string part) => $"Hopper Assembly/{ModelName}/{part}";

    /// <summary>
    /// Copies the anchors as they are placed in the scene into the configuration,
    /// so rebuilding the scene from it never moves them back.
    /// </summary>
    private static int SyncAnchorPositions(DigitalTwinMachineConfiguration configuration)
    {
        Dictionary<string, PerformanceStatSource> sources = Object
            .FindObjectsByType<PerformanceStatSource>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .GroupBy(source => source.StatId)
            .ToDictionary(group => group.Key, group => group.First());

        int changed = 0;
        foreach (MachineSensorDefinition sensor in configuration.sensors)
        {
            if (!sources.TryGetValue(sensor.id, out PerformanceStatSource source))
                continue;
            Vector3 position = source.transform.localPosition;
            if ((sensor.localPosition - position).sqrMagnitude < 1e-10f)
                continue;
            sensor.localPosition = position;
            changed++;
        }

        return changed;
    }

    /// <summary>Rebuilds the part groups from the configuration with the X-Ray setup's own steps.</summary>
    private static void RebuildPartGroups(DigitalTwinMachineConfiguration configuration, GameObject equipment)
    {
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        System.Type setup = typeof(MachineXRaySceneSetup);

        List<MachinePartGroup> groups = (List<MachinePartGroup>)setup.GetMethod("CreatePartGroups", flags)
            .Invoke(null, new object[] { configuration, equipment });

        MachineXRayPresenter presenter = Object.FindAnyObjectByType<MachineXRayPresenter>(FindObjectsInactive.Include);
        if (presenter != null)
            setup.GetMethod("ConfigurePresenter", flags).Invoke(null, new object[]
            {
                presenter, MachineXRaySceneSetup.GetOrCreateMaterial(), equipment, groups
            });

        MachineSelectionHighlighter highlighter = Object.FindAnyObjectByType<MachineSelectionHighlighter>(FindObjectsInactive.Include);
        if (highlighter != null)
            setup.GetMethod("ConfigureHighlighter", flags).Invoke(null, new object[] { highlighter, groups });

        foreach (MachinePartGroup group in groups)
            Debug.Log($"SEPARATOR_RECONNECT part '{group.name}' renderers={group.Renderers.Length}: " +
                      string.Join(", ", group.Renderers.Select(renderer => renderer.name)));
    }

    /// <summary>
    /// Earlier setups put a part group on an empty object beside the geometry
    /// (a second "Bunker" next to the bunker mesh). The groups now live under
    /// Machine Parts, which leaves that object with nothing on it.
    /// </summary>
    private static void RemoveEmptyLeftovers(Transform assembly)
    {
        foreach (Transform child in assembly.Cast<Transform>().ToList())
        {
            if (child.childCount > 0 || child.GetComponents<Component>().Length > 1 ||
                PrefabUtility.IsPartOfPrefabInstance(child))
                continue;
            Debug.Log($"SEPARATOR_RECONNECT removed the empty '{Path(child)}'");
            Object.DestroyImmediate(child.gameObject);
        }
    }

    private static string Path(Transform transform) =>
        transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name;
}
