#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-only check of card-to-part highlighting. For every card shown on the
/// rails it outlines the parts that card's sensor is read from, in the Full
/// Body and Cross Section views, from the app's camera and from a close-up
/// along the same direction, beside an un-outlined reference frame. Logs the
/// card-to-part map. Never saves.
/// </summary>
public static class SelectionOutlineRenderTest
{
    private static readonly Color Blue = new(0.25f, 0.65f, 1f, 1f);

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
        Debug.Log($"OUTLINE-TEST: {groups.Length} part(s): " +
                  string.Join(", ", groups.Select(g => $"{g.DisplayName}[{g.Renderers.Length}]")));

        List<MachineSensorDefinition> cards = DigitalTwinMachineConfigurationLoader.LoadDefault()
            .sensors.Where(sensor => sensor.enabled).ToList();

        foreach (MachineSensorDefinition card in cards)
        {
            string[] parts = groups.Where(g => ListsSensor(g, card.id)).Select(g => g.DisplayName).ToArray();
            Debug.Log($"OUTLINE-TEST: card '{card.label}' -> {(parts.Length > 0 ? string.Join(" + ", parts) : "NO PART")}");
        }

        Vector3 appPosition = cam.transform.position;
        Quaternion appRotation = cam.transform.rotation;
        float appNearClip = cam.nearClipPlane;

        // Ambient lighting is not ready for the first batch frame.
        Render(cam, 64, 64);

        foreach (bool fullBody in new[] { true, false })
        {
            if (full != null) full.gameObject.SetActive(fullBody);
            if (cut != null) cut.gameObject.SetActive(!fullBody);
            string view = fullBody ? "full" : "cross";

            SelectionOutline.Clear();
            Save(Render(cam, 900, 700), dir, $"outline_{view}_none.png");

            foreach (MachineSensorDefinition card in cards)
            {
                List<Renderer> renderers = groups
                    .Where(g => ListsSensor(g, card.id))
                    .SelectMany(g => g.Renderers)
                    .ToList();

                if (renderers.Count == 0)
                    continue;

                SelectionOutline.SetTargets(renderers);
                SelectionOutline.SetAppearance(Blue, 1f);
                string file = card.label.Replace(' ', '_');

                Save(Render(cam, 900, 700), dir, $"outline_{view}_{file}.png");

                if (FrameCloseUp(cam, renderers, appRotation))
                    Save(Render(cam, 600, 600), dir, $"outline_{view}_{file}_closeup.png");

                cam.transform.SetPositionAndRotation(appPosition, appRotation);
                cam.nearClipPlane = appNearClip;
            }
        }

        SelectionOutline.Clear();
        Debug.Log("OUTLINE-TEST: done");
        EditorApplication.Exit(0);
    }

    private static bool ListsSensor(MachinePartGroup group, string sensorId)
    {
        SerializedProperty ids = new SerializedObject(group).FindProperty("sensorIds");

        for (int i = 0; i < ids.arraySize; i++)
        {
            if (string.Equals(ids.GetArrayElementAtIndex(i).stringValue, sensorId,
                    System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Moves in along the app camera's own direction, so the part is seen the
    // way the user sees it, only nearer.
    private static bool FrameCloseUp(Camera cam, List<Renderer> renderers, Quaternion rotation)
    {
        Bounds bounds = default;
        bool found = false;

        foreach (Renderer target in renderers)
        {
            if (target == null || !target.enabled || !target.gameObject.activeInHierarchy)
                continue;

            if (!found)
            {
                bounds = target.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(target.bounds);
            }
        }

        if (!found)
            return false;

        float radius = Mathf.Max(bounds.extents.magnitude, 0.05f);
        float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.8f;

        cam.nearClipPlane = 0.01f;
        cam.transform.SetPositionAndRotation(bounds.center - rotation * Vector3.forward * distance, rotation);
        return true;
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

    private static void Save(Texture2D tex, string dir, string file) =>
        File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
}
#endif
