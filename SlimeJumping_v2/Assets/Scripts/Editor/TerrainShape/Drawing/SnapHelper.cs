using UnityEditor;
using UnityEngine;

namespace SlimeJumping.TerrainShape.Editor.Drawing
{
    public enum SnapKind { None, Vertex, Edge, Grid }

    public struct SnapResult
    {
        public Vector2 pos;     // local XY on the active layer plane
        public SnapKind kind;
        public bool snapped => kind != SnapKind.None;
    }

    public static class SnapHelper
    {
        // Resolves the cursor position with snap precedence: Vertex > Edge > Grid > raw cursor.
        // `raw` is the unsnapped local-XY projection of the cursor on the layer plane. Returns
        // a SnapResult containing the snapped (or raw) position and which snap fired.
        public static SnapResult Resolve(
            Vector2 raw,
            Transform xfm, float planeZ,
            TerrainShape2D src,
            bool vertex, bool edge, bool grid, float gridSize,
            Vector2 mouseGUI,
            float vertexPxRadius = 8f,
            float edgePxRadius = 8f)
        {
            var result = new SnapResult { pos = raw, kind = SnapKind.None };

            // Multi-loop: scan every loop's vertices and edges. Outer + hole both contribute.
            var front = src != null ? src.FrontLayer : null;
            if (vertex && front != null && front.loops != null)
            {
                float bestPx = vertexPxRadius;
                Vector2 bestLocal = raw;
                bool gotIt = false;
                foreach (var loop in front.loops)
                {
                    if (loop == null || loop.points == null) continue;
                    for (int i = 0; i < loop.points.Count; i++)
                    {
                        Vector2 lp = loop.points[i].pos;
                        Vector3 wp = xfm.TransformPoint(new Vector3(lp.x, lp.y, planeZ));
                        Vector2 g = HandleUtility.WorldToGUIPoint(wp);
                        float d = Vector2.Distance(g, mouseGUI);
                        if (d < bestPx) { bestPx = d; bestLocal = lp; gotIt = true; }
                    }
                }
                if (gotIt) { result.pos = bestLocal; result.kind = SnapKind.Vertex; return result; }
            }

            if (edge && front != null && front.loops != null)
            {
                float bestPx = edgePxRadius;
                Vector2 bestLocal = raw;
                bool gotIt = false;
                foreach (var loop in front.loops)
                {
                    if (loop == null || loop.points == null || loop.points.Count < 2) continue;
                    int n = loop.points.Count;
                    for (int i = 0; i < n; i++)
                    {
                        Vector2 a = loop.points[i].pos;
                        Vector2 b = loop.points[(i + 1) % n].pos;
                        Vector3 wa = xfm.TransformPoint(new Vector3(a.x, a.y, planeZ));
                        Vector3 wb = xfm.TransformPoint(new Vector3(b.x, b.y, planeZ));
                        Vector2 ga = HandleUtility.WorldToGUIPoint(wa);
                        Vector2 gb = HandleUtility.WorldToGUIPoint(wb);
                        Vector2 ab = gb - ga;
                        float l2 = ab.sqrMagnitude;
                        if (l2 < 1e-6f) continue;
                        float t = Mathf.Clamp01(Vector2.Dot(mouseGUI - ga, ab) / l2);
                        Vector2 cp = ga + ab * t;
                        float d = Vector2.Distance(cp, mouseGUI);
                        if (d < bestPx)
                        {
                            bestPx = d;
                            bestLocal = Vector2.Lerp(a, b, t);
                            gotIt = true;
                        }
                    }
                }
                if (gotIt) { result.pos = bestLocal; result.kind = SnapKind.Edge; return result; }
            }

            if (grid && gridSize > 1e-4f)
            {
                Vector2 snapped = new Vector2(
                    Mathf.Round(raw.x / gridSize) * gridSize,
                    Mathf.Round(raw.y / gridSize) * gridSize);
                result.pos = snapped;
                result.kind = SnapKind.Grid;
                return result;
            }

            return result;
        }

        // Draws a small disc indicator at the snap position. Cyan = vertex, green = edge, white = grid.
        public static void DrawSnapRing(Transform xfm, float planeZ, SnapResult snap)
        {
            if (snap.kind == SnapKind.None) return;
            Vector3 wp = xfm.TransformPoint(new Vector3(snap.pos.x, snap.pos.y, planeZ));
            float r = HandleUtility.GetHandleSize(wp) * 0.08f;
            Color c = snap.kind == SnapKind.Vertex ? Color.cyan
                    : snap.kind == SnapKind.Edge   ? new Color(0.4f, 1f, 0.4f)
                    : new Color(1f, 1f, 1f, 0.9f);
            Handles.color = c;
            Handles.DrawWireDisc(wp, xfm.forward, r);
            Handles.DrawWireDisc(wp, xfm.forward, r * 0.6f);
        }

        // Renders a faint grid in the active layer plane. Limited to a window around the cursor
        // to keep handle count low even with small grid sizes and large viewports.
        public static void DrawGridLines(Transform xfm, float planeZ, float gridSize, Vector2 cursorLocalXY,
            float halfExtentLocal = 4f, int maxLinesPerAxis = 24)
        {
            if (gridSize < 1e-4f) return;
            int half = Mathf.Min(maxLinesPerAxis / 2, Mathf.CeilToInt(halfExtentLocal / gridSize));
            float cx = Mathf.Round(cursorLocalXY.x / gridSize) * gridSize;
            float cy = Mathf.Round(cursorLocalXY.y / gridSize) * gridSize;
            Color c = new Color(0.7f, 0.85f, 1f, 0.18f);
            Handles.color = c;
            float minX = cx - half * gridSize, maxX = cx + half * gridSize;
            float minY = cy - half * gridSize, maxY = cy + half * gridSize;
            for (int i = -half; i <= half; i++)
            {
                float x = cx + i * gridSize;
                Vector3 a = xfm.TransformPoint(new Vector3(x, minY, planeZ));
                Vector3 b = xfm.TransformPoint(new Vector3(x, maxY, planeZ));
                Handles.DrawLine(a, b);
            }
            for (int j = -half; j <= half; j++)
            {
                float y = cy + j * gridSize;
                Vector3 a = xfm.TransformPoint(new Vector3(minX, y, planeZ));
                Vector3 b = xfm.TransformPoint(new Vector3(maxX, y, planeZ));
                Handles.DrawLine(a, b);
            }
        }

        // Snaps an angle (radians) to the nearest multiple of a step (degrees). The reference is
        // the world +X axis in local 2D space — i.e., absolute angle. Returns a unit-length
        // direction vector pointing along the snapped angle.
        public static Vector2 SnapAngleAbsolute(Vector2 dir, float stepDeg)
        {
            if (dir.sqrMagnitude < 1e-12f) return Vector2.right;
            float angRad = Mathf.Atan2(dir.y, dir.x);
            float angDeg = angRad * Mathf.Rad2Deg;
            float snappedDeg = Mathf.Round(angDeg / stepDeg) * stepDeg;
            float r = snappedDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(r), Mathf.Sin(r));
        }
    }
}
