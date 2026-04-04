using UnityEngine;

namespace SlimeJumping.Platforms
{
    public class TiltPlatform : PlatformMotor
    {
        [SerializeField] float maxTiltAngle = 15f;
        [SerializeField] float stiffness = 20f;
        [SerializeField] float tiltDamping = 5f;

        float baseRotation;
        float currentTilt;
        float targetTilt;
        float tiltVelocity;
        float halfWidth;

        protected override void Awake()
        {
            base.Awake();
            baseRotation = rb.rotation;
            halfWidth = GetComponentInChildren<Collider2D>().bounds.extents.x;
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass)
        {
            float localX = transform.InverseTransformPoint(riderWorldPos).x;
            float ratio = Mathf.Clamp(localX / halfWidth, -1f, 1f);
            targetTilt = -ratio * maxTiltAngle * mass;
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
            return (rb.position, baseRotation + currentTilt);
        }
    }
}
