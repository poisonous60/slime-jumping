using UnityEngine;

namespace SlimeJumping.Platforms
{
    public class PendulumPlatform : PlatformMotor
    {
        [SerializeField] Transform pivot;
        [SerializeField, Min(0.01f)] float ropeLength = 3f;
        [SerializeField] float gravity = 10f;
        [SerializeField] float damping = 0.02f;
        [SerializeField] float startAngleDeg = 30f;
        [SerializeField] float swayRatio = 0.3f;

        [Header("Rider Tilt")]
        [SerializeField] float maxTiltAngle = 15f;
        [SerializeField] float tiltStiffness = 20f;
        [SerializeField] float tiltDamping = 5f;

        float angle;
        float angularVel;

        float halfWidth;
        float currentTilt;
        float targetTilt;
        float tiltVelocity;

        protected override void Awake()
        {
            base.Awake();

            if (pivot == null)
            {
                Debug.LogError($"[PendulumPlatform] pivot is not assigned on {name}. Disabling.", this);
                enabled = false;
                return;
            }

            var box = GetComponentInChildren<BoxCollider2D>();
            if (box == null)
            {
                Debug.LogError($"[PendulumPlatform] No BoxCollider2D found in children of {name}. Disabling.", this);
                enabled = false;
                return;
            }

            angle = startAngleDeg * Mathf.Deg2Rad;
            angularVel = 0f;
            halfWidth = box.size.x * box.transform.lossyScale.x * 0.5f;
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass)
        {
            float localX = transform.InverseTransformPoint(riderWorldPos).x;
            float ratio = Mathf.Clamp(localX / halfWidth, -1f, 1f);
            targetTilt = Mathf.Clamp(-ratio * maxTiltAngle * mass, -maxTiltAngle, maxTiltAngle);
        }

        public override void RemoveRider()
        {
            targetTilt = 0f;
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            // Pendulum equation: θ'' = -(g/L)sin(θ) - d·θ'
            float angularAccel = -(gravity / ropeLength) * Mathf.Sin(angle)
                                  - damping * angularVel;
            angularVel += angularAccel * dt;
            angle += angularVel * dt;

            Vector2 pivotPos = pivot.position;
            Vector2 offset = new Vector2(
                Mathf.Sin(angle) * ropeLength,
                -Mathf.Cos(angle) * ropeLength
            );

            float springForce = (targetTilt - currentTilt) * tiltStiffness;
            float dampingForce = -tiltVelocity * tiltDamping;
            tiltVelocity += (springForce + dampingForce) * dt;
            currentTilt += tiltVelocity * dt;
            float rotDeg = angle * Mathf.Rad2Deg * swayRatio + currentTilt;

            return (pivotPos + offset, rotDeg);
        }

        void OnDrawGizmos()
        {
            if (pivot == null) return;
            Gizmos.color = Color.yellow;
            Vector2 pivotPos = pivot.position;
            Vector2 platformPos = rb != null ? rb.position : (Vector2)transform.position;
            Gizmos.DrawLine(pivotPos, platformPos);
            Gizmos.DrawSphere(pivotPos, 0.1f);
        }
    }
}
