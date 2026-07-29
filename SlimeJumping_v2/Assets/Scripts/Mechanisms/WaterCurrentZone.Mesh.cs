using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    public partial class WaterCurrentZone
    {
        // 5 verts per cross-section (L, L', C, R', R) form a half-arc bulging toward the camera;
        // the Z depth is what keeps it from reading as a flat sticker off-axis.
        const int CrossSectionVerts = 5;
        // 4 quads per ribbon segment × 2 tris per quad = 8 tris per segment.
        const int TrisPerSegment = 8;

        // cos(π/4) — Z multiplier for the L'/R' (mid-arc) cross-section verts.
        const float CosQuarter = 0.70710678f;

        // k = 0..4 (L, L', C, R', R): UOffset = offset along normalLR in units of (ribbonWidth/2);
        // ZMul scales (-tubeDepth) for the arc; DispSign selects the L/R sim channel (0 = none);
        // DispMag weights it (1 at silhouette, 0 at apex); VertU = UV.x; VertA = vertex alpha
        // driving the shader edge fade.
        static readonly float[] UOffset  = { -1f,        -0.5f,       0f, +0.5f,       +1f };
        static readonly float[] ZMul     = {  0f,         CosQuarter, 1f,  CosQuarter,  0f };
        static readonly int[]   DispSign = { -1,         -1,          0,  +1,          +1 };
        static readonly float[] DispMag  = {  1f,         0.5f,       0f,  0.5f,        1f };
        static readonly float[] VertU    = {  0f,         0.25f,      0.5f, 0.75f,      1f };
        static readonly float[] VertA    = {  1f,         0.15f,      0f,   0.15f,      1f };

        Mesh ribbonMesh;
        Vector3[] meshVerts;
        Vector2[] meshUVs;
        Color[]   meshColors;
        int[]     meshTris;

        void BuildRibbonMesh()
        {
            if (points == null || points.Length < 2)
            {
                if (meshFilter != null) meshFilter.sharedMesh = null;
                return;
            }

            int m = points.Length;
            int vertCount = m * CrossSectionVerts;
            int triCount  = TrisPerSegment * (m - 1);

            if (meshVerts  == null || meshVerts.Length  != vertCount) meshVerts  = new Vector3[vertCount];
            if (meshUVs    == null || meshUVs.Length    != vertCount) meshUVs    = new Vector2[vertCount];
            if (meshColors == null || meshColors.Length != vertCount) meshColors = new Color[vertCount];
            if (meshTris   == null || meshTris.Length   != triCount * 3) meshTris = new int[triCount * 3];

            FillStaticAttribs();
            FillTriangles();
            FillVertPositions();

            if (ribbonMesh == null)
            {
                // After domain reload (entering/exiting play mode) the C# field resets to null
                // but the FlowMesh child's MeshFilter still references the previously-built Mesh.
                // Recover that reference instead of leaking it and allocating a fresh one.
                if (meshFilter != null && meshFilter.sharedMesh != null
                    && meshFilter.sharedMesh.name == RibbonMeshName)
                {
                    ribbonMesh = meshFilter.sharedMesh;
                }
                else
                {
                    ribbonMesh = new Mesh { name = RibbonMeshName };
                    ribbonMesh.MarkDynamic();
                }
            }
            ribbonMesh.Clear();
            ribbonMesh.vertices  = meshVerts;
            ribbonMesh.triangles = meshTris;
            ribbonMesh.uv        = meshUVs;
            ribbonMesh.colors    = meshColors;
            ribbonMesh.RecalculateBounds();

            if (meshFilter != null) meshFilter.sharedMesh = ribbonMesh;
        }

        /// <summary>Positions only — UVs, colors and triangles are written once in BuildRibbonMesh.</summary>
        void UpdateMeshVerts()
        {
            if (ribbonMesh == null || points == null || points.Length < 2) return;

            int m = points.Length;
            int expectedVerts = m * CrossSectionVerts;
            if (meshVerts == null || meshVerts.Length != expectedVerts)
            {
                // Sample count changed since last BuildRibbonMesh — full rebuild needed.
                BuildRibbonMesh();
                return;
            }

            FillVertPositions();
            ribbonMesh.vertices = meshVerts;
            ribbonMesh.RecalculateBounds();
        }

        void FillVertPositions()
        {
            int m = points.Length;
            float halfW = ribbonWidth * 0.5f;
            for (int i = 0; i < m; i++)
            {
                var pt = points[i];
                int baseIdx = i * CrossSectionVerts;
                for (int k = 0; k < CrossSectionVerts; k++)
                {
                    float baseOffset = UOffset[k] * halfW;
                    // Sim displacement: positive disp = outward (away from centerline). For L
                    // side (DispSign = -1) the outward direction is -normalLR, so the
                    // contribution along +normalLR is -disp. R side adds +disp.
                    float disp = DispSign[k] < 0 ? pt.dispL
                              : DispSign[k] > 0 ? pt.dispR
                                                : 0f;
                    float dispOffset = DispSign[k] * disp * DispMag[k];
                    float horizontal = baseOffset + dispOffset;

                    Vector2 xy = pt.restCenter + pt.normalLR * horizontal;
                    float z = -tubeDepth * ZMul[k];
                    meshVerts[baseIdx + k] = new Vector3(xy.x, xy.y, z);
                }
            }
        }

        void FillStaticAttribs()
        {
            int m = points.Length;
            for (int i = 0; i < m; i++)
            {
                int baseIdx = i * CrossSectionVerts;
                float v = points[i].arcLength;
                for (int k = 0; k < CrossSectionVerts; k++)
                {
                    meshUVs[baseIdx + k] = new Vector2(VertU[k], v);
                    // Shader reads only color.a (vertex.color.a → IN.edgeT). RGB is preserved
                    // for any future shader variant that wants a center→edge tint gradient.
                    Color rgb = Color.Lerp(centerColor, edgeColor, VertA[k]);
                    rgb.a = VertA[k];
                    meshColors[baseIdx + k] = rgb;
                }
            }
        }

        void FillTriangles()
        {
            int m = points.Length;
            int t = 0;
            for (int i = 0; i < m - 1; i++)
            {
                int rowI = i * CrossSectionVerts;
                int rowN = (i + 1) * CrossSectionVerts;
                // 4 quads (between cross-section verts k and k+1).
                for (int k = 0; k < CrossSectionVerts - 1; k++)
                {
                    int va = rowI + k;       // upstream, this side
                    int vb = rowI + k + 1;   // upstream, right neighbor
                    int vc = rowN + k;       // downstream, this side
                    int vd = rowN + k + 1;   // downstream, right neighbor

                    // Winding puts the front-face normal at -Z (toward camera) for a top→bottom
                    // centerline; Cull Off is deliberate — the back face sells the arc off-axis.
                    meshTris[t++] = va; meshTris[t++] = vb; meshTris[t++] = vc;
                    meshTris[t++] = vb; meshTris[t++] = vd; meshTris[t++] = vc;
                }
            }
        }

        // ---- Catmull-Rom (verbatim from WaterZone_v2.Mesh.cs:177-187) ----

        static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                2f * p1
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }
    }
}
