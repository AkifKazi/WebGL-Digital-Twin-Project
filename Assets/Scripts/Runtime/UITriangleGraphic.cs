using UnityEngine;
using UnityEngine.UI;

/// <summary>The small triangle that points from a detail panel to its card.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UITriangleGraphic : MaskableGraphic
{
    public enum PointDirection
    {
        Left,
        Right,
        Up,
        Down
    }

    [SerializeField] private bool pointLeft = true;
    [SerializeField] private PointDirection direction = PointDirection.Left;

    /// <summary>Left or right. Kept for scenes and tools that set only the side.</summary>
    public bool PointLeft
    {
        get => direction == PointDirection.Left;
        set => Direction = value ? PointDirection.Left : PointDirection.Right;
    }

    public PointDirection Direction
    {
        get => direction;
        set
        {
            if (direction == value)
                return;
            direction = value;
            pointLeft = value == PointDirection.Left;
            SetVerticesDirty();
        }
    }

    protected override void Awake()
    {
        base.Awake();
        // Older scenes saved only the side.
        if (direction is PointDirection.Left or PointDirection.Right)
            direction = pointLeft ? PointDirection.Left : PointDirection.Right;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        Vector2 tip;
        Vector2 baseA;
        Vector2 baseB;
        switch (direction)
        {
            case PointDirection.Up:
                tip = new Vector2(r.center.x, r.yMax);
                baseA = new Vector2(r.xMin, r.yMin);
                baseB = new Vector2(r.xMax, r.yMin);
                break;
            case PointDirection.Down:
                tip = new Vector2(r.center.x, r.yMin);
                baseA = new Vector2(r.xMax, r.yMax);
                baseB = new Vector2(r.xMin, r.yMax);
                break;
            case PointDirection.Right:
                tip = new Vector2(r.xMax, r.center.y);
                baseA = new Vector2(r.xMin, r.yMax);
                baseB = new Vector2(r.xMin, r.yMin);
                break;
            default:
                tip = new Vector2(r.xMin, r.center.y);
                baseA = new Vector2(r.xMax, r.yMax);
                baseB = new Vector2(r.xMax, r.yMin);
                break;
        }

        int start = vh.currentVertCount;
        vh.AddVert(tip, color, Vector4.zero);
        vh.AddVert(baseA, color, Vector4.zero);
        vh.AddVert(baseB, color, Vector4.zero);
        vh.AddTriangle(start, start + 1, start + 2);
    }
}
