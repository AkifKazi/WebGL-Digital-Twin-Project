using TMPro;
using UnityEngine;

/// <summary>
/// Displays a stable FPS reading using unscaled time, so it also works when
/// gameplay time is paused or slowed. Attach this to a TextMeshPro UI object.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public sealed class FpsDisplay : MonoBehaviour
{
    [SerializeField, Min(0.1f)]
    private float refreshInterval = 0.5f;

    [SerializeField]
    private bool showFrameTime = true;

    [SerializeField]
    private bool showQualityLevel = true;

    private TMP_Text label;
    private float elapsedTime;
    private int elapsedFrames;
    private int cachedQualityLevel = -1;
    private string cachedQualityName;

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
        label.raycastTarget = false;
    }

    private void Update()
    {
        elapsedTime += Time.unscaledDeltaTime;
        elapsedFrames++;

        if (elapsedTime < refreshInterval)
            return;

        float fps = elapsedFrames / elapsedTime;
        float frameTimeMs = 1000f / Mathf.Max(fps, 0.01f);

        if (showFrameTime && showQualityLevel)
        {
            label.text = $"FPS {fps:0}\n{frameTimeMs:0.0} ms\n{QualityLevelName()}";
        }
        else if (showFrameTime)
        {
            label.text = $"FPS {fps:0}\n{frameTimeMs:0.0} ms";
        }
        else if (showQualityLevel)
        {
            label.text = $"FPS {fps:0}\n{QualityLevelName()}";
        }
        else
        {
            label.text = $"FPS {fps:0}";
        }

        elapsedTime = 0f;
        elapsedFrames = 0;
    }

    /// <summary>
    /// QualitySettings.names builds a fresh string array on every read, which a
    /// readout twice a second has no reason to pay for. The preset is only
    /// changed deliberately, so the name is looked up when the level changes.
    /// </summary>
    private string QualityLevelName()
    {
        int level = QualitySettings.GetQualityLevel();
        if (level != cachedQualityLevel || cachedQualityName == null)
        {
            cachedQualityLevel = level;
            string[] names = QualitySettings.names;
            cachedQualityName = level >= 0 && level < names.Length ? names[level] : string.Empty;
        }

        return cachedQualityName;
    }
}
