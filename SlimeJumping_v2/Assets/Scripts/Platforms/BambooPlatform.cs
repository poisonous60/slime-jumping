using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;
using SlimeJumping.Settings;

namespace SlimeJumping.Platforms
{
    // Vertical bamboo: single Rigidbody2D at base; N child segments form a collider-only
    // polyline that approximates a smooth circular-arc cantilever curve. A separate single
    // visual mesh (procedural cylinder) is bent by the SlimeJumping/BambooBend shader using
    // the SAME closed-form curve, so visual and collider stay sub-pixel coincident.
    //
    // One ODE state (currentAngle, angularVel) drives tip deflection. Shape function is
    // hardcoded linear h(t)=t — local tangent angle scales linearly along height — because
    // the shader is hardcoded to that form to guarantee visual/collider match.
    //
    // All segment colliders share the root rigidbody → slime sees one body, no carrier
    // handoff between segments.
    public class BambooPlatform : PlatformMotor, ISurfaceAnchorProvider
    {
        public enum SegmentColliderShape { Capsule, Box }

        [Header("Settings")]
        [Tooltip("Shared gameplay tuning asset. All spring/pumping/decay/carrier/re-entry/tip-grab knobs live here so multiple bamboos can share one tuning file. If null, a runtime instance with class-default values is auto-created in Awake — old scenes keep working with the pre-refactor defaults.")]
        [SerializeField] BambooSettings settings;

        [Header("Surface Override")]
        [SerializeField] SurfaceMaterial bambooSurfaceMaterial;
        public override SurfaceMaterial OverrideSurfaceMaterial => bambooSurfaceMaterial;

        [Header("Mode")]
        [Tooltip("If true, bamboo spawns as visual-only decoration (no collider segments, no rider physics). Rebuild Segments after toggling.")]
        [SerializeField] bool backgroundOnly = false;

        [Header("Geometry")]
        [SerializeField, Min(0.5f)] float bambooLength = 4f;
        [SerializeField, Range(2, 32)] int segmentCount = 12;
        [SerializeField, Min(0.02f)] float segmentRadius = 0.15f;
        [Tooltip("Small length overlap between neighboring collider segments. This hides capsule/box seam jitter while the visual node rings still stay aligned to segment boundaries. Rebuild Segments after changing.")]
        [SerializeField, Min(0f)] float colliderSegmentOverlap = 0.03f;
        [SerializeField] SegmentColliderShape colliderShape = SegmentColliderShape.Capsule;

        [Header("Visual")]
        [Tooltip("Material using the SlimeJumping/BambooBend shader. If null, a runtime material is created. _BendAngleDeg and _BambooLength are pushed per-frame via MaterialPropertyBlock so a single shared material works across many bamboos.")]
        [SerializeField] Material bendMaterial;
        [Tooltip("Optional bark texture sampled by the BambooBend shader's _BaseMap. Mixed in via baseMapStrength; leave at 0 for clean stylized color.")]
        [SerializeField] Texture2D bambooBarkTexture;
        [SerializeField, Range(0f, 1f)] float baseMapStrength = 0f;

        [Header("Visual — Stalk Color Gradient")]
        [Tooltip("Stalk hue at the base of the bamboo. Gradient lerps from this to stalkColorTip along the length.")]
        [SerializeField] Color stalkColorBase = new Color(0.30f, 0.52f, 0.20f, 1f);
        [Tooltip("Stalk hue at the tip of the bamboo.")]
        [SerializeField] Color stalkColorTip = new Color(0.50f, 0.68f, 0.28f, 1f);
        [Tooltip("Multiplier applied to the stalk hue in shadow zones. 1 = no shadow, 0.6 default ≈ real bamboo shadow depth.")]
        [SerializeField, Range(0f, 1f)] float shadowDarken = 0.60f;

