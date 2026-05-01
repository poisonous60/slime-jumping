using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// Builds the static snow mesh whose vertices the snow shader will displace at
    /// runtime via _SnowHeightBuf (StructuredBuffer&lt;float&gt;). The mesh itself is
    /// un-displaced — vertex positions equal patch.verts[i]; the per-vertex displacement
    /// direction lives in tangent.xyz (typically patch.vertNormals[i] for surface verts,
    /// zero for "base" boundary verts that should stay glued to the host); uv2.x carries
    /// the raw vertex index (cast to uint in the shader to index _SnowHeightBuf),
    /// uv2.y carries heightRest for that vertex. Phase 3 fragment shader reads both
    /// to compute the carve mask 1 - heightCurrent/heightRest.
    ///
    /// Per-tri flat shading is preserved by re-emitting each triangle's three corners
    /// per face with that face's normal, computed at REST positions (verts +
    /// vertNormals * heightRest). Carve-time vertex displacement does tilt the actual
    /// face slightly, but for typical dip depths the lighting deviation is invisible
    /// — and updating face normals every frame would cost an order of magnitude more
    /// than the upload itself.
    ///
    /// Top face: each patch triangle emitted twice (front + reversed back) so the
    /// material can render double-sided without Cull Off. Side strip: per directed
    /// boundary edge a→b, emits a 4-corner skirt where the two "base" verts have
    /// tangent.xyz = 0 (anchored to the host surface) and the two "top" verts
    /// displace fully along the patch vertex normal.
    /// </summary>
    public static class SnowMeshBuilder
    {
        public struct BuildParams
        {
            public string meshName;
        }

        // patch is passed by value (not `in`) because the local AddVert function captures
        // it; C# disallows in-parameter capture in nested functions. TopPatch is just
        // a handful of array references, so the copy is essentially free.
        public static Mesh Build(SnowTopSurfaceExtractor.TopPatch patch, float[] heightRest, BuildParams p)
        {
            var mesh = new Mesh { name = p.meshName ?? "SnowPatch" };
            int V = patch.verts != null ? patch.verts.Length : 0;
            if (V < 3 || patch.tris == null || patch.tris.Length < 3) return mesh;
            if (heightRest == null || heightRest.Length != V) return mesh;

            // Rest-displaced positions (only used for face-normal computation).
            var restTop = new Vector3[V];
            float maxRest = 0f;
            for (int i = 0; i < V; i++)
            {
                float h = Mathf.Max(0f, heightRest[i]);
                if (h > maxRest) maxRest = h;
                restTop[i] = patch.verts[i] + patch.vertNormals[i] * h;
            }

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tans  = new List<Vector4>();
            var uvs   = new List<Vector2>();
            var uvs2  = new List<Vector2>();
            var tris  = new List<int>();

            Vector4 zeroTangent = new Vector4(0f, 0f, 0f, 1f);

            void AddVert(int origIdx, Vector3 normal, Vector4 tangent)
            {
                Vector3 pos = patch.verts[origIdx];
                verts.Add(pos);
                norms.Add(normal);
                tans.Add(tangent);
                uvs.Add(new Vector2(pos.x, pos.y));
                // uv2.x carries the raw vertex index (the shader casts to uint and
                // indexes _SnowHeightBuf). uv2.y carries heightRest used by the
                // fragment carve mask.
                uvs2.Add(new Vector2(origIdx, heightRest[origIdx]));
            }

            // Top face: per patch triangle, emit both winding orders so a single material
            // renders front + back without needing Cull Off.
            for (int t = 0; t < patch.tris.Length; t += 3)
            {
                int ia = patch.tris[t], ib = patch.tris[t + 1], ic = patch.tris[t + 2];
                Vector3 ra = restTop[ia], rb = restTop[ib], rc = restTop[ic];
                Vector3 fn = Vector3.Cross(rb - ra, rc - ra);
                if (fn.sqrMagnitude < 1e-12f) continue;
                fn.Normalize();

                var tA = (Vector4)patch.vertNormals[ia]; tA.w = 1f;
                var tB = (Vector4)patch.vertNormals[ib]; tB.w = 1f;
                var tC = (Vector4)patch.vertNormals[ic]; tC.w = 1f;

                int v0 = verts.Count;
                AddVert(ia, fn, tA);
                AddVert(ib, fn, tB);
                AddVert(ic, fn, tC);
                tris.Add(v0); tris.Add(v0 + 1); tris.Add(v0 + 2);

                Vector3 bn = -fn;
                int v3 = verts.Count;
                AddVert(ia, bn, tA);
                AddVert(ic, bn, tC);
                AddVert(ib, bn, tB);
                tris.Add(v3); tris.Add(v3 + 1); tris.Add(v3 + 2);
            }

            // Side strip: each directed boundary edge a→b spawns two triangles. Base
            // verts (tangent = 0) stay on the host surface; top verts (tangent =
            // vertNormal) ride heightCurrent. Skirt is visible only where boundary
            // height > 0; collapses harmlessly when taper drives boundary to zero.
            int beLen = patch.boundaryEdges != null ? patch.boundaryEdges.Length : 0;
            for (int e = 0; e < beLen; e += 2)
            {
                int a = patch.boundaryEdges[e];
                int b = patch.boundaryEdges[e + 1];
                Vector3 A = patch.verts[a];
                Vector3 B = patch.verts[b];
                Vector3 C = restTop[b];
                Vector3 D = restTop[a];

                var topTanA = (Vector4)patch.vertNormals[a]; topTanA.w = 1f;
                var topTanB = (Vector4)patch.vertNormals[b]; topTanB.w = 1f;

                Vector3 n1 = Vector3.Cross(B - A, C - A);
                if (n1.sqrMagnitude < 1e-12f) n1 = Vector3.up; else n1.Normalize();

                int q0 = verts.Count;
                AddVert(a, n1, zeroTangent);  // base at a
                AddVert(b, n1, zeroTangent);  // base at b
                AddVert(b, n1, topTanB);      // top at b
                tris.Add(q0); tris.Add(q0 + 1); tris.Add(q0 + 2);

                Vector3 n1b = -n1;
                int q0b = verts.Count;
                AddVert(a, n1b, zeroTangent);
                AddVert(b, n1b, topTanB);
                AddVert(b, n1b, zeroTangent);
                tris.Add(q0b); tris.Add(q0b + 1); tris.Add(q0b + 2);

                Vector3 n2 = Vector3.Cross(C - A, D - A);
                if (n2.sqrMagnitude < 1e-12f) n2 = n1; else n2.Normalize();

                int q3 = verts.Count;
                AddVert(a, n2, zeroTangent);  // base at a
                AddVert(b, n2, topTanB);      // top at b
                AddVert(a, n2, topTanA);      // top at a
                tris.Add(q3); tris.Add(q3 + 1); tris.Add(q3 + 2);

                Vector3 n2b = -n2;
                int q3b = verts.Count;
                AddVert(a, n2b, zeroTangent);
                AddVert(a, n2b, topTanA);
                AddVert(b, n2b, topTanB);
                tris.Add(q3b); tris.Add(q3b + 1); tris.Add(q3b + 2);
            }

            if (verts.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTangents(tans);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uvs2);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            // Bounds account for un-displaced positions only. Pad by maxRest along each
            // axis so frustum culling doesn't clip the displaced shell on grazing camera
            // angles. (Bounds.Expand adds amount/2 to each side, so 2*maxRest yields
            // maxRest padding on every axis.)
            if (maxRest > 0f)
            {
                var b = mesh.bounds;
                b.Expand(2f * maxRest);
                mesh.bounds = b;
            }
            return mesh;
        }
    }
}
