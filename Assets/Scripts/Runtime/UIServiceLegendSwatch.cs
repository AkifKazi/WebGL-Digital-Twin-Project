using UnityEngine;
using UnityEngine.UI;

/// <summary>One element of the service bar on its own, for the legend under the list.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class UIServiceLegendSwatch : MaskableGraphic
{
    public enum Kind
    {
        Run,
        Estimate,
        Plan,
        Gap
    }

    [SerializeField] private Kind kind;
    [SerializeField] private Color swatchColor = Color.white;

    public void Configure(Kind value, Color swatch)
    {
        kind = value;
        swatchColor = swatch;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect r = GetPixelAdjustedRect();
        Color c = swatchColor * color;
        float mid = r.center.y;
        switch (kind)
        {
            case Kind.Run:
                UIMesh.AddRect(vh, new Vector2(r.xMin, mid - 3f), new Vector2(r.xMax, mid + 3f), c);
                break;
            case Kind.Estimate:
                UIMesh.AddRect(vh, new Vector2(r.center.x - 1f, r.yMin), new Vector2(r.center.x + 1f, r.yMax), c);
                break;
            case Kind.Plan:
                for (float y = r.yMin; y < r.yMax; y += 4f)
                    UIMesh.AddRect(vh, new Vector2(r.center.x - 1f, y), new Vector2(r.center.x + 1f, Mathf.Min(y + 2f, r.yMax)), c);
                break;
            case Kind.Gap:
                // As on the bar: hatched, bracketed above and below.
                Color edge = new(c.r, c.g, c.b, c.a * 0.55f);
                UIServiceBarGraphic.AddHatch(vh, r.xMin, r.xMax, mid - 3f, mid + 3f, c,
                    UIServiceBarGraphic.GapHatchPitch, UIServiceBarGraphic.GapHatchStripe);
                UIMesh.AddRect(vh, new Vector2(r.xMin, mid + 3f), new Vector2(r.xMax, mid + 4f), edge);
                UIMesh.AddRect(vh, new Vector2(r.xMin, mid - 4f), new Vector2(r.xMax, mid - 3f), edge);
                break;
        }
    }
}
