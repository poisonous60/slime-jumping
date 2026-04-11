using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(Collider2D))]
    public class SpikeHazard : MonoBehaviour, IHazardSource
    {
        [Header("Hazard")]
        [SerializeField] private float knockbackForce = 10f;
        [SerializeField] private float stunDuration = 0.5f;

        [Header("Direction")]
        [Tooltip("If true, knockback follows spike's local up direction.\nIf false, auto-compute from spike center toward slime.")]
        [SerializeField] private bool useLocalUp = true;

        [Tooltip("Override target: knockback aims toward this world position instead of local up.\nOnly used when useLocalUp is false and this is assigned.")]
        [SerializeField] private Transform redirectTarget;

        public bool IsActive => isActiveAndEnabled;

        void Reset()
        {
            GetComponent<Collider2D>().isTrigger = true;
        }

        public HazardEffect GetHazardEffect()
        {
            return new HazardEffect
            {
                knockbackForce = knockbackForce,
                knockbackDirection = useLocalUp ? (Vector2)transform.up : Vector2.zero,
                stunDuration = stunDuration,
                damageType = DamageType.Spike
            };
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            var effect = GetHazardEffect();

            if (!useLocalUp)
            {
                if (redirectTarget != null)
                {
                    // Redirect mode: knockback aims toward target position
                    Vector2 dir = (Vector2)redirectTarget.position - (Vector2)slime.transform.position;
                    effect.knockbackDirection = dir.sqrMagnitude > 0.0001f
                        ? dir.normalized
                        : Vector2.up;
                }
                else
                {
                    // Auto mode: knockback from spike center toward slime
                    Vector2 dir = (Vector2)slime.transform.position - (Vector2)transform.position;
                    effect.knockbackDirection = dir.sqrMagnitude > 0.0001f
                        ? dir.normalized
                        : Vector2.up;
                }
            }

            slime.ApplyHazardEffect(effect);
        }

        // --- Gizmos ---

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.6f);
            Vector3 center = transform.position;

            if (useLocalUp)
            {
                Vector3 dir = transform.up;
                Gizmos.DrawLine(center, center + dir * 0.8f);
                Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
                Vector3 tip = center + dir * 0.8f;
                Gizmos.DrawLine(tip, tip - dir * 0.2f + perp * 0.1f);
                Gizmos.DrawLine(tip, tip - dir * 0.2f - perp * 0.1f);
            }
            else if (redirectTarget != null)
            {
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
                Gizmos.DrawLine(center, redirectTarget.position);
                Gizmos.DrawWireSphere(redirectTarget.position, 0.15f);
            }
        }

        void OnDrawGizmosSelected()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;

            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.15f);
            if (col is BoxCollider2D box)
            {
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(
                    transform.TransformPoint(box.offset),
                    transform.rotation, transform.lossyScale);
                Gizmos.DrawCube(Vector3.zero, box.size);
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
                Gizmos.DrawWireCube(Vector3.zero, box.size);
                Gizmos.matrix = old;
            }
            else if (col is CircleCollider2D circle)
            {
                Vector3 center = transform.TransformPoint(circle.offset);
                float r = circle.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y);
                Gizmos.DrawSphere(center, r);
                Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.5f);
                Gizmos.DrawWireSphere(center, r);
            }
        }
    }
}
