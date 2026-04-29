using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// Barycentric recursive subdivision of a TopPatch. Each level splits every triangle
    /// into 4 sub-triangles by inserting midpoint vertices on each edge; midpoints are
    /// shared between adjacent triangles via canonical edge keying. Boundary edges split
    /// along with their parent edges so the patch perimeter stays intact (and dense).
    ///
    /// Phase 2 needs this so vertex displacement shaders have enough vertices for visible
    /// carve dips on low-poly hosts. The host's 3D triangulation shape is preserved —
    /// midpoint positions/normals are linear interpolation of endpoints, no Loop-style
    /// smoothing.
    /// </summary>
    public static class SnowPatchSubdivider
    {
        public static SnowTopSurfaceExtractor.TopPatch Subdivide(
            SnowTopSurfaceExtractor.TopPatch src, int levels)
        {
            var p = src;
            for (int i = 0; i < levels; i++) p = SubdivideOnce(p);
            return p;
        }

        // Note: deliberately not `in` — the local function GetMidpoint captures src,
        // and C# disallows `in` parameter capture in nested functions. The struct
        // is just a few array refs anyway, so the by-value copy is essentially free.
        static SnowTopSurfaceExtractor.TopPatch SubdivideOnce(
            SnowTopSurfaceExtractor.TopPatch src)
        {
            if (src.verts == null || src.tris == null) return src;

            var newVerts = new List<Vector3>(src.verts);
            var newNormals = new List<Vector3>(src.vertNormals);
            var edgeMidpoints = new Dictionary<long, int>();

            int GetMidpoint(int a, int b)
            {
                int lo = a < b ? a : b;
                int hi = a < b ? b : a;
                long key = ((long)lo << 32) | (uint)hi;
                if (edgeMidpoints.TryGetValue(key, out int idx)) return idx;
                idx = newVerts.Count;
                Vector3 mp = (src.verts[a] + src.verts[b]) * 0.5f;
                Vector3 mn = src.vertNormals[a] + src.vertNormals[b];
                if (mn.sqrMagnitude > 1e-10f) mn.Normalize();
                else mn = src.vertNormals[a]; // degenerate fallback
                newVerts.Add(mp);
                newNormals.Add(mn);
                edgeMidpoints[key] = idx;
                return idx;
            }

            var newTris = new List<int>(src.tris.Length * 4);
            for (int t = 0; t < src.tris.Length; t += 3)
            {
                int a = src.tris[t], b = src.tris[t + 1], c = src.tris[t + 2];
                int mab = GetMidpoint(a, b);
                int mbc = GetMidpoint(b, c);
                int mca = GetMidpoint(c, a);
                // 4 CCW sub-triangles. Order matches: corner pieces + center.
                newTris.Add(a);   newTris.Add(mab); newTris.Add(mca);
                newTris.Add(mab); newTris.Add(b);   newTris.Add(mbc);
                newTris.Add(mca); newTris.Add(mbc); newTris.Add(c);
                newTris.Add(mab); newTris.Add(mbc); newTris.Add(mca);
            }

            // Each boundary edge (a → b) splits into two boundary edges (a → mab) + (mab → b).
            // Direction preserved so downstream "interior on the left" assumption still holds.
            int beLen = src.boundaryEdges != null ? src.boundaryEdges.Length : 0;
            var newBoundary = new List<int>(beLen * 2);
            for (int e = 0; e < beLen; e += 2)
            {
                int a = src.boundaryEdges[e];
                int b = src.boundaryEdges[e + 1];
                int mab = GetMidpoint(a, b);
                newBoundary.Add(a);   newBoundary.Add(mab);
                newBoundary.Add(mab); newBoundary.Add(b);
            }

            return new SnowTopSurfaceExtractor.TopPatch
            {
                verts = newVerts.ToArray(),
                vertNormals = newNormals.ToArray(),
                tris = newTris.ToArray(),
                boundaryEdges = newBoundary.ToArray(),
            };
        }
    }
}
