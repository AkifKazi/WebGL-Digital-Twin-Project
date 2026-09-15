using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Mesh helpers shared by the telemetry graphics below.</summary>
internal static class UIMesh
{
    public static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color32 ca, Color32 cb, Color32 cc, Color32 cd)
    {
        int start = vh.currentVertCount;
        vh.AddVert(a, ca, Vector4.zero);
        vh.AddVert(b, cb, Vector4.zero);
        vh.AddVert(c, cc, Vector4.zero);
        vh.AddVert(d, cd, Vector4.zero);
        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }

    public static void AddRect(VertexHelper vh, Vector2 min, Vector2 max, Color32 color) =>
        AddQuad(vh, min, new Vector2(min.x, max.y), max, new Vector2(max.x, min.y), color, color, color, color);

    /// <summary>A straight segment of the given width, slightly overlapping its neighbours so joints stay closed.</summary>
    public static void AddLine(VertexHelper vh, Vector2 from, Vector2 to, float width, Color32 color)
    {
        Vector2 direction = to - from;
        if (direction.sqrMagnitude < 1e-6f)
            return;
        direction.Normalize();
        Vector2 extend = direction * (width * 0.25f);
        Vector2 normal = new Vector2(-direction.y, direction.x) * (width * 0.5f);
        from -= extend;
        to += extend;
        AddQuad(vh, from - normal, from + normal, to + normal, to - normal, color, color, color, color);
    }

    public static void AddDisc(VertexHelper vh, Vector2 centre, float radius, Color32 color, int segments = 20)
    {
        int start = vh.currentVertCount;
        vh.AddVert(centre, color, Vector4.zero);
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            vh.AddVert(centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, color, Vector4.zero);
        }
        for (int i = 1; i <= segments; i++)
            vh.AddTriangle(start, start + i, start + i + 1);
    }
}
