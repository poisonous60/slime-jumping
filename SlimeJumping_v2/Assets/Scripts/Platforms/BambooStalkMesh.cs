using UnityEngine;

namespace SlimeJumping.Platforms
{
    /// <summary>
    /// Procedural straight cylinder for bamboo stalks. Base at y=0, tip at y=length,
    /// positionOS in world-equivalent units so SlimeJumping/BambooBend can apply its
    /// vertex bend in place without knowing any Transform scale.
    ///
    /// Used by BambooPlatformV3. BambooPlatform (v1) still carries its own private copy
    /// of this function — v1 is deliberately left untouched, so the duplication stands
    /// until someone has a reason to edit v1 anyway.
    /// </summary>
    public static class BambooStalkMesh
    {
        public static Mesh Build(int radialSegments, int heightSegments, float radius, float length)
        {
            radialSegments = Mathf.Max(3, radialSegments);
            heightSegments = Mathf.Max(1, heightSegments);

            int ringVerts = radialSegments + 1; // duplicate seam vert for clean UVs
            int vertCount = ringVerts * (heightSegments + 1);
            var verts = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];

            for (int j = 0; j <= heightSegments; j++)
            {
                float v = (float)j / heightSegments;
                float y = v * length;
                for (int i = 0; i < ringVerts; i++)
                {
                    float u = (float)i / radialSegments;
                    float angle = u * Mathf.PI * 2f;
                    float cx = Mathf.Cos(angle);
                    float cz = Mathf.Sin(angle);
                    int idx = j * ringVerts + i;
                    verts[idx] = new Vector3(cx * radius, y, cz * radius);
                    normals[idx] = new Vector3(cx, 0f, cz);
                    uvs[idx] = new Vector2(u, v);
                }
            }

            int triCount = radialSegments * heightSegments * 6;
            var tris = new int[triCount];
            int ti = 0;
            for (int j = 0; j < heightSegments; j++)
            {
                for (int i = 0; i < radialSegments; i++)
                {
                    int a = j * ringVerts + i;
                    int b = a + 1;
                    int c = a + ringVerts;
                    int d = c + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
            }

            var mesh = new Mesh { name = "BambooCylinder" };
            mesh.indexFormat = vertCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            // Generous bounds: the bent visual extends laterally up to ~length in the worst case.
            mesh.bounds = new Bounds(new Vector3(0f, length * 0.5f, 0f),
                                     new Vector3(length * 2f, length * 1.2f, length * 2f));
            return mesh;
        }
    }
}
