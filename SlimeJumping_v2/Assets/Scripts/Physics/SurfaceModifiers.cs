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
        public AngleScaledValue stickDuration;
        public AngleScaledValue impactRedirect;
        public AngleScaledValue wallAttachDampen;
        public AngleScaledValue groundDashScale;
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

        public float GetCeilingThreshold(SlimeSettings settings) =>
            overrideCeilingAngle ? ceilingThresholdOverride : settings.ceilingThreshold;
        public float GetCeilingReattachThreshold(SlimeSettings settings) =>
            overrideCeilingAngle ? ceilingReattachThresholdOverride : settings.ceilingReattachThreshold;

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
            stickDuration = AngleScaledValue.Constant(1f),
            impactRedirect = AngleScaledValue.Constant(0f),
            wallAttachDampen = AngleScaledValue.Constant(1f),
            groundDashScale = AngleScaledValue.Constant(1f),
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
            ceilingReattachThresholdOverride = 0f
        };
    }
}
