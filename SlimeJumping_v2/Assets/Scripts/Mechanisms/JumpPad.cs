using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Platform effect that launches the slime on contact.</summary>
    public class JumpPad : MonoBehaviour, IPlatformEffect
    {
        [Header("Jump Pad")]
        [SerializeField] private float launchForce = 20f;
        [Tooltip("Launch direction in local space. Auto-normalized.")]
        [SerializeField] private Vector2 launchDirection = Vector2.up;
        [SerializeField] private float cooldown = 0.3f;

        [Header("VFX (optional)")]
        [Tooltip("Space-warp shockwave fired on each launch. Leave null to disable.")]
        [SerializeField] private SpaceWarpEffect warpEffect;

        private float cooldownTimer;

        public void OnSlimeAttach(SlimeController slime)
        {
            if (cooldownTimer > 0f) return;

            Vector2 worldDir = ((Vector2)transform.TransformDirection(launchDirection)).normalized;
            slime.ForceAirborneWithVelocity(worldDir * launchForce);
            cooldownTimer = cooldown;

            if (warpEffect != null) warpEffect.TriggerStretch(slime.transform);
        }

        public void OnSlimeDetach(SlimeController slime) { }
        public void OnSlimeStay(SlimeController slime, float dt) { }

        void FixedUpdate()
        {
            if (cooldownTimer > 0f)
                cooldownTimer -= Time.fixedDeltaTime;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.6f);
            Vector3 center = transform.position;
            Vector3 dir = transform.TransformDirection(launchDirection).normalized;
            float len = Mathf.Clamp(launchForce * 0.05f, 0.5f, 3f);

            Gizmos.DrawLine(center, center + dir * len);
            Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
            Vector3 tip = center + dir * len;
            Gizmos.DrawLine(tip, tip - dir * 0.25f + perp * 0.12f);
            Gizmos.DrawLine(tip, tip - dir * 0.25f - perp * 0.12f);
        }
    }
}