        [Header("Visual — Node Geometry (마디)")]
        // Visible node rings always track segmentCount: shader scars sit at t = k/segmentCount,
        // the same parameter the Segment_XX colliders split on, so rings land on the collider
        // joints. Spacing follows bambooLength automatically (ring at world y = k/segmentCount * L).
        [Tooltip("Bulge radius multiplier — real bamboo nodes swell outward ~5-10% wider than the internodes. This is the defining 3D feature.")]
        [SerializeField, Range(0f, 0.5f)] float bulgeAmount = 0.10f;
        [Tooltip("Bulge half-width along the UV-V cycle. Controls how broad the swelling is around each node center.")]
        [SerializeField, Range(0.01f, 0.4f)] float bulgeWidth = 0.12f;
        [Tooltip("Sheath-scar core half-width — dist inside this is full scar (color + groove). Real scar is a subtle line, not a thick band.")]
        [SerializeField, Range(0.001f, 0.1f)] float scarHalfWidth = 0.004f;
        [Tooltip("Scar edge softness — how far past the core the scar fades to 0. Larger = wider brown gradient around the node line.")]
        [SerializeField, Range(0f, 0.2f)] float scarSoftness = 0.02f;
        [Tooltip("Color falloff curve for the brown-to-green transition. 1 = linear, >1 = brown concentrated at center (steep edge), <1 = brown spread broadly with quick taper at the end.")]
        [SerializeField, Range(0.1f, 6f)] float scarColorCurve = 1f;
        [Tooltip("Sheath-scar pinch depth — pulls the radius slightly inward right at the node center for a real groove.")]
        [SerializeField, Range(0f, 0.2f)] float scarDepth = 0.03f;
        [Tooltip("Sheath-scar color (thin desaturated olive/brown line, blends into the green stalk).")]
        [SerializeField] Color scarColor = new Color(0.22f, 0.26f, 0.14f, 1f);

        [Header("Visual — Toon Lighting")]
        [Tooltip("Threshold of N·L between shadow and lit. 0 ≈ standard half-Lambert split.")]
        [SerializeField, Range(-1f, 1f)] float toonThreshold = 0.15f;
        [Tooltip("Softness of the lit/shadow band.")]
        [SerializeField, Range(0.001f, 0.5f)] float toonSoftness = 0.08f;
        [Tooltip("Rim light strength on silhouette edges.")]
        [SerializeField, Range(0f, 1f)] float rimStrength = 0.25f;
        [Tooltip("Rim power; higher = thinner rim band.")]
        [SerializeField, Range(0.5f, 8f)] float rimPower = 3f;
        [Tooltip("World-space light direction the shader uses for the toon split. Points TOWARD the light.")]
        [SerializeField] Vector3 lightDirWS = new Vector3(0.3f, 0.85f, -0.45f);

        [Header("Visual — Mesh Tessellation")]
        [Tooltip("Radial segments around the cylinder (visual smoothness, no physics cost).")]
        [SerializeField, Range(6, 32)] int visualRadialSegments = 16;
        [Tooltip("Height segments along the cylinder (more = smoother curve in shader bend).")]
        [SerializeField, Range(4, 64)] int visualHeightSegments = 24;

        // Gameplay knobs — naturalFreqHz, dampingRatio, maxDeflectionDeg, clampRestitution,
        // riderPumpGain, chargePumpScale, riderTorqueScale, passiveDecayRate, riderDecayRate,
        // maxTipSpeed, velocityTransferScale, carrierMinContribution, carrierDistanceScale,
        // slamMinHorizontalSpeed, slamReattachLockout, grabEnterDot, tiltTorque,
        // chargingTiltScale, grabModeCarrierScale — all moved into the BambooSettings asset
        // referenced at the top of this component. Per-stalk inspector fields below stay
        // local (geometry, visual, surface override, mode toggle).

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

        Transform[] segmentTransforms;
        MeshRenderer visualRenderer;
        MaterialPropertyBlock propertyBlock;

