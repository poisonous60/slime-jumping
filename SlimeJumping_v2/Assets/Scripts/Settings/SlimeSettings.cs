using UnityEngine;
using SlimeJumping.Physics;

namespace SlimeJumping.Settings
{
    public enum HyperDashInertiaSource { ChargeStart, LaunchMoment }

    [CreateAssetMenu(fileName = "SlimeSettings", menuName = "SlimeJumping/Slime Physical Settings")]
    public class SlimeSettings : ScriptableObject
    {
        [Header("Surface Movement")]
        public float crawlSpeed = 5f;
        public float crawlAcceleration = 50f;
        public float crawlDeceleration = 30f;
        [Tooltip("Deceleration applied when crawl speed exceeds max crawl speed (e.g. from landing momentum)")]
        public float crawlOverspeedDeceleration = 15f;
        public float slideGravityScale = 1f;
        public float slideThreshold = 1f;
        public float slideResistance = 2f;

        [Header("Ground Dash")]
        public float groundDashImpulse = 8f;
        public float groundDashCooldown = 0.3f;
        public float groundDashInputBuffer = 0.1f;
        public float groundDashAngleMin = 30f;
        public float groundDashAngleMax = 60f;
        public float dashFalloffStart = 5f;
        public float dashFalloffEnd = 13f;

        [Header("Hop")]
        [Tooltip("If true, hop along the surface normal (tangent-based). If false, hop in world-up (gravity-up) direction.")]
        public bool hopAlongSurfaceNormal = true;
        public float hopPower = 2.25f;
        [Tooltip("Minimum hop power at hopMaxAngle.")]
        public float hopPowerMin = 1.125f;
        [Tooltip("Surface angle (degrees) below which full hopPower applies.")]
        public float hopMinAngle = 70f;
        [Tooltip("Surface angle (degrees) at which hopPowerMin applies. Lerp between.")]
        public float hopMaxAngle = 90f;
        public float hopCooldown = 0.1f;
        public float hopBufferDuration = 0.1f;
        [Range(0f, 1f)]
        public float hopLandingRetention = 1f;
        [Tooltip("Tangent speed bonus added per hop in movement direction. 0 = no momentum gain.")]
        [Min(0f)] public float hopMomentumBonus = 1.5f;
        [Tooltip("Maximum tangent speed achievable via hop momentum accumulation.")]
        [Min(0f)] public float hopMomentumMaxSpeed = 20f;

        [Header("Hyper Dash")]
        [Tooltip("ChargeStart: capture tangent speed at the moment charging begins (legacy). LaunchMoment: read tangent speed at the launch frame so any deceleration during the charge actually reduces the inherited momentum.")]
        public HyperDashInertiaSource hyperDashInertiaSource = HyperDashInertiaSource.ChargeStart;
        [Range(0f, 1f)] public float tangentInheritanceFactor = 0.7f;
        [Tooltip("Crawl speed below this threshold is ignored during tangent inheritance. 0 = inherit full crawl speed.")]
        [Min(0f)] public float hyperDashDeduction = 5f;
        [Tooltip("Energy conversion ratio below which hyper dash is fully retained.")]
        public float hyperDashConversionDampingMin = 1.2f;
        [Tooltip("Energy conversion ratio at which hyper dash is fully dampened.")]
        public float hyperDashConversionDampingMax = 2f;

        [Header("Charge Circle")]
        [Min(0.01f)] public float chargeRadius = 1.5f;
        public float autoDriftSpeed = 1.5f;
        public float inputAcceleration = 3.0f;
        [Min(0.01f)] public float baseLaunchPower = 15f;
        public float minLaunchPower = 3f;
        public float chargeCurveExponent = 1f;
        [Tooltip("Grace period at full charge to adjust aim direction without affecting power.")]
        [Min(0f)] public float chargeFullAimTime = 0.1f;
        public float maxAimAngularSpeed = 180f;
        public float chargeNormalFollowSpeed = 5f;
        public float chargingCoyoteTime = 0.2f;
        public float coyoteGraceTime = 0.1f;
        public int chargingSurfaceProbeGraceFrames = 3;
        [Tooltip("Jump press-to-land buffer (seconds). Pressing Jump this long before landing still triggers charging.")]
        public float chargeInputBuffer = 0.1f;
        [Tooltip("If true, merely holding Jump in the air also triggers charging on landing (legacy behavior). If false, only a press within chargeInputBuffer before landing triggers charging — matches hop/ground-dash.")]
        public bool allowChargeAirHold = false;

