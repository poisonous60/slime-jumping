using UnityEngine;

namespace SlimeJumping.Platforms
{
    [RequireComponent(typeof(CircleCollider2D))]
    public class RollingDiskPlatform : PlatformMotor
    {
        [SerializeField] float riderAngAccelScale = 50f;
        [SerializeField] float angularDamping = 0.3f;
        [SerializeField] float maxAngularSpeed = 720f;

        [Tooltip("Rider angular position from the top of the disk (measured from world-up) " +
                 "below which no torque is applied. Analogous to stickSlopeThreshold for sliding.")]
        [SerializeField, Range(0f, 89f)] float deadZoneAngleDeg = 15f;

        Vector2 basePosition;
        float currentAngle;
        float angularVel;
        float pendingAngAccel;
        float diskRadius;

        protected override void Awake()
        {
            base.Awake();
            basePosition = rb.position;
            currentAngle = rb.rotation;

            var col = GetComponent<CircleCollider2D>();
            float maxScale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
            diskRadius = col.radius * maxScale;
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass, Vector2 riderVelocity, bool isCharging)
        {
            // World-horizontal offset — gravity (world-down) creates the imbalance,
            // so the torque arm is measured in world X, not disk-local X.
            float offsetX = riderWorldPos.x - rb.position.x;

            // Dead zone: within deadZoneAngleDeg of the disk's top, no torque.
            // offsetX ≈ radius · sin(rider-angle-from-top) for a rider on the disk surface.
            // Compare |offsetX| against the sin of the threshold to get an angle-based band.
            float deadZoneX = diskRadius * Mathf.Sin(deadZoneAngleDeg * Mathf.Deg2Rad);
            float excessMag = Mathf.Abs(offsetX) - deadZoneX;
            if (excessMag <= 0f) return;

            // Smooth fade-in past the threshold: torque starts at 0 at the boundary and
            // scales linearly, avoiding a hard step at the dead-zone edge.
            float effectiveArm = excessMag * Mathf.Sign(offsetX);
            pendingAngAccel += -effectiveArm * mass * riderAngAccelScale;
        }

        public override void RemoveRider() { }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            float angAccel = pendingAngAccel - angularDamping * angularVel;
            angularVel = Mathf.Clamp(angularVel + angAccel * dt, -maxAngularSpeed, maxAngularSpeed);
            currentAngle += angularVel * dt;
            pendingAngAccel = 0f;
            return (basePosition, currentAngle);
        }
    }
}
