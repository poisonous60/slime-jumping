using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Water zone: drag + buoyancy + fall speed cap.
    /// Blocks attach by default (slime floats, doesn't stick to submerged surfaces).
    /// Uses the two-channel velocity model: MultiplyVelocity (drag) + AddExternalVelocity (buoyancy).
    /// </summary>
    public class WaterZone : EnvironmentalZone
    {
        [Header("Entry/Exit")]
        [Tooltip("Velocity multiplier on zone entry (< 1 = slow down).")]
        [Range(0.01f, 1f)]
        [SerializeField] private float entryMultiplier = 0.4f;

        [Tooltip("Velocity multiplier on zone exit (> 1 = speed boost).")]
        [Min(0.01f)]
        [SerializeField] private float exitMultiplier = 2.5f;

        [Header("Internal Physics")]
        [Tooltip("Per-second velocity retention while inside (0.4 = 40% speed after 1s). 1 = no drag.")]
        [Range(0.01f, 1f)]
        [SerializeField] private float dragFactor = 0.4f;

        [Tooltip("Upward acceleration opposing gravity (units/s²).")]
        [SerializeField] private float buoyancy = 12f;

        [Tooltip("Maximum fall speed inside water (units/s). 0 = no cap.")]
        [Min(0f)]
        [SerializeField] private float maxFallSpeed = 4f;

        [Header("Behavior")]
        [Tooltip("Block surface attachment while inside (slime floats instead of sticking).")]
        [SerializeField] private bool blockAttach = true;

        [Header("Visual")]
        [SerializeField] private Color waterColor = new Color(0.2f, 0.5f, 1f, 0.25f);

        protected override void OnSlimeEnter(SlimeController slime)
        {
            slime.MultiplyVelocity(entryMultiplier);
            if (blockAttach)
                slime.PushBlockAttach();
            slime.PushInFluid();
        }

        protected override void OnSlimeExit(SlimeController slime)
        {
            slime.MultiplyVelocity(exitMultiplier);
            if (blockAttach)
                slime.PopBlockAttach();
            slime.PopInFluid();
        }

        protected override void ApplyEffect(SlimeController slime, float dt)
        {
            // Drag (multiplicative channel)
            if (dragFactor < 1f)
                slime.MultiplyVelocity(Mathf.Pow(dragFactor, dt));

            // Buoyancy (additive channel, opposing gravity)
            slime.AddExternalVelocity(slime.GravityUp * buoyancy * dt);

            // Fall speed cap
            if (maxFallSpeed > 0f)
                slime.ClampFallSpeed(maxFallSpeed);
        }

        // --- Gizmos ---

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.08f);
            var col = GetComponent<Collider2D>();
            if (col is BoxCollider2D box)
            {
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(
                    transform.TransformPoint(box.offset),
                    transform.rotation, transform.lossyScale);
                Gizmos.DrawCube(Vector3.zero, box.size);
                Gizmos.matrix = old;
            }

            // Water surface line
            Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.5f);
            if (col is BoxCollider2D box2)
            {
                Vector3 center = transform.TransformPoint(box2.offset);
                float halfW = box2.size.x * transform.lossyScale.x * 0.5f;
                float halfH = box2.size.y * transform.lossyScale.y * 0.5f;
                Vector3 left = center + new Vector3(-halfW, halfH, 0f);
                Vector3 right = center + new Vector3(halfW, halfH, 0f);
                Gizmos.DrawLine(left, right);
            }
        }

        void OnDrawGizmosSelected()
        {
            var col = GetComponent<Collider2D>();
            if (col == null) return;

            Gizmos.color = waterColor;
            if (col is BoxCollider2D box)
            {
                Matrix4x4 old = Gizmos.matrix;
                Gizmos.matrix = Matrix4x4.TRS(
                    transform.TransformPoint(box.offset),
                    transform.rotation, transform.lossyScale);
                Gizmos.DrawCube(Vector3.zero, box.size);
                Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.5f);
                Gizmos.DrawWireCube(Vector3.zero, box.size);
                Gizmos.matrix = old;
            }

            // Buoyancy direction arrows
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.4f);
            Vector3 center2 = transform.position;
            float len = Mathf.Clamp(buoyancy * 0.05f, 0.3f, 2f);
            Vector3 perp = Vector3.right;
            for (int i = -2; i <= 2; i++)
            {
                Vector3 start = center2 + perp * (i * 0.5f);
                Gizmos.DrawLine(start, start + Vector3.up * len);
            }
        }
    }
}
