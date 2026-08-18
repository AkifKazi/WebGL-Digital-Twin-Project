using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

public static class DigitalTwinProjectAudit
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject[] roots = scene.GetRootGameObjects();

        Debug.Log("HIERARCHY_AUDIT_BEGIN");
        foreach (GameObject root in roots)
            LogHierarchy(root.transform, 0);
        Debug.Log("HIERARCHY_AUDIT_END");

        UnityEngine.Object[] dependencies = EditorUtility.CollectDependencies(roots);

        var textures = dependencies
            .OfType<Texture>()
            .Select(texture => new
            {
                Texture = texture,
                Path = AssetDatabase.GetAssetPath(texture),
                Bytes = Profiler.GetRuntimeMemorySizeLong(texture)
            })
            .Where(item => !string.IsNullOrEmpty(item.Path))
            .OrderByDescending(item => item.Bytes)
            .ToArray();

        Debug.Log($"SCENE_DEPENDENCIES total={dependencies.Length} textures={textures.Length}");
        foreach (var item in textures.Take(40))
        {
            Debug.Log(
                $"SCENE_TEXTURE bytes={item.Bytes} size={item.Texture.width}x{item.Texture.height} " +
                $"format={item.Texture.graphicsFormat} path={item.Path}");
        }

        int missingScripts = roots.Sum(root => CountMissingScripts(root.transform));
        int behaviours = roots.Sum(root => root.GetComponentsInChildren<MonoBehaviour>(true).Length);
        int renderers = roots.Sum(root => root.GetComponentsInChildren<Renderer>(true).Length);
        Debug.Log(
            $"SCENE_AUDIT_SUMMARY roots={roots.Length} behaviours={behaviours} " +
            $"renderers={renderers} missingScripts={missingScripts}");

        foreach (Renderer renderer in roots.SelectMany(
                     root => root.GetComponentsInChildren<Renderer>(true)))
        {
            if (!renderer.enabled)
                Debug.Log($"DISABLED_RENDERER path={GetPath(renderer.transform)} type={renderer.GetType().Name}");
        }

        foreach (Behaviour behaviour in roots.SelectMany(
                     root => root.GetComponentsInChildren<Behaviour>(true)))
        {
            if (!behaviour.enabled)
                Debug.Log($"DISABLED_BEHAVIOUR path={GetPath(behaviour.transform)} type={behaviour.GetType().Name}");
        }
    }

    private static void LogHierarchy(Transform transform, int depth)
    {
        string componentNames = string.Join(
            ",",
            transform.GetComponents<Component>()
                .Where(component => component != null && component is not Transform)
                .Select(component => component.GetType().Name));

        Debug.Log(
            $"HIERARCHY depth={depth} active={transform.gameObject.activeSelf} " +
            $"path={GetPath(transform)} components=[{componentNames}]");

        for (int i = 0; i < transform.childCount; i++)
            LogHierarchy(transform.GetChild(i), depth + 1);
    }

    private static int CountMissingScripts(Transform transform)
    {
        int total = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
        for (int i = 0; i < transform.childCount; i++)
            total += CountMissingScripts(transform.GetChild(i));
        return total;
    }

    private static string GetPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}
