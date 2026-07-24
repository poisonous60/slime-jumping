using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;
using SlimeJumping.Settings;

namespace SlimeJumping.Platforms
{
    // Bamboo V3 — a springboard, not a wall.
    //
    // Same geometry story as BambooPlatform (v1): one Rigidbody2D at the base, N
    // collider-only child segments approximating a circular-arc cantilever, and a single
    // procedural cylinder bent by SlimeJumping/BambooBend using the SAME closed-form curve
    // so the visual and the collider stay sub-pixel coincident.
    //
    // What is different is everything above the geometry. v1 layered seven non-physical
    // corrections on the cantilever ODE (impact-speed curves, contact-height shaping on top
    // of the moment arm that already encodes height, a second spring set that swapped in the
    // frame the rider left, two decay rates outside the damping ratio, tangential projection
    // of the impact, a launch-recoil scale, a charge-power bonus). They fought each other:
    // hitting a tilted stalk from below produced literally zero bend because the impact
    // projection threw away everything parallel to the stalk, and launching deleted the
    // stalk's swing energy because the deflection cap collapsed from 130 deg to 30 deg the
    // instant riderAttached went false.
    //
    // V3 keeps ONE honest ODE and three real impulses:
    //   weight   tau = r x (m*g)                 every frame the rider is on
    //   landing  dOmega = (r x m*v_impact) / I   the slime's momentum is absorbed
    //   push-off dOmega = (r x -m*v_launch) / I  Newton's third law
    // No projections, no gain curves, no height shaping — the cross product already knows
    // the angle of incidence and the moment arm already knows the contact height.
    //
    // The one non-physical rule is adhesion: a sticky slime would never be thrown off a
    // spring, so the stalk breaks its grip when the surface acceleration at the contact
    // point exceeds adhesionBreakAccel, and hands it the surface velocity. That single
    // threshold is what splits "hard hit -> automatic launch" from "soft landing -> perch".
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
        // Visible node rings always track segmentCount: shader scars sit at t = k/segmentCount,
        // the same parameter the Segment_XX colliders split on, so rings land on the joints.
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

        // Lockout after we throw the slime off, so the stalk (still swinging hard) cannot
        // immediately re-break a grip the slime has not really had time to form.
        // ForceAirborneWithVelocity already registers a launch-clearance collider ignore;
        // this only guards the adhesion test itself. Not a designer knob.
        const float PostEjectLockout = 0.15f;

        Vector2 pivotWorldPos;
        float baseRotation;
        float currentAngle;      // deg, tangent angle at the tip of the arc
        float angularVel;        // deg/s
        float lastAngularAccelRad;
        float pendingRiderTorque;

        // Throw detector state. peakBendSinceAttach is the largest |currentAngle| this swing
        // reached; returningFromPeak flips once angular velocity reverses (the stalk has
        // stopped bending outward); prevAngularVel is the previous frame's value, used to
        // spot the |omega| turnover that marks maximum tip speed.
        float peakBendSinceAttach;
        bool returningFromPeak;
        float prevAngularVel;

        bool riderAttached;
        float currentRiderMass;
        float currentRiderArmSqr;
        Vector2 lastRiderWorldPos;
        Vector2 previousRiderVel;

        // Departure snapshot, armed by RemoveRider and cashed in by HandleSlimeLaunch if
        // SlimeController.OnLaunch fires in the SAME frame. recoilFrame = -1 means unarmed.
        int recoilFrame = -1;
        Vector2 recoilArm;
        float recoilMass;
        Vector2 recoilRiderVel;
        float recoilOmegaRad;

        float ejectLockoutTimer;
        // True only while WE are calling ForceAirborneWithVelocity. The RemoveRider that
        // call triggers must not also apply the Newton-third-law recoil: an adhesion break
        // hands the slime the surface velocity it was already moving at, it does not push.
        bool selfEjecting;

        SlimeController cachedSlime;
        SlimeController launchSubscribedSlime;

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

