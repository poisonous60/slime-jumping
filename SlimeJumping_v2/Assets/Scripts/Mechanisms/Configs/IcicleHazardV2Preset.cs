using UnityEngine;
using SlimeJumping.Platforms.ThinIceV2;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Tuning preset for <see cref="IcicleHazardV2"/>. Excludes visual asset refs and scene refs — those are per-instance.</summary>
    [CreateAssetMenu(fileName = "IcicleHazardV2Preset",
        menuName = "SlimeJumping/Icicle/Hazard V2 Preset", order = 1)]
    public class IcicleHazardV2Preset : ScriptableObject
    {
        [Header("Detection Box (local space, beneath icicle)")]
        public Vector2 detectionOffset = new Vector2(0f, -3f);
        public Vector2 detectionSize = new Vector2(1.5f, 6f);
        public LayerMask slimeLayer = ~0;

        [Header("Shake Phase")]
        public float shakeDuration = 0.8f;
        public float shakeAmplitude = 0.08f;
        public float shakeFrequency = 40f;

        [Header("Fall Phase")]
        [Tooltip("Time(seconds since fall start) → downward speed (units/sec). " +
                 "After the curve's last keyframe time, the final value is held " +
                 "(use this to author a slow → accelerating → constant profile). " +
                 "Authored from t=0; negative outputs are clamped to 0.")]
        public AnimationCurve fallSpeedCurve = new AnimationCurve(
            new Keyframe(0f,   1.5f),
            new Keyframe(0.6f, 6f),
            new Keyframe(1.2f, 6f));
        public LayerMask breakOnLayers = ~0;
        [Tooltip("Seconds after fall start during which break-on-ground is suppressed.")]
        public float breakImmunityDuration = 0.3f;

        [Header("Shatter — shard launch")]
        [Tooltip("Mesh library used to pick shard meshes at random. Reuses the " +
                 "ThinIce v2 type; assign your own asset for icicle-specific shapes.")]
        public ThinIceShardSet shardSet;
        [Tooltip("Prefab carrying MeshFilter + MeshRenderer + Rigidbody2D + ThinIceShardRuntime.")]
        public GameObject shardRuntimePrefab;
        [Min(1)] public int shardCount = 16;
        [Min(0f)] public float shardSpeedMin = 2f;
        [Min(0f)] public float shardSpeedMax = 4f;
        [Tooltip("Upward velocity bias added to each shard. Lower than ThinIce since " +
                 "an icicle slammed downward, less bounce-up.")]
        public float shardUpwardBias = 0.7f;
        [Min(0f)] public float shardSpinDeg = 360f;
        [Min(0.05f)] public float shardLifetimeMin = 1.2f;
        [Min(0.05f)] public float shardLifetimeMax = 1.8f;
        [Min(0.01f)] public float shardFadeWindow = 0.4f;
        public Vector2 shardScaleRange = new Vector2(0.7f, 1.3f);
        [Tooltip("Fraction of fall speed at impact bled into shards (downward). " +
                 "Reads as 'icicle slammed and shattered' rather than 'teleported and exploded'.")]
        [Range(0f, 1f)] public float impactVelocityInherit = 0.3f;

        [Header("Shatter — VFX / audio / shake")]
        [Tooltip("Particle prefab spawned once at impact center. The PS handles its own " +
                 "lifetime via Stop Action = Destroy (or similar); not parented to the icicle.")]
        public GameObject frostCloudVFXPrefab;
        [Tooltip("One clip is picked at random per shatter event.")]
        public SlimeJumping.Audio.SoundDef shatterSound;

        [Tooltip("Boost emissive color via MaterialPropertyBlock. Requires a shader with " +
                 "an emission/color property; default off.")]
        public bool useEmissiveFlash = false;
        public Color flashColor = Color.white;
        [Min(0.01f)] public float flashDuration = 0.05f;
        [Min(1f)] public float flashIntensity = 4f;

        [Tooltip("ImpulseEmitter ref lives on the COMPONENT (scene ref). This flag and " +
                 "shakeForce are tunable here; the actual emitter is wired per-instance.")]
        public bool useCameraShake = false;
        [Min(0f)] public float shakeForce = 0.5f;

        [Header("Respawn")]
        [Tooltip("Negative = one-shot (no respawn).")]
        public float respawnTime = -1f;
        public float regenDuration = 0.6f;
        public LayerMask regenBlockingLayers = ~0;

        [Header("Audio (overridden by IcicleAudioManager when present)")]
        public SlimeJumping.Audio.SoundDef shakeStartSound;
        public SlimeJumping.Audio.SoundDef fallingSound;
        public SlimeJumping.Audio.SoundDef impactSound;
    }
}
