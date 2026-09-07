using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Renders the machine as an additive hologram: swaps the equipment renderers
/// to the X-Ray material, runs a build-up sweep, keeps a roving scan band
/// alive, and colours each <see cref="MachinePartGroup"/> by its worst alarm
/// state.
///
/// Original material arrays are cached on entry and restored verbatim on exit,
/// so the Full Model and Cross Section views are unaffected.
/// </summary>
[DisallowMultipleComponent]
public sealed class MachineXRayPresenter : MonoBehaviour
{
    private static readonly int OpacityId = Shader.PropertyToID("_XRayOpacity");
    private static readonly int RevealId = Shader.PropertyToID("_XRayReveal");
    private static readonly int RevealMinId = Shader.PropertyToID("_XRayRevealMinY");
    private static readonly int RevealMaxId = Shader.PropertyToID("_XRayRevealMaxY");
    private static readonly int RevealActiveId = Shader.PropertyToID("_XRayRevealActive");
    private static readonly int ScanYId = Shader.PropertyToID("_XRayScanY");
    private static readonly int ScanWidthId = Shader.PropertyToID("_XRayScanWidth");
    private static readonly int ScanIntensityId = Shader.PropertyToID("_XRayScanIntensity");

    [Header("Material")]
    [Tooltip("Material applied to every equipment renderer while the X-Ray view is active.")]
    [SerializeField] private Material xrayMaterial;

    [Header("Equipment")]
    [Tooltip("Roots whose mesh renderers switch to the X-Ray material. Filled in by scene setup.")]
    [SerializeField] private GameObject[] equipmentRoots = Array.Empty<GameObject>();

    [Tooltip("Renderers that keep their normal material, for example ground or props.")]
    [SerializeField] private Renderer[] excludedRenderers = Array.Empty<Renderer>();

    [Header("Mechanisms")]
    [Tooltip("Mechanisms driving alarm colour and hover isolation. Filled in from the machine configuration.")]
    [SerializeField] private MachinePartGroup[] partGroups = Array.Empty<MachinePartGroup>();

