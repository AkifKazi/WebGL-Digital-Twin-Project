using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A service pin on the model in one mesh, in the menu style of the rest of
/// the UI: a dark glass tile with a border and a lit bar down its left edge in
/// the part's state colour, a soft halo round it, and a flag in the top right
/// corner when the part is due. The dark tile keeps the number readable over
/// a bright sky as well as over the dark machine.
///
/// Hovered, a selection frame settles in round the tile, a gap away from it.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIServicePinGraphic : MaskableGraphic
{
    [SerializeField] private Color baseColor = new(0.016f, 0.051f, 0.071f, 0.86f);
    [SerializeField] private Color stateColor = Color.cyan;
    [SerializeField] private Color selectColor = new(0.918f, 0.992f, 1f, 1f);
    [Tooltip("How much of the state colour tints the tile.")]
    [SerializeField, Range(0f, 1f)] private float tint = 0.16f;
    [SerializeField, Min(0f)] private float border = 1.5f;
    [SerializeField, Min(0f)] private float accent = 3f;
    [SerializeField, Min(0f)] private float flag = 9f;
    [Tooltip("The selection frame's gap from the tile, once it has settled.")]
    [SerializeField, Min(0f)] private float selectGap = 4f;
    [Tooltip("How much further out the selection frame starts before it settles.")]
    [SerializeField, Min(0f)] private float selectTravel = 6f;

    private bool flagged;
    private float selected;

    /// <summary>The state colour, and whether the part is due (it then carries the corner flag).</summary>
    public void SetState(Color state, bool due)
    {
        if (stateColor == state && flagged == due)
            return;
        stateColor = state;
        flagged = due;
        SetVerticesDirty();
    }

    /// <summary>How far the selection frame has settled in, 0 (none) to 1.</summary>
    public void SetSelected(float amount)
    {
        amount = Mathf.Clamp01(amount);
        if (Mathf.Approximately(selected, amount))
            return;
        selected = amount;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0f || r.height <= 0f)
            return;

        // The halo: fading frames outside the tile, brighter when selected.
        float halo = 1f + selected * 0.6f;
        AddFrame(vh, Grow(r, 1f), 1f, Fade(stateColor, 0.32f * halo));
        AddFrame(vh, Grow(r, 2f), 1f, Fade(stateColor, 0.14f * halo));
        AddFrame(vh, Grow(r, 3f), 1f, Fade(stateColor, 0.05f * halo));

        Color fill = Color.Lerp(baseColor, stateColor, tint + selected * 0.08f);
        fill.a = baseColor.a;
        UIMesh.AddRect(vh, r.min, r.max, fill * color);

        // Light falling from the top edge, as on the panels.
        Color top = Fade(stateColor, 0.18f + selected * 0.1f);
        Color clear = Fade(stateColor, 0f);
        float sheen = r.yMax - r.height * 0.55f;
        UIMesh.AddQuad(vh,
            new Vector2(r.xMin, sheen), new Vector2(r.xMin, r.yMax),
            new Vector2(r.xMax, r.yMax), new Vector2(r.xMax, sheen),
            clear, top, top, clear);

        AddFrame(vh, r, border, stateColor * color);
        UIMesh.AddRect(vh, r.min, new Vector2(r.xMin + accent, r.yMax), stateColor * color);

        if (flagged && flag > 0f)
        {
            float size = Mathf.Min(flag, r.width * 0.4f, r.height * 0.4f);
            Color c = stateColor * color;
            AddTriangle(vh, new Vector2(r.xMax - size, r.yMax), r.max, new Vector2(r.xMax, r.yMax - size), c);
        }

        if (selected <= 0f)
            return;

        float eased = 1f - (1f - selected) * (1f - selected);
        Rect frame = Grow(r, selectGap + border + (1f - eased) * selectTravel);
        Color select = Fade(selectColor, selectColor.a * eased);
        AddFrame(vh, frame, 1.5f, select);
    }

    private Color Fade(Color c, float alpha) => new Color(c.r, c.g, c.b, alpha) * color;

    private static Rect Grow(Rect r, float by) =>
        new(r.xMin - by, r.yMin - by, r.width + by * 2f, r.height + by * 2f);

    private static void AddFrame(VertexHelper vh, Rect r, float t, Color c)
    {
        t = Mathf.Min(t, r.width * 0.5f, r.height * 0.5f);
        if (t <= 0f || c.a <= 0f)
            return;
        UIMesh.AddRect(vh, new Vector2(r.xMin, r.yMax - t), r.max, c);
        UIMesh.AddRect(vh, r.min, new Vector2(r.xMax, r.yMin + t), c);
        UIMesh.AddRect(vh, new Vector2(r.xMin, r.yMin + t), new Vector2(r.xMin + t, r.yMax - t), c);
        UIMesh.AddRect(vh, new Vector2(r.xMax - t, r.yMin + t), new Vector2(r.xMax, r.yMax - t), c);
    }

    private static void AddTriangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color32 color)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, color, Vector4.zero);
        vh.AddVert(b, color, Vector4.zero);
        vh.AddVert(c, color, Vector4.zero);
        vh.AddTriangle(start, start + 1, start + 2);
    }
}
