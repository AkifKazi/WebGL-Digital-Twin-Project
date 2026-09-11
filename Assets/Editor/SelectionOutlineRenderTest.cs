#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-only check of the selection outline. Renders each mechanism outlined
/// from the app's camera in the Full Body and Cross Section views, beside an
/// un-outlined reference frame, so the renderer feature can be seen drawing and
/// occluded parts can be seen outlined. Never saves.
/// </summary>
public static class SelectionOutlineRenderTest
{
    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogError("SelectionOutlineRenderTest.Run is batch-mode only.");
            return;
        }

        string dir = System.Environment.GetEnvironmentVariable("RENDER_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);

        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);

        Camera cam = Camera.main;
        Transform equipment = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .First(t => t.name == "03 - Equipment");
        Transform full = equipment.Find("Hopper Assembly/Full Model");
        Transform cut = equipment.Find("Hopper Assembly/Cross-Section Model");

        Transform ui = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
            .FirstOrDefault(t => t.name == "06 - User Interface");
        if (ui != null) ui.gameObject.SetActive(false);

        MachinePartGroup[] groups = Object.FindObjectsByType<MachinePartGroup>(FindObjectsInactive.Include);
        Debug.Log($"OUTLINE-TEST: {groups.Length} mechanism(s): " +
                  string.Join(", ", groups.Select(g => $"{g.DisplayName}[{g.Renderers.Length}]")));

        // Ambient lighting is not ready for the first batch frame.
        Render(cam, 64, 64);

        foreach (bool fullBody in new[] { true, false })
        {
            if (full != null) full.gameObject.SetActive(fullBody);
            if (cut != null) cut.gameObject.SetActive(!fullBody);
            string view = fullBody ? "full" : "cross";

            SelectionOutline.Clear();
            Texture2D reference = Render(cam, 900, 700);
            Save(reference, dir, $"outline_{view}_none.png");

            foreach (MachinePartGroup group in groups)
            {
                // The conveyor carries the alarm demonstration, so it is shown
                // in the critical colour the highlighter would use.
                Color color = group.DisplayName.Contains("Conveyor")
                    ? new Color(1f, 0.26f, 0.22f, 1f)
                    : new Color(0.25f, 0.65f, 1f, 1f);

                SelectionOutline.SetTargets(group.Renderers);
                SelectionOutline.SetAppearance(color, 1f);

                Texture2D shot = Render(cam, 900, 700);
                Save(shot, dir, $"outline_{view}_{group.DisplayName.Replace(' ', '_')}.png");
                Debug.Log($"OUTLINE-TEST: {view} '{group.DisplayName}' mean difference vs no outline = {MeanDiff(reference, shot):F4}");
            }
        }

        SelectionOutline.Clear();
        Debug.Log("OUTLINE-TEST: done");
        EditorApplication.Exit(0);
    }

    private static Texture2D Render(Camera cam, int w, int h)
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

    private static float MeanDiff(Texture2D a, Texture2D b)
    {
        Color[] pa = a.GetPixels(), pb = b.GetPixels();
        double sum = 0;
        for (int i = 0; i < pa.Length; i++)
            sum += Mathf.Abs(pa[i].r - pb[i].r) + Mathf.Abs(pa[i].g - pb[i].g) + Mathf.Abs(pa[i].b - pb[i].b);
        return (float)(sum / (pa.Length * 3));
    }

    private static void Save(Texture2D tex, string dir, string file) =>
        File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
}
#endif
