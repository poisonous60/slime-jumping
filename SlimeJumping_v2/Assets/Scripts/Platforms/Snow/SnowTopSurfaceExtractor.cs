using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// Extracts the upward-facing top surface of a host mesh as one or more connected
    /// patches. Each patch is the actual host triangulation filtered by face-normal slope —
    /// not a 2D ribbon — so the snow output covers the whole visual top including any
    /// curvature/undulation in 3D, not just a Z=0 cross-section.
    ///
    /// Position-quantized vertex welding handles flat-shaded duplicate verts. Connected-
    /// component split via union-find groups disjoint top regions into separate patches.
    /// Boundary edges are recorded as directed (a → b) pairs so the side-strip quad can
    /// face outward without orientation guessing.
    ///
    /// Optional slope-cut detection: when detectSlopeCut=true, each boundary edge is
    /// classified as either mesh-edge (the host actually ends) or slope-cut (the host
    /// continues past the edge but the next triangle exceeds maxSnowSlopeDeg). The
    /// per-vertex flag isSlopeCutAdjacent[v] is true when v is an endpoint of any
    /// slope-cut boundary edge. SnowField uses it to apply a separate taper depth at
    /// slope-cut endings without affecting mesh-edge boundaries.
    /// </summary>
    public static class SnowTopSurfaceExtractor
    {
        public struct TopPatch
        {
            public Vector3[] verts;          // welded patch vertices in mesh-local space
            public Vector3[] vertNormals;    // per-vertex normal (averaged across kept tris)
            public int[]     tris;           // CCW triangles, indices into verts
            public int[]     boundaryEdges;  // directed boundary, flat array: [a0, b0, a1, b1, ...]
            // Optional slope-cut classification — populated when Extract was called with
            // detectSlopeCut=true. true at v iff v is an endpoint of at least one slope-cut
            // boundary edge (one whose reverse direction has a rejected steep tri in the
            // host mesh). null when detection was disabled.
            public bool[]    isSlopeCutAdjacent;
        }

        public static List<TopPatch> Extract(Mesh mesh, float maxSnowSlopeDeg)
            => Extract(mesh, maxSnowSlopeDeg, detectSlopeCut: false);

        public static List<TopPatch> Extract(Mesh mesh, float maxSnowSlopeDeg, bool detectSlopeCut)
        {
            var result = new List<TopPatch>();
            if (mesh == null) return result;

            var meshVerts = mesh.vertices;
            var meshTris  = mesh.triangles;
            if (meshVerts.Length < 3 || meshTris.Length < 3) return result;

            float cosMax = Mathf.Cos(Mathf.Clamp(maxSnowSlopeDeg, 0f, 89.99f) * Mathf.Deg2Rad);

            // Position-weld every mesh vertex to a canonical index. Required so flat-shaded
            // duplicate vertices collapse and adjacent triangles share verts in our patch.
            const float quant = 1e-4f;
            var keyToCanonical = new Dictionary<long, int>();
            var canonicalVerts = new List<Vector3>();
            int Canon(Vector3 v)
            {
                long kx = (long)Mathf.RoundToInt(v.x / quant);
                long ky = (long)Mathf.RoundToInt(v.y / quant);
                long kz = (long)Mathf.RoundToInt(v.z / quant);
                long k = ((kx & 0x1FFFFFL) << 42) ^ ((ky & 0x1FFFFFL) << 21) ^ (kz & 0x1FFFFFL);
                if (!keyToCanonical.TryGetValue(k, out int idx))
                {
                    idx = canonicalVerts.Count;
                    keyToCanonical[k] = idx;
                    canonicalVerts.Add(v);
                }
                return idx;
            }

            // Filter triangles: keep those whose face normal points up enough.
            // detectSlopeCut also retains rejected-tri canonical edges so later we can
            // classify each patch boundary edge as mesh-edge vs slope-cut.
            var keptCanonTris = new List<int>(); // packed [a, b, c, a, b, c, ...]
            var keptFaceNormals = new List<Vector3>();
            HashSet<long> rejectedDirEdgeSet = detectSlopeCut ? new HashSet<long>() : null;
            for (int t = 0; t < meshTris.Length; t += 3)
            {
                Vector3 a = meshVerts[meshTris[t]];
                Vector3 b = meshVerts[meshTris[t + 1]];
                Vector3 c = meshVerts[meshTris[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                int ca = Canon(a), cb = Canon(b), cc = Canon(c);
                if (ca == cb || cb == cc || ca == cc) continue; // degenerate after weld
                if (n.y < cosMax)
                {
                    if (rejectedDirEdgeSet != null)
                    {
                        rejectedDirEdgeSet.Add(DirKey(ca, cb));
                        rejectedDirEdgeSet.Add(DirKey(cb, cc));
                        rejectedDirEdgeSet.Add(DirKey(cc, ca));
                    }
                    continue;
                }
                keptCanonTris.Add(ca);
                keptCanonTris.Add(cb);
                keptCanonTris.Add(cc);
                keptFaceNormals.Add(n);
            }

            int V = canonicalVerts.Count;
            if (keptCanonTris.Count == 0 || V == 0) return result;

            // Per-vertex averaged normal — only over KEPT triangles, so it represents the
            // top-surface orientation, not the full mesh.
            var vertNormals = new Vector3[V];
            for (int t = 0; t < keptCanonTris.Count; t += 3)
            {
                Vector3 fn = keptFaceNormals[t / 3];
                vertNormals[keptCanonTris[t]]     += fn;
                vertNormals[keptCanonTris[t + 1]] += fn;
                vertNormals[keptCanonTris[t + 2]] += fn;
            }
            for (int i = 0; i < V; i++)
            {
                if (vertNormals[i].sqrMagnitude > 1e-10f) vertNormals[i].Normalize();
                else vertNormals[i] = Vector3.up;
            }

            // Connected-component union-find on canonical verts via shared triangle edges.
            var parent = new int[V];
            for (int i = 0; i < V; i++) parent[i] = i;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            for (int t = 0; t < keptCanonTris.Count; t += 3)
            {
                Union(keptCanonTris[t],     keptCanonTris[t + 1]);
                Union(keptCanonTris[t + 1], keptCanonTris[t + 2]);
            }

            // Group canonical triangle indices by component root.
            var compTris = new Dictionary<int, List<int>>();
            for (int t = 0; t < keptCanonTris.Count; t += 3)
            {
                int root = Find(keptCanonTris[t]);
                if (!compTris.TryGetValue(root, out var list))
                {
                    list = new List<int>();
                    compTris[root] = list;
                }
                list.Add(t);
            }

            // Build a TopPatch per component: re-index verts to local space, gather tris,
            // detect directed boundary edges (a→b appears but b→a doesn't).
            foreach (var kv in compTris)
            {
                var triStarts = kv.Value;

                var localOf = new Dictionary<int, int>();
                var compVerts = new List<Vector3>();
                var compNorms = new List<Vector3>();
                var compTrisLocal = new List<int>(triStarts.Count * 3);
                int LocalOf(int globalIdx)
                {
                    if (!localOf.TryGetValue(globalIdx, out int loc))
                    {
                        loc = compVerts.Count;
                        localOf[globalIdx] = loc;
                        compVerts.Add(canonicalVerts[globalIdx]);
                        compNorms.Add(vertNormals[globalIdx]);
                    }
                    return loc;
                }
                foreach (int t in triStarts)
                {
                    compTrisLocal.Add(LocalOf(keptCanonTris[t]));
                    compTrisLocal.Add(LocalOf(keptCanonTris[t + 1]));
                    compTrisLocal.Add(LocalOf(keptCanonTris[t + 2]));
                }

                // Directed edges via a HashSet keyed on (a,b) packed long.
                var dirSet = new HashSet<long>();
                for (int i = 0; i < compTrisLocal.Count; i += 3)
                {
                    int a = compTrisLocal[i], b = compTrisLocal[i + 1], c = compTrisLocal[i + 2];
                    dirSet.Add(DirKey(a, b));
                    dirSet.Add(DirKey(b, c));
                    dirSet.Add(DirKey(c, a));
                }
                var bedges = new List<int>();
                bool[] isSlopeCutAdj = null;
                int[] localToCanon = null;
                if (detectSlopeCut)
                {
                    localToCanon = new int[compVerts.Count];
                    foreach (var pair in localOf) localToCanon[pair.Value] = pair.Key;
                    isSlopeCutAdj = new bool[compVerts.Count];
                }
                bool anySlopeCut = false;
                foreach (long e in dirSet)
                {
                    int a = (int)(e >> 32);
                    int b = (int)(e & 0xFFFFFFFFL);
                    long rev = DirKey(b, a);
                    if (!dirSet.Contains(rev))
                    {
                        bedges.Add(a);
                        bedges.Add(b);
                        if (detectSlopeCut)
                        {
                            // Slope-cut iff the host mesh has a rejected tri across this
                            // edge — i.e., the reverse direction in canonical indices is
                            // covered by some rejected triangle's directed edge set.
                            int canA = localToCanon[a], canB = localToCanon[b];
                            if (rejectedDirEdgeSet.Contains(DirKey(canB, canA)))
                            {
                                isSlopeCutAdj[a] = true;
                                isSlopeCutAdj[b] = true;
                                anySlopeCut = true;
                            }
                        }
                    }
                }

                result.Add(new TopPatch
                {
                    verts = compVerts.ToArray(),
                    vertNormals = compNorms.ToArray(),
                    tris = compTrisLocal.ToArray(),
                    boundaryEdges = bedges.ToArray(),
                    isSlopeCutAdjacent = anySlopeCut ? isSlopeCutAdj : null,
                });
            }

            return result;
        }

        static long DirKey(int a, int b) => ((long)a << 32) | (uint)b;
    }
}
