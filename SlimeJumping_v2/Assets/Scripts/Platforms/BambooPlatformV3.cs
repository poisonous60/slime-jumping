using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;
using SlimeJumping.Settings;

namespace SlimeJumping.Platforms
{
    // Bamboo V3 — springboard. Rigidbody2D base + collider-only child segments on the same
    // closed-form arc the BambooBend shader bends the visual to, so both stay coincident.
    // One damped-spring ODE: weight torque r x (m*g) every frame the rider is on, plus one
    // landing impulse dOmega = (r x m*v_rel)/I on firstAttach. A player launch applies NO
    // reaction impulse by design — the throw must look the same whether or not jump was pressed.
    public class BambooPlatformV3 : PlatformMotor, ISurfaceAnchorProvider
    {
        public enum SegmentColliderShape { Capsule, Box }

        [Header("Settings")]
        [Tooltip("Shared gameplay tuning asset (5 knobs). If null, a runtime instance with the class defaults is created in Awake so a freshly dropped stalk still behaves.")]
        [SerializeField] BambooV3Settings settings;

        [Header("Surface Override")]
        [SerializeField] SurfaceMaterial bambooSurfaceMaterial;
        public override SurfaceMaterial OverrideSurfaceMaterial => bambooSurfaceMaterial;

        [Header("Mode")]
        [Tooltip("If true the stalk spawns as visual-only decoration: no collider segments, no rider physics, no adhesion break. Rebuild Segments after toggling.")]
        [SerializeField] bool backgroundOnly = false;

        [Header("Geometry")]
        [SerializeField, Min(0.5f)] float bambooLength = 6f;
        [SerializeField, Range(2, 32)] int segmentCount = 12;
        [SerializeField, Min(0.02f)] float segmentRadius = 0.15f;
        [Tooltip("Small length overlap between neighbouring collider segments — hides capsule/box seam jitter while the visual node rings stay aligned to the segment boundaries. Rebuild Segments after changing.")]
        [SerializeField, Min(0f)] float colliderSegmentOverlap = 0.03f;
        [SerializeField] SegmentColliderShape colliderShape = SegmentColliderShape.Capsule;

        [Header("Visual")]
        [Tooltip("Material using the SlimeJumping/BambooBend shader. If null a runtime material is created. _BendAngleDeg and _BambooLength are pushed per-frame via MaterialPropertyBlock so one shared material works across many stalks.")]
        [SerializeField] Material bendMaterial;
        [Tooltip("Optional bark texture sampled as the shader's _BaseMap. Mixed in via baseMapStrength; leave at 0 for clean stylized colour.")]
        [SerializeField] Texture2D bambooBarkTexture;
        [SerializeField, Range(0f, 1f)] float baseMapStrength = 0f;

        [Header("Visual — Stalk Color Gradient")]
        [SerializeField] Color stalkColorBase = new Color(0.30f, 0.52f, 0.20f, 1f);
        [SerializeField] Color stalkColorTip = new Color(0.50f, 0.68f, 0.28f, 1f);
        [SerializeField, Range(0f, 1f)] float shadowDarken = 0.60f;

        [Header("Visual — Node Geometry (마디)")]
        // Shader scars sit at t = k/segmentCount, the same split the Segment_XX colliders use,
        // so the visible rings land on the collider joints.
        [SerializeField, Range(0f, 0.5f)] float bulgeAmount = 0.10f;
        [SerializeField, Range(0.01f, 0.4f)] float bulgeWidth = 0.12f;
        [SerializeField, Range(0.001f, 0.1f)] float scarHalfWidth = 0.004f;
        [SerializeField, Range(0f, 0.2f)] float scarSoftness = 0.02f;
        [SerializeField, Range(0.1f, 6f)] float scarColorCurve = 1f;
        [SerializeField, Range(0f, 0.2f)] float scarDepth = 0.03f;
        [SerializeField] Color scarColor = new Color(0.22f, 0.26f, 0.14f, 1f);

        [Header("Visual — Toon Lighting")]
        [SerializeField, Range(-1f, 1f)] float toonThreshold = 0.15f;
        [SerializeField, Range(0.001f, 0.5f)] float toonSoftness = 0.08f;
        [SerializeField, Range(0f, 1f)] float rimStrength = 0.25f;
        [SerializeField, Range(0.5f, 8f)] float rimPower = 3f;
        [Tooltip("World-space light direction the shader uses for the toon split. Points TOWARD the light.")]
        [SerializeField] Vector3 lightDirWS = new Vector3(0.3f, 0.85f, -0.45f);

        [Header("Visual — Mesh Tessellation")]
        [SerializeField, Range(6, 32)] int visualRadialSegments = 16;
        [SerializeField, Range(4, 64)] int visualHeightSegments = 24;

        // Lockout after a throw so the still-swinging stalk cannot instantly re-break a grip the
        // slime has not had time to form. Guards the throw gate only; collider ignore is separate.
        const float PostEjectLockout = 0.15f;

        // Landing impulse bleeds into the spring over this window. Instantaneous delta-omega
        // teleports the surface out from under a still-attaching slime and SurfaceProbe loses
        // it, so fast arrivals bounce off instead of sticking.
        const float LandingImpulseRampTime = 0.1f;

        // peakBendSinceAttach is a high-water mark, so omega jitter at a decayed equilibrium can
        // still pass the peak gate and "throw" at ~0.3 m/s into the whip-back, which recatches and
        // re-throws forever. Genuine spring-back throws run ~6 m/s and clear this easily.
        const float MinEjectSpeed = 2f;

