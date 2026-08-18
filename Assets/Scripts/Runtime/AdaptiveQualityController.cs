using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Chooses between named Unity quality levels using a smoothed FPS sample.
/// Designed for infrequent quality changes rather than frame-by-frame scaling.
/// </summary>
public sealed class AdaptiveQualityController : MonoBehaviour
{
    [Header("Quality level names")]
    [SerializeField] private string highQualityName = "PC";
    [SerializeField] private string mediumQualityName = "WebGL";
    [SerializeField] private string lowQualityName = "Mobile";

    [Header("Sampling")]
    [SerializeField, Min(0f)] private float startupDelay = 10f;
    [SerializeField, Min(1f)] private float sampleDuration = 5f;
    [SerializeField, Min(0f)] private float switchCooldown = 10f;

    [Header("Thresholds")]
    [Tooltip("Medium changes to Low below this average FPS.")]
    [SerializeField, Min(1f)] private float mediumToLowFps = 25f;

    [Tooltip("High changes to Medium below this average FPS.")]
    [SerializeField, Min(1f)] private float highToMediumFps = 45f;

    [Tooltip("Medium can change to High above this average FPS.")]
    [SerializeField, Min(1f)] private float mediumToHighFps = 55f;

    [Header("Behaviour")]
    [SerializeField] private bool allowAutomaticUpgrade = false;
    [SerializeField, Min(1)] private int upgradeSamplesRequired = 3;
    [SerializeField] private bool disableAutomaticChangesInDevelopmentBuild = true;
    [SerializeField] private bool logDecisions = true;

    [Header("Mobile WebGL")]
    [Tooltip("Mobile browsers remain on the low-quality preset instead of participating in adaptive quality.")]
    [SerializeField] private bool lockMobileBrowserToLowQuality = true;

    [Tooltip("Frame-rate limit used by mobile browsers. Desktop WebGL remains browser driven.")]
    [SerializeField, Range(15, 60)] private int mobileBrowserTargetFrameRate = 30;

    private const string PreferenceKey = "GraphicsQualityMode";
    private const string AutoMode = "Auto";

    private int highIndex = -1;
    private int mediumIndex = -1;
    private int lowIndex = -1;

    private float warmupRemaining;
    private float cooldownRemaining;
    private float sampleTime;
    private int sampleFrames;
    private int successfulUpgradeSamples;
    private bool automaticMode;
    private bool mobileBrowserQualityLocked;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern int DigitalTwin_IsMobileBrowser();
#endif

    public float LastAverageFps { get; private set; }

    private void Awake()
    {
        ResolveQualityIndices();

#if UNITY_WEBGL && !UNITY_EDITOR
        mobileBrowserQualityLocked =
            lockMobileBrowserToLowQuality && DigitalTwin_IsMobileBrowser() != 0;

        Application.targetFrameRate = mobileBrowserQualityLocked
            ? mobileBrowserTargetFrameRate
            : -1;
#endif

        if (mobileBrowserQualityLocked)
        {
            // This is deliberately session-only. A quality preference selected on
            // desktop must not make a phone render at PC quality, and using a phone
            // must not overwrite that desktop preference.
            automaticMode = false;
            ApplyQuality(lowIndex, lowQualityName);

            if (logDecisions)
            {
                Debug.Log(
                    $"Adaptive quality: mobile browser locked to {lowQualityName} " +
                    $"at {mobileBrowserTargetFrameRate} FPS.");
            }

            warmupRemaining = startupDelay;
            return;
        }

        string savedMode = PlayerPrefs.GetString(PreferenceKey, AutoMode);
        automaticMode = string.Equals(savedMode, AutoMode, StringComparison.OrdinalIgnoreCase);

        if (automaticMode)
        {
            ApplyQuality(mediumIndex, mediumQualityName);
        }
        else
        {
            SetManualQuality(savedMode, savePreference: false);
        }

        warmupRemaining = startupDelay;
    }

    private void Update()
    {
        if (!automaticMode || !Application.isFocused)
        {
            ResetSample();
            return;
        }

        if (disableAutomaticChangesInDevelopmentBuild && Debug.isDebugBuild)
            return;

        float deltaTime = Time.unscaledDeltaTime;

        if (warmupRemaining > 0f)
        {
            warmupRemaining -= deltaTime;
            return;
        }

        if (cooldownRemaining > 0f)
        {
            cooldownRemaining -= deltaTime;
            return;
        }

        sampleTime += deltaTime;
        sampleFrames++;

        if (sampleTime < sampleDuration)
            return;

        LastAverageFps = sampleFrames / Mathf.Max(sampleTime, 0.001f);
        EvaluateQuality(LastAverageFps);
        ResetSample();
    }

