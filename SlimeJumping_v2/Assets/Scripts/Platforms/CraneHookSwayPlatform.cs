using UnityEngine;

namespace SlimeJumping.Platforms
{
    /// Crane-suspended container platform: small sideways X sway around the wire-top
    /// pivot. Rider interaction follows the TiltPlatform pattern — PlatformCarrier feeds
    /// ApplyRiderInfluence every tick, so a weight-shift target tracks the slime while
    /// ridden and landing momentum kicks the spring. The container collider is this
    /// kinematic body, so the slime is genuinely carried; visuals follow the interpolated
    /// body pose with a world-Z rotation, keeping every joint attached.
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Platforms/Crane Hook Sway Platform")]
    public class CraneHookSwayPlatform : PlatformMotor
    {
        [Header("Scene References")]
        [Tooltip("Visual subtree rotated around the wire top (hook + wire + beams + container meshes).")]
        [SerializeField] private Transform visualRoot;
        [Tooltip("Wire mesh — sway pivot is the top center of its bounds (inside the slide housing).")]
        [SerializeField] private Renderer wireRenderer;
        [Tooltip("Optional second kinematic body (spreader beams collider) driven with the same sway.")]
        [SerializeField] private Rigidbody2D beamsBody;

        [Header("Sway (sideways X at container level, world units)")]
        [Tooltip("Max lateral offset of the container in world units. Targets and kicks never exceed this.")]
        [Min(0f)][SerializeField] private float maxSwayOffset = 0.25f;
        [SerializeField] private float swayFrequency = 0.4f;
        [Range(0f, 1f)][SerializeField] private float swayDampingRatio = 0.18f;

        [Header("Rider Response")]
        [Tooltip("Lateral target per unit of rider offset from the container center per unit rider mass. " +
                 "Positive = assembly drifts away from the rider side (hanging-load equilibrium). Negative flips.")]
        [SerializeField] private float weightShiftPerMass = 0.04f;
        [Tooltip("Fraction of the rider's landing momentum converted to lateral sway speed.")]
        [SerializeField] private float landKickScale = 0.08f;

        private Vector2 basePos;
        private float baseRot;
        private Vector2 beamsBasePos;
        private float beamsBaseRot;
        private Vector3 visualBasePos;
        private Quaternion visualBaseRot;
        private Vector3 pivot;
        private float armLength;

        private float swayOffset, swayVel, swayTarget;
        private bool hasRider;

        protected override void Awake()
        {
            base.Awake();

            if (visualRoot == null || wireRenderer == null)
            {
                Debug.LogWarning("[CraneHookSwayPlatform] Missing scene references — disabling.", this);
                enabled = false;
                return;
            }

            var wb = wireRenderer.bounds;
            pivot = new Vector3(wb.center.x, wb.max.y, wb.center.z);

            basePos = rb.position;
            baseRot = rb.rotation;
            armLength = Mathf.Max(0.1f, pivot.y - basePos.y);

            if (beamsBody != null)
            {
                beamsBody.bodyType = RigidbodyType2D.Kinematic;
                beamsBody.interpolation = RigidbodyInterpolation2D.Interpolate;
                beamsBasePos = beamsBody.position;
                beamsBaseRot = beamsBody.rotation;
            }

            visualBasePos = visualRoot.position;
            visualBaseRot = visualRoot.rotation;

            // Both physics bodies must live outside the visual subtree, or the LateUpdate
            // visual rotation would double-move their colliders.
            DetachFromVisual(transform);
            if (beamsBody != null) DetachFromVisual(beamsBody.transform);
        }

        private void DetachFromVisual(Transform t)
        {
            if (t.IsChildOf(visualRoot)) t.SetParent(visualRoot.parent, true);
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass, Vector2 riderVelocity, bool isCharging)
        {
            float offsetX = riderWorldPos.x - rb.position.x;

            if (!hasRider)
            {
                // Momentum into a pendulum: lateral speed change is v.x plus the torque
                // of the vertical component applied off-center.
                float lateral = riderVelocity.x + riderVelocity.y * offsetX / armLength;
                swayVel += lateral * landKickScale;

                // Amplitude ≈ sqrt(x² + (v/ω)²) — cap velocity by the energy remaining
                // under the max offset.
                float omega = SwayOmega();
                float velCap = omega * Mathf.Sqrt(Mathf.Max(0f,
                    maxSwayOffset * maxSwayOffset - swayOffset * swayOffset));
                swayVel = Mathf.Clamp(swayVel, -velCap, velCap);
            }

            swayTarget = Mathf.Clamp(-offsetX * weightShiftPerMass * mass, -maxSwayOffset, maxSwayOffset);
            hasRider = true;
        }

        public override void RemoveRider()
        {
            hasRider = false;
            swayTarget = 0f;
        }

        private float SwayOmega() => 2f * Mathf.PI * Mathf.Max(0.01f, swayFrequency);

        protected override (Vector2 position, float rotation) ComputeMotion(float dt)
        {
            float w = SwayOmega();
            float accel = w * w * (swayTarget - swayOffset) - 2f * swayDampingRatio * w * swayVel;
            swayVel += accel * dt;
            swayOffset += swayVel * dt;

            if (Mathf.Abs(swayOffset) > maxSwayOffset)
            {
                swayOffset = Mathf.Sign(swayOffset) * maxSwayOffset;
                if (swayOffset * swayVel > 0f) swayVel = 0f;
            }

            float angleDeg = AngleDeg();
            return (RotatedAroundPivot(basePos, angleDeg), baseRot + angleDeg);
        }

        protected override void OnAfterMotion(float dt)
        {
            if (beamsBody == null) return;
            float angleDeg = AngleDeg();
            beamsBody.MovePosition(RotatedAroundPivot(beamsBasePos, angleDeg));
            beamsBody.MoveRotation(beamsBaseRot + angleDeg);
        }

        private float AngleDeg() => (swayOffset / armLength) * Mathf.Rad2Deg;

        private Vector2 RotatedAroundPivot(Vector2 point, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(rad), s = Mathf.Sin(rad);
            Vector2 arm = point - (Vector2)pivot;
            return (Vector2)pivot + new Vector2(arm.x * c - arm.y * s, arm.x * s + arm.y * c);
        }

        // Visuals track the INTERPOLATED body transform, not the fixed-step rb pose.
        // The lateral offset is read back from the transform and re-expressed as a
        // world-Z rotation around the pivot (local axes are unusable — parent rig
        // carries a Y rotation, so local-Z would swing front-to-back).
        private void LateUpdate()
        {
            float interpOffsetX = transform.position.x - basePos.x;
            float angleDeg = (interpOffsetX / armLength) * Mathf.Rad2Deg;
            Quaternion q = Quaternion.AngleAxis(angleDeg, Vector3.forward);
            visualRoot.position = pivot + q * (visualBasePos - pivot);
            visualRoot.rotation = q * visualBaseRot;
        }
    }
}
