using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// Static snow mesh; the shader displaces it at runtime via _SnowHeightBuf.
    /// Shader contract: verts un-displaced, tangent.xyz = displacement dir (zero = base
    /// vert glued to the host), uv2 = (raw vertex index, heightRest).
    /// Flat shading via per-face corner re-emit, face normals taken at REST positions —
    /// re-normalizing per frame would cost more than the buffer upload, carve tilt is invisible.
    /// </summary>
    public static class SnowMeshBuilder
    {
        public struct BuildParams
        {
            public string meshName;
        }

        // patch is by value, not `in`: C# forbids capturing an in-parameter in a local function (AddVert).
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
                uvs2.Add(new Vector2(origIdx, heightRest[origIdx]));
            }

            // Both winding orders per triangle: double-sided without needing Cull Off in the material.
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

            // Side skirt per boundary edge: base verts (tangent = 0) stay on the host,
            // top verts ride heightCurrent. Collapses harmlessly when taper zeroes the boundary.
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

            // Bounds cover un-displaced verts only — pad by maxRest or frustum culling clips the
            // displaced shell at grazing angles. Expand() adds amount/2 per side, hence 2*maxRest.
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
