using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    [RequireComponent(typeof(Rigidbody2D))]
    [DefaultExecutionOrder(-200)]
    public class PlatformCarrier : MonoBehaviour
    {
        Rigidbody2D rb;
        SlimeController slime;
        PlatformMotor currentPlatform;

        [SerializeField] float slimeMass = 1f;

        public bool IsOnPlatform => currentPlatform != null;

        void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            slime = GetComponent<SlimeController>();
        }

        Vector2 RiderVelocity => slime != null ? slime.Velocity : Vector2.zero;

        public void Attach(Collider2D surfaceCollider)
        {
            if (currentPlatform != null) currentPlatform.RemoveRider();

            currentPlatform = surfaceCollider
                ? surfaceCollider.GetComponentInParent<PlatformMotor>()
                : null;

            if (currentPlatform != null)
                currentPlatform.ApplyRiderInfluence(rb.position, slimeMass, RiderVelocity, RiderIsCharging);
        }

        public void Detach()
        {
            if (currentPlatform != null) currentPlatform.RemoveRider();
            currentPlatform = null;
        }

        void FixedUpdate()
        {
            if (currentPlatform == null) return;
            currentPlatform.ApplyRiderInfluence(rb.position, slimeMass, RiderVelocity, RiderIsCharging);
        }

        bool RiderIsCharging => slime != null && slime.IsCharging;

        /// <summary>
        /// Platform velocity including rotational tangential contribution.
        /// Used at launch/detach to inherit platform momentum into slime velocity.
        /// </summary>
        public Vector2 GetEffectiveVelocity()
        {
            if (currentPlatform == null) return Vector2.zero;

            Vector2 vel = currentPlatform.Velocity;

            if (Mathf.Abs(currentPlatform.AngularVelocity) > 0.001f)
            {
                float radPerSec = currentPlatform.AngularVelocity * Mathf.Deg2Rad;
                Vector2 arm = rb.position - (Vector2)currentPlatform.transform.position;
                vel += new Vector2(-arm.y, arm.x) * radPerSec;
            }

            return vel;
        }

        /// <summary>
        /// Translate the rider by the platform's position delta this frame, plus the
        /// rotational displacement of the rider's arm around the platform pivot.
        /// Carries the rider as a position offset rather than baking platform velocity
        /// into the rider's linearVelocity — avoids dynamic/kinematic integration
        /// jitter on moving platforms.
        /// </summary>
        public void ApplyPositionCarry(float carryScale)
        {
            if (currentPlatform == null || carryScale == 0f) return;

            Vector2 delta = currentPlatform.DeltaPosition;

            if (Mathf.Abs(currentPlatform.DeltaRotation) > 0.001f)
            {
                float rad = currentPlatform.DeltaRotation * Mathf.Deg2Rad;
                float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
                Vector2 arm = rb.position - (Vector2)currentPlatform.transform.position;
                Vector2 rotatedArm = new Vector2(arm.x * c - arm.y * s, arm.x * s + arm.y * c);
                delta += rotatedArm - arm;
            }

            rb.MovePosition(rb.position + delta * carryScale);
        }
    }
}
