using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class scr_mapLineRenderer : Graphic
{
    public float lineWidth = 3f;

    private List<(Vector2 a, Vector2 b, Color c)> segments = new List<(Vector2, Vector2, Color)>();

    public void SetSegments(List<(Vector2 a, Vector2 b, Color c)> segs)
    {
        segments = segs;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        foreach (var seg in segments)
            DrawSegment(vh, seg.a, seg.b, seg.c);
    }

    private void DrawSegment(VertexHelper vh, Vector2 a, Vector2 b, Color c)
    {
        int start = vh.currentVertCount;
        Vector2 dir = (b - a).normalized;
        Vector2 perp = new Vector2(-dir.y, dir.x) * lineWidth * 0.5f;

        UIVertex v = UIVertex.simpleVert;
        v.color = c;

        v.position = a - perp; vh.AddVert(v);
        v.position = a + perp; vh.AddVert(v);
        v.position = b + perp; vh.AddVert(v);
        v.position = b - perp; vh.AddVert(v);

        vh.AddTriangle(start, start + 1, start + 2);
        vh.AddTriangle(start, start + 2, start + 3);
    }
}