        // Read-only views of state the stalk already keeps, so a diagnostic can pull the
        // whole contact -> impulse -> bend -> throw chain without this component pushing
        // anything to it.
        /// <summary>True once ApplyRiderInfluence has run for a rider and before RemoveRider.
        /// If this is false while the slime is visibly sitting on the stalk, the carrier never
        /// attached and NO impulse can ever reach the spring.</summary>
        public bool RiderAttached => riderAttached;
        public Vector2 LastRiderWorldPos => lastRiderWorldPos;
        public Vector2 PivotWorldPos => Application.isPlaying ? pivotWorldPos : (Vector2)transform.position;
        /// <summary>Largest |bend| reached since the rider landed — what the throw gate tests.</summary>
        public float PeakBendSinceAttach => peakBendSinceAttach;
        /// <summary>Stage 1 of the throw: the stalk has passed its peak and is springing back.</summary>
        public bool ReturningFromPeak => returningFromPeak;
        public float EjectLockoutTimer => ejectLockoutTimer;
        public float EjectBendDeg => settings != null ? settings.ejectBendDeg : 0f;
        public float NaturalFreqHz => settings != null ? settings.naturalFreqHz : 0f;
        public float DampingRatio => settings != null ? settings.dampingRatio : 0f;
        public bool HasSettingsAsset => settings != null;
        public bool BackgroundOnly => backgroundOnly;
        /// <summary>Rotational inertia of the stalk alone — the number that decides how much a
        /// given impact impulse actually bends it.</summary>
        public float StalkInertiaDebug => rb != null ? StalkInertia() : 0f;

        /// <summary>Rest axis of the stalk in world space (root local +Y). The root
        /// rigidbody holds the authored tilt and never rotates at runtime — bend lives in
        /// the child segments — so this is the stalk's "vertical" whatever Z rotation the
        /// designer gave it.</summary>
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
            pivotWorldPos = rb.position;
            baseRotation = rb.rotation;
            currentAngle = 0f;
            angularVel = 0f;
            CacheChildren();
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

        /// <summary>
        /// Subscribes to OnLaunch the first time a rider actually shows up, rather than in
        /// OnEnable: a stalk that never carries anyone never needs the event, and this way
        /// the slime is guaranteed to exist by the time we look for it (no scene-load
        /// ordering assumption). Same pattern ThinIcePlatformV2 uses for the launch cue.
        /// </summary>
        void EnsureLaunchSubscription()
        {
            if (launchSubscribedSlime != null) return;

            var slime = ResolveSlime();
            if (slime == null) return;

            slime.OnLaunch += HandleSlimeLaunch;
            launchSubscribedSlime = slime;
        }

        void OnEnable()
        {
            // riderAttached survives a disable, so restore the subscription right away
            // instead of waiting for the next carrier tick to call EnsureLaunchSubscription.
            if (riderAttached) EnsureLaunchSubscription();
        }

        void OnDisable()
        {
            if (launchSubscribedSlime != null)
            {
                launchSubscribedSlime.OnLaunch -= HandleSlimeLaunch;
                launchSubscribedSlime = null;
            }
            recoilFrame = -1;
        }

        // --- Carrier contract -------------------------------------------------------

        // Pivot is fixed; all motion is the child segments bending, so the base rigidbody
        // reports no linear velocity and PlatformCarrier's omega x arm term does the work.
        public override Vector2 Velocity { get => Vector2.zero; protected set { } }
        public override float AngularVelocity { get => angularVel; protected set { } }

        // Unscaled: a launch inherits exactly the surface velocity at the contact point.
        // The boost ceiling is therefore maxTipSpeed and nothing else — no transfer scale,
        // no falloff gate, no per-height fraction. PlatformMotor's base returns 1f, so no
        // override is needed here; this comment is the record that the omission is intentional.

        public override Vector2 GetRiderCarryDelta(Vector2 riderWorldPos, float dt)
        {
            if (dt <= 0f) return Vector2.zero;

            float radPerSec = angularVel * Mathf.Deg2Rad;
            if (Mathf.Abs(radPerSec) < 0.0001f) return Vector2.zero;

            Vector2 arm = riderWorldPos - pivotWorldPos;
            return new Vector2(-arm.y, arm.x) * radPerSec * dt;
        }

        // --- Rider physics ----------------------------------------------------------

