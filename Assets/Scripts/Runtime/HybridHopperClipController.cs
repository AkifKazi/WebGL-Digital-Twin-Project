using System;
using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HybridHopperClipController : MonoBehaviour
{
    public event Action<bool> ViewStateChanged;
    public event Action<bool> TransitionStateChanged;

    public bool IsCrossSection => isCrossSection;
    public bool IsTransitioning => transitionRoutine != null;


    [Header("Models")]
    [Tooltip("Complete exterior model used by Full Model mode.")]
    [SerializeField] private GameObject fullHopper;
    [Tooltip("Prepared cutaway model used by Cross Section mode.")]
    [SerializeField] private GameObject cutHopper;

    [Header("Full Hopper Renderers With Clip Shader")]
    [Tooltip("Renderers animated by the clipping shader. Leave empty to discover them from Full Hopper at runtime.")]
    [SerializeField] private Renderer[] fullHopperRenderers = Array.Empty<Renderer>();

    [Header("Clip Animation Values")]
    [SerializeField] private float fullViewClipX = 1.5f;
    [SerializeField] private float crossSectionClipX = -1.5f;

    [Header("Transition")]
    [SerializeField, Min(0.01f)] private float transitionDuration = 1.0f;
    [SerializeField] private bool useSmoothStep = true;

    [Header("Start State")]
    [SerializeField] private bool startInCrossSection;

    [Header("Shader Property Names")]
    [Tooltip("Shader property controlling the world-space clipping plane.")]
    [SerializeField] private string clipXProperty = "_ClipX";
    [Tooltip("Shader property enabling clipping on compatible materials.")]
    [SerializeField] private string clipEnabledProperty = "_ClipEnabled";

    [Header("Machine Specs UI")]
    [SerializeField] private CanvasGroup machineSpecsUI;
    [SerializeField, Min(0.01f)] private float specsFadeDuration = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool showDebug;

    private bool isCrossSection;
    private Coroutine transitionRoutine;
    private Coroutine specsRoutine;
    private MaterialPropertyBlock propertyBlock;
    private int clipXPropertyId;
    private int clipEnabledPropertyId;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        CacheShaderPropertyIds();

        if ((fullHopperRenderers == null || fullHopperRenderers.Length == 0) && fullHopper != null)
        {
            fullHopperRenderers = fullHopper.GetComponentsInChildren<Renderer>(true);
        }
    }

    private void Start()
    {
        isCrossSection = startInCrossSection;

        if (isCrossSection)
        {
            if (fullHopper != null)
                fullHopper.SetActive(false);

            if (cutHopper != null)
                cutHopper.SetActive(true);

            SetClipValues(crossSectionClipX);
            SetCanvasGroupInstant(machineSpecsUI, 1f, true);
        }
        else
        {
            if (fullHopper != null)
                fullHopper.SetActive(true);

            if (cutHopper != null)
                cutHopper.SetActive(false);

            SetClipValues(fullViewClipX);
            SetCanvasGroupInstant(machineSpecsUI, 0f, false);

        }

        ViewStateChanged?.Invoke(isCrossSection);
        TransitionStateChanged?.Invoke(false);
    }

    public void ToggleView()
    {
        SetCrossSectionView(!isCrossSection);
    }

    public void ShowCrossSection()
    {
        SetCrossSectionView(true);
    }

    public void ShowFull()
    {
        SetCrossSectionView(false);
    }

    public void SetCrossSectionView(bool showCrossSection)
    {
        // Ignore selection of the mode that is already active.
        if (showCrossSection == isCrossSection)
            return;

        // Prevent conflicting commands while the model is animating.
        if (transitionRoutine != null)
            return;

        isCrossSection = showCrossSection;

        ViewStateChanged?.Invoke(isCrossSection);
        TransitionStateChanged?.Invoke(true);

        transitionRoutine = StartCoroutine(
            showCrossSection
                ? TransitionToCrossSection()
                : TransitionToFull()
        );
    }

    private IEnumerator TransitionToCrossSection()
    {
        if (cutHopper != null)
            cutHopper.SetActive(true);

        if (fullHopper != null)
            fullHopper.SetActive(true);

        SetClipValues(fullViewClipX);

        // Specs appear because Toggle View was pressed.
        FadeMachineSpecs(true);

        yield return AnimateClip(fullViewClipX, crossSectionClipX);

        SetClipValues(crossSectionClipX);

        if (fullHopper != null)
            fullHopper.SetActive(false);

        transitionRoutine = null;
        TransitionStateChanged?.Invoke(false);
    }

    private IEnumerator TransitionToFull()
    {
        if (cutHopper != null)
            cutHopper.SetActive(true);

        if (fullHopper != null)
            fullHopper.SetActive(true);

        SetClipValues(crossSectionClipX);

        // Specs disappear because Toggle View was pressed again.
        FadeMachineSpecs(false);

        yield return AnimateClip(crossSectionClipX, fullViewClipX);

        SetClipValues(fullViewClipX);

        if (cutHopper != null)
            cutHopper.SetActive(false);

        transitionRoutine = null;
        TransitionStateChanged?.Invoke(false);
    }

    private IEnumerator AnimateClip(float fromX, float toX)
    {
        float timer = 0f;

        while (timer < transitionDuration)
        {
            timer += Time.deltaTime;

            float t = Mathf.Clamp01(timer / transitionDuration);

            if (useSmoothStep)
                t = Mathf.SmoothStep(0f, 1f, t);

            float currentX = Mathf.Lerp(fromX, toX, t);
            SetClipValues(currentX);

            if (showDebug)
                Debug.Log("Current ClipX: " + currentX.ToString("F2"));

            yield return null;
        }

        SetClipValues(toX);
    }

    private void SetClipValues(float clipX)
    {
        if (fullHopperRenderers == null)
            return;

        foreach (Renderer r in fullHopperRenderers)
        {
            if (r == null)
                continue;

            r.GetPropertyBlock(propertyBlock);

            propertyBlock.SetFloat(clipXPropertyId, clipX);

            // ClipEnabled remains 1 throughout, as requested.
            propertyBlock.SetFloat(clipEnabledPropertyId, 1f);

            r.SetPropertyBlock(propertyBlock);
        }
    }

    private void CacheShaderPropertyIds()
    {
        clipXPropertyId = Shader.PropertyToID(
            string.IsNullOrWhiteSpace(clipXProperty) ? "_ClipX" : clipXProperty);
        clipEnabledPropertyId = Shader.PropertyToID(
            string.IsNullOrWhiteSpace(clipEnabledProperty) ? "_ClipEnabled" : clipEnabledProperty);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        transitionDuration = Mathf.Max(0.01f, transitionDuration);
        specsFadeDuration = Mathf.Max(0.01f, specsFadeDuration);
        CacheShaderPropertyIds();
    }