        // omega^2 = wn^2 (A^2 - x^2) at x = A/2 gives sqrt(3)/2. ThrowCapable only, to predict
        // the speed the throw WOULD leave with.
        const float HalfReturnOmegaFraction = 0.866f;

        Vector2 pivotWorldPos;
        float baseRotation;
        float currentAngle;      // deg, tangent angle at the tip of the arc
        float angularVel;        // deg/s
        float lastAngularAccelRad;
        float pendingRiderTorque;

        // External loads are not riders — they never claim firstAttach. `pending` accumulates the
        // tick; `externalInertia` is committed once per ComputeMotion so every read agrees.
        float pendingExternalInertia;
        float externalInertia;

        float peakBendSinceAttach;
        float peakAngleSigned;   // signed angle at the peak — the throw's return-fraction reference
        bool returningFromPeak;
        float prevAngularVel;

        // Landing-impulse ramp state (see LandingImpulseRampTime).
        float rampOmegaRemainingDeg;
        float rampOmegaRateDeg;

        bool riderAttached;
        float currentRiderMass;
        float currentRiderArmSqr;
        Vector2 lastRiderWorldPos;

        float ejectLockoutTimer;

        // Cling timer for a slime hanging under this stalk (see UnderhangSlip).
        UnderhangSlip underhang;

        // Diagnostic view only. The carrier (order -200) calls ApplyRiderInfluence BEFORE this
        // motor (-100), so these cannot be zeroed in ComputeMotion without clobbering the same
        // step's value — each write stamps lastImpulseFixedTime and stale reads report zero.
        float lastRiderImpulseDeg;
        int lastRiderImpulseKind;
        bool lastRiderChargingFlag;
        float lastImpulseFixedTime = -1f;
        float lastWeightTorque;

        SlimeController cachedSlime;

        Transform[] segmentTransforms;
        MeshRenderer visualRenderer;
        MaterialPropertyBlock propertyBlock;

        static readonly int BendAngleId = Shader.PropertyToID("_BendAngleDeg");
        static readonly int BambooLengthId = Shader.PropertyToID("_BambooLength");
        static readonly int BambooColorBaseId = Shader.PropertyToID("_BambooColorBase");
        static readonly int BambooColorTipId = Shader.PropertyToID("_BambooColorTip");
        static readonly int ShadowDarkenId = Shader.PropertyToID("_ShadowDarken");
        static readonly int ScarColorId = Shader.PropertyToID("_ScarColor");
        static readonly int NodeFrequencyId = Shader.PropertyToID("_NodeFrequency");
        static readonly int BulgeAmountId = Shader.PropertyToID("_BulgeAmount");
        static readonly int BulgeWidthId = Shader.PropertyToID("_BulgeWidth");
        static readonly int ScarHalfWidthId = Shader.PropertyToID("_ScarHalfWidth");
        static readonly int ScarSoftnessId = Shader.PropertyToID("_ScarSoftness");
        static readonly int ScarColorCurveId = Shader.PropertyToID("_ScarColorCurve");
        static readonly int ScarDepthId = Shader.PropertyToID("_ScarDepth");
        static readonly int ToonThresholdId = Shader.PropertyToID("_ToonThreshold");
        static readonly int ToonSoftnessId = Shader.PropertyToID("_ToonSoftness");
        static readonly int RimId = Shader.PropertyToID("_Rim");
        static readonly int RimPowerId = Shader.PropertyToID("_RimPower");
        static readonly int LightDirWSId = Shader.PropertyToID("_LightDirWS");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseMapStrengthId = Shader.PropertyToID("_BaseMapStrength");

        const string VisualChildName = "Visual";

        public float CurrentDeflectionDeg => currentAngle;
        public float BambooLength => bambooLength;
        public float SegmentRadius => segmentRadius;
        public float TipSpeed => Mathf.Abs(angularVel * Mathf.Deg2Rad) * bambooLength;
        /// <summary>Shared tuning asset — the fallen-pole corpse rides the same knobs.</summary>
        public BambooV3Settings Settings => settings;

        // Read-only views so a diagnostic can pull the chain; this component pushes nothing to it.
        /// <summary>True between ApplyRiderInfluence and RemoveRider. False while the slime visibly
        /// sits on the stalk = the carrier never attached, so no impulse can reach the spring.</summary>
        public bool RiderAttached => riderAttached;
        public Vector2 LastRiderWorldPos => lastRiderWorldPos;
        public Vector2 PivotWorldPos => Application.isPlaying ? pivotWorldPos : (Vector2)transform.position;
        /// <summary>Largest |bend| reached since the rider landed — what the throw gate tests.</summary>
        public float PeakBendSinceAttach => peakBendSinceAttach;
        /// <summary>Stage 1 of the throw: the stalk has passed its peak and is springing back.</summary>
        public bool ReturningFromPeak => returningFromPeak;

        /// <summary>Will this swing end in a throw? Predicts both CheckSpringThrow gates from the
        /// undamped SHO state, including the pending landing ramp — reading angularVel alone calls a
        /// hard hit unthrowable for its first frames. Damping ignored so it decays false on its own.</summary>
        public bool ThrowCapable
        {
            get
            {
                if (backgroundOnly) return false;
                if (settings.ejectBendDeg <= 0f || ejectLockoutTimer > 0f) return false;

                // Arm from the SLIME, not the shared rider slot: a fallen pole resting here writes
                // lastRiderWorldPos too, so that would predict a throw for a non-slime contact.
                if (!TryGetSlimeArm(out Vector2 arm)) return false;

                float omegaN = 2f * Mathf.PI * settings.naturalFreqHz;
                if (omegaN < 1e-4f) return false;

                var (stiffness, _, _) = ComputeSpringCoeffs();
                float eqDeg = stiffness > 1e-4f ? (lastWeightTorque / stiffness) * Mathf.Rad2Deg : 0f;
                float offsetDeg = currentAngle - eqDeg;
                float rateDeg = (angularVel + rampOmegaRemainingDeg) / omegaN;
                float ampDeg = Mathf.Sqrt(offsetDeg * offsetDeg + rateDeg * rateDeg);

                // The real gate measures |currentAngle| against zero, not against equilibrium.
                if (Mathf.Abs(eqDeg) + ampDeg < settings.ejectBendDeg) return false;

                float ejectSpeed = ampDeg * Mathf.Deg2Rad * omegaN * HalfReturnOmegaFraction * arm.magnitude;
                return ejectSpeed >= MinEjectSpeed;
            }
        }