        /// <summary>Second moment of the stalk alone about the base pivot (uniform rod).
        /// This is the inertia that responds when the rider is NOT co-rotating with the
        /// stalk — i.e. immediately after an impulsive separation.</summary>
        float StalkInertia() => rb.mass * bambooLength * bambooLength / 3f;

        (float stiffness, float damping, float ITotal) ComputeSpringCoeffs()
        {
            float ITotal = StalkInertia() + (riderAttached ? currentRiderMass * currentRiderArmSqr : 0f);
            float omega = 2f * Mathf.PI * settings.naturalFreqHz;
            float stiffness = omega * omega * ITotal;
            float damping = 2f * settings.dampingRatio * omega * ITotal;
            return (stiffness, damping, ITotal);
        }

        /// <summary>
        /// Applies a linear impulse delivered at world offset <paramref name="arm"/> from the
        /// pivot. dOmega = (arm x J) / I — the whole of V3's "how hard does this bend the
        /// stalk" logic. The cross product handles the angle of incidence (a hit running
        /// along the stalk genuinely should barely bend it, a broadside hit should bend it a
        /// lot) and |arm| handles the contact height, so neither needs a separate knob.
        ///
        /// <paramref name="inertia"/> is explicit because it differs by case: while the rider
        /// stays coupled to the stalk the pair rotate together (rider inertia included), but
        /// an impulse that separates them leaves the stalk moving alone (stalk inertia only).
        /// </summary>
        void ApplyContactImpulse(Vector2 arm, Vector2 impulse, float inertia)
        {
            if (inertia <= 1e-6f) return;

            float deltaOmegaRad = (arm.x * impulse.y - arm.y * impulse.x) / inertia;
            angularVel += deltaOmegaRad * Mathf.Rad2Deg;
            ClampAngularVel();
        }

        void ClampAngularVel()
        {
            float maxAngularVelDeg = (settings.maxTipSpeed / Mathf.Max(bambooLength, 1e-4f)) * Mathf.Rad2Deg;
            angularVel = Mathf.Clamp(angularVel, -maxAngularVelDeg, maxAngularVelDeg);
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass, Vector2 riderVelocity, bool isCharging)
        {
            Vector2 arm = riderWorldPos - pivotWorldPos;
            currentRiderMass = mass;
            currentRiderArmSqr = arm.sqrMagnitude;
            lastRiderWorldPos = riderWorldPos;

            bool firstAttach = !riderAttached;
            riderAttached = true;
            EnsureLaunchSubscription();

            if (firstAttach)
            {
                // Landing impulse. RELATIVE velocity against STALK inertia — deliberately the
                // exact same form as the push-off recoil in HandleSlimeLaunch, so arriving at
                // a given speed bends the stalk as hard as leaving at that speed does.
                //
                // The first version conserved angular momentum into the combined inertia,
                // w' = (I_stalk*w + arm x m*v) / (I_stalk + m*arm^2). That is the textbook
                // perfectly-inelastic capture and it is not wrong, but it is maximally lossy:
                // measured on a 7 m stalk with the rider 5.68 m out, m*arm^2 = 32.3 against
                // I_stalk = 16.33, so two thirds of the slime's momentum went into swinging
                // the RIDER around the pivot instead of bending the stalk. A 22 m/s landing
                // produced 12.8 deg of bend while a launch off the same stalk drove the tip to
                // 24.8 m/s. That asymmetry is the whole "it only shakes after I jump" problem.
                //
                // Using the relative velocity is what keeps this honest at I_stalk. Dividing
                // the raw impact by I_stalk would let a slime that lands moving WITH an
                // already-swinging stalk speed it up further — free energy. Measured against
                // the surface velocity it is already moving at, matching the surface delivers
                // exactly zero impulse, and meeting it head-on delivers more. No free energy,
                // and no rider-inertia tax on the bend.
                //
                // PlatformCarrier.Attach runs before SlimeController zeroes the slime's
                // velocity (SlimeController.Airborne.cs), so riderVelocity here is the real
                // impact velocity.
                Vector2 surfaceVel = new Vector2(-arm.y, arm.x) * (angularVel * Mathf.Deg2Rad);
                ApplyContactImpulse(arm, (riderVelocity - surfaceVel) * mass, StalkInertia());

                // Fresh swing: this landing's bend is what the throw gate measures, not
                // whatever the stalk was already doing before the slime arrived.
                peakBendSinceAttach = 0f;
                returningFromPeak = false;
                prevAngularVel = angularVel;
            }
            else
            {
                // Already coupled: only the rider's frame-to-frame momentum change is an
                // impulse, and the pair rotate together, so the combined inertia responds.
                var (_, _, ITotal) = ComputeSpringCoeffs();
                ApplyContactImpulse(arm, (riderVelocity - previousRiderVel) * mass, ITotal);
            }

            // Baseline from zero after a first attach: the slime's velocity is about to be
            // zeroed, and measuring that as a phantom -v next tick would cancel the landing
            // impulse we just applied.
            previousRiderVel = firstAttach ? Vector2.zero : riderVelocity;

            // Rider weight. Zero on a vertical stalk (arm.x = 0), a real static sag on a
            // tilted one — which is exactly the "it droops a little when I stand on it"
            // read that requirement 3 wants from a soft landing.
            Vector2 weight = Physics2D.gravity * mass;
            pendingRiderTorque += arm.x * weight.y - arm.y * weight.x;
        }

