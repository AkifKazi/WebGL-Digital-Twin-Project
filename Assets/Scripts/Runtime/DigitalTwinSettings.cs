using System;
using UnityEngine;

/// <summary>
/// The viewer's preferences, set from the settings panel and kept between
/// visits (PlayerPrefs: the browser's storage in the WebGL build). Every
/// setting is read where it applies; <see cref="Changed"/> tells those places
/// to read again. Setting a value to what it already is does nothing.
/// </summary>
public static class DigitalTwinSettings
{
    public enum EdgeShading
    {
        Off,
        Subtle,
        Strong
    }

    public enum UnitStyle
    {
        /// <summary>Units as engineers write them: mm/s, kW, °C.</summary>
        Engineering,
        /// <summary>Units in capitals, like the rest of the interface: MM/S, KW.</summary>
        Capitals
    }

    public enum FrameRateLimit
    {
        Display,
        Sixty,
        Thirty
    }

    public const float MinimumInterfaceScale = 0.8f;
    public const float MaximumInterfaceScale = 1.25f;
    public const float MinimumSensitivity = 0.25f;
    public const float MaximumSensitivity = 2f;

    // Defaults: the look and feel before there were settings, except the
    // interface, which reads better a little smaller than its designed size.
    private const float DefaultInterfaceScale = 0.9f;
    private const EdgeShading DefaultEdgeShading = EdgeShading.Subtle;
    private const UnitStyle DefaultUnits = UnitStyle.Capitals;
    private const FrameRateLimit DefaultFrameRate = FrameRateLimit.Display;

    private const string Prefix = "settings.";

    private static bool loaded;
    private static float interfaceScale = DefaultInterfaceScale;
    private static bool highContrast;
    private static bool reduceMotion;
    private static EdgeShading edgeShading = DefaultEdgeShading;
    private static UnitStyle units = DefaultUnits;
    private static bool dimOtherCards = true;
    private static float orbitSensitivity = 1f;
    private static float zoomSensitivity = 1f;
    private static bool invertVertical;
    private static bool smoothCamera = true;
    private static bool showFrameRate;
    private static FrameRateLimit frameRate = DefaultFrameRate;

    /// <summary>Raised after any setting changes, and after a reset.</summary>
    public static event Action Changed;

    // ---- display ------------------------------------------------------------

    /// <summary>The interface's size, as a share of its designed size.</summary>
    public static float InterfaceScale
    {
        get { Load(); return interfaceScale; }
        set => Set(ref interfaceScale, Mathf.Clamp(value, MinimumInterfaceScale, MaximumInterfaceScale));
    }

    /// <summary>Panels more opaque and edges stronger, for bright rooms and projectors.</summary>
    public static bool HighContrast
    {
        get { Load(); return highContrast; }
        set => Set(ref highContrast, value);
    }

    /// <summary>No breathing glows, travelling pulses or blended transitions.</summary>
    public static bool ReduceMotion
    {
        get { Load(); return reduceMotion; }
        set => Set(ref reduceMotion, value);
    }

    /// <summary>How much the top and bottom of the screen darken behind the controls there.</summary>
    public static EdgeShading EdgeShade
    {
        get { Load(); return edgeShading; }
        set => Set(ref edgeShading, value);
    }

    // ---- telemetry ----------------------------------------------------------

    public static UnitStyle Units
    {
        get { Load(); return units; }
        set => Set(ref units, value);
    }

    /// <summary>Whether the other cards fade back while one is focused.</summary>
    public static bool DimOtherCards
    {
        get { Load(); return dimOtherCards; }
        set => Set(ref dimOtherCards, value);
    }

    // ---- camera -------------------------------------------------------------

    /// <summary>Multiplies how far a drag or a key turns the camera.</summary>
    public static float OrbitSensitivity
    {
        get { Load(); return orbitSensitivity; }
        set => Set(ref orbitSensitivity, Mathf.Clamp(value, MinimumSensitivity, MaximumSensitivity));
    }

    /// <summary>Multiplies how far the wheel, a pinch or a key zooms.</summary>
    public static float ZoomSensitivity
    {
        get { Load(); return zoomSensitivity; }
        set => Set(ref zoomSensitivity, Mathf.Clamp(value, MinimumSensitivity, MaximumSensitivity));
    }

    /// <summary>Dragging up tilts the camera down instead of up.</summary>
    public static bool InvertVertical
    {
        get { Load(); return invertVertical; }
        set => Set(ref invertVertical, value);
    }