        /// <summary>Moment arm of the SLIME, or false if it is not riding this stalk. The rider slot
        /// holds whatever touched last (e.g. a fallen pole), so slime-targeted math must re-measure.</summary>
        bool TryGetSlimeArm(out Vector2 arm)
        {
            arm = Vector2.zero;
            var slime = ResolveSlime();
            if (slime == null) return false;
            var surf = slime.SurfaceCollider;
            if (surf == null || !surf.transform.IsChildOf(transform)) return false;
            arm = slime.Position - pivotWorldPos;
            return arm.sqrMagnitude > 1e-6f;
        }
        public float EjectLockoutTimer => ejectLockoutTimer;
        public float EjectBendDeg => settings != null ? settings.ejectBendDeg : 0f;
        public float NaturalFreqHz => settings != null ? settings.naturalFreqHz : 0f;
        public float DampingRatio => settings != null ? settings.dampingRatio : 0f;
        public bool HasSettingsAsset => settings != null;
        public bool BackgroundOnly => backgroundOnly;
        /// <summary>Rotational inertia of the stalk alone — decides how much an impact bends it.</summary>
        public float StalkInertiaDebug => rb != null ? StalkInertia() : 0f;

        /// <summary>Angular impulse (deg/s) the most recent rider influence injected this step, 0
        /// otherwise. Non-zero while the rider is charging means jump is kicking the stalk.</summary>
        public float LastRiderImpulseDeg => lastImpulseFixedTime == Time.fixedTime ? lastRiderImpulseDeg : 0f;
        /// <summary>Which branch produced LastRiderImpulseDeg: 0 none, 1 landing (firstAttach), 2 coupled (per-frame momentum change).</summary>
        public int LastRiderImpulseKind => lastImpulseFixedTime == Time.fixedTime ? lastRiderImpulseKind : 0;
        /// <summary>isCharging flag the carrier passed on the most recent rider influence this step.</summary>
        public bool LastRiderCharging => lastImpulseFixedTime == Time.fixedTime && lastRiderChargingFlag;
        /// <summary>Rider-weight torque applied on the previous fixed step (constant-ish; not the jump kick).</summary>
        public float LastWeightTorque => lastWeightTorque;

