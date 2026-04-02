using UnityEngine;

namespace SlimeJumping.Settings
{
    [CreateAssetMenu(fileName = "SlimeSettings", menuName = "SlimeJumping/Slime Settings")]
    public class SlimeSettings : ScriptableObject
    {
        [Header("Ground Movement")]
        public float groundMoveSpeed = 5f;

        [Header("Charge Circle")]
        public float chargeRadius = 1.5f;
        public float autoDriftSpeed = 1.5f;
        public float inputAcceleration = 3.0f;
        public float baseLaunchPower = 15f;

        [Header("Air Control")]
        public float airControlAngularSpeed = 200f;

        [Header("Physics")]
        public float gravity = -20f;
        public float slimeRadius = 0.4f;
        public float groundCheckDistance = 0.1f;
        public LayerMask groundLayer;
        public float postLaunchIgnoreTime = 0.05f;
        public float launchClearanceThreshold = 0.05f;
        public float maxIgnoreTime = 0.15f;
    }
}
