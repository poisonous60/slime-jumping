using System;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Platforms
{
    /// CrumblingPlatform plus crack/shatter/regen audio, a shatter ParticleSystem, and a scale+alpha regen animation.
    public class ThinIcePlatform : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Seconds between first attach and shatter")]
        [SerializeField] float triggerDelay = 1.0f;
        [Tooltip("Shatter animation duration")]
        [SerializeField] float crumbleDuration = 0.15f;
        [Tooltip("Seconds hidden before regen starts. Negative = permanent")]
        [SerializeField] float respawnTime = 3.0f;
        [Tooltip("Regen animation duration (scale + fade in)")]
        [SerializeField] float regenDuration = 0.6f;

        [Header("Angle Filter")]
        [SerializeField] bool useAngleLimit;
        [Tooltip("0 = floor, 90 = wall, 180 = ceiling")]
        [SerializeField] float minSurfaceAngle;
        [SerializeField] float maxSurfaceAngle = 180f;

        [Header("Visuals")]
        [SerializeField] Transform visualRoot;
        [SerializeField] Renderer visualRenderer;
        [SerializeField] float maxShakeOffset = 0.05f;
        [Tooltip("Tint during trigger countdown")]
        [SerializeField] Color crackingTint = new Color(0.55f, 0.8f, 1f, 1f);

        [Header("Audio")]
        [Tooltip("Per-tile clips are overwritten at Awake by ThinIcePlatformAudioManager " +
                 "if one is present in the scene.")]
        [SerializeField] AudioClip crackSound;
        [SerializeField] AudioClip shatterSound;
        [SerializeField] AudioClip regenSound;
        [Range(0f, 1f)] [SerializeField] float soundVolume = 1f;

        [Header("Particles")]
        [Tooltip("Played once at the moment the platform shatters")]
        [SerializeField] ParticleSystem shatterParticles;

        [Header("Respawn")]
        [Tooltip("Layers that block regen when overlapping")]
        [SerializeField] LayerMask blockingLayers = ~0;

        enum IceState { Idle, Triggered, Crumbling, Gone, Regenerating }

        IceState state = IceState.Idle;
        float timer;
        bool readyToRespawn;

        bool IsOneShot => respawnTime < 0f;

        SlimeController slime;
        Collider2D[] myColliders;
        Collider2D[] overlapResults = new Collider2D[4];

        MaterialPropertyBlock mpb;
        Color originalColor;
        Vector3 originalScale = Vector3.one;
        static readonly int ColorID = Shader.PropertyToID("_Color");

        /// <summary>Overwrites the per-tile clips + volume; ThinIcePlatformAudioManager broadcasts a shared set across the scene.</summary>
        public void ApplyAudioSettings(AudioClip crack, AudioClip shatter, AudioClip regen, float volume)
        {
            crackSound = crack;
            shatterSound = shatter;
            regenSound = regen;
            soundVolume = volume;
        }

        void Awake()
        {
            myColliders = GetComponents<Collider2D>();
            mpb = new MaterialPropertyBlock();
            if (visualRenderer != null)
            {
                visualRenderer.GetPropertyBlock(mpb);
                originalColor = mpb.GetColor(ColorID);
                if (originalColor.a == 0f)
                    originalColor = Color.white;
            }
            if (visualRoot != null)
                originalScale = visualRoot.localScale;
        }

        void FixedUpdate()
        {
            if (slime == null)
            {
                slime = FindAnyObjectByType<SlimeController>();
                if (slime == null) return;
            }

            float dt = Time.fixedDeltaTime;

            switch (state)
            {
                case IceState.Idle:         UpdateIdle();            break;
                case IceState.Triggered:    UpdateTriggered(dt);     break;
                case IceState.Crumbling:    UpdateCrumbling(dt);     break;
                case IceState.Gone:         UpdateGone(dt);          break;
                case IceState.Regenerating: UpdateRegenerating(dt);  break;
            }
        }

        // --- State Updates ---

        void UpdateIdle()
        {
            if (slime.CurrentState != SlimeController.SlimeState.Attached) return;
            if (Array.IndexOf(myColliders, slime.SurfaceCollider) < 0) return;

            if (useAngleLimit)
            {
                float angle = Mathf.Acos(Vector2.Dot(slime.SurfaceNormal, slime.GravityUp)) * Mathf.Rad2Deg;
                if (angle < minSurfaceAngle || angle > maxSurfaceAngle) return;
            }

            SetState(IceState.Triggered);
        }

        void UpdateTriggered(float dt)
        {
            timer -= dt;
            if (timer <= 0f)
            {
                SetState(IceState.Crumbling);
                return;
            }

            float t = 1f - timer / triggerDelay;
            Color c = Color.Lerp(originalColor, crackingTint, t);
            ApplyVisual(c, 1f, maxShakeOffset * 0.4f * t, 1f);
        }

        void UpdateCrumbling(float dt)
        {
            timer -= dt;
            if (timer <= 0f)
            {
                SetState(IceState.Gone);
                return;
            }

            float t = 1f - timer / crumbleDuration;
            ApplyVisual(crackingTint, 1f - t, maxShakeOffset, 1f);
        }

        void UpdateGone(float dt)
        {
            if (IsOneShot) return; // disabled in SetState for one-shot

            if (!readyToRespawn)
            {
                timer -= dt;
                if (timer <= 0f)
                    readyToRespawn = true;
                return;
            }

            // Pre-check only: colliders stay off through the regen animation, commit only if clear.
            if (IsRegenBlocked())
                return;

            SetState(IceState.Regenerating);
        }

        void UpdateRegenerating(float dt)
        {
            timer -= dt;
            float elapsed = Mathf.Max(0f, regenDuration - timer);
            float t = regenDuration > 0f ? Mathf.Clamp01(elapsed / regenDuration) : 1f;

            float eased = 1f - Mathf.Pow(1f - t, 2f);

            ApplyVisual(originalColor, eased, 0f, eased);

            if (timer <= 0f)
                SetState(IceState.Idle);
        }

        // --- State Transitions ---

        void SetState(IceState newState)
        {
            state = newState;

            switch (newState)
            {
                case IceState.Idle:
                    readyToRespawn = false;
                    foreach (var col in myColliders) col.enabled = true;
                    if (visualRenderer != null) visualRenderer.enabled = true;
                    if (visualRoot != null)
                    {
                        visualRoot.localPosition = Vector3.zero;
                        visualRoot.localScale = originalScale;
                    }
                    ApplyVisual(originalColor, 1f, 0f, 1f);
                    break;

                case IceState.Triggered:
                    timer = triggerDelay;
                    PlaySound(crackSound);
                    if (triggerDelay <= 0f)
                    {
                        SetState(IceState.Crumbling);
                        return;
                    }
                    break;

                case IceState.Crumbling:
                    timer = crumbleDuration;
                    if (crumbleDuration <= 0f)
                    {
                        SetState(IceState.Gone);
                        return;
                    }
                    break;

                case IceState.Gone:
                    foreach (var col in myColliders) col.enabled = false;
                    if (visualRenderer != null) visualRenderer.enabled = false;
                    if (visualRoot != null)
                    {
                        visualRoot.localPosition = Vector3.zero;
                        visualRoot.localScale = originalScale;
                    }
                    readyToRespawn = false;
                    timer = respawnTime;
                    PlaySound(shatterSound);
                    if (shatterParticles != null)
                        shatterParticles.Play(true);
                    if (IsOneShot)
                        enabled = false;
                    break;

                case IceState.Regenerating:
                    timer = regenDuration;
                    // Colliders off through the fade-in so the slime passes through a not-yet-solid tile; Idle re-enables.
                    foreach (var col in myColliders) col.enabled = false;
                    if (visualRenderer != null) visualRenderer.enabled = true;
                    if (visualRoot != null)
                        visualRoot.localPosition = Vector3.zero;
                    PlaySound(regenSound);
                    if (regenDuration <= 0f)
                    {
                        SetState(IceState.Idle);
                        return;
                    }
                    // Start visual at scale/alpha 0 to avoid a 1-frame pop.
                    ApplyVisual(originalColor, 0f, 0f, 0f);
                    break;
            }
        }

        // --- Helpers ---

        bool IsRegenBlocked()
        {
            ContactFilter2D filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = blockingLayers,
                useTriggers = false
            };

            // Re-disable right after each Overlap, else the slime can rest on a mid-regen tile.
            foreach (var col in myColliders)
            {
                col.enabled = true;
                int hits = col.Overlap(filter, overlapResults);
                col.enabled = false;
                if (hits > 0) return true;
            }
            return false;
        }

        void ApplyVisual(Color color, float alpha, float shakeMagnitude, float scaleFactor)
        {
            if (visualRenderer != null)
            {
                color.a = alpha;
                mpb.SetColor(ColorID, color);
                visualRenderer.SetPropertyBlock(mpb);
            }

            if (visualRoot != null)
            {
                if (shakeMagnitude > 0f)
                {
                    Vector2 offset = UnityEngine.Random.insideUnitCircle * shakeMagnitude;
                    visualRoot.localPosition = new Vector3(offset.x, offset.y, 0f);
                }
                else
                {
                    visualRoot.localPosition = Vector3.zero;
                }

                visualRoot.localScale = originalScale * scaleFactor;
            }
        }

        void PlaySound(AudioClip clip)
        {
            if (clip == null) return;
            SoundService.PlayAt(clip, transform.position, SoundCategory.SFX_Object, soundVolume);
        }

        // --- Editor Helpers ---

        void Reset()
        {
            visualRoot = transform.Find("Visual");
            if (visualRoot != null)
                visualRenderer = visualRoot.GetComponent<Renderer>();
        }
    }
}