    public void SetAuto()
    {
        if (KeepMobileBrowserLocked())
            return;

        automaticMode = true;
        PlayerPrefs.SetString(PreferenceKey, AutoMode);
        PlayerPrefs.Save();

        ApplyQuality(mediumIndex, mediumQualityName);
        warmupRemaining = startupDelay;
        cooldownRemaining = 0f;
        successfulUpgradeSamples = 0;
        ResetSample();
    }

    public void SetHigh()
    {
        if (KeepMobileBrowserLocked())
            return;

        SetManualQuality(highQualityName, savePreference: true);
    }

    public void SetMedium()
    {
        if (KeepMobileBrowserLocked())
            return;

        SetManualQuality(mediumQualityName, savePreference: true);
    }

    public void SetLow()
    {
        if (KeepMobileBrowserLocked())
            return;

        SetManualQuality(lowQualityName, savePreference: true);
    }

    private bool KeepMobileBrowserLocked()
    {
        if (!mobileBrowserQualityLocked)
            return false;

        automaticMode = false;
        ApplyQuality(lowIndex, lowQualityName);
        Application.targetFrameRate = mobileBrowserTargetFrameRate;
        return true;
    }

    private void EvaluateQuality(float averageFps)
    {
        int currentIndex = QualitySettings.GetQualityLevel();

        if (currentIndex == highIndex && averageFps < highToMediumFps)
        {
            ApplyAutomaticQuality(mediumIndex, mediumQualityName, averageFps);
            return;
        }

        if (currentIndex == mediumIndex && averageFps < mediumToLowFps)
        {
            ApplyAutomaticQuality(lowIndex, lowQualityName, averageFps);
            return;
        }

        if (!allowAutomaticUpgrade || currentIndex != mediumIndex)
        {
            successfulUpgradeSamples = 0;
            return;
        }

        successfulUpgradeSamples = averageFps >= mediumToHighFps
            ? successfulUpgradeSamples + 1
            : 0;

        if (successfulUpgradeSamples >= upgradeSamplesRequired)
        {
            ApplyAutomaticQuality(highIndex, highQualityName, averageFps);
            successfulUpgradeSamples = 0;
        }
    }

    private void ApplyAutomaticQuality(int index, string levelName, float measuredFps)
    {
        if (logDecisions)
        {
            Debug.Log($"Adaptive quality: {measuredFps:0.0} FPS, switching to {levelName}.");
        }

        ApplyQuality(index, levelName);
        cooldownRemaining = switchCooldown;
        successfulUpgradeSamples = 0;
    }

    private void SetManualQuality(string levelName, bool savePreference)
    {
        int index = FindQualityIndex(levelName);
        if (index < 0)
        {
            Debug.LogWarning($"Quality level '{levelName}' is unavailable in this build.");
            return;
        }

        automaticMode = false;
        ApplyQuality(index, levelName);

        if (savePreference)
        {
            PlayerPrefs.SetString(PreferenceKey, levelName);
            PlayerPrefs.Save();
        }
    }

    private void ApplyQuality(int index, string levelName)
    {
        if (index < 0)
        {
            Debug.LogWarning($"Quality level '{levelName}' is unavailable in this build.");
            return;
        }

        if (QualitySettings.GetQualityLevel() != index)
        {
            // Changes are deliberately infrequent, so fully apply the new preset.
            QualitySettings.SetQualityLevel(index, true);
        }
    }

    private void ResolveQualityIndices()
    {
        highIndex = FindQualityIndex(highQualityName);
        mediumIndex = FindQualityIndex(mediumQualityName);
        lowIndex = FindQualityIndex(lowQualityName);

        if (highIndex < 0 || mediumIndex < 0 || lowIndex < 0)
        {
            Debug.LogWarning(
                "Adaptive quality requires PC, WebGL and Mobile quality levels to be enabled for this platform.");
        }
    }

    private static int FindQualityIndex(string levelName)
    {
        string[] names = QualitySettings.names;
        for (int i = 0; i < names.Length; i++)
        {
            if (string.Equals(names[i], levelName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private void ResetSample()
    {
        sampleTime = 0f;
        sampleFrames = 0;
    }
}
