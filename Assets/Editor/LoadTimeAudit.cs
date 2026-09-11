#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Structured load-time audit: records what the scene actually asks the player
/// to download and set up (unique meshes, their vertex layouts, renderer and
/// material counts, particle budgets) and then runs the release build so the
/// statistics can be read beside the build report's per-asset sizes.
///
/// Batch mode only. Report path comes from LOAD_AUDIT_REPORT.
/// </summary>
public static class LoadTimeAudit
{
    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogError("LoadTimeAudit.Run is batch-mode only.");
            return;
        }

        string reportPath = System.Environment.GetEnvironmentVariable("LOAD_AUDIT_REPORT")
                            ?? Path.Combine(Path.GetTempPath(), "load-audit.txt");

        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);

        StringBuilder report = new();
        WriteMeshSection(report);
        WriteRendererSection(report);
        WriteParticleSection(report);

        File.WriteAllText(reportPath, report.ToString());
        Debug.Log($"LOAD-AUDIT: report written to {reportPath}");

        WebGLBuildAudit.BuildRelease();
    }

    private static void WriteMeshSection(StringBuilder report)
    {
        Dictionary<Mesh, List<string>> users = new();

        foreach (MeshFilter filter in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
        {
            if (filter.sharedMesh == null)
                continue;

            if (!users.TryGetValue(filter.sharedMesh, out List<string> list))
                users[filter.sharedMesh] = list = new List<string>();

            list.Add(PathOf(filter.transform));
        }

        report.AppendLine("== MESHES (unique, used by scene) ==");
        report.AppendLine("verts\ttris\tsubmeshes\tstrideBytes\testMB\tattributes\tasset\tmesh\tusers");

        long totalVerts = 0;
        long totalTris = 0;

        foreach (KeyValuePair<Mesh, List<string>> pair in users.OrderByDescending(p => p.Key.vertexCount))
        {
            Mesh mesh = pair.Key;

            long tris = 0;
            for (int i = 0; i < mesh.subMeshCount; i++)
                tris += (long)mesh.GetIndexCount(i) / 3;

            int stride = 0;
            for (int i = 0; i < mesh.vertexBufferCount; i++)
                stride += mesh.GetVertexBufferStride(i);

            int indexBytes = mesh.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2;
            double estMb = (mesh.vertexCount * (double)stride + tris * 3 * indexBytes) / 1048576.0;

            string attributes = string.Join(",", mesh.GetVertexAttributes()
                .Select(a => $"{a.attribute}:{a.format}x{a.dimension}"));

            report.AppendLine($"{mesh.vertexCount}\t{tris}\t{mesh.subMeshCount}\t{stride}\t{estMb:F2}\t{attributes}\t" +
                              $"{AssetDatabase.GetAssetPath(mesh)}\t{mesh.name}\t{pair.Value.Count}:{string.Join(" | ", pair.Value.Take(3))}");

            totalVerts += mesh.vertexCount;
            totalTris += tris;
        }

        report.AppendLine($"TOTAL unique meshes={users.Count} verts={totalVerts} tris={totalTris}");
        report.AppendLine();
    }

    private static void WriteRendererSection(StringBuilder report)
    {
        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);

        int active = renderers.Count(r => r.enabled && r.gameObject.activeInHierarchy);
        int drawItems = renderers.Where(r => r.enabled && r.gameObject.activeInHierarchy)
                                 .Sum(r => r.sharedMaterials.Length);

        HashSet<Material> materials = new(renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null));

        report.AppendLine("== RENDERERS ==");
        report.AppendLine($"renderers total={renderers.Length} activeAtStart={active} materialSlotsActive={drawItems} uniqueMaterials={materials.Count}");

        report.AppendLine("material\tshader\ttextures");
        foreach (Material material in materials.OrderBy(m => m.name))
        {
            IEnumerable<string> textures = material.GetTexturePropertyNames()
                .Select(material.GetTexture)
                .Where(t => t != null)
                .Select(t => $"{t.name}({t.width}x{t.height})");

            report.AppendLine($"{material.name}\t{material.shader.name}\t{string.Join(", ", textures)}");
        }

        report.AppendLine();
    }

    private static void WriteParticleSection(StringBuilder report)
    {
        report.AppendLine("== PARTICLES ==");
        report.AppendLine("system\tactive\tmaxParticles\trenderMode\tmesh\tcollision\tshadows");

        foreach (ParticleSystem system in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include))
        {
            var renderer = system.GetComponent<ParticleSystemRenderer>();

            report.AppendLine($"{PathOf(system.transform)}\t{system.gameObject.activeInHierarchy}\t{system.main.maxParticles}\t" +
                              $"{(renderer != null ? renderer.renderMode.ToString() : "-")}\t" +
                              $"{(renderer != null && renderer.mesh != null ? renderer.mesh.name + ":" + renderer.mesh.vertexCount + "v" : "-")}\t" +
                              $"{system.collision.enabled}:{system.collision.type}\t" +
                              $"{(renderer != null ? renderer.shadowCastingMode.ToString() : "-")}");
        }

        report.AppendLine();
    }

    private static string PathOf(Transform t)
    {
        List<string> parts = new();

        for (Transform c = t; c != null; c = c.parent)
            parts.Insert(0, c.name);

        return string.Join("/", parts);
    }
}
#endif
