using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Batch-mode report of how the scene is wired to the imported models: every
/// object under the equipment root with its source model, materials and
/// components, plus every broken reference, missing prefab asset and missing
/// script in the scene. Read-only; the scene is never saved.
/// </summary>
public static class EquipmentLinkageReport
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string EquipmentRootName = "03 - Equipment";

    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        StringBuilder report = new();

        Transform equipment = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(t => t.name == EquipmentRootName);

        report.AppendLine("== EQUIPMENT HIERARCHY ==");
        if (equipment == null)
            report.AppendLine("equipment root missing");
        else
            Describe(equipment, 0, report);

        report.AppendLine("== BROKEN LINKS ==");
        int broken = 0;

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;

                if (PrefabUtility.IsPrefabAssetMissing(go))
                {
                    report.AppendLine($"MISSING PREFAB ASSET: {PathOf(t)}");
                    broken++;
                }

                int missingScripts = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (missingScripts > 0)
                {
                    report.AppendLine($"MISSING SCRIPT x{missingScripts}: {PathOf(t)}");
                    broken++;
                }

                foreach (Component component in go.GetComponents<Component>())
                {
                    if (component == null)
                        continue;

                    SerializedProperty property = new SerializedObject(component).GetIterator();

                    while (property.Next(true))
                    {
                        if (property.propertyType != SerializedPropertyType.ObjectReference)
                            continue;

                        // A non-zero instance id with no object is a reference whose
                        // target no longer exists - what the Inspector shows as "Missing".
                        // The id accessor is deprecated but still the reliable test for it.
#pragma warning disable CS0618
                        bool missing = property.objectReferenceValue == null &&
                                       property.objectReferenceInstanceIDValue != 0;
#pragma warning restore CS0618
                        if (missing)
                        {
                            report.AppendLine($"MISSING REFERENCE: {PathOf(t)} [{component.GetType().Name}] {property.propertyPath}");
                            broken++;
                        }
                    }
                }
            }
        }

        report.AppendLine($"LINKAGE_REPORT broken={broken}");
        System.IO.File.WriteAllText("Logs/EquipmentLinkageReport.txt", report.ToString());
        Debug.Log($"LINKAGE_REPORT broken={broken} -> Logs/EquipmentLinkageReport.txt");
    }

    private static void Describe(Transform t, int depth, StringBuilder report)
    {
        GameObject go = t.gameObject;
        string source = "";

        if (PrefabUtility.IsAnyPrefabInstanceRoot(go))
            source = $"  <= {AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go))}";

        string materials = "";
        Renderer renderer = go.GetComponent<Renderer>();

        if (renderer != null)
        {
            MeshFilter filter = go.GetComponent<MeshFilter>();
            string mesh = filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "NO MESH";
            materials = $"  mesh={mesh} mats=[{string.Join(", ", renderer.sharedMaterials.Select(m => m != null ? m.name : "NULL"))}]" +
                        (renderer.enabled ? "" : " (renderer off)");
        }

        IEnumerable<string> components = go.GetComponents<Component>()
            .Where(c => c != null && !(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer))
            .Select(c => c.GetType().Name);

        string extra = string.Join(",", components);
        report.AppendLine(
            $"{new string(' ', depth * 2)}{t.name}{(go.activeSelf ? "" : " (inactive)")}{source}{materials}" +
            (extra.Length > 0 ? $"  {{{extra}}}" : ""));

        foreach (Transform child in t)
            Describe(child, depth + 1, report);
    }

    private static string PathOf(Transform t) =>
        t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
}
