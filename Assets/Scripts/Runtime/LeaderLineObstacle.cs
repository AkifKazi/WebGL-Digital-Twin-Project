using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a piece of translucent UI, such as the bottom controls or the top bar's
/// pagination, that leader lines fade over, as they fade over cards: a line
/// showing through it would make it hard to read. Opaque UI (the detail
/// panel) needs no marker; a line simply runs behind it.
///
/// Each layout's <see cref="StatLeaderLineManager"/> finds the markers in its
/// layout and hands them to its lines. A marker that serves every layout
/// (a window floating over both, such as the Ask window) is marked
/// <see cref="EveryLayout"/>, and every manager adds it to its own.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class LeaderLineObstacle : MonoBehaviour
{
    [Tooltip("Lines of every layout fade over it, not only the layout it sits in. For UI outside the layouts.")]
    [SerializeField] private bool everyLayout;

    private static readonly List<LeaderLineObstacle> Shared = new();
    private CanvasGroup[] groups;

    /// <summary>Raised when a marker for every layout comes or goes, so the managers gather again.</summary>
    public static event Action SharedChanged;

    /// <summary>The markers for every layout that are enabled now.</summary>
    public static IReadOnlyList<LeaderLineObstacle> EveryLayoutObstacles => Shared;

    public RectTransform Rect => (RectTransform)transform;

    public bool EveryLayout
    {
        get => everyLayout;
        set
        {
            if (everyLayout == value)
                return;
            everyLayout = value;
            Register(isActiveAndEnabled && value);
        }
    }

    /// <summary>On screen and drawn: active, and not faded out by a group above it (the pagination when there is one page, say).</summary>
    public bool IsShown
    {
        get
        {
            if (!isActiveAndEnabled)
                return false;
            groups ??= GetComponentsInParent<CanvasGroup>(true);
            float alpha = 1f;
            foreach (CanvasGroup group in groups)
            {
                alpha *= group.alpha;
                if (group.ignoreParentGroups)
                    break;
            }
            return alpha >= 0.05f;
        }
    }

    private void OnEnable() => Register(everyLayout);

    private void OnDisable() => Register(false);

    private void Register(bool shared)
    {
        bool listed = Shared.Contains(this);
        if (shared == listed)
            return;
        if (shared)
            Shared.Add(this);
        else
            Shared.Remove(this);
        SharedChanged?.Invoke();
    }

    // A move in the hierarchy changes the groups above it.
    private void OnTransformParentChanged() => groups = null;
}