        /// <summary>Rest axis in world space (root local +Y). The root rigidbody holds the authored
        /// tilt and never rotates at runtime — bend lives in the child segments.</summary>
        public Vector2 StalkUp
        {
            get
            {
                Vector2 up = transform.up;
                return up.sqrMagnitude > 1e-6f ? up.normalized : Vector2.up;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureSettings();
            EnsureSurfaceTag();
            pivotWorldPos = rb.position;
            baseRotation = rb.rotation;
            currentAngle = 0f;
            angularVel = 0f;
            CacheChildren();
        }

        /// <summary>The tag must exist before touchdown: SlimeController reads the ceiling-attach arc
        /// from a SurfaceTag at the moment of contact, BEFORE the carrier pushes OverrideSurfaceMaterial.
        /// Without it a tilted stalk's underside reads as a wall and nothing ever attaches.</summary>
        void EnsureSurfaceTag()
        {
            if (bambooSurfaceMaterial == null) return;
            var tag = GetComponent<SurfaceTag>();
            if (tag == null) tag = gameObject.AddComponent<SurfaceTag>();
            if (tag.material == null) tag.material = bambooSurfaceMaterial;
        }

        void EnsureSettings()
        {
            if (settings == null)
                settings = ScriptableObject.CreateInstance<BambooV3Settings>();
        }

        void CacheChildren()
        {
            segmentTransforms = new Transform[segmentCount];
            for (int i = 0; i < segmentCount; i++)
            {
                var child = transform.Find(SegmentName(i));
                if (child != null) segmentTransforms[i] = child;
            }

            var visualChild = transform.Find(VisualChildName);
            visualRenderer = visualChild != null ? visualChild.GetComponent<MeshRenderer>() : null;
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();
        }

        static string SegmentName(int i) => $"Segment_{i:D2}";

        SlimeController ResolveSlime()
        {
            if (cachedSlime == null) cachedSlime = FindFirstObjectByType<SlimeController>();
            return cachedSlime;
        }

        // --- Carrier contract -------------------------------------------------------

        // Pivot is fixed; all motion is the child segments bending, so the base rigidbody
        // reports no linear velocity and PlatformCarrier's omega x arm term does the work.
        public override Vector2 Velocity { get => Vector2.zero; protected set { } }
        public override float AngularVelocity { get => angularVel; protected set { } }

        // Scales the surface velocity (omega x arm) the launch inherits, with a falloff to zero as the
        // slime gets fast. Read at the launch moment: slime.Velocity holds aim power + hyperdash, not this.
        public override float VelocityTransferScale
        {
            get
            {
                var slime = ResolveSlime();
                float preBonusSpeed = slime != null ? slime.Velocity.magnitude : 0f;
                float speedMul = settings.boostFalloffEndSpeed > settings.boostFalloffStartSpeed
                    ? 1f - Mathf.Clamp01(Mathf.InverseLerp(settings.boostFalloffStartSpeed, settings.boostFalloffEndSpeed, preBonusSpeed))
                    : (preBonusSpeed >= settings.boostFalloffStartSpeed ? 0f : 1f);
                return settings.launchBoostScale * speedMul;
            }
        }

        public override Vector2 GetRiderCarryDelta(Vector2 riderWorldPos, float dt)
        {
            if (dt <= 0f) return Vector2.zero;

            float radPerSec = angularVel * Mathf.Deg2Rad;
            if (Mathf.Abs(radPerSec) < 0.0001f) return Vector2.zero;

            Vector2 arm = riderWorldPos - pivotWorldPos;
            return new Vector2(-arm.y, arm.x) * radPerSec * dt;
        }

        // --- Rider physics ----------------------------------------------------------

        /// <summary>Second moment of the stalk alone about the base pivot (uniform rod) — the inertia
        /// that responds while the rider is not yet co-rotating with it.</summary>
        float StalkInertia() => rb.mass * bambooLength * bambooLength / 3f;

        (float stiffness, float damping, float ITotal) ComputeSpringCoeffs()
        {
            float ITotal = StalkInertia() + (riderAttached ? currentRiderMass * currentRiderArmSqr : 0f)
                           + externalInertia;
            float omega = 2f * Mathf.PI * settings.naturalFreqHz;
            float stiffness = omega * omega * ITotal;
            float damping = 2f * settings.dampingRatio * omega * ITotal;
            return (stiffness, damping, ITotal);
        }

        void ClampAngularVel()
        {
            float maxAngularVelDeg = (settings.maxTipSpeed / Mathf.Max(bambooLength, 1e-4f)) * Mathf.Rad2Deg;
            angularVel = Mathf.Clamp(angularVel, -maxAngularVelDeg, maxAngularVelDeg);
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass, Vector2 riderVelocity, bool isCharging)
        {
            Vector2 arm = riderWorldPos - pivotWorldPos;

            bool firstAttach = !riderAttached;
            riderAttached = true;
            lastRiderChargingFlag = isCharging;
            lastImpulseFixedTime = Time.fixedTime;
            lastRiderImpulseDeg = 0f;
            lastRiderImpulseKind = 0;

            // Charging must not move the stalk. Refreshing currentRiderArmSqr from the drifting slime
            // wobbles ITotal and PARAMETRICALLY PUMPS the swing (peak bend grew with zero impulse);
            // dropping the rider weight instead lurches it as the static sag releases. So freeze both.
            if (!firstAttach && isCharging)
            {
                Vector2 frozenArm = lastRiderWorldPos - pivotWorldPos;
                Vector2 frozenWeight = Physics2D.gravity * currentRiderMass;
                pendingRiderTorque += frozenArm.x * frozenWeight.y - frozenArm.y * frozenWeight.x;
                return;
            }

            currentRiderMass = mass;
            currentRiderArmSqr = arm.sqrMagnitude;
            lastRiderWorldPos = riderWorldPos;

            if (firstAttach)
            {
                lastRiderImpulseKind = 1;
                // Divided by STALK inertia, not the combined: inelastic capture into I + m*arm^2 sank
                // two thirds of the momentum into swinging the RIDER. Relative to surfaceVel so matching
                // the surface injects zero; Attach runs before SlimeController zeroes riderVelocity.
                Vector2 surfaceVel = new Vector2(-arm.y, arm.x) * (angularVel * Mathf.Deg2Rad);
                // landingImpulseGain is height-weighted (quadratic in contact height): a low landing
                // stays purely physical so the tip does not wave, a tip landing gets the full bonus.
                float heightT = Mathf.Clamp01(arm.magnitude / bambooLength);
                float landingGain = Mathf.Lerp(1f, settings.landingImpulseGain, heightT * heightT);

                lastRiderImpulseDeg = InjectImpactImpulse(arm, riderVelocity - surfaceVel, mass * landingGain);

                // Fresh swing: the throw gate measures this landing's bend, not the stalk's prior motion.
                peakBendSinceAttach = 0f;
                returningFromPeak = false;
                prevAngularVel = angularVel;
            }
            // No per-frame rider impulse: dOmega = (riderVel - prevRiderVel)*mass/ITotal fed the
            // stalk's own carry (omega x arm) back into itself. Landing is the only rider momentum.

            // Rider weight: zero on a vertical stalk (arm.x = 0), real static sag on a tilted one.
            Vector2 weight = Physics2D.gravity * mass;
            pendingRiderTorque += arm.x * weight.y - arm.y * weight.x;
        }

        /// <summary>Impact at moment arm `arm` into angular impulse; returns the delta-omega armed
        /// (deg/s). Armed as a RAMP — instant teleports the surface out from under an attaching slime.</summary>
        float InjectImpactImpulse(Vector2 arm, Vector2 relVel, float mass)
        {
            float relSpeed = relVel.magnitude;
            if (settings.impactSoftCap > 0f && relSpeed > 1e-4f)
            {
                float compressed = settings.impactSoftCap * Mathf.Log(1f + relSpeed / settings.impactSoftCap);
                relVel *= compressed / relSpeed;
            }

            Vector2 impulse = relVel * mass;
            float inertia = StalkInertia();
            if (inertia <= 1e-6f) return 0f;

            float dOmegaDeg = (arm.x * impulse.y - arm.y * impulse.x) / inertia * Mathf.Rad2Deg;
            rampOmegaRemainingDeg += dOmegaDeg;
            rampOmegaRateDeg = rampOmegaRemainingDeg / LandingImpulseRampTime;
            return dOmegaDeg;
        }

        /// <summary>Dead weight resting on this stalk; call every FixedUpdate, stop calling to remove.
        /// NOT a rider — the one-deep rider slot is the slime's, and a load taking it eats its landing kick.</summary>
        public void ApplyExternalLoad(Vector2 worldPoint, float mass)
        {
            if (backgroundOnly || mass <= 0f) return;
            Vector2 arm = worldPoint - pivotWorldPos;
            Vector2 weight = Physics2D.gravity * mass;
            pendingRiderTorque += arm.x * weight.y - arm.y * weight.x;
            pendingExternalInertia += mass * arm.sqrMagnitude;
        }

        /// <summary>Impact from something that is NOT the rider — a fallen pole handing over the slime.
        /// The rider contract cannot carry it: the landing kick only fires on the firstAttach edge, and
        /// the pole holds the rider slot the whole time it rests here.</summary>
        public void ApplyExternalImpact(Vector2 worldPoint, Vector2 velocity, float mass)
        {
            if (backgroundOnly || mass <= 0f) return;
            Vector2 arm = worldPoint - pivotWorldPos;
            Vector2 surfaceVel = new Vector2(-arm.y, arm.x) * (angularVel * Mathf.Deg2Rad);
            InjectImpactImpulse(arm, velocity - surfaceVel, mass);
        }

        public override void RemoveRider()
        {
            // No impulse on detach by design: however the rider leaves, the stalk keeps its own I*omega.
            // A Newton-third-law launch recoil was tried and removed — it kicked the stalk off its
            // natural swing, and the throw must look the same whether or not jump was pressed.
            riderAttached = false;

            RegisterRelandLockout();
        }

        /// <summary>SurfaceProbe skips these colliders for the FULL relandLockoutTime while physics stays
        /// ON — stock launch clearance releases on separation, and the whip-back outruns a weak jump.</summary>
        void RegisterRelandLockout()
        {
            if (backgroundOnly) return;
            if (settings.relandLockoutTime <= 0f) return;

            var slime = ResolveSlime();
            if (slime == null) return;

            if (segmentTransforms == null || segmentTransforms.Length != segmentCount) CacheChildren();
            for (int i = 0; i < segmentCount; i++)
            {
                var seg = segmentTransforms[i];
                if (seg == null) continue;
                var col = seg.GetComponent<Collider2D>();
                if (col != null) slime.RegisterReattachGuard(col, settings.relandLockoutTime);
            }
        }

        // --- ODE --------------------------------------------------------------------

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            if (ejectLockoutTimer > 0f) ejectLockoutTimer -= dt;

            // Commit external inertia BEFORE any ComputeSpringCoeffs and keep it for the whole frame:
            // OnAfterMotion re-derives the coefficients, and zeroing here would pair a pole-free
            // stiffness with a lastWeightTorque that still carries the pole — inside the throw gate.
            externalInertia = pendingExternalInertia;
            pendingExternalInertia = 0f;

            // Drain the landing-impulse ramp (see LandingImpulseRampTime).
            if (rampOmegaRemainingDeg != 0f)
            {
                float step = rampOmegaRateDeg * dt;
                if (Mathf.Abs(step) >= Mathf.Abs(rampOmegaRemainingDeg)) step = rampOmegaRemainingDeg;
                angularVel += step;
                rampOmegaRemainingDeg -= step;
                ClampAngularVel();
            }

            var (stiffness, damping, ITotal) = ComputeSpringCoeffs();

            float angleRad = currentAngle * Mathf.Deg2Rad;
            float angVelRad = angularVel * Mathf.Deg2Rad;

            float springTorque = -stiffness * angleRad;
            float dampingTorque = -damping * angVelRad;

            // One spring / cap / damping ratio whether or not a rider is attached. v1 swapped all
            // three the frame the rider left, snapping to the smaller cap and wiping the swing energy.
            float angularAccelRad = (springTorque + dampingTorque + pendingRiderTorque) / Mathf.Max(ITotal, 1e-6f);
            lastAngularAccelRad = angularAccelRad;

            angularVel += angularAccelRad * Mathf.Rad2Deg * dt;
            currentAngle += angularVel * dt;

            ClampAngularVel();

            // Swing bookkeeping here, not in the detector, so it stays correct on rider-less frames.
            float absAngle = Mathf.Abs(currentAngle);
            if (absAngle > peakBendSinceAttach)
            {
                peakBendSinceAttach = absAngle;
                peakAngleSigned = currentAngle;
            }
            // Stage 1 of the throw: angular velocity reversing sign IS the peak of the bend.
            if (!returningFromPeak && angularVel * prevAngularVel < 0f) returningFromPeak = true;

            float maxAngle = settings.maxDeflectionDeg;
            if (currentAngle > maxAngle)
            {
                currentAngle = maxAngle;
                if (angularVel > 0f) angularVel = 0f;
            }
            else if (currentAngle < -maxAngle)
            {
                currentAngle = -maxAngle;
                if (angularVel < 0f) angularVel = 0f;
            }

            lastWeightTorque = pendingRiderTorque;   // diagnostic snapshot before reset
            pendingRiderTorque = 0f;

            // Pivot and root rotation stay fixed — the bend lives in the child segment transforms.
            return (pivotWorldPos, baseRotation);
        }

