using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A square-cornered outline with an optional fill, drawn as one mesh. Used for
/// the service view's rows, pins and scrollbar, which need a border in a
/// state colour the sliced panel sprites cannot take.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIFrameGraphic : MaskableGraphic
{
    [SerializeField, Min(0f)] private float thickness = 2f;
    [SerializeField] private Color borderColor = Color.white;
    [SerializeField] private Color fillColor = Color.clear;

    public void SetColors(Color border, Color fill)
    {
        if (borderColor == border && fillColor == fill)
            return;
        borderColor = border;
        fillColor = fill;
        SetVerticesDirty();
    }

    public void SetThickness(float value)
    {
        value = Mathf.Max(0f, value);
        if (Mathf.Approximately(thickness, value))
            return;
        thickness = value;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width <= 0f || r.height <= 0f)
            return;

        if (fillColor.a > 0f)
            UIMesh.AddRect(vh, r.min, r.max, fillColor * color);

        float t = Mathf.Min(thickness, r.width * 0.5f, r.height * 0.5f);
        if (t <= 0f || borderColor.a <= 0f)
            return;

        Color edge = borderColor * color;
        UIMesh.AddRect(vh, new Vector2(r.xMin, r.yMax - t), r.max, edge);
        UIMesh.AddRect(vh, r.min, new Vector2(r.xMax, r.yMin + t), edge);
        UIMesh.AddRect(vh, new Vector2(r.xMin, r.yMin + t), new Vector2(r.xMin + t, r.yMax - t), edge);
        UIMesh.AddRect(vh, new Vector2(r.xMax - t, r.yMin + t), new Vector2(r.xMax, r.yMax - t), edge);
    }
}
