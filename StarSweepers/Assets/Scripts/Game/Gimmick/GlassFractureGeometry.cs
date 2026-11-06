using System.Collections.Generic;
using UnityEngine;

internal static class GlassFractureGeometry
{
    private const float Epsilon = 0.0001f;

    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    public static Vector2 ExtendToEdge(Vector2 point, Vector2 direction, float halfX, float halfZ)
    {
        float xTime = Mathf.Abs(direction.x) > Epsilon
            ? ((direction.x > 0f ? halfX : -halfX) - point.x) / direction.x
            : float.PositiveInfinity;
        float zTime = Mathf.Abs(direction.y) > Epsilon
            ? ((direction.y > 0f ? halfZ : -halfZ) - point.y) / direction.y
            : float.PositiveInfinity;
        return point + direction * Mathf.Max(0f, Mathf.Min(xTime, zTime));
    }

    public static List<List<Vector2>> GetFaces(
        List<(Vector2 Start, Vector2 End)> cuts, float halfX, float halfZ)
    {
        var corners = new[]
        {
            new Vector2(-halfX, -halfZ), new Vector2(halfX, -halfZ),
            new Vector2(halfX, halfZ), new Vector2(-halfX, halfZ)
        };
        for (int i = 0; i < corners.Length; i++)
        {
            cuts.Add((corners[i], corners[(i + 1) % corners.Length]));
        }

        var points = new List<Vector2>();
        var neighbors = new List<List<int>>();
        foreach (var cut in cuts)
        {
            Vector2 direction = cut.End - cut.Start;
            var stops = new List<float> { 0f, 1f };
            foreach (var other in cuts)
            {
                AddIntersections(stops, cut.Start, direction, other.Start, other.End);
            }
            stops.Sort();

            int previous = AddVertex(points, neighbors, cut.Start);
            foreach (float stop in stops)
            {
                int next = AddVertex(points, neighbors, cut.Start + direction * stop);
                if (next != previous && !neighbors[previous].Contains(next))
                {
                    neighbors[previous].Add(next);
                    neighbors[next].Add(previous);
                }
                previous = next;
            }
        }

        // Walking clockwise from each incoming edge traces the face on its left.
        for (int i = 0; i < points.Count; i++)
        {
            Vector2 pivot = points[i];
            neighbors[i].Sort((a, b) => Angle(points[a] - pivot).CompareTo(Angle(points[b] - pivot)));
        }

        var visited = new HashSet<(int, int)>();
        var faces = new List<List<Vector2>>();
        for (int start = 0; start < points.Count; start++)
        {
            foreach (int neighbor in neighbors[start])
            {
                var face = new List<Vector2>();
                int from = start;
                int to = neighbor;
                while (visited.Add((from, to)))
                {
                    face.Add(points[from]);
                    List<int> nextEdges = neighbors[to];
                    int incoming = nextEdges.IndexOf(from);
                    from = to;
                    to = nextEdges[(incoming + nextEdges.Count - 1) % nextEdges.Count];
                }

                float area = 0f;
                for (int i = 0; i < face.Count; i++)
                {
                    area += Cross(face[i], face[(i + 1) % face.Count]);
                }
                if (area > Epsilon)
                {
                    faces.Add(face);
                }
            }
        }
        return faces;
    }

    private static float Angle(Vector2 direction) => Mathf.Atan2(direction.y, direction.x);

