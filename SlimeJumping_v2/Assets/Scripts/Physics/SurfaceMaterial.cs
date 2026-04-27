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

        [Header("Wall Attach")]
        [Tooltip("Speed multiplier when attaching to surface. 1 = no dampen, 0.5 = half speed")]
        public AngleScaledValue wallAttachSpeedDampen = AngleScaledValue.Constant(1f);

        [Header("Hop")]
        [Tooltip("Tangent velocity retained on Hop. 1 = full preserve (default), 0 = wipe tangent (W acts as pure kill-brake). Ice → keep 1. Sticky mud → 0.")]
        public AngleScaledValue hopVelocityRetention = AngleScaledValue.Constant(1f);
        [Tooltip("Opposing-input Hop behavior. ON (default) = zero existing tangent and build bonus in input direction same frame (arcade-responsive, good for grass/mud). OFF = decelerate toward 0 by hopMomentumBonus, never flip in one hop (inertial / ice).")]
        public bool hopInstantFlip = true;

        [Header("Slide Physics")]
        [Tooltip("ON = inertial slide (gravity-along-tangent integrated as acceleration; slideResistance is friction in m/s²; landed tangent velocity flows into the inertial channel). OFF (default, untagged surfaces) = legacy steady-state slide where slideSpeed is recomputed each frame from current tangent gravity. Use ON for ice / halfpipes where kinetic energy must convert back to height.")]
        public bool useInertialSlide = false;

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
            hopInstantFlip = hopInstantFlip,
            useInertialSlide = useInertialSlide
        };
    }
}
