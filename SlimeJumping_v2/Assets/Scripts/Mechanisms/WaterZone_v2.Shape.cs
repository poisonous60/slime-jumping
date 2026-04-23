using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    public partial class WaterZone_v2
    {
        struct SurfacePoint
        {
            public Vector2 restPosLocal;
            public Vector2 restNormal;
            public float   displacement;
            public float   velocity;
        }

        [Header("Outline Points (local space, order defines loop)")]
        [Tooltip("Place these freely in the inspector or via the scene-view handles. " +
                 "Winding is auto-detected — normals always point outward.")]
        [SerializeField] List<Vector2> restPointsLocal = new List<Vector2>();

        SurfacePoint[] points;

        void BuildShape()
        {
            if (restPointsLocal == null || restPointsLocal.Count < 3)
            {
                Debug.LogWarning($"[WaterZone_v2:{name}] restPointsLocal has < 3 points — " +
                                 "generating fallback circle. Use the context menu to regenerate, " +
                                 "or edit points in the inspector / scene view.", this);
                GenerateCirclePoints();
            }

            int n = restPointsLocal.Count;
            points = new SurfacePoint[n];
            for (int i = 0; i < n; i++)
            {
                points[i].restPosLocal = restPointsLocal[i];
                points[i].displacement = 0f;
                points[i].velocity     = 0f;
            }

            RecomputeNormals();
            RecomputeCenter();
        }

        /// <summary>
        /// Outward normal per vertex = average of adjacent edge right-perpendiculars.
        /// Auto-flipped to point outward regardless of user winding order (CW/CCW).
        /// </summary>
        void RecomputeNormals()
        {
            if (points == null || points.Length < 3) return;
            int n = points.Length;

            // Shoelace signed area: positive = CCW, negative = CW.
            float signedArea2 = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = points[i].restPosLocal;
                Vector2 b = points[(i + 1) % n].restPosLocal;
                signedArea2 += a.x * b.y - b.x * a.y;
            }
            // For CCW, (edge.y, -edge.x) points outward. For CW, flip.
            float outwardSign = signedArea2 >= 0f ? 1f : -1f;

            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                int next = (i + 1) % n;
                Vector2 ePrev = points[i].restPosLocal    - points[prev].restPosLocal;
                Vector2 eNext = points[next].restPosLocal - points[i].restPosLocal;

                Vector2 nPrev = new Vector2(ePrev.y, -ePrev.x) * outwardSign;
                Vector2 nNext = new Vector2(eNext.y, -eNext.x) * outwardSign;

                Vector2 sum = nPrev.normalized + nNext.normalized;
                points[i].restNormal = sum.sqrMagnitude > 1e-8f
                    ? sum.normalized
                    : nNext.normalized;
            }
        }

        void RecomputeCenter()
        {
            if (points == null || points.Length == 0) { centerLocal = Vector2.zero; return; }
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < points.Length; i++) sum += points[i].restPosLocal;
            centerLocal = sum / points.Length;
        }

        Vector2 LocalPos(int i)
            => points[i].restPosLocal + points[i].restNormal * points[i].displacement;

        Vector2 WorldPos(int i)
            => transform.TransformPoint(LocalPos(i));

        // ---- Shape generators (exposed as context menu + callable from editor tools) ----

        [ContextMenu("Generate Circle Points")]
        public void GenerateCirclePoints()
        {
            int n = Mathf.Max(6, pointCount);
            if (restPointsLocal == null) restPointsLocal = new List<Vector2>();
            restPointsLocal.Clear();
            for (int i = 0; i < n; i++)
            {
                float a = (float)i / n * Mathf.PI * 2f;
                restPointsLocal.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }

        [ContextMenu("Generate Star Points")]
        public void GenerateStarPoints()
        {
            int tips = Mathf.Max(3, pointCount / 2);
            int n = tips * 2;
            if (restPointsLocal == null) restPointsLocal = new List<Vector2>();
            restPointsLocal.Clear();
            float inner = radius * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float a = (float)i / n * Mathf.PI * 2f + Mathf.PI * 0.5f;
                float r = (i % 2 == 0) ? radius : inner;
                restPointsLocal.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        }

        [ContextMenu("Generate Line Points")]
        public void GenerateLinePointsDefault() => GenerateLinePoints(pointCount, radius * 0.5f);

        public void GenerateLinePoints(int count, float spacing)
        {
            count = Mathf.Max(2, count);
            float totalWidth = (count - 1) * spacing;
            float depth = spacing;
            if (restPointsLocal == null) restPointsLocal = new List<Vector2>();
            restPointsLocal.Clear();
            for (int i = 0; i < count; i++)
                restPointsLocal.Add(new Vector2(-totalWidth * 0.5f + i * spacing, 0f));
            restPointsLocal.Add(new Vector2( totalWidth * 0.5f, -depth));
            restPointsLocal.Add(new Vector2(-totalWidth * 0.5f, -depth));
        }

        [ContextMenu("Clear Points")]
        public void ClearPoints()
        {
            if (restPointsLocal == null) restPointsLocal = new List<Vector2>();
            restPointsLocal.Clear();
        }

        public int OutlinePointCount => restPointsLocal == null ? 0 : restPointsLocal.Count;
        public Vector2 GetOutlinePointLocal(int i) => restPointsLocal[i];
        public void SetOutlinePointLocal(int i, Vector2 v) => restPointsLocal[i] = v;
        public void InsertOutlinePointLocal(int index, Vector2 v) => restPointsLocal.Insert(index, v);
        public void RemoveOutlinePointAt(int i) => restPointsLocal.RemoveAt(i);

        /// Bulk-replace the outline. Caller is responsible for calling
        /// RebuildRuntime() afterwards if Play-mode state needs refreshing.
        public void ReplaceOutlinePoints(IList<Vector2> pts)
        {
            if (restPointsLocal == null) restPointsLocal = new List<Vector2>();
            restPointsLocal.Clear();
            if (pts == null) return;
            for (int i = 0; i < pts.Count; i++) restPointsLocal.Add(pts[i]);
        }

        /// <summary>
        /// Copies the rest outline into the attached PolygonCollider2D (if any).
        /// The trigger zone follows the static rest shape, not the animated surface —
        /// that keeps entry/exit detection stable even when waves are rippling.
        /// Safe to call every frame; no-ops if no PolygonCollider2D is attached.
        /// </summary>
        [ContextMenu("Sync Collider To Rest Shape")]
        public void SyncColliderToRestShape()
        {
            var poly = GetComponent<PolygonCollider2D>();
            if (poly == null) return;
            if (restPointsLocal == null || restPointsLocal.Count < 3) return;
            poly.points = restPointsLocal.ToArray();
        }
    }
}