        // World-space tip position cache, refreshed each FU in OnAfterMotion after the
        // segment polyline has been recomputed. Read by SlimeController.BambooGrab to
        // lock rb.position pre-probe.
        Vector2 cachedTipWorldPos;

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
        public bool UsesTipGrab => settings.interactionMode == BambooInteractionMode.TipGrab;
        public bool UsesSideReboundV2 => settings.interactionMode == BambooInteractionMode.SideReboundV2;
        public float GrabEnterDot => settings.grabEnterDot;
        /// <summary>Multiplier on the charge-power launch component for a tip-grab launch
        /// (the up-the-stalk jump). Read by SlimeController.Launch.</summary>
        public float GrabLaunchPowerScale => settings.grabLaunchPowerScale;
        /// <summary>Multiplier on the charge auto-drift speed while tip-grabbed (charge fills
        /// faster than on flat ground). Read by SlimeController.FixedUpdateCharging.</summary>
        public float GrabChargeFillScale => settings.grabChargeFillScale;
        /// <summary>Override for chargeFullAimTime (edge grace before auto-launch) while
        /// tip-grabbed. -1 = use the slime default. Read by SlimeController.FixedUpdateCharging.</summary>
        public float GrabChargeFullAimTime => settings.grabChargeFullAimTime;
        public float V2CarrierScale => settings.v2CarrierScale;
        public float SegmentRadius => segmentRadius;
        public float TipSwingSpeed => Mathf.Abs(angularVel * Mathf.Deg2Rad) * bambooLength;
        /// <summary>World-space tip position cached at the end of each FixedUpdate from
        /// the same centerline math used for collider segment placement and the visual
        /// shader bend. SlimeController.BambooGrab reads this each FU to anchor the
        /// grabbed slime.</summary>
        public Vector2 GetTipWorldPos() => cachedTipWorldPos;
        /// <summary>World-space direction of the bamboo's local up axis at the tip
        /// (i.e. the bend tangent at t=1). Used by SlimeController.BambooGrab to offset
        /// the grabbed slime above the tip cap so its CircleCollider doesn't penetrate
        /// the topmost capsule (which would cause the physics resolver to fight the
        /// per-FU position lock and produce visible jitter).</summary>
        public Vector2 GetTipUpDirection()
        {
            float rad = currentAngle * Mathf.Deg2Rad;
            Vector3 localTangent = new Vector3(-Mathf.Sin(rad), Mathf.Cos(rad), 0f);
            Vector3 worldTangent = transform.TransformDirection(localTangent);
            Vector2 v = new Vector2(worldTangent.x, worldTangent.y);
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector2.up;
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureSettings();
            pivotWorldPos = rb.position;
            baseRotation = rb.rotation;
            currentAngle = 0f;
            angularVel = 0f;
            detachTimer = settings.slamReattachLockout;
            hasPrevRiderVel = false;

            CacheChildren();
            // Seed the tip cache so a grab fired on the very first FU (before
            // OnAfterMotion has populated it) reads a sensible value.
            cachedTipWorldPos = transform.TransformPoint(new Vector3(0f, bambooLength, 0f));
        }

