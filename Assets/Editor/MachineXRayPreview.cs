#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Offscreen turntable capture of the X-Ray view, so the look can be checked
/// without entering Play mode. Writes PNGs to XRAY_PREVIEW_DIR.
/// </summary>
public static class MachineXRayPreview
{
    /// <summary>
    /// Batch mode only: this replaces materials and reopens the scene, which
    /// would discard unsaved editor work. Run it with
    /// -batchmode -executeMethod MachineXRayPreview.Capture.
    /// </summary>
    public static void Capture()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogError("MachineXRayPreview.Capture is batch-mode only; it reopens the scene and swaps materials.");
            return;
        }

        string outputDir = System.Environment.GetEnvironmentVariable("XRAY_PREVIEW_DIR")
                           ?? Path.Combine(Path.GetTempPath(), "xray-preview");

        Directory.CreateDirectory(outputDir);

        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);

        Material material = MachineXRaySceneSetup.GetOrCreateMaterial();
        MachineXRaySceneSetup.ApplyPreset(material);

        GameObject equipment = Object.FindObjectsByType<GameObject>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(go => go.name == "03 - Equipment");

        if (equipment == null)
        {
            Debug.LogError("XRAY-PREVIEW: equipment root not found.");
            EditorApplication.Exit(5);
            return;
        }

        // Show the cutaway model, which is what X-Ray mode inspects.
        Transform full = equipment.transform.Find("Hopper Assembly/Full Model");
        Transform cut = equipment.transform.Find("Hopper Assembly/Cross-Section Model");

        if (full != null) full.gameObject.SetActive(false);
        if (cut != null) cut.gameObject.SetActive(true);

        const int previewLayer = 31;
        Bounds bounds = default;
        bool boundsValid = false;

        var renderers = equipment.GetComponentsInChildren<MeshRenderer>(false);

        foreach (MeshRenderer r in renderers)
        {
            Material[] slots = new Material[r.sharedMaterials.Length];

            for (int i = 0; i < slots.Length; i++)
                slots[i] = material;

            r.sharedMaterials = slots;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.gameObject.layer = previewLayer;

            if (!boundsValid)
            {
                bounds = r.bounds;
                boundsValid = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        Shader.SetGlobalFloat("_XRayOpacity", 1f);
        Shader.SetGlobalFloat("_XRayReveal", 1f);
        Shader.SetGlobalFloat("_XRayRevealActive", 0f);
        Shader.SetGlobalFloat("_XRayScanY", bounds.center.y);
        Shader.SetGlobalFloat("_XRayScanWidth", bounds.size.y * 0.06f);
        Shader.SetGlobalFloat("_XRayScanIntensity", 0.8f);

        Camera cam = Camera.main;

        if (cam == null)
        {
            Debug.LogError("XRAY-PREVIEW: no main camera.");
            EditorApplication.Exit(7);
            return;
        }

        cam.cullingMask = 1 << previewLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.008f, 0.014f, 0.024f, 1f);
        cam.allowHDR = true;
        cam.fieldOfView = 34f;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 300f;

        float distance = bounds.extents.magnitude /
                         Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;

        foreach (int angle in new[] { 35, 135 })
        {
            Quaternion orbit = Quaternion.Euler(16f, angle, 0f);
            cam.transform.position = bounds.center + orbit * new Vector3(0f, 0f, -distance);
            cam.transform.LookAt(bounds.center);

            Render(cam, Path.Combine(outputDir, $"machine_xray_{angle:D3}.png"));
        }

        // Low, close angle looking up through the chute: the view where grazing
        // panels flare worst, so glare tuning can be judged on the hard case.
        cam.transform.position = bounds.center + new Vector3(0.35f, -0.30f, -0.55f) * distance;
        cam.transform.LookAt(bounds.center + new Vector3(0f, bounds.extents.y * 0.35f, 0f));
        Render(cam, Path.Combine(outputDir, "machine_xray_glare.png"));

        // Alarm shot: colour the vibratory drive critical and the conveyor warning.
        Quaternion heroOrbit = Quaternion.Euler(16f, 35f, 0f);
        cam.transform.position = bounds.center + heroOrbit * new Vector3(0f, 0f, -distance);
        cam.transform.LookAt(bounds.center);

        TintMechanism(equipment, "Vibratory Drive", new Color(1f, 0.26f, 0.22f), 1f);
        TintMechanism(equipment, "Conveyors", new Color(1f, 0.72f, 0.18f), 0.75f);

        Render(cam, Path.Combine(outputDir, "machine_xray_alarms.png"));

        // Hover isolation: focus the drive motor, dissolve everything else, and
        // keep the alarmed conveyor pinned lit the way the runtime rule does.
        ClearStatus(equipment);
        SetFocusDim(equipment, "Hopper Assembly", 1f);
        SetFocusDim(equipment, "Vibratory Drive/Upper Rotor", 1f);
        SetFocusDim(equipment, "Vibratory Drive/Motor", 0f);
        SetFocusDim(equipment, "Vibratory Drive/Lower Rotor", 0f);

        SetFocusDim(equipment, "Conveyors", 0f);
        TintMechanism(equipment, "Conveyors", new Color(1f, 0.26f, 0.22f), 1f);

        cam.transform.position = bounds.center + heroOrbit * new Vector3(0f, 0f, -distance * 0.55f);
        cam.transform.LookAt(bounds.center - new Vector3(0f, bounds.extents.y * 0.35f, 0f));

        Render(cam, Path.Combine(outputDir, "machine_xray_isolated.png"));

        // Objective isolation check: identical camera, whole machine lit, then
        // whole machine ghosted. The ratio is the actual reduction achieved.
        ClearStatus(equipment);

        cam.transform.position = bounds.center + heroOrbit * new Vector3(0f, 0f, -distance);
        cam.transform.LookAt(bounds.center);

        SetFocusDimAll(equipment, 0f);
        double lit = Measure(cam);

        SetFocusDimAll(equipment, 1f);
        double ghosted = Measure(cam);

        SetFocusDimAll(equipment, 0f);

        Debug.Log($"XRAY-MEASURE: lit={lit:F3} ghosted={ghosted:F3} reduction={(1.0 - ghosted / System.Math.Max(lit, 1e-6)) * 100.0:F1}%");

        Debug.Log("XRAY-PREVIEW: done");
        EditorApplication.Exit(0);
    }

    private static void SetFocusDim(GameObject equipment, string path, float dim)
    {
        Transform target = equipment.transform.Find(path);

        if (target == null)
            return;

        MaterialPropertyBlock block = new();

        foreach (Renderer r in target.GetComponentsInChildren<Renderer>(false))
        {
            r.GetPropertyBlock(block);
            block.SetFloat("_FocusDim", dim);
            r.SetPropertyBlock(block);
        }
    }

    private static void SetFocusDimAll(GameObject equipment, float dim)
    {
        MaterialPropertyBlock block = new();

        foreach (Renderer r in equipment.GetComponentsInChildren<Renderer>(false))
        {
            r.GetPropertyBlock(block);
            block.SetFloat("_FocusDim", dim);
            r.SetPropertyBlock(block);
        }
    }

    private static double Measure(Camera cam)
    {
        const int width = 640;
        const int height = 480;

        RenderTexture rt = new(width, height, 24, RenderTextureFormat.DefaultHDR);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;

        Texture2D shot = new(width, height, TextureFormat.RGBA32, false);
        shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        shot.Apply();

        RenderTexture.active = null;
        cam.targetTexture = null;

        double total = 0;

        foreach (Color32 pixel in shot.GetPixels32())
            total += pixel.r + pixel.g + pixel.b;

        Object.DestroyImmediate(shot);
        rt.Release();
        Object.DestroyImmediate(rt);

        return total / (width * (double)height);
    }

    private static void ClearStatus(GameObject equipment)
    {
        MaterialPropertyBlock block = new();

        foreach (Renderer r in equipment.GetComponentsInChildren<Renderer>(false))
        {
            r.GetPropertyBlock(block);
            block.SetFloat("_StatusBlend", 0f);
            r.SetPropertyBlock(block);
        }
    }

    private static void TintMechanism(GameObject equipment, string mechanism, Color color, float blend)
    {
        Transform target = equipment.transform.Find(mechanism);

        if (target == null)
            return;

        MaterialPropertyBlock block = new();

        foreach (Renderer r in target.GetComponentsInChildren<Renderer>(false))
        {
            r.GetPropertyBlock(block);
            block.SetColor("_StatusColor", color);
            block.SetFloat("_StatusBlend", blend);
            r.SetPropertyBlock(block);
        }
    }

    private static void Render(Camera cam, string path)
    {
        const int width = 1280;
        const int height = 900;

        RenderTexture rt = new(width, height, 24, RenderTextureFormat.DefaultHDR);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;

        Texture2D shot = new(width, height, TextureFormat.RGBA32, false);
        shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        shot.Apply();

        RenderTexture.active = null;
        cam.targetTexture = null;

        Color32[] pixels = shot.GetPixels32();
        long total = 0;
        int peak = 0;

        foreach (Color32 pixel in pixels)
        {
            int luma = pixel.r + pixel.g + pixel.b;
            total += luma;
            peak = Mathf.Max(peak, luma);
        }

        Debug.Log($"XRAY-PREVIEW: {Path.GetFileName(path)} mean={(total / (double)pixels.Length):F2} peak={peak}");

        File.WriteAllBytes(path, shot.EncodeToPNG());

        Object.DestroyImmediate(shot);
        rt.Release();
        Object.DestroyImmediate(rt);
    }
}
#endif
