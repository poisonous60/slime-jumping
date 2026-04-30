using UnityEngine;

namespace SlimeJumping.Physics
{
    [CreateAssetMenu(menuName = "SlimeJumping/Surface Material")]
    public class SurfaceMaterial : ScriptableObject
    {
        [Header("Carrier — moving platform friction")]
        [Tooltip("0 = ice (platform moves, slime stays), 1 = full follow")]
        public AngleScaledValue carrierFriction = AngleScaledValue.Constant(1f);

        [Header("Crawl")]
        public AngleScaledValue crawlSpeedMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue crawlAccelerationMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue crawlDecelerationMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue crawlOverspeedDecelerationMultiplier = AngleScaledValue.Constant(1f);

        [Header("Slide")]
        [Tooltip("0 = ice (no resistance), 2+ = mud (high resistance)")]
        public AngleScaledValue slideResistanceMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideGravityMultiplier = AngleScaledValue.Constant(1f);
        [Tooltip("Time (seconds) for slide to ramp from 0 to full after landing. 0 = instant.")]
        public AngleScaledValue slideRampUpTime = AngleScaledValue.Constant(0f);
        [Tooltip("Inertial-slide only. Linear drag coefficient (1/s) applied as slideVelocity *= (1 - k*dt). 0 = none. 0.5–1.5 ≈ mild ice drag (climb shorter than drop).")]
        public AngleScaledValue slideLinearDrag = AngleScaledValue.Constant(0f);
        [Tooltip("Inertial-slide only. Multiplier on tangent gravity when it accelerates motion (slideAccel sign matches slideVelocity). 1 = neutral, >1 = sharper descent speed-up.")]
        public AngleScaledValue slideDownhillGain = AngleScaledValue.Constant(1f);
        [Tooltip("Inertial-slide only. Multiplier on tangent gravity when it opposes motion (slideAccel sign opposes slideVelocity). 1 = neutral, >1 = sharper ascent slow-down.")]
        public AngleScaledValue slideUphillBrake = AngleScaledValue.Constant(1f);

        [Header("Landing")]
        [Tooltip("0 = instant stop (default), 1 = full tangent velocity preserved")]
        public AngleScaledValue landingVelocityRetention = AngleScaledValue.Constant(0f);
        public AngleScaledValue stickDurationMultiplier = AngleScaledValue.Constant(1f);
        [Tooltip("0 = absorb normal impact (default), 0.5 = redirect 50% of normal speed to tangent")]
        public AngleScaledValue impactRedirectRatio = AngleScaledValue.Constant(0f);

        [Header("Ground Dash")]
        [Tooltip("Ground dash impulse multiplier. 0 = no dash on this surface")]
        public AngleScaledValue groundDashScale = AngleScaledValue.Constant(1f);
        [Tooltip("Override the SlimeSettings ground-dash tuning with the values below. OFF = use global SlimeSettings (default).")]
        public bool overrideGroundDash = false;
        [Min(0f)] public float groundDashImpulse = 8f;
        [Tooltip("Domain start (deg) for the angle falloff curve. The surface angle is normalized to t ∈ [0,1] across [angleMin, angleMax] before being fed into the curve.")]
        public float groundDashAngleMin = 30f;
        [Tooltip("Domain end (deg) for the angle falloff curve.")]
        public float groundDashAngleMax = 60f;
        [Tooltip("Maps normalized angle t ∈ [0,1] (0 = at angleMin, 1 = at angleMax) → angle scale. Default linear (0,1)→(1,0) matches the legacy 1 - InverseLerp(min,max,angle) behavior. Use this to make floors get full dash and walls get nothing with a custom curve in between.")]
        public AnimationCurve groundDashAngleFalloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        [Tooltip("Domain start (m/s) for the speed falloff curve. Same-direction tangent speed is normalized to t ∈ [0,1] across [dashFalloffStart, dashFalloffEnd] before being fed into the curve.")]
        [Min(0f)] public float dashFalloffStart = 5f;
        [Tooltip("Domain end (m/s) for the speed falloff curve.")]
        [Min(0f)] public float dashFalloffEnd = 13f;
        [Tooltip("Maps normalized same-direction speed t ∈ [0,1] (0 = at start, 1 = at end) → dash strength scale. Default linear (0,1)→(1,0) matches legacy 1 - InverseLerp(start,end,sameDirSpeed) behavior.")]
        public AnimationCurve dashSpeedFalloffCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        [Tooltip("Skip dash speed falloff entirely — every dash applies full impulse regardless of current tangent speed. Use for ice where the player should keep stacking dashes. Requires overrideGroundDash = true. Overrides dashSpeedFalloffCurve.")]
        public bool disableDashFalloff = false;

        [Header("Wall Attach")]
        [Tooltip("Speed multiplier when attaching to surface. 1 = no dampen, 0.5 = half speed")]
        public AngleScaledValue wallAttachSpeedDampen = AngleScaledValue.Constant(1f);

