using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Granite Blast (그라니테 블라스트) — energy cannon with three configurable firing modes.
    /// Branching: lightning tendrils that arc out and rain down on the ground around the target.
    /// Beam: a sustained thick laser drawn from muzzle to first blocker.
    /// Burst: a cone of fast bullet projectiles.
    /// FSM: Idle → Detecting → PreFire (준비) → FireStart (시작) → Firing (중) → PostFire (이후) → Cooldown.
    /// All hits route through SlimeController.ApplyHazardEffect.
    /// </summary>
    public class GraniteBlastEmitter : MonoBehaviour, IHazardSource
    {
        public enum FireMode { Branching, Beam, Burst }
        public enum State { Idle, Detecting, PreFire, FireStart, Firing, PostFire, Cooldown }

        [Header("Mode")]
        [SerializeField] private FireMode mode = FireMode.Branching;

        [Header("Target")]
        [Tooltip("Target to detect. If null, auto-finds SlimeController on Start.")]
        [SerializeField] private Transform target;
        [SerializeField] private float detectionRange = 18f;
        [Tooltip("Layers that block line of sight. Leave 0 to skip LOS checks.")]
        [SerializeField] private LayerMask lineOfSightBlockers = 0;

        [Header("Muzzle")]
        [Tooltip("Muzzle Transform — origin of the blast. If null, this transform.")]
        [SerializeField] private Transform muzzle;
        [Tooltip("If true, the blast aims at the live target's position at fire time. If false, fires along muzzle.right (barrel's local +X).")]
        [SerializeField] private bool aimAtTarget = true;

        [Header("Phase Timing")]
        [Tooltip("Idle wait once a target is first acquired.")]
        [SerializeField] private float detectionDelay = 0.6f;
        [Tooltip("발사 전 준비 — charge glow swells, aim-lock telegraph appears. Player has time to react.")]
        [SerializeField] private float preFireDuration = 0.7f;
        [Tooltip("발사 시작 — short discharge frame: muzzle flash, fire SFX, initial-wave projectiles.")]
        [SerializeField] private float fireStartDuration = 0.08f;
        [Tooltip("발사 중 — sustained firing window. Branching/Burst spawn the rest of their projectiles staggered through this window. Beam stays alive.")]
        [SerializeField] private float firingDuration = 0.45f;
        [Tooltip("발사 이후 — recovery wind-down before cooldown. No new projectiles spawn here.")]
        [SerializeField] private float postFireDuration = 0.25f;
        [Tooltip("Quiet time after PostFire before the next blast can begin.")]
        [SerializeField] private float cooldownDuration = 1.2f;
        [SerializeField] private float loseSightGrace = 0.5f;

        [Header("Hit Mask / Range")]
        [SerializeField] private float maxRange = 30f;
        [Tooltip("Layers the blast can hit. Branching tendrils stop at the first collider on this mask along their fire direction; nothing on this mask in range = bolt extends to maxRange in mid-air. Default + SlimeNode (6) + Environment (7).")]
        [SerializeField] private LayerMask bulletHitMask = (1 << 0) | (1 << 6) | (1 << 7);
        [Tooltip("CircleCast radius used by all three modes for slime hit detection. Bigger = more forgiving 'sweep' feel; match visual line width for strict detection.")]
        [SerializeField] private float hitRadius = 0.4f;

        [Header("Hazard")]
        [SerializeField] private float knockbackForce = 18f;
        [SerializeField] private float stunDuration = 0.5f;

        [Header("Branching Mode")]
        [SerializeField] private int tendrilCount = 7;
        [Tooltip("World-space horizontal jitter (±units) on the captured slime XY before computing the fire direction. Adds variety to the bolt fan.")]
        [SerializeField] private float targetRadius = 0f;
        [Tooltip("World-space upward bow added to the Bezier control points along the muzzle→target line. 0 = perfectly straight bolt directly at the target. Higher = bolt arches upward over the path.")]
        [SerializeField] private float bowHeight = 0f;
        [Tooltip("World-space perpendicular jitter (±units) on each Bezier control point, so the bolts don't all overlap on the same straight line.")]
        [SerializeField] private float controlPointJitter = 0.3f;
        [Tooltip("World-space distance per Bezier segment. Total segments per tendril = path-length / this, clamped to [6, 48].")]
        [SerializeField] private float tendrilSegmentLength = 0.6f;
        [Tooltip("Max perpendicular noise (units) along the curve, scaled by segment length.")]
        [SerializeField] private float tendrilJaggedness = 0.35f;
        [Tooltip("Projectile travel speed in world units per second. The tendril's tip advances along the muzzle→maxRange line at this rate, doing per-frame collision sweeps; it halts on the first slime/terrain it actually encounters at that moment. Higher = snappier bolt, lower = slower visible travel.")]
        [SerializeField] private float tendrilTravelSpeed = 60f;
        [Tooltip("Fallback fixed travel time (sec) for the tip to traverse the full maxRange. Used only when tendrilTravelSpeed <= 0.")]
        [SerializeField] private float tendrilDrawTime = 0.2f;
        [SerializeField] private float tendrilHoldTime = 0.18f;
        [SerializeField] private float tendrilFadeTime = 0.28f;
        [Tooltip("Core line width for tendrils.")]
        [SerializeField] private float tendrilCoreWidth = 0.12f;
        [Tooltip("Fraction of tendrils spawned at FireStart. The rest are staggered through Firing.")]
        [Range(0f, 1f)] [SerializeField] private float branchingFireStartFraction = 0.3f;
        [Tooltip("Chase spread (sec). At FireStart, the N tendrils are spread across the slime's recorded XY history — tendril 0 (fires first) uses the OLDEST direction in the window; tendril N-1 (fires last) uses the NEWEST. 0 = all tendrils share the same direction reference (lagOffset only).")]
        [SerializeField] private float trailDuration = 0.6f;
        [Tooltip("Base lag (sec) — even the NEWEST tendril uses the slime's position THIS many seconds before FireStart as its direction reference. 0 = newest tendril uses the FireStart position. Combined with trailDuration, the OLDEST tendril references (trailLagOffset + trailDuration) seconds ago.")]
        [SerializeField] private float trailLagOffset = 0f;
        [Tooltip("Sample interval for trail recording (sec). Effective trail samples = (trailDuration + trailLagOffset) / interval.")]
        [SerializeField] private float trailSampleInterval = 0.04f;

        [Header("Beam Mode")]
        [Tooltip("Time the beam takes to fade out at the end of its life.")]
        [SerializeField] private float beamFadeTime = 0.18f;
        [Tooltip("Core line width for the beam.")]
        [SerializeField] private float beamCoreWidth = 0.55f;
        [Tooltip("Beam tip travel speed in world units per second. The beam visibly grows from muzzle along the locked direction, doing per-frame collision sweeps; halts on the first slime/terrain it actually encounters at that moment. 0 = instant full-length beam (hitscan-style fallback).")]
        [SerializeField] private float beamTravelSpeed = 80f;

        [Header("Burst Mode (multi-beam fan)")]
        [Tooltip("Number of beam-flashes in the fan.")]
        [SerializeField] private int burstFlashCount = 6;
        [Tooltip("Total cone spread in degrees (split half above / half below the aim line).")]
        [SerializeField] private float burstSpreadDegrees = 32f;
        [Tooltip("Each flash's lifetime — long enough that the whole fan is visible at once.")]
        [SerializeField] private float burstFlashDuration = 0.7f;
        [SerializeField] private float burstFlashFadeTime = 0.25f;
        [Tooltip("Core line width for each burst flash. Thinner than the main Beam mode.")]
        [SerializeField] private float burstFlashCoreWidth = 0.22f;
        [Tooltip("Fraction of flashes fired at FireStart. Only used when burstFlashInterval == 0.")]
        [Range(0f, 1f)] [SerializeField] private float burstFireStartFraction = 1f;
        [Tooltip("Seconds between consecutive flash spawns. > 0 enables sequential 'one at a time' firing (overrides burstFireStartFraction). 0 = batch mode using fraction.")]
        [SerializeField] private float burstFlashInterval = 0f;
        [Tooltip("Burst flash tip travel speed in world units per second. Same projectile-style growth + collision sweep as Beam mode. 0 = instant full-length flash.")]
        [SerializeField] private float burstFlashTravelSpeed = 150f;

        [Header("Visual Colors")]
        [Tooltip("Color of the beam / tendril line. Use a near-white core for max visibility.")]
        [SerializeField] private Color coreColor = new Color(0.85f, 0.96f, 1f, 1f);
        [Tooltip("World Z offset applied to projectile lines so they render in front of 2.5D ground.")]
        [SerializeField] private float lineZOffset = -0.5f;

        [Header("Aim-Lock Telegraph (PreFire)")]
        [Tooltip("Show a thin telegraph line from muzzle through the locked aim during PreFire so the slime can react.")]
        [SerializeField] private bool showPreFireTelegraph = true;
        [SerializeField] private float telegraphWidth = 0.04f;
        [SerializeField] private Color telegraphColor = new Color(0.7f, 0.95f, 1f, 0.3f);

        [Header("Audio")]
        [SerializeField] private AudioClip detectSound;
        [Tooltip("발사 전 준비. Plays once when PreFire begins.")]
        [SerializeField] private AudioClip preFireSound;
        [Tooltip("발사 시작. Sharp punctuation at FireStart.")]
        [SerializeField] private AudioClip fireStartSound;
        [Tooltip("발사 이후. Optional residual sound at PostFire.")]
        [SerializeField] private AudioClip postFireSound;
        [Tooltip("Plays at each tendril/beam/burst-flash impact point on the ground. Branching = once per tendril; Beam = once per beam (first ground contact); Burst = once per flash.")]
        [SerializeField] private AudioClip groundImpactSound;
        [Range(0f, 1f)] [SerializeField] private float detectVolume = 0.6f;
        [Range(0f, 1f)] [SerializeField] private float preFireVolume = 0.7f;
        [Range(0f, 1f)] [SerializeField] private float fireStartVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float postFireVolume = 0.55f;
        [Range(0f, 1f)] [SerializeField] private float groundImpactVolume = 0.7f;
        [Tooltip("Random pitch jitter applied to each ground-impact sound (multiplier offset). 0 = no jitter.")]
        [Range(0f, 0.4f)] [SerializeField] private float groundImpactPitchJitter = 0.1f;
        [Tooltip("Branching mode: random pitch jitter per fireStartSound play (one per tendril). 0 = no jitter.")]
        [Range(0f, 0.4f)] [SerializeField] private float fireStartPitchJitter = 0.08f;

        [Header("VFX")]
        [Tooltip("Particle system that scales 0.05x → 1x during PreFire. Stopped at PostFire.")]
        [SerializeField] private ParticleSystem chargeGlow;
        [Tooltip("Particle burst at the muzzle on FireStart.")]
        [SerializeField] private ParticleSystem muzzleFlash;
        [Tooltip("Particle prefab spawned at each tendril/beam impact point, and at bullet impacts.")]
        [SerializeField] private ParticleSystem impactPrefab;
        [Tooltip("Enabled while a target is detected.")]
        [SerializeField] private GameObject detectionIndicator;

        // --- runtime FSM ---
        private State state = State.Idle;
        private float stateTimer;
        private float graceTimer;
        private bool indicatorOn;

        // Locked at FireStart so all phases use the same aim.
        private Vector3 lockedOrigin;
        private Vector2 lockedDir;

        // Branching: only the per-tendril LAG (in trail-buffer index units) is frozen
        // at FireStart. The actual direction reference is looked up from the LIVE
        // trail at each tendril's spawn moment, so still-pending tendrils continue
        // to chase the slime through the Firing window. Once a tendril has been
        // spawned (GraniteBlastTendril GameObject created), its bolt is baked.
        private struct PendingTendril
        {
            public int lagSamples;        // trail-index offset back from newest at spawn time
        }
        private readonly List<PendingTendril> pendingTendrils = new List<PendingTendril>(16);
        private int tendrilsSpawned;
        private int tendrilsAtFireStart;

        // Slime XY trail — purely world-space positions, oldest at index 0. Sampled
        // every Update at trailSampleInterval, trimmed to trailDuration. PlanBranching
        // takes a one-shot snapshot at FireStart.
        private readonly List<Vector2> slimeWorldTrail = new List<Vector2>(64);
        private float lastTrailSampleTime = -999f;

        // Burst scheduling: precomputed bullet directions, consumed across FireStart + Firing.
        private readonly List<Vector2> pendingBullets = new List<Vector2>(16);
        private int bulletsSpawned;
        private int bulletsAtFireStart;
        // Time the active burst window started (FireStart entry). Used by interval mode
        // to spawn one flash every burstFlashInterval seconds.
        private float burstStartTime;

        // PreFire telegraph line.
        private LineRenderer telegraphLR;

        // --- shared material for line projectiles ---
        private Material coreMatCached;

        // --- IHazardSource ---
        public bool IsActive => isActiveAndEnabled;

        public HazardEffect GetHazardEffect() => new HazardEffect
        {
            knockbackForce = knockbackForce,
            knockbackDirection = Vector2.zero,
            stunDuration = stunDuration,
            damageType = DamageType.Lightning,
        };

        // --- public access for spawned projectiles ---
        public LayerMask BulletHitMask => bulletHitMask;
        public float MaxRange => maxRange;
        public Color CoreColor => coreColor;
        public float LineZOffset => lineZOffset;
        public ParticleSystem ImpactPrefab => impactPrefab;
        public float HitRadius => Mathf.Max(0.01f, hitRadius);

        public Material GetCoreMaterial() => GetOrCreateMat(ref coreMatCached);

        // Called by tendril / beam / burst-flash on ground impact. Each spawned
        // projectile is responsible for invoking this once when its line first
        // reaches the ground (or completes its draw to a ground-snapped endpoint).
        public void PlayGroundImpactSound(Vector3 worldPos)
        {
            if (groundImpactSound == null) return;
            float pitch = 1f;
            if (groundImpactPitchJitter > 0f)
                pitch = 1f + Random.Range(-groundImpactPitchJitter, groundImpactPitchJitter);
            SoundService.PlayAt(groundImpactSound, worldPos, SoundCategory.SFX_Object, groundImpactVolume, pitch);
        }

        private Material GetOrCreateMat(ref Material slot)
        {
            if (slot != null) return slot;
            // Same setup as TurretHazard's bulletTracer — known to render visibly in this project.
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("UI/Default");
            slot = new Material(sh);
            slot.mainTexture = Texture2D.whiteTexture;
            return slot;
        }

        void Start()
        {
            if (target == null)
            {
                var slime = FindFirstObjectByType<SlimeController>(FindObjectsInactive.Exclude);
                if (slime != null) target = slime.transform;
            }
            ApplyIndicator(false);
            if (chargeGlow != null) StopChargeGlow();
            HideTelegraph();
        }

        void OnDisable()
        {
            ApplyIndicator(false);
            StopChargeGlow();
            HideTelegraph();
            state = State.Idle;
            graceTimer = 0f;
            pendingTendrils.Clear();
            pendingBullets.Clear();
            slimeWorldTrail.Clear();
        }

        // --- fire direction / origin ---

        public Vector3 AimOrigin() => muzzle != null ? muzzle.position : transform.position;

        public Vector2 AimDirection2D()
        {
            if (aimAtTarget && target != null)
            {
                Vector3 origin = AimOrigin();
                Vector2 d = new Vector2(target.position.x - origin.x, target.position.y - origin.y);
                if (d.sqrMagnitude > 0.0001f) return d.normalized;
            }
            Transform t = muzzle != null ? muzzle : transform;
            Vector3 r = t.right;
            Vector2 dr = new Vector2(r.x, r.y);
            return dr.sqrMagnitude < 0.0001f ? Vector2.right : dr.normalized;
        }

        // --- detection ---

        private bool IsTargetVisible()
        {
            if (target == null) return false;
            Vector3 origin = AimOrigin();
            Vector3 delta = target.position - origin;
            float distSqr = delta.sqrMagnitude;
            if (distSqr > detectionRange * detectionRange) return false;
            if (lineOfSightBlockers == 0) return true;

            float dist = Mathf.Sqrt(distSqr);
            Vector2 dir = new Vector2(delta.x, delta.y);
            if (dir.sqrMagnitude < 0.0001f) return true;
            dir.Normalize();

            var hit = RaycastExcludingSelf(origin, dir, dist, lineOfSightBlockers);
            if (hit.collider == null) return true;
            var slime = hit.collider.GetComponentInParent<SlimeController>();
            return slime != null && slime.transform == target;
        }

        public RaycastHit2D RaycastExcludingSelf(Vector2 origin, Vector2 dir, float maxDist, LayerMask mask)
        {
            // Sort by distance so we always return the *closest* valid solid hit —
            // Physics2D.RaycastAll's array order is not guaranteed.
            var hits = Physics2D.RaycastAll(origin, dir, maxDist, mask);
            RaycastHit2D best = default;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                // Skip triggers (InteractionSensor / Water / etc. on the bullet hit mask
                // would otherwise block the beam at the muzzle).
                if (h.collider.isTrigger) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                }
            }
            return best;
        }

        // Same exclusion logic as the line raycast above, but sweeps a circle of
        // `radius`, giving the swept tube some forgiveness — used for tendril
        // endpoint resolution so a slightly stale direction (slime moved a hair off
        // the line) still terminates on slime/terrain instead of overshooting to
        // maxRange in mid-air.
        // Triggers are skipped EXCEPT those belonging to a SlimeController: the bolt
        // must stop at the slime's body even when that body collider is configured
        // as a trigger (otherwise the line passes through and ends in mid-air past
        // the slime). Foreign triggers (water zones, interaction sensors, etc.)
        // remain pass-through.
        public RaycastHit2D CircleCastExcludingSelf(Vector2 origin, float radius, Vector2 dir, float maxDist, LayerMask mask)
        {
            var hits = Physics2D.CircleCastAll(origin, radius, dir, maxDist, mask);
            RaycastHit2D best = default;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.isTrigger
                    && h.collider.GetComponentInParent<SlimeController>() == null) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                }
            }
            return best;
        }

        // --- FSM ---

        void Update()
        {
            float dt = Time.deltaTime;

            SampleTrail();

            bool seen = IsTargetVisible();
            graceTimer = seen ? loseSightGrace : Mathf.Max(0f, graceTimer - dt);
            bool effectivelySeen = seen || graceTimer > 0f;

            switch (state)
            {
                case State.Idle:
                    if (effectivelySeen) EnterDetecting();
                    break;

                case State.Detecting:
                    if (!effectivelySeen) { EnterIdle(); break; }
                    stateTimer -= dt;
                    if (stateTimer <= 0f) EnterPreFire();
                    break;

                case State.PreFire:
                    UpdateChargeGlow(stateTimer, preFireDuration);
                    UpdateTelegraph();
                    stateTimer -= dt;
                    if (stateTimer <= 0f) EnterFireStart();
                    break;

                case State.FireStart:
                    UpdateChargeGlow(0f, preFireDuration); // hold full
                    // Burst interval mode releases flashes one-at-a-time across the
                    // FireStart + Firing window — drive its scheduler from FireStart too.
                    SpawnFiringWaveIfDue();
                    stateTimer -= dt;
                    if (stateTimer <= 0f) EnterFiring();
                    break;

                case State.Firing:
                    SpawnFiringWaveIfDue();
                    stateTimer -= dt;
                    if (stateTimer <= 0f) EnterPostFire();
                    break;

                case State.PostFire:
                    stateTimer -= dt;
                    if (stateTimer <= 0f) EnterCooldown();
                    break;

                case State.Cooldown:
                    stateTimer -= dt;
                    if (stateTimer <= 0f)
                    {
                        if (effectivelySeen) EnterPreFire();
                        else EnterIdle();
                    }
                    break;
            }
        }

        // --- transitions ---

        private void EnterIdle()
        {
            state = State.Idle;
            ApplyIndicator(false);
            StopChargeGlow();
            HideTelegraph();
            pendingTendrils.Clear();
            pendingBullets.Clear();
        }

        private void EnterDetecting()
        {
            state = State.Detecting;
            stateTimer = detectionDelay;
            ApplyIndicator(true);
            if (detectSound != null)
                SoundService.PlayAt(detectSound, AimOrigin(), SoundCategory.SFX_Object, detectVolume);
        }

        private void EnterPreFire()
        {
            state = State.PreFire;
            stateTimer = preFireDuration;
            ApplyIndicator(true);

            // Lock origin and direction at the start of PreFire so the telegraph line
            // matches the actual shot. (Even though spawning happens at FireStart.)
            lockedOrigin = AimOrigin();
            lockedDir = AimDirection2D();

            StartChargeGlow();
            ShowTelegraph();
            if (preFireSound != null)
                SoundService.PlayAt(preFireSound, lockedOrigin, SoundCategory.SFX_Object, preFireVolume);
        }

        private void EnterFireStart()
        {
            state = State.FireStart;
            stateTimer = fireStartDuration;
            HideTelegraph();

            if (muzzleFlash != null) muzzleFlash.Play(true);
            // Branching plays fireStartSound per-tendril (in SpawnTendrilsUpTo) instead of
            // one global hit at FireStart, so each lightning bolt has its own muzzle crack.
            if (fireStartSound != null && mode != FireMode.Branching)
                SoundService.PlayAt(fireStartSound, lockedOrigin, SoundCategory.SFX_Object, fireStartVolume);

            // Pre-plan muzzle parameters now; endpoints are resolved at spawn time
            // against the live trail so post-PreFire movement is followed.
            switch (mode)
            {
                case FireMode.Branching:
                    PlanBranching();
                    SpawnTendrilsUpTo(tendrilsAtFireStart);
                    break;
                case FireMode.Beam:
                    SpawnBeam();
                    break;
                case FireMode.Burst:
                    PlanBurst();
                    burstStartTime = Time.time;
                    if (burstFlashInterval > 0f)
                    {
                        // Interval mode: release just the first flash now; the rest are
                        // dispatched by SpawnFiringWaveIfDue based on elapsed time.
                        SpawnBulletsUpTo(1);
                    }
                    else
                    {
                        SpawnBulletsUpTo(bulletsAtFireStart);
                    }
                    break;
            }
        }

        private void EnterFiring()
        {
            state = State.Firing;
            stateTimer = firingDuration;
        }

        private void EnterPostFire()
        {
            state = State.PostFire;
            stateTimer = postFireDuration;
            // Make sure all planned spawns went out, even at low frame rates.
            if (mode == FireMode.Branching) SpawnTendrilsUpTo(pendingTendrils.Count);
            else if (mode == FireMode.Burst) SpawnBulletsUpTo(pendingBullets.Count);

            StopChargeGlow();
            if (postFireSound != null)
                SoundService.PlayAt(postFireSound, lockedOrigin, SoundCategory.SFX_Object, postFireVolume);
        }

        private void EnterCooldown()
        {
            state = State.Cooldown;
            stateTimer = cooldownDuration;
            pendingTendrils.Clear();
            pendingBullets.Clear();
        }

        private void ApplyIndicator(bool on)
        {
            if (indicatorOn == on) return;
            indicatorOn = on;
            if (detectionIndicator != null) detectionIndicator.SetActive(on);
        }

        // --- charge glow scaling (tied to PreFire progress) ---

        private Vector3 chargeGlowBaseScale = Vector3.one;
        private bool chargeGlowCaptured;

        private void StartChargeGlow()
        {
            if (chargeGlow == null) return;
            if (!chargeGlowCaptured)
            {
                chargeGlowBaseScale = chargeGlow.transform.localScale;
                chargeGlowCaptured = true;
            }
            chargeGlow.transform.localScale = chargeGlowBaseScale * 0.05f;
            if (!chargeGlow.gameObject.activeSelf) chargeGlow.gameObject.SetActive(true);
            chargeGlow.Play(true);
        }

        private void UpdateChargeGlow(float remaining, float total)
        {
            if (chargeGlow == null || !chargeGlowCaptured) return;
            float elapsed = Mathf.Max(0f, total - remaining);
            float t = total > 0f ? Mathf.Clamp01(elapsed / total) : 1f;
            float k = 1f - Mathf.Pow(1f - t, 2f); // ease-out
            chargeGlow.transform.localScale = chargeGlowBaseScale * Mathf.Lerp(0.05f, 1f, k);
        }

        private void StopChargeGlow()
        {
            if (chargeGlow == null) return;
            chargeGlow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (chargeGlowCaptured)
                chargeGlow.transform.localScale = chargeGlowBaseScale;
        }

        // --- PreFire telegraph (thin warning line from muzzle to slime — LIVE) ---

        private void ShowTelegraph()
        {
            if (!showPreFireTelegraph) return;
            EnsureTelegraph();
            if (telegraphLR == null) return;
            telegraphLR.enabled = true;
        }

        private void HideTelegraph()
        {
            if (telegraphLR != null) telegraphLR.enabled = false;
        }

        private void UpdateTelegraph()
        {
            if (telegraphLR == null || !telegraphLR.enabled) return;
            // Live origin + direction — telegraph chases the slime through PreFire
            // instead of locking at PreFire entry. No "FireStart locks position"
            // concept; the line just shows where the bolt will currently aim.
            Vector3 origin = AimOrigin();
            Vector2 dir = AimDirection2D();
            float reach = maxRange;
            if (bulletHitMask != 0)
            {
                var hit = RaycastExcludingSelf(origin, dir, maxRange, bulletHitMask);
                if (hit.collider != null) reach = hit.distance;
            }
            Vector3 a = new Vector3(origin.x, origin.y, origin.z + lineZOffset);
            Vector3 b = new Vector3(origin.x + dir.x * reach,
                                    origin.y + dir.y * reach,
                                    origin.z + lineZOffset);
            telegraphLR.SetPosition(0, a);
            telegraphLR.SetPosition(1, b);
        }

        private void EnsureTelegraph()
        {
            if (telegraphLR != null) return;
            var go = new GameObject("PreFireTelegraph_Auto");
            go.transform.SetParent(transform, false);
            telegraphLR = go.AddComponent<LineRenderer>();
            telegraphLR.useWorldSpace = true;
            telegraphLR.positionCount = 2;
            telegraphLR.widthMultiplier = telegraphWidth;
            telegraphLR.numCapVertices = 2;
            telegraphLR.alignment = LineAlignment.View;
            telegraphLR.textureMode = LineTextureMode.Stretch;
            telegraphLR.sortingOrder = 99;
            var sh = Shader.Find("Sprites/Default");
            var m = new Material(sh);
            m.mainTexture = Texture2D.whiteTexture;
            telegraphLR.material = m;
            telegraphLR.startColor = telegraphColor;
            telegraphLR.endColor = telegraphColor;
            telegraphLR.enabled = false;
        }

        // --- Branching: plan + spawn ---

        // ───────────────────────────────────────────────────────────────────
        // World-space targeting (rebuild). All math operates on world XY directly:
        // no atan2, no sin/cos, no muzzle-relative angles. P0/P1/P2/P3 are derived by
        // adding world axes (Vector2.up, Vector2.right) and lerping world midpoints.
        // ───────────────────────────────────────────────────────────────────

        private void SampleTrail()
        {
            if (target == null) return;
            float now = Time.time;
            float interval = Mathf.Max(0.001f, trailSampleInterval);
            if (now - lastTrailSampleTime < interval) return;
            lastTrailSampleTime = now;

            slimeWorldTrail.Add(new Vector2(target.position.x, target.position.y));

            // Cap to (trailDuration + trailLagOffset) so even the oldest tendril
            // (lagFrac=1) at the largest lagOffset still has a sample available.
            float maxBackSec = Mathf.Max(0f, trailDuration) + Mathf.Max(0f, trailLagOffset);
            int maxSamples = Mathf.CeilToInt(maxBackSec / interval) + 2;
            while (slimeWorldTrail.Count > maxSamples) slimeWorldTrail.RemoveAt(0);
        }

        private void PlanBranching()
        {
            pendingTendrils.Clear();
            tendrilsSpawned = 0;

            int n = Mathf.Max(1, tendrilCount);
            tendrilsAtFireStart = Mathf.Clamp(Mathf.RoundToInt(n * branchingFireStartFraction), 1, n);

            // Snapshot the slime's world-XY trail. Tendril i (0..N-1) maps onto a
            // sample from oldest (i=0) to newest (i=N-1). All work is plain index math
            // on a list of world Vector2 — no time interpolation, no angles. After this
            // function returns, no further tracking happens; slime movement cannot
            // shift any pending tendril.
            // Convert seconds → buffer-index counts using the fixed sample interval.
            //   lagOffsetSamples = how many indices back from newest even the latest tendril references
            //   spreadSamples    = additional spread distributed across tendrils for chase
            float interval = Mathf.Max(0.001f, trailSampleInterval);
            int lagOffsetSamples = Mathf.RoundToInt(Mathf.Max(0f, trailLagOffset) / interval);
            int spreadSamples = Mathf.RoundToInt(Mathf.Max(0f, trailDuration) / interval);

            for (int i = 0; i < n; i++)
            {
                // Tendril i: lagFrac=1 (i=0, fires first) → lag = lagOffset + duration.
                //            lagFrac=0 (i=N-1, fires last) → lag = lagOffset only.
                float lagFrac = n == 1 ? 0f : 1f - (float)i / (n - 1);
                int lagSamples = lagOffsetSamples + Mathf.RoundToInt(lagFrac * spreadSamples);
                pendingTendrils.Add(new PendingTendril { lagSamples = lagSamples });
            }
        }

        private void SpawnTendrilsUpTo(int targetCount)
        {
            int t = Mathf.Min(targetCount, pendingTendrils.Count);
            float segLen = Mathf.Max(0.1f, tendrilSegmentLength);
            float bow = Mathf.Max(0f, bowHeight);

            while (tendrilsSpawned < t)
            {
                var pt = pendingTendrils[tendrilsSpawned++];

                // LIVE muzzle position — re-read every spawn so a moving emitter (or
                // an emitter rotated to track) keeps the bolts coming out of the
                // current barrel position, not a frozen PreFire snapshot.
                Vector3 muzzleOrigin = AimOrigin();
                float muzzleZ = muzzleOrigin.z;
                Vector2 muzzle2D = new Vector2(muzzleOrigin.x, muzzleOrigin.y);

                // Per-tendril fire SFX so each bolt has its own muzzle crack.
                if (fireStartSound != null)
                {
                    float pitch = 1f;
                    if (fireStartPitchJitter > 0f)
                        pitch = 1f + Random.Range(-fireStartPitchJitter, fireStartPitchJitter);
                    SoundService.PlayAt(fireStartSound, muzzleOrigin, SoundCategory.SFX_Object, fireStartVolume, pitch);
                }

                // LIVE trail lookup at this tendril's spawn moment — pending tendrils
                // continue to track the slime through the Firing window. Only THIS
                // tendril's direction is computed now; future tendrils will resample
                // when they spawn.
                int bufLen = slimeWorldTrail.Count;
                Vector2 slimePos;
                if (bufLen > 0)
                {
                    int idx = Mathf.Clamp((bufLen - 1) - pt.lagSamples, 0, bufLen - 1);
                    slimePos = slimeWorldTrail[idx];
                }
                else
                {
                    slimePos = target != null
                        ? new Vector2(target.position.x, target.position.y)
                        : muzzle2D;
                }
                slimePos.x += Random.Range(-targetRadius, targetRadius);

                // Direction-only reference. The bolt's full-length path is laid out
                // to maxRange and the Tendril itself does per-frame projectile-style
                // collision sweeps at its tip — if at the moment the tip reaches a
                // position there's a slime/terrain collider there, the bolt halts;
                // if not, it continues. So a slime that was on the line at fire time
                // but moved away by the time the tip arrives no longer halts the bolt.
                Vector2 delta = slimePos - muzzle2D;
                float dlen = delta.magnitude;
                Vector2 dir = dlen > 0.0001f ? delta / dlen : Vector2.right;
                Vector2 worldTarget = muzzle2D + dir * maxRange;

                // World-space bezier control points — pure XY arithmetic on the
                // muzzle→target straight line. No upward bias unless bowHeight > 0.
                Vector3 p0 = muzzleOrigin;
                Vector3 p3 = new Vector3(worldTarget.x, worldTarget.y, muzzleZ);

                Vector3 line33 = Vector3.Lerp(p0, p3, 0.33f);
                Vector3 line67 = Vector3.Lerp(p0, p3, 0.67f);
                float j1x = Random.Range(-controlPointJitter, controlPointJitter);
                float j1y = Random.Range(-controlPointJitter, controlPointJitter);
                float j2x = Random.Range(-controlPointJitter, controlPointJitter);
                float j2y = Random.Range(-controlPointJitter, controlPointJitter);
                Vector3 p1 = new Vector3(line33.x + j1x, line33.y + bow + j1y, muzzleZ);
                Vector3 p2 = new Vector3(line67.x + j2x, line67.y + bow + j2y, muzzleZ);

                // Segment count uses the bezier control polygon length (upper bound on
                // arc length) — denser segments for longer bolts.
                float controlPolyLen = Vector3.Distance(p0, p1)
                                     + Vector3.Distance(p1, p2)
                                     + Vector3.Distance(p2, p3);
                int segments = Mathf.Clamp(Mathf.RoundToInt(controlPolyLen / segLen), 6, 48);

                // Travel time uses the straight-line chord (muzzle → maxRange endpoint)
                // so the tip's world-space speed along the line of fire matches
                // `tendrilTravelSpeed` exactly, regardless of bow/jitter on the curve.
                float chordLen = Vector3.Distance(p0, p3);
                float drawTime = tendrilTravelSpeed > 0f
                    ? Mathf.Max(0.001f, chordLen / tendrilTravelSpeed)
                    : tendrilDrawTime;

                SpawnTendril(p0, p1, p2, p3,
                    segments: segments,
                    jaggedness: tendrilJaggedness,
                    coreWidth: tendrilCoreWidth,
                    drawTime: drawTime,
                    holdTime: tendrilHoldTime,
                    fadeTime: tendrilFadeTime,
                    canHit: true);
            }
        }

        private void SpawnFiringWaveIfDue()
        {
            switch (mode)
            {
                case FireMode.Branching:
                {
                    int total = pendingTendrils.Count;
                    if (total <= 0) return;
                    // Branching staggers across the Firing window only — FireStart already
                    // dispatched its share. Skip while still in FireStart.
                    if (state != State.Firing) return;
                    float prog = firingDuration > 0f
                        ? Mathf.Clamp01(1f - (stateTimer / firingDuration))
                        : 1f;
                    int desired = tendrilsAtFireStart
                                  + Mathf.RoundToInt((total - tendrilsAtFireStart) * prog);
                    SpawnTendrilsUpTo(desired);
                    break;
                }
                case FireMode.Burst:
                {
                    int total = pendingBullets.Count;
                    if (total <= 0) return;
                    int desired;
                    if (burstFlashInterval > 0f)
                    {
                        // Interval mode: 1 flash every burstFlashInterval seconds since FireStart.
                        // Spans both FireStart and Firing.
                        float elapsed = Time.time - burstStartTime;
                        desired = 1 + Mathf.FloorToInt(elapsed / burstFlashInterval);
                    }
                    else
                    {
                        // Batch mode: bulletsAtFireStart at FireStart, rest staggered through Firing.
                        if (state != State.Firing) return;
                        float prog = firingDuration > 0f
                            ? Mathf.Clamp01(1f - (stateTimer / firingDuration))
                            : 1f;
                        desired = bulletsAtFireStart
                                  + Mathf.RoundToInt((total - bulletsAtFireStart) * prog);
                    }
                    SpawnBulletsUpTo(desired);
                    break;
                }
            }
        }

        private void SpawnTendril(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                                  int segments, float jaggedness, float coreWidth,
                                  float drawTime, float holdTime, float fadeTime, bool canHit)
        {
            var go = new GameObject("GraniteBlast_Tendril");
            go.transform.position = p0;
            var t = go.AddComponent<GraniteBlastTendril>();
            t.Init(this, p0, p1, p2, p3,
                segments: segments, jaggedness: jaggedness, coreWidth: coreWidth,
                drawTime: drawTime, holdTime: holdTime, fadeTime: fadeTime, canHit: canHit);
        }

        // --- Beam ---

        private void SpawnBeam()
        {
            // Beam lives across all three firing phases so it stays committed.
            float beamLife = fireStartDuration + firingDuration + postFireDuration;
            var go = new GameObject("GraniteBlast_Beam");
            go.transform.position = lockedOrigin;
            var beam = go.AddComponent<GraniteBlastBeam>();
            beam.Init(this, muzzle != null ? muzzle : transform, lockedDir,
                duration: beamLife,
                fadeTime: beamFadeTime,
                coreWidth: beamCoreWidth,
                travelSpeed: beamTravelSpeed);
        }

        // --- Burst (multi-beam fan) ---
        // Each "burst flash" is a short-lived GraniteBlastBeam — same visual as Beam mode,
        // just thinner, briefer, and many at once across the spread cone.

        private void PlanBurst()
        {
            pendingBullets.Clear();
            bulletsSpawned = 0;
            int n = Mathf.Max(1, burstFlashCount);
            bulletsAtFireStart = Mathf.Clamp(Mathf.RoundToInt(n * burstFireStartFraction), 1, n);

            float halfSpread = burstSpreadDegrees * 0.5f;
            float baseAngle = Mathf.Atan2(lockedDir.y, lockedDir.x);
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : (float)i / (n - 1);
                float angleDeg = Mathf.Lerp(-halfSpread, halfSpread, t);
                float jitter = Random.Range(-2f, 2f);
                float a = baseAngle + (angleDeg + jitter) * Mathf.Deg2Rad;
                pendingBullets.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)));
            }
        }

        private void SpawnBulletsUpTo(int targetCount)
        {
            int target = Mathf.Min(targetCount, pendingBullets.Count);
            while (bulletsSpawned < target)
            {
                Vector2 v = pendingBullets[bulletsSpawned++];
                var go = new GameObject("GraniteBlast_BurstFlash");
                go.transform.position = lockedOrigin;
                var beam = go.AddComponent<GraniteBlastBeam>();
                beam.Init(this, muzzle != null ? muzzle : transform, v,
                    duration: burstFlashDuration,
                    fadeTime: burstFlashFadeTime,
                    coreWidth: burstFlashCoreWidth,
                    travelSpeed: burstFlashTravelSpeed);
            }
        }

        // --- Gizmos ---

        void OnDrawGizmosSelected()
        {
            Vector3 origin = AimOrigin();
            Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.35f);
            Gizmos.DrawWireSphere(origin, detectionRange);

            Vector2 d = AimDirection2D();
            Vector3 d3 = new Vector3(d.x, d.y, 0f);
            Gizmos.color = new Color(0.7f, 0.95f, 1f, 0.9f);
            Gizmos.DrawLine(origin, origin + d3 * Mathf.Min(maxRange, detectionRange));

            // Hit-volume preview: a swept "tube" from muzzle along the aim direction
            // shows the actual CircleCast radius used by all three modes.
            float r = Mathf.Max(0.01f, hitRadius);
            float previewLen = Mathf.Min(maxRange, detectionRange);
            Vector3 perp = new Vector3(-d.y, d.x, 0f) * r;
            Vector3 endP = origin + d3 * previewLen;
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.5f);
            Gizmos.DrawWireSphere(origin, r);
            Gizmos.DrawWireSphere(endP, r);
            Gizmos.DrawLine(origin + perp, endP + perp);
            Gizmos.DrawLine(origin - perp, endP - perp);

            if (mode == FireMode.Branching && target != null)
            {
                Gizmos.color = new Color(1f, 0.7f, 0.3f, 0.5f);
                Gizmos.DrawWireSphere(target.position, targetRadius);
            }
        }
    }
}
