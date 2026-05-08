using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.TerrainShape.Editor.Clipper2;

using Path64 = System.Collections.Generic.List<SlimeJumping.TerrainShape.Editor.Clipper2.Point64>;
using Paths64 = System.Collections.Generic.List<System.Collections.Generic.List<SlimeJumping.TerrainShape.Editor.Clipper2.Point64>>;

namespace SlimeJumping.TerrainShape.Editor.Drawing
{
    // Multi-loop boolean operations on polygon outlines via Clipper2. All inputs/outputs are
    // open-form (no duplicated final vertex). Output preserves Clipper2's NonZero winding —
    // outer loops are CCW (signed area > 0), holes are CW (signed area < 0). Callers infer
    // outer/hole role from winding rather than a stored flag.
    public static class PolygonOps
    {
        // Clipper2 uses long integer coordinates. We multiply Vector2 (in local units, typically <100)
        // by 1e5 so a 1e-5 unit is the smallest distinguishable step — well below visible precision.
        const double SCALE = 100000.0;

        // Union of `existing` (multi-loop) with `drawn` (single closed loop).
        // Disjoint result is preserved as separate outer loops in the output.
        public static List<List<Vector2>> Union(IList<List<Vector2>> existing, IList<Vector2> drawn)
        {
            var cleanDrawn = Sanitize(drawn);
            if (cleanDrawn.Count < 3) return CopyMultiLoop(existing);

            var subject = MultiLoopToPaths64(existing);
            if (subject.Count == 0)
            {
                // No existing geometry — drawn becomes the lone outer.
                return new List<List<Vector2>> { cleanDrawn };
            }

            var clip = new Paths64 { ToPath64(cleanDrawn) };
            return RunClipper(subject, clip, ClipType.Union);
        }

        // Difference: `existing` minus `eraser`. Splits, holes, or full deletion are all natural
        // outcomes — the caller decides what to do (e.g. reject empty results).
        public static List<List<Vector2>> Subtract(IList<List<Vector2>> existing, IList<Vector2> eraser)
        {
            var cleanEraser = Sanitize(eraser);
            if (cleanEraser.Count < 3) return CopyMultiLoop(existing);

            var subject = MultiLoopToPaths64(existing);
            if (subject.Count == 0) return new List<List<Vector2>>();

            var clip = new Paths64 { ToPath64(cleanEraser) };
            return RunClipper(subject, clip, ClipType.Difference);
        }

        // Cleans up self-overlaps, fixes winding to NonZero convention. Used by mesh build to
        // keep TriangleNet's hole-point flood-fill from being confused by weird user states.
        public static List<List<Vector2>> Normalize(IList<List<Vector2>> loops)
        {
            var subject = MultiLoopToPaths64(loops);
            if (subject.Count == 0) return new List<List<Vector2>>();
            // Self-union — empty clip set. Re-runs the NonZero fill rule and outputs in canonical
            // outer-CCW / hole-CW form regardless of input winding.
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

        // Removes consecutive duplicates (sqrMagnitude < 1e-8 ≈ 1e-4 unit) and the closed-form
        // final-equals-first duplicate.
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