    /// <summary>The camera eases to where it is sent instead of moving straight there.</summary>
    public static bool SmoothCamera
    {
        get { Load(); return smoothCamera; }
        set => Set(ref smoothCamera, value);
    }

    // ---- graphics -----------------------------------------------------------

    public static bool ShowFrameRate
    {
        get { Load(); return showFrameRate; }
        set => Set(ref showFrameRate, value);
    }

    public static FrameRateLimit FrameRate
    {
        get { Load(); return frameRate; }
        set => Set(ref frameRate, value);
    }

    /// <summary>Application.targetFrameRate for the limit; -1 follows the display.</summary>
    public static int TargetFrameRate => FrameRate switch
    {
        FrameRateLimit.Sixty => 60,
        FrameRateLimit.Thirty => 30,
        _ => -1
    };

    /// <summary>Puts every setting back to its default.</summary>
    public static void ResetToDefaults()
    {
        Load();
        interfaceScale = DefaultInterfaceScale;
        highContrast = false;
        reduceMotion = false;
        edgeShading = DefaultEdgeShading;
        units = DefaultUnits;
        dimOtherCards = true;
        orbitSensitivity = 1f;
        zoomSensitivity = 1f;
        invertVertical = false;
        smoothCamera = true;
        showFrameRate = false;
        frameRate = DefaultFrameRate;
        Save();
        Changed?.Invoke();
    }

    /// <summary>True when every setting is at its default.</summary>
    public static bool AtDefaults
    {
        get
        {
            Load();
            return Mathf.Approximately(interfaceScale, DefaultInterfaceScale) && !highContrast && !reduceMotion &&
                   edgeShading == DefaultEdgeShading && units == DefaultUnits && dimOtherCards &&
                   Mathf.Approximately(orbitSensitivity, 1f) && Mathf.Approximately(zoomSensitivity, 1f) &&
                   !invertVertical && smoothCamera && !showFrameRate && frameRate == DefaultFrameRate;
        }
    }

    // ---- storage ------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        loaded = false;
        Changed = null;
    }

    private static void Load()
    {
        if (loaded)
            return;
        loaded = true;
        interfaceScale = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "interfaceScale", DefaultInterfaceScale),
            MinimumInterfaceScale, MaximumInterfaceScale);
        highContrast = PlayerPrefs.GetInt(Prefix + "highContrast", 0) != 0;
        reduceMotion = PlayerPrefs.GetInt(Prefix + "reduceMotion", 0) != 0;
        edgeShading = (EdgeShading)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "edgeShading", (int)DefaultEdgeShading), 0, 2);
        units = (UnitStyle)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "units", (int)DefaultUnits), 0, 1);
        dimOtherCards = PlayerPrefs.GetInt(Prefix + "dimOtherCards", 1) != 0;
        orbitSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "orbitSensitivity", 1f), MinimumSensitivity, MaximumSensitivity);
        zoomSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "zoomSensitivity", 1f), MinimumSensitivity, MaximumSensitivity);
        invertVertical = PlayerPrefs.GetInt(Prefix + "invertVertical", 0) != 0;
        smoothCamera = PlayerPrefs.GetInt(Prefix + "smoothCamera", 1) != 0;
        showFrameRate = PlayerPrefs.GetInt(Prefix + "showFrameRate", 0) != 0;
        frameRate = (FrameRateLimit)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "frameRate", (int)DefaultFrameRate), 0, 2);
    }

    private static void Save()
    {
        PlayerPrefs.SetFloat(Prefix + "interfaceScale", interfaceScale);
        PlayerPrefs.SetInt(Prefix + "highContrast", highContrast ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "reduceMotion", reduceMotion ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "edgeShading", (int)edgeShading);
        PlayerPrefs.SetInt(Prefix + "units", (int)units);
        PlayerPrefs.SetInt(Prefix + "dimOtherCards", dimOtherCards ? 1 : 0);
        PlayerPrefs.SetFloat(Prefix + "orbitSensitivity", orbitSensitivity);
        PlayerPrefs.SetFloat(Prefix + "zoomSensitivity", zoomSensitivity);
        PlayerPrefs.SetInt(Prefix + "invertVertical", invertVertical ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "smoothCamera", smoothCamera ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "showFrameRate", showFrameRate ? 1 : 0);
        PlayerPrefs.SetInt(Prefix + "frameRate", (int)frameRate);
        // WebGL writes PlayerPrefs to the browser's storage only on Save.
        PlayerPrefs.Save();
    }

    private static void Set<T>(ref T field, T value)
    {
        Load();
        if (Equals(field, value))
            return;
        field = value;
        Save();
        Changed?.Invoke();
    }
}
