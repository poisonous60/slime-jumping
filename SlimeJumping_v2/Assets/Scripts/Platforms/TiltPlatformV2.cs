using UnityEngine;

namespace SlimeJumping.Platforms
{
    // Seesaw beam. The rider's lever arm becomes a torque and the angle integrates it, so the beam
    // keeps swinging toward the end that was stepped on while the rider crosses to the other one.
    public class TiltPlatformV2 : PlatformMotor
    {
        [SerializeField, Min(0f)] float maxTiltAngle = 15f;

        [Header("Seesaw Torque")]
        [Tooltip("Tilt acceleration (deg/s^2) with the rider at the far end and mass 1. " +
                 "Lower = beam is still on its way down when the rider reaches the other end.")]
        [SerializeField, Min(0f)] float tiltAccel = 45f;
        [Tooltip("Per-second decay of tilt speed. 0 = frictionless, keeps swinging.")]
        [SerializeField, Min(0f)] float angularDamping = 1.5f;
        [Tooltip("Lever arm ratio treated as standing on the pivot.")]
        [SerializeField, Range(0f, 1f)] float centerDeadZone = 0.15f;

        [Header("Response Lag")]
        [Tooltip("Seconds for the effective lever arm to catch up to the rider's real position. " +
                 "Higher = crossing the beam buys more time before the torque flips.")]
        [SerializeField, Min(0f)] float leverLag = 0.35f;

        [Header("Unloaded")]
        [Tooltip("Spring pulling the beam back to level, faded in by how far the lever arm has " +
                 "decayed. 0 = beam holds the angle the rider left it at, like a real seesaw.")]
        [SerializeField, Min(0f)] float levelReturnStiffness = 0f;

        Vector2 basePosition;
        float baseRotation;
        float halfWidth;

        float currentTilt;
        float tiltVelocity;
        float lever;
        float targetLever;
        float riderMass = 1f;

        protected override void Awake()
        {
            base.Awake();

            // Any Collider2D: mesh-baked platforms carry a PolygonCollider2D, not a box.
            // World AABB x-extent — platforms are authored flat, so it is the half width.
            var col = GetComponentInChildren<Collider2D>();
            halfWidth = col != null ? col.bounds.extents.x : 0f;
            if (halfWidth < 0.001f)
            {
                Debug.LogError($"[TiltPlatformV2] No Collider2D with nonzero width in children of {name}. Disabling.", this);
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
            targetLever = Mathf.Abs(ratio) < centerDeadZone ? 0f : ratio;
            riderMass = mass;
        }

        public override void RemoveRider()
        {
            targetLever = 0f;
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            // Exponential catch-up, not a raw read: the torque has to lag the rider's real position
            // for the far end to still be rising when the rider lands on it.
            lever = leverLag > 0f
                ? Mathf.Lerp(lever, targetLever, 1f - Mathf.Exp(-dt / leverLag))
                : targetLever;

            float accel = -lever * tiltAccel * riderMass - tiltVelocity * angularDamping;
            // Levelling fades in with the lever arm rather than switching on at detach: a spring
            // that appears the frame the rider jumps out-torques the swing and snaps the beam flat,
            // and one that runs under a rider is the equilibrium the integrator exists to avoid.
            accel += -currentTilt * levelReturnStiffness * (1f - Mathf.Abs(lever));

            tiltVelocity += accel * dt;
            currentTilt += tiltVelocity * dt;

            if (Mathf.Abs(currentTilt) > maxTiltAngle)
            {
                currentTilt = Mathf.Sign(currentTilt) * maxTiltAngle;
                tiltVelocity = 0f;
            }

            return (basePosition, baseRotation + currentTilt);
        }
    }
}
