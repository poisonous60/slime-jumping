using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Gravity vector replacement zone. NOT a force field.
    /// Inside this zone, "down" points in a different direction.
    /// Gravity magnitude is always |SlimeSettings.gravity|.
    /// No falloff, no attraction, no deadzone.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class GravityField : MonoBehaviour
    {
        public enum GravityMode { Fixed, Radial }

        [SerializeField] private GravityMode gravityMode = GravityMode.Fixed;

        [Tooltip("Fixed mode: local 'down' direction (world-space, auto-normalized).")]
        [SerializeField] private Vector2 localDownDirection = Vector2.down;

        [Tooltip("Radial mode: pivot point in local space. Down = toward this point.")]
        [SerializeField] private Vector2 localPivot = Vector2.zero;

        [Tooltip("Radial mode: false = inward (toward pivot), true = outward (away from pivot).")]
        [SerializeField] private bool invertDirection = false;

        private SlimeController trackedSlime;

        void Reset()
        {
            GetComponent<Collider2D>().isTrigger = true;
            gameObject.layer = LayerMask.NameToLayer("GravityField");
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;
            trackedSlime = slime;
            slime.PushGravityField(this);
        }

        void OnTriggerExit2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null || slime != trackedSlime) return;
            trackedSlime = null;
            slime.PopGravityField(this);
        }

        /// <summary>
        /// Returns the normalized "down" direction at the given world position.
        /// Fixed mode: constant direction regardless of position.
        /// Radial mode: toward (or away from) the pivot point.
        /// </summary>
        public Vector2 GetDownDirection(Vector2 worldPos)
        {
            if (gravityMode == GravityMode.Fixed)
            {
                if (localDownDirection.sqrMagnitude < 0.0001f) return Vector2.down;
                return localDownDirection.normalized;
            }

            // Radial mode
            Vector2 worldPivot = (Vector2)transform.TransformPoint(localPivot);
            Vector2 towardPivot = worldPivot - worldPos;
            if (towardPivot.sqrMagnitude < 0.0001f)
                return Vector2.down; // NaN guard: degenerate case (slime at pivot)

            Vector2 dir = towardPivot.normalized;
            return invertDirection ? -dir : dir;
        }

        // --- Gizmos ---

        private static readonly Color GizmoColor = new Color(0.7f, 0.3f, 1f, 0.6f);
        private static readonly Color GizmoFill = new Color(0.7f, 0.3f, 1f, 0.12f);
        private static readonly Color GizmoWire = new Color(0.7f, 0.3f, 1f, 0.5f);

        void OnDrawGizmos()
        {
            Gizmos.color = GizmoColor;

            if (gravityMode == GravityMode.Fixed)
            {
                Vector3 center = transform.position;
                Vector3 dir = ((Vector3)(Vector2)localDownDirection).normalized;
                float len = 1.5f;
                Gizmos.DrawLine(center, center + dir * len);
                // Arrowhead
                Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
                Vector3 tip = center + dir * len;
                Gizmos.DrawLine(tip, tip - dir * 0.25f + perp * 0.12f);
                Gizmos.DrawLine(tip, tip - dir * 0.25f - perp * 0.12f);
            }
            else
            {
                // Radial: draw pivot marker
                Vector3 worldPivot = transform.TransformPoint(localPivot);
                Gizmos.DrawWireSphere(worldPivot, 0.15f);
            }
        }

        void OnDrawGizmosSelected()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;

            // Zone fill
            Gizmos.color = GizmoFill;
            if (col is CircleCollider2D circle)
            {
                Vector3 center = transform.TransformPoint(circle.offset);
                float worldRadius = circle.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
                Gizmos.DrawSphere(center, worldRadius);
                Gizmos.color = GizmoWire;
                Gizmos.DrawWireSphere(center, worldRadius);
            }
            else if (col is BoxCollider2D box)
            {
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(
                    transform.TransformPoint(box.offset),
                    transform.rotation, transform.lossyScale);
                Gizmos.DrawCube(Vector3.zero, box.size);
                Gizmos.color = GizmoWire;
                Gizmos.DrawWireCube(Vector3.zero, box.size);
                Gizmos.matrix = old;
            }

            // Direction field arrows
            if (gravityMode == GravityMode.Fixed)
            {
                Gizmos.color = new Color(0.8f, 0.5f, 1f, 0.4f);
                Vector3 dir = ((Vector3)(Vector2)localDownDirection).normalized;
                float len = 1.0f;
                Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
                for (int i = -2; i <= 2; i++)
                {
                    Vector3 offset = perp * (i * 0.4f);
                    Vector3 start = transform.position + offset;
                    Gizmos.DrawLine(start, start + dir * len);
                }
            }
            else
            {
                // Radial: arrows from boundary points toward/away from pivot
                Gizmos.color = new Color(0.8f, 0.5f, 1f, 0.4f);
                Vector3 worldPivot = transform.TransformPoint(localPivot);
                float arrowLen = 0.8f;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * Mathf.PI * 2f / 8f;
                    Vector3 boundaryDir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                    Vector3 boundaryPoint = transform.position + boundaryDir * 1.5f;
                    Vector3 towardPivot = (worldPivot - boundaryPoint).normalized;
                    Vector3 arrowDir = invertDirection ? -towardPivot : towardPivot;
                    Gizmos.DrawLine(boundaryPoint, boundaryPoint + arrowDir * arrowLen);
                }
                // Pivot marker
                Gizmos.color = GizmoColor;
                Gizmos.DrawWireSphere(worldPivot, 0.15f);
                // Line from center to pivot if offset
                if (localPivot.sqrMagnitude > 0.01f)
                {
                    Gizmos.color = new Color(0.7f, 0.3f, 1f, 0.3f);
                    Gizmos.DrawLine(transform.position, worldPivot);
                }
            }
        }
    }
}
