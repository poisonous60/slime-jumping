namespace SlimeJumping.Physics
{
    public struct SurfaceModifiers
    {
        public float crawlSpeed;
        public float crawlAccel;
        public float crawlDecel;
        public float slideResistance;
        public float slideGravity;
        public float carrierFriction;
        public float landingVelocityRetention;
        public float stickDuration;
        public float impactRedirect;

        public static readonly SurfaceModifiers Default = new()
        {
            crawlSpeed = 1f, crawlAccel = 1f, crawlDecel = 1f,
            slideResistance = 1f, slideGravity = 1f, carrierFriction = 1f,
            landingVelocityRetention = 0f, stickDuration = 1f, impactRedirect = 0f
        };
    }
}
