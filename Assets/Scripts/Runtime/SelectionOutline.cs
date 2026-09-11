using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the selection outline marks this frame. Written by
/// <see cref="MachineSelectionHighlighter"/> and read by
/// <see cref="SelectionOutlineFeature"/>. Static so the renderer feature, which
/// is an asset, needs no reference into the scene.
/// </summary>
public static class SelectionOutline
{
    private static readonly List<Renderer> targets = new();

    public static IReadOnlyList<Renderer> Targets => targets;

    public static Color OutlineColor { get; private set; } = new(0.25f, 0.65f, 1f, 1f);

    /// <summary>0 hides the outline, 1 draws it at full strength.</summary>
    public static float Strength { get; private set; }

    public static bool IsActive => Strength > 0.001f && targets.Count > 0;

    public static void SetTargets(IEnumerable<Renderer> renderers)
    {
        targets.Clear();

        if (renderers == null)
            return;

        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && !targets.Contains(renderer))
                targets.Add(renderer);
        }
    }

    public static void SetAppearance(Color color, float strength)
    {
        OutlineColor = color;
        Strength = Mathf.Clamp01(strength);
    }

    public static void Clear()
    {
        targets.Clear();
        Strength = 0f;
    }

    // Static state survives a play-mode restart when domain reload is off.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad() => Clear();
}