#endif

    private void FadeMachineSpecs(bool show)
    {
        if (machineSpecsUI == null)
            return;

        if (specsRoutine != null)
            StopCoroutine(specsRoutine);

        specsRoutine = StartCoroutine(FadeCanvasGroup(machineSpecsUI, show));
    }

    private IEnumerator FadeCanvasGroup(CanvasGroup group, bool show)
    {
        if (group == null)
            yield break;

        float from = group.alpha;
        float to = show ? 1f : 0f;

        if (show)
            group.gameObject.SetActive(true);

        float timer = 0f;

        while (timer < specsFadeDuration)
        {
            timer += Time.deltaTime;

            float t = Mathf.Clamp01(timer / specsFadeDuration);
            t = Mathf.SmoothStep(0f, 1f, t);

            group.alpha = Mathf.Lerp(from, to, t);

            yield return null;
        }

        group.alpha = to;
        group.interactable = show;
        group.blocksRaycasts = show;

        if (!show)
            group.gameObject.SetActive(false);

        specsRoutine = null;
    }

    private void SetCanvasGroupInstant(CanvasGroup group, float alpha, bool interactable)
    {
        if (group == null)
            return;

        group.alpha = alpha;
        group.interactable = interactable;
        group.blocksRaycasts = interactable;
        group.gameObject.SetActive(alpha > 0f);
    }
}
