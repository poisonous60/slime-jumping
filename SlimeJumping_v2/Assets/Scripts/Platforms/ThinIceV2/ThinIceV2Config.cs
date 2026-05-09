using UnityEngine;

namespace SlimeJumping.Platforms.ThinIceV2
{
    /// <summary>
    /// Single source of truth for ThinIce v2 shared assets. Every <see cref="ThinIcePlatformV2"/>
    /// resolves to one of these instead of binding shard set / debris VFX / frost cloud VFX
    /// per-platform in the inspector. The canonical asset lives at
    /// <c>Assets/Resources/ThinIceV2Config.asset</c> and is auto-loaded; per-platform override
    /// is exposed for special variants (e.g. a different ice tone in another biome).
    ///
    /// Run <c>SlimeJumping/Thin Ice v2/Generate Default Config and Prefabs</c> once to create
    /// the asset + the two VFX prefabs it points at.
    /// </summary>
    [CreateAssetMenu(fileName = "ThinIceV2Config",
        menuName = "SlimeJumping/Thin Ice v2/Config", order = 0)]
    public sealed class ThinIceV2Config : ScriptableObject
    {
        [Header("Shard")]
        [Tooltip("Generic shard mesh library. Generate via 'SlimeJumping/Thin Ice v2/Generate Shared Shard Set'.")]
        public ThinIceShardSet shardSet;

        [Tooltip("Prefab with MeshFilter + MeshRenderer + Rigidbody2D + ThinIceShardRuntime. " +
                 "Mesh is overwritten per-spawn from the ShardSet.")]
        public GameObject shardRuntimePrefab;

        [Header("VFX prefabs")]
        [Tooltip("Telegraph debris emitter. Spawned as a child of each platform at Awake; " +
                 "emit shape gets reconfigured to the collider's world bounds, emission rate driven by crack progress.")]
        public GameObject debrisVFXPrefab;

        [Tooltip("Frost cloud one-shot played at the moment of shatter. Spawned as a child of each platform at Awake.")]
        public GameObject frostCloudVFXPrefab;

        // ---------------------------------------------------------------------

        const string ResourcesPath = "ThinIceV2Config";

        /// <summary>
        /// Returns the override if assigned, otherwise the canonical asset at
        /// <c>Assets/Resources/ThinIceV2Config.asset</c>. May return null if neither exists —
        /// callers warn-and-bail rather than crash.
        /// </summary>
        public static ThinIceV2Config Resolve(ThinIceV2Config @override)
        {
            if (@override != null) return @override;
            return Resources.Load<ThinIceV2Config>(ResourcesPath);
        }
    }
}
