#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Batch-only inspection renders. Produces the view states, a mid-wipe frame
/// to check the cross-section transition, and per-group contact sheets that
/// show every part isolated and marked in place with an editor-style
/// selection outline and faint fill drawn through occluders. Never saves.
/// </summary>
public static class ModelInspectionRenders
{
    private const int Tile = 220;
    private static readonly Color SelectionTint = new(0.25f, 0.65f, 1f);

    private static string outDir;
    private static Camera cam;

    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogError("ModelInspectionRenders.Run is batch-mode only.");
            return;
        }

        outDir = System.Environment.GetEnvironmentVariable("RENDER_DIR") ?? Path.Combine(Path.GetTempPath(), "renders");
        Directory.CreateDirectory(outDir);

        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);

        Transform equipment = FindTransform("03 - Equipment");
        Transform full = FindTransform("full model of hopper");
        Transform cut = FindTransform("Cut detailed hopper");

        foreach (string hide in new[] { "04 - Environment", "05 - Effects", "06 - User Interface" })
            SetActive(FindTransform(hide), false);

        GameObject host = new("InspectionCamera");
        cam = host.AddComponent<Camera>();
        host.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f, 1f);
        cam.fieldOfView = 30f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 500f;

        // The first batch render happens before ambient lighting is ready, so
        // one frame is rendered and thrown away before anything is measured.
        Bounds hopper = BoundsOf(equipment.Find("Hopper Assembly"), cut, full);
        RenderFramed(hopper, 35f, 18f, 1f, 64, 64);

        // Wipe test: the full model alone at the start and the end of the
        // transition. If its materials ignore the clip, the two are identical.
        SetActive(full, true); SetActive(cut, false);
        SetClip(full, 1.5f);
        Texture2D wipeStart = RenderFromApp(900, 700);
        SetClip(full, 0.14f);
        Texture2D wipeEnd = RenderFromApp(900, 700);
        Save(wipeStart, "wipe_start_fullonly.png");
        Save(wipeEnd, "wipe_end_fullonly.png");
        Debug.Log($"INSPECT: full model alone, clip 1.5 vs 0.14, mean abs difference = {MeanDiff(wipeStart, wipeEnd):F4}");
        SetClip(full, 1.5f);

        // The three states from the app's own starting camera.
        SetActive(full, true); SetActive(cut, false);
        Save(RenderFromApp(900, 700), "app_full.png");
        SetActive(full, false); SetActive(cut, true);
        Save(RenderFromApp(900, 700), "app_cross.png");
        SetActive(full, true); SetActive(cut, true); SetClip(full, 0.8f);
        Save(RenderFromApp(900, 700), "app_wipe_mid.png");
        SetClip(full, 1.5f);

        // Contact sheets.
        Material maskMaterial = new(Shader.Find("Universal Render Pipeline/Unlit"));
        maskMaterial.SetColor("_BaseColor", Color.white);

        SetActive(full, false); SetActive(cut, true);
        // Looked up under the equipment root: telemetry category folders share
        // some of these names and hold no geometry.
        foreach (string group in new[] { "Cut detailed hopper", "Conveyor", "motor", "Hopper Assembly" })
            ContactSheet(equipment.Find(group), equipment, maskMaterial);

        SetActive(full, true); SetActive(cut, false);
        ContactSheet(full, equipment, maskMaterial);

        Debug.Log("INSPECT: done");
        EditorApplication.Exit(0);
    }

    private static void ContactSheet(Transform group, Transform equipment, Material maskMaterial)
    {
        if (group == null) return;

        List<MeshRenderer> parts = group.GetComponentsInChildren<MeshRenderer>(false).ToList();

        if (parts.Count == 0)
        {
            Debug.LogWarning($"INSPECT: '{group.name}' has no visible parts, sheet skipped.");
            return;
        }
        List<MeshRenderer> everything = equipment.GetComponentsInChildren<MeshRenderer>(false).ToList();

        Bounds context = BoundsOf(group);
        context.Expand(context.size.magnitude * 0.25f);

        const int pairsPerRow = 3;
        int rows = Mathf.CeilToInt(parts.Count / (float)pairsPerRow);
        Texture2D sheet = new(Tile * 2 * pairsPerRow, Tile * rows, TextureFormat.RGBA32, false);
        sheet.SetPixels32(Enumerable.Repeat(new Color32(0, 0, 0, 255), sheet.width * sheet.height).ToArray());

        StringBuilder legend = new();

        for (int i = 0; i < parts.Count; i++)
        {
            MeshRenderer part = parts[i];

            // Isolated.
            foreach (MeshRenderer r in everything) r.enabled = r == part;
            Texture2D isolated = RenderFramed(part.bounds, 35f, 20f, 1.15f, Tile, Tile);

            // In place, marked like an editor selection.
            foreach (MeshRenderer r in everything) r.enabled = true;
            Texture2D inPlace = RenderFramed(context, 35f, 20f, 1f, Tile, Tile);

            Material[] original = part.sharedMaterials;
            foreach (MeshRenderer r in everything) r.enabled = r == part;
            part.sharedMaterials = Enumerable.Repeat(maskMaterial, original.Length).ToArray();
            Color background = cam.backgroundColor;
            cam.backgroundColor = Color.black;
            Texture2D mask = RenderFramed(context, 35f, 20f, 1f, Tile, Tile);
            cam.backgroundColor = background;
            part.sharedMaterials = original;
            foreach (MeshRenderer r in everything) r.enabled = true;

            CompositeSelection(inPlace, mask, SelectionTint, 0.28f, 2);

            int col = i % pairsPerRow;
            int row = i / pairsPerRow;
            int y = sheet.height - (row + 1) * Tile;

            sheet.SetPixels(col * Tile * 2, y, Tile, Tile, isolated.GetPixels());
            sheet.SetPixels(col * Tile * 2 + Tile, y, Tile, Tile, inPlace.GetPixels());

            Mesh mesh = part.GetComponent<MeshFilter>()?.sharedMesh;
            long tris = mesh != null ? Enumerable.Range(0, mesh.subMeshCount).Sum(s => (long)mesh.GetIndexCount(s)) / 3 : 0;
            legend.AppendLine($"{i:D2} row{row} col{col}\t{part.name}\tmat={string.Join(",", original.Select(m => m != null ? m.name : "null"))}\t" +
                              $"size={part.bounds.size.x:F2}x{part.bounds.size.y:F2}x{part.bounds.size.z:F2}\t" +
                              $"center={part.bounds.center.x:F2},{part.bounds.center.y:F2},{part.bounds.center.z:F2}\ttris={tris}");
        }

        sheet.Apply();
        string safe = group.name.Replace(' ', '_');
        Save(sheet, $"sheet_{safe}.png");
        File.WriteAllText(Path.Combine(outDir, $"sheet_{safe}.txt"), legend.ToString());
    }

    /// <summary>Outline where the mask ends, faint fill where it covers.</summary>
    private static void CompositeSelection(Texture2D target, Texture2D mask, Color tint, float fill, int radius)
    {
        int w = target.width, h = target.height;
        Color[] c = target.GetPixels();
        Color[] m = mask.GetPixels();
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && m[y * w + x].r > 0.5f;

        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            if (Inside(x, y)) { c[i] = Color.Lerp(c[i], tint, fill); continue; }

            bool edge = false;
            for (int dy = -radius; dy <= radius && !edge; dy++)
            for (int dx = -radius; dx <= radius && !edge; dx++)
                if (dx * dx + dy * dy <= radius * radius && Inside(x + dx, y + dy)) edge = true;

            if (edge) c[i] = tint;
        }

        target.SetPixels(c);
        target.Apply();
    }

    /// <summary>Renders from the Main Camera's authored pose, the view users start in.</summary>
    private static Texture2D RenderFromApp(int w, int h)
    {
        Camera app = Camera.main;

        if (app != null)
        {
            cam.transform.SetPositionAndRotation(app.transform.position, app.transform.rotation);
            cam.fieldOfView = app.fieldOfView;
        }

        Texture2D shot = Capture(w, h);
        cam.fieldOfView = 30f;
        return shot;
    }

    private static Texture2D RenderFramed(Bounds bounds, float yaw, float pitch, float margin, int w, int h)
    {
        float radius = Mathf.Max(bounds.extents.magnitude, 0.01f);
        float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * margin;
        cam.transform.position = bounds.center + Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -distance);
        cam.transform.LookAt(bounds.center);
        return Capture(w, h);
    }

    private static Texture2D Capture(int w, int h)
    {
        RenderTexture rt = new(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;

        Texture2D shot = new(w, h, TextureFormat.RGBA32, false);
        shot.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        shot.Apply();

        RenderTexture.active = null;
        cam.targetTexture = null;
        rt.Release();
        Object.DestroyImmediate(rt);
        return shot;
    }

    private static void SetClip(Transform root, float clipX)
    {
        if (root == null) return;
        MaterialPropertyBlock block = new();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            r.GetPropertyBlock(block);
            block.SetFloat("_ClipX", clipX);
            block.SetFloat("_ClipEnabled", 1f);
            r.SetPropertyBlock(block);
        }
    }

    private static float MeanDiff(Texture2D a, Texture2D b)
    {
        Color[] pa = a.GetPixels(), pb = b.GetPixels();
        double sum = 0;
        for (int i = 0; i < pa.Length; i++)
            sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
        return (float)(sum / (pa.Length * 3));
    }

    private static Bounds BoundsOf(params Transform[] roots)
    {
        bool any = false;
        Bounds b = default;
        foreach (Transform root in roots)
        {
            if (root == null) continue;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
        }
        return any ? b : new Bounds(Vector3.zero, Vector3.one);
    }

    private static void SetActive(Transform t, bool active)
    {
        if (t != null) t.gameObject.SetActive(active);
    }

    private static Transform FindTransform(string name) =>
        Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).FirstOrDefault(t => t.name == name);

    private static void Save(Texture2D tex, string file) =>
        File.WriteAllBytes(Path.Combine(outDir, file), tex.EncodeToPNG());
}
#endif
