using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Granite Blast (그라니테 블라스트) — energy cannon with three configurable firing modes.
    /// Branching: lightning tendrils that arc out and rain down on the ground around the target.
    /// Beam: a sustained thick laser drawn from muzzle to first blocker.
    /// Burst: a cone of fast bullet projectiles.
    /// FSM: Idle → Detecting → PreFire (준비) → FireStart (시작) → Firing (중) → PostFire (이후) → Cooldown.
    /// Hits push the slime via SlimeController.AddExternalVelocity (no stun, no input lockout).
    ///
    /// This is the Core partial: serialized fields, FSM dispatch, transitions, public API.
    /// Mode-specific code lives in sibling partial files (Branching/Beam/Burst), and the
    /// support helpers (Targeting/Telegraph/Impact) live next to them.
    /// </summary>
    public partial class GraniteBlastEmitter : MonoBehaviour
    {
        public enum FireMode { Branching, Beam, Burst }
        public enum State { Idle, Detecting, PreFire, FireStart, Firing, PostFire, Cooldown }

        [Header("Firing Mode Control")]
        [Tooltip("When true (default), the emitter auto-detects targets and fires on its own. When false, the emitter stays Idle until external code calls FireOnce() — used for scripted/triggered shots (e.g., GraniteBlastTrigger volumes).")]
        [SerializeField] private bool autoFire = true;

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

        [Header("Knockback")]
        [Tooltip("One-shot KB applied per discrete bolt hit (Branching tendril, single-shot Beam, Burst flash). Units: m/s — added to slime velocity once.")]
        [SerializeField] private float knockbackImpulse = 18f;
        [Tooltip("Sustained KB applied every frame the slime stays inside a sustained Beam. Units: m/s² — multiplied by Time.deltaTime per push.")]
        [SerializeField] private float knockbackAcceleration = 60f;
        [Tooltip("Beam mode only. When true, the beam keeps re-sweeping each frame and pushes whatever slime sits in the path; the slime is not stunned and can keep playing — but cannot easily walk into the beam. False = legacy single-hit halt behavior (each beam halts on the first slime, applies impulse once).")]
        [SerializeField] private bool sustainedHit = false;

        [Header("Branching Mode")]
        [Tooltip("Optional A* navigation graph. When set, Branching tendrils route through navGraph.FindPath(muzzle, slime) ONLY IF a direct line from muzzle to slime is blocked by walls (navWallMask). Direct LOS clear → bezier direct fire. Behind a wall → A* through navGraph. No clean entry/exit nodes → bezier fallback. Leave null to disable graph routing entirely.")]
        [SerializeField] private GraniteBlastNavGraph navGraph;
        [Tooltip("Layers that count as walls for NavGraph LOS gating. Used by IsDirectPathClear (direct shortcut) and ClosestVisibleNode (entry/exit selection). Should NOT include the slime layer — slime in path is fine, walls aren't. Default = Environment (layer 7). 0 disables LOS gating (NavGraph reverts to legacy Euclidean nearest-node selection).")]
        [SerializeField] private LayerMask navWallMask = (1 << 7);
        [SerializeField] private int tendrilCount = 7;
        [Tooltip("World-space horizontal jitter (±units) on the captured slime XY before computing the fire direction. Adds variety to the bolt fan.")]
        [SerializeField] private float targetRadius = 0f;
        [Tooltip("World-space upward bow added to the Bezier control points along the muzzle→target line. 0 = perfectly straight bolt directly at the target. Higher = bolt arches upward over the path.")]
        [SerializeField] private float bowHeight = 0f;
        [Tooltip("World-space Z bow added to the Bezier control points (depth). 0 = bolt stays in the muzzle's Z plane. >0 pushes the midpoint forward (toward camera with default URP setup), <0 pushes it back. Combined with bowHeight gives the bolt a 3D arc.")]
        [SerializeField] private float bowDepthZ = 0f;
        [Tooltip("World-space perpendicular jitter (±units) on each Bezier control point, so the bolts don't all overlap on the same straight line.")]
        [SerializeField] private float controlPointJitter = 0.3f;
        [Tooltip("World-space Z jitter (±units) on each Bezier control point. Independent from controlPointJitter — adds depth wobble per tendril so a fan of bolts doesn't all sit in the same plane. 0 = all bolts share the muzzle's Z plane (legacy 2D behavior).")]
        [SerializeField] private float controlPointZJitter = 0f;
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
        [Tooltip("Particle prefab spawned at each tendril/beam impact point, and at bullet impacts. Plays for both slime hits and ground hits — a small spark per contact.")]
        [SerializeField] private ParticleSystem impactPrefab;
        [Tooltip("Optional larger explosion VFX spawned only on GROUND hits (not slime hits). Use a chunkier, brighter prefab — visual flair, no AoE damage.")]
        [SerializeField] private ParticleSystem groundExplosionPrefab;
        [Tooltip("Enabled while a target is detected.")]
        [SerializeField] private GameObject detectionIndicator;

        // --- runtime FSM ---
        private State state = State.Idle;
        private float stateTimer;
        private float graceTimer;
        private bool indicatorOn;

        // Locked at PreFire entry so all phases use the same aim.
        private Vector3 lockedOrigin;
        private Vector2 lockedDir;

        // Charge-glow runtime — base scale captured on first Play. Lives here (Core)
        // because charge-glow drive is shared across modes.
        private Vector3 chargeGlowBaseScale = Vector3.one;
        private bool chargeGlowCaptured;

        // --- public access for spawned projectiles ---
        public LayerMask BulletHitMask => bulletHitMask;
        public float MaxRange => maxRange;
        public Color CoreColor => coreColor;
        public float LineZOffset => lineZOffset;
        public ParticleSystem ImpactPrefab => impactPrefab;
        public ParticleSystem GroundExplosionPrefab => groundExplosionPrefab;
        public float HitRadius => Mathf.Max(0.01f, hitRadius);
        public float KnockbackImpulse => knockbackImpulse;
        public float KnockbackAcceleration => knockbackAcceleration;
        public bool SustainedHit => sustainedHit;
        public LayerMask NavWallMask => navWallMask;
        public State CurrentState => state;
        public bool IsBusy => state != State.Idle && state != State.Cooldown;

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

        // External trigger entry point. Skips auto-detection and goes straight into
        // PreFire (so the configured telegraph still plays). Ignored if the emitter
        // is already firing — prevents trigger double-fires from chained re-entries.
        // Returns true on accepted, false if rejected.
        public bool FireOnce()
        {
            if (state != State.Idle && state != State.Cooldown) return false;
            EnterPreFire();
            return true;
        }

        // --- FSM ---

        void Update()
        {
            float dt = Time.deltaTime;

            SampleTrail();

            bool seen = autoFire && IsTargetVisible();
            graceTimer = seen ? loseSightGrace : Mathf.Max(0f, graceTimer - dt);
            bool effectivelySeen = seen || graceTimer > 0f;

            switch (state)
            {
                case State.Idle:
                    if (autoFire && effectivelySeen) EnterDetecting();
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
                        if (autoFire && effectivelySeen) EnterPreFire();
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
    }
}