        public override void RemoveRider()
        {
            // No impulse is applied here, deliberately.
            //
            // An attached slime's `velocity` field does NOT include the surface velocity the
            // carrier is moving it at — carry is positional (PlatformCarrier.ApplyPositionCarry
            // / GetRiderCarryDelta), and the tangent slide is what lands in `velocity`
            // (SlimeController.Attached.cs). Momentum bookkeeping taken at detach time is
            // therefore lossy, and worse, it cannot tell a player launch apart from an
            // ordinary decouple: sliding onto a neighbouring surface, a hazard hit (which
            // detaches BEFORE assigning knockback), a teleport. Guessing from the velocity
            // magnitude gets it wrong in both directions — it either bleeds most of the swing
            // on every launch or feeds the swing on every slide-off, which is free energy.
            //
            // So we only snapshot here, and let SlimeController.OnLaunch decide. That event is
            // raised only by the player's charge launch (SlimeController.Launch.cs) and never
            // by ForceAirborneWithVelocity, so an ordinary decouple simply never fires it and
            // the stalk correctly keeps its omega: when the rider's inertia stops being
            // coupled, the stalk's own I*omega is unchanged.
            if (!selfEjecting && riderAttached && currentRiderMass > 0f)
            {
                recoilFrame = Time.frameCount;
                recoilArm = lastRiderWorldPos - pivotWorldPos;
                recoilMass = currentRiderMass;
                recoilRiderVel = previousRiderVel;
                recoilOmegaRad = angularVel * Mathf.Deg2Rad;
            }

            riderAttached = false;
        }

        /// <summary>
        /// Newton's third law for a player launch off this stalk. Raised from Launch() after
        /// carrier.Detach(), in the same frame RemoveRider armed the snapshot — the frame
        /// check is what keeps a later launch off some other platform from cashing in a stale
        /// one.
        /// </summary>
        void HandleSlimeLaunch(Vector2 launchPos, Vector2 launchVel, bool wasHop)
        {
            if (Time.frameCount != recoilFrame) return;
            recoilFrame = -1;
            if (recoilMass <= 0f) return;

            // The rider's TRUE pre-launch velocity is its tangent slide plus the surface
            // velocity it was being carried at. Only the difference is the player's push:
            // Launch hands the omega x arm term back as carrier inheritance, and charging the
            // stalk for momentum that was already its own would take a large bite out of the
            // swing on every launch (about 75% on a 5 m stalk) — the same "launching kills
            // the recoil" failure V3 exists to remove, just arriving by another route.
            Vector2 riderVelBefore = recoilRiderVel + new Vector2(-recoilArm.y, recoilArm.x) * recoilOmegaRad;
            Vector2 impulse = -(launchVel - riderVelBefore) * recoilMass;
            // Stalk inertia only: this impulse is what SEPARATES the two, so the stalk is
            // moving alone the instant after. Using the combined inertia would understate the
            // recoil by I_stalk / I_total.
            ApplyContactImpulse(recoilArm, impulse, StalkInertia());
        }

