#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-only, read-only report of the geometry the conveyor rock flow and the
/// single-model section depend on: belt and roller positions in world space,
/// which line is on the right of the start view, how the belt texture maps to
/// metres, the outlets and every empty marker, rock sizes, and where the cut
/// frame sits against the clip plane. Never saves.
/// </summary>
public static class ConveyorGeometryReport
{
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        StringBuilder report = new();
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include);
        Transform Find(string name) => all.FirstOrDefault(t => t.name == name);
        Camera cam = Camera.main;

        Transform conveyors = Find("Conveyors");
        if (conveyors != null)
        {
            foreach (Transform line in conveyors)
            {
                report.AppendLine($"LINE {line.name}");

                foreach (Transform part in line)
                {
                    Renderer renderer = part.GetComponent<Renderer>();

                    if (renderer == null)
                    {
                        report.AppendLine($"  marker {part.name} world={part.position:F3}");
                        continue;
                    }

                    Bounds b = renderer.bounds;
                    float viewX = cam != null ? cam.WorldToViewportPoint(b.center).x : -1f;
                    report.AppendLine($"  {part.name} center={b.center:F3} size={b.size:F3} viewportX={viewX:F2}");
                }

                Transform belt = line.Find("Belt");
                if (belt != null)
                    DescribeBeltUv(belt, report);

                Transform rollers = line.Find("Idler Rollers");
                if (rollers != null)
                    DescribeRollerClusters(rollers, report);
            }
        }

        report.AppendLine("== EMPTIES (transform only)");
        foreach (Transform t in all)
        {
            if (t is RectTransform || t.GetComponents<Component>().Length != 1)
                continue;

            report.AppendLine($"  {PathOf(t)} world={t.position:F3} children={t.childCount}");
        }

        report.AppendLine("== ROCK SYSTEMS");
        foreach (ParticleSystem system in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include))
        {
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            if (renderer == null || renderer.renderMode != ParticleSystemRenderMode.Mesh)
                continue;

            ParticleSystem.MainModule main = system.main;
            Mesh mesh = renderer.mesh;
            report.AppendLine(
                $"  {system.name}: world={system.transform.position:F3} size={main.startSize.constantMin:F3}..{main.startSize.constantMax:F3} " +
                $"3D={main.startSize3D} max={main.maxParticles} rate={system.emission.rateOverTime.constant:F2} " +
                $"lifetime={main.startLifetime.constant:F1} mesh={(mesh != null ? mesh.name + " ext=" + mesh.bounds.extents.ToString("F3") : "none")} " +
                $"material={(renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none")}");
        }

        foreach (string name in new[] { "Filtered Flow Outlet", "Coarse Flow Outlet", "Coarse Flow Center" })
        {
            Transform marker = Find(name);
            if (marker != null)
                report.AppendLine($"  {name} world={marker.position:F3}");
        }

        FilteredParticleFlow filtered = Object.FindObjectsByType<FilteredParticleFlow>(FindObjectsInactive.Include).FirstOrDefault();
        if (filtered != null)
            report.AppendLine($"  filtered: outletFallLifetime={filtered.outletFallLifetime} outletExitGravity={filtered.outletExitGravity} forward={filtered.outletExitForwardSpeed}");

        ControlledRockFlow coarse = Object.FindObjectsByType<ControlledRockFlow>(FindObjectsInactive.Include).FirstOrDefault();
        if (coarse != null)
            report.AppendLine($"  coarse: outletFallLifetime={coarse.outletFallLifetime} outletExitGravity={coarse.outletExitGravity} forward={coarse.outletExitForwardSpeed}");

        report.AppendLine("== SECTION");
        HybridHopperClipController clip = Object.FindObjectsByType<HybridHopperClipController>(FindObjectsInactive.Include).FirstOrDefault();
        if (clip != null)
        {
            SerializedObject data = new(clip);
            report.AppendLine($"  clip: full={data.FindProperty("fullViewClipX").floatValue} cross={data.FindProperty("crossSectionClipX").floatValue} " +
                              $"fullHopper={data.FindProperty("fullHopper").objectReferenceValue} cutHopper={data.FindProperty("cutHopper").objectReferenceValue}");
        }

        foreach (string name in new[] { "Hopper Cut Frame", "Full Model" })
        {
            Transform root = Find(name);
            if (root == null)
                continue;

            report.AppendLine($"  {name} parent={(root.parent != null ? root.parent.name : "<root>")} active={root.gameObject.activeSelf} pos={root.position:F3}");

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                string objectX = mesh != null ? $"objectX={mesh.bounds.min.x:F3}..{mesh.bounds.max.x:F3}" : "";
                report.AppendLine($"    {renderer.name} worldX={renderer.bounds.min.x:F3}..{renderer.bounds.max.x:F3} {objectX} " +
                                  $"mats=[{string.Join(", ", renderer.sharedMaterials.Select(m => m != null ? m.name : "NULL"))}]");
            }
        }

        System.IO.File.WriteAllText("Logs/ConveyorGeometryReport.txt", report.ToString());
        Debug.Log("CONVEYOR_GEOMETRY_REPORT -> Logs/ConveyorGeometryReport.txt");
    }

    /// <summary>How far the belt surface moves in metres per unit of each texture axis, on the top run.</summary>
    private static void DescribeBeltUv(Transform belt, StringBuilder report)
    {
        Mesh mesh = belt.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null)
            return;

        Vector3[] vertices = mesh.vertices;
        Vector2[] uvs = mesh.uv;
        Bounds bounds = belt.GetComponent<Renderer>().bounds;
        Vector3 axis = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;

        List<(float along, Vector2 uv)> top = new();
        for (int i = 0; i < vertices.Length && i < uvs.Length; i++)
        {
            Vector3 world = belt.TransformPoint(vertices[i]);
            if (world.y >= bounds.center.y + bounds.extents.y * 0.5f)
                top.Add((Vector3.Dot(world, axis), uvs[i]));
        }

        report.AppendLine($"  belt axis={axis} topVertices={top.Count} of {vertices.Length}");
        for (int component = 0; component < 2; component++)
        {
            (float slope, float r) = Fit(top.Select(v => (double)v.uv[component]).ToList(), top.Select(v => (double)v.along).ToList());
            report.AppendLine($"    uv.{(component == 0 ? "x" : "y")}: metresPerUv={slope:F4} r={r:F3}");
        }
    }

    /// <summary>Groups the roller vertices along the belt so the end roller can be told apart.</summary>
    private static void DescribeRollerClusters(Transform rollers, StringBuilder report)
    {
        Mesh mesh = rollers.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null)
            return;

        Bounds bounds = rollers.GetComponent<Renderer>().bounds;
        Vector3 axis = bounds.size.x >= bounds.size.z ? Vector3.right : Vector3.forward;
        List<float> along = mesh.vertices.Select(v => Vector3.Dot(rollers.TransformPoint(v), axis)).OrderBy(v => v).ToList();

        List<(float min, float max)> clusters = new();
        float start = along[0], previous = along[0];
        foreach (float value in along.Skip(1))
        {
            if (value - previous > 0.05f)
            {
                clusters.Add((start, previous));
                start = value;
            }
            previous = value;
        }
        clusters.Add((start, previous));

        report.AppendLine($"  rollers axis={axis} submeshes={mesh.subMeshCount} clusters={clusters.Count}: " +
                          string.Join(" ", clusters.Select(c => $"[{c.min:F2}..{c.max:F2}]")));
    }

    private static (float slope, float r) Fit(List<double> x, List<double> y)
    {
        int n = x.Count;
        if (n < 3)
            return (0f, 0f);

        double mx = x.Average(), my = y.Average(), sxy = 0, sxx = 0, syy = 0;
        for (int i = 0; i < n; i++)
        {
            sxy += (x[i] - mx) * (y[i] - my);
            sxx += (x[i] - mx) * (x[i] - mx);
            syy += (y[i] - my) * (y[i] - my);
        }

        if (sxx <= 0 || syy <= 0)
            return (0f, 0f);

        return ((float)(sxy / sxx), (float)(sxy / System.Math.Sqrt(sxx * syy)));
    }

    private static string PathOf(Transform t) =>
        t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
}
#endif
