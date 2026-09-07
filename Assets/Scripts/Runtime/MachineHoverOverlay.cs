using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Draws the X-Ray edge treatment on top of the normal materials when a
/// telemetry card is hovered, in the Full Body and Cross Section views.
///
/// The machine keeps its real materials. A lightweight proxy renderer is
/// created per mesh the first time its mechanism is hovered, then simply
/// enabled and disabled, so nothing is rebuilt and idle cost is zero.
/// </summary>
[DisallowMultipleComponent]
public sealed class MachineHoverOverlay : MonoBehaviour
{
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");

    [Header("Overlay")]
    [Tooltip("Material drawn over the hovered mechanism. Uses the X-Ray shader with the body fill removed.")]
    [SerializeField] private Material overlayMaterial;

    [Tooltip("Peak strength of the overlay.")]
    [SerializeField, Range(0f, 3f)] private float overlayOpacity = 1f;

    [Header("Behaviour")]
    [Tooltip("View controller used to suppress the overlay in the X-Ray view, which does its own isolation.")]
    [SerializeField] private MachineViewModeController viewModeController;

    [Tooltip("Mechanisms eligible for the overlay. Left empty, they are discovered from the equipment roots.")]
    [SerializeField] private MachinePartGroup[] partGroups = System.Array.Empty<MachinePartGroup>();

    [Tooltip("Roots searched for mechanisms when the list above is empty.")]
    [SerializeField] private GameObject[] equipmentRoots = System.Array.Empty<GameObject>();

    [Tooltip("Seconds to fade the overlay in and out.")]
    [SerializeField, Min(0.02f)] private float fadeDuration = 0.2f;

    private readonly List<MachinePartGroup> resolvedGroups = new();
    private readonly Dictionary<MachinePartGroup, List<Renderer>> proxies = new();

    private MaterialPropertyBlock propertyBlock;
    private MachinePartGroup activeGroup;
    private Coroutine fadeRoutine;
    private float currentOpacity;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        ResolveGroups();
    }

    private void OnEnable()
    {
        PerformanceStatCardView.ActiveFocusChanged += HandleCardFocusChanged;

        if (viewModeController != null)
            viewModeController.ModeChanged += HandleModeChanged;
    }

    private void OnDisable()
    {
        PerformanceStatCardView.ActiveFocusChanged -= HandleCardFocusChanged;

        if (viewModeController != null)
            viewModeController.ModeChanged -= HandleModeChanged;

        HideImmediate();
    }

    private void ResolveGroups()
    {
        resolvedGroups.Clear();

        if (partGroups != null && partGroups.Length > 0)
        {
            foreach (MachinePartGroup group in partGroups)
            {
                if (group != null)
                    resolvedGroups.Add(group);
            }

            return;
        }

        foreach (GameObject root in equipmentRoots)
        {
            if (root != null)
                resolvedGroups.AddRange(root.GetComponentsInChildren<MachinePartGroup>(true));
        }
    }

    private void HandleModeChanged(MachineViewMode mode)
    {
        // The X-Ray view isolates mechanisms itself, so the overlay stands down.
        if (mode == MachineViewMode.XRay)
            HideImmediate();
    }

    private void HandleCardFocusChanged(PerformanceStatCardView card)
    {
        if (overlayMaterial == null)
            return;

        if (viewModeController != null && viewModeController.CurrentMode == MachineViewMode.XRay)
            return;

        PerformanceStatSource sensor = card != null ? card.BoundSource : null;
        MachinePartGroup target = null;

        if (sensor != null)
        {
            foreach (MachinePartGroup group in resolvedGroups)
            {
                if (group != null && group.Owns(sensor))
                {
                    target = group;
                    break;
                }
            }

            // A sensor with no mechanism leaves the machine untouched.
            if (target == null)
                return;
        }

        SetActiveGroup(target);
    }

    private void SetActiveGroup(MachinePartGroup group)
    {
        if (group == activeGroup)
            return;

        if (activeGroup != null)
            SetProxiesActive(activeGroup, false);

        activeGroup = group;

        if (activeGroup != null)
        {
            EnsureProxies(activeGroup);
            SetProxiesActive(activeGroup, true);
        }

        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);

        fadeRoutine = StartCoroutine(FadeRoutine(activeGroup != null ? overlayOpacity : 0f));
    }

    private IEnumerator FadeRoutine(float target)
    {
        float from = currentOpacity;
        float timer = 0f;

        while (timer < fadeDuration)
        {
            timer += Time.unscaledDeltaTime;

            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(timer / fadeDuration));
            ApplyOpacity(Mathf.Lerp(from, target, t));

            yield return null;
        }

        ApplyOpacity(target);

        // Nothing is drawn at zero, so the proxies are switched off entirely.
        if (Mathf.Approximately(target, 0f) && activeGroup == null)
        {
            foreach (KeyValuePair<MachinePartGroup, List<Renderer>> entry in proxies)
                SetProxiesActive(entry.Key, false);
        }

        fadeRoutine = null;
    }

    private void ApplyOpacity(float opacity)
    {
        currentOpacity = opacity;

        if (activeGroup == null || !proxies.TryGetValue(activeGroup, out List<Renderer> group))
            return;

        propertyBlock ??= new MaterialPropertyBlock();

        foreach (Renderer proxy in group)
        {
            if (proxy == null)
                continue;

            proxy.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(OpacityId, opacity);
            proxy.SetPropertyBlock(propertyBlock);
        }
    }

    /// <summary>
    /// Creates one proxy per mesh, parented to the source renderer so it
    /// inherits every transform update for free. Built once per mechanism.
    /// </summary>
    private void EnsureProxies(MachinePartGroup group)
    {
        if (proxies.ContainsKey(group))
            return;

        List<Renderer> created = new();

        foreach (Renderer source in group.Renderers)
        {
            if (source == null || source.GetComponent<MachineOverlayProxy>() != null)
                continue;

            if (source is not MeshRenderer)
                continue;

            MeshFilter sourceFilter = source.GetComponent<MeshFilter>();

            if (sourceFilter == null || sourceFilter.sharedMesh == null)
                continue;

            GameObject proxyObject = new("Hover Overlay");
            proxyObject.transform.SetParent(source.transform, false);
            proxyObject.AddComponent<MachineOverlayProxy>();

            MeshFilter filter = proxyObject.AddComponent<MeshFilter>();
            filter.sharedMesh = sourceFilter.sharedMesh;

            MeshRenderer renderer = proxyObject.AddComponent<MeshRenderer>();

            Material[] slots = new Material[sourceFilter.sharedMesh.subMeshCount];

            for (int i = 0; i < slots.Length; i++)
                slots[i] = overlayMaterial;

            renderer.sharedMaterials = slots;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.enabled = false;

            created.Add(renderer);
        }

        proxies[group] = created;
    }

    private void SetProxiesActive(MachinePartGroup group, bool active)
    {
        if (group == null || !proxies.TryGetValue(group, out List<Renderer> group2))
            return;

        foreach (Renderer proxy in group2)
        {
            if (proxy != null)
                proxy.enabled = active;
        }
    }

    private void HideImmediate()
    {
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }

        currentOpacity = 0f;

        foreach (KeyValuePair<MachinePartGroup, List<Renderer>> entry in proxies)
            SetProxiesActive(entry.Key, false);

        activeGroup = null;
    }
}