        // --- ODE --------------------------------------------------------------------

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            if (ejectLockoutTimer > 0f) ejectLockoutTimer -= dt;

            var (stiffness, damping, ITotal) = ComputeSpringCoeffs();

            float angleRad = currentAngle * Mathf.Deg2Rad;
            float angVelRad = angularVel * Mathf.Deg2Rad;

            float springTorque = -stiffness * angleRad;
            float dampingTorque = -damping * angVelRad;

            // One spring, one cap, one damping ratio — identical whether or not a rider is
            // attached. v1 swapped all three the frame the rider left, which snapped the
            // stalk to the smaller cap and wiped the swing energy the recoil needed.
            float angularAccelRad = (springTorque + dampingTorque + pendingRiderTorque) / Mathf.Max(ITotal, 1e-6f);
            lastAngularAccelRad = angularAccelRad;

            angularVel += angularAccelRad * Mathf.Rad2Deg * dt;
            currentAngle += angularVel * dt;

            ClampAngularVel();

            // Swing bookkeeping for the throw detector. Tracked here rather than in the
            // detector so it stays correct on frames with no rider.
            float absAngle = Mathf.Abs(currentAngle);
            if (absAngle > peakBendSinceAttach) peakBendSinceAttach = absAngle;
            // Stage 1 of the throw: the tip has stopped bending outward and started coming
            // back. Angular velocity reversing sign IS the peak of the bend.
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

            pendingRiderTorque = 0f;

