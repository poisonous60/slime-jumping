using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;
using SlimeJumping.Platforms.ThinIceV2;

namespace SlimeJumping.Mechanisms
{
    /// Idle → Shaking → Falling → Broken → Regenerating icicle; curve-driven fall speed, shatters on impact.
    /// Sibling of <see cref="IcicleHazard"/> v1, which stays in place for existing scenes.
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(Rigidbody2D))]
    public class IcicleHazardV2 : MonoBehaviour
    {
        private enum IcicleState { Idle, Shaking, Falling, Broken, Regenerating }

        [Header("Preset (overwrites tuning fields below as a set)")]
        [Tooltip("Optional. When assigned, the preset's detection / shake / fall / shatter / respawn / audio values overwrite the inline fields below on OnValidate (edit-time) and Awake (runtime). Visual asset refs (mesh/material) and scene refs (visualRoot, mesh/renderer, solidCollider, impulseEmitter) stay on this component regardless.")]
        [SerializeField] private IcicleHazardV2Preset preset;
        [SerializeField, HideInInspector] private IcicleHazardV2Preset _lastAppliedPreset;

        [Header("Detection Box (local space, beneath icicle)")]
        [SerializeField] private Vector2 detectionOffset = new Vector2(0f, -3f);
        [SerializeField] private Vector2 detectionSize = new Vector2(1.5f, 6f);
        [SerializeField] private LayerMask slimeLayer = ~0;

        [Header("Shake Phase")]
        [SerializeField] private float shakeDuration = 0.8f;
        [SerializeField] private float shakeAmplitude = 0.08f;
        [SerializeField] private float shakeFrequency = 40f;

        [Header("Fall Phase")]
        [Tooltip("Time(seconds since fall start) → downward speed (units/sec). After the last keyframe, the final value is held.")]
        [SerializeField] private AnimationCurve fallSpeedCurve = new AnimationCurve(
            new Keyframe(0f,   1.5f),
            new Keyframe(0.6f, 6f),
            new Keyframe(1.2f, 6f));
        [SerializeField] private LayerMask breakOnLayers = ~0;
        [SerializeField] private float breakImmunityDuration = 0.3f;

        [Header("Visuals (mesh is swappable)")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;
        [SerializeField] private Mesh mesh;
        [SerializeField] private Material material;

        [Header("Collision")]
        [SerializeField] private Collider2D solidCollider;

        [Header("Shatter — shard launch")]
        [SerializeField] private ThinIceShardSet shardSet;
        [SerializeField] private GameObject shardRuntimePrefab;
        [Min(1)] [SerializeField] private int shardCount = 16;
        [Min(0f)] [SerializeField] private float shardSpeedMin = 2f;
        [Min(0f)] [SerializeField] private float shardSpeedMax = 4f;
        [SerializeField] private float shardUpwardBias = 0.7f;
        [Min(0f)] [SerializeField] private float shardSpinDeg = 360f;
        [Min(0.05f)] [SerializeField] private float shardLifetimeMin = 1.2f;
        [Min(0.05f)] [SerializeField] private float shardLifetimeMax = 1.8f;
        [Min(0.01f)] [SerializeField] private float shardFadeWindow = 0.4f;
        [SerializeField] private Vector2 shardScaleRange = new Vector2(0.7f, 1.3f);
        [Range(0f, 1f)] [SerializeField] private float impactVelocityInherit = 0.3f;

        [Header("Shatter — VFX / audio / shake")]
        [SerializeField] private GameObject frostCloudVFXPrefab;
        [SerializeField] private SlimeJumping.Audio.SoundDef shatterSound;

        [SerializeField] private bool useEmissiveFlash = false;
        [SerializeField] private Color flashColor = Color.white;
        [Min(0.01f)] [SerializeField] private float flashDuration = 0.05f;
        [Min(1f)] [SerializeField] private float flashIntensity = 4f;
        [Tooltip("Shader color property boosted during the flash. Default '_BaseColor' matches URP/Lit; set to '_CrackColor' for ThinIce-style emissive.")]
        [SerializeField] private string flashColorProperty = "_BaseColor";

        [SerializeField] private bool useCameraShake = false;
        [SerializeField] private ImpulseEmitter impulseEmitter;
        [Min(0f)] [SerializeField] private float shakeForce = 0.5f;

        [Header("Respawn")]
        [SerializeField] private float respawnTime = -1f;
        [SerializeField] private float regenDuration = 0.6f;
        [SerializeField] private LayerMask regenBlockingLayers = ~0;

        [Header("Audio")]
        [SerializeField] private SlimeJumping.Audio.SoundDef shakeStartSound;
        [SerializeField] private SlimeJumping.Audio.SoundDef fallingSound;
        [SerializeField] private SlimeJumping.Audio.SoundDef impactSound;

        /// <summary>Mirrors v1's API so IcicleAudioManager can broadcast a shared set.</summary>
        public void ApplyAudioSettings(SlimeJumping.Audio.SoundDef shakeStart,
                                       SlimeJumping.Audio.SoundDef falling,
                                       SlimeJumping.Audio.SoundDef impact)
        {
            shakeStartSound = shakeStart;
            fallingSound = falling;
            impactSound = impact;
        }

        private IcicleState state = IcicleState.Idle;
        private float timer;
        private float fallElapsed;
        private float lastFallSpeed;
        private float flashRemaining;
        private Vector3 visualOrigin;
        private Vector3 originalVisualScale = Vector3.one;
        private Vector2 spawnPosition;
        private bool respawnReady;
        private Rigidbody2D rb;
        private MaterialPropertyBlock mpb;
        private Color baseFlashColor;
        private bool baseFlashColorCached;
        private int flashColorPropertyID;
        private readonly RaycastHit2D[] castHits = new RaycastHit2D[1];
        private readonly Collider2D[] overlapResults = new Collider2D[4];
        private readonly Collider2D[] detectResults = new Collider2D[8];

        private bool IsOneShot => respawnTime < 0f;

        private void Reset()
        {
            visualRoot = transform.Find("Visual");
            if (visualRoot != null)
            {
                meshFilter = visualRoot.GetComponent<MeshFilter>();
                meshRenderer = visualRoot.GetComponent<MeshRenderer>();
            }
            solidCollider = GetComponent<Collider2D>();
        }

        private void Awake()
        {
            ApplyPreset(preset);

            rb = GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;

            if (meshFilter != null && mesh != null) meshFilter.sharedMesh = mesh;
            if (meshRenderer != null && material != null) meshRenderer.sharedMaterial = material;

            if (visualRoot != null)
            {
                visualOrigin = visualRoot.localPosition;
                originalVisualScale = visualRoot.localScale;
            }

            spawnPosition = rb.position;

            mpb = new MaterialPropertyBlock();
            flashColorPropertyID = string.IsNullOrEmpty(flashColorProperty)
                ? Shader.PropertyToID("_BaseColor")
                : Shader.PropertyToID(flashColorProperty);
        }

        private void OnValidate()
        {
            if (preset != _lastAppliedPreset)
            {
                _lastAppliedPreset = preset;
                if (preset != null) ApplyPreset(preset);
            }

            if (meshFilter != null && mesh != null) meshFilter.sharedMesh = mesh;
            if (meshRenderer != null && material != null) meshRenderer.sharedMaterial = material;

            if (fallSpeedCurve == null || fallSpeedCurve.length == 0)
                Debug.LogWarning($"{nameof(IcicleHazardV2)} '{name}': fallSpeedCurve has no keys; the icicle will not fall.", this);
        }

        private void ApplyPreset(IcicleHazardV2Preset p)
        {
            if (p == null) return;
            detectionOffset = p.detectionOffset;
            detectionSize = p.detectionSize;
            slimeLayer = p.slimeLayer;

            shakeDuration = p.shakeDuration;
            shakeAmplitude = p.shakeAmplitude;
            shakeFrequency = p.shakeFrequency;

            fallSpeedCurve = p.fallSpeedCurve;
            breakOnLayers = p.breakOnLayers;
            breakImmunityDuration = p.breakImmunityDuration;

            shardSet = p.shardSet;
            shardRuntimePrefab = p.shardRuntimePrefab;
            shardCount = p.shardCount;
            shardSpeedMin = p.shardSpeedMin;
            shardSpeedMax = p.shardSpeedMax;
            shardUpwardBias = p.shardUpwardBias;
            shardSpinDeg = p.shardSpinDeg;
            shardLifetimeMin = p.shardLifetimeMin;
            shardLifetimeMax = p.shardLifetimeMax;
            shardFadeWindow = p.shardFadeWindow;
            shardScaleRange = p.shardScaleRange;
            impactVelocityInherit = p.impactVelocityInherit;

            frostCloudVFXPrefab = p.frostCloudVFXPrefab;
            shatterSound = p.shatterSound;

            useEmissiveFlash = p.useEmissiveFlash;
            flashColor = p.flashColor;
            flashDuration = p.flashDuration;
            flashIntensity = p.flashIntensity;

            useCameraShake = p.useCameraShake;
            shakeForce = p.shakeForce;

            respawnTime = p.respawnTime;
            regenDuration = p.regenDuration;
            regenBlockingLayers = p.regenBlockingLayers;

            shakeStartSound = p.shakeStartSound;
            fallingSound = p.fallingSound;
            impactSound = p.impactSound;
        }

        private void FixedUpdate()
        {
            switch (state)
            {
                case IcicleState.Idle:         UpdateIdle();         break;
                case IcicleState.Shaking:      UpdateShaking();      break;
                case IcicleState.Falling:      UpdateFalling();      break;
                case IcicleState.Broken:       UpdateBroken();       break;
                case IcicleState.Regenerating: UpdateRegenerating(); break;
            }
        }

        private void Update()
        {
            if (flashRemaining > 0f)
            {
                flashRemaining -= Time.deltaTime;
                float t = Mathf.Clamp01(flashRemaining / flashDuration);
                ApplyFlashIntensity(t);
                if (flashRemaining <= 0f) ApplyFlashIntensity(0f);
            }
        }

        // --- State updates ---

        private void UpdateIdle()
        {
            Vector2 center = rb.position + (Vector2)transform.TransformVector(detectionOffset);
            Vector2 worldScale = transform.lossyScale;
            Vector2 size = new Vector2(
                Mathf.Abs(detectionSize.x * worldScale.x),
                Mathf.Abs(detectionSize.y * worldScale.y));
            float angle = transform.eulerAngles.z;

            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = slimeLayer,
                useTriggers = true,
            };
            int count = Physics2D.OverlapBox(center, size, angle, filter, detectResults);
            for (int i = 0; i < count; i++)
            {
                if (detectResults[i].GetComponentInParent<SlimeController>() != null)
                {
                    SetState(IcicleState.Shaking);
                    return;
                }
            }
        }

        private void UpdateShaking()
        {
            timer -= Time.fixedDeltaTime;
            if (timer <= 0f)
            {
                SetState(IcicleState.Falling);
                return;
            }

            if (visualRoot != null)
            {
                float t = Time.time * shakeFrequency;
                float ox = Mathf.Sin(t) * shakeAmplitude;
                float oy = Mathf.Cos(t * 0.73f) * shakeAmplitude * 0.4f;
                visualRoot.localPosition = visualOrigin + new Vector3(ox, oy, 0f);
            }
        }

        private void UpdateFalling()
        {
            fallElapsed += Time.fixedDeltaTime;
            float speed = SampleFallSpeed(fallElapsed);
            lastFallSpeed = speed;

            float dist = speed * Time.fixedDeltaTime;
            if (dist <= 0f) return;

            bool armed = fallElapsed >= breakImmunityDuration;
            if (armed && solidCollider != null)
            {
                ContactFilter2D filter = new ContactFilter2D
                {
                    useLayerMask = true,
                    layerMask = breakOnLayers,
                    useTriggers = false,
                };
                int count = solidCollider.Cast(Vector2.down, filter, castHits, dist);
                if (count > 0)
                {
                    float travel = Mathf.Max(0f, castHits[0].distance);
                    rb.MovePosition(rb.position + Vector2.down * travel);
                    SetState(IcicleState.Broken);
                    return;
                }
            }

            rb.MovePosition(rb.position + Vector2.down * dist);
        }

        private void UpdateBroken()
        {
            if (IsOneShot) return;

            if (!respawnReady)
            {
                timer -= Time.fixedDeltaTime;
                if (timer > 0f) return;
                respawnReady = true;
                rb.position = spawnPosition;
            }

            if (IsRegenBlocked()) return;

            SetState(IcicleState.Regenerating);
        }

        private void UpdateRegenerating()
        {
            timer -= Time.fixedDeltaTime;
            float elapsed = Mathf.Max(0f, regenDuration - timer);
            float t = regenDuration > 0f ? Mathf.Clamp01(elapsed / regenDuration) : 1f;
            float eased = 1f - Mathf.Pow(1f - t, 2f);
            if (visualRoot != null) visualRoot.localScale = originalVisualScale * eased;

            if (timer <= 0f)
                SetState(IcicleState.Idle);
        }

        private bool IsRegenBlocked()
        {
            if (solidCollider == null) return false;

            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = regenBlockingLayers,
                useTriggers = false,
            };

            solidCollider.enabled = true;
            int hits = solidCollider.Overlap(filter, overlapResults);
            solidCollider.enabled = false;
            return hits > 0;
        }

        // Public so debug / inspector tools can preview the profile. Pure read.
        public float SampleFallSpeed(float t)
        {
            if (fallSpeedCurve == null || fallSpeedCurve.length == 0) return 0f;
            float lastKeyTime = fallSpeedCurve[fallSpeedCurve.length - 1].time;
            float clamped = Mathf.Min(t, lastKeyTime);
            return Mathf.Max(0f, fallSpeedCurve.Evaluate(clamped));
        }

        private void SetState(IcicleState newState)
        {
            state = newState;

            switch (newState)
            {
                case IcicleState.Idle:
                    respawnReady = false;
                    rb.position = spawnPosition;
                    if (solidCollider != null) solidCollider.enabled = true;
                    if (meshRenderer != null) meshRenderer.enabled = true;
                    if (visualRoot != null)
                    {
                        visualRoot.localPosition = visualOrigin;
                        visualRoot.localScale = originalVisualScale;
                    }
                    break;

                case IcicleState.Shaking:
                    timer = shakeDuration;
                    SoundService.Play(shakeStartSound, transform.position);
                    if (shakeDuration <= 0f)
                    {
                        SetState(IcicleState.Falling);
                        return;
                    }
                    break;

                case IcicleState.Falling:
                    fallElapsed = 0f;
                    lastFallSpeed = 0f;
                    if (visualRoot != null) visualRoot.localPosition = visualOrigin;
                    SoundService.Play(fallingSound, transform.position);
                    break;

                case IcicleState.Broken:
                    EnterBroken();
                    break;

                case IcicleState.Regenerating:
                    timer = regenDuration;
                    rb.position = spawnPosition;
                    if (visualRoot != null)
                    {
                        visualRoot.localPosition = visualOrigin;
                        visualRoot.localScale = originalVisualScale * 0f;
                    }
                    if (meshRenderer != null) meshRenderer.enabled = true;
                    if (regenDuration <= 0f)
                    {
                        SetState(IcicleState.Idle);
                        return;
                    }
                    break;
            }
        }

        // --- Shatter ---

        private void EnterBroken()
        {
            SoundService.Play(impactSound, transform.position);
            SpawnShards(lastFallSpeed);
            SpawnFrostCloud();
            SoundService.Play(shatterSound, transform.position);

            if (useEmissiveFlash)
            {
                flashRemaining = flashDuration;
                ApplyFlashIntensity(1f);
            }
            if (useCameraShake && impulseEmitter != null)
            {
                impulseEmitter.Emit(shakeForce);
            }

            if (solidCollider != null) solidCollider.enabled = false;
            if (meshRenderer != null) meshRenderer.enabled = false;
            respawnReady = false;
            timer = respawnTime;
            if (IsOneShot) enabled = false;
        }

        private void SpawnFrostCloud()
        {
            if (frostCloudVFXPrefab == null) return;
            Vector3 origin = solidCollider != null ? solidCollider.bounds.center : (Vector3)rb.position;
            // Not parented — the prefab's ParticleSystem must self-destroy via Stop Action.
            Instantiate(frostCloudVFXPrefab, origin, Quaternion.identity);
        }

        // Mirror of ThinIcePlatformV2.SpawnShards, plus downward bias from the impact speed.
        private void SpawnShards(float fallSpeedAtImpact)
        {
            if (shardRuntimePrefab == null) return;
            if (shardSet == null || shardSet.shards == null || shardSet.shards.Count == 0) return;
            if (solidCollider == null) return;

            Bounds bb = solidCollider.bounds;
            Vector2 center = bb.center;
            Vector2 inherit = Vector2.down * (fallSpeedAtImpact * impactVelocityInherit);

            int shardLib = shardSet.shards.Count;
            for (int i = 0; i < shardCount; i++)
            {
                Vector2 spawn = new Vector2(
                    Random.Range(bb.min.x, bb.max.x),
                    Random.Range(bb.min.y, bb.max.y));

                int idx = Random.Range(0, shardLib);
                var sd = shardSet.shards[idx];
                if (sd.mesh == null) continue;

                var go = Instantiate(shardRuntimePrefab, spawn, Random.rotation);
                float scale = Random.Range(shardScaleRange.x, shardScaleRange.y);
                go.transform.localScale = Vector3.one * scale;

                var mf = go.GetComponent<MeshFilter>();
                if (mf != null) mf.sharedMesh = sd.mesh;

                var srb = go.GetComponent<Rigidbody2D>();
                if (srb != null)
                {
                    Vector2 outDir = (spawn - center);
                    if (outDir.sqrMagnitude < 0.0001f)
                        outDir = Random.insideUnitCircle.normalized;
                    else
                        outDir.Normalize();

                    Vector2 vel = outDir * Random.Range(shardSpeedMin, shardSpeedMax)
                                + Vector2.up * shardUpwardBias
                                + inherit;
                    srb.linearVelocity = vel;
                    srb.angularVelocity = Random.Range(-shardSpinDeg, shardSpinDeg);
                }

                var rt = go.GetComponent<ThinIceShardRuntime>();
                if (rt != null)
                {
                    float life = Random.Range(shardLifetimeMin, shardLifetimeMax);
                    rt.Begin(life, shardFadeWindow);
                }
            }
        }

        private void ApplyFlashIntensity(float t)
        {
            if (meshRenderer == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            if (!baseFlashColorCached)
            {
                meshRenderer.GetPropertyBlock(mpb);
                baseFlashColor = mpb.GetColor(flashColorPropertyID);
                if (baseFlashColor == default && meshRenderer.sharedMaterial != null
                    && meshRenderer.sharedMaterial.HasProperty(flashColorPropertyID))
                {
                    baseFlashColor = meshRenderer.sharedMaterial.GetColor(flashColorPropertyID);
                }
                baseFlashColorCached = true;
            }
            meshRenderer.GetPropertyBlock(mpb);
            Color c = Color.Lerp(baseFlashColor, flashColor * flashIntensity, t);
            mpb.SetColor(flashColorPropertyID, c);
            meshRenderer.SetPropertyBlock(mpb);
        }


        private void OnDrawGizmosSelected()
        {
            Vector2 center = (Vector2)transform.position + (Vector2)transform.TransformVector(detectionOffset);
            Vector2 worldScale = transform.lossyScale;
            Vector2 size = new Vector2(
                Mathf.Abs(detectionSize.x * worldScale.x),
                Mathf.Abs(detectionSize.y * worldScale.y));
            float angle = transform.eulerAngles.z;

            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, Quaternion.Euler(0f, 0f, angle), Vector3.one);
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.15f);
            Gizmos.DrawCube(Vector3.zero, size);
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.7f);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = old;
        }
    }
}
