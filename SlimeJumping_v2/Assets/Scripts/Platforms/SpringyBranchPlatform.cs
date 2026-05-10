using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Platforms
{
    // Hierarchy: BranchPivot (this) is world-fixed in position and rotates only.
    // BranchBody (child) is offset along +X by halfLength so the collider extends outward.
    // Returning a constant pivotWorldPos from ComputeMotion keeps motor.Velocity = 0,
    // so PlatformCarrier.GetEffectiveVelocity only adds AngularVelocity * arm.
    public class SpringyBranchPlatform : PlatformMotor
    {
        [Header("Surface Override")]
        [Tooltip("SurfaceMaterial pushed onto the slime's override stack while attached. " +
                 "Mirrors WaterCurrentZone's pattern; PlatformCarrier handles the Push/Pop on " +
                 "Attach/Detach. Use to grant a longer energyConversionCoyoteTime (or other " +
                 "per-surface tuning) without touching the global SlimeSettings.")]
        [SerializeField] SurfaceMaterial branchSurfaceMaterial;

        public override SurfaceMaterial OverrideSurfaceMaterial => branchSurfaceMaterial;

        [Header("Geometry")]
        [SerializeField, Min(0.1f)] float branchLength = 4f;

        [Header("Spring Shape")]
        [SerializeField, Range(0.3f, 3f)] float naturalFreqHz = 1f;
        [SerializeField, Range(0f, 1f)] float dampingRatio = 0.2f;
        [SerializeField, Range(5f, 90f)] float maxDeflectionDeg = 60f;
        [SerializeField, Range(0f, 0.9f)] float clampRestitution = 0.3f;

        [Header("Pumping (energy in)")]
        [Tooltip("Multiplier on angular impulse from rider Δp. Unified path for slam landing and body shifts.")]
        [SerializeField, Range(0f, 5f)] float riderPumpGain = 1f;
        [Tooltip("Multiplier on riderPumpGain during charging. 0 = timing-only puzzle (body shifts do nothing during charge).")]
        [SerializeField, Range(0f, 2f)] float chargePumpScale = 1f;
        [Tooltip("Static gravity torque scale. Tunes how far rider weight alone pulls the branch down.")]
        [SerializeField, Range(0f, 2f)] float riderTorqueScale = 1f;

        [Header("Decay (energy out)")]
        [Tooltip("Exponential decay (1/s) applied on top of spring damping when no rider.")]
        [SerializeField, Range(0f, 5f)] float passiveDecayRate = 0f;
        [Tooltip("Exponential decay (1/s) applied on top of spring damping when rider attached.")]
        [SerializeField, Range(0f, 5f)] float riderDecayRate = 0f;

        [Header("Ceiling")]
        [Tooltip("Hard cap on tip linear speed (|angularVel| × branchLength). The maximum carrier boost designers can rely on.")]
        [SerializeField, Min(0f)] float maxTipSpeed = 18f;
        [Tooltip("Scale applied to the full effective velocity (Velocity + arm×ω) transferred to the slime at launch/hop.")]
        [SerializeField, Range(0f, 2f)] float velocityTransferScale = 1f;

        [Header("Boost Falloff (pre-bonus speed gate)")]
        [Tooltip("Below this pre-bonus launch speed (m/s) the branch transfers full velocityTransferScale. " +
                 "Pre-bonus speed = slime.Velocity.magnitude when carrier.GetLaunchVelocity is called — " +
                 "already includes EnergyConversion power and hyperdash inheritance, but NOT the branch's " +
                 "own carrier contribution (that's what's being scaled). Lets the branch reward normal " +
                 "launches without stacking extra tip-speed on top of a high-fall EnergyConversion launch.")]
        [SerializeField, Min(0f)] float boostFalloffStartSpeed = 12f;
        [Tooltip("Above this pre-bonus launch speed (m/s) the branch transfers zero carrier velocity. " +
                 "Linear falloff between boostFalloffStartSpeed and boostFalloffEndSpeed. " +
                 "Set ≤ boostFalloffStartSpeed for hard cutoff. The slime's own (EnergyConversion + " +
                 "hyperdash) speed passes through unchanged — only the branch's carrier inheritance is " +
                 "removed, so a high-fall landing still launches with its full converted energy.")]
        [SerializeField, Min(0f)] float boostFalloffEndSpeed = 18f;

        [Header("Re-entry Guards")]
        [SerializeField, Min(0f)] float slamMinDownSpeed = 3f;
        [SerializeField, Min(0f)] float slamReattachLockout = 0.15f;

        Vector2 pivotWorldPos;
        float baseRotation;
        float currentAngle;
        float angularVel;
        bool riderAttached;
        float detachTimer;
        float pendingRiderTorque;
        Vector2 previousRiderVel;
        bool hasPrevRiderVel;
        float currentRiderMass;
        float currentRiderArmSqr;
        SlimeController cachedSlime;

        protected override void Awake()
        {
            base.Awake();
            pivotWorldPos = rb.position;
            baseRotation = rb.rotation;
            currentAngle = 0f;
            angularVel = 0f;
            detachTimer = slamReattachLockout;
            hasPrevRiderVel = false;
        }

        public float CurrentDeflectionDeg => currentAngle;

        // Mirrors TautLine.GetLaunchBonus' boost-falloff gate. Queried by
        // PlatformCarrier.GetLaunchVelocity at the launch moment, when slime.Velocity
        // already contains aim power × charge ratio + hyperdash inheritance, but the
        // carrier contribution is what we're about to scale. Falloff multiplier deletes
        // the branch's tip-speed transfer when the slime is already going fast (e.g.
        // big EnergyConversion of a high fall) so the branch can't stack extra speed
        // on top of an already-converted launch.
        public override float VelocityTransferScale
        {
            get
            {
                if (cachedSlime == null) cachedSlime = FindFirstObjectByType<SlimeController>();
                float preBonusSpeed = cachedSlime != null ? cachedSlime.Velocity.magnitude : 0f;
                float speedMul = boostFalloffEndSpeed > boostFalloffStartSpeed
                    ? 1f - Mathf.Clamp01(Mathf.InverseLerp(boostFalloffStartSpeed, boostFalloffEndSpeed, preBonusSpeed))
                    : (preBonusSpeed >= boostFalloffStartSpeed ? 0f : 1f);
                return velocityTransferScale * speedMul;
            }
        }

        (float stiffness, float damping, float ITotal) ComputeSpringCoeffs()
        {
            float IBranch = rb.mass * branchLength * branchLength / 3f;
            float ITotal = IBranch + (riderAttached ? currentRiderMass * currentRiderArmSqr : 0f);
            float omega = 2f * Mathf.PI * naturalFreqHz;
            float stiffness = omega * omega * ITotal;
            float damping = 2f * dampingRatio * omega * ITotal;
            return (stiffness, damping, ITotal);
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass, Vector2 riderVelocity, bool isCharging)
        {
            Vector2 r = riderWorldPos - pivotWorldPos;
            currentRiderMass = mass;
            currentRiderArmSqr = r.sqrMagnitude;

            bool firstAttach = !riderAttached;
            riderAttached = true;

            Vector2 dv = hasPrevRiderVel ? (riderVelocity - previousRiderVel) : riderVelocity;

            bool suppress = firstAttach
                            && (detachTimer < slamReattachLockout)
                            && (-riderVelocity.y < slamMinDownSpeed);

            if (!suppress)
            {
                Vector2 impulseP = dv * mass;
                float pumpGain = riderPumpGain * (isCharging ? chargePumpScale : 1f);
                var (_, _, ITotal) = ComputeSpringCoeffs();
                if (ITotal > 1e-6f)
                {
                    float impulseAngVelRad = (r.x * impulseP.y - r.y * impulseP.x) / ITotal * pumpGain;
                    angularVel += impulseAngVelRad * Mathf.Rad2Deg;
                }
            }

            // On firstAttach SlimeController immediately zeroes its velocity after carrier.Attach
            // (see SlimeController.Airborne.cs), which would otherwise be seen as a phantom Δv
            // next tick and cancel the slam we just applied. Baseline from zero so the next-tick
            // Δp measures real post-landing motion.
            previousRiderVel = firstAttach ? Vector2.zero : riderVelocity;
            hasPrevRiderVel = true;

            Vector2 gravity = Physics2D.gravity * mass;
            float gravTorque = r.x * gravity.y - r.y * gravity.x;
            pendingRiderTorque += gravTorque * riderTorqueScale;
        }

        public override void RemoveRider()
        {
            riderAttached = false;
            hasPrevRiderVel = false;
            detachTimer = 0f;
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            detachTimer += dt;

            var (stiffness, damping, ITotal) = ComputeSpringCoeffs();

            float angleRadFromRest = currentAngle * Mathf.Deg2Rad;
            float angVelRad = angularVel * Mathf.Deg2Rad;

            float springTorque = -stiffness * angleRadFromRest;
            float dampingTorque = -damping * angVelRad;
            float angularAccelRad = (springTorque + dampingTorque + pendingRiderTorque) / Mathf.Max(ITotal, 1e-6f);
            float angularAccelDeg = angularAccelRad * Mathf.Rad2Deg;

            angularVel += angularAccelDeg * dt;
            currentAngle += angularVel * dt;

            float decayRate = riderAttached ? riderDecayRate : passiveDecayRate;
            if (decayRate > 0f)
                angularVel *= Mathf.Exp(-decayRate * dt);

            float maxAngularVelDeg = (maxTipSpeed / Mathf.Max(branchLength, 1e-4f)) * Mathf.Rad2Deg;
            angularVel = Mathf.Clamp(angularVel, -maxAngularVelDeg, maxAngularVelDeg);

            if (currentAngle > maxDeflectionDeg)
            {
                currentAngle = maxDeflectionDeg;
                if (angularVel > 0f) angularVel = -angularVel * clampRestitution;
            }
            else if (currentAngle < -maxDeflectionDeg)
            {
                currentAngle = -maxDeflectionDeg;
                if (angularVel < 0f) angularVel = -angularVel * clampRestitution;
            }

            pendingRiderTorque = 0f;

            return (pivotWorldPos, baseRotation + currentAngle);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Vector2 pivot = Application.isPlaying ? pivotWorldPos : (rb != null ? rb.position : (Vector2)transform.position);
            Gizmos.DrawSphere(pivot, 0.12f);
            // Use transform.rotation: at edit time this matches Body's placement,
            // at runtime PlatformMotor keeps it in sync with baseRotation + currentAngle.
            float angleDeg = (rb != null ? rb.rotation : transform.eulerAngles.z);
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector2 tip = pivot + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * branchLength;
            Gizmos.DrawLine(pivot, tip);
        }
    }
}
