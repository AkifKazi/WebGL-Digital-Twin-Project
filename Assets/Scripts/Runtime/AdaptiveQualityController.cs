using System;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Chooses the graphics preset. Every device starts on the lightest preset, so
/// a weak phone or laptop is never asked to render more than it can. On a
/// desktop browser the viewer can turn HD on, and that choice is remembered in
/// the browser; phones stay light, because a preset chosen on a desktop must
/// never make a phone render at PC quality.
///
/// The preset is never changed behind the viewer's back: an earlier version
/// sampled the frame rate and stepped the quality down mid-session, which read
/// as the picture changing at random.
/// </summary>
public sealed class AdaptiveQualityController : MonoBehaviour
{
    /// <summary>Raised when the preset changes, so controls can show the new state.</summary>
    public event Action QualityChanged;

    [Header("Quality level names")]
    [SerializeField] private string highQualityName = "PC";
    [SerializeField] private string lowQualityName = "Mobile";

    [Header("Mobile WebGL")]
    [Tooltip("Mobile browsers stay on the light preset, whatever is stored, and HD is not offered.")]
#pragma warning disable CS0414 // Read only in WebGL player builds, so editor compiles see it as unused.
    [SerializeField] private bool lockMobileBrowserToLowQuality = true;
#pragma warning restore CS0414

    [Tooltip("Frame-rate limit used by mobile browsers. Desktop WebGL remains browser driven.")]
    [SerializeField, Range(15, 60)] private int mobileBrowserTargetFrameRate = 30;

    [Header("Behaviour")]
    [Tooltip("Remembers the viewer's HD choice in the browser, so a refresh keeps it.")]
    [SerializeField] private bool rememberChoice = true;

    [SerializeField] private bool logDecisions = true;

    private const string PreferenceKey = "GraphicsQualityMode";
    private const string HighMode = "High";
    private const string LowMode = "Low";

    private int highIndex = -1;
    private int lowIndex = -1;
    private bool mobileBrowser;
    private bool highQuality;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern int DigitalTwin_IsMobileBrowser();
#endif

    /// <summary>True on a desktop browser, where HD can be offered.</summary>
    public bool IsDesktop => !mobileBrowser;

    /// <summary>True while the high preset is running.</summary>
    public bool HighQualityEnabled => highQuality;

    private void Awake()
    {
        highIndex = FindQualityIndex(highQualityName);
        lowIndex = FindQualityIndex(lowQualityName);
        if (highIndex < 0 || lowIndex < 0)
        {
            Debug.LogWarning(
                $"Quality presets '{highQualityName}' and '{lowQualityName}' must both be enabled for this platform.",
                this);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        mobileBrowser = lockMobileBrowserToLowQuality && DigitalTwin_IsMobileBrowser() != 0;
        Application.targetFrameRate = mobileBrowser ? mobileBrowserTargetFrameRate : -1;
#endif

        // The light preset is the starting point everywhere; only a desktop
        // viewer's stored choice can raise it.
        bool wantsHigh = !mobileBrowser && rememberChoice &&
                         string.Equals(PlayerPrefs.GetString(PreferenceKey, LowMode), HighMode,
                             StringComparison.OrdinalIgnoreCase);
        Apply(wantsHigh);

        if (logDecisions)
        {
            Debug.Log(
                $"Graphics quality: {(mobileBrowser ? "mobile browser, light preset locked" : highQuality ? "HD" : "light")}.",
                this);
        }
    }

    /// <summary>Turns HD on or off. Ignored on phones, which stay on the light preset.</summary>
    public void SetHighQuality(bool enabled)
    {
        if (mobileBrowser && enabled)
            return;
        if (enabled == highQuality)
            return;

        Apply(enabled);

        if (rememberChoice && !mobileBrowser)
        {
            PlayerPrefs.SetString(PreferenceKey, enabled ? HighMode : LowMode);
            PlayerPrefs.Save();
        }

        if (logDecisions)
            Debug.Log($"Graphics quality: {(enabled ? "HD" : "light")} chosen.", this);
    }

    /// <summary>Flips between HD and the light preset; for the HD button.</summary>
    public void ToggleHighQuality() => SetHighQuality(!highQuality);

    private void Apply(bool high)
    {
        highQuality = high && !mobileBrowser;
        int index = highQuality ? highIndex : lowIndex;
        if (index >= 0 && QualitySettings.GetQualityLevel() != index)
        {
            // Changes are rare and deliberate, so the preset is applied in full.
            QualitySettings.SetQualityLevel(index, true);
        }

        QualityChanged?.Invoke();
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
}