    private static int AddVertex(List<Vector2> points, List<List<int>> neighbors, Vector2 point)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if ((points[i] - point).sqrMagnitude <= Epsilon * Epsilon)
            {
                return i;
            }
        }
        points.Add(point);
        neighbors.Add(new List<int>());
        return points.Count - 1;
    }

    private static void AddIntersections(
        List<float> stops, Vector2 start, Vector2 direction, Vector2 otherStart, Vector2 otherEnd)
    {
        Vector2 otherDirection = otherEnd - otherStart;
        Vector2 offset = otherStart - start;
        float determinant = Cross(direction, otherDirection);
        if (Mathf.Abs(determinant) > Epsilon * direction.magnitude * otherDirection.magnitude)
        {
            float t = Cross(offset, otherDirection) / determinant;
            float u = Cross(offset, direction) / determinant;
            if (t >= -Epsilon && t <= 1f + Epsilon && u >= -Epsilon && u <= 1f + Epsilon)
            {
                stops.Add(Mathf.Clamp01(t));
            }
            return;
        }

        // Collinear cuts can overlap along a pane edge. Split both at the shared endpoints.
        float length = direction.magnitude;
        if (length <= Epsilon || Mathf.Abs(Cross(offset, direction)) > Epsilon * length)
        {
            return;
        }
        float first = Vector2.Dot(offset, direction) / direction.sqrMagnitude;
        float last = Vector2.Dot(otherEnd - start, direction) / direction.sqrMagnitude;
        stops.Add(Mathf.Clamp01(first));
        stops.Add(Mathf.Clamp01(last));
    }

    public static List<int> Triangulate(List<Vector2> polygon)
    {
        var remaining = new List<int>();
        var triangles = new List<int>();
        for (int i = 0; i < polygon.Count; i++)
        {
            remaining.Add(i);
        }

        while (remaining.Count >= 3)
        {
            bool clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int a = remaining[(i + remaining.Count - 1) % remaining.Count];
                int b = remaining[i];
                int c = remaining[(i + 1) % remaining.Count];
                Vector2 ab = polygon[b] - polygon[a];
                Vector2 bc = polygon[c] - polygon[b];
                float turn = Cross(ab, bc);
                if (Mathf.Abs(turn) <= Epsilon * Epsilon && Vector2.Dot(ab, bc) >= 0f)
                {
                    remaining.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (turn <= 0f || ContainsOtherVertex(polygon, remaining, a, b, c))
                {
                    continue;
                }

                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
            {
                triangles.Clear();
                break;
            }
        }
        return triangles;
    }

    private static bool ContainsOtherVertex(List<Vector2> polygon, List<int> remaining, int a, int b, int c)
    {
        foreach (int index in remaining)
        {
            if (index == a || index == b || index == c)
            {
                continue;
            }
            Vector2 point = polygon[index];
            if (Cross(polygon[b] - polygon[a], point - polygon[a]) >= 0f
                && Cross(polygon[c] - polygon[b], point - polygon[b]) >= 0f
                && Cross(polygon[a] - polygon[c], point - polygon[c]) >= 0f)
            {
                return true;
            }
        }
        return false;
    }

    public static Mesh Extrude(List<Vector2> polygon, List<int> faces, Vector2 pivot, float thickness)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        int count = polygon.Count;
        float top = thickness * 0.5f;

        foreach (Vector2 point in polygon)
        {
            vertices.Add(Position(point, pivot, top));
        }
        foreach (Vector2 point in polygon)
        {
            vertices.Add(Position(point, pivot, -top));
        }
        for (int i = 0; i < faces.Count; i += 3)
        {
            triangles.Add(faces[i]);
            triangles.Add(faces[i + 2]);
            triangles.Add(faces[i + 1]);
            triangles.Add(faces[i] + count);
            triangles.Add(faces[i + 1] + count);
            triangles.Add(faces[i + 2] + count);
        }

        for (int i = 0; i < count; i++)
        {
            int next = (i + 1) % count;
            int start = vertices.Count;
            vertices.Add(vertices[i]);
            vertices.Add(vertices[next]);
            vertices.Add(vertices[next + count]);
            vertices.Add(vertices[i + count]);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        var mesh = new Mesh { name = "Glass shard" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        return mesh;
    }

    private static Vector3 Position(Vector2 point, Vector2 pivot, float height)
    {
        return new Vector3(point.x - pivot.x, height, point.y - pivot.y);
    }
}
