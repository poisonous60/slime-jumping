using UnityEngine;

namespace SlimeJumping.Platforms
{
    public class PendulumPlatform : PlatformMotor
    {
        [SerializeField] Transform pivot;
        [SerializeField] float ropeLength = 3f;
        [SerializeField] float gravity = 10f;
        [SerializeField] float damping = 0.02f;
        [SerializeField] float startAngleDeg = 30f;

        float angle;
        float angularVel;

        protected override void Awake()
        {
            base.Awake();
            angle = startAngleDeg * Mathf.Deg2Rad;
            angularVel = 0f;
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

            float rotDeg = angle * Mathf.Rad2Deg * 0.3f;

            return (pivotPos + offset, rotDeg);
        }

        void OnDrawGizmos()
        {
            if (pivot == null) return;
            Gizmos.color = Color.yellow;
            Vector2 pivotPos = pivot.position;
            Vector2 platformPos = Application.isPlaying
                ? rb.position
                : (Vector2)transform.position;
            Gizmos.DrawLine(pivotPos, platformPos);
            Gizmos.DrawSphere(pivotPos, 0.1f);
        }
    }
}
