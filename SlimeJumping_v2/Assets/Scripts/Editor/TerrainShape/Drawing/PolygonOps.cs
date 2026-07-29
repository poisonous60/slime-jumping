using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.TerrainShape.Editor.Clipper2;

using Path64 = System.Collections.Generic.List<SlimeJumping.TerrainShape.Editor.Clipper2.Point64>;
using Paths64 = System.Collections.Generic.List<System.Collections.Generic.List<SlimeJumping.TerrainShape.Editor.Clipper2.Point64>>;

namespace SlimeJumping.TerrainShape.Editor.Drawing
{
    // Multi-loop polygon booleans via Clipper2. Loops are open-form (no repeated final vertex).
    // Winding carries the role: outer = CCW, hole = CW. Callers infer role from winding, not a flag.
    public static class PolygonOps
    {
        // Clipper2 takes long integer coords; 1e5 scale makes 1e-5 local units the smallest step.
        const double SCALE = 100000.0;

        // Union of `existing` (multi-loop) with `drawn`; disjoint parts stay separate outer loops.
        public static List<List<Vector2>> Union(IList<List<Vector2>> existing, IList<Vector2> drawn)
        {
            var cleanDrawn = Sanitize(drawn);
            if (cleanDrawn.Count < 3) return CopyMultiLoop(existing);

            var subject = MultiLoopToPaths64(existing);
            if (subject.Count == 0)
            {
                return new List<List<Vector2>> { cleanDrawn };
            }

            var clip = new Paths64 { ToPath64(cleanDrawn) };
            return RunClipper(subject, clip, ClipType.Union);
        }

        // `existing` minus `eraser`. Splits, holes and empty results are all valid — caller decides.
        public static List<List<Vector2>> Subtract(IList<List<Vector2>> existing, IList<Vector2> eraser)
        {
            var cleanEraser = Sanitize(eraser);
            if (cleanEraser.Count < 3) return CopyMultiLoop(existing);

            var subject = MultiLoopToPaths64(existing);
            if (subject.Count == 0) return new List<List<Vector2>>();

            var clip = new Paths64 { ToPath64(cleanEraser) };
            return RunClipper(subject, clip, ClipType.Difference);
        }

        // Fixes self-overlaps/winding; mesh build needs this or TriangleNet's hole flood-fill misfires.
        public static List<List<Vector2>> Normalize(IList<List<Vector2>> loops)
        {
            var subject = MultiLoopToPaths64(loops);
            if (subject.Count == 0) return new List<List<Vector2>>();
            // Self-union with empty clip: NonZero re-fill normalizes to outer-CCW / hole-CW.
            return RunClipper(subject, new Paths64(), ClipType.Union);
        }

        // ────────── Internals ──────────

        static Paths64 MultiLoopToPaths64(IList<List<Vector2>> loops)
        {
            var result = new Paths64();
            if (loops == null) return result;
            foreach (var loop in loops)
            {
                var clean = Sanitize(loop);
                if (clean.Count >= 3) result.Add(ToPath64(clean));
            }
            return result;
        }

        static List<List<Vector2>> RunClipper(Paths64 subject, Paths64 clip, ClipType op)
        {
            var clipper = new Clipper64();
            clipper.AddSubject(subject);
            if (clip.Count > 0) clipper.AddClip(clip);
            var solution = new Paths64();
            if (!clipper.Execute(op, FillRule.NonZero, solution))
                return new List<List<Vector2>>();

            var result = new List<List<Vector2>>(solution.Count);
            foreach (var path in solution)
            {
                if (path.Count < 3) continue;
                result.Add(FromPath64(path));
            }
            return result;
        }

        static List<List<Vector2>> CopyMultiLoop(IList<List<Vector2>> loops)
        {
            var result = new List<List<Vector2>>(loops != null ? loops.Count : 0);
            if (loops == null) return result;
            foreach (var loop in loops)
            {
                var clean = Sanitize(loop);
                if (clean.Count >= 3) result.Add(clean);
            }
            return result;
        }

        // Drops consecutive duplicates and the closed-form final-equals-first vertex.
        static List<Vector2> Sanitize(IList<Vector2> pts)
        {
            const float DEDUP_SQR = 1e-8f;
            var result = new List<Vector2>(pts != null ? pts.Count : 0);
            if (pts == null) return result;
            for (int i = 0; i < pts.Count; i++)
            {
                Vector2 p = pts[i];
                if (result.Count > 0 && (result[result.Count - 1] - p).sqrMagnitude < DEDUP_SQR) continue;
                result.Add(p);
            }
            if (result.Count >= 2 && (result[0] - result[result.Count - 1]).sqrMagnitude < DEDUP_SQR)
                result.RemoveAt(result.Count - 1);
            return result;
        }

        static Path64 ToPath64(IList<Vector2> pts)
        {
            var path = new Path64(pts.Count);
            for (int i = 0; i < pts.Count; i++)
            {
                path.Add(new Point64(
                    (long)System.Math.Round(pts[i].x * SCALE),
                    (long)System.Math.Round(pts[i].y * SCALE)));
            }
            return path;
        }

        static List<Vector2> FromPath64(Path64 path)
        {
            var result = new List<Vector2>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                result.Add(new Vector2(
                    (float)(path[i].X / SCALE),
                    (float)(path[i].Y / SCALE)));
            }
            return result;
        }
    }
}
