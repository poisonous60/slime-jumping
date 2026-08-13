using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;

namespace SlimeJumping.Platforms.ThinIceV2
{
    /// Pogostuck-style thin ice: touch arms it, then timer expiry or slime departure shatters
    /// it into shards. Tuning lives in <see cref="ThinIceV2Config"/>; the fields below are
    /// per-platform overrides gated by section toggles.
    public class ThinIcePlatformV2 : MonoBehaviour
    {
        public enum IceState { Idle, Cracking, Shattered, Regenerating }

        // --- Source --------------------------------------------------------------

        [Header("Source")]
        [Tooltip("Optional override of the shared ThinIceV2Config. Leave null to auto-load " +
                 "Assets/Resources/ThinIceV2Config.asset.")]
        [SerializeField] ThinIceV2Config configOverride;

        [Tooltip("Renderer that displays the ice surface. Auto-found at Awake (self or first child).")]
        [SerializeField] Renderer visualRenderer;

        [Tooltip("Collider whose bounds drive shard spawn distribution + slime-attach detection. " +
                 "Auto-found at Awake (self or any child).")]
        [SerializeField] Collider2D platformCollider;

        // --- Behavior (override) -------------------------------------------------

        [Header("Behavior (override)")]
        [Tooltip("If on, the values below replace ThinIceV2Config's defaults for this platform only.")]
        [SerializeField] bool overrideBehavior;
        [Min(0f)] [SerializeField] float crackingDuration = 2.0f;
        [Min(0.01f)] [SerializeField] float regenDuration = 0.6f;

        // --- Angle Filter (override) ---------------------------------------------

        [Header("Angle Filter (override)")]
        [SerializeField] bool overrideAngleFilter;
        [SerializeField] bool useAngleLimit;
        [Tooltip("0 = floor, 90 = wall, 180 = ceiling.")]
        [SerializeField] float minSurfaceAngle;
        [SerializeField] float maxSurfaceAngle = 180f;

        // --- Particles (override) ------------------------------------------------

        [Header("Particles (override)")]
        [SerializeField] bool overrideParticles;
        [Min(0f)] [SerializeField] float debrisPeakRate = 40f;

        // --- Shard launch (override) ---------------------------------------------

        [Header("Shard launch (override)")]
        [SerializeField] bool overrideShardLaunch;
        [Min(1)] [SerializeField] int shardCount = 16;
        [Min(0f)] [SerializeField] float shardSpeedMin = 2f;
        [Min(0f)] [SerializeField] float shardSpeedMax = 4f;
        [SerializeField] float shardUpwardBias = 1.0f;
        [Min(0f)] [SerializeField] float shardSpinDeg = 360f;
        [Min(0.05f)] [SerializeField] float shardLifetimeMin = 1.2f;
        [Min(0.05f)] [SerializeField] float shardLifetimeMax = 1.8f;
        [Min(0.01f)] [SerializeField] float shardFadeWindow = 0.4f;
        [Range(0f, 1f)] [SerializeField] float jumpVelocityInherit = 0.3f;
        [SerializeField] Vector2 shardScaleRange = new Vector2(0.7f, 1.3f);

        // --- Audio (override) ----------------------------------------------------

        [Header("Audio (override)")]
        [SerializeField] bool overrideAudio;
        [SerializeField] SoundDef crackSound;
        [SerializeField] SoundDef shatterSound;
        [SerializeField] SoundDef regenSound;

        // --- Optional FX (per-instance: ImpulseEmitter is a scene ref) -----------

        [Header("Optional FX (default OFF)")]
        [SerializeField] bool useEmissiveFlash = false;
        [SerializeField] Color flashColor = Color.white;
        [Min(0.01f)] [SerializeField] float flashDuration = 0.05f;
        [Tooltip("Magnitude multiplier on top of base emissive color.")]
        [Min(1f)] [SerializeField] float flashIntensity = 4f;

        [SerializeField] bool useCameraShake = false;
        [SerializeField] ImpulseEmitter impulseEmitter;
        [Min(0f)] [SerializeField] float shakeForce = 0.5f;

        // --- Shader properties ---------------------------------------------------

        static readonly int CrackProgressID = Shader.PropertyToID("_CrackProgress");
        static readonly int CrackColorID    = Shader.PropertyToID("_CrackColor");

        // --- Runtime state -------------------------------------------------------

        IceState state = IceState.Idle;
        float    timer;
        Vector2  pendingLaunchVel;

