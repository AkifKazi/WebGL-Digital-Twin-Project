using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the X-Ray inspection view from the machine configuration.
///
/// This runs as part of <see cref="DigitalTwinSceneSetup"/>, so adopting a new
/// machine needs no extra steps: declare the mechanisms in the configuration
/// asset and the rig, the alarm binding, and the third segment are created.
///
/// All of this is editor-time on purpose. Resolving hierarchy and building
/// components at startup would cost WebGL load time for something that never
/// changes while the application runs.
/// </summary>
public static class MachineXRaySceneSetup
{
    private const string ShaderName = "Digital Twin/Machine X-Ray";
    private const string MaterialPath = "Assets/Materials/X-Ray/Machine X-Ray.mat";
    private const string OverlayMaterialPath = "Assets/Materials/X-Ray/Machine X-Ray Overlay.mat";
    private const string ControllerName = "X-Ray View Controller";
    private const string EquipmentRootName = "03 - Equipment";
    private const string EnvironmentRootName = "04 - Environment";
    private const string EffectsRootName = "05 - Effects";

    // Portrait has room for three segments only with short copy and no icons.
    private const string PortraitLayoutName = "Portrait layout";
    private const float WideControlWidth = 940f;
    private const float PortraitButtonWidth = 150f;

    // Portrait fills the safe area minus padding; the cap only prevents the
    // control stretching absurdly wide on a tablet held upright.
    private const float PortraitMaxWidth = 1100f;

    public static void Apply(DigitalTwinMachineConfiguration configuration)
    {
        Material material = GetOrCreateMaterial();

        if (material == null)
            return;

        GameObject equipmentRoot = FindSceneObject(EquipmentRootName);

        if (equipmentRoot == null)
        {
            Debug.LogWarning($"X-Ray setup skipped: '{EquipmentRootName}' is not in the scene.");
            return;
        }

        List<MachinePartGroup> groups = CreatePartGroups(configuration, equipmentRoot);

        GameObject host = FindSceneObject(ControllerName);

        if (host == null)
        {
            host = new GameObject(ControllerName);
            Undo.RegisterCreatedObjectUndo(host, "Create X-Ray view controller");

            Transform runtimeRoot = FindSceneObject("Digital Twin Runtime")?.transform;

            if (runtimeRoot != null)
                host.transform.SetParent(runtimeRoot, true);
        }

        Material overlayMaterial = GetOrCreateOverlayMaterial();

        MachineXRayPresenter presenter = GetOrAddComponent<MachineXRayPresenter>(host);
        XRayEnvironmentController environment = GetOrAddComponent<XRayEnvironmentController>(host);
        MachineViewModeController viewMode = GetOrAddComponent<MachineViewModeController>(host);

        ConfigurePresenter(presenter, material, equipmentRoot, groups);
        ConfigureEnvironment(environment);
        ConfigureViewMode(viewMode, presenter, environment);
        ConfigureSegmentedControls(viewMode);

        MachineHoverOverlay overlay = GetOrAddComponent<MachineHoverOverlay>(host);
        ConfigureHoverOverlay(overlay, overlayMaterial, viewMode, groups);

        Debug.Log($"X-Ray view configured with {groups.Count} mechanism(s).", host);
    }

    // -----------------------------------------------------------------------
    // Material
    // -----------------------------------------------------------------------

