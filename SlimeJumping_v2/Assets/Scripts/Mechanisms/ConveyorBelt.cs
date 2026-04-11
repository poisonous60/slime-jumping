using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Platform effect that pushes the slime along the conveyor direction while attached.
    /// Uses AddExternalVelocity — SlimeController projects it onto surfaceTangent
    /// for non-Airborne states (SlimeController.cs FixedUpdate).
    /// </summary>
    public class ConveyorBelt : MonoBehaviour, IPlatformEffect
    {
        [Header("Conveyor")]
        [Tooltip("Push speed (units/s²). Acceleration, not velocity.")]
        [SerializeField] private float speed = 8f;
        [Tooltip("Conveyor direction in local space. Auto-normalized.")]
        [SerializeField] private Vector2 localDirection = Vector2.right;
        [Tooltip("Max speed along conveyor direction before falloff. 0 = unlimited.")]
        [SerializeField] private float terminalSpeed = 12f;

        public void OnSlimeAttach(SlimeController slime) { }
        public void OnSlimeDetach(SlimeController slime) { }

        public void OnSlimeStay(SlimeController slime, float dt)
        {
            Vector2 worldDir = ((Vector2)transform.TransformDirection(localDirection)).normalized;
            float accel = speed;

            if (terminalSpeed > 0f)
            {
                float along = Vector2.Dot(slime.Velocity, worldDir);
                if (along > 0f)
                    accel *= Mathf.Clamp01(1f - along / terminalSpeed);
            }

            slime.AddExternalVelocity(worldDir * accel * dt);
        }

        // --- Gizmos ---

        void OnDrawGizmos()
        {
            if (localDirection.sqrMagnitude < 0.0001f) return;

            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.6f);
            Vector3 center = transform.position;
            Vector3 dir = transform.TransformDirection(localDirection).normalized;
            float len = 1.2f;

            // Three parallel flow arrows
            Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
            for (int i = -1; i <= 1; i++)
            {
                Vector3 start = center + perp * (i * 0.3f);
                Gizmos.DrawLine(start, start + dir * len);
                Vector3 tip = start + dir * len;
                Gizmos.DrawLine(tip, tip - dir * 0.2f + perp * 0.08f);
                Gizmos.DrawLine(tip, tip - dir * 0.2f - perp * 0.08f);
            }
        }
    }
}