        protected override void OnAfterMotion(float dt)
        {
            ApplySegmentTransforms();
            PushVisualBendProperties();
            CheckSpringThrow();
            if (!backgroundOnly)
                underhang.Tick(ResolveSlime(), transform, settings.underhangClingTime,
                    settings.relandLockoutTime, ThrowCapable, dt);
            prevAngularVel = angularVel;
        }

        /// <summary>Peak bend vs ejectBendDeg splits hard hit (throw) from soft landing (perch). Fires at
        /// HALF-RETURN, not the |omega| turnover — there the tip is fastest and SurfaceProbe sheds the rider
        /// first. Attached only. Order -100 runs before SlimeController (0), so the throw lands this frame.</summary>
        void CheckSpringThrow()
        {
            if (backgroundOnly) return;
            if (!riderAttached) return;
            if (ejectLockoutTimer > 0f) return;
            if (settings.ejectBendDeg <= 0f) return;
            if (!returningFromPeak) return;

            // Below the threshold this swing is spent: rearm and stay an ordinary platform.
            if (peakBendSinceAttach < settings.ejectBendDeg)
            {
                returningFromPeak = false;
                peakBendSinceAttach = 0f;
                return;
            }

            // Half-return fire point; equilibrium theta_eq = weight torque / stiffness.
            var (stiffness, _, _) = ComputeSpringCoeffs();
            float eqDeg = stiffness > 1e-4f ? (lastWeightTorque / stiffness) * Mathf.Rad2Deg : 0f;
            float swingRange = Mathf.Abs(peakAngleSigned - eqDeg);
            if (swingRange < 1e-3f) return;
            if (Mathf.Abs(currentAngle - eqDeg) > swingRange * 0.5f) return;

            var slime = ResolveSlime();
            if (slime == null) return;
            if (slime.CurrentState != SlimeController.SlimeState.Attached) return;
            // Must be the SLIME's arm: the rider slot can be held by a fallen pole, which would
            // throw along the wrong arm.
            if (!TryGetSlimeArm(out Vector2 arm)) return;

            float omega = angularVel * Mathf.Deg2Rad;
            Vector2 ejectVel = new Vector2(-arm.y, arm.x) * omega;

            // Peak gate passed on the high-water mark but the CURRENT omega cannot carry the slime:
            // rearm, else it "throws" at ~0, whips back, recatches, and loops forever while aborting
            // the player's charge.
            if (ejectVel.magnitude < MinEjectSpeed)
            {
                returningFromPeak = false;
                peakBendSinceAttach = 0f;
                return;
            }

            returningFromPeak = false;
            peakBendSinceAttach = 0f;

            ejectLockoutTimer = PostEjectLockout;
            // Clears the carrier (calls back into RemoveRider), registers the launch-clearance ignore
            // so the slime cannot re-stick, and flips to Airborne. Same entry point JumpPad uses.
            slime.ForceAirborneWithVelocity(ejectVel);
        }

