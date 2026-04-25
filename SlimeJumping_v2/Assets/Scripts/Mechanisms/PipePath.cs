using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Waypoint path between two PipeEntrances, stored in this GameObject's LOCAL
    /// space so the path follows when the parent (test setup root, level chunk)
    /// is moved in the editor. SampleAtDistance returns world coordinates.
    /// Designer constraint: keep this transform's lossyScale at 1 — TotalLength
    /// reads local distances, so non-unit scale would desync the arc-length math.
    /// </summary>
    public class PipePath : MonoBehaviour
    {
        [Tooltip("Path waypoints in this transform's local space. First = entry endpoint, last = exit endpoint.")]
        [SerializeField] private Vector2[] waypoints = new Vector2[2];

        public int WaypointCount => waypoints != null ? waypoints.Length : 0;

        /// <summary>Local-space waypoint at index i.</summary>
        public Vector2 GetLocalWaypoint(int i) => waypoints[i];
        /// <summary>World-space waypoint at index i.</summary>
        public Vector2 GetWaypoint(int i) => transform.TransformPoint(waypoints[i]);

        public Vector2 FirstPoint => transform.TransformPoint(waypoints[0]);
        public Vector2 LastPoint => transform.TransformPoint(waypoints[waypoints.Length - 1]);

        /// <summary>Replace waypoints. Points must be in this transform's LOCAL space.</summary>
        public void SetWaypoints(Vector2[] localPoints)
        {
            waypoints = localPoints;
        }

        /// <summary>Replace waypoints from world-space inputs. Each point is converted to local.</summary>
        public void SetWaypointsWorld(Vector2[] worldPoints)
        {
            if (worldPoints == null) { waypoints = null; return; }
            waypoints = new Vector2[worldPoints.Length];
            for (int i = 0; i < worldPoints.Length; i++)
                waypoints[i] = (Vector2)transform.InverseTransformPoint(worldPoints[i]);
        }

        public float TotalLength
        {
            get
            {
                if (waypoints == null || waypoints.Length < 2) return 0f;
                float total = 0f;
                for (int i = 1; i < waypoints.Length; i++)
                    total += Vector2.Distance(waypoints[i - 1], waypoints[i]);
                return total;
            }
        }

        /// <summary>
        /// Sample a world-space point along the path at the given arc-length distance.
        /// Distance is clamped to [0, TotalLength]. Tangent is the unit world
        /// direction of the segment that contains the sample.
        /// </summary>
        public Vector2 SampleAtDistance(float distance, out Vector2 tangent)
        {
            tangent = Vector2.right;
            if (waypoints == null || waypoints.Length < 2) return (Vector2)transform.position;

            if (distance <= 0f)
            {
                Vector2 wsA = (Vector2)transform.TransformPoint(waypoints[0]);
                Vector2 wsB = (Vector2)transform.TransformPoint(waypoints[1]);
                tangent = (wsB - wsA).normalized;
                return wsA;
            }

            float remaining = distance;
            for (int i = 1; i < waypoints.Length; i++)
            {
                Vector2 a = waypoints[i - 1];
                Vector2 b = waypoints[i];
                Vector2 seg = b - a;
                float segLen = seg.magnitude;
                if (remaining <= segLen)
                {
                    float t = segLen > 0.0001f ? remaining / segLen : 0f;
                    Vector2 localSample = Vector2.Lerp(a, b, t);
                    Vector2 wsA = (Vector2)transform.TransformPoint(a);
                    Vector2 wsB = (Vector2)transform.TransformPoint(b);
                    tangent = segLen > 0.0001f ? (wsB - wsA).normalized : Vector2.right;
                    return (Vector2)transform.TransformPoint(localSample);
                }
                remaining -= segLen;
            }

            int last = waypoints.Length - 1;
            Vector2 wsLastA = (Vector2)transform.TransformPoint(waypoints[last - 1]);
            Vector2 wsLastB = (Vector2)transform.TransformPoint(waypoints[last]);
            tangent = (wsLastB - wsLastA).normalized;
            return wsLastB;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (waypoints == null || waypoints.Length < 2) return;
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            for (int i = 1; i < waypoints.Length; i++)
            {
                Vector3 a = transform.TransformPoint(waypoints[i - 1]);
                Vector3 b = transform.TransformPoint(waypoints[i]);
                Gizmos.DrawLine(a, b);
            }

            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            for (int i = 0; i < waypoints.Length; i++)
                Gizmos.DrawSphere(transform.TransformPoint(waypoints[i]), 0.15f);
        }
#endif
    }
}
