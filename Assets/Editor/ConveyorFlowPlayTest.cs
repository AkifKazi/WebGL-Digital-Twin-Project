using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Batch-only check of the conveyor rock flow in play mode. Plays the scene,
/// and at each checkpoint logs how many rocks landed and ride on each belt,
/// how fast they move and where they are, then renders the app view and a
/// close-up of each belt. Exits the editor when done. Never saves.
///
/// Run without -quit: the editor has to stay open while play mode runs.
/// </summary>
[InitializeOnLoad]
public static class ConveyorFlowPlayTest
{
    private const string ActiveKey = "ConveyorFlowPlayTest.Active";
    private const string StartKey = "ConveyorFlowPlayTest.Start";
    private static readonly float[] Checkpoints = { 5f, 12f, 30f };
    private static int next;

    static ConveyorFlowPlayTest()
    {
        if (SessionState.GetBool(ActiveKey, false))
            EditorApplication.update += Tick;
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity", OpenSceneMode.Single);
        SessionState.SetBool(ActiveKey, true);
        SessionState.SetFloat(StartKey, (float)EditorApplication.timeSinceStartup);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (EditorApplication.timeSinceStartup - SessionState.GetFloat(StartKey, 0f) > 300f)
        {
            Finish("timed out");
            return;
        }

        if (!EditorApplication.isPlaying)
            return;

        if (next >= Checkpoints.Length)
        {
            Finish("done");
            return;
        }

        if (Time.time < Checkpoints[next])
            return;

        Capture(Checkpoints[next]);
        next++;
    }

    private static void Capture(float checkpoint)
    {
        string dir = System.Environment.GetEnvironmentVariable("RENDER_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);

        foreach (ConveyorRockTransfer transfer in Object.FindObjectsByType<ConveyorRockTransfer>(FindObjectsInactive.Include))
        {
            ParticleSystem system = transfer.GetComponent<ParticleSystem>();
            ParticleSystem.Particle[] rocks = new ParticleSystem.Particle[system.main.maxParticles];
            int count = system.GetParticles(rocks);

            string spread = "none";
            if (count > 0)
            {
                var riding = rocks.Take(count).ToArray();
                float minX = riding.Min(r => r.position.x), maxX = riding.Max(r => r.position.x);
                float minY = riding.Min(r => r.position.y), maxY = riding.Max(r => r.position.y);
                float minZ = riding.Min(r => r.position.z), maxZ = riding.Max(r => r.position.z);
                float speed = riding.Average(r => new Vector3(r.velocity.x, 0f, r.velocity.z).magnitude);
                spread = $"x={minX:F2}..{maxX:F2} y={minY:F3}..{maxY:F3} z={minZ:F2}..{maxZ:F2} meanGroundSpeed={speed:F2}";
            }

            // Falling rocks already below the belt top missed the belt entirely.
            SerializedObject setup = new(transfer);
            Transform marker = setup.FindProperty("landingPoint").objectReferenceValue as Transform;
            float beltTop = marker != null ? marker.position.y + setup.FindProperty("surfaceOffset").floatValue : 0f;
            SerializedProperty feeds = setup.FindProperty("feedSystems");
            int missed = 0;
            for (int f = 0; f < feeds.arraySize; f++)
            {
                if (feeds.GetArrayElementAtIndex(f).objectReferenceValue is not ParticleSystem feed)
                    continue;

                ParticleSystem.Particle[] falling = new ParticleSystem.Particle[feed.main.maxParticles];
                int fallingCount = feed.GetParticles(falling);
                for (int i = 0; i < fallingCount; i++)
                    if (falling[i].position.y < beltTop - 0.05f)
                        missed++;
            }

            Debug.Log($"PLAY-TEST t={Time.time:F1}s {transfer.name}: landed={transfer.LandedTotal} carried={transfer.CarriedTotal} " +
                      $"riding={count} fallingBelowBelt={missed} rockSpeed={transfer.RockSpeed:F2} {spread}");
        }

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Vector3 position = cam.transform.position;
        Quaternion rotation = cam.transform.rotation;
        float near = cam.nearClipPlane;
        int stamp = Mathf.RoundToInt(checkpoint);

        Save(Render(cam, 1280, 720), dir, $"play_{stamp:00}s_app.png");

        // Close-ups from beside and above each belt's landing zone.
        foreach (ConveyorRockTransfer transfer in Object.FindObjectsByType<ConveyorRockTransfer>(FindObjectsInactive.Include))
        {
            SerializedObject data = new(transfer);
            Transform landing = data.FindProperty("landingPoint").objectReferenceValue as Transform;
            Transform removal = data.FindProperty("killPoint").objectReferenceValue as Transform;

            if (landing == null || removal == null)
                continue;

            Vector3 run = removal.position - landing.position;
            run.y = 0f;
            Vector3 along = run.normalized;
            Vector3 side = Vector3.Cross(Vector3.up, along);
            Vector3 outward = Vector3.Dot(side, landing.position) >= 0f ? side : -side;
            // Above and behind the landing zone, over the outer skirt, looking
            // down the belt the way the rocks travel.
            Vector3 focus = landing.position + along * 1.4f;

            cam.nearClipPlane = 0.02f;
            cam.transform.position = landing.position - along * 0.9f + outward * 0.45f + Vector3.up * 0.85f;
            cam.transform.LookAt(focus);
            Save(Render(cam, 1100, 620), dir, $"play_{stamp:00}s_{transfer.name.Replace(' ', '_')}.png");
        }

        cam.transform.SetPositionAndRotation(position, rotation);
        cam.nearClipPlane = near;
    }

    private static void Finish(string reason)
    {
        Debug.Log($"PLAY-TEST {reason}");
        EditorApplication.update -= Tick;
        SessionState.EraseBool(ActiveKey);
        SessionState.EraseFloat(StartKey);
        EditorApplication.Exit(0);
    }

    private static Texture2D Render(Camera cam, int w, int h)
    {
        RenderTexture rt = new(w, h, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = cam.targetTexture;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;

        Texture2D shot = new(w, h, TextureFormat.RGBA32, false);
        shot.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        shot.Apply();

        RenderTexture.active = null;
        cam.targetTexture = previous;
        rt.Release();
        Object.Destroy(rt);
        return shot;
    }

    private static void Save(Texture2D tex, string dir, string file) =>
        File.WriteAllBytes(Path.Combine(dir, file), tex.EncodeToPNG());
}