    [Header("Transition")]
    [Tooltip("Seconds for the build-up sweep when entering the view.")]
    [SerializeField, Min(0.05f)] private float revealDuration = 1.1f;
    [Tooltip("Seconds for the hologram to fade out when leaving the view.")]
    [SerializeField, Min(0.05f)] private float dissolveDuration = 0.5f;
    [Tooltip("Easing applied to both the build-up and the fade-out.")]
    [SerializeField] private AnimationCurve revealEase = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Header("Scan band")]
    [Tooltip("Runs a slow scan band up the machine while the view is idle.")]
    [SerializeField] private bool scanBandEnabled = true;
    [Tooltip("Scan band travel speed in metres per second.")]
    [SerializeField, Min(0.01f)] private float scanSpeed = 0.55f;
    [Tooltip("Thickness of the scan band in metres.")]
    [SerializeField, Min(0.01f)] private float scanWidth = 0.35f;
    [Tooltip("Brightness of the scan band. Set to zero to disable it.")]
    [SerializeField, Min(0f)] private float scanIntensity = 0.8f;
    [Tooltip("Seconds of dead time between scan passes.")]
    [SerializeField, Min(0f)] private float scanRestTime = 1.6f;

    [Header("Alarm palette")]
    [Tooltip("Colour for mechanisms within limits.")]
    [SerializeField] private Color normalColor = new(0.21f, 0.93f, 1.00f);
    [Tooltip("Colour for mechanisms with a sensor in warning.")]
    [SerializeField] private Color warningColor = new(1.00f, 0.72f, 0.18f);
    [Tooltip("Colour for mechanisms with a sensor in critical.")]
    [SerializeField] private Color criticalColor = new(1.00f, 0.26f, 0.22f);
    [Tooltip("Colour for mechanisms whose sensors are stale or unavailable.")]
    [SerializeField] private Color unavailableColor = new(0.42f, 0.47f, 0.55f);

    [Tooltip("How strongly a warning recolours its mechanism.")]
    [SerializeField, Range(0f, 1f)] private float warningBlend = 0.75f;
    [Tooltip("How strongly a critical alarm recolours its mechanism.")]
    [SerializeField, Range(0f, 1f)] private float criticalBlend = 1f;
    [Tooltip("How strongly an unavailable reading recolours its mechanism.")]
    [SerializeField, Range(0f, 1f)] private float unavailableBlend = 0.55f;

    [Tooltip("Tint mechanisms that are within limits. Off keeps them in the base blue.")]
    [SerializeField] private bool tintNormalMechanisms;

    [Header("Selection")]
    [Tooltip("Allows a mechanism to be highlighted by picking it in the 3D view.")]
    [SerializeField] private bool selectionEnabled = true;

    [Tooltip("Strength of the highlight applied to the selected mechanism.")]
    [SerializeField, Range(0f, 1f)] private float selectionBlend = 1f;

    [Header("Hover isolation")]
    [Tooltip("Hovering a telemetry card fades every mechanism except the one that owns that sensor.")]
    [SerializeField] private bool isolateOnCardFocus = true;

    [Tooltip("How far de-focused mechanisms fade back. 1 is the full dissolve.")]
    [SerializeField, Range(0f, 1f)] private float isolationStrength = 1f;

    [Tooltip("Pulls the hovered mechanism toward the highlight colour. A mechanism in " +
             "warning or critical keeps its alarm colour instead.")]
    [SerializeField, Range(0f, 1f)] private float focusHighlightStrength = 1f;

    [Tooltip("Mechanisms in warning or critical are never dimmed by hover isolation, " +
             "so a fault stays visible while another part is inspected.")]
    [SerializeField] private bool keepAlarmedMechanismsLit = true;

    [Tooltip("Seconds to cross-fade in and out of isolation. Matches the card peer fade by default.")]
    [SerializeField, Min(0.02f)] private float isolationFadeDuration = 0.23f;

    /// <summary>Raised when the hologram finishes appearing or disappearing.</summary>
    public event Action<bool> PresentationChanged;

    /// <summary>Raised when a mechanism is selected, or with null when cleared.</summary>
    public event Action<MachinePartGroup> SelectionChanged;

    public bool IsPresenting { get; private set; }
    public bool IsTransitioning => transitionRoutine != null;
    public MachinePartGroup Selected { get; private set; }

    private readonly List<Renderer> managedRenderers = new();
    private readonly List<Material[]> originalMaterials = new();
    private readonly List<ShadowCastingMode> originalShadowModes = new();
    private readonly List<MachinePartGroup> resolvedGroups = new();
    private readonly HashSet<Renderer> excludedSet = new();

    private Coroutine transitionRoutine;
    private Coroutine isolationRoutine;
    private MachinePartGroup focusedGroup;
    private Bounds machineBounds;
    private bool boundsValid;
    private float scanTimer;
    private float statusFade;

    private void Awake()
    {
        foreach (Renderer excluded in excludedRenderers)
        {
            if (excluded != null)
                excludedSet.Add(excluded);
        }

        ResolveGroups();
        ResetGlobals();
    }

    private void OnEnable()
    {
        foreach (MachinePartGroup group in resolvedGroups)
        {
            if (group != null)
                group.StateChanged += HandleGroupStateChanged;
        }

        // The UI publishes which sensor is focused; this component is the only
        // thing that knows which geometry that sensor belongs to.
        PerformanceStatCardView.ActiveFocusChanged += HandleCardFocusChanged;
    }

    private void OnDisable()
    {
        foreach (MachinePartGroup group in resolvedGroups)
        {
            if (group != null)
                group.StateChanged -= HandleGroupStateChanged;
        }

        PerformanceStatCardView.ActiveFocusChanged -= HandleCardFocusChanged;

        ResetGlobals();
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

    private void ResetGlobals()
    {
        Shader.SetGlobalFloat(OpacityId, 1f);
        Shader.SetGlobalFloat(RevealId, 1f);
        Shader.SetGlobalFloat(RevealActiveId, 0f);
        Shader.SetGlobalFloat(ScanIntensityId, 0f);
        Shader.SetGlobalFloat(ScanWidthId, scanWidth);
    }

    // -----------------------------------------------------------------------
    // Presentation
    // -----------------------------------------------------------------------

    public void SetPresenting(bool presenting)
    {
        if (presenting == IsPresenting || transitionRoutine != null)
            return;

        if (xrayMaterial == null)
        {
            Debug.LogWarning("MachineXRayPresenter: no X-Ray material assigned.", this);
            return;
        }

        IsPresenting = presenting;
        transitionRoutine = StartCoroutine(presenting ? EnterRoutine() : ExitRoutine());
    }

    private IEnumerator EnterRoutine()
    {
        SwapToXRay();
        RecalculateBounds();

        float padding = Mathf.Max(machineBounds.size.y * 0.05f, 0.05f);
        Shader.SetGlobalFloat(RevealMinId, machineBounds.min.y - padding);
        Shader.SetGlobalFloat(RevealMaxId, machineBounds.max.y + padding);
        Shader.SetGlobalFloat(RevealActiveId, 1f);
        Shader.SetGlobalFloat(OpacityId, 1f);
        Shader.SetGlobalFloat(ScanWidthId, scanWidth);

        float timer = 0f;

        while (timer < revealDuration)
        {
            timer += Time.deltaTime;

            float t = Mathf.Clamp01(timer / revealDuration);

            Shader.SetGlobalFloat(RevealId, revealEase.Evaluate(t));

            // Alarm colours arrive after the shell has mostly built, so the
            // sweep reads as one motion rather than two competing ones.
            statusFade = Mathf.Clamp01((t - 0.45f) / 0.55f);
            PushStatusVisuals();

            yield return null;
        }

        Shader.SetGlobalFloat(RevealId, 1f);
        Shader.SetGlobalFloat(RevealActiveId, 0f);

        statusFade = 1f;
        PushStatusVisuals();

        scanTimer = 0f;

        // A card may already have been hovered before the view opened, so apply
        // any pending isolation now that the hologram is on screen.
        if (focusedGroup != null)
            isolationRoutine = StartCoroutine(IsolationRoutine());

        transitionRoutine = null;
        PresentationChanged?.Invoke(true);
    }

    private IEnumerator ExitRoutine()
    {
        Shader.SetGlobalFloat(ScanIntensityId, 0f);

        float timer = 0f;

        while (timer < dissolveDuration)
        {
            timer += Time.deltaTime;

            float t = Mathf.Clamp01(timer / dissolveDuration);

            Shader.SetGlobalFloat(OpacityId, 1f - revealEase.Evaluate(t));
            statusFade = 1f - t;
            PushStatusVisuals();

            yield return null;
        }

        ClearSelection();
        SetFocusedGroup(null);
        RestoreMaterials();
        ResetGlobals();

        transitionRoutine = null;
        PresentationChanged?.Invoke(false);
    }

    private void Update()
    {
        if (IsPresenting && transitionRoutine == null)
            UpdateScanBand();
    }

    private void UpdateScanBand()
    {
        if (!scanBandEnabled || scanIntensity <= 0f || !boundsValid)
        {
            Shader.SetGlobalFloat(ScanIntensityId, 0f);
            return;
        }

        float height = Mathf.Max(machineBounds.size.y, 0.01f);
        float travelTime = height / Mathf.Max(scanSpeed, 0.01f);
        float cycle = travelTime + scanRestTime;

        scanTimer += Time.deltaTime;

        if (scanTimer > cycle)
            scanTimer -= cycle;

        if (scanTimer > travelTime)
        {
            Shader.SetGlobalFloat(ScanIntensityId, 0f);
            return;
        }

        float t = scanTimer / travelTime;

        Shader.SetGlobalFloat(ScanYId, Mathf.Lerp(machineBounds.min.y, machineBounds.max.y, t));
        Shader.SetGlobalFloat(ScanWidthId, scanWidth);

        // Sine envelope so the band fades in and out at the ends.
        Shader.SetGlobalFloat(ScanIntensityId, scanIntensity * Mathf.Sin(t * Mathf.PI));
    }

    // -----------------------------------------------------------------------
    // Materials
    // -----------------------------------------------------------------------

    private void SwapToXRay()
    {
        if (managedRenderers.Count > 0)
            RestoreMaterials();

        foreach (GameObject root in equipmentRoots)
            CollectRenderers(root);

        for (int i = 0; i < managedRenderers.Count; i++)
        {
            Renderer target = managedRenderers[i];
            Material[] replacement = new Material[originalMaterials[i].Length];

            for (int slot = 0; slot < replacement.Length; slot++)
                replacement[slot] = xrayMaterial;

            target.sharedMaterials = replacement;

            // A hologram casting a solid shadow would read as a black blob.
            target.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    private void CollectRenderers(GameObject root)
    {
        if (root == null)
            return;

        foreach (Renderer candidate in root.GetComponentsInChildren<Renderer>(true))
        {
            if (candidate == null || excludedSet.Contains(candidate))
                continue;

            // Particles, trails and sprites keep their own materials.
            if (candidate is not (MeshRenderer or SkinnedMeshRenderer))
                continue;

            if (managedRenderers.Contains(candidate))
                continue;

            managedRenderers.Add(candidate);
            originalMaterials.Add(candidate.sharedMaterials);
            originalShadowModes.Add(candidate.shadowCastingMode);
        }
    }

    private void RestoreMaterials()
    {
        for (int i = 0; i < managedRenderers.Count; i++)
        {
            Renderer target = managedRenderers[i];

            if (target == null)
                continue;

            target.sharedMaterials = originalMaterials[i];
            target.shadowCastingMode = originalShadowModes[i];
        }

        managedRenderers.Clear();
        originalMaterials.Clear();
        originalShadowModes.Clear();

        foreach (MachinePartGroup group in resolvedGroups)
        {
            if (group != null)
                group.ClearVisuals();
        }
    }

    private void RecalculateBounds()
    {
        boundsValid = false;

        foreach (Renderer target in managedRenderers)
        {
            if (target == null)
                continue;

            if (!boundsValid)
            {
                machineBounds = target.bounds;
                boundsValid = true;
            }
            else
            {
                machineBounds.Encapsulate(target.bounds);
            }
        }

        if (!boundsValid)
            machineBounds = new Bounds(transform.position, Vector3.one);
    }

    // -----------------------------------------------------------------------
    // Alarm state and selection
    // -----------------------------------------------------------------------

    private void HandleGroupStateChanged(MachinePartGroup group)
    {
        if (!IsPresenting)
            return;

        PushStatusVisuals();

        // A mechanism entering or leaving alarm changes whether it stays lit,
        // so the isolation targets are recalculated.
        if (focusedGroup != null)
        {
            if (isolationRoutine != null)
                StopCoroutine(isolationRoutine);

            isolationRoutine = StartCoroutine(IsolationRoutine());
        }
    }

    /// <summary>Re-applies alarm colours for every mechanism.</summary>
    public void PushStatusVisuals()
    {
        foreach (MachinePartGroup group in resolvedGroups)
        {
            if (group == null)
                continue;

            GetStatusVisual(group.WorstState, out Color color, out float blend);
            group.ApplyStatusVisual(color, blend * statusFade);
        }
    }

    private void GetStatusVisual(StatVisualState state, out Color color, out float blend)
    {
        switch (state)
        {
            case StatVisualState.Critical:
                color = criticalColor;
                blend = criticalBlend;
                return;

            case StatVisualState.Warning:
                color = warningColor;
                blend = warningBlend;
                return;

            case StatVisualState.Unavailable:
                color = unavailableColor;
                blend = unavailableBlend;
                return;

            default:
                color = normalColor;
                blend = tintNormalMechanisms ? 0.25f : 0f;
                return;
        }
    }

    // -----------------------------------------------------------------------
    // Hover isolation
    // -----------------------------------------------------------------------

    private void HandleCardFocusChanged(PerformanceStatCardView card)
    {
        if (!isolateOnCardFocus)
            return;

        FocusOn(card != null ? card.BoundSource : null);
    }

    /// <summary>
    /// Fades every mechanism except the one owning <paramref name="sensor"/>.
    /// Pass null to return the whole machine to full presence.
    /// </summary>
    public void FocusOn(PerformanceStatSource sensor)
    {
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
        }

        // A sensor with no mechanism (ambient, supply) must not blank the
        // machine, so isolation is skipped rather than applied to nothing.
        if (sensor != null && target == null)
            return;

        SetFocusedGroup(target);
    }

    private void SetFocusedGroup(MachinePartGroup group)
    {
        if (group == focusedGroup)
            return;

        focusedGroup = group;

        if (!IsPresenting)
            return;

        if (isolationRoutine != null)
            StopCoroutine(isolationRoutine);

        isolationRoutine = StartCoroutine(IsolationRoutine());
    }

    private IEnumerator IsolationRoutine()
    {
        // Capture the starting dim of each mechanism so an interrupted fade
        // continues from where it is instead of snapping.
        int count = resolvedGroups.Count;
        float[] from = new float[count];
        float[] to = new float[count];
        float[] highlightFrom = new float[count];
        float[] highlightTo = new float[count];

        for (int i = 0; i < count; i++)
        {
            MachinePartGroup group = resolvedGroups[i];

            from[i] = group != null ? group.CurrentFocusDim : 0f;
            highlightFrom[i] = group != null ? group.CurrentFocusHighlight : 0f;

            bool isFocused = group == focusedGroup;

            // An alarmed mechanism is pinned lit: a fault must not disappear
            // because the operator is reading a different card.
            bool pinned = keepAlarmedMechanismsLit && group != null && group.IsAlarmed;

            to[i] = focusedGroup == null || isFocused || pinned ? 0f : isolationStrength;

            // The highlight colour would override an alarm colour, so an
            // alarmed mechanism keeps red or amber even while hovered.
            highlightTo[i] = isFocused && focusedGroup != null && !(group != null && group.IsAlarmed)
                ? focusHighlightStrength
                : 0f;
        }

        float timer = 0f;

        while (timer < isolationFadeDuration)
        {
            timer += Time.unscaledDeltaTime;

            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(timer / isolationFadeDuration));

            for (int i = 0; i < count; i++)
            {
                if (resolvedGroups[i] == null)
                    continue;

                resolvedGroups[i].ApplyFocusDim(Mathf.Lerp(from[i], to[i], t));
                resolvedGroups[i].ApplyFocusHighlight(Mathf.Lerp(highlightFrom[i], highlightTo[i], t));
            }

            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            if (resolvedGroups[i] == null)
                continue;

            resolvedGroups[i].ApplyFocusDim(to[i]);
            resolvedGroups[i].ApplyFocusHighlight(highlightTo[i]);
        }

        isolationRoutine = null;
    }

    /// <summary>Highlights one mechanism. Pass null to clear.</summary>
    public void Select(MachinePartGroup group)
    {
        if (!selectionEnabled || group == Selected)
            return;

        if (Selected != null)
            Selected.ApplySelectionVisual(0f);

        Selected = group;

        if (Selected != null)
            Selected.ApplySelectionVisual(selectionBlend);

        SelectionChanged?.Invoke(Selected);
    }

    public void ClearSelection()
    {
        Select(null);
    }

    /// <summary>Returns the mechanism under a screen point, or null.</summary>
    public MachinePartGroup Pick(Camera camera, Vector2 screenPosition)
    {
        if (camera == null || !IsPresenting)
            return null;

        Ray ray = camera.ScreenPointToRay(screenPosition);

        MachinePartGroup nearestGroup = null;
        float nearestDistance = float.MaxValue;

        foreach (MachinePartGroup group in resolvedGroups)
        {
            if (group == null || !group.isActiveAndEnabled)
                continue;

            if (!group.TryGetWorldBounds(out Bounds bounds))
                continue;

            if (bounds.IntersectRay(ray, out float distance) && distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestGroup = group;
            }
        }

        return nearestGroup;
    }
}
