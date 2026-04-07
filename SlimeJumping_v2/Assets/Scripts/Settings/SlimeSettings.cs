using UnityEngine;

namespace SlimeJumping.Settings
{
    [CreateAssetMenu(fileName = "SlimeSettings", menuName = "SlimeJumping/Slime Settings")]
    public class SlimeSettings : ScriptableObject
    {
        [Header("Surface Movement")]
        public float crawlSpeed = 5f;
        public float crawlAcceleration = 50f;
        public float crawlDeceleration = 30f;
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
        public float hopPower = 2.25f;
        public float hopCooldown = 0.1f;
        public float hopBufferDuration = 0.1f;
        [Range(0f, 1f)]
        public float hopLandingRetention = 1f;

        [Header("Hyper Dash")]
        [Range(0f, 1f)] public float tangentInheritanceFactor = 0.7f;
        [Tooltip("Crawl speed below this threshold is ignored during tangent inheritance. 0 = inherit full crawl speed.")]
        [Min(0f)] public float hyperDashDeduction = 5f;

        [Header("Charge Circle")]
        [Min(0.01f)] public float chargeRadius = 1.5f;
        public float autoDriftSpeed = 1.5f;
        public float inputAcceleration = 3.0f;
        [Min(0.01f)] public float baseLaunchPower = 15f;
        public float minLaunchPower = 3f;
        public float chargeCurveExponent = 1f;
        public float maxAimAngularSpeed = 180f;
        public float chargeNormalFollowSpeed = 5f;
        public float chargingCoyoteTime = 0.2f;
        public float coyoteGraceTime = 0.1f;
        public int chargingSurfaceProbeGraceFrames = 3;

        [Header("Attach Debounce")]
        public float attachToggleWindow = 0.1f;
        public int attachToggleLockoutThreshold = 10;
        public float attachLockoutDuration = 0.1f;

        [Header("Energy Conversion")]
        [Min(0f)] public float initialConversionRate = 2.25f;
        [Range(0f, 1f)]
        public float subsequentConversionRate = 0.9f;
        public float notJumpConversionBonus = 1.3f;
        public float aimDriftRadiusScale = 0.7f;

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
        public float floatGravityScale = 0.5f;
        public float diveGravityScale = 2.0f;
        public float risingGravityScale = 1f;
        public float fallingGravityScale = 1f;
        public float apexThreshold = 0f;

        [Header("Physics")]
        public float gravity = -20f;
        public float slimeRadius = 0.4f;
        public float surfaceCheckDistance = 0.05f;
        public float attachedCheckMultiplier = 3f;
        public float airborneCheckMultiplier = 1f;
        public LayerMask groundLayer;
        public float postLaunchIgnoreTime = 0.05f;
        public float launchClearanceThreshold = 0.05f;
        public float maxIgnoreTime = 0.15f;
    }
}
