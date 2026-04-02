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

        [Header("Charge Circle")]
        public float chargeRadius = 1.5f;
        public float autoDriftSpeed = 1.5f;
        public float inputAcceleration = 3.0f;
        public float baseLaunchPower = 15f;
        public float minLaunchRatio = 0.05f;
        public float maxAimAngularSpeed = 180f;

        [Header("Energy Conversion")]
        public float heightRestorationRatio = 2.25f;
        public float driftCompensation = 0.7f;

        [Header("Stick")]
        public float attachedStickDuration = 0.3f;
        public float chargingStickDuration = 0.2f;
        public float stickSlopeThreshold = 0.7f; // surfaceUp above this = floor-like, no stick
        public float ceilingThreshold = -0.3f; // surfaceUp below this = ceiling, can't attach

        [Header("Air Control")]
        public float airMaxHorizontalSpeed = 5f;
        public float airAcceleration = 20f;
        public float airDeceleration = 15f;

        [Header("Physics")]
        public float gravity = -20f;
        public float slimeRadius = 0.4f;
        public float surfaceCheckDistance = 0.05f;
        public LayerMask groundLayer;
        public float postLaunchIgnoreTime = 0.05f;
        public float launchClearanceThreshold = 0.05f;
        public float maxIgnoreTime = 0.15f;
    }
}