        [Header("Attach Debounce")]
        public float attachToggleWindow = 0.1f;
        public int attachToggleLockoutThreshold = 10;
        public float attachLockoutDuration = 0.1f;

        [Header("Energy Conversion")]
        [Min(0f)] public float initialConversionRate = 2.25f;
        [Min(0f)] public float subsequentConversionRate = 0.9f;
        public float notJumpConversionBonus = 1.3f;
        [Tooltip("Multiplicative bonus on spawnDistance when the flight originated from a hop. 1 = no bonus.")]
        [Min(0f)] public float hopConversionBonus = 1f;
        [Tooltip("Additive bonus on landing shadow v², expressed as a fraction of a full charge jump's v² (baseLaunchPower²). Applied only when the flight originated from a hop.")]
        [Min(0f)] public float hopConversionEnergyBonus = 0f;
        public float aimDriftRadiusScale = 0.7f;
        [Tooltip("Grace window (seconds) after a no-Space landing during which pressing Space still grants energy conversion, as if Space had been held on the landing frame. 0 disables.")]
        [Min(0f)] public float energyConversionCoyoteTime = 0.12f;

        [Header("Stick")]
        public float minStickDuration = 0.3f;
        public float maxStickDuration = 0.6f;
        public float stickSpeedScale = 0.1f;
        public float stickDecelerationTime = 0.05f;
        public float stickSlopeThreshold = 0.7f; // surfaceUp above this = floor-like, no stick
        [Range(0f, 1f)]
        public float stickCrawlMultiplier = 0.2f; // 0 = blocked, 1 = full crawl
        public float stickSlideTransitionRampTime = 0.2f;

        [Header("Charge Entry Grace")]
        public float chargeEntryStickBonus = 0.2f;
        public float chargingSlideGravityScale = 0.5f;

        [Header("Ceiling")]
        [Range(90f, 180f)]
        public float ceilingAngle = 107f;
        [Range(90f, 180f)]
        public float ceilingReattachAngle = 101f;
        public float ceilingThreshold => Mathf.Cos(ceilingAngle * Mathf.Deg2Rad);
        public float ceilingReattachThreshold => Mathf.Cos(ceilingReattachAngle * Mathf.Deg2Rad);

        [Header("Air Control")]
        public float airMaxHorizontalSpeed = 5f;
        public float airAcceleration = 20f;
        public float airDeceleration = 15f;
        [Tooltip("W-hold gravity scale while rising (velocity along gravityUp > 0).")]
        public float floatGravityScaleRising = 1f;
        [Tooltip("W-hold gravity scale while falling.")]
        public float floatGravityScaleFalling = 0.5f;
        [Tooltip("S-hold gravity scale while rising.")]
        public float diveGravityScaleRising = 1f;
        [Tooltip("S-hold gravity scale while falling.")]
        public float diveGravityScaleFalling = 2.0f;
        public float risingGravityScale = 1f;
        public float fallingGravityScale = 1f;
        public float apexThreshold = 0f;
        [Tooltip("Seconds after a charged launch (not hop) during which air control is disabled.")]
        public float postLaunchAirControlLockTime = 0.15f;

        [Header("Air Control — In Fluid")]
        [Tooltip("W-hold gravity scale while submerged. Applied regardless of vertical direction — buoyancy-driven motion has no clean apex.")]
        public float floatGravityScaleInFluid = 0.5f;
        [Tooltip("S-hold gravity scale while submerged. Applied regardless of vertical direction — buoyancy-driven motion has no clean apex.")]
        public float diveGravityScaleInFluid = 2.0f;

        [Header("Physics")]
        public float gravity = -20f;
        public float slimeRadius = 0.4f;
        public float surfaceCheckDistance = 0.05f;
        public float contactSkin = 0.02f;
        public float depenetrationSkin = 0.01f;
        [Tooltip("Min penetration advantage another contact needs to steal primary from the previous primary. Damps frame-to-frame flipping at concave corners.")]
        public float primaryContactHysteresis = 0.005f;
        public float attachedCheckMultiplier = 3f;
        public float airborneCheckMultiplier = 1f;
        public LayerMask groundLayer;

