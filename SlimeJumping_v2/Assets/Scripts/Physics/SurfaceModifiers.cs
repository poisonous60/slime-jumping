using UnityEngine;
using SlimeJumping.Settings;

namespace SlimeJumping.Physics
{
    public struct SurfaceModifiers
    {
        public AngleScaledValue crawlSpeed;
        public AngleScaledValue crawlAccel;
        public AngleScaledValue crawlDecel;
        public AngleScaledValue crawlOverspeedDecel;
        public AngleScaledValue slideResistance;
        public AngleScaledValue slideGravity;
        public AngleScaledValue slideRampUp;
        public AngleScaledValue slideLinearDrag;
        public AngleScaledValue slideDownhillGain;
        public AngleScaledValue slideUphillBrake;
        public AngleScaledValue carrierFriction;
        public AngleScaledValue landingVelocityRetention;
        public AngleScaledValue landingExcessGain;
        public bool enableLandingTangentSpeedRemap;
        public AnimationCurve landingTangentSpeedRemapCurve;
        public AngleScaledValue stickDuration;
        public AngleScaledValue impactRedirect;
        public AngleScaledValue wallAttachDampen;
        public AngleScaledValue groundDashScale;
        public AngleScaledValue hopPowerMultiplier;
        public AngleScaledValue hopVelocityRetention;
        public AngleScaledValue hopNormalToTangentRatio;
        public bool hopInstantFlip;
        public bool overrideHopMomentum;
        public float hopMomentumBonus;
        public float hopMomentumMaxSpeed;
        public bool overrideGroundDash;
        public float groundDashImpulse;
        public float groundDashAngleMin;
        public float groundDashAngleMax;
        public AnimationCurve groundDashAngleFalloffCurve;
        public float dashFalloffStart;
        public float dashFalloffEnd;
        public AnimationCurve dashSpeedFalloffCurve;
        public bool disableDashFalloff;
        public bool useInertialSlide;
        public bool overrideCeilingAngle;
        public float ceilingThresholdOverride;
        public float ceilingReattachThresholdOverride;
        public bool overrideEnergyConversionCoyoteTime;
        public float energyConversionCoyoteTimeOverride;

        public float GetCeilingThreshold(SlimeSettings settings) =>
            overrideCeilingAngle ? ceilingThresholdOverride : settings.ceilingThreshold;
        public float GetCeilingReattachThreshold(SlimeSettings settings) =>
            overrideCeilingAngle ? ceilingReattachThresholdOverride : settings.ceilingReattachThreshold;
        public float GetEnergyConversionCoyoteTime(SlimeSettings settings) =>
            overrideEnergyConversionCoyoteTime ? energyConversionCoyoteTimeOverride : settings.energyConversionCoyoteTime;

        public static readonly SurfaceModifiers Default = new()
        {
            crawlSpeed = AngleScaledValue.Constant(1f),
            crawlAccel = AngleScaledValue.Constant(1f),
            crawlDecel = AngleScaledValue.Constant(1f),
            crawlOverspeedDecel = AngleScaledValue.Constant(1f),
            slideResistance = AngleScaledValue.Constant(1f),
            slideGravity = AngleScaledValue.Constant(1f),
            slideRampUp = AngleScaledValue.Constant(0f),
            slideLinearDrag = AngleScaledValue.Constant(0f),
            slideDownhillGain = AngleScaledValue.Constant(1f),
            slideUphillBrake = AngleScaledValue.Constant(1f),
            carrierFriction = AngleScaledValue.Constant(1f),
            landingVelocityRetention = AngleScaledValue.Constant(0f),
            landingExcessGain = AngleScaledValue.Constant(1f),
            enableLandingTangentSpeedRemap = false,
            landingTangentSpeedRemapCurve = null,
            stickDuration = AngleScaledValue.Constant(1f),
            impactRedirect = AngleScaledValue.Constant(0f),
            wallAttachDampen = AngleScaledValue.Constant(1f),
            groundDashScale = AngleScaledValue.Constant(1f),
            hopPowerMultiplier = AngleScaledValue.Constant(1f),
            hopVelocityRetention = AngleScaledValue.Constant(1f),
            hopNormalToTangentRatio = AngleScaledValue.Constant(0f),
            hopInstantFlip = true,
            overrideHopMomentum = false,
            hopMomentumBonus = 0f,
            hopMomentumMaxSpeed = 0f,
            overrideGroundDash = false,
            groundDashImpulse = 0f,
            groundDashAngleMin = 0f,
            groundDashAngleMax = 0f,
            groundDashAngleFalloffCurve = null,
            dashFalloffStart = 0f,
            dashFalloffEnd = 0f,
            dashSpeedFalloffCurve = null,
            disableDashFalloff = false,
            useInertialSlide = false,
            overrideCeilingAngle = false,
            ceilingThresholdOverride = 0f,
            ceilingReattachThresholdOverride = 0f,
            overrideEnergyConversionCoyoteTime = false,
            energyConversionCoyoteTimeOverride = 0f
        };

