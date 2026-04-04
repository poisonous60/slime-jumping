using UnityEngine;

namespace SlimeJumping.Physics
{
    [CreateAssetMenu(menuName = "SlimeJumping/Surface Material")]
    public class SurfaceMaterial : ScriptableObject
    {
        [Header("Carrier — moving platform friction")]
        [Tooltip("0 = ice (platform moves, slime stays), 1 = full follow")]
        public float carrierFriction = 1f;

        [Header("Crawl")]
        public float crawlSpeedMultiplier = 1f;
        public float crawlAccelerationMultiplier = 1f;
        public float crawlDecelerationMultiplier = 1f;

        [Header("Slide")]
        [Tooltip("0 = ice (no resistance), 2+ = mud (high resistance)")]
        public float slideResistanceMultiplier = 1f;
        public float slideGravityMultiplier = 1f;

        [Header("Landing")]
        [Tooltip("0 = instant stop (default), 1 = full tangent velocity preserved")]
        public float landingVelocityRetention = 0f;
        public float stickDurationMultiplier = 1f;
        [Tooltip("0 = absorb normal impact (default), 0.5 = redirect 50% of normal speed to tangent")]
        public float impactRedirectRatio = 0f;

        private void OnValidate()
        {
            carrierFriction = Mathf.Clamp01(carrierFriction);
            crawlSpeedMultiplier = Mathf.Max(0f, crawlSpeedMultiplier);
            crawlAccelerationMultiplier = Mathf.Max(0f, crawlAccelerationMultiplier);
            crawlDecelerationMultiplier = Mathf.Max(0f, crawlDecelerationMultiplier);
            slideResistanceMultiplier = Mathf.Max(0f, slideResistanceMultiplier);
            slideGravityMultiplier = Mathf.Max(0f, slideGravityMultiplier);
            landingVelocityRetention = Mathf.Clamp01(landingVelocityRetention);
            stickDurationMultiplier = Mathf.Max(0f, stickDurationMultiplier);
            impactRedirectRatio = Mathf.Max(0f, impactRedirectRatio);
        }

        public SurfaceModifiers ToModifiers() => new SurfaceModifiers
        {
            crawlSpeed = crawlSpeedMultiplier,
            crawlAccel = crawlAccelerationMultiplier,
            crawlDecel = crawlDecelerationMultiplier,
            slideResistance = slideResistanceMultiplier,
            slideGravity = slideGravityMultiplier,
            carrierFriction = carrierFriction,
            landingVelocityRetention = landingVelocityRetention,
            stickDuration = stickDurationMultiplier,
            impactRedirect = impactRedirectRatio
        };
    }
}