        [Header("Surface Probe")]
        [Tooltip("Contact detector. Single = per-collider ClosestPoint 1회 (기존). Multi = N-probe 샘플링 (multi-path polygon 대응).")]
        public SurfaceProbeMode surfaceProbeMode = SurfaceProbeMode.Single;
        [Tooltip("(Multi) Per-collider probe samples. 8은 multi-path polygon 간격에 충분.")]
        [Range(1, 16)] public int surfaceProbeSamples = 8;
        [Tooltip("(Multi) Probe ring radius as fraction of slimeRadius.")]
        [Range(0f, 1f)] public float surfaceProbeRingScale = 0.8f;
        [Tooltip("(Multi) Dedupe: dot(n1,n2) > 이 값이면 같은 면으로 머지.")]
        [Range(0.9f, 1f)] public float surfaceProbeDedupeDot = 0.97f;
        [Tooltip("EffectiveNormal weight ramp: smoothstep ramp covers this fraction of the soft band.\n" +
                 "0 = OFF (legacy step weight). 1 = ramp covers entire soft band. Recommended: 1.0.")]
        [Range(0f, 1f)] public float effectiveNormalWeightRampBand = 1f;
        [Tooltip("Soft-band multiplier for EffectiveNormal & ProjectVelocityAgainstContacts.\n" +
                 "Contacts within (radius + this × contactSkin) contribute, weighted by smoothstep ramp.\n" +
                 "1 = legacy hard threshold (active = within radius+contactSkin only).\n" +
                 "Higher = wider window, smoother active-set transitions on fast curve slides at the cost of looser into-surface clipping.\n" +
                 "Recommended: 6~10. At slime speed v, gap per frame ≈ v × dt; band must exceed expected gap for continuity.")]
        [Range(1f, 15f)] public float effectiveNormalSoftBandMultiplier = 8f;
        [Tooltip("Phase A depenetration aggregation:\n" +
                 "ON (default): per-collider net push = soft-banded weighted average of unique-face contacts (smooth bisector direction × weighted average depth). Eliminates chord-junction transverse kick on Bezier-sampled curves at high tangent speed.\n" +
                 "OFF (legacy): per-collider net push = Σ n[p]·depth[p]. May produce jitter at chord junctions on concave curves but pushes harder at sharp V-notches.")]
        public bool depenetrationUseSmoothedAggregation = true;
        [Tooltip("Velocity midpoint arc compensation (Phase 2A):\n" +
                 "ON (default): in Attached / Charging, the rewritten `velocity = surfaceTangent × speed` is rotated by ω·dt/2 (ω estimated from prev-frame Δθ). Aims each fixed-step chord at the midpoint of the predicted arc, halving chord-arc sagitta visible through Rigidbody2D interpolation on tight curves at high tangent speed.\n" +
                 "OFF: velocity uses the leading-edge tangent unmodified (legacy).")]
        public bool useSurfaceTangentArcCompensation = true;
        [Tooltip("ProjectVelocityAgainstContacts: contacts whose normal aligns with fallbackNormal (smoothed surfaceNormal) by more than this dot threshold are treated as 'same surface' samples and skipped from multi-clip.\n" +
                 "Same-direction multi-probe samples are noisy; clipping against all of them produces vel jitter as the active set toggles.\n" +
                 "0.7 ≈ 45° tolerance (default). Lower = stricter, more contacts skipped (smoother but less corner safety). Higher = original behavior with more multi-clip.\n" +
                 "fallbackNormal itself always provides a primary-axis clip — corners with truly opposing walls (dot < threshold) still get multi-clipped.")]
        [Range(0f, 1f)] public float projectVelocitySameDirectionDot = 0.7f;

        [Header("Surface Normal Smoothing")]
        [Tooltip("surfaceNormal 저역통과 필터 컷오프(rad/s). 0 = OFF.\n" +
                 "예: 20 → 매 50Hz 프레임당 ~33% 갱신 (90° 튐이 한 프레임에 ~30°로 감쇠, 약 5프레임에 90% 수렴).\n" +
                 "정적 곡면에서도 EffectiveNormal active-set churn (aC=1↔3 swing) 으로 발생하는 normal jitter 를 억제하기 위해 항상 적용됨.")]
        [Min(0f)] public float surfaceNormalSmoothRate = 20f;
        [Tooltip("회전 플랫폼(이 각속도 deg/s 이상) 에서 추가로 적용할 smoothing rate 가중치.\n" +
                 "회전 플랫폼은 기본보다 강하게 smoothing 하고 싶을 때만 사용. 1 = 동일 (기본 rate 만 적용). 2 = 회전 시 2배 rate.")]
        [Min(1f)] public float surfaceNormalSmoothRotatingMultiplier = 1f;
        [Tooltip("이 각속도(deg/s) 이상의 플랫폼에 붙어있을 때 surfaceNormalSmoothRotatingMultiplier 적용.")]
        [Min(0f)] public float surfaceNormalSmoothAngularThreshold = 10f;

