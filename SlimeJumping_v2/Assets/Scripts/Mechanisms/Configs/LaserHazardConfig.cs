using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Shared tuning for <see cref="LaserHazard"/>; placement values (beamLength,
    /// patrol range/speed/delay) stay on the component. Auto-loaded from
    /// Resources/LaserHazardConfig when a laser has no explicit reference.</summary>
    [CreateAssetMenu(fileName = "LaserHazardConfig",
        menuName = "SlimeJumping/Laser/Hazard Config", order = 0)]
    public class LaserHazardConfig : ScriptableObject
    {
        [Header("Beam")]
        [Tooltip("Hitbox thickness. Visual widths are independent knobs below.")]
        public float beamWidth = 0.25f;

        [Header("Hit")]
        [Tooltip("World-space knockback angle in degrees. 0 = right, 20 = right slightly up, 160 = left slightly up.")]
        public float knockbackAngle = 160f;
        [Tooltip("Speed added to the slime along knockbackAngle (units/s). Additive, so hits against the push direction slow the slime and hits along it speed it up.")]
        public float knockbackSpeed = 12f;
        [Tooltip("Seconds between hits.")]
        public float hitCooldown = 0.35f;
        [Tooltip("Burn mark size scale for laser hits. 1 = SlimeBurnVisual default, 0 = no mark (flash only).")]
        [Range(0f, 3f)] public float burnMarkScale = 1f;

        [Header("Audio")]
        [Tooltip("One-shot on slime hit, SFX_Object bus (World SFX slider). Empty = silent.")]
        public AudioClip hitClip;
        [Range(0f, 1f)] public float hitVolume = 1f;

        [Header("Visual")]
        public Color coreColor = new Color(1f, 0.9f, 0.9f, 1f);
        public Color glowColor = new Color(1f, 0.12f, 0.05f, 0.55f);
        public float coreWidth = 0.09f;
        public float glowWidth = 0.4f;
        [Tooltip("Width flicker amplitude as a fraction of base width.")]
        [Range(0f, 1f)] public float pulseAmount = 0.18f;
        public float pulseSpeed = 22f;
        [Tooltip("Local Z offset of the beam lines, for 2.5D sorting.")]
        public float visualZOffset = 0f;

        void OnValidate()
        {
            beamWidth = Mathf.Max(0.01f, beamWidth);
            coreWidth = Mathf.Max(0.001f, coreWidth);
            glowWidth = Mathf.Max(0.001f, glowWidth);
        }
    }
}