        /// <summary>BambooV3_Surface lifts the ceiling limit (ceilingAngle 180) so a slime can attach from
        /// below, which also kills SlimeController's ceiling detach — grip holds while ThrowCapable, then slips.</summary>
        public struct UnderhangSlip
        {
            float clingSeconds;

            /// <summary>Per-FixedUpdate tick. Returns true on the frame it dropped the slime.</summary>
            public bool Tick(SlimeController slime, Transform pole, float clingTime,
                float lockoutTime, bool throwCapable, float dt)
            {
                // Charging is exempt: a charge must end in the player's own launch, never in a slip.
                if (throwCapable || !IsHangingUnder(slime, pole))
                {
                    clingSeconds = 0f;
                    return false;
                }

                clingSeconds += dt;
                if (clingSeconds < clingTime) return false;
                clingSeconds = 0f;
                Release(slime, pole, lockoutTime);
                return true;
            }

            static bool IsHangingUnder(SlimeController slime, Transform pole)
            {
                if (slime == null || pole == null) return false;
                if (slime.CurrentState != SlimeController.SlimeState.Attached) return false;
                if (Vector2.Dot(slime.SurfaceNormal, slime.GravityUp) >= 0f) return false;
                var surf = slime.SurfaceCollider;
                return surf != null && surf.transform.IsChildOf(pole);
            }

            // Guard the WHOLE pole: releasing one segment just hands the slime to the next capsule.
            static void Release(SlimeController slime, Transform pole, float lockoutTime)
            {
                if (lockoutTime > 0f)
                {
                    var cols = pole.GetComponentsInChildren<Collider2D>();
                    for (int i = 0; i < cols.Length; i++) slime.RegisterReattachGuard(cols[i], lockoutTime);
                }
                slime.ForceAirborneWithVelocity(slime.Velocity);
            }
        }

        // --- Centerline (must match BambooBend.shader exactly) -----------------------

        // Positive currentAngle = CCW around +Z, shape h(t)=t, so the centerline is an arc of
        // radius L/bendRad: x(t) = (L/bendRad)*(cos(bendRad*t) - 1), y(t) = (L/bendRad)*sin(bendRad*t)
        Vector2 ComputeCenterlinePoint(float t)
        {
            float bendRad = currentAngle * Mathf.Deg2Rad;
            if (Mathf.Abs(bendRad) < 1e-4f)
                return new Vector2(0f, t * bambooLength);

            float invK = bambooLength / bendRad;
            float u = bendRad * t;
            return new Vector2(invK * (Mathf.Cos(u) - 1f), invK * Mathf.Sin(u));
        }

        Vector2 ComputeCenterlineTangent(float t)
        {
            float bendRad = currentAngle * Mathf.Deg2Rad;
            if (Mathf.Abs(bendRad) < 1e-4f)
                return Vector2.up;

            float u = bendRad * t;
            return new Vector2(-Mathf.Sin(u), Mathf.Cos(u)).normalized;
        }

