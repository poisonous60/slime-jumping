using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// Icicle hanging from a ceiling. When the slime enters a box-shaped detection
    /// zone beneath it, the icicle shakes in place, then drops at a constant speed.
    /// On impact with a configured ground layer it breaks — particles, sound, and
    /// the solid collider disables. The solid collider travels with the icicle
    /// during the fall so the slime can ride it.
    ///
    /// When `respawnTime >= 0`, the icicle restores to its spawn position after a
    /// cooldown and plays a scale-in regen animation; `respawnTime < 0` makes it
    /// one-shot (matches ThinIcePlatform's respawn semantics).
    ///
    /// Visual shake is applied to a separate `visualRoot` child so the collider is
    /// untouched by the tremor (per project rule: divide visual and collider).
    [RequireComponent(typeof(Rigidbody2D))]
    public class IcicleHazard : MonoBehaviour
    {
        private enum IcicleState { Idle, Shaking, Falling, Broken, Regenerating }

        [Header("Detection Box (local space, beneath icicle)")]
        [Tooltip("Center of the detection box relative to icicle origin.")]
        [SerializeField] private Vector2 detectionOffset = new Vector2(0f, -3f);
        [Tooltip("Size of the detection box (X = horizontal reach, Y = vertical reach).")]
        [SerializeField] private Vector2 detectionSize = new Vector2(1.5f, 6f);
        [Tooltip("Layers checked for the slime. Icicle triggers only if a SlimeController is found inside this box.")]
        [SerializeField] private LayerMask slimeLayer = ~0;

        [Header("Shake Phase")]
        [Tooltip("Seconds spent shaking in place before the icicle drops.")]
        [SerializeField] private float shakeDuration = 0.8f;
        [Tooltip("Max pixel-space displacement of the visual during shake.")]
        [SerializeField] private float shakeAmplitude = 0.08f;
        [Tooltip("Shake oscillation frequency (higher = buzzier).")]
        [SerializeField] private float shakeFrequency = 40f;

        [Header("Fall Phase")]
        [Tooltip("Constant downward speed while falling (world units / sec).")]
        [SerializeField] private float fallSpeed = 6f;
        [Tooltip("Layers that cause the icicle to break on contact. Exclude the slime layer so the slime can ride the icicle.")]
        [SerializeField] private LayerMask breakOnLayers = ~0;

        [Header("Visuals (mesh is swappable)")]
        [Tooltip("Shaken during tremor phase. Kept separate from the collider.")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private MeshFilter meshFilter;
        [SerializeField] private MeshRenderer meshRenderer;
        [Tooltip("Mesh used for the icicle. Applied to meshFilter at Awake/validate.")]
        [SerializeField] private Mesh mesh;
        [Tooltip("Material applied to meshRenderer at Awake/validate.")]
        [SerializeField] private Material material;

        [Header("Collision")]
        [Tooltip("Solid collider that slime rides. Travels with the icicle. Disabled on break.")]
        [SerializeField] private Collider2D solidCollider;

        [Header("Break Particles")]
        [Tooltip("Played once at the moment the icicle hits the ground.")]
        [SerializeField] private ParticleSystem breakParticles;

        [Header("Respawn")]
        [Tooltip("Seconds after break before regen starts. Negative = one-shot (no respawn).")]
        [SerializeField] private float respawnTime = -1f;
        [Tooltip("Regen animation duration. Visual scales from 0 to original over this window (ease-out). Collider stays OFF during regen so slime can pass through.")]
        [SerializeField] private float regenDuration = 0.6f;
        [Tooltip("Layers that block regen when overlapping the icicle at its spawn position. Typically includes the slime so we don't pop the icicle into it.")]
        [SerializeField] private LayerMask regenBlockingLayers = ~0;

        [Header("Audio")]
        [SerializeField] private AudioClip shakeStartSound;
        [SerializeField] private AudioClip fallingSound;
        [SerializeField] private AudioClip impactSound;
        [Range(0f, 1f)][SerializeField] private float soundVolume = 1f;

        /// <summary>
        /// Overwrites the per-icicle audio clips + volume. Called by
        /// IcicleAudioManager to broadcast a shared set across the scene.
        /// </summary>
        public void ApplyAudioSettings(AudioClip shakeStart, AudioClip falling, AudioClip impact, float volume)
        {
            shakeStartSound = shakeStart;
            fallingSound = falling;
            impactSound = impact;
            soundVolume = volume;
        }

        private IcicleState state = IcicleState.Idle;
        private float timer;
        private Vector3 visualOrigin;
        private Vector3 originalVisualScale = Vector3.one;
        private Vector2 spawnPosition;
        private bool respawnReady;
        private Rigidbody2D rb;
        private readonly RaycastHit2D[] castHits = new RaycastHit2D[1];
        private readonly Collider2D[] overlapResults = new Collider2D[4];

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
        }

        private void OnValidate()
        {
            if (meshFilter != null && mesh != null) meshFilter.sharedMesh = mesh;
            if (meshRenderer != null && material != null) meshRenderer.sharedMaterial = material;
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

        // --- State updates ---

        private void UpdateIdle()
        {
            Vector2 center = rb.position + (Vector2)transform.TransformVector(detectionOffset);
            Vector2 worldScale = transform.lossyScale;
            Vector2 size = new Vector2(
                Mathf.Abs(detectionSize.x * worldScale.x),
                Mathf.Abs(detectionSize.y * worldScale.y));
            float angle = transform.eulerAngles.z;

            Collider2D hit = Physics2D.OverlapBox(center, size, angle, slimeLayer);
            if (hit == null) return;
            if (hit.GetComponentInParent<SlimeController>() == null) return;

            SetState(IcicleState.Shaking);
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
            float dist = fallSpeed * Time.fixedDeltaTime;
            if (dist <= 0f) return;

            // Sweep the solid collider downward so high fall speeds can't tunnel
            // through thin ground, and slime riding on top can't falsely trigger
            // a break via OnCollisionEnter2D. Deterministic and layer-filtered.
            if (solidCollider != null)
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
                // Return to spawn position (invisible, collider still off) so the
                // overlap check below tests the regen footprint, not the impact point.
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
            float eased = 1f - Mathf.Pow(1f - t, 2f); // ease-out
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

            // Overlap() requires the collider to be enabled. Flip it on for one
            // query, then back off so nothing can rest on a mid-regen icicle.
            solidCollider.enabled = true;
            int hits = solidCollider.Overlap(filter, overlapResults);
            solidCollider.enabled = false;
            return hits > 0;
        }

        // --- Transitions ---

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
                    PlaySound(shakeStartSound);
                    if (shakeDuration <= 0f)
                    {
                        SetState(IcicleState.Falling);
                        return;
                    }
                    break;

                case IcicleState.Falling:
                    if (visualRoot != null) visualRoot.localPosition = visualOrigin;
                    PlaySound(fallingSound);
                    break;

                case IcicleState.Broken:
                    PlaySound(impactSound);
                    if (breakParticles != null)
                    {
                        // Only detach in one-shot mode — otherwise the respawning
                        // icicle still needs its particle system child for the next break.
                        if (IsOneShot) breakParticles.transform.SetParent(null, true);
                        breakParticles.Play(true);
                    }
                    if (solidCollider != null) solidCollider.enabled = false;
                    if (meshRenderer != null) meshRenderer.enabled = false;
                    respawnReady = false;
                    timer = respawnTime;
                    if (IsOneShot) enabled = false;
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
                    // solidCollider stays OFF during fade-in so slime can pass through
                    // a not-yet-solid icicle; Idle re-enables it.
                    if (regenDuration <= 0f)
                    {
                        SetState(IcicleState.Idle);
                        return;
                    }
                    break;
            }
        }

        private void PlaySound(AudioClip clip)
        {
            if (clip == null) return;
            SoundService.PlayAt(clip, transform.position, SoundCategory.SFX_Object, soundVolume);
        }

        // --- Gizmos ---

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
