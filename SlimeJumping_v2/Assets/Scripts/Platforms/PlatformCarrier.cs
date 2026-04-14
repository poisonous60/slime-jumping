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
        /// Add to linearVelocity for smooth interpolation.
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
    }
}
