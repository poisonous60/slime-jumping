using UnityEngine;

namespace SlimeJumping.Platforms.ThinIceV2
{
    /// <summary>
    /// Shared assets + default tuning for ThinIce v2; per-platform fields are optional overrides.
    /// Canonical asset auto-loads from <c>Assets/Resources/ThinIceV2Config.asset</c>, created by
    /// <c>SlimeJumping/Thin Ice v2/Generate Default Config and Prefabs</c>.
    /// </summary>
    [CreateAssetMenu(fileName = "ThinIceV2Config",
        menuName = "SlimeJumping/Thin Ice v2/Config", order = 0)]
    public sealed class ThinIceV2Config : ScriptableObject
    {
        [Header("Shared assets")]
        [Tooltip("Generic shard mesh library. Generate via 'SlimeJumping/Thin Ice v2/Generate Shared Shard Set'.")]
        public ThinIceShardSet shardSet;

        [Tooltip("Prefab with MeshFilter + MeshRenderer + Rigidbody2D + ThinIceShardRuntime. " +
                 "Mesh is overwritten per-spawn from the ShardSet.")]
        public GameObject shardRuntimePrefab;

        [Tooltip("Telegraph debris emitter. Spawned as a child of each platform at Awake; " +
                 "emit shape gets reconfigured to the collider's world bounds, emission rate driven by crack progress.")]
        public GameObject debrisVFXPrefab;

        [Tooltip("Frost cloud one-shot played at the moment of shatter. Spawned as a child of each platform at Awake.")]
        public GameObject frostCloudVFXPrefab;

        [Header("Behavior")]
        [Tooltip("Seconds the slime can stand on the ice before timer-shatter fires.")]
        [Min(0f)] public float crackingDuration = 2.0f;

        [Tooltip("Seconds for crack progress to reverse from 1 → 0 when zone-respawn triggers.")]
        [Min(0.01f)] public float regenDuration = 0.6f;

        [Header("Angle Filter")]
        public bool useAngleLimit;
        [Tooltip("0 = floor, 90 = wall, 180 = ceiling.")]
        public float minSurfaceAngle;
        public float maxSurfaceAngle = 180f;

        [Header("Particles")]
        [Tooltip("Peak emission rate (particles/sec) at full crack progress.")]
        [Min(0f)] public float debrisPeakRate = 40f;

        [Header("Shard launch")]
        [Tooltip("How many shards to spawn at shatter. Distributed randomly across collider bounds.")]
        [Min(1)] public int shardCount = 16;
        [Min(0f)] public float shardSpeedMin = 2f;
        [Min(0f)] public float shardSpeedMax = 4f;
        [Tooltip("Constant upward kick added to every shard so they arc rather than splat sideways.")]
        public float shardUpwardBias = 1.0f;
        [Tooltip("Random angular velocity range in degrees/sec. Sign is also randomized.")]
        [Min(0f)] public float shardSpinDeg = 360f;
        [Min(0.05f)] public float shardLifetimeMin = 1.2f;
        [Min(0.05f)] public float shardLifetimeMax = 1.8f;
        [Tooltip("Fade window before despawn (alpha 1→0).")]
        [Min(0.01f)] public float shardFadeWindow = 0.4f;
        [Tooltip("Fraction of slime launch velocity inherited by each shard on jump-shatter (0~1).")]
        [Range(0f, 1f)] public float jumpVelocityInherit = 0.3f;
        [Tooltip("Random scale range applied per spawned shard.")]
        public Vector2 shardScaleRange = new Vector2(0.7f, 1.3f);

        [Header("Audio")]
        public SlimeJumping.Audio.SoundDef crackSound;
        public SlimeJumping.Audio.SoundDef shatterSound;
        public SlimeJumping.Audio.SoundDef regenSound;

        const string ResourcesPath = "ThinIceV2Config";

        /// <summary>Override if assigned, else the canonical Resources asset. Null if neither exists.</summary>
        public static ThinIceV2Config Resolve(ThinIceV2Config @override)
        {
            if (@override != null) return @override;
            return Resources.Load<ThinIceV2Config>(ResourcesPath);
        }
    }
}
