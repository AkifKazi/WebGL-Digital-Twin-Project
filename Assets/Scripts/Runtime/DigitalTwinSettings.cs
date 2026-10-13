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
    public const int MinimumRailsPerSide = 1;
    public const int MaximumRailsPerSide = TelemetryLayoutSettings.MaximumRailsPerSide;

    // Defaults: the look and feel before there were settings, except the
    // interface, which on a desktop reads better a little smaller than its
    // designed size. A phone or tablet keeps the designed size: its screen is
    // small already, and a finger needs the room.
    private const float DesktopInterfaceScale = 0.9f;
    private const float MobileInterfaceScale = 1f;
    private const EdgeShading DefaultEdgeShading = EdgeShading.Strong;
    private const UnitStyle DefaultUnits = UnitStyle.Capitals;
    private const FrameRateLimit DefaultFrameRate = FrameRateLimit.Display;
    // One rail of cards each side of the model, two in all: the model keeps
    // the room, and the other cards wait on the next page.
    private const int DefaultRailsPerSide = 1;

    private const string Prefix = "settings.";

    // Version 2 keeps only what a viewer chose: a value at its default is not
    // stored, so a default changed later reaches everyone who never chose it.
    // Version 1 (no version key) stored every value, its defaults with them.
    private const int StorageVersion = 2;
    private const float Version1InterfaceScale = 0.9f;
    private const EdgeShading Version1EdgeShading = EdgeShading.Subtle;

    private static bool loaded;
    private static float interfaceScale = DesktopInterfaceScale;

    /// <summary>The interface scale a new viewer starts at, and Reset returns to, on this device.</summary>
    public static float DefaultInterfaceScale => DeviceKind.IsMobile ? MobileInterfaceScale : DesktopInterfaceScale;
    private static bool highContrast;
    private static bool reduceMotion;
    private static EdgeShading edgeShading = DefaultEdgeShading;
    private static UnitStyle units = DefaultUnits;
    private static bool dimOtherCards = true;
    private static int railsPerSide = DefaultRailsPerSide;
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

    /// <summary>
    /// Card rails the viewer asks for on each side of the model: 1 is two
    /// rails in all. A layout runs fewer while they do not fit beside the
    /// model (the cards go to further pages), and a phone runs at most
    /// <see cref="TelemetryLayoutSettings.MaximumMobileRailsPerSide"/>.
    /// </summary>
    public static int RailsPerSide
    {
        get { Load(); return railsPerSide; }
        set => Set(ref railsPerSide, Mathf.Clamp(value, MinimumRailsPerSide, MaximumRailsPerSide));
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
        railsPerSide = DefaultRailsPerSide;
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
                   railsPerSide == DefaultRailsPerSide &&
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
        Migrate();
        interfaceScale = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "interfaceScale", DefaultInterfaceScale),
            MinimumInterfaceScale, MaximumInterfaceScale);
        highContrast = PlayerPrefs.GetInt(Prefix + "highContrast", 0) != 0;
        reduceMotion = PlayerPrefs.GetInt(Prefix + "reduceMotion", 0) != 0;
        edgeShading = (EdgeShading)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "edgeShading", (int)DefaultEdgeShading), 0, 2);
        units = (UnitStyle)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "units", (int)DefaultUnits), 0, 1);
        dimOtherCards = PlayerPrefs.GetInt(Prefix + "dimOtherCards", 1) != 0;
        railsPerSide = Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "railsPerSide", DefaultRailsPerSide),
            MinimumRailsPerSide, MaximumRailsPerSide);
        orbitSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "orbitSensitivity", 1f), MinimumSensitivity, MaximumSensitivity);
        zoomSensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(Prefix + "zoomSensitivity", 1f), MinimumSensitivity, MaximumSensitivity);
        invertVertical = PlayerPrefs.GetInt(Prefix + "invertVertical", 0) != 0;
        smoothCamera = PlayerPrefs.GetInt(Prefix + "smoothCamera", 1) != 0;
        showFrameRate = PlayerPrefs.GetInt(Prefix + "showFrameRate", 0) != 0;
        frameRate = (FrameRateLimit)Mathf.Clamp(PlayerPrefs.GetInt(Prefix + "frameRate", (int)DefaultFrameRate), 0, 2);
    }

    /// <summary>
    /// Version 1 wrote its defaults as if they were chosen: the interface at
    /// 0.9 and subtle edge shading, for everyone who changed any setting.
    /// Those values are read as not chosen, so today's defaults apply (the
    /// designed size on a phone, strong shading). A viewer who did pick them
    /// cannot be told apart, and picks them again.
    /// </summary>
    private static void Migrate()
    {
        if (PlayerPrefs.GetInt(Prefix + "version", 1) >= StorageVersion)
            return;
        string scaleKey = Prefix + "interfaceScale";
        if (PlayerPrefs.HasKey(scaleKey) &&
            Mathf.Approximately(PlayerPrefs.GetFloat(scaleKey), Version1InterfaceScale))
            PlayerPrefs.DeleteKey(scaleKey);
        if (PlayerPrefs.GetInt(Prefix + "edgeShading", -1) == (int)Version1EdgeShading)
            PlayerPrefs.DeleteKey(Prefix + "edgeShading");
        PlayerPrefs.SetInt(Prefix + "version", StorageVersion);
        PlayerPrefs.Save();
    }

    private static void Save()
    {
        Store("interfaceScale", interfaceScale, DefaultInterfaceScale);
        Store("highContrast", highContrast ? 1 : 0, 0);
        Store("reduceMotion", reduceMotion ? 1 : 0, 0);
        Store("edgeShading", (int)edgeShading, (int)DefaultEdgeShading);
        Store("units", (int)units, (int)DefaultUnits);
        Store("dimOtherCards", dimOtherCards ? 1 : 0, 1);
        Store("railsPerSide", railsPerSide, DefaultRailsPerSide);
        Store("orbitSensitivity", orbitSensitivity, 1f);
        Store("zoomSensitivity", zoomSensitivity, 1f);
        Store("invertVertical", invertVertical ? 1 : 0, 0);
        Store("smoothCamera", smoothCamera ? 1 : 0, 1);
        Store("showFrameRate", showFrameRate ? 1 : 0, 0);
        Store("frameRate", (int)frameRate, (int)DefaultFrameRate);
        PlayerPrefs.SetInt(Prefix + "version", StorageVersion);
        // WebGL writes PlayerPrefs to the browser's storage only on Save.
        PlayerPrefs.Save();
    }

    // Kept only when chosen, so a viewer at a default follows it if it changes.
    private static void Store(string key, int value, int fallback)
    {
        if (value == fallback)
            PlayerPrefs.DeleteKey(Prefix + key);
        else
            PlayerPrefs.SetInt(Prefix + key, value);
    }

    private static void Store(string key, float value, float fallback)
    {
        if (Mathf.Approximately(value, fallback))
            PlayerPrefs.DeleteKey(Prefix + key);
        else
            PlayerPrefs.SetFloat(Prefix + key, value);
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
