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

            var box = GetComponentInChildren<BoxCollider2D>();
            if (box == null)
            {
                Debug.LogError($"[TiltPlatform] No BoxCollider2D found in children of {name}. Disabling.", this);
                enabled = false;
                return;
            }

            basePosition = rb.position;
            baseRotation = rb.rotation;
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
            float springForce = (targetTilt - currentTilt) * stiffness;
            float dampingForce = -tiltVelocity * tiltDamping;
            tiltVelocity += (springForce + dampingForce) * dt;
            currentTilt += tiltVelocity * dt;
            return (basePosition, baseRotation + currentTilt);
        }
    }
}
