using UnityEngine;

/// <summary>
/// Marks a piece of translucent UI, such as the bottom controls or the top bar's
/// pagination, that leader lines fade over, as they fade over cards: a line
/// showing through it would make it hard to read. Opaque UI (the detail
/// panel) needs no marker; a line simply runs behind it.
///
/// Each layout's <see cref="StatLeaderLineManager"/> finds the markers in its
/// layout and hands them to its lines.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public sealed class LeaderLineObstacle : MonoBehaviour
{
    private CanvasGroup[] groups;

    public RectTransform Rect => (RectTransform)transform;

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

    // A move in the hierarchy changes the groups above it.
    private void OnTransformParentChanged() => groups = null;
}
