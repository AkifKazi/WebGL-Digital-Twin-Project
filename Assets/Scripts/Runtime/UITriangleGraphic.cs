using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>The small triangle that points from a detail panel to its card.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UITriangleGraphic : MaskableGraphic
{
    [SerializeField] private bool pointLeft = true;

    public bool PointLeft
    {
        get => pointLeft;
        set
        {
            if (pointLeft == value)
                return;
            pointLeft = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        Vector2 tip = new(pointLeft ? r.xMin : r.xMax, r.center.y);
        float baseX = pointLeft ? r.xMax : r.xMin;
        int start = vh.currentVertCount;
        vh.AddVert(tip, color, Vector4.zero);
        vh.AddVert(new Vector2(baseX, r.yMax), color, Vector4.zero);
        vh.AddVert(new Vector2(baseX, r.yMin), color, Vector4.zero);
        vh.AddTriangle(start, start + 1, start + 2);
    }
}
