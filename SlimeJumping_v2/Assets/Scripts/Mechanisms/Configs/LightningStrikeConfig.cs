using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Shared tuning for <see cref="LightningStrikeField"/>; scene refs (anchors) stay on the field component.</summary>
    [CreateAssetMenu(fileName = "LightningStrikeConfig",
        menuName = "SlimeJumping/Lightning/Strike Config", order = 0)]
    public class LightningStrikeConfig : ScriptableObject
    {
        [Header("Timing")]
        [Tooltip("Seconds between two strikes of the same anchor.")]
        public float period = 2f;
        [Tooltip("Off = each anchor fires once and never again.")]
        public bool repeat = true;

        [Header("Path")]
        [Tooltip("Max fall distance. Also the bolt length when no ground is found below.")]
        public float maxLength = 20f;
        [Tooltip("Layers the downward probe treats as ground when picking the landing point.")]
        public LayerMask groundMask = (1 << 7);
        [Tooltip("World units per line segment. Total segments = path length / this, clamped to [6, 48].")]
        public float segmentLength = 0.6f;
        [Tooltip("Max perpendicular crackle noise per vertex, scaled by segment length.")]
        public float jaggedness = 0.35f;
        [Tooltip("Sideways bow (±units) of the two mid control points. 0 = dead straight drop.")]
        public float lateralSway = 0.4f;
        [Tooltip("Depth bow (±units, Z) of the two mid control points. Endpoints stay in the anchor's Z plane.")]
        public float depthSway = 0f;

        [Header("Draw")]
        [Tooltip("Tip speed in world units per second.")]
        public float travelSpeed = 60f;
        [Tooltip("Fallback fall time (sec) used only when travelSpeed <= 0.")]
        public float drawTime = 0.2f;
        public float holdTime = 0.18f;
        public float fadeTime = 0.28f;
        public float coreWidth = 0.12f;
        public Color coreColor = new Color(0.85f, 0.96f, 1f, 1f);
        [Tooltip("Z offset of the line so it draws in front of the 2.5D terrain.")]
        public float lineZOffset = -0.5f;

        [Header("Hit")]
        [Tooltip("Layers that stop the bolt. Default (0, slime body) | SlimeNode (6) | Environment (7) — same as GraniteBlast's bulletHitMask.")]
        public LayerMask hitMask = (1 << 0) | (1 << 6) | (1 << 7);
        [Tooltip("CircleCast radius of the advancing tip, independent of line width.")]
        public float hitRadius = 0.4f;
        [Tooltip("Sideways deltaV (m/s) applied to the slime, away from the bolt.")]
        public float knockbackImpulse = 12f;

        [Header("Hit Feedback")]
        [Tooltip("Time.timeScale freeze duration (realtime sec) on slime hit. 0 = disabled.")]
        public float hitStopDuration = 0.06f;
        [Tooltip("Time.timeScale during the freeze. 0 = full freeze, 0.05 = 95% slow-mo.")]
        [Range(0f, 1f)] public float hitStopFrozenScale = 0f;
        [Tooltip("Impulse force multiplier for the camera shake on slime hit. The emitter itself is a scene ref on LightningStrikeField.")]
        public float hitShakeForce = 1f;

        [Header("Impact")]
        [Tooltip("Small spark at the contact point.")]
        public ParticleSystem impactPrefab;
        [Tooltip("Big explosion at the contact point.")]
        public ParticleSystem groundExplosionPrefab;
        public AudioClip impactSound;
        [Range(0f, 1f)] public float impactVolume = 1f;
        [Range(0f, 0.5f)] public float impactPitchJitter = 0.1f;

        [Header("Snow Impact")]
        public bool snowCarveOnImpact = true;
        public float snowCarveDepth = 0.18f;
        public float snowCarveRadius = 0.6f;
    }
}