        SlimeController slime;
        Collider2D[]    myColliders;
        MaterialPropertyBlock mpb;
        Color    baseCrackColor;
        bool     crackColorCached;
        ThinIceRespawnZone parentZone;
        float    flashRemaining;

        ThinIceV2Config resolvedConfig;
        ParticleSystem  debrisVFX;
        ParticleSystem  frostCloudVFX;

        public IceState State => state;
        public bool HasZone => parentZone != null;
        public ThinIceV2Config ResolvedConfig => resolvedConfig;

        // --- Resolved getters: section-toggle picks override → config, and a null config
        // falls back to the serialized values so the platform still works standalone.

        float ResolvedCrackingDuration => overrideBehavior || resolvedConfig == null ? crackingDuration : resolvedConfig.crackingDuration;
        float ResolvedRegenDuration    => overrideBehavior || resolvedConfig == null ? regenDuration    : resolvedConfig.regenDuration;

        bool  ResolvedUseAngleLimit   => overrideAngleFilter || resolvedConfig == null ? useAngleLimit   : resolvedConfig.useAngleLimit;
        float ResolvedMinSurfaceAngle => overrideAngleFilter || resolvedConfig == null ? minSurfaceAngle : resolvedConfig.minSurfaceAngle;
        float ResolvedMaxSurfaceAngle => overrideAngleFilter || resolvedConfig == null ? maxSurfaceAngle : resolvedConfig.maxSurfaceAngle;

        float ResolvedDebrisPeakRate  => overrideParticles || resolvedConfig == null ? debrisPeakRate : resolvedConfig.debrisPeakRate;

        int     ResolvedShardCount         => overrideShardLaunch || resolvedConfig == null ? shardCount         : resolvedConfig.shardCount;
        float   ResolvedShardSpeedMin      => overrideShardLaunch || resolvedConfig == null ? shardSpeedMin      : resolvedConfig.shardSpeedMin;
        float   ResolvedShardSpeedMax      => overrideShardLaunch || resolvedConfig == null ? shardSpeedMax      : resolvedConfig.shardSpeedMax;
        float   ResolvedShardUpwardBias    => overrideShardLaunch || resolvedConfig == null ? shardUpwardBias    : resolvedConfig.shardUpwardBias;
        float   ResolvedShardSpinDeg       => overrideShardLaunch || resolvedConfig == null ? shardSpinDeg       : resolvedConfig.shardSpinDeg;
        float   ResolvedShardLifetimeMin   => overrideShardLaunch || resolvedConfig == null ? shardLifetimeMin   : resolvedConfig.shardLifetimeMin;
        float   ResolvedShardLifetimeMax   => overrideShardLaunch || resolvedConfig == null ? shardLifetimeMax   : resolvedConfig.shardLifetimeMax;
        float   ResolvedShardFadeWindow    => overrideShardLaunch || resolvedConfig == null ? shardFadeWindow    : resolvedConfig.shardFadeWindow;
        float   ResolvedJumpVelocityInherit => overrideShardLaunch || resolvedConfig == null ? jumpVelocityInherit : resolvedConfig.jumpVelocityInherit;
        Vector2 ResolvedShardScaleRange    => overrideShardLaunch || resolvedConfig == null ? shardScaleRange    : resolvedConfig.shardScaleRange;

        SoundDef ResolvedCrackSound   => overrideAudio || resolvedConfig == null ? crackSound   : resolvedConfig.crackSound;
        SoundDef ResolvedShatterSound => overrideAudio || resolvedConfig == null ? shatterSound : resolvedConfig.shatterSound;
        SoundDef ResolvedRegenSound   => overrideAudio || resolvedConfig == null ? regenSound   : resolvedConfig.regenSound;

        // --- Lifecycle -----------------------------------------------------------

        void Reset()
        {
            visualRenderer  = GetComponentInChildren<Renderer>();
            platformCollider = GetComponentInChildren<Collider2D>();
        }

        void Awake()
        {
            if (visualRenderer == null) visualRenderer = GetComponentInChildren<Renderer>();
            if (platformCollider == null) platformCollider = GetComponentInChildren<Collider2D>();
            myColliders = GetComponentsInChildren<Collider2D>(true);
            if (myColliders == null || myColliders.Length == 0)
            {
                Debug.LogWarning($"[ThinIcePlatformV2] No Collider2D found on '{name}' or any child.", this);
            }
            mpb = new MaterialPropertyBlock();

            ResolveConfigAndSpawnVFX();
            CacheBaseCrackColor();
            ApplyShaderProgress(0f);
            ConfigureDebrisShape();
        }

