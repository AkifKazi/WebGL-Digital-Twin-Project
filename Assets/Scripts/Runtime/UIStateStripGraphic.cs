using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A row of equal segments with a gap between them: the status timeline.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIStateStripGraphic : MaskableGraphic
{
    [SerializeField, Min(0f)] private float gap = 2f;

    private readonly List<Color> segments = new();

    public void SetSegments(IReadOnlyList<Color> colors)
    {
        segments.Clear();
        for (int i = 0; i < colors.Count; i++)
            segments.Add(colors[i]);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        int count = segments.Count;
        if (count == 0)
            return;

        Rect rect = GetPixelAdjustedRect();
        float spacing = gap;
        float width = (rect.width - spacing * (count - 1)) / count;
        if (width < 1f)
        {
            spacing = 0f;
            width = rect.width / count;
        }

        for (int i = 0; i < count; i++)
        {
            float x = rect.xMin + i * (width + spacing);
            UIMesh.AddRect(vh, new Vector2(x, rect.yMin), new Vector2(x + width, rect.yMax), segments[i] * color);
        }
    }
}
