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
        public float contactSkin = 0.02f;
        public float depenetrationSkin = 0.01f;
        public float attachedCheckMultiplier = 3f;
        public float airborneCheckMultiplier = 1f;
        public LayerMask groundLayer;
        public float postLaunchIgnoreTime = 0.05f;
        public float launchClearanceThreshold = 0.05f;
        public float maxIgnoreTime = 0.15f;

        [Header("Deformation General")]
        public float deformSmoothTime = 0.1f;
        public float offsetSmoothTime = 0.1f;
        public float deformAxisSlerpSpeed = 15f;
        public float deformScaleMin = 0.4f;
        public float deformScaleMax = 1.8f;

        [Header("Surface Squash (C-5)")]
        public float surfaceSquashIntensity = 0.12f;
        public float surfaceSquashAsymmetry = 0.3f;
        public float gravitySagDistance = 0.06f;

        [Header("Impact Squash (C-2)")]
        public float impactSquashIntensity = 0.25f;
        public float impactDuration = 0.3f;
        public float impactDecayRate = 8f;
        public float impactOscFreq = 2.5f;
        public float impactSpeedReference = 20f;
        public float impactMinThreshold = 0.05f;

        [Header("Velocity Stretch (C-4)")]
        public float velocityStretchIntensity = 0.15f;
        public float velocityStretchSpeedRef = 15f;
        public float velocityStretchMax = 0.3f;
        public float velocityLeanDistance = 0.05f;
        public float velocityLeanMax = 0.08f;
        public float velocityAxisThreshold = 1f;

        [Header("Breathing (C-6)")]
        public float breathAmplitude = 0.02f;
        public float breathFrequency = 0.4f;

        [Header("Face Tracking")]
        public float faceOffsetScale = 0.03f;
        public float faceOffsetMax = 0.2f;
        public float faceSmoothTime = 0.1f;
        public float faceChargingScale = 0.2f;
        public float faceLingerThreshold = 0.1f;
        public float faceLingerDecay = 1.5f;

        [Header("Face Idle Drift")]
        public float faceDriftAmplitude = 0.01f;
        public float faceDriftFreqX = 0.7f;
        public float faceDriftFreqY = 0.9f;
        public float faceDriftSharpness = 4f;

        [Header("Blink")]
        public float blinkDuration = 0.1f;
        public float blinkIntervalMin = 3f;
        public float blinkIntervalMax = 7f;
    }
}