            // Pivot and root rotation are both fixed — the authored tilt lives in
            // baseRotation and the bend lives in the child segment transforms.
            return (pivotWorldPos, baseRotation);
        }

        protected override void OnAfterMotion(float dt)
        {
            ApplySegmentTransforms();
            PushVisualBendProperties();
            CheckSpringThrow();
            prevAngularVel = angularVel;
        }

        /// <summary>
        /// The one deliberately non-physical rule in V3: a slime sticks to everything, so
        /// left alone it would ride the stalk through the whole swing and never be thrown.
        /// This is what makes the stalk a springboard for a hard hit and a perch for a soft
        /// one, off a single number.
        ///
        /// Two stages, matching what a trampoline actually does:
        ///   1. the stalk bends out and reaches its peak — detected as angular velocity
        ///      reversing sign;
        ///   2. it springs back and the tip reaches MAXIMUM SPEED — detected as |omega|
        ///      turning over, which is the instant it passes its loaded equilibrium.
        /// The throw fires at (2), and the slime leaves with the surface velocity it is
        /// already being carried at, omega x arm. That instant is by construction the
        /// largest throw the stalk can give.
        ///
        /// Gate: the peak bend reached in (1) must exceed ejectBendDeg. A hard hit bends far
        /// and throws; a soft landing never gets there and the stalk stays a platform. The
        /// knob is an ANGLE, which is the thing the player can actually see on screen — the
        /// whole point of the design is that what you see predicts what you get.
        ///
        /// Why not a surface-acceleration threshold (the first attempt): a = r*sqrt(alpha^2 +
        /// omega^4) turns out to be nearly FLAT across the swing — its value right after the
        /// impact is about the same as at the peak. So it does not pick a moment at all: over
        /// the threshold it fires on the landing frame before anything visibly bends, under it
        /// it never fires. There is no middle. Detecting the peak explicitly fixes both ends.
        ///
        /// Stage 2 uses the |omega| turnover rather than a zero-crossing of the angle because
        /// a tilted stalk carrying a rider oscillates about a weight-shifted equilibrium, not
        /// about zero — a small swing might never cross zero at all.
        ///
        /// Runs at PlatformMotor's execution order (-100), before SlimeController's own
        /// FixedUpdate (0), so the slime spends this frame already Airborne with the throw
        /// velocity rather than reacting one frame late.
        /// </summary>
        void CheckSpringThrow()
        {
            if (backgroundOnly) return;
            if (!riderAttached) return;
            if (ejectLockoutTimer > 0f) return;
            if (settings.ejectBendDeg <= 0f) return;
            if (!returningFromPeak) return;

            // Stage 2: |omega| turned over, i.e. the tip just passed its fastest point.
            if (Mathf.Abs(angularVel) >= Mathf.Abs(prevAngularVel)) return;
            if (Mathf.Abs(prevAngularVel) < 1e-4f) return;

            // Gate on how far it actually bent. Below the threshold this swing is spent, so
            // rearm and let the stalk keep oscillating as an ordinary platform.
            if (peakBendSinceAttach < settings.ejectBendDeg)
            {
                returningFromPeak = false;
                peakBendSinceAttach = 0f;
                return;
            }

            Vector2 arm = lastRiderWorldPos - pivotWorldPos;
            float armLen = arm.magnitude;
            if (armLen < 1e-3f) return;

            var slime = ResolveSlime();
            if (slime == null) return;
            if (slime.CurrentState != SlimeController.SlimeState.Attached
                && slime.CurrentState != SlimeController.SlimeState.Charging) return;

            float omega = angularVel * Mathf.Deg2Rad;
            Vector2 ejectVel = new Vector2(-arm.y, arm.x) * omega;

            returningFromPeak = false;
            peakBendSinceAttach = 0f;

            ejectLockoutTimer = PostEjectLockout;
            selfEjecting = true;
            try
            {
                // Clears the carrier (which calls back into RemoveRider), registers the
                // launch-clearance collider ignore so the slime does not re-stick to the
                // stalk it is leaving, and flips the state machine to Airborne. Same public
                // entry point JumpPad / HammerPlatform / JumpBall use.
                //
                // finally, not a plain reset: ForceAirborneWithVelocity fans out to
                // IPlatformEffect.OnSlimeDetach listeners, so a throw anywhere in that tree
                // would otherwise leave selfEjecting stuck true and silently kill every
                // future launch recoil on this stalk.
                slime.ForceAirborneWithVelocity(ejectVel);
            }
            finally
            {
                selfEjecting = false;
            }
        }

        // --- Centerline (must match BambooBend.shader exactly) -----------------------

        // Positive currentAngle = counter-clockwise around +Z. Shape function is hardcoded
        // linear h(t)=t, so the local tangent angle at fraction t is currentAngle*t and the
        // centerline is an arc of radius L/bendRad:
        //   x(t) = (L/bendRad) * (cos(bendRad*t) - 1)
        //   y(t) = (L/bendRad) * sin(bendRad*t)
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
        // polyline approximates the same smooth curve the shader bends to.
        void ApplySegmentTransforms()
        {
            if (segmentTransforms == null || segmentTransforms.Length != segmentCount) CacheChildren();
            if (segmentTransforms == null || segmentTransforms.Length == 0) return;

            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 pStart = ComputeCenterlinePoint((float)i / segmentCount);
                Vector2 pEnd = ComputeCenterlinePoint((float)(i + 1) / segmentCount);
                Vector2 chord = pEnd - pStart;

                // Signed angle from +Y to the chord, CCW around +Z: rotating +Y by a gives
                // (-sin a, cos a), so a = atan2(-chord.x, chord.y).
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

        /// <summary>
        /// Snap source of truth while the slime is on this stalk. SurfaceProbe's default
        /// ClosestPoint pass jitters across the overlapping capsule caps of the polyline;
        /// this resolves against the continuous centerline the shader uses instead.
        /// </summary>
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
            // consumesPlatformCarry: this anchor is already computed against the CURRENT
            // bent pose, so applying PlatformCarrier's omega x arm carry on top would
            // double-correct the position on a fast-swinging stalk.
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

        // Shader _NodeFrequency = rings per stalk length, driven off segmentCount so the
        // visible scar rings coincide with the Segment_XX collider joints.
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
        // Inspector edits do not go through RebuildSegments, so push the material params
        // directly and let colours / scar / bulge / lighting update live. Mesh-geometry
        // fields (length, counts, radius, collider shape) still need Rebuild Segments
        // because the mesh and colliders are baked at creation time.
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
            // Also match Unity's "(N)" duplicate suffix: Ctrl+D renames the child "Visual"
            // to "Visual (1)", and an exact-string match would leak it into a second Visual.
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
            // Push params at creation so the edit-mode rest pose normalises height and
            // colours correctly — no MaterialPropertyBlock runs outside play mode.
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
