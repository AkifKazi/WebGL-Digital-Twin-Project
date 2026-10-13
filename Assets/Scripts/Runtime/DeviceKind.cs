using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// Whether the twin runs on a phone or tablet. In a browser the page tells
/// (DigitalTwinDevice.jslib: a mobile user agent, or a touch screen with a
/// coarse pointer); elsewhere Unity's platform does. Asked once.
/// </summary>
public static class DeviceKind
{
    private static bool? mobile;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern int DigitalTwin_IsMobileBrowser();
#endif

#if UNITY_EDITOR
    /// <summary>For tests: answers in place of the platform; null asks it.</summary>
    public static bool? EditorOverride;
#endif

    /// <summary>True on a phone or tablet.</summary>
    public static bool IsMobile
    {
        get
        {
#if UNITY_EDITOR
            if (EditorOverride is bool forced)
                return forced;
#endif
            if (mobile == null)
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                mobile = DigitalTwin_IsMobileBrowser() != 0;
#else
                mobile = Application.isMobilePlatform || SystemInfo.deviceType == DeviceType.Handheld;
#endif
            }
            return mobile.Value;
        }
    }
}