        /// <summary>
        /// Guarantees `settings` is non-null. If no asset is wired, builds a runtime
        /// BambooSettings instance whose fields hold the class-default values — keeps
        /// pre-refactor scenes working without manual migration. Public so OnValidate
        /// and the editor "Refresh" / "Rebuild" context-menu paths can also rely on it.
        /// </summary>
        void EnsureSettings()
        {
            if (settings == null)
                settings = ScriptableObject.CreateInstance<BambooSettings>();
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

        // VelocityTransferScale is queried by PlatformCarrier at launch. Carrier computes
        // ω × arm using rb.position (base pivot) and slime.transform.position. For (mostly)
        // vertical bamboo, |arm| ≈ slime height along bamboo, so carrier supplies
        // tipSpeed * t where t = armLen / bambooLength.
        //
        // We want: finalCarrier = tipSpeed * (carrierMin + carrierDistanceScale * t) * velocityTransferScale.
        // So: VelocityTransferScale = ((carrierMin + carrierDistanceScale * t) / t) * velocityTransferScale.
        // The 1/t blowup at t→0 cancels because ω×arm→0 too; clamp t to avoid div-by-zero.
        public override float VelocityTransferScale
        {
            get
            {
                if (cachedSlime == null) cachedSlime = FindFirstObjectByType<SlimeController>();
                if (cachedSlime == null) return 0f;

                Vector2 arm = (Vector2)cachedSlime.transform.position - pivotWorldPos;
                float t = Mathf.Clamp01(arm.magnitude / Mathf.Max(bambooLength, 1e-4f));
                float requested = settings.carrierMinContribution + settings.carrierDistanceScale * t;
                float carrierGives = Mathf.Max(t, 1e-3f);
                // Tip-grab gives the slime an extra carrier multiplier so the swing-back
                // launch can be tuned independently from side-attach launches. Slime's
                // CurrentBambooGrab is cleared in ChangeState's exit-to-Airborne hook,
                // which runs AFTER carrier.GetLaunchVelocity in Launch() — so the boost
                // applies on the launching frame.
                float grabBoost = cachedSlime.CurrentBambooGrab == this ? settings.grabModeCarrierScale : 1f;
                float v2Boost = cachedSlime.CurrentBambooV2Rebound == this ? settings.v2CarrierScale : 1f;
                return (requested / carrierGives) * settings.velocityTransferScale * grabBoost * v2Boost;
            }
        }

        // Report the ODE angular velocity to PlatformCarrier so it can compute ω × arm
        // for slime at any height. Position remains fixed (base pivot).
        public override Vector2 Velocity { get => Vector2.zero; protected set { } }
        public override float AngularVelocity { get => angularVel; protected set { } }

        public override Vector2 GetRiderCarryDelta(Vector2 riderWorldPos, float dt)
        {
            if (dt <= 0f) return Vector2.zero;

            float radPerSec = angularVel * Mathf.Deg2Rad;
            if (Mathf.Abs(radPerSec) < 0.0001f) return Vector2.zero;

            Vector2 arm = riderWorldPos - pivotWorldPos;
            return new Vector2(-arm.y, arm.x) * radPerSec * dt;
        }

        /// <summary>True when this bamboo is the slime's active tip-grab target.
        /// Lazy-resolves the SlimeController reference (same lookup VelocityTransferScale
        /// uses) so the grab-aware swaps in ComputeSpringCoeffs / ComputeMotion / ApplyTiltTorque
        /// actually fire — without this lazy init, cachedSlime stayed null in normal play
        /// (it was only populated inside VelocityTransferScale, which runs at launch), so
        /// every grab-mode knob (grabSpringFreqHz, grabDampingRatio, grabMaxDeflectionDeg,
        /// grabHoldBendDeg) silently fell back to the side-attach defaults.</summary>
        bool IsGrabbedByCachedSlime()
        {
            if (cachedSlime == null) cachedSlime = FindFirstObjectByType<SlimeController>();
            return cachedSlime != null && cachedSlime.CurrentBambooGrab == this;
        }

        public bool IsV2SideContact(Vector2 surfaceNormal, Vector2 gravityUp)
        {
            if (!UsesSideReboundV2) return false;
            return Mathf.Abs(Vector2.Dot(surfaceNormal, gravityUp)) <= settings.v2SideNormalMaxUpDot;
        }

        public float GetHeight01(Vector2 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            return Mathf.Clamp01(local.y / Mathf.Max(bambooLength, 1e-4f));
        }

        public bool CanArmV2ReboundAt(Vector2 worldPos)
        {
            return GetHeight01(worldPos) >= settings.v2ReboundMinContactHeight01;
        }

        public bool TryGetAnalyticSurfaceAnchor(Vector2 riderWorldPos, float riderRadius, out Vector2 anchorWorldPos, out Vector2 normalWorld)
        {
            anchorWorldPos = riderWorldPos;
            normalWorld = Vector2.up;
            if (backgroundOnly) return false;

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
            float offset = segmentRadius + Mathf.Max(0f, riderRadius);
            Vector2 anchorLocal = center + outward * offset;

            anchorWorldPos = transform.TransformPoint(anchorLocal);
            normalWorld = transform.TransformVector(outward).normalized;
            return true;
        }

        public bool TryGetSurfaceAnchor(Collider2D sourceCollider, Vector2 riderWorldPos, float riderRadius,
            out SurfaceAnchorInfo anchorInfo)
        {
            anchorInfo = default;
            if (sourceCollider == null) return false;
            if (!sourceCollider.enabled) return false;
            if (!sourceCollider.gameObject.activeInHierarchy) return false;

            if (!TryGetAnalyticSurfaceAnchor(riderWorldPos, riderRadius,
                out Vector2 anchorWorldPos, out Vector2 normalWorld))
            {
                return false;
            }

            Vector2 tangentWorld = new Vector2(-normalWorld.y, normalWorld.x);
            anchorInfo = new SurfaceAnchorInfo(anchorWorldPos, normalWorld, tangentWorld, true, name);
            return true;
        }

        public float EvaluateV2ChargedLaunchBonus(float chargePower)
        {
            return BambooV2Response.Evaluate(
                settings.v2ResponseMode,
                chargePower,
                settings.v2ChargedLaunchBonusByChargePower,
                settings.v2ChargedLaunchBonus);
        }

        public void ApplyV2LaunchReleaseKick(Vector2 riderWorldPos, Vector2 launchVelocity)
        {
            if (!UsesSideReboundV2) return;
            if (settings.v2LaunchReleaseVelocityScale <= 0f) return;

            Vector2 arm = riderWorldPos - pivotWorldPos;
            float heightScale = EvaluateV2LaunchReleaseHeightScale(GetHeight01(riderWorldPos));
            float targetAngularVelDeg = BambooV2Response.EvaluateLaunchReleaseAngularVelocityDeg(
                arm,
                launchVelocity,
                settings.v2LaunchReleaseVelocityScale,
                heightScale);
            if (Mathf.Abs(targetAngularVelDeg) < 1e-4f) return;

            if (Mathf.Abs(targetAngularVelDeg) > Mathf.Abs(angularVel))
                angularVel = targetAngularVelDeg;
            else
                angularVel += targetAngularVelDeg;

            float maxAngularVelDeg = (settings.v2LaunchReleaseMaxTipSpeed / Mathf.Max(bambooLength, 1e-4f)) * Mathf.Rad2Deg;
            angularVel = Mathf.Clamp(angularVel, -maxAngularVelDeg, maxAngularVelDeg);
        }

        float EvaluateV2BendPumpGain(float impactSpeed)
        {
            return BambooV2Response.Evaluate(
                settings.v2ResponseMode,
                impactSpeed,
                settings.v2BendPumpGainByImpactSpeed,
                settings.v2BendPumpGain);
        }

        float EvaluateV2BendHeightScale(float height01)
        {
            return BambooV2Response.EvaluateHeightScale(
                height01,
                settings.v2BendMinHeightScale,
                settings.v2BendHeightPower);
        }

        float EvaluateV2LaunchReleaseHeightScale(float height01)
        {
            return BambooV2Response.EvaluateOptionalHeightScale(
                settings.v2LaunchReleaseUseHeightScale,
                height01,
                settings.v2LaunchReleaseMinHeightScale,
                settings.v2LaunchReleaseHeightPower);
        }

        (float stiffness, float damping, float ITotal) ComputeSpringCoeffs()
        {
            float IBranch = rb.mass * bambooLength * bambooLength / 3f;
            float ITotal = IBranch + (riderAttached ? currentRiderMass * currentRiderArmSqr : 0f);
            // Spring stiffness AND damping ratio both swap to grab-specific values while
            // this bamboo is the slime's tip-grab target. This lets the side-attach
            // physics stay stiff/well-damped (slime hopping off shouldn't leave the
            // bamboo ringing, and side hits shouldn't fling the cap), while the grab
            // mode can be soft/underdamped — giving the player a wide sideways bend
            // range and resonance-pumping headroom.
            bool grabActive = IsGrabbedByCachedSlime();
            bool v2Active = UsesSideReboundV2 && riderAttached;
            float freqHz = settings.naturalFreqHz;
            float zeta = settings.dampingRatio;
            if (grabActive)
            {
                freqHz = settings.grabSpringFreqHz;
                zeta = settings.grabDampingRatio;
            }
            else if (v2Active)
            {
                freqHz = settings.v2SpringFreqHz;
                zeta = settings.v2DampingRatio;
            }
            float omega = 2f * Mathf.PI * freqHz;
            float stiffness = omega * omega * ITotal;
            float damping = 2f * zeta * omega * ITotal;
            return (stiffness, damping, ITotal);
        }

        public override void ApplyRiderInfluence(Vector2 riderWorldPos, float mass, Vector2 riderVelocity, bool isCharging)
        {
            Vector2 r = riderWorldPos - pivotWorldPos;
            float riderHeight01 = GetHeight01(riderWorldPos);
            currentRiderMass = mass;
            currentRiderArmSqr = r.sqrMagnitude;

            bool firstAttach = !riderAttached;
            riderAttached = true;

            Vector2 dv = hasPrevRiderVel ? (riderVelocity - previousRiderVel) : riderVelocity;

            // Re-entry guard mirrors SpringyBranchPlatform's slam-suppress, but for vertical
            // bamboo a "slam" is a fast lateral hit (not a downward fall), so gate on |vx|.
            bool suppress = firstAttach
                            && (detachTimer < settings.slamReattachLockout)
                            && (Mathf.Abs(riderVelocity.x) < settings.slamMinHorizontalSpeed);

            if (!suppress)
            {
                Vector2 impulseP = dv * mass;
                float pumpGain = settings.riderPumpGain * (isCharging ? settings.chargePumpScale : 1f);
                if (UsesSideReboundV2)
                    pumpGain *= EvaluateV2BendPumpGain(Mathf.Abs(riderVelocity.x)) * EvaluateV2BendHeightScale(riderHeight01);
                var (_, _, ITotal) = ComputeSpringCoeffs();
                if (ITotal > 1e-6f)
                {
                    float impulseAngVelRad = (r.x * impulseP.y - r.y * impulseP.x) / ITotal * pumpGain;
                    angularVel += impulseAngVelRad * Mathf.Rad2Deg;
                }
            }

            previousRiderVel = firstAttach ? Vector2.zero : riderVelocity;
            hasPrevRiderVel = true;

            Vector2 gravity = Physics2D.gravity * mass;
            float gravTorque = r.x * gravity.y - r.y * gravity.x;
            pendingRiderTorque += gravTorque * settings.riderTorqueScale;
        }

        public override void RemoveRider()
        {
            riderAttached = false;
            hasPrevRiderVel = false;
            detachTimer = 0f;
        }

        /// <summary>
        /// Player A/D input while in tip-grab. Two-channel pump:
        ///
        /// 1. Lean channel — held input applies a constant lean torque whose magnitude
        ///    is tuned so the steady-state bend equals settings.grabHoldBendDeg. This
        ///    gives a visible static deflection when the player simply holds a direction.
        ///    Disabled during Charging (chargingTiltScale = 0 by default).
        ///
        /// 2. Anti-damping boost channel (C2) — when the held input direction matches
        ///    the tip's current motion direction (matched-pump condition), an extra torque
        ///    proportional to |angularVel| is added in the direction of motion. This is
        ///    the "kid on a swing pushes when going forward" pattern: the player doesn't
        ///    have to time A↔D flips at the exact swing extremes; merely pushing in the
        ///    direction of motion injects energy, and the amplitude grows over multiple
        ///    swings until the soft cap at grabMaxDeflectionDeg. The gain is in 1/s units
        ///    so above the spring's damping coefficient (2ζω) the effective net damping
        ///    flips negative while matched. Disabled during Charging by default.
        ///
        /// Sign convention: positive currentAngle = CCW (tip toward −X), so dθ/dt > 0
        /// means the tip is moving toward −X. Pressing A (inputX = −1) pushes the tip
        /// toward −X, which requires a positive torque; pressing D is symmetric. Hence
        /// the leading minus in both channels. Matched-pump condition is therefore
        /// (−inputX) · angularVel &gt; 0 (same sign in the physics-frame).
        /// </summary>
        public void ApplyTiltTorque(float inputX, bool isCharging)
        {
            if (Mathf.Abs(inputX) < 1e-4f) return;

            var (stiffness, _, ITotal) = ComputeSpringCoeffs();

            // Lean channel — steady-state equilibrium shift.
            float leanScale = isCharging ? settings.chargingTiltScale : 1f;
            if (leanScale > 0f)
            {
                float refTorque = stiffness * settings.grabHoldBendDeg * Mathf.Deg2Rad;
                pendingRiderTorque += -inputX * refTorque * leanScale;
            }

            // Anti-damping boost channel — energy injection in direction of motion.
            float pumpScale = isCharging ? settings.grabPumpChargingScale : 1f;
            if (pumpScale > 0f && settings.grabPumpBoostGain > 0f)
            {
                float angVelRad = angularVel * Mathf.Deg2Rad;
                // Threshold avoids fighting numerical noise around ω≈0 where sign(ω)
                // flips rapidly; below the threshold lean alone drives motion until
                // the bamboo accumulates enough velocity for the boost to lock onto.
                const float pumpVelThresholdRad = 0.05f;
                bool matched = (-inputX) * angVelRad > 0f;
                if (matched && Mathf.Abs(angVelRad) > pumpVelThresholdRad)
                {
                    pendingRiderTorque += Mathf.Abs(inputX) * angVelRad * settings.grabPumpBoostGain * ITotal * pumpScale;
                }
            }
        }

        protected override (Vector2, float) ComputeMotion(float dt)
        {
            detachTimer += dt;

            var (stiffness, damping, ITotal) = ComputeSpringCoeffs();

            float angleRadFromRest = currentAngle * Mathf.Deg2Rad;
            float angVelRad = angularVel * Mathf.Deg2Rad;

            float springTorque = -stiffness * angleRadFromRest;
            float dampingTorque = -damping * angVelRad;

            // Deflection cap swaps grab vs side: side-attach uses the hard ω=0 clamp
            // below (predictable saturation for slime hopping off a bent stalk); grab
            // uses a quartic soft-cap restoring torque added INSIDE the ODE so the bend
            // asymptotes to grabMaxDeflectionDeg without an artificial velocity wipe.
            bool grabActive = IsGrabbedByCachedSlime();
            bool v2Active = UsesSideReboundV2 && riderAttached;
            float maxAngle = settings.maxDeflectionDeg;
            if (grabActive)
                maxAngle = settings.grabMaxDeflectionDeg;
            else if (v2Active)
                maxAngle = settings.v2MaxDeflectionDeg;

            float softCapTorque = 0f;
            if (grabActive && settings.grabCapPenaltyGain > 0f && maxAngle > 1e-4f)
            {
                float normAbs = Mathf.Abs(currentAngle) / maxAngle;
                float n2 = normAbs * normAbs;
                float n4 = n2 * n2;
                softCapTorque = -Mathf.Sign(currentAngle) * settings.grabCapPenaltyGain * n4 * stiffness * maxAngle * Mathf.Deg2Rad;
            }

            float angularAccelRad = (springTorque + dampingTorque + softCapTorque + pendingRiderTorque) / Mathf.Max(ITotal, 1e-6f);
            float angularAccelDeg = angularAccelRad * Mathf.Rad2Deg;

            angularVel += angularAccelDeg * dt;
            currentAngle += angularVel * dt;

            float decayRate = riderAttached ? settings.riderDecayRate : settings.passiveDecayRate;
            if (decayRate > 0f)
                angularVel *= Mathf.Exp(-decayRate * dt);

            float maxAngularVelDeg = (settings.maxTipSpeed / Mathf.Max(bambooLength, 1e-4f)) * Mathf.Rad2Deg;
            angularVel = Mathf.Clamp(angularVel, -maxAngularVelDeg, maxAngularVelDeg);

            if (!grabActive)
            {
                if (currentAngle > maxAngle)
                {
                    currentAngle = maxAngle;
                    if (angularVel > 0f) angularVel = -angularVel * settings.clampRestitution;
                }
                else if (currentAngle < -maxAngle)
                {
                    currentAngle = -maxAngle;
                    if (angularVel < 0f) angularVel = -angularVel * settings.clampRestitution;
                }
            }

            pendingRiderTorque = 0f;

            // Pivot position fixed; root rotation fixed too (segments rotate locally).
            return (pivotWorldPos, baseRotation);
        }

        protected override void OnAfterMotion(float dt)
        {
            ApplySegmentTransforms();
            PushVisualBendProperties();
        }

        // Closed-form circular arc — must match BambooBend.shader exactly.
        // Sign convention: positive currentAngle = counter-clockwise rotation around +Z
        // (standard Unity Euler Z, matching the rider-impulse cross product `r × p`). For
        // linear shape h(t)=t the local tangent angle at fraction t is currentAngle*t and
        // the centerline is the arc of a circle of radius L/bendRad:
        //   x(t) = (L/bendRad) * (cos(bendRad*t) - 1)   // negative X for positive bendRad (CCW bend)
        //   y(t) = (L/bendRad) * sin(bendRad*t)
        // Degenerate bendRad→0 falls back to straight line along +Y.
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

        // Place each collider segment as a chord between consecutive centerline samples.
        // The polyline approximates the same smooth curve the shader bends to, so the
        // collider surface and visual surface stay coincident.
        void ApplySegmentTransforms()
        {
            // Refresh the tip cache every FU regardless of segment availability —
            // SlimeController.BambooGrab depends on this being current even when the
            // bamboo is backgroundOnly (no collider segments built).
            Vector2 localTip = ComputeCenterlinePoint(1f);
            cachedTipWorldPos = transform.TransformPoint(localTip);

            if (segmentTransforms == null || segmentTransforms.Length != segmentCount) CacheChildren();
            if (segmentTransforms == null || segmentTransforms.Length == 0) return;

            for (int i = 0; i < segmentCount; i++)
            {
                Vector2 pStart = ComputeCenterlinePoint((float)i / segmentCount);
                Vector2 pEnd = ComputeCenterlinePoint((float)(i + 1) / segmentCount);
                Vector2 chord = pEnd - pStart;

                // Signed angle from +Y (rest direction) to chord, measured CCW around +Z.
                // Derivation: rotating +Y by α CCW yields (-sin α, cos α). Set this equal to
                // chord/|chord| → sin α = -chord.x/|chord|, cos α = chord.y/|chord| →
                // α = atan2(-chord.x, chord.y). Unity Euler Z rotation is CCW for positive α,
                // matching the rider-impulse sign convention used by ComputeCenterlinePoint.
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

        void PushVisualBendProperties()
        {
            if (visualRenderer == null) return;
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

            visualRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(BendAngleId, currentAngle);
            propertyBlock.SetFloat(BambooLengthId, bambooLength);
            visualRenderer.SetPropertyBlock(propertyBlock);
        }

        // Shader _NodeFrequency = rings per stalk length. Driven straight off segmentCount
        // so visible scar rings coincide with the Segment_XX collider joints.
        float VisibleNodeFrequency() => Mathf.Max(1, segmentCount);

        int EffectiveVisualHeightSegments()
        {
            int safeSegmentCount = Mathf.Max(1, segmentCount);
            int targetHeightSegments = Mathf.Max(visualHeightSegments, safeSegmentCount * 4);
            int rowsPerColliderSegment = Mathf.CeilToInt((float)targetHeightSegments / safeSegmentCount);
            return safeSegmentCount * Mathf.Max(1, rowsPerColliderSegment);
        }

#if UNITY_EDITOR
        // Inspector field changes don't go through RebuildSegments — push the visual /
        // material params directly so colors, scar, bulge, lighting update live in the
        // editor without a manual rebuild. Mesh-geometry changes (bambooLength,
        // segmentCount, segmentRadius, visual*Segments, colliderShape) still need
        // Rebuild Segments because the mesh and colliders are baked at creation time.
        void OnValidate()
        {
            // delayCall defers past mid-serialization so we don't touch the renderer
            // before the new field value is committed.
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
                Debug.LogWarning($"[BambooPlatform] '{VisualChildName}' child not found on {name}. Click 'Rebuild Segments' first.", this);
                return;
            }
            var mr = visualChild.GetComponent<MeshRenderer>();
            if (mr == null)
            {
                Debug.LogWarning($"[BambooPlatform] Visual MeshRenderer missing on {name}.", this);
                return;
            }
            // Use the live instance — sharedMaterial on a runtime-created material is
            // the same object, but if the user dropped an asset reference in
            // `bendMaterial`, sharedMaterial would mutate the asset. Guard with .material
            // in play mode for safety, sharedMaterial in edit mode for asset-safe writes.
            var mat = Application.isPlaying ? mr.material : mr.sharedMaterial;
            if (mat == null)
            {
                Debug.LogWarning($"[BambooPlatform] No material on visual renderer for {name}.", this);
                return;
            }

            mat.SetFloat(BambooLengthId, bambooLength);
            mat.SetFloat(BendAngleId, Application.isPlaying ? currentAngle : 0f);
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

            UnityEditor.EditorUtility.SetDirty(mat);
            UnityEditor.SceneView.RepaintAll();
        }
#endif

        // Editor convenience: rebuild segment colliders + the visual mesh to match current
        // bambooLength / segmentCount / colliderShape / visual fields.
        // Right-click the component header → Rebuild Segments.
        [ContextMenu("Rebuild Segments")]
        public void RebuildSegments()
        {
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
            // Match Unity's "(N)" duplicate suffix too — when user Ctrl+D's a bamboo,
            // child "Visual" gets renamed "Visual (1)" and exact-string match leaks it,
            // producing a duplicate Visual on the next Rebuild.
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
            mf.sharedMesh = BuildCylinderMesh(visualRadialSegments, EffectiveVisualHeightSegments(), segmentRadius, bambooLength);

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

            var mat = new Material(shader) { name = "BambooBend (Auto)" };
            // Push _BambooLength on material so the edit-mode rest pose normalizes
            // height correctly (no MaterialPropertyBlock runs outside play mode).
            mat.SetFloat(BambooLengthId, bambooLength);
            mat.SetFloat(BendAngleId, 0f);
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
            return mat;
        }

        // Procedural straight cylinder, base at y=0, tip at y=length. positionOS is in
        // world-equivalent units so the BambooBend shader can apply its bend in-place
        // without having to know any Transform scale.
        static Mesh BuildCylinderMesh(int radialSegments, int heightSegments, float radius, float length)
        {
            radialSegments = Mathf.Max(3, radialSegments);
            heightSegments = Mathf.Max(1, heightSegments);

            int ringVerts = radialSegments + 1; // duplicate seam vert for clean UVs
            int vertCount = ringVerts * (heightSegments + 1);
            var verts = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];

            for (int j = 0; j <= heightSegments; j++)
            {
                float v = (float)j / heightSegments;
                float y = v * length;
                for (int i = 0; i < ringVerts; i++)
                {
                    float u = (float)i / radialSegments;
                    float angle = u * Mathf.PI * 2f;
                    float cx = Mathf.Cos(angle);
                    float cz = Mathf.Sin(angle);
                    int idx = j * ringVerts + i;
                    verts[idx] = new Vector3(cx * radius, y, cz * radius);
                    normals[idx] = new Vector3(cx, 0f, cz);
                    uvs[idx] = new Vector2(u, v);
                }
            }

            int triCount = radialSegments * heightSegments * 6;
            var tris = new int[triCount];
            int ti = 0;
            for (int j = 0; j < heightSegments; j++)
            {
                for (int i = 0; i < radialSegments; i++)
                {
                    int a = j * ringVerts + i;
                    int b = a + 1;
                    int c = a + ringVerts;
                    int d = c + 1;
                    tris[ti++] = a; tris[ti++] = c; tris[ti++] = b;
                    tris[ti++] = b; tris[ti++] = c; tris[ti++] = d;
                }
            }

            var mesh = new Mesh { name = "BambooCylinder" };
            mesh.indexFormat = vertCount > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            // Generous bounds: bent visual extends laterally up to ~bambooLength in worst case.
            mesh.bounds = new Bounds(new Vector3(0f, length * 0.5f, 0f),
                                     new Vector3(length * 2f, length * 1.2f, length * 2f));
            return mesh;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            Vector3 basePos = Application.isPlaying ? (Vector3)pivotWorldPos : transform.position;
            Gizmos.DrawSphere(basePos, 0.12f);
            Gizmos.DrawLine(basePos, basePos + Vector3.up * bambooLength);
        }
    }
}