    public static Material GetOrCreateMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);

        if (existing != null)
            return existing;

        Shader shader = Shader.Find(ShaderName);

        if (shader == null)
        {
            Debug.LogError($"Shader '{ShaderName}' was not found.");
            return null;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));

        Material material = new(shader) { name = "Machine X-Ray" };
        ApplyPreset(material);

        AssetDatabase.CreateAsset(material, MaterialPath);
        AssetDatabase.SaveAssets();

        return material;
    }

    /// <summary>Blue glass shell with cyan-white grazing edges, tuned for a near-black stage.</summary>
    public static void ApplyPreset(Material material)
    {
        material.SetColor("_BaseColor", new Color(0.08f, 0.42f, 0.85f));
        material.SetColor("_EdgeColor", new Color(0.72f, 0.93f, 1.00f) * 1.05f);
        material.SetColor("_StatusColor", new Color(1.00f, 0.40f, 0.14f));

        material.SetFloat("_FillOpacity", 0.055f);
        material.SetFloat("_FillFresnel", 0.9f);

        material.SetFloat("_EdgeThreshold", 0.75f);
        material.SetFloat("_EdgeWidth", 2.2f);
        material.SetFloat("_EdgeSoftness", 0.02f);
        material.SetFloat("_EdgeIntensity", 1.85f);
        material.SetFloat("_EdgeGate", 120f);
        material.SetFloat("_CreaseIntensity", 1.45f);
        material.SetFloat("_CreaseSharpness", 6f);

        material.SetFloat("_LightFloor", 0.42f);
        material.SetFloat("_LightInfluence", 0.45f);
        material.SetFloat("_LightWrap", 0.70f);
        material.SetFloat("_AmbientInfluence", 0.35f);
        // Top-down key: upward faces read brighter than vertical ones, and the
        // result no longer depends on the scene light the X-Ray view dims.
        material.SetVector("_KeyLightDirection", new Vector4(0f, 1f, 0f, 0f));

        material.SetFloat("_Sheen", 0.10f);
        material.SetFloat("_SheenSharpness", 40f);

        // Glare is the view-dependent flare on grazing panels. Held well down
        // so surfaces stay readable from every camera angle, and removed
        // entirely on mechanisms that are not the hover focus.
        material.SetFloat("_GlareStrength", 0.30f);
        material.SetFloat("_GhostGlareScale", 0f);

        // De-focused mechanisms collapse to roughly a tenth of their presence
        // and take on a cold tint, so the hovered part stands well clear.
        material.SetFloat("_GhostDissolve", 0.90f);
        material.SetColor("_GhostTint", new Color(0.16f, 0.34f, 0.62f));
        material.SetFloat("_GhostTintBlend", 0.85f);


        material.SetFloat("_BackFaceDim", 0.25f);
        material.SetFloat("_ContourIntensity", 0.14f);
        material.SetFloat("_ContourSpacing", 0.5f);

        material.SetFloat("_GhostFillScale", 0.015f);
        material.SetFloat("_GhostEdgeScale", 0.25f);

        material.SetFloat("_ClipEdgeGlow", 2f);
        material.SetFloat("_RevealBandIntensity", 3f);

        material.renderQueue = 3010;
    }

    /// <summary>
    /// Edge-only variant drawn over the normal materials when a card is
    /// hovered outside the X-Ray view. The body fill is removed so the real
    /// surface still reads through underneath.
    /// </summary>
    public static Material GetOrCreateOverlayMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath);

        if (existing != null)
            return existing;

        Shader shader = Shader.Find(ShaderName);

        if (shader == null)
            return null;

        Directory.CreateDirectory(Path.GetDirectoryName(OverlayMaterialPath));

        Material material = new(shader) { name = "Machine X-Ray Overlay" };
        ApplyOverlayPreset(material);

        AssetDatabase.CreateAsset(material, OverlayMaterialPath);
        AssetDatabase.SaveAssets();

        return material;
    }

    public static void ApplyOverlayPreset(Material material)
    {
        ApplyPreset(material);

        // Almost no body: the machine's own material provides the surface, and
        // the overlay contributes the rim and edge treatment on top of it.
        material.SetFloat("_FillOpacity", 0.010f);
        material.SetFloat("_FillFresnel", 6f);
        material.SetFloat("_GlareStrength", 0.20f);

        material.SetFloat("_EdgeIntensity", 2.2f);
        material.SetFloat("_CreaseIntensity", 0.9f);
        material.SetFloat("_ContourIntensity", 0f);
        material.SetFloat("_BackFaceDim", 0.15f);

        material.SetColor("_BaseColor", new Color(0.16f, 0.62f, 1.00f));
        material.SetColor("_EdgeColor", new Color(0.62f, 0.90f, 1.00f) * 1.15f);

        // Independent of the presenter's globals, which are zero until it runs.
        material.SetFloat("_IgnoreGlobalOpacity", 1f);

        // Drawn over the very mesh it decorates, so it needs a depth bias or
        // the coplanar surfaces lose the LEqual test and nothing appears.
        material.SetFloat("_OffsetFactor", -1f);
        material.SetFloat("_OffsetUnits", -1f);

        // Driven per-renderer as the overlay fades in and out.
        material.SetFloat("_Opacity", 0f);
    }

    private static void ConfigureHoverOverlay(
        MachineHoverOverlay overlay,
        Material overlayMaterial,
        MachineViewModeController viewMode,
        IReadOnlyList<MachinePartGroup> groups)
    {
        SerializedObject serialized = new(overlay);

        serialized.FindProperty("overlayMaterial").objectReferenceValue = overlayMaterial;
        serialized.FindProperty("viewModeController").objectReferenceValue = viewMode;

        SerializedProperty partGroups = serialized.FindProperty("partGroups");
        partGroups.arraySize = groups.Count;

        for (int i = 0; i < groups.Count; i++)
            partGroups.GetArrayElementAtIndex(i).objectReferenceValue = groups[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    [MenuItem("Tools/Digital Twin/Reset X-Ray Material To Preset", priority = 40)]
    public static void ResetMaterial()
    {
        Material material = GetOrCreateMaterial();

        if (material == null)
            return;

        // Edited in place so the asset GUID, and every reference to it, survive.
        Undo.RecordObject(material, "Reset X-Ray material");
        ApplyPreset(material);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();

        Material overlay = AssetDatabase.LoadAssetAtPath<Material>(OverlayMaterialPath);

        if (overlay != null)
        {
            Undo.RecordObject(overlay, "Reset X-Ray overlay material");
            ApplyOverlayPreset(overlay);
            EditorUtility.SetDirty(overlay);
            AssetDatabase.SaveAssets();
        }

        Debug.Log("X-Ray materials reset to the shipped preset.", material);
    }

    // -----------------------------------------------------------------------
    // Mechanisms
    // -----------------------------------------------------------------------

    private static List<MachinePartGroup> CreatePartGroups(
        DigitalTwinMachineConfiguration configuration,
        GameObject equipmentRoot)
    {
        List<MachinePartGroup> groups = new();

        if (configuration.machineParts == null)
            return groups;

        foreach (MachinePartDefinition part in configuration.machineParts)
        {
            List<Transform> targets = ResolvePaths(equipmentRoot.transform, part);

            if (targets.Count == 0)
            {
                Debug.LogWarning($"X-Ray mechanism '{part.id}' skipped: no geometry matched its paths.");
                continue;
            }

            // The first resolved object hosts the component; the renderer list
            // spans every path, so one mechanism can cover several objects.
            MachinePartGroup group = GetOrAddComponent<MachinePartGroup>(targets[0].gameObject);

            SerializedObject serialized = new(group);
            serialized.FindProperty("displayName").stringValue = part.displayName;

            List<Renderer> renderers = new();

            foreach (Transform target in targets)
                renderers.AddRange(target.GetComponentsInChildren<Renderer>(true));

            SerializedProperty rendererProperty = serialized.FindProperty("targetRenderers");
            rendererProperty.arraySize = renderers.Count;

            for (int i = 0; i < renderers.Count; i++)
                rendererProperty.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];

            SerializedProperty categories = serialized.FindProperty("sensorCategories");
            categories.arraySize = part.sensorCategories?.Count ?? 0;

            for (int i = 0; i < categories.arraySize; i++)
                categories.GetArrayElementAtIndex(i).stringValue = part.sensorCategories[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();

            groups.Add(group);
        }

        // Mechanisms removed from the configuration must not linger in the scene.
        foreach (MachinePartGroup stale in equipmentRoot.GetComponentsInChildren<MachinePartGroup>(true))
        {
            if (!groups.Contains(stale))
                Undo.DestroyObjectImmediate(stale);
        }

        return groups;
    }

    private static List<Transform> ResolvePaths(Transform equipmentRoot, MachinePartDefinition part)
    {
        List<Transform> targets = new();

        if (part.objectPaths == null)
            return targets;

        foreach (string path in part.objectPaths)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;

            Transform found = equipmentRoot.Find(path.Trim());

            if (found != null)
                targets.Add(found);
            else
                Debug.LogWarning($"X-Ray mechanism '{part.id}': '{EquipmentRootName}/{path}' was not found.");
        }

        return targets;
    }

    // -----------------------------------------------------------------------
    // Rig
    // -----------------------------------------------------------------------

    private static void ConfigurePresenter(
        MachineXRayPresenter presenter,
        Material material,
        GameObject equipmentRoot,
        IReadOnlyList<MachinePartGroup> groups)
    {
        SerializedObject serialized = new(presenter);
        serialized.FindProperty("xrayMaterial").objectReferenceValue = material;

        SerializedProperty roots = serialized.FindProperty("equipmentRoots");
        roots.arraySize = 1;
        roots.GetArrayElementAtIndex(0).objectReferenceValue = equipmentRoot;

        SerializedProperty partGroups = serialized.FindProperty("partGroups");
        partGroups.arraySize = groups.Count;

        for (int i = 0; i < groups.Count; i++)
            partGroups.GetArrayElementAtIndex(i).objectReferenceValue = groups[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void ConfigureEnvironment(XRayEnvironmentController environment)
    {
        SerializedObject serialized = new(environment);

        serialized.FindProperty("targetCamera").objectReferenceValue = Camera.main;

        Light key = Object
            .FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(light => light.type == LightType.Directional);

        serialized.FindProperty("keyLight").objectReferenceValue = key;

        AssignSingle(serialized.FindProperty("hiddenObjects"), FindSceneObject(EnvironmentRootName));
        AssignSingle(serialized.FindProperty("hiddenEffects"), FindSceneObject(EffectsRootName));

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void AssignSingle(SerializedProperty property, GameObject value)
    {
        property.arraySize = value != null ? 1 : 0;

        if (value != null)
            property.GetArrayElementAtIndex(0).objectReferenceValue = value;
    }

    private static void ConfigureViewMode(
        MachineViewModeController viewMode,
        MachineXRayPresenter presenter,
        XRayEnvironmentController environment)
    {
        SerializedObject serialized = new(viewMode);

        serialized.FindProperty("clipController").objectReferenceValue =
            Object.FindFirstObjectByType<HybridHopperClipController>();

        serialized.FindProperty("xrayPresenter").objectReferenceValue = presenter;
        serialized.FindProperty("environmentController").objectReferenceValue = environment;

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // -----------------------------------------------------------------------
    // Segmented control
    // -----------------------------------------------------------------------

    private static void ConfigureSegmentedControls(MachineViewModeController viewMode)
    {
        foreach (ViewModeSegmentedControl control in Object.FindObjectsByType<ViewModeSegmentedControl>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool portrait = IsUnder(control.transform, PortraitLayoutName);

            SerializedObject serialized = new(control);
            EnsureXRayButton(serialized, control);

            serialized.FindProperty("viewModeController").objectReferenceValue = viewMode;

            // Portrait cannot fit three icons and long copy, so it gets short
            // labels and no icons. Wide keeps the authored presentation.
            serialized.FindProperty("showIcons").boolValue = !portrait;
            serialized.FindProperty("applySegmentText").boolValue = true;
            serialized.FindProperty("exteriorText").stringValue = portrait ? "EXTERIOR" : "FULL BODY";
            serialized.FindProperty("sectionText").stringValue = portrait ? "INTERIOR" : "CROSS SECTION";
            serialized.FindProperty("xrayText").stringValue = "X-RAY";

            // The control measures itself against the safe area, so it adapts to
            // any screen instead of relying on an authored width.
            serialized.FindProperty("adaptWidthToScreen").boolValue = true;
            serialized.FindProperty("widthReference").objectReferenceValue = FindSafeArea(control.transform);
            serialized.FindProperty("horizontalPadding").floatValue = portrait ? 28f : 24f;
            serialized.FindProperty("maximumWidth").floatValue = portrait ? PortraitMaxWidth : WideControlWidth;
            serialized.FindProperty("minimumSegmentWidth").floatValue = portrait ? 90f : 180f;
            serialized.FindProperty("autoSizeLabels").boolValue = true;

            serialized.ApplyModifiedPropertiesWithoutUndo();

            ApplySegmentSizing(control, portrait);
        }
    }

    private static void EnsureXRayButton(SerializedObject serialized, ViewModeSegmentedControl control)
    {
        SerializedProperty xrayButtonProperty = serialized.FindProperty("xrayButton");

        if (xrayButtonProperty.objectReferenceValue != null)
            return;

        if (serialized.FindProperty("sectionButton").objectReferenceValue is not Button sectionButton)
        {
            Debug.LogWarning("Segmented control has no cross-section button to copy.", control);
            return;
        }

        // Cloning keeps the authored sprites, fonts, and layout settings
        // identical to the other segments.
        GameObject clone = Object.Instantiate(sectionButton.gameObject, sectionButton.transform.parent);
        clone.name = "X-Ray Button";
        clone.transform.SetSiblingIndex(sectionButton.transform.GetSiblingIndex() + 1);

        Undo.RegisterCreatedObjectUndo(clone, "Add X-Ray segment button");

        Button cloneButton = clone.GetComponent<Button>();
        cloneButton.onClick = new Button.ButtonClickedEvent();

        xrayButtonProperty.objectReferenceValue = cloneButton;

        CopySegmentReference<Image>(serialized, "sectionBackground", "xrayBackground", sectionButton, clone);
        CopySegmentReference<Image>(serialized, "sectionIcon", "xrayIcon", sectionButton, clone);
        CopySegmentReference<TMP_Text>(serialized, "sectionLabel", "xrayLabel", sectionButton, clone);
    }

    private static void ApplySegmentSizing(ViewModeSegmentedControl control, bool portrait)
    {
        // Wide has room for three segments, but the same floor would still cap
        // how small they may become on a narrow window.
        if (!portrait)
        {
            foreach (Transform child in control.transform)
            {
                LayoutElement wideElement = child.GetComponent<LayoutElement>();

                if (wideElement == null)
                    continue;

                Undo.RecordObject(wideElement, "Resize view mode segment");
                wideElement.minWidth = 0f;
                wideElement.flexibleWidth = 1f;
                EditorUtility.SetDirty(wideElement);
            }
        }

        LayoutElement container = control.GetComponent<LayoutElement>();

        if (container != null && !portrait)
        {
            // Wide has room: widen the container so three segments keep their
            // authored width instead of being squeezed.
            Undo.RecordObject(container, "Resize view mode control");
            container.preferredWidth = WideControlWidth;
            EditorUtility.SetDirty(container);
        }

        if (!portrait)
            return;

        // Portrait is already flexible width; the buttons just need to stop
        // asking for desktop-sized widths.
        foreach (Transform child in control.transform)
        {
            LayoutElement element = child.GetComponent<LayoutElement>();

            if (element == null)
                continue;

            Undo.RecordObject(element, "Resize view mode segment");

            // The authored 240 minimum meant three segments could never fit a
            // phone: the layout had no room to shrink them and overflowed the
            // screen instead. The runtime pass sizes them from the space
            // available, so the floor is cleared here.
            element.minWidth = 0f;
            element.preferredWidth = PortraitButtonWidth;
            element.flexibleWidth = 1f;
            EditorUtility.SetDirty(element);
        }
    }

    private static void CopySegmentReference<T>(
        SerializedObject serialized,
        string sourceProperty,
        string targetProperty,
        Button sourceButton,
        GameObject clone) where T : Component
    {
        SerializedProperty source = serialized.FindProperty(sourceProperty);
        SerializedProperty target = serialized.FindProperty(targetProperty);

        if (source.objectReferenceValue is not Component sourceComponent)
        {
            target.objectReferenceValue = null;
            return;
        }

        string relativePath = GetRelativePath(sourceButton.transform, sourceComponent.transform);

        Transform match = string.IsNullOrEmpty(relativePath)
            ? clone.transform
            : clone.transform.Find(relativePath);

        target.objectReferenceValue = match != null ? match.GetComponent<T>() : null;
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (target == root)
            return string.Empty;

        List<string> segments = new();

        for (Transform current = target; current != null && current != root; current = current.parent)
            segments.Insert(0, current.name);

        return string.Join("/", segments);
    }

    private static bool IsUnder(Transform transform, string ancestorName)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.name == ancestorName)
                return true;
        }

        return false;
    }

    /// <summary>Nearest "Safe area" ancestor, which is the usable screen rect.</summary>
    private static RectTransform FindSafeArea(Transform from)
    {
        for (Transform current = from; current != null; current = current.parent)
        {
            if (current.name == "Safe area" && current is RectTransform rect)
                return rect;
        }

        return from.parent as RectTransform;
    }

    private static GameObject FindSceneObject(string name) =>
        Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(candidate => candidate.name == name);

    private static T GetOrAddComponent<T>(GameObject host) where T : Component
    {
        T existing = host.GetComponent<T>();
        return existing != null ? existing : Undo.AddComponent<T>(host);
    }
}
