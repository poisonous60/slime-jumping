using UnityEngine;
using MegaFiers;

namespace SlimeJumping.Platforms
{
    // Bend-based SpringyBranch: the spring-damper state drives MegaBend.angle, the mesh
    // deforms, MegaDeform2DCollider follows. The Transform stays world-fixed.
    // Inherits PlatformMotor because carrier.Attach() walks up to find one — without it
    // the rider-impulse pump never wires up. AngularVelocity is overridden because the
    // base computes it from rigid TRS deltas, which are always 0 here.
    public class SpringyBranchPlatform_v2 : PlatformMotor
    {
        [Header("Bend Target")]
        [SerializeField] MegaBend bend;

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
        [Tooltip("Static gravity torque scale. Tunes how far rider weight alone bends the branch.")]
        [SerializeField, Range(0f, 2f)] float riderTorqueScale = 1f;

        [Header("Decay (energy out)")]
        [SerializeField, Range(0f, 5f)] float passiveDecayRate = 0f;
        [SerializeField, Range(0f, 5f)] float riderDecayRate = 0f;

        [Header("Ceiling")]
        [Tooltip("Hard cap on tip linear speed (|bendAngleVel| rad/s × branchLength).")]
        [SerializeField, Min(0f)] float maxTipSpeed = 18f;
        [SerializeField, Range(0f, 2f)] float velocityTransferScale = 1f;

        [Header("Re-entry Guards")]
        [SerializeField, Min(0f)] float slamMinDownSpeed = 3f;
        [SerializeField, Min(0f)] float slamReattachLockout = 0.15f;

        [Header("Rest Dead-Zone")]
        [Tooltip("|bendAngle| below this (deg) counts as 'at rest' when no rider is attached. " +
                 "Combined with restVelocityEpsilon, snaps physics to 0 so MegaBend's sub-threshold " +
                 "deformation doesn't jitter the collider outline.")]
        [SerializeField, Min(0f)] float restAngleEpsilon = 0.01f;
        [Tooltip("|bendAngleVel| below this (deg/s) counts as 'at rest' when no rider is attached.")]
        [SerializeField, Min(0f)] float restVelocityEpsilon = 0.1f;

        [Header("Direction")]
        [Tooltip("World-space direction the branch tip deflects toward for positive bendAngle. " +
                 "Default Vector2.down matches gravity-driven bending. Normalize not required.")]
        [SerializeField] Vector2 bendDownWorld = Vector2.down;

        [Header("Pivot")]
        [Tooltip("World-space anchor for the physics bend pivot. If assigned, this " +
                 "Transform's position is the torque origin. Leave empty to fall back " +
                 "to the motor transform's own position. Physics pivot is independent " +
                 "of MegaBend.gizmoPos — the two can be placed separately.")]
        [SerializeField] Transform pivotAnchor;
        [Tooltip("Extra world-space offset added on top of the auto/anchor pivot. Handy when " +
                 "you want a small nudge without creating a new Transform.")]
        [SerializeField] Vector2 pivotWorldOffset;

        // Captured once in Awake and returned verbatim from ComputeMotion so
        // PlatformMotor.MovePosition keeps the transform pinned. Independent of the bend pivot.
        Vector2 basePosition;
        float baseRotation;

        Vector2 pivotWorldPos;
        float bendAngle;
        float bendAngleVel;
        bool riderAttached;
        float detachTimer;
        float pendingRiderTorque;
        Vector2 previousRiderVel;
        bool hasPrevRiderVel;
        float currentRiderMass;
        float currentRiderArmSqr;

        // Intentionally does NOT read MegaBend.gizmoPos — the physics pivot is
        // self-owned and decoupled from the visual modifier.
        Vector2 ComputePivotWorld()
        {
            if (pivotAnchor != null)
                return (Vector2)pivotAnchor.position + pivotWorldOffset;
            return (Vector2)transform.position + pivotWorldOffset;
        }

        protected override void Awake()
        {
            base.Awake();
            basePosition = rb.position;
            baseRotation = rb.rotation;
            pivotWorldPos = ComputePivotWorld();
            bendAngle = 0f;
            bendAngleVel = 0f;
            detachTimer = slamReattachLockout;
            hasPrevRiderVel = false;

            if (pivotAnchor == null)
                Debug.LogWarning($"[SpringyBranchPlatform_v2] {name}: pivotAnchor unset — physics pivot falls back to this transform's position. Assign a child Pivot Transform if the visual bend fixed-point differs from the root.", this);
        }

        public float CurrentDeflectionDeg => bendAngle;
        public override float VelocityTransferScale => velocityTransferScale;

        // Cantilever uniform-curvature: surface dY/dt at arc r ≈ (r²/2L)·θ_dot, and
        // PlatformCarrier uses AngularVelocity × arm → AngularVelocity = (r/2L)·θ_dot.
        // Contract is deg/s; returning rad/s under-scales launch velocity ~57× and kills jumping.
        public override float AngularVelocity
        {
            get
            {
                if (!riderAttached) return 0f;
                float r = Mathf.Sqrt(currentRiderArmSqr);
                return (r / (2f * Mathf.Max(branchLength, 1e-4f))) * bendAngleVel;
            }
            protected set { /* base FixedUpdate still assigns; ignore, our getter is authoritative */ }
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
            // Rider arm is pivot→rider in the MOTOR's local frame; MegaBend's transform
            // is never consulted.
            Vector2 pivot = ComputePivotWorld();
            pivotWorldPos = pivot;
            Vector2 r = (Vector2)transform.InverseTransformDirection(riderWorldPos - pivot);
            currentRiderMass = mass;
            currentRiderArmSqr = r.sqrMagnitude;

            bool firstAttach = !riderAttached;
            riderAttached = true;

            Vector2 dvWorld = hasPrevRiderVel ? (riderVelocity - previousRiderVel) : riderVelocity;
            Vector2 dv = (Vector2)transform.InverseTransformDirection(dvWorld);

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
                    bendAngleVel += impulseAngVelRad * Mathf.Rad2Deg;
                }
            }