        [Header("Hop")]
        [Tooltip("Tangent velocity retained on Hop. 1 = full preserve (default), 0 = wipe tangent (W acts as pure kill-brake). Ice → keep 1. Sticky mud → 0. Operates on Dot(velocity, surfaceTangent).")]
        public AngleScaledValue hopVelocityRetention = AngleScaledValue.Constant(1f);
        [Tooltip("Normal-component redirection on Hop. 0 (default) = discard normal velocity (legacy). 1 = the absolute value of |Dot(velocity, surfaceNormal)| is added to the tangent direction at full magnitude. Direction sign comes from existing tangent component, falling back to input direction. Use on ice/halfpipes where incoming flight perpendicular to the surface should slingshot along it.")]
        public AngleScaledValue hopNormalToTangentRatio = AngleScaledValue.Constant(0f);
        [Tooltip("Opposing-input Hop behavior. ON (default) = zero existing tangent and build bonus in input direction same frame (arcade-responsive, good for grass/mud). OFF = decelerate toward 0 by hopMomentumBonus, never flip in one hop (inertial / ice).")]
        public bool hopInstantFlip = true;
        [Tooltip("Override the SlimeSettings hop-momentum tuning with the values below. OFF = use global SlimeSettings (default).")]
        public bool overrideHopMomentum = false;
        [Tooltip("Tangent speed bonus added per hop in input direction.")]
        [Min(0f)] public float hopMomentumBonus = 1.5f;
        [Tooltip("Maximum tangent speed achievable via hop momentum accumulation.")]
        [Min(0f)] public float hopMomentumMaxSpeed = 20f;

        [Header("Slide Physics")]
        [Tooltip("ON = inertial slide (gravity-along-tangent integrated as acceleration; slideResistance is friction in m/s²; landed tangent velocity flows into the inertial channel). OFF (default, untagged surfaces) = legacy steady-state slide where slideSpeed is recomputed each frame from current tangent gravity. Use ON for ice / halfpipes where kinetic energy must convert back to height.")]
        public bool useInertialSlide = false;

        [Header("Audio")]
        [Tooltip("Hard landing clip override. Null → SlimeLocomotionAudio's default landHardClip plays.")]
        public AudioClip landHardClip;
        [Tooltip("Soft landing clip override. Null → SlimeLocomotionAudio's default landSoftClip plays.")]
        public AudioClip landSoftClip;
        [Tooltip("Charged Launch clip override. Null → SlimeLocomotionAudio's default launchClip plays.")]
        public AudioClip launchClip;
        [Tooltip("Hop clip override. Null → SlimeLocomotionAudio's default hopClip plays.")]
        public AudioClip hopClip;
        [Tooltip("Volume for this surface's landHardClip override. 0 = inherit SlimeLocomotionAudio.landHardVolume. Only used when landHardClip is also set.")]
        [Range(0f, 1f)] public float landHardVolume = 0f;
        [Tooltip("Volume for this surface's landSoftClip override. 0 = inherit SlimeLocomotionAudio.landSoftVolume. Only used when landSoftClip is also set.")]
        [Range(0f, 1f)] public float landSoftVolume = 0f;
        [Tooltip("Volume for this surface's launchClip override. 0 = inherit SlimeLocomotionAudio.launchVolume. Only used when launchClip is also set.")]
        [Range(0f, 1f)] public float launchVolume = 0f;
        [Tooltip("Volume for this surface's hopClip override. 0 = inherit SlimeLocomotionAudio.hopVolume. Only used when hopClip is also set.")]
        [Range(0f, 1f)] public float hopVolume = 0f;

        [Header("Ceiling")]
        [Tooltip("Override the global ceiling thresholds with the values below. ON lets sticky surfaces extend the attachable arc past 107° (e.g. 180° = slime climbs full overhangs) or slippery surfaces tighten it.")]
        public bool overrideCeilingAngle = false;
        [Range(90f, 180f)]
        [Tooltip("Detach / probe-filter angle. surfaceUp below cos(this) → contact treated as ceiling.")]
        public float ceilingAngle = 107f;
        [Range(90f, 180f)]
        [Tooltip("Re-attach angle (Airborne landing). Hysteresis: must be ≤ ceilingAngle.")]
        public float ceilingReattachAngle = 101f;

        public SurfaceModifiers ToModifiers() => new SurfaceModifiers
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
            hopVelocityRetention = hopVelocityRetention,
            hopNormalToTangentRatio = hopNormalToTangentRatio,
            hopInstantFlip = hopInstantFlip,
            overrideHopMomentum = overrideHopMomentum,
            hopMomentumBonus = hopMomentumBonus,
            hopMomentumMaxSpeed = hopMomentumMaxSpeed,
            overrideGroundDash = overrideGroundDash,
            groundDashImpulse = groundDashImpulse,
            groundDashAngleMin = groundDashAngleMin,
            groundDashAngleMax = groundDashAngleMax,
            groundDashAngleFalloffCurve = groundDashAngleFalloffCurve,
            dashFalloffStart = dashFalloffStart,
            dashFalloffEnd = dashFalloffEnd,
            dashSpeedFalloffCurve = dashSpeedFalloffCurve,
            disableDashFalloff = disableDashFalloff,
            useInertialSlide = useInertialSlide,
            overrideCeilingAngle = overrideCeilingAngle,
            ceilingThresholdOverride = Mathf.Cos(ceilingAngle * Mathf.Deg2Rad),
            ceilingReattachThresholdOverride = Mathf.Cos(ceilingReattachAngle * Mathf.Deg2Rad)
        };
    }
}