        float ClosestCenterlineT(Vector2 localPoint)
        {
            int samples = Mathf.Max(16, segmentCount * 4);
            float bestT = 0f;
            float bestSqr = float.PositiveInfinity;
            Vector2 prev = ComputeCenterlinePoint(0f);

            for (int i = 0; i < samples; i++)
            {
                float t0 = (float)i / samples;
                float t1 = (float)(i + 1) / samples;
                Vector2 next = ComputeCenterlinePoint(t1);
                Vector2 seg = next - prev;
                float segSqr = seg.sqrMagnitude;
                float u = segSqr > 1e-8f
                    ? Mathf.Clamp01(Vector2.Dot(localPoint - prev, seg) / segSqr)
                    : 0f;
                Vector2 closest = prev + seg * u;
                float sqr = (localPoint - closest).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestT = Mathf.Lerp(t0, t1, u);
                }
                prev = next;
            }

            return Mathf.Clamp01(bestT);
        }

        // Each collider segment is the chord between consecutive centerline samples, so the
        // polyline approximates the same curve the shader bends to.
        void ApplySegmentTransforms()
        {
            if (segmentTransforms == null || segmentTransforms.Length != segmentCount) CacheChildren();
            if (segmentTransforms == null || segmentTransforms.Length == 0) return;

            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 pStart = ComputeCenterlinePoint((float)i / segmentCount);
                Vector2 pEnd = ComputeCenterlinePoint((float)(i + 1) / segmentCount);
                Vector2 chord = pEnd - pStart;

                // Signed angle from +Y, CCW around +Z: rotating +Y by a gives (-sin a, cos a).
                float chordAngleDeg = Mathf.Atan2(-chord.x, chord.y) * Mathf.Rad2Deg;
                Vector2 center = (pStart + pEnd) * 0.5f;

                var seg = segmentTransforms[i];
                if (seg != null)
                {
                    seg.localPosition = new Vector3(center.x, center.y, 0f);
                    seg.localRotation = Quaternion.Euler(0f, 0f, chordAngleDeg);
                }
            }
        }

        // --- Analytic surface anchor -------------------------------------------------

        /// <summary>Snap source of truth on this stalk: SurfaceProbe's ClosestPoint pass jitters across
        /// the overlapping capsule caps, so resolve against the continuous centerline instead.</summary>
        public bool TryGetSurfaceAnchor(Collider2D sourceCollider, Vector2 riderWorldPos, float riderRadius,
            out SurfaceAnchorInfo anchorInfo)
        {
            anchorInfo = default;
            if (backgroundOnly) return false;
            if (sourceCollider == null) return false;
            if (!sourceCollider.enabled) return false;
            if (!sourceCollider.gameObject.activeInHierarchy) return false;

            Vector2 local = transform.InverseTransformPoint(riderWorldPos);
            float t = ClosestCenterlineT(local);
            Vector2 center = ComputeCenterlinePoint(t);
            Vector2 outward = local - center;
            if (outward.sqrMagnitude < 1e-8f)
            {
                Vector2 tangent = ComputeCenterlineTangent(t);
                outward = new Vector2(tangent.y, -tangent.x);
            }

            outward.Normalize();
            Vector2 anchorLocal = center + outward * (segmentRadius + Mathf.Max(0f, riderRadius));

            Vector2 anchorWorldPos = transform.TransformPoint(anchorLocal);
            Vector2 normalWorld = ((Vector2)transform.TransformVector(outward)).normalized;
            Vector2 tangentWorld = new Vector2(-normalWorld.y, normalWorld.x);
            // consumesPlatformCarry: the anchor is already in the CURRENT bent pose, so the carrier's
            // omega x arm on top would double-correct on a fast-swinging stalk.
            anchorInfo = new SurfaceAnchorInfo(anchorWorldPos, normalWorld, tangentWorld, true, name);
            return true;
        }

        // --- Visual ------------------------------------------------------------------

        void PushVisualBendProperties()
        {
            if (visualRenderer == null) return;
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

            visualRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(BendAngleId, currentAngle);
            propertyBlock.SetFloat(BambooLengthId, bambooLength);
            visualRenderer.SetPropertyBlock(propertyBlock);
        }

        // Rings per stalk length, driven off segmentCount so scars coincide with collider joints.
        float VisibleNodeFrequency() => Mathf.Max(1, segmentCount);

        int EffectiveVisualHeightSegments()
        {
            int safeSegmentCount = Mathf.Max(1, segmentCount);
            int targetHeightSegments = Mathf.Max(visualHeightSegments, safeSegmentCount * 4);
            int rowsPerColliderSegment = Mathf.CeilToInt((float)targetHeightSegments / safeSegmentCount);
            return safeSegmentCount * Mathf.Max(1, rowsPerColliderSegment);
        }

        void ApplyMaterialParams(Material mat, float bendAngleDeg)
        {
            mat.SetFloat(BambooLengthId, bambooLength);
            mat.SetFloat(BendAngleId, bendAngleDeg);
            mat.SetColor(BambooColorBaseId, stalkColorBase);
            mat.SetColor(BambooColorTipId, stalkColorTip);
            mat.SetFloat(ShadowDarkenId, shadowDarken);
            mat.SetColor(ScarColorId, scarColor);
            mat.SetFloat(NodeFrequencyId, VisibleNodeFrequency());
            mat.SetFloat(BulgeAmountId, bulgeAmount);
            mat.SetFloat(BulgeWidthId, bulgeWidth);
            mat.SetFloat(ScarHalfWidthId, scarHalfWidth);
            mat.SetFloat(ScarSoftnessId, scarSoftness);
            mat.SetFloat(ScarColorCurveId, scarColorCurve);
            mat.SetFloat(ScarDepthId, scarDepth);
            mat.SetFloat(ToonThresholdId, toonThreshold);
            mat.SetFloat(ToonSoftnessId, toonSoftness);
            mat.SetFloat(RimId, rimStrength);
            mat.SetFloat(RimPowerId, rimPower);
            mat.SetVector(LightDirWSId, new Vector4(lightDirWS.x, lightDirWS.y, lightDirWS.z, 0f));
            mat.SetFloat(BaseMapStrengthId, baseMapStrength);
            if (bambooBarkTexture != null) mat.SetTexture(BaseMapId, bambooBarkTexture);
        }

