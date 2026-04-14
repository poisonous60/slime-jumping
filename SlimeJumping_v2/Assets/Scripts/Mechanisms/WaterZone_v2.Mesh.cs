using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    public partial class WaterZone_v2
    {
        [Header("Visual Smoothing")]
        [Tooltip("Catmull-Rom samples per outline segment. 1 = straight polylines. " +
                 "4-6 = smooth curves at cheap cost. Affects mesh and outline line both.")]
        [Min(1)] [SerializeField] int curveSubdivisions = 5;

        [Header("Visual Toggles")]
        [Tooltip("Draw the filled water body mesh.")]
        [SerializeField] bool showMesh = true;
        [Tooltip("Draw the outline line. Disable for a fill-only look.")]
        [SerializeField] bool showOutline = true;

        Mesh         mesh;
        MeshRenderer cachedMeshRenderer;
        Vector3[]    meshVerts;
        int[]        meshTris;
        Color[]      meshColors;

        void BuildMesh()
        {
            var mf = GetComponent<MeshFilter>();
            if (cachedMeshRenderer == null) cachedMeshRenderer = GetComponent<MeshRenderer>();
            if (mesh == null)
            {
                mesh = new Mesh { name = "WaterZoneV2_FanMesh" };
                mesh.MarkDynamic();
                mf.sharedMesh = mesh;
            }
            else
            {
                mesh.Clear();
            }

            AllocateMeshBuffers();
            UpdateMeshVerts();
            mesh.triangles = meshTris;
            mesh.colors    = meshColors;
            mesh.RecalculateNormals();
        }

        void AllocateMeshBuffers()
        {
            int perim = PerimeterSampleCount();
            meshVerts  = new Vector3[perim + 1];
            meshTris   = new int[perim * 3];
            meshColors = new Color[perim + 1];

            // Winding: for Unity's default 2D camera (at -Z looking +Z),
            // the visible front face must have its normal pointing -Z.
            // center -> next -> current gives -Z normal for CCW-ordered outline points.
            for (int i = 0; i < perim; i++)
            {
                meshTris[i * 3]     = 0;
                meshTris[i * 3 + 1] = ((i + 1) % perim) + 1;
                meshTris[i * 3 + 2] = i + 1;
            }
            // meshColors[0] = center (deep, A=0 = no rim).
            // meshColors[1..perim] = edge (shallow, A=1 = full rim).
            // The Water_v2 shader uses color.rgb as depth tint and color.a as rim factor;
            // fan-mesh interpolation produces a center→edge gradient for free.
            meshColors[0] = new Color(centerColor.r, centerColor.g, centerColor.b, 0f);
            for (int i = 1; i < perim + 1; i++)
                meshColors[i] = new Color(edgeColor.r, edgeColor.g, edgeColor.b, 1f);
        }

        [ContextMenu("Rebuild Mesh")]
        void RebuildMeshContextMenu()
        {
            if (points == null || points.Length < 3) BuildShape();
            BuildMesh();
        }

        int PerimeterSampleCount()
        {
            int n = points == null ? 0 : points.Length;
            int subdiv = Mathf.Max(1, curveSubdivisions);
            return n * subdiv;
        }

        void UpdateMeshVerts()
        {
            if (mesh == null || points == null || points.Length < 3) return;

            if (cachedMeshRenderer != null && cachedMeshRenderer.enabled != showMesh)
                cachedMeshRenderer.enabled = showMesh;
            if (!showMesh) return;

            int perim = PerimeterSampleCount();
            if (meshVerts == null || meshVerts.Length != perim + 1)
            {
                AllocateMeshBuffers();
                mesh.Clear();
                mesh.vertices = meshVerts;      // must set verts before triangles on fresh mesh
                mesh.triangles = meshTris;
                mesh.colors = meshColors;
            }

            meshVerts[0] = new Vector3(centerLocal.x, centerLocal.y, 0f);
            FillPerimeterSamples(meshVerts, writeOffset: 1, z: 0f);

            mesh.vertices = meshVerts;
            mesh.RecalculateBounds();
        }

        void UpdateOutlineLine()
        {
            if (outlineLine == null || points == null || points.Length < 3) return;

            if (outlineLine.enabled != showOutline) outlineLine.enabled = showOutline;
            if (!showOutline) return;

            int perim = PerimeterSampleCount();
            outlineLine.useWorldSpace = false;
            outlineLine.loop          = true;
            if (outlineLine.positionCount != perim) outlineLine.positionCount = perim;

            // LineRenderer.SetPositions needs a Vector3[]. Reuse meshVerts[1..] by writing
            // directly into outlineLine via per-index SetPosition — avoids another buffer.
            int n = points.Length;
            int subdiv = Mathf.Max(1, curveSubdivisions);
            int w = 0;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = LocalPos((i - 1 + n) % n);
                Vector2 p1 = LocalPos(i);
                Vector2 p2 = LocalPos((i + 1) % n);
                Vector2 p3 = LocalPos((i + 2) % n);
                for (int s = 0; s < subdiv; s++)
                {
                    float t = s / (float)subdiv;
                    Vector2 v = CatmullRom(p0, p1, p2, p3, t);
                    outlineLine.SetPosition(w++, new Vector3(v.x, v.y, 0f));
                }
            }
        }

        /// <summary>
        /// Writes n*subdiv interpolated positions around the closed loop into <paramref name="buf"/>
        /// starting at <paramref name="writeOffset"/>. Uses Catmull-Rom with wrap-around neighbors.
        /// </summary>
        void FillPerimeterSamples(Vector3[] buf, int writeOffset, float z)
        {
            int n = points.Length;
            int subdiv = Mathf.Max(1, curveSubdivisions);
            int w = writeOffset;
            for (int i = 0; i < n; i++)
            {
                Vector2 p0 = LocalPos((i - 1 + n) % n);
                Vector2 p1 = LocalPos(i);
                Vector2 p2 = LocalPos((i + 1) % n);
                Vector2 p3 = LocalPos((i + 2) % n);
                for (int s = 0; s < subdiv; s++)
                {
                    float t = s / (float)subdiv;
                    Vector2 v = CatmullRom(p0, p1, p2, p3, t);
                    buf[w++] = new Vector3(v.x, v.y, z);
                }
            }
        }

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