            // First attach baselines from zero, else SlimeController's post-Attach
            // velocity zero-out registers as a phantom Δv.
            previousRiderVel = firstAttach ? Vector2.zero : riderVelocity;
            hasPrevRiderVel = true;

            Vector2 gravityWorld = Physics2D.gravity * mass;
            Vector2 gravity = (Vector2)transform.InverseTransformDirection(gravityWorld);
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

            float angleRadFromRest = bendAngle * Mathf.Deg2Rad;
            float angVelRad = bendAngleVel * Mathf.Deg2Rad;

            float springTorque = -stiffness * angleRadFromRest;
            float dampingTorque = -damping * angVelRad;
            float angularAccelRad = (springTorque + dampingTorque + pendingRiderTorque) / Mathf.Max(ITotal, 1e-6f);
            float angularAccelDeg = angularAccelRad * Mathf.Rad2Deg;

            bendAngleVel += angularAccelDeg * dt;
            bendAngle += bendAngleVel * dt;

            float decayRate = riderAttached ? riderDecayRate : passiveDecayRate;
            if (decayRate > 0f)
                bendAngleVel *= Mathf.Exp(-decayRate * dt);

            float maxAngularVelDeg = (maxTipSpeed / Mathf.Max(branchLength, 1e-4f)) * Mathf.Rad2Deg;
            bendAngleVel = Mathf.Clamp(bendAngleVel, -maxAngularVelDeg, maxAngularVelDeg);

            if (bendAngle > maxDeflectionDeg)
            {
                bendAngle = maxDeflectionDeg;
                if (bendAngleVel > 0f) bendAngleVel = -bendAngleVel * clampRestitution;
            }
            else if (bendAngle < -maxDeflectionDeg)
            {
                bendAngle = -maxDeflectionDeg;
                if (bendAngleVel < 0f) bendAngleVel = -bendAngleVel * clampRestitution;
            }

            // Riderless underdamped oscillation leaves microscopic angle deltas; MegaBend
            // keeps rewriting verts and the collider outline visibly "breathes" at rest.
            // Snap to exact zero so MegaBend's |angle| < 1e-6 no-op path kicks in.
            if (!riderAttached
                && Mathf.Abs(bendAngle) < restAngleEpsilon
                && Mathf.Abs(bendAngleVel) < restVelocityEpsilon)
            {
                bendAngle = 0f;
                bendAngleVel = 0f;
            }

            pendingRiderTorque = 0f;

            pivotWorldPos = ComputePivotWorld();

            // basePosition, not pivotWorldPos — the Rigidbody2D stays pinned to its
            // original location; all visible motion is in the deforming mesh.
            return (basePosition, baseRotation);
        }

        void LateUpdate()
        {
            if (bend == null) return;
            // MegaModifyObject.LateUpdate rewrites verts from bend.angle, MegaDeform2DCollider
            // (execution order int.MaxValue) picks them up in the same frame.
            // MegaBend.axis=X renders positive angle as "tip up", opposite of physics — flip here.
            float signed = bendAngle;
            if (bend.axis == MegaAxis.X) signed = -signed;
            bend.angle = signed;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;

            Vector3 pivot3 = ComputePivotWorld();
            Gizmos.DrawSphere(pivot3, Mathf.Max(0.12f, branchLength * 0.03f));

            // Physics frame: length axis is transform.+X (matches r.x in ApplyRiderInfluence),
            // drop axis is bendDownWorld. No MegaBend introspection.
            Vector3 worldLengthAxis = transform.right;
            Vector3 worldDropDir = ((Vector3)bendDownWorld).normalized;
            if (worldDropDir.sqrMagnitude < 1e-8f) worldDropDir = Vector3.down;

            // Marks a MegaBend sitting on a different transform than the physics pivot.
            if (bend != null && Vector3.Distance(pivot3, bend.transform.position) > 1e-3f)
            {
                Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
                Gizmos.DrawWireSphere(bend.transform.position, Mathf.Max(0.08f, branchLength * 0.02f));
                Gizmos.color = Color.cyan;
            }

            // Positive bendAngle = CCW, which curves the tip AWAY from dropDir.
            // Negate so gravity-driven negative bendAngle draws a downward arc.
            float angleDeg = Application.isPlaying ? -bendAngle : 0f;
            float angleRad = angleDeg * Mathf.Deg2Rad;
            Vector3 prev = pivot3;
            int segs = 16;
            for (int i = 1; i <= segs; i++)
            {
                float s = (i / (float)segs) * branchLength;
                float along = Mathf.Abs(angleRad) < 1e-4f
                    ? s
                    : (branchLength / angleRad) * Mathf.Sin(s * angleRad / branchLength);
                float drop = Mathf.Abs(angleRad) < 1e-4f
                    ? 0f
                    : (branchLength / angleRad) * (1f - Mathf.Cos(s * angleRad / branchLength));
                Vector3 p = pivot3 + worldLengthAxis * along + worldDropDir * drop;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
