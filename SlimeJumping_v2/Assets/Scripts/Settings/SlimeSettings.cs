using UnityEngine;

namespace SlimeJumping.Settings
{
    [CreateAssetMenu(fileName = "SlimeSettings", menuName = "SlimeJumping/Slime Settings")]
    public class SlimeSettings : ScriptableObject
    {
        [Header("Ground Movement")]
        public float groundMoveSpeed = 5f;

        [Header("Charge Jump")]
        public float baseLaunchPower = 15f;
        public float fullChargeTime = 1f;

        [Header("Aim Control")]
        public float baseRotationSpeed = 180f;
        [Range(0f, 1f)] public float aimResistFactor = 0.85f;
        public float maxAimAngle = 85f;

        [Header("Air Control")]
        public float airControlAccel = 8f;

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