        /// <summary>
        /// Component-wise lerp for blending two surface profiles by a 0..1 weight.
        /// AngleScaledValue / float fields use linear lerp; bool fields and curves
        /// snap at t=0.5 (no graceful interpolation possible — discrete by nature).
        /// AnimationCurve references prefer the snapped side rather than null.
        /// Used by snow coverage to smoothly blend the host SurfaceMaterial with the
        /// snow SurfaceMaterial as carve depth varies along the patch.
        /// </summary>
        public static SurfaceModifiers Lerp(SurfaceModifiers a, SurfaceModifiers b, float t)
        {
            t = Mathf.Clamp01(t);
            bool snapToB = t >= 0.5f;
            return new SurfaceModifiers
            {
                crawlSpeed                = AngleScaledValue.Lerp(a.crawlSpeed,                b.crawlSpeed,                t),
                crawlAccel                = AngleScaledValue.Lerp(a.crawlAccel,                b.crawlAccel,                t),
                crawlDecel                = AngleScaledValue.Lerp(a.crawlDecel,                b.crawlDecel,                t),
                crawlOverspeedDecel       = AngleScaledValue.Lerp(a.crawlOverspeedDecel,       b.crawlOverspeedDecel,       t),
                slideResistance           = AngleScaledValue.Lerp(a.slideResistance,           b.slideResistance,           t),
                slideGravity              = AngleScaledValue.Lerp(a.slideGravity,              b.slideGravity,              t),
                slideRampUp               = AngleScaledValue.Lerp(a.slideRampUp,               b.slideRampUp,               t),
                slideLinearDrag           = AngleScaledValue.Lerp(a.slideLinearDrag,           b.slideLinearDrag,           t),
                slideDownhillGain         = AngleScaledValue.Lerp(a.slideDownhillGain,         b.slideDownhillGain,         t),
                slideUphillBrake          = AngleScaledValue.Lerp(a.slideUphillBrake,          b.slideUphillBrake,          t),
                carrierFriction           = AngleScaledValue.Lerp(a.carrierFriction,           b.carrierFriction,           t),
                landingVelocityRetention  = AngleScaledValue.Lerp(a.landingVelocityRetention,  b.landingVelocityRetention,  t),
                landingExcessGain         = AngleScaledValue.Lerp(a.landingExcessGain,         b.landingExcessGain,         t),
                enableLandingTangentSpeedRemap = snapToB ? b.enableLandingTangentSpeedRemap : a.enableLandingTangentSpeedRemap,
                landingTangentSpeedRemapCurve = snapToB && b.landingTangentSpeedRemapCurve != null
                    ? b.landingTangentSpeedRemapCurve
                    : a.landingTangentSpeedRemapCurve ?? b.landingTangentSpeedRemapCurve,
                stickDuration             = AngleScaledValue.Lerp(a.stickDuration,             b.stickDuration,             t),
                impactRedirect            = AngleScaledValue.Lerp(a.impactRedirect,            b.impactRedirect,            t),
                wallAttachDampen          = AngleScaledValue.Lerp(a.wallAttachDampen,          b.wallAttachDampen,          t),
                groundDashScale           = AngleScaledValue.Lerp(a.groundDashScale,           b.groundDashScale,           t),
                hopPowerMultiplier        = AngleScaledValue.Lerp(a.hopPowerMultiplier,        b.hopPowerMultiplier,        t),
                hopVelocityRetention      = AngleScaledValue.Lerp(a.hopVelocityRetention,      b.hopVelocityRetention,      t),
                hopNormalToTangentRatio   = AngleScaledValue.Lerp(a.hopNormalToTangentRatio,   b.hopNormalToTangentRatio,   t),

                hopInstantFlip          = snapToB ? b.hopInstantFlip          : a.hopInstantFlip,
                overrideHopMomentum     = snapToB ? b.overrideHopMomentum     : a.overrideHopMomentum,
                hopMomentumBonus        = Mathf.Lerp(a.hopMomentumBonus,        b.hopMomentumBonus,        t),
                hopMomentumMaxSpeed     = Mathf.Lerp(a.hopMomentumMaxSpeed,     b.hopMomentumMaxSpeed,     t),
                overrideGroundDash      = snapToB ? b.overrideGroundDash      : a.overrideGroundDash,
                groundDashImpulse       = Mathf.Lerp(a.groundDashImpulse,       b.groundDashImpulse,       t),
                groundDashAngleMin      = Mathf.Lerp(a.groundDashAngleMin,      b.groundDashAngleMin,      t),
                groundDashAngleMax      = Mathf.Lerp(a.groundDashAngleMax,      b.groundDashAngleMax,      t),
                groundDashAngleFalloffCurve = snapToB && b.groundDashAngleFalloffCurve != null
                    ? b.groundDashAngleFalloffCurve
                    : a.groundDashAngleFalloffCurve ?? b.groundDashAngleFalloffCurve,
                dashFalloffStart        = Mathf.Lerp(a.dashFalloffStart,        b.dashFalloffStart,        t),
                dashFalloffEnd          = Mathf.Lerp(a.dashFalloffEnd,          b.dashFalloffEnd,          t),
                dashSpeedFalloffCurve = snapToB && b.dashSpeedFalloffCurve != null
                    ? b.dashSpeedFalloffCurve
                    : a.dashSpeedFalloffCurve ?? b.dashSpeedFalloffCurve,
                disableDashFalloff      = snapToB ? b.disableDashFalloff      : a.disableDashFalloff,
                useInertialSlide        = snapToB ? b.useInertialSlide        : a.useInertialSlide,
                overrideCeilingAngle    = snapToB ? b.overrideCeilingAngle    : a.overrideCeilingAngle,
                ceilingThresholdOverride         = Mathf.Lerp(a.ceilingThresholdOverride,         b.ceilingThresholdOverride,         t),
                ceilingReattachThresholdOverride = Mathf.Lerp(a.ceilingReattachThresholdOverride, b.ceilingReattachThresholdOverride, t),
                overrideEnergyConversionCoyoteTime = snapToB ? b.overrideEnergyConversionCoyoteTime : a.overrideEnergyConversionCoyoteTime,
                energyConversionCoyoteTimeOverride = Mathf.Lerp(a.energyConversionCoyoteTimeOverride, b.energyConversionCoyoteTimeOverride, t),
            };
        }
    }
}