        public float postLaunchIgnoreTime = 0.05f;
        public float launchClearanceThreshold = 0.05f;
        public float maxIgnoreTime = 0.15f;

        public float Gravity => gravity;

        [Header("Stunned")]
        [Tooltip("Default stun duration if hazard doesn't specify.")]
        public float defaultStunDuration = 0.5f;
        [Tooltip("Default knockback force if hazard doesn't specify.")]
        public float defaultKnockbackForce = 8f;

        [Header("Default Surface Modifiers (used when no SurfaceTag is present, or as the override-off fallback for tagged surfaces)")]
        public AngleScaledValue carrierFriction = AngleScaledValue.Constant(1f);
        public AngleScaledValue crawlSpeedMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue crawlAccelerationMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue crawlDecelerationMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue crawlOverspeedDecelerationMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideResistanceMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideGravityMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideRampUpTime = AngleScaledValue.Constant(0f);
        public AngleScaledValue slideLinearDrag = AngleScaledValue.Constant(0f);
        public AngleScaledValue slideDownhillGain = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideUphillBrake = AngleScaledValue.Constant(1f);
        public AngleScaledValue landingVelocityRetention = AngleScaledValue.Constant(0f);
        public AngleScaledValue stickDurationMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue impactRedirectRatio = AngleScaledValue.Constant(0f);
        public AngleScaledValue groundDashScale = AngleScaledValue.Constant(1f);
        public AngleScaledValue wallAttachSpeedDampen = AngleScaledValue.Constant(1f);
        public AngleScaledValue hopPowerMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue hopVelocityRetention = AngleScaledValue.Constant(1f);
        public AngleScaledValue hopNormalToTangentRatio = AngleScaledValue.Constant(0f);
        public bool hopInstantFlip = true;
        public bool useInertialSlide = false;
        [Tooltip("Default angle falloff curve for ground dash on untagged surfaces / when overrideGroundDash is off. Domain is [groundDashAngleMin, groundDashAngleMax] normalized to [0,1]. Empty curve = legacy linear.")]
        public AnimationCurve groundDashAngleFalloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        [Tooltip("Default speed falloff curve for ground dash on untagged surfaces / when overrideGroundDash is off. Domain is [dashFalloffStart, dashFalloffEnd] normalized to [0,1]. Empty curve = legacy linear.")]
        public AnimationCurve dashSpeedFalloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        [Tooltip("Skip dash speed falloff entirely (every dash applies full impulse). Global default — surface tags with overrideGroundDash override this.")]
        public bool disableDashFalloff = false;

        public SurfaceModifiers BuildDefaultModifiers() => new SurfaceModifiers
        {
            crawlSpeed = crawlSpeedMultiplier,
            crawlAccel = crawlAccelerationMultiplier,
            crawlDecel = crawlDecelerationMultiplier,
            crawlOverspeedDecel = crawlOverspeedDecelerationMultiplier,
            slideResistance = slideResistanceMultiplier,
            slideGravity = slideGravityMultiplier,
            slideRampUp = slideRampUpTime,
            slideLinearDrag = slideLinearDrag,
            slideDownhillGain = slideDownhillGain,
            slideUphillBrake = slideUphillBrake,
            carrierFriction = carrierFriction,
            landingVelocityRetention = landingVelocityRetention,
            stickDuration = stickDurationMultiplier,
            impactRedirect = impactRedirectRatio,
            wallAttachDampen = wallAttachSpeedDampen,
            groundDashScale = groundDashScale,
            overrideGroundDash = false,
            groundDashImpulse = 0f,
            groundDashAngleMin = 0f,
            groundDashAngleMax = 0f,
            groundDashAngleFalloffCurve = null,
            dashFalloffStart = 0f,
            dashFalloffEnd = 0f,
            dashSpeedFalloffCurve = null,
            disableDashFalloff = false,
            hopPowerMultiplier = hopPowerMultiplier,
            hopVelocityRetention = hopVelocityRetention,
            hopNormalToTangentRatio = hopNormalToTangentRatio,
            hopInstantFlip = hopInstantFlip,
            overrideHopMomentum = false,
            hopMomentumBonus = 0f,
            hopMomentumMaxSpeed = 0f,
            useInertialSlide = useInertialSlide,
            overrideCeilingAngle = false,
            ceilingThresholdOverride = 0f,
            ceilingReattachThresholdOverride = 0f
        };
    }
}
