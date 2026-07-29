using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.Platforms
{
    /// <summary>Default <see cref="ISnakePath"/>: authored waypoints + optional Catmull-Rom, backed by a dense arc-length table so SampleWorld is O(log N).</summary>
    [DisallowMultipleComponent]
    public class WaypointSnakePath : MonoBehaviour, ISnakePath
    {
        [Serializable]
        public struct PathWaypoint
        {
            [Tooltip("Local position of this waypoint.")]
            public Vector3 localPos;
            [Tooltip("Head speed (m/s) while traveling from this waypoint to the next.")]
            public float speedAfter;
        }

        [SerializeField] List<PathWaypoint> waypoints = new List<PathWaypoint>();
        [SerializeField] bool loop = true;
        [SerializeField] bool useCatmullRom = true;
        [Tooltip("Samples per path-segment when building the arc-length table. Higher = smoother Catmull-Rom / more accurate arc length; cost is linear.")]
        [Range(2, 64)] [SerializeField] int samplesPerSegment = 16;
        [SerializeField] bool drawGizmos = true;

        Vector3[] denseLocalPos;   // dense samples (local space)
        float[]   denseArc;        // cumulative local arc length; only matches world arc without non-uniform scale
        int[]     denseSegIndex;   // which waypoint-segment each dense sample belongs to
        float     totalArcLen;

        public float TotalArcLen => totalArcLen;
        public bool  Loop        => loop;

        void Awake()
        {
            Rebuild();
        }

        void OnEnable()
        {
            // Dense table isn't serialized — domain reload wipes it, leaving gizmos/handles empty.
            if (denseLocalPos == null || denseLocalPos.Length < 2) Rebuild();
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (samplesPerSegment < 2) samplesPerSegment = 2;
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                Rebuild();
            };
        }
#endif

        public void Rebuild()
        {
            int n = waypoints == null ? 0 : waypoints.Count;
            if (n < 2)
            {
                denseLocalPos = null;
                denseArc = null;
                denseSegIndex = null;
                totalArcLen = 0f;
                return;
            }

            int segCount = loop ? n : n - 1;
            int k = Mathf.Max(2, samplesPerSegment);
            // Per segment we sample k points at t=[0..k-1]/k. Final sample caps the last segment at t=1.
            int denseCount = segCount * k + 1;

            if (denseLocalPos == null || denseLocalPos.Length != denseCount)
            {
                denseLocalPos = new Vector3[denseCount];
                denseArc = new float[denseCount];
                denseSegIndex = new int[denseCount];
            }

            int write = 0;
            for (int s = 0; s < segCount; s++)
            {
                for (int i = 0; i < k; i++)
                {
                    float t = i / (float)k;
                    denseLocalPos[write] = SampleSegmentLocal(s, t);
                    denseSegIndex[write] = s;
                    write++;
                }
            }
            // Closing sample.
            {
                int lastSeg = segCount - 1;
                float tEnd = loop ? 0f : 1f;
                int segOfEnd = loop ? 0 : lastSeg;
                denseLocalPos[write] = SampleSegmentLocal(segOfEnd, tEnd);
                denseSegIndex[write] = lastSeg;
            }

            denseArc[0] = 0f;
            for (int i = 1; i < denseCount; i++)
                denseArc[i] = denseArc[i - 1] + Vector3.Distance(denseLocalPos[i - 1], denseLocalPos[i]);

            totalArcLen = denseArc[denseCount - 1];
        }

        Vector3 SampleSegmentLocal(int segIndex, float t)
        {
            int n = waypoints.Count;
            Vector3 p1 = waypoints[segIndex].localPos;
            Vector3 p2 = waypoints[(segIndex + 1) % n].localPos;

            if (!useCatmullRom)
                return Vector3.Lerp(p1, p2, t);

            Vector3 p0, p3;
            if (loop)
            {
                p0 = waypoints[(segIndex - 1 + n) % n].localPos;
                p3 = waypoints[(segIndex + 2) % n].localPos;
            }
            else
            {
                p0 = (segIndex == 0) ? (2f * p1 - p2) : waypoints[segIndex - 1].localPos;
                p3 = (segIndex + 2 >= n) ? (2f * p2 - p1) : waypoints[segIndex + 2].localPos;
            }

            // Standard Catmull-Rom (tension 0.5).
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3
            );
        }

        public void SampleWorld(float arcLen, out Vector3 position, out Vector3 tangent)
        {
            if (denseLocalPos == null || denseLocalPos.Length < 2 || totalArcLen <= 0f)
            {
                position = transform.position;
                tangent = transform.right;
                return;
            }

            arcLen = NormalizeArc(arcLen);

            int lo = 0, hi = denseArc.Length - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (denseArc[mid] <= arcLen) lo = mid;
                else hi = mid;
            }

            float segLen = denseArc[hi] - denseArc[lo];
            float u = segLen > 1e-6f ? (arcLen - denseArc[lo]) / segLen : 0f;
            Vector3 posLocal = Vector3.Lerp(denseLocalPos[lo], denseLocalPos[hi], u);
            position = transform.TransformPoint(posLocal);

            // Tangent: central-difference in arc-space via a tiny epsilon.
            float eps = Mathf.Max(totalArcLen * 1e-4f, 1e-3f);
            Vector3 a = SampleWorldPositionOnly(NormalizeArc(arcLen - eps));
            Vector3 b = SampleWorldPositionOnly(NormalizeArc(arcLen + eps));
            Vector3 t3d = b - a;
            float tm = t3d.magnitude;
            tangent = tm > 1e-6f ? t3d / tm : transform.right;
        }

        Vector3 SampleWorldPositionOnly(float arcLen)
        {
            int lo = 0, hi = denseArc.Length - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (denseArc[mid] <= arcLen) lo = mid;
                else hi = mid;
            }
            float segLen = denseArc[hi] - denseArc[lo];
            float u = segLen > 1e-6f ? (arcLen - denseArc[lo]) / segLen : 0f;
            return transform.TransformPoint(Vector3.Lerp(denseLocalPos[lo], denseLocalPos[hi], u));
        }

        float NormalizeArc(float arcLen)
        {
            if (totalArcLen <= 0f) return 0f;
            if (loop)
            {
                float m = arcLen % totalArcLen;
                if (m < 0f) m += totalArcLen;
                return m;
            }
            return Mathf.Clamp(arcLen, 0f, totalArcLen);
        }

        public float SpeedAt(float arcLen)
        {
            if (waypoints == null || waypoints.Count < 2) return 0f;
            if (denseLocalPos == null || denseLocalPos.Length < 2) return waypoints[0].speedAfter;

            arcLen = NormalizeArc(arcLen);

            // Same binary search; use denseSegIndex to identify which waypoint segment this arc sits in.
            int lo = 0, hi = denseArc.Length - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (denseArc[mid] <= arcLen) lo = mid;
                else hi = mid;
            }
            int segIdx = denseSegIndex[lo];
            if (segIdx < 0) segIdx = 0;
            if (segIdx >= waypoints.Count) segIdx = waypoints.Count - 1;
            return waypoints[segIdx].speedAfter;
        }

        void OnDrawGizmos()
        {
            if (!drawGizmos) return;
            if (waypoints == null || waypoints.Count < 2) return;

            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            for (int i = 0; i < waypoints.Count; i++)
            {
                Vector3 w = transform.TransformPoint(waypoints[i].localPos);
                Gizmos.DrawSphere(w, 0.12f);
            }

            // Dense curve.
            if (denseLocalPos != null && denseLocalPos.Length >= 2)
            {
                Gizmos.color = new Color(0.9f, 0.6f, 0.2f, 0.9f);
                for (int i = 0; i < denseLocalPos.Length - 1; i++)
                {
                    Gizmos.DrawLine(
                        transform.TransformPoint(denseLocalPos[i]),
                        transform.TransformPoint(denseLocalPos[i + 1]));
                }
            }
            else
            {
                // Fallback: straight polyline so pre-built gizmos are still useful.
                Gizmos.color = new Color(0.4f, 0.6f, 0.9f, 0.7f);
                int n = waypoints.Count;
                int last = loop ? n : n - 1;
                for (int i = 0; i < last; i++)
                {
                    Vector3 a = transform.TransformPoint(waypoints[i].localPos);
                    Vector3 b = transform.TransformPoint(waypoints[(i + 1) % n].localPos);
                    Gizmos.DrawLine(a, b);
                }
            }
        }

        // Used by the editor test setup to author waypoints from code.
        public void SetWaypoints(List<PathWaypoint> newWaypoints, bool loop, bool catmullRom)
        {
            waypoints = newWaypoints;
            this.loop = loop;
            useCatmullRom = catmullRom;
            Rebuild();
        }
    }
}
