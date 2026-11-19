using UnityEngine;
using UnityEngine.UI;

/// <summary>結果画面で使う円・星・フック・衝撃の仮表示。</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class WinnerResultSymbol : MaskableGraphic
{
    public enum Shape { Circle, Star, Hook, Burst }
    public Shape Symbol;

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Vector2 center = rectTransform.rect.center;
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;
        if (Symbol == Shape.Hook)
        {
            for (int i = 0; i < 20; i++)
            {
                float a = Mathf.Lerp(-160f, 100f, i / 20f) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(-160f, 100f, (i + 1f) / 20f) * Mathf.Deg2Rad;
                AddQuad(mesh, center + Direction(a) * radius, center + Direction(b) * radius,
                    center + Direction(b) * radius * 0.72f, center + Direction(a) * radius * 0.72f);
            }
            return;
        }

        int points = Symbol == Shape.Circle ? 48 : Symbol == Shape.Star ? 10 : 16;
        mesh.AddVert(center, color, Vector2.zero);
        for (int i = 0; i <= points; i++)
        {
            float angle = Mathf.PI * 0.5f + i * Mathf.PI * 2f / points;
            float size = Symbol == Shape.Circle || i % 2 == 0 ? radius : radius * 0.43f;
            mesh.AddVert(center + Direction(angle) * size, color, Vector2.zero);
            if (i > 0)
            {
                mesh.AddTriangle(0, i, i + 1);
            }
        }
    }

    private static Vector2 Direction(float angle) => new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

    private void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(a, color, Vector2.zero);
        mesh.AddVert(b, color, Vector2.zero);
        mesh.AddVert(c, color, Vector2.zero);
        mesh.AddVert(d, color, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start, start + 2, start + 3);
    }
}
