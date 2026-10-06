using UnityEngine;

/// <summary>
/// Keeps this object's CanvasGroup in step with another one. Rails generated
/// at runtime follow the authored rail beside them, so whatever fades that
/// rail (the switch to Service, the telemetry sheet) fades them too without
/// knowing they exist.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
[DefaultExecutionOrder(90)]
public sealed class UICanvasGroupFollower : MonoBehaviour
{
    [SerializeField] private CanvasGroup source;

    private CanvasGroup group;

    public void Follow(CanvasGroup target)
    {
        source = target;
        Apply();
    }

    private void Awake() => group = GetComponent<CanvasGroup>();

    private void OnEnable() => Apply();

    // After everything that fades the source this frame.
    private void LateUpdate() => Apply();

    private void Apply()
    {
        if (source == null)
            return;
        if (group == null)
            group = GetComponent<CanvasGroup>();
        if (!Mathf.Approximately(group.alpha, source.alpha))
            group.alpha = source.alpha;
        group.interactable = source.interactable;
        group.blocksRaycasts = source.blocksRaycasts;
    }
}
