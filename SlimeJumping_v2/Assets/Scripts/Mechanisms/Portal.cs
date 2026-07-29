using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    [RequireComponent(typeof(BoxCollider2D))]
    public class Portal : MonoBehaviour
    {
        [SerializeField] private Portal exit;
        [SerializeField] private float exitOffset = 0.6f;
        [SerializeField] private float reentryCooldown = 0.2f;
        [SerializeField] private float maxExitSpeed = 30f;

        private float cooldownUntil;

        public Vector2 OutNormal => transform.right;
        public Vector2 Tangent => transform.up;

        void Reset()
        {
            GetComponent<BoxCollider2D>().isTrigger = true;
        }

        void OnTriggerEnter2D(Collider2D other)
        {
            if (exit == null || Time.time < cooldownUntil) return;

            var slime = other.GetComponentInParent<SlimeController>();
            if (slime == null) return;

            Teleport(slime);
        }

        void Teleport(SlimeController slime)
        {
            Vector2 v = slime.Velocity;

            float vIn = Vector2.Dot(v, -OutNormal);
            float vTangent = Vector2.Dot(v, Tangent);

            Vector2 newV = exit.OutNormal * vIn + exit.Tangent * vTangent;

            // Clamp to prevent tunneling on repeated loops
            if (maxExitSpeed > 0f && newV.magnitude > maxExitSpeed)
                newV = newV.normalized * maxExitSpeed;

            // Preserve tangent offset (entry position along portal face → exit)
            float tangentOff = Vector2.Dot((Vector2)slime.transform.position - (Vector2)transform.position, Tangent);
            Vector2 exitPos = (Vector2)exit.transform.position + exit.OutNormal * exitOffset - exit.Tangent * tangentOff;
            slime.TeleportTo(exitPos, newV);

            exit.cooldownUntil = Time.time + reentryCooldown;
        }

        void OnDrawGizmos()
        {
            Vector3 p = transform.position;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(p - (Vector3)Tangent * 0.5f, p + (Vector3)Tangent * 0.5f);

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(p, p + (Vector3)(Vector2)OutNormal * 0.5f);
        }
    }
}
