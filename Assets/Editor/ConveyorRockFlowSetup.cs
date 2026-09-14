using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Puts rocks on the conveyors. For each line: files the landing and removal
/// markers under the flow effects, builds a belt rock system that uses a
/// non-clipping copy of the rock material, wires a <see cref="ConveyorRockTransfer"/>
/// to the falling systems that feed it, and gives those falling rocks real
/// gravity so they reach the belt. The fault line also gets its end roller
/// split from the idler rollers, so an alarm can point at that one roller.
///
/// Idempotent, and saves the scene: back it up first.
/// </summary>
public static class ConveyorRockFlowSetup
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string SourceMaterialPath = "Assets/Materials/Rock Particles.mat";
    private const string ConveyorMaterialPath = "Assets/Materials/Rock Particles - Conveyor.mat";
    private const string ConveyorModelPath = "Assets/Models/Conveyor.fbx";
    private const string DerivedMeshFolder = "Assets/Models/Derived";
    private const string BeltSpeedSensorId = "FEED-01.BELT.SPEED";
    private const string DriveRollerName = "Drive Roller";

    // A real drop onto the belt. The flows used a slow-motion 3 m/s² and
    // removed rocks 0.47 s after the outlet - before they reached the belt.
    private const float OutletGravity = 9.81f;
    private const float OutletFallLifetime = 1.5f;

    private sealed class Line
    {
        public string Equipment;
        public string Prefix;
        public string[] LandingNames;
        public string[] RemovalNames;
        public bool FromScreen;
        public int MaxRidingRocks;
        public float RideShare;
        public string[] RollerMeshNames;
    }

    private static readonly Line[] Lines =
    {
        new()
        {
            Equipment = "Filtered Material Conveyor",
            Prefix = "Filtered Conveyor",
            LandingNames = new[] { "Filtered Conveyor - Rock Landing", "conveyor filtered start y position" },
            RemovalNames = new[] { "Filtered Conveyor - Rock Removal", "conveyor filtered end x position (1)", "conveyor filtered end x position" },
            FromScreen = true,
            MaxRidingRocks = 700,
            RideShare = 0.35f,
            // The mentor's fault is on this line's head roller.
            RollerMeshNames = new[] { "Cylinder.059", "Filtered Conveyor - Idler Rollers" }
        },
        new()
        {
            Equipment = "Coarse Material Conveyor",
            Prefix = "Coarse Conveyor",
            LandingNames = new[] { "Coarse Conveyor - Rock Landing", "conveyor big rock start y position" },
            RemovalNames = new[] { "Coarse Conveyor - Rock Removal", "conveyor big rock end x position" },
            FromScreen = false,
            MaxRidingRocks = 300,
            RideShare = 1f,
            RollerMeshNames = null
        }
    };

    [MenuItem("Tools/Digital Twin/Set Up Conveyor Rock Flow", priority = 65)]
    public static void Apply()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform equipment = FindInScene("03 - Equipment");
        Transform effects = FindInScene("Material Flow Effects");

        if (equipment == null || effects == null)
        {
            Debug.LogError("Conveyor rock flow skipped: '03 - Equipment' or 'Material Flow Effects' is missing.");
            return;
        }

        Material rockMaterial = GetOrCreateConveyorMaterial();
        PerformanceStatSource beltSpeed = Object
            .FindObjectsByType<PerformanceStatSource>(FindObjectsInactive.Include)
            .FirstOrDefault(sensor => sensor.StatId == BeltSpeedSensorId);

        foreach (Line line in Lines)
        {
            Transform conveyor = equipment.Find("Conveyors/" + line.Equipment);
            Transform landing = AdoptMarker(effects, line.LandingNames);
            Transform removal = AdoptMarker(effects, line.RemovalNames);

            if (conveyor == null || landing == null || removal == null)
            {
                Debug.LogWarning($"Conveyor rock flow: '{line.Equipment}' skipped - its belt or markers are missing.");
                continue;
            }

            ParticleSystem[] feeds = line.FromScreen
                ? Object.FindObjectsByType<FilteredParticleFlow>(FindObjectsInactive.Include)
                    .OrderBy(flow => flow.name).Select(flow => flow.GetComponent<ParticleSystem>()).ToArray()
                : Object.FindObjectsByType<ControlledRockFlow>(FindObjectsInactive.Include)
                    .OrderBy(flow => flow.name).Select(flow => flow.GetComponent<ParticleSystem>()).ToArray();

            if (feeds.Length == 0)
            {
                Debug.LogWarning($"Conveyor rock flow: no falling rock systems feed '{line.Equipment}'.");
                continue;
            }

            GiveOutletFallRealGravity(line.FromScreen);

            ParticleSystem rocks = GetOrCreateBeltSystem(effects, line.Prefix + " Rocks", feeds[0], rockMaterial, line.MaxRidingRocks);
            ConfigureTransfer(rocks, feeds, landing, removal, conveyor, beltSpeed, line);

            if (line.RollerMeshNames != null)
                SplitDriveRoller(conveyor, landing.position, removal.position, line);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("CONVEYOR_ROCK_FLOW configured");
    }

    // -----------------------------------------------------------------------
    // Markers, material and systems
    // -----------------------------------------------------------------------

    /// <summary>Finds a marker by its current or original name and files it with the other flow markers.</summary>
    private static Transform AdoptMarker(Transform effects, string[] names)
    {
        Transform marker = effects.Find(names[0]) ??
                           names.Select(FindInScene).FirstOrDefault(found => found != null);

        if (marker == null)
            return null;

        marker.name = names[0];

        if (marker.parent != effects)
            marker.SetParent(effects, true);

        return marker;
    }

    private static Material GetOrCreateConveyorMaterial()
    {
        Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceMaterialPath);
        Material material = AssetDatabase.LoadAssetAtPath<Material>(ConveyorMaterialPath);

        if (material == null)
        {
            material = new Material(source) { name = "Rock Particles - Conveyor" };
            AssetDatabase.CreateAsset(material, ConveyorMaterialPath);
        }
        else if (source != null)
        {
            // Stays identical to the falling rocks, so the hand-off cannot be seen.
            material.shader = source.shader;
            material.CopyPropertiesFromMaterial(source);
        }

        // The belts run across the cut plane; rocks on them must never be clipped.
        material.SetFloat("_ClipEnabled", 0f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static ParticleSystem GetOrCreateBeltSystem(
        Transform effects,
        string name,
        ParticleSystem template,
        Material material,
        int maxParticles)
    {
        Transform existing = effects.Find(name);
        GameObject host;

        if (existing != null)
        {
            host = existing.gameObject;
        }
        else
        {
            // A copy of a feeding system keeps the same rock mesh and renderer
            // settings; its flow script belongs to the falling rocks and goes.
            host = Object.Instantiate(template.gameObject, effects);
            host.name = name;

            foreach (MonoBehaviour behaviour in host.GetComponents<MonoBehaviour>())
                Object.DestroyImmediate(behaviour);

            Undo.RegisterCreatedObjectUndo(host, "Create belt rock system");
        }

        host.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        host.transform.localScale = Vector3.one;

        ParticleSystem system = host.GetComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = true;
        main.prewarm = false;
        main.startLifetime = 600f;
        main.startSpeed = 0f;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles;

        // Rocks only arrive by hand-off, and move only as the transfer moves them.
        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false;
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;
        ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
        velocity.enabled = false;
        ParticleSystem.LimitVelocityOverLifetimeModule limit = system.limitVelocityOverLifetime;
        limit.enabled = false;
        ParticleSystem.InheritVelocityModule inherit = system.inheritVelocity;
        inherit.enabled = false;
        ParticleSystem.ForceOverLifetimeModule force = system.forceOverLifetime;
        force.enabled = false;
        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = false;
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = false;
        ParticleSystem.RotationOverLifetimeModule rotation = system.rotationOverLifetime;
        rotation.enabled = false;
        ParticleSystem.NoiseModule noise = system.noise;
        noise.enabled = false;
        ParticleSystem.CollisionModule collision = system.collision;
        collision.enabled = false;
        ParticleSystem.TriggerModule trigger = system.trigger;
        trigger.enabled = false;
        ParticleSystem.SubEmittersModule subEmitters = system.subEmitters;
        subEmitters.enabled = false;

        host.GetComponent<ParticleSystemRenderer>().sharedMaterial = material;
        return system;
    }

    private static void ConfigureTransfer(
        ParticleSystem rocks,
        ParticleSystem[] feeds,
        Transform landing,
        Transform removal,
        Transform conveyor,
        PerformanceStatSource beltSpeed,
        Line line)
    {
        ConveyorRockTransfer transfer = rocks.GetComponent<ConveyorRockTransfer>();

        if (transfer == null)
            transfer = Undo.AddComponent<ConveyorRockTransfer>(rocks.gameObject);

        SerializedObject serialized = new(transfer);

        SerializedProperty feedProperty = serialized.FindProperty("feedSystems");
        feedProperty.arraySize = feeds.Length;
        for (int i = 0; i < feeds.Length; i++)
            feedProperty.GetArrayElementAtIndex(i).objectReferenceValue = feeds[i];

        serialized.FindProperty("landingPoint").objectReferenceValue = landing;
        serialized.FindProperty("killPoint").objectReferenceValue = removal;
        serialized.FindProperty("beltHalfWidth").floatValue = UsableHalfWidth(conveyor);
        serialized.FindProperty("catchBehind").floatValue = 0.6f;
        serialized.FindProperty("rideShare").floatValue = line.RideShare;
        serialized.FindProperty("beltSpeedSensor").objectReferenceValue = beltSpeed;

        Transform belt = conveyor.Find("Belt");
        Renderer beltRenderer = belt != null ? belt.GetComponent<Renderer>() : null;
        serialized.FindProperty("beltRenderer").objectReferenceValue = beltRenderer;

        // The markers sit roughly on the belt; rocks rest on its measured top.
        serialized.FindProperty("surfaceOffset").floatValue = beltRenderer != null
            ? Mathf.Clamp(beltRenderer.bounds.max.y - landing.position.y, -0.1f, 0.1f)
            : 0f;

        if (beltRenderer != null && TryMeasureBeltTexture(belt, out float metresPerUv))
        {
            serialized.FindProperty("beltMetresPerUv").floatValue = metresPerUv;

            // The belt material already scrolls towards the discharge end on
            // both lines; only its speed is calibrated here.
            Vector4 direction = beltRenderer.sharedMaterial != null && beltRenderer.sharedMaterial.HasProperty("_Direction")
                ? beltRenderer.sharedMaterial.GetVector("_Direction")
                : new Vector4(0f, 1f, 0f, 0f);
            serialized.FindProperty("beltUvDirection").vector2Value = new Vector2(direction.x, direction.y);
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Half the gap between the side guards, less a rock's half-width.</summary>
    private static float UsableHalfWidth(Transform conveyor)
    {
        Renderer inner = conveyor.Find("Side Guard - Inner")?.GetComponent<Renderer>();
        Renderer outer = conveyor.Find("Side Guard - Outer")?.GetComponent<Renderer>();

        if (inner == null || outer == null)
            return 0.3f;

        Vector3 gap = outer.bounds.center - inner.bounds.center;
        float halfGap = new Vector3(gap.x, 0f, gap.z).magnitude * 0.5f;
        return Mathf.Max(0.1f, halfGap - 0.06f);
    }

    /// <summary>Metres per texture V unit on the belt's top run, from a straight-line fit.</summary>
    private static bool TryMeasureBeltTexture(Transform belt, out float metresPerUv)
    {
        metresPerUv = 1f;
        Mesh mesh = belt.GetComponent<MeshFilter>()?.sharedMesh;

        if (mesh == null)
            return false;

        Bounds bounds = belt.GetComponent<Renderer>().bounds;
        Vector3 axis = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;

        double n = 0, sv = 0, sa = 0, svv = 0, sva = 0;
        for (int i = 0; i < vertices.Length && i < uvs.Length; i++)
        {
            Vector3 world = belt.TransformPoint(vertices[i]);

            if (world.y < bounds.center.y + bounds.extents.y * 0.5f)
                continue;

            double v = uvs[i].y, a = Vector3.Dot(world, axis);
            n++; sv += v; sa += a; svv += v * v; sva += v * a;
        }

        double variance = n * svv - sv * sv;

        if (n < 3 || variance <= 1e-9)
            return false;

        metresPerUv = Mathf.Abs((float)((n * sva - sv * sa) / variance));
        return metresPerUv > 0.001f;
    }

    /// <summary>
    /// The outlet drop was tuned slow and short; rocks now fall under real
    /// gravity and live long enough to be caught by the belt.
    /// </summary>
    private static void GiveOutletFallRealGravity(bool fromScreen)
    {
        IEnumerable<MonoBehaviour> flows = fromScreen
            ? Object.FindObjectsByType<FilteredParticleFlow>(FindObjectsInactive.Include)
            : Object.FindObjectsByType<ControlledRockFlow>(FindObjectsInactive.Include);

        foreach (MonoBehaviour flow in flows)
        {
            SerializedObject serialized = new(flow);
            serialized.FindProperty("outletExitGravity").floatValue = OutletGravity;
            serialized.FindProperty("outletFallLifetime").floatValue = OutletFallLifetime;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // -----------------------------------------------------------------------
    // Drive roller
    // -----------------------------------------------------------------------

    /// <summary>
    /// The idler rollers are one mesh - an Array in Blender. The roller at the
    /// head end is cut out of the imported mesh into its own object, so the
    /// Blender modifier never has to be applied. Rebuilt from the model file
    /// every run.
    /// </summary>
    private static void SplitDriveRoller(Transform conveyor, Vector3 landing, Vector3 removal, Line line)
    {
        Transform rollers = conveyor.Find("Idler Rollers");
        MeshFilter rollerFilter = rollers != null ? rollers.GetComponent<MeshFilter>() : null;
        Mesh source = AssetDatabase.LoadAllAssetsAtPath(ConveyorModelPath)
            .OfType<Mesh>()
            .FirstOrDefault(mesh => line.RollerMeshNames.Contains(mesh.name));

        if (rollerFilter == null || source == null)
        {
            Debug.LogWarning($"Drive roller for '{line.Equipment}' skipped: roller mesh not found.");
            return;
        }

        Vector3 travel = removal - landing;
        travel.y = 0f;
        travel.Normalize();

        Matrix4x4 toWorld = rollers.localToWorldMatrix;
        Vector3[] vertices = source.vertices;
        float[] along = vertices.Select(v => Vector3.Dot(toWorld.MultiplyPoint3x4(v), travel)).ToArray();

        // The last gap along the travel direction separates the head roller.
        List<float> sorted = along.OrderBy(value => value).ToList();
        float threshold = float.NaN;
        for (int i = sorted.Count - 1; i > 0; i--)
        {
            if (sorted[i] - sorted[i - 1] > 0.05f)
            {
                threshold = (sorted[i] + sorted[i - 1]) * 0.5f;
                break;
            }
        }

        if (float.IsNaN(threshold))
        {
            Debug.LogWarning($"Drive roller for '{line.Equipment}' skipped: the rollers could not be told apart.");
            return;
        }

        string folder = EnsureFolder();
        Mesh rest = SaveMesh(Extract(source, along, threshold, keepHead: false, $"{line.Prefix} - Idler Rollers"),
            $"{folder}/{line.Prefix} - Idler Rollers.asset");
        Mesh head = SaveMesh(Extract(source, along, threshold, keepHead: true, $"{line.Prefix} - Drive Roller"),
            $"{folder}/{line.Prefix} - Drive Roller.asset");

        rollerFilter.sharedMesh = rest;
        EditorUtility.SetDirty(rollerFilter);

        Transform drive = conveyor.Find(DriveRollerName);

        if (drive == null)
        {
            drive = new GameObject(DriveRollerName, typeof(MeshFilter), typeof(MeshRenderer)).transform;
            Undo.RegisterCreatedObjectUndo(drive.gameObject, "Create drive roller");
            drive.SetParent(conveyor, false);
        }

        drive.SetLocalPositionAndRotation(rollers.localPosition, rollers.localRotation);
        drive.localScale = rollers.localScale;
        drive.gameObject.layer = rollers.gameObject.layer;
        GameObjectUtility.SetStaticEditorFlags(drive.gameObject, GameObjectUtility.GetStaticEditorFlags(rollers.gameObject));
        drive.SetSiblingIndex(rollers.GetSiblingIndex() + 1);

        drive.GetComponent<MeshFilter>().sharedMesh = head;

        MeshRenderer from = rollers.GetComponent<MeshRenderer>();
        MeshRenderer to = drive.GetComponent<MeshRenderer>();
        to.sharedMaterials = from.sharedMaterials;
        to.shadowCastingMode = from.shadowCastingMode;
        to.receiveShadows = from.receiveShadows;
        to.lightProbeUsage = from.lightProbeUsage;
        to.reflectionProbeUsage = from.reflectionProbeUsage;
        EditorUtility.SetDirty(to);
    }

    private static Mesh Extract(Mesh source, float[] along, float threshold, bool keepHead, string name)
    {
        Vector3[] positions = source.vertices;
        Vector3[] normals = source.normals;
        Vector4[] tangents = source.tangents;
        Vector2[] uv = source.uv;
        Vector2[] uv2 = source.uv2;
        Color[] colors = source.colors;

        Dictionary<int, int> remap = new();
        List<int> order = new();
        List<int>[] triangles = new List<int>[source.subMeshCount];

        for (int sub = 0; sub < source.subMeshCount; sub++)
        {
            triangles[sub] = new List<int>();
            int[] indices = source.GetTriangles(sub);

            for (int t = 0; t < indices.Length; t += 3)
            {
                float centre = (along[indices[t]] + along[indices[t + 1]] + along[indices[t + 2]]) / 3f;

                if ((centre >= threshold) != keepHead)
                    continue;

                for (int k = 0; k < 3; k++)
                {
                    int index = indices[t + k];

                    if (!remap.TryGetValue(index, out int mapped))
                    {
                        mapped = order.Count;
                        remap.Add(index, mapped);
                        order.Add(index);
                    }

                    triangles[sub].Add(mapped);
                }
            }
        }

        Mesh mesh = new() { name = name };
        mesh.indexFormat = order.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        mesh.SetVertices(order.Select(i => positions[i]).ToList());
        if (normals.Length == positions.Length) mesh.SetNormals(order.Select(i => normals[i]).ToList());
        if (tangents.Length == positions.Length) mesh.SetTangents(order.Select(i => tangents[i]).ToList());
        if (uv.Length == positions.Length) mesh.SetUVs(0, order.Select(i => uv[i]).ToList());
        if (uv2.Length == positions.Length) mesh.SetUVs(1, order.Select(i => uv2[i]).ToList());
        if (colors.Length == positions.Length) mesh.SetColors(order.Select(i => colors[i]).ToList());

        mesh.subMeshCount = source.subMeshCount;
        for (int sub = 0; sub < source.subMeshCount; sub++)
            mesh.SetTriangles(triangles[sub], sub);

        mesh.RecalculateBounds();
        mesh.UploadMeshData(true);
        return mesh;
    }

    /// <summary>Writes into an existing asset when there is one, so its GUID and every reference survive.</summary>
    private static Mesh SaveMesh(Mesh mesh, string path)
    {
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);

        if (existing == null)
        {
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        EditorUtility.CopySerialized(mesh, existing);
        existing.name = mesh.name;
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static string EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(DerivedMeshFolder))
            AssetDatabase.CreateFolder("Assets/Models", "Derived");

        return DerivedMeshFolder;
    }

    private static Transform FindInScene(string name)
    {
        Scene active = SceneManager.GetActiveScene();
        return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .FirstOrDefault(candidate => candidate.gameObject.scene == active && candidate.name == name);
    }
}