        // VFX are instantiated at Awake rather than authored as scene children, so dropping
        // this onto a freshly baked mesh needs no per-platform asset wiring.
        void ResolveConfigAndSpawnVFX()
        {
            resolvedConfig = ThinIceV2Config.Resolve(configOverride);
            if (resolvedConfig == null)
            {
                Debug.LogWarning(
                    "[ThinIcePlatformV2] No ThinIceV2Config resolved. Run " +
                    "'SlimeJumping/Thin Ice v2/Generate Default Config and Prefabs' " +
                    "or assign Config Override.", this);
                return;
            }

            if (resolvedConfig.debrisVFXPrefab != null)
            {
                var go = Instantiate(resolvedConfig.debrisVFXPrefab, transform);
                go.transform.localPosition = Vector3.zero;
                go.name = "DebrisVFX";
                debrisVFX = go.GetComponent<ParticleSystem>();
            }
            if (resolvedConfig.frostCloudVFXPrefab != null)
            {
                var go = Instantiate(resolvedConfig.frostCloudVFXPrefab, transform);
                // TerrainShape2D-baked colliders can be offset from the transform origin;
                // anchor to the collider center or the puff pops away from the ice.
                go.transform.position = platformCollider != null
                    ? platformCollider.bounds.center
                    : transform.position;
                go.name = "FrostCloudVFX";
                frostCloudVFX = go.GetComponent<ParticleSystem>();
            }
        }

        void OnEnable()
        {
            TrySubscribeSlime();
        }

        void OnDisable()
        {
            UnsubscribeSlime();
        }

        // Avoids O(scene) FindAnyObjectByType firing once per platform when dozens of
        // ThinIce terrain wrappers wake at the same hysteresis boundary crossing.
        static SlimeController s_cachedSlime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAndHookSlimeEvents()
        {
            // "Enter Play Mode without domain reload" keeps statics across sessions — without
            // this reset the cache holds a stale slime from the previous session.
            s_cachedSlime = null;
            SlimeController.OnSpawned += HandleSlimeSpawned;
            SlimeController.OnDespawned += HandleSlimeDespawned;
        }

        static void HandleSlimeSpawned(SlimeController s) => s_cachedSlime = s;

        static void HandleSlimeDespawned(SlimeController s)
        {
            if (s_cachedSlime == s) s_cachedSlime = null;
        }

        void TrySubscribeSlime()
        {
            if (slime != null) return;
            slime = s_cachedSlime != null ? s_cachedSlime : FindAnyObjectByType<SlimeController>();
            if (slime == null) return;
            slime.OnLaunch += OnSlimeLaunch;
        }

        void UnsubscribeSlime()
        {
            if (slime == null) return;
            slime.OnLaunch -= OnSlimeLaunch;
            slime = null;
        }

        public void RegisterParentZone(ThinIceRespawnZone zone)
        {
            parentZone = zone;
        }

        // --- Tick ----------------------------------------------------------------

        void FixedUpdate()
        {
            if (slime == null) { TrySubscribeSlime(); if (slime == null) return; }

            float dt = Time.fixedDeltaTime;
            switch (state)
            {
                case IceState.Idle:         UpdateIdle(); break;
                case IceState.Cracking:     UpdateCracking(dt); break;
                case IceState.Shattered:    /* event-driven, no tick */ break;
                case IceState.Regenerating: UpdateRegenerating(dt); break;
            }
        }

        void Update()
        {
            if (flashRemaining > 0f)
            {
                flashRemaining -= Time.deltaTime;
                float t = Mathf.Clamp01(flashRemaining / Mathf.Max(0.0001f, flashDuration));
                ApplyFlashIntensity(t);
                if (flashRemaining <= 0f) ApplyFlashIntensity(0f);
            }
        }

        // --- States --------------------------------------------------------------

        void UpdateIdle()
        {
            // Charging counts too: chargeBuffer lets Airborne→Charging skip Attached on
            // landing, and the player would launch off ice that never started cracking.
            var st = slime.CurrentState;
            if (st != SlimeController.SlimeState.Attached &&
                st != SlimeController.SlimeState.Charging) return;
            if (System.Array.IndexOf(myColliders, slime.SurfaceCollider) < 0) return;
            if (ResolvedUseAngleLimit)
            {
                float dot = Mathf.Clamp(Vector2.Dot(slime.SurfaceNormal, slime.GravityUp), -1f, 1f);
                float angle = Mathf.Acos(dot) * Mathf.Rad2Deg;
                if (angle < ResolvedMinSurfaceAngle || angle > ResolvedMaxSurfaceAngle) return;
            }
            EnterCracking();
        }

