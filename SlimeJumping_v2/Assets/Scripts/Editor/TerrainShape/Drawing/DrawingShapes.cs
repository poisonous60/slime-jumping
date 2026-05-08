using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.TerrainShape.Editor.Drawing
{
    public enum DrawSubTool
    {
        Rectangle,
        Triangle,
        Circle,
        Polyline,
        Freehand,
        Stamp,
    }

    // Geometry helpers for the Draw tool. All outputs are open-form polygons (no duplicated final
    // vertex) in CCW winding when possible.
    public static class DrawingShapes
    {
        // Press → release bbox → axis-aligned rectangle. Returns 4 vertices CCW.
        public static List<Vector2> Rectangle(Vector2 a, Vector2 b)
        {
            float xmin = Mathf.Min(a.x, b.x), xmax = Mathf.Max(a.x, b.x);
            float ymin = Mathf.Min(a.y, b.y), ymax = Mathf.Max(a.y, b.y);
            return new List<Vector2>
            {
                new Vector2(xmin, ymin),
                new Vector2(xmax, ymin),
                new Vector2(xmax, ymax),
                new Vector2(xmin, ymax),
            };
        }

        // Isosceles triangle inscribed in bbox. Peak at top-middle, base along bbox bottom.
        public static List<Vector2> Triangle(Vector2 a, Vector2 b)
        {
            float xmin = Mathf.Min(a.x, b.x), xmax = Mathf.Max(a.x, b.x);
            float ymin = Mathf.Min(a.y, b.y), ymax = Mathf.Max(a.y, b.y);
            return new List<Vector2>
            {
                new Vector2(xmin, ymin),
                new Vector2(xmax, ymin),
                new Vector2((xmin + xmax) * 0.5f, ymax),
            };
        }

        // Center + radius. Vertex count adaptive: ~one vertex per 0.2 local-unit arc length.
        public static List<Vector2> Circle(Vector2 center, float radius)
        {
            radius = Mathf.Max(1e-4f, radius);
            int n = Mathf.Clamp(Mathf.RoundToInt(2f * Mathf.PI * radius / 0.2f), 8, 64);
            var pts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                // -π/2 start so the first vertex is at the bottom — matches the existing
                // Make Circle template's convention.
                float a = -Mathf.PI * 0.5f + (i / (float)n) * Mathf.PI * 2f;
                pts.Add(center + new Vector2(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius));
            }
            return pts;
        }

        // Ramer-Douglas-Peucker simplification. Keeps endpoints, drops intermediate points whose
        // perpendicular distance to the segment connecting the kept neighbours is below `epsilon`.
        // Stable, O(n log n) average. Used to thin freehand strokes.
        public static List<Vector2> RDPSimplify(IList<Vector2> raw, float epsilon)
        {
            int n = raw == null ? 0 : raw.Count;
            if (n <= 2 || epsilon <= 0f)
            {
                var copy = new List<Vector2>(n);
                if (raw != null) for (int i = 0; i < n; i++) copy.Add(raw[i]);
                return copy;
            }
            var keep = new bool[n];
            keep[0] = true;
            keep[n - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, n - 1));
            float eps2 = epsilon * epsilon;
            while (stack.Count > 0)
            {
                var (i0, i1) = stack.Pop();
                if (i1 - i0 < 2) continue;
                float maxD2 = 0f;
                int maxI = -1;
                Vector2 a = raw[i0], b = raw[i1];
                Vector2 ab = b - a;
                float ab2 = ab.sqrMagnitude;
                for (int i = i0 + 1; i < i1; i++)
                {
                    Vector2 ap = raw[i] - a;
                    float t = ab2 > 1e-12f ? Mathf.Clamp01(Vector2.Dot(ap, ab) / ab2) : 0f;
                    Vector2 cp = a + ab * t;
                    float d2 = (raw[i] - cp).sqrMagnitude;
                    if (d2 > maxD2) { maxD2 = d2; maxI = i; }
                }
                if (maxD2 > eps2 && maxI >= 0)
                {
                    keep[maxI] = true;
                    stack.Push((i0, maxI));
                    stack.Push((maxI, i1));
                }
            }
            var result = new List<Vector2>();
            for (int i = 0; i < n; i++) if (keep[i]) result.Add(raw[i]);
            return result;
        }

        // Closes a polygon by removing a final vertex equal (within epsilon) to the first.
        public static void DropFinalDuplicate(List<Vector2> pts, float eps = 1e-4f)
        {
            if (pts == null || pts.Count < 2) return;
            if ((pts[0] - pts[pts.Count - 1]).sqrMagnitude < eps * eps)
                pts.RemoveAt(pts.Count - 1);
        }
    }
}
