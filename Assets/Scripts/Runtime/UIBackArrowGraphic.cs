using UnityEngine;
using UnityEngine.UI;

/// <summary>A left-pointing arrow, drawn centred in its rect in the graphic's colour.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIBackArrowGraphic : MaskableGraphic
{
    [SerializeField, Min(4f)] private float size = 24f;
    [SerializeField, Min(0.5f)] private float thickness = 2.6f;

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Vector2 centre = GetPixelAdjustedRect().center;
        float half = size * 0.5f;
        Vector2 tip = centre + new Vector2(-half * 0.8f, 0f);
        UIMesh.AddLine(vh, tip, centre + new Vector2(half * 0.8f, 0f), thickness, color);
        UIMesh.AddLine(vh, tip, tip + new Vector2(half * 0.55f, half * 0.55f), thickness, color);
        UIMesh.AddLine(vh, tip, tip + new Vector2(half * 0.55f, -half * 0.55f), thickness, color);
    }
}