        void UpdateCracking(float dt)
        {
            timer -= dt;
            float progress = Mathf.Clamp01(1f - timer / Mathf.Max(0.0001f, ResolvedCrackingDuration));
            ApplyShaderProgress(progress);
            ApplyDebrisRate(progress);

            if (timer <= 0f)
            {
                EnterShattered(jumpInherit: false);
                return;
            }

            // Detach without launch (external force, gravity flip) — normal jumps come
            // through OnSlimeLaunch instead.
            bool stillAttachedHere =
                slime.CurrentState == SlimeController.SlimeState.Attached &&
                System.Array.IndexOf(myColliders, slime.SurfaceCollider) >= 0;
            bool charging =
                slime.CurrentState == SlimeController.SlimeState.Charging &&
                System.Array.IndexOf(myColliders, slime.SurfaceCollider) >= 0;
            if (!stillAttachedHere && !charging)
            {
                EnterShattered(jumpInherit: false);
            }
        }

        void UpdateRegenerating(float dt)
        {
            timer -= dt;
            float t = Mathf.Clamp01(1f - timer / Mathf.Max(0.0001f, ResolvedRegenDuration));
            float progress = 1f - t; // 1 → 0
            ApplyShaderProgress(progress);
            if (timer <= 0f) EnterIdle();
        }

        // --- Transitions ---------------------------------------------------------

        void EnterIdle()
        {
            state = IceState.Idle;
            ApplyShaderProgress(0f);
            ApplyDebrisRate(0f);
            if (visualRenderer != null) visualRenderer.enabled = true;
            for (int i = 0; i < myColliders.Length; i++) myColliders[i].enabled = true;
        }

        void EnterCracking()
        {
            state = IceState.Cracking;
            timer = ResolvedCrackingDuration;
            SoundService.Play(ResolvedCrackSound, transform.position);
        }

        void EnterShattered(bool jumpInherit)
        {
            state = IceState.Shattered;
            ApplyShaderProgress(1f);
            ApplyDebrisRate(0f);

            // Spawn shards BEFORE disabling colliders: a disabled Collider2D reports zero
            // bounds, collapsing the shard spawn distribution to a single point.
            SpawnShards(jumpInherit);

            if (visualRenderer != null) visualRenderer.enabled = false;
            for (int i = 0; i < myColliders.Length; i++) myColliders[i].enabled = false;

            if (frostCloudVFX != null) frostCloudVFX.Play(true);
            if (useEmissiveFlash)
            {
                flashRemaining = flashDuration;
                ApplyFlashIntensity(1f);
            }
            if (useCameraShake && impulseEmitter != null)
            {
                impulseEmitter.Emit(shakeForce);
            }
            SoundService.Play(ResolvedShatterSound, transform.position);
        }

        void EnterRegenerating()
        {
            state = IceState.Regenerating;
            timer = ResolvedRegenDuration;
            if (visualRenderer != null) visualRenderer.enabled = true;
            ApplyShaderProgress(1f);
            SoundService.Play(ResolvedRegenSound, transform.position);
        }

        // --- Public API for Zone -------------------------------------------------

        public void RequestRegen()
        {
            if (state != IceState.Shattered) return;
            EnterRegenerating();
        }

        // --- OnLaunch handler ----------------------------------------------------

        void OnSlimeLaunch(Vector2 pos, Vector2 launchVel, bool wasHop)
        {
            if (state != IceState.Cracking) return;
            if (System.Array.IndexOf(myColliders, slime.SurfaceCollider) < 0) return;

            pendingLaunchVel = launchVel;
            EnterShattered(jumpInherit: true);
        }

        // --- Shard spawn ---------------------------------------------------------

