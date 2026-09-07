using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Drops the scene to a near-black inspection environment while the X-Ray view
/// is active, then restores every value it touched.
///
/// The controller stores the original state on entry rather than reading it
/// from the inspector, so the normal views keep whatever lighting the scene is
/// authored with.
/// </summary>
[DisallowMultipleComponent]
public sealed class XRayEnvironmentController : MonoBehaviour
{
    [Header("Camera")]
    [Tooltip("Camera switched to a solid dark background. Defaults to the main camera.")]
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Color xrayBackgroundColor = new(0.008f, 0.014f, 0.024f, 1f);

    [Header("Lighting")]
    [SerializeField] private Light keyLight;
    [Tooltip("Key light intensity multiplier while X-Ray is active.")]
    [SerializeField, Range(0f, 1f)] private float keyLightScale = 0.15f;
    [SerializeField] private Color xrayAmbientColor = new(0.02f, 0.03f, 0.05f, 1f);

    [Header("Hidden while X-Ray is active")]
    [Tooltip("Environment objects such as ground planes and site props.")]
    [SerializeField] private GameObject[] hiddenObjects = System.Array.Empty<GameObject>();

    [Tooltip("Particle and effect roots that would read as noise against the dark background.")]
    [SerializeField] private GameObject[] hiddenEffects = System.Array.Empty<GameObject>();

    [Header("Fog")]
    [SerializeField] private bool disableFog = true;

    [Header("Transition")]
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.45f;

    private readonly List<bool> hiddenObjectStates = new();
    private readonly List<bool> hiddenEffectStates = new();

    private bool isDark;
    private bool hasCapturedState;

    private CameraClearFlags originalClearFlags;
    private Color originalBackgroundColor;
    private float originalKeyLightIntensity;
    private Color originalAmbientColor;
    private AmbientMode originalAmbientMode;
    private float originalAmbientIntensity;
    private bool originalFog;

    private Camera ResolvedCamera => targetCamera != null ? targetCamera : Camera.main;

    public bool IsDark => isDark;

    /// <summary>Blend factor the view controller drives between 0 and 1.</summary>
    public float FadeDuration => fadeDuration;

    private void OnDisable()
    {
        // Never leave the scene in the dark state if this object goes away.
        if (isDark)
            SetDarkEnvironment(false);
    }

    public void SetDarkEnvironment(bool dark)
    {
        if (dark == isDark)
            return;

        if (dark)
            CaptureOriginalState();

        isDark = dark;

        ApplyCamera(dark);
        ApplyLighting(dark);
        ApplyHiddenObjects(dark);

        if (disableFog)
            RenderSettings.fog = dark ? false : originalFog;
    }

    private void CaptureOriginalState()
    {
        Camera cam = ResolvedCamera;

        if (cam != null)
        {
            originalClearFlags = cam.clearFlags;
            originalBackgroundColor = cam.backgroundColor;
        }

        if (keyLight != null)
            originalKeyLightIntensity = keyLight.intensity;

        originalAmbientColor = RenderSettings.ambientLight;
        originalAmbientMode = RenderSettings.ambientMode;
        originalAmbientIntensity = RenderSettings.ambientIntensity;
        originalFog = RenderSettings.fog;

        hasCapturedState = true;
    }

    private void ApplyCamera(bool dark)
    {
        Camera cam = ResolvedCamera;

        if (cam == null || !hasCapturedState)
            return;

        if (dark)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = xrayBackgroundColor;
        }
        else
        {
            cam.clearFlags = originalClearFlags;
            cam.backgroundColor = originalBackgroundColor;
        }
    }

    private void ApplyLighting(bool dark)
    {
        if (!hasCapturedState)
            return;

        if (keyLight != null)
        {
            keyLight.intensity = dark
                ? originalKeyLightIntensity * keyLightScale
                : originalKeyLightIntensity;
        }

        if (dark)
        {
            // Flat ambient keeps the hologram readable without the skybox
            // bleeding daylight colour into the shell.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = xrayAmbientColor;
            RenderSettings.ambientIntensity = 1f;
        }
        else
        {
            RenderSettings.ambientMode = originalAmbientMode;
            RenderSettings.ambientLight = originalAmbientColor;
            RenderSettings.ambientIntensity = originalAmbientIntensity;
        }
    }

    private void ApplyHiddenObjects(bool dark)
    {
        if (dark)
        {
            CaptureAndHide(hiddenObjects, hiddenObjectStates);
            CaptureAndHide(hiddenEffects, hiddenEffectStates);
        }
        else
        {
            Restore(hiddenObjects, hiddenObjectStates);
            Restore(hiddenEffects, hiddenEffectStates);
        }
    }

    private static void CaptureAndHide(GameObject[] objects, List<bool> states)
    {
        states.Clear();

        foreach (GameObject target in objects)
        {
            if (target == null)
            {
                states.Add(false);
                continue;
            }

            states.Add(target.activeSelf);
            target.SetActive(false);
        }
    }

    private static void Restore(GameObject[] objects, List<bool> states)
    {
        for (int i = 0; i < objects.Length && i < states.Count; i++)
        {
            if (objects[i] != null)
                objects[i].SetActive(states[i]);
        }

        states.Clear();
    }
}
