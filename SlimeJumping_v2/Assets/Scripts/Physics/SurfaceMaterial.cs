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

        [Header("Slide")]
        [Tooltip("0 = ice (no resistance), 2+ = mud (high resistance)")]
        public AngleScaledValue slideResistanceMultiplier = AngleScaledValue.Constant(1f);
        public AngleScaledValue slideGravityMultiplier = AngleScaledValue.Constant(1f);
        [Tooltip("Time (seconds) for slide to ramp from 0 to full after landing. 0 = instant.")]
        public AngleScaledValue slideRampUpTime = AngleScaledValue.Constant(0f);

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

        public SurfaceModifiers ToModifiers() => new SurfaceModifiers
        {
            crawlSpeed = crawlSpeedMultiplier,
            crawlAccel = crawlAccelerationMultiplier,
            crawlDecel = crawlDecelerationMultiplier,
            slideResistance = slideResistanceMultiplier,
            slideGravity = slideGravityMultiplier,
            slideRampUp = slideRampUpTime,
            carrierFriction = carrierFriction,
            landingVelocityRetention = landingVelocityRetention,
            stickDuration = stickDurationMultiplier,
            impactRedirect = impactRedirectRatio,
            wallAttachDampen = wallAttachSpeedDampen,
            groundDashScale = groundDashScale
        };
    }
}
