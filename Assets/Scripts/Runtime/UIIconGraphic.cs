using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Vector icons for the panel header, so no icon textures ship with it.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIIconGraphic : MaskableGraphic
{
    public enum Shape
    {
        Close,
        Pin
    }

    [SerializeField] private Shape shape = Shape.Close;
    [SerializeField, Min(0.5f)] private float thickness = 2f;

    public Shape IconShape
    {
        get => shape;
        set
        {
            shape = value;
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        float size = Mathf.Min(r.width, r.height);
        Vector2 c = r.center;

        if (shape == Shape.Close)
        {
            float h = size * 0.34f;
            UIMesh.AddLine(vh, c + new Vector2(-h, -h), c + new Vector2(h, h), thickness, color);
            UIMesh.AddLine(vh, c + new Vector2(-h, h), c + new Vector2(h, -h), thickness, color);
            return;
        }

        // A push pin, tilted like the pin in the reference.
        Quaternion tilt = Quaternion.Euler(0f, 0f, -45f);
        void Part(float x0, float y0, float x1, float y1)
        {
            Vector2 P(float x, float y) => c + (Vector2)(tilt * new Vector3(x * size, y * size, 0f));
            UIMesh.AddQuad(vh, P(x0, y0), P(x0, y1), P(x1, y1), P(x1, y0), color, color, color, color);
        }
        Part(-0.17f, 0.20f, 0.17f, 0.40f);   // head
        Part(-0.10f, 0.02f, 0.10f, 0.20f);   // body
        Part(-0.26f, -0.06f, 0.26f, 0.02f);  // collar
        Part(-0.025f, -0.42f, 0.025f, -0.06f); // needle
    }
}
