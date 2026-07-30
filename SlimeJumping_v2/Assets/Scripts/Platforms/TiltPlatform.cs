using UnityEngine;

namespace SlimeJumping.Platforms
{
    public class TiltPlatform : PlatformMotor
    {
        [SerializeField] float maxTiltAngle = 15f;
        [SerializeField] float stiffness = 20f;
        [SerializeField] float tiltDamping = 5f;

        Vector2 basePosition;
        float baseRotation;
        float currentTilt;
        float targetTilt;
        float tiltVelocity;
        float halfWidth;

        protected override void Awake()
        {
            base.Awake();

            // Any Collider2D: mesh-baked platforms carry a PolygonCollider2D, not a box.
            // World AABB x-extent — platforms are authored flat, so it is the half width.
            var col = GetComponentInChildren<Collider2D>();
            halfWidth = col != null ? col.bounds.extents.x : 0f;
            if (halfWidth < 0.001f)
            {
                Debug.LogError($"[TiltPlatform] No Collider2D with nonzero width in children of {name}. Disabling.", this);
                enabled = false;
                return;
            }

            basePosition = rb.position;
            baseRotation = rb.rotation;
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass, Vector2 riderVelocity, bool isCharging)
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
            float springForce = (targetTilt - currentTilt) * stiffness;
            float dampingForce = -tiltVelocity * tiltDamping;
            tiltVelocity += (springForce + dampingForce) * dt;
            currentTilt += tiltVelocity * dt;
            return (basePosition, baseRotation + currentTilt);
        }
    }
}
