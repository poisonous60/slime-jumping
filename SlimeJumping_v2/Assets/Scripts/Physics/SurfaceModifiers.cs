using UnityEngine;

namespace SlimeJumping.Physics
{
    public struct SurfaceModifiers
    {
        public AngleScaledValue crawlSpeed;
        public AngleScaledValue crawlAccel;
        public AngleScaledValue crawlDecel;
        public AngleScaledValue slideResistance;
        public AngleScaledValue slideGravity;
        public AngleScaledValue slideRampUp;
        public AngleScaledValue carrierFriction;
        public AngleScaledValue landingVelocityRetention;
        public AngleScaledValue stickDuration;
        public AngleScaledValue impactRedirect;
        public AngleScaledValue wallAttachDampen;
        public AngleScaledValue hopScale;

        public static readonly SurfaceModifiers Default = new()
        {
            crawlSpeed = AngleScaledValue.Constant(1f),
            crawlAccel = AngleScaledValue.Constant(1f),
            crawlDecel = AngleScaledValue.Constant(1f),
            slideResistance = AngleScaledValue.Constant(1f),
            slideGravity = AngleScaledValue.Constant(1f),
            slideRampUp = AngleScaledValue.Constant(0f),
            carrierFriction = AngleScaledValue.Constant(1f),
            landingVelocityRetention = AngleScaledValue.Constant(0f),
            stickDuration = AngleScaledValue.Constant(1f),
            impactRedirect = AngleScaledValue.Constant(0f),
            wallAttachDampen = AngleScaledValue.Constant(1f),
            hopScale = AngleScaledValue.Constant(1f)
        };
    }
}
