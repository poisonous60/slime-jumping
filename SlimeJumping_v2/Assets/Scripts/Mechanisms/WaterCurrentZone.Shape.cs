using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    public partial class WaterCurrentZone
    {
        struct RibbonPoint
        {
            public Vector2 restCenter;   // sampled centerline point (local 2D)
            public Vector2 normalLR;     // unit perpendicular, +n = right of flow direction
            public float   arcLength;    // cumulative distance from points[0]
            public float   dispL, velL;  // left-channel sim state (positive = outward, away from center)
            public float   dispR, velR;  // right-channel sim state
        }

        [Header("Centerline (local space)")]
        [Tooltip("Defines the curve the ribbon follows. 2+ points required. Edit via inspector or " +
                 "scene-view yellow drag handles. Shift-click an edge to insert a point; " +
                 "Ctrl-click a point to delete.")]
        [SerializeField] List<Vector2> centerlineLocal = new List<Vector2>();

        [Tooltip("Width of the ribbon perpendicular to the centerline (local units).")]
        [Min(0.05f)] [SerializeField] float ribbonWidth = 0.8f;

        [Tooltip("Z extrusion of the ribbon's center toward camera (local units). " +
                 "0 = flat plane (reads as a sticker from any off-axis camera angle). " +
                 "0.2 = visible cross-section depth. Crests in Phase B further push the apex " +
                 "forward; keep _CrestAmplitude < tubeDepth to avoid clipping past the GameObject's " +
                 "Z plane.")]
        [Min(0f)] [SerializeField] float tubeDepth = 0.2f;

        [Tooltip("Catmull-Rom samples per centerline segment. 1 = polyline; 4-6 = smooth curve.")]
        [Min(1)] [SerializeField] int curveSubdivisions = 5;

        // Subdivided samples derived from centerlineLocal. Length = (N-1)*curveSubdivisions + 1.
        RibbonPoint[] points;
        float totalArcLength;

        // Set after migration runs once. Stops a re-migration from clobbering an authored
        // centerline if a designer later clears it intentionally and the polygon path is still
        // present.
        [SerializeField, HideInInspector] bool migrated;

        public int CenterlinePointCount => centerlineLocal == null ? 0 : centerlineLocal.Count;
        public Vector2 GetCenterlinePointLocal(int i) => centerlineLocal[i];
        public void SetCenterlinePointLocal(int i, Vector2 v)
        {
            centerlineLocal[i] = v;
            migrated = true;
        }
        public void InsertCenterlinePointLocal(int index, Vector2 v)
        {
            centerlineLocal.Insert(index, v);
            migrated = true;
        }
        public void RemoveCenterlinePointAt(int i)
        {
            centerlineLocal.RemoveAt(i);
            migrated = true;
        }

        void BuildShape()
        {
            int n = centerlineLocal == null ? 0 : centerlineLocal.Count;
            if (n < 2)
            {
                points = null;
                totalArcLength = 0f;
                return;
            }

            int subdiv = Mathf.Max(1, curveSubdivisions);
            int sampleCount = (n - 1) * subdiv + 1;
            if (points == null || points.Length != sampleCount)
                points = new RibbonPoint[sampleCount];

            // Catmull-Rom subdivide. Open-polyline endpoint clamping: replicate boundary points
            // so endpoints sit exactly on the user's authored centerline (no overshoot).
            int w = 0;
            for (int i = 0; i < n - 1; i++)
            {
                Vector2 p0 = centerlineLocal[Mathf.Max(i - 1, 0)];
                Vector2 p1 = centerlineLocal[i];
                Vector2 p2 = centerlineLocal[i + 1];
                Vector2 p3 = centerlineLocal[Mathf.Min(i + 2, n - 1)];

                for (int s = 0; s < subdiv; s++)
                {
                    float t = s / (float)subdiv;
                    var prev = points[w];
                    points[w] = new RibbonPoint
                    {
                        restCenter = CatmullRom(p0, p1, p2, p3, t),
                        // sim state preserved across rebuilds when sample count matches
                        dispL = prev.dispL, velL = prev.velL,
                        dispR = prev.dispR, velR = prev.velR,
                    };
                    w++;
                }
            }
            // Final endpoint (t=1 of the last segment).
            {
                var prev = points[w];
                points[w] = new RibbonPoint
                {
                    restCenter = centerlineLocal[n - 1],
                    dispL = prev.dispL, velL = prev.velL,
                    dispR = prev.dispR, velR = prev.velR,
                };
            }

            // Tangent + normal per sample. Central differences interior, one-sided at endpoints.
            int m = points.Length;
            for (int i = 0; i < m; i++)
            {
                Vector2 tan;
                if (i == 0)            tan = points[1].restCenter         - points[0].restCenter;
                else if (i == m - 1)   tan = points[m - 1].restCenter     - points[m - 2].restCenter;
                else                   tan = points[i + 1].restCenter     - points[i - 1].restCenter;

                if (tan.sqrMagnitude < 1e-8f) tan = Vector2.right;
                tan.Normalize();
                // Rotate 90° CCW: gives "right of flow direction" for left-handed Unity 2D.
                // For tangent (0,-1) (flow points down), normalLR = (1,0) (right). Matches the
                // U axis convention of the previous WaterCurrentZone (line 254-255).
                points[i].normalLR = new Vector2(-tan.y, tan.x);
            }

            // Cumulative arc length for UV.V (downstream scroll axis).
            points[0].arcLength = 0f;
            for (int i = 1; i < m; i++)
                points[i].arcLength = points[i - 1].arcLength
                                    + Vector2.Distance(points[i - 1].restCenter, points[i].restCenter);
            totalArcLength = points[m - 1].arcLength;
        }

        // ---- Centerline generators (context menus) ----

        [ContextMenu("Generate Vertical Line")]
        public void GenerateVerticalLine()
        {
            if (centerlineLocal == null) centerlineLocal = new List<Vector2>();
            centerlineLocal.Clear();
            centerlineLocal.Add(new Vector2(0f, +2f));
            centerlineLocal.Add(new Vector2(0f, +1f));
            centerlineLocal.Add(new Vector2(0f,  0f));
            centerlineLocal.Add(new Vector2(0f, -1f));
            centerlineLocal.Add(new Vector2(0f, -2f));
            migrated = true;
        }

        [ContextMenu("Generate Slope (45°)")]
        public void GenerateSlope45()
        {
            if (centerlineLocal == null) centerlineLocal = new List<Vector2>();
            centerlineLocal.Clear();
            for (int i = -2; i <= 2; i++)
                centerlineLocal.Add(new Vector2(i, -i));
            migrated = true;
        }

        [ContextMenu("Clear Centerline")]
        public void ClearCenterline()
        {
            if (centerlineLocal == null) centerlineLocal = new List<Vector2>();
            centerlineLocal.Clear();
            migrated = true;
        }

        /// <summary>
        /// Build the ribbon's silhouette polygon (left outline forward + right outline reversed)
        /// and write it into the attached PolygonCollider2D. Trigger area exactly matches the
        /// visual silhouette (in XY plane). Inner cross-section vertices L'/C/R' stay out of
        /// the collider — they only affect the visual mesh's Z depth.
        /// </summary>
        [ContextMenu("Sync Collider To Ribbon Outline")]
        public void SyncColliderToRibbonOutline()
        {
            var poly = GetComponent<PolygonCollider2D>();
            if (poly == null) return;
            if (points == null || points.Length < 2) return;

            int m = points.Length;
            var path = new Vector2[m * 2];
            float halfW = ribbonWidth * 0.5f;
            for (int i = 0; i < m; i++)
                path[i] = points[i].restCenter - points[i].normalLR * halfW;
            for (int i = 0; i < m; i++)
                path[m + i] = points[m - 1 - i].restCenter + points[m - 1 - i].normalLR * halfW;

            poly.SetPath(0, path);
        }

        // ---- One-shot migration from the legacy rectangular PolygonCollider2D ----

        /// <summary>
        /// Extracts a vertical centerline + ribbonWidth from the existing PolygonCollider2D's
        /// AABB. Used to migrate scene instances authored with the old cube-prism mesh
        /// (rectangular polygon path) without forcing designers to re-author from scratch.
        /// Idempotent — sets `migrated` after running so subsequent OnValidate calls noop.
        /// </summary>
        void TryMigrateFromPolygon()
        {
            if (migrated) return;
            if (centerlineLocal != null && centerlineLocal.Count > 0) { migrated = true; return; }
            var poly = GetComponent<PolygonCollider2D>();
            if (poly == null || poly.pathCount == 0) return;
            var path = poly.GetPath(0);
            if (path == null || path.Length < 3) return;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < path.Length; i++)
            {
                Vector2 p = path[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }

            float midX = (minX + maxX) * 0.5f;
            if (centerlineLocal == null) centerlineLocal = new List<Vector2>();
            centerlineLocal.Clear();
            centerlineLocal.Add(new Vector2(midX, maxY));
            centerlineLocal.Add(new Vector2(midX, (minY + maxY) * 0.5f));
            centerlineLocal.Add(new Vector2(midX, minY));
            ribbonWidth = Mathf.Max(0.05f, maxX - minX);
            migrated = true;
        }
    }
}
