using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Emitter-level preset; when assigned its values OVERWRITE the emitter's inline fields as one set, on OnValidate and Start.</summary>
    /// Per-mode tuning (Branching/Beam/Burst) lives in separate SOs; scene refs stay on the emitter.
    [CreateAssetMenu(fileName = "GraniteBlastEmitterPreset",
        menuName = "SlimeJumping/Granite Blast/Emitter Preset", order = -1)]
    public class GraniteBlastEmitterPreset : ScriptableObject
    {
        [Header("Detection")]
        public float detectionRange = 18f;
        [Tooltip("Layers that block line of sight. Leave 0 to skip LOS checks.")]
        public LayerMask lineOfSightBlockers = 0;

        [Header("Phase Timing")]
        [Tooltip("Idle wait once a target is first acquired.")]
        public float detectionDelay = 0.6f;
        [Tooltip("발사 전 준비 — charge glow swells, aim-lock telegraph appears.")]
        public float preFireDuration = 0.7f;
        [Tooltip("발사 시작 — short discharge frame.")]
        public float fireStartDuration = 0.08f;
        [Tooltip("발사 중 — sustained firing window.")]
        public float firingDuration = 0.45f;
        [Tooltip("발사 이후 — recovery wind-down.")]
        public float postFireDuration = 0.25f;
        [Tooltip("Quiet time before the next blast can begin.")]
        public float cooldownDuration = 1.2f;
        public float loseSightGrace = 0.5f;

        [Header("Hit Mask / Range")]
        public float maxRange = 30f;
        [Tooltip("Layers the blast can hit. Default + SlimeNode (6) + Environment (7).")]
        public LayerMask bulletHitMask = (1 << 0) | (1 << 6) | (1 << 7);
        [Tooltip("CircleCast radius used by all three modes for slime hit detection.")]
        public float hitRadius = 0.4f;

        [Header("Knockback")]
        [Tooltip("One-shot KB applied per discrete bolt hit. Units: m/s.")]
        public float knockbackImpulse = 18f;
        [Tooltip("Sustained KB applied every frame the slime stays inside a sustained Beam. Units: m/s².")]
        public float knockbackAcceleration = 60f;
        [Tooltip("Beam mode only. When true, the beam keeps re-sweeping every frame.")]
        public bool sustainedHit = false;

        [Header("Hit Feedback")]
        [Tooltip("Time.timeScale freeze duration (realtime sec) on first slime hit per burst. 0 = disabled.")]
        public float hitStopDuration = 0.06f;
        [Tooltip("Time.timeScale during the freeze. 0 = full freeze.")]
        [Range(0f, 1f)]
        public float hitStopFrozenScale = 0.0f;
        [Tooltip("Impulse force multiplier for camera shake on slime hit.")]
        public float hitShakeForce = 1.0f;

        [Header("Stun (optional)")]
        [Tooltip("Stun duration (sec) applied on slime hit. 0 = disabled.")]
        public float stunOnHitDuration = 0f;
        public SlimeJumping.Interfaces.DamageType stunDamageType = SlimeJumping.Interfaces.DamageType.Lightning;

        [Header("Visual Colors")]
        public Color coreColor = new Color(0.85f, 0.96f, 1f, 1f);
        [Tooltip("World Z offset applied to projectile lines so they render in front of 2.5D ground.")]
        public float lineZOffset = -0.5f;

        [Header("Aim-Lock Telegraph (PreFire)")]
        public bool showPreFireTelegraph = true;
        public float telegraphWidth = 0.04f;
        public Color telegraphColor = new Color(0.7f, 0.95f, 1f, 0.3f);

        [Header("Audio")]
        public SlimeJumping.Audio.SoundDef detectSound;
        public SlimeJumping.Audio.SoundDef preFireSound;
        public SlimeJumping.Audio.SoundDef fireStartSound;
        public SlimeJumping.Audio.SoundDef postFireSound;
        public SlimeJumping.Audio.SoundDef groundImpactSound;

        [Header("VFX Prefabs")]
        [Tooltip("Spawned at each tendril/beam/bullet impact (slime + ground).")]
        public ParticleSystem impactPrefab;
        [Tooltip("Optional larger explosion VFX spawned only on GROUND hits.")]
        public ParticleSystem groundExplosionPrefab;

        [Header("Snow Impact")]
        public bool snowCarveOnImpact = true;
        public float snowCarveDepth = 0.18f;
        public float snowCarveRadius = 0.6f;
    }
}