        void SpawnShards(bool jumpInherit)
        {
            if (resolvedConfig == null) return;
            var shardRuntimePrefab = resolvedConfig.shardRuntimePrefab;
            var shardSet = resolvedConfig.shardSet;
            if (shardRuntimePrefab == null) return;
            if (shardSet == null || shardSet.shards == null || shardSet.shards.Count == 0) return;
            if (platformCollider == null) return;

            Bounds bb = platformCollider.bounds;
            Vector2 center = bb.center;
            Vector2 inherit = jumpInherit ? pendingLaunchVel * ResolvedJumpVelocityInherit : Vector2.zero;

            int shardLib = shardSet.shards.Count;
            int count = ResolvedShardCount;
            float speedMin = ResolvedShardSpeedMin;
            float speedMax = ResolvedShardSpeedMax;
            float upBias = ResolvedShardUpwardBias;
            float spinDeg = ResolvedShardSpinDeg;
            float lifeMin = ResolvedShardLifetimeMin;
            float lifeMax = ResolvedShardLifetimeMax;
            float fadeWin = ResolvedShardFadeWindow;
            Vector2 scaleRange = ResolvedShardScaleRange;

            for (int i = 0; i < count; i++)
            {
                // AABB sampling, not true in-collider sampling: platforms are mostly convex,
                // and on odd shapes the stray outside spawns read as outer chips.
                Vector2 spawn = new Vector2(
                    Random.Range(bb.min.x, bb.max.x),
                    Random.Range(bb.min.y, bb.max.y));

                int idx = Random.Range(0, shardLib);
                var sd = shardSet.shards[idx];
                if (sd.mesh == null) continue;

                var go = Instantiate(shardRuntimePrefab, spawn, Random.rotation);
                float scale = Random.Range(scaleRange.x, scaleRange.y);
                go.transform.localScale = Vector3.one * scale;

                var mf = go.GetComponent<MeshFilter>();
                if (mf != null) mf.sharedMesh = sd.mesh;

                var rb = go.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    Vector2 outDir = (spawn - center);
                    if (outDir.sqrMagnitude < 0.0001f)
                        outDir = Random.insideUnitCircle.normalized;
                    else
                        outDir.Normalize();

                    Vector2 vel = outDir * Random.Range(speedMin, speedMax)
                                + Vector2.up * upBias
                                + inherit;
                    rb.linearVelocity = vel;
                    rb.angularVelocity = Random.Range(-spinDeg, spinDeg);
                }

                var rt = go.GetComponent<ThinIceShardRuntime>();
                if (rt != null)
                {
                    float life = Random.Range(lifeMin, lifeMax);
                    rt.Begin(life, fadeWin);
                }
            }
        }

        // --- Shader / debris helpers --------------------------------------------

        void CacheBaseCrackColor()
        {
            if (visualRenderer == null) return;
            visualRenderer.GetPropertyBlock(mpb);
            baseCrackColor = mpb.GetColor(CrackColorID);
            if (baseCrackColor.a == 0f && visualRenderer.sharedMaterial != null
                && visualRenderer.sharedMaterial.HasProperty(CrackColorID))
            {
                baseCrackColor = visualRenderer.sharedMaterial.GetColor(CrackColorID);
            }
            crackColorCached = true;
        }

        void ApplyShaderProgress(float progress)
        {
            if (visualRenderer == null) return;
            visualRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(CrackProgressID, progress);
            visualRenderer.SetPropertyBlock(mpb);
        }

        // shape.position/scale are PS-local, so the emitter is parked unrotated on the collider centre.
        void ConfigureDebrisShape()
        {
            if (debrisVFX == null || platformCollider == null) return;
            Bounds w = platformCollider.bounds;
            debrisVFX.transform.SetPositionAndRotation(w.center, Quaternion.identity);
            var shape = debrisVFX.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position  = Vector3.zero;
            shape.scale     = new Vector3(
                Mathf.Max(0.01f, w.size.x),
                Mathf.Max(0.01f, w.size.y),
                0.05f);
        }

        void ApplyDebrisRate(float progress)
        {
            if (debrisVFX == null) return;
            var emission = debrisVFX.emission;
            emission.rateOverTime = progress * ResolvedDebrisPeakRate;
            if (progress > 0f && !debrisVFX.isPlaying) debrisVFX.Play(true);
            else if (progress <= 0f && debrisVFX.isPlaying) debrisVFX.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void ApplyFlashIntensity(float t)
        {
            if (visualRenderer == null) return;
            if (!crackColorCached) CacheBaseCrackColor();
            visualRenderer.GetPropertyBlock(mpb);
            Color c = Color.Lerp(baseCrackColor, flashColor * flashIntensity, t);
            mpb.SetColor(CrackColorID, c);
            visualRenderer.SetPropertyBlock(mpb);
        }

        // --- Editor preview ------------------------------------------------------

#if UNITY_EDITOR
        public void EditorPreviewProgress(float p)
        {
            if (visualRenderer == null) visualRenderer = GetComponentInChildren<Renderer>();
            if (mpb == null) mpb = new MaterialPropertyBlock();
            ApplyShaderProgress(Mathf.Clamp01(p));
        }
#endif
    }
}