#if UNITY_EDITOR
        // Inspector edits bypass RebuildSegments, so push material params directly. Mesh geometry
        // (length, counts, radius, shape) still needs Rebuild Segments — mesh and colliders are baked.
        void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += DeferredRefresh;
        }

        void DeferredRefresh()
        {
            if (this == null) return;
            RefreshVisualMaterialFromFields();
        }

        [ContextMenu("Refresh Visual Material")]
        public void RefreshVisualMaterialFromFields()
        {
            var visualChild = transform.Find(VisualChildName);
            if (visualChild == null)
            {
                Debug.LogWarning($"[BambooPlatformV3] '{VisualChildName}' child not found on {name}. Click 'Rebuild Segments' first.", this);
                return;
            }
            var mr = visualChild.GetComponent<MeshRenderer>();
            if (mr == null)
            {
                Debug.LogWarning($"[BambooPlatformV3] Visual MeshRenderer missing on {name}.", this);
                return;
            }
            // .material in play mode (safe instance), .sharedMaterial in edit mode so an
            // asset the designer wired into bendMaterial is written asset-safely.
            var mat = Application.isPlaying ? mr.material : mr.sharedMaterial;
            if (mat == null)
            {
                Debug.LogWarning($"[BambooPlatformV3] No material on visual renderer for {name}.", this);
                return;
            }

            ApplyMaterialParams(mat, Application.isPlaying ? currentAngle : 0f);

            UnityEditor.EditorUtility.SetDirty(mat);
            UnityEditor.SceneView.RepaintAll();
        }
#endif

        // --- Build -------------------------------------------------------------------

        [ContextMenu("Rebuild Segments")]
        public void RebuildSegments()
        {
            EnsureSettings();
            EnsureSurfaceTag();
            EnsureRigidbody();
            DestroyExistingChildren();
            BuildColliderSegments();
            BuildVisual();
            CacheChildren();
        }

        void EnsureRigidbody()
        {
            if (GetComponent<Rigidbody2D>() == null)
            {
                var newRb = gameObject.AddComponent<Rigidbody2D>();
                newRb.bodyType = RigidbodyType2D.Kinematic;
            }
        }

        void DestroyExistingChildren()
        {
            // Match Unity's "(N)" duplicate suffix too: Ctrl+D renames "Visual" to "Visual (1)",
            // and an exact-string match would leak it into a second Visual.
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child.name.StartsWith("Segment_")
                    || child.name == VisualChildName
                    || child.name.StartsWith(VisualChildName + " ("))
                    DestroyChild(child.gameObject);
            }
        }

        static void DestroyChild(GameObject go)
        {
#if UNITY_EDITOR
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
#else
            Destroy(go);
#endif
        }

        void BuildColliderSegments()
        {
            if (backgroundOnly) return;

            float segLen = bambooLength / segmentCount;
            float colliderLen = segLen + Mathf.Min(colliderSegmentOverlap, segLen * 0.45f);

            for (int i = 0; i < segmentCount; i++)
            {
                var seg = new GameObject(SegmentName(i));
                seg.transform.SetParent(transform, false);
                seg.layer = gameObject.layer;
                seg.transform.localPosition = new Vector3(0f, (i + 0.5f) * segLen, 0f);
                seg.transform.localRotation = Quaternion.identity;

                if (colliderShape == SegmentColliderShape.Capsule)
                {
                    var col = seg.AddComponent<CapsuleCollider2D>();
                    col.direction = CapsuleDirection2D.Vertical;
                    col.size = new Vector2(segmentRadius * 2f, colliderLen);
                    col.offset = Vector2.zero;
                }
                else
                {
                    var col = seg.AddComponent<BoxCollider2D>();
                    col.size = new Vector2(segmentRadius * 2f, colliderLen);
                    col.offset = Vector2.zero;
                }
            }
        }

        void BuildVisual()
        {
            var visual = new GameObject(VisualChildName);
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            visual.layer = gameObject.layer;

            var mf = visual.AddComponent<MeshFilter>();
            mf.sharedMesh = BambooStalkMesh.Build(visualRadialSegments, EffectiveVisualHeightSegments(),
                                                  segmentRadius, bambooLength);

            var mr = visual.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = ResolveBendMaterial();
        }

        Material ResolveBendMaterial()
        {
            if (bendMaterial != null) return bendMaterial;

            var shader = Shader.Find("SlimeJumping/BambooBend");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "BambooBend V3 (Auto)" };
            // No MaterialPropertyBlock runs outside play mode, so push params at creation for the
            // edit-mode rest pose.
            ApplyMaterialParams(mat, 0f);
            return mat;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 0.3f, 0.9f);
            Vector3 basePos = Application.isPlaying ? (Vector3)pivotWorldPos : transform.position;
            Gizmos.DrawSphere(basePos, 0.12f);
            Gizmos.DrawLine(basePos, basePos + transform.up * bambooLength);
        }
    }
}
