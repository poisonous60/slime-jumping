using UnityEngine;
using UnityEngine.Serialization;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Energy cannon. FSM: Idle → Detecting → PreFire → FireStart → Firing → PostFire → Cooldown. Core partial; mode code lives in the Branching/Beam/Burst/Mortar siblings.</summary>
    public partial class GraniteBlastEmitter : MonoBehaviour
    {
        public enum FireMode { Branching, Beam, Burst, Mortar }
        public enum State { Idle, Detecting, PreFire, FireStart, Firing, PostFire, Cooldown }
        // InitialLagThenLive — initial dir from the lagged trail, then tracks the LIVE slime.
        // ContinuousLag — rotation target is always the lagged trail position.
        public enum BeamTrackMode { InitialLagThenLive, ContinuousLag }

        [Header("Emitter Presets (per mode — each one overwrites the inspector blocks below as a set)")]
        [Tooltip("Preset applied when the active FireMode resolves to Branching (either via the static `mode` field or via distance-based selection). Same SO type as the Beam/Burst slots — pick a different SO per mode to give each mode its own Phase Timing / Audio / Feedback feel. Leave null to fall back to the inspector's inline values.")]
        [FormerlySerializedAs("preset")]
        [SerializeField] private GraniteBlastEmitterPreset branchingPreset;
        [Tooltip("Preset applied when the active FireMode resolves to Beam.")]
        [SerializeField] private GraniteBlastEmitterPreset beamPreset;
        [Tooltip("Preset applied when the active FireMode resolves to Burst.")]
        [SerializeField] private GraniteBlastEmitterPreset burstPreset;
        [Tooltip("Preset applied when the active FireMode resolves to Mortar.")]
        [SerializeField] private GraniteBlastEmitterPreset mortarPreset;
        // Per-slot 'last applied' caches: OnValidate re-applies only the slot whose
        // reference changed, so local field edits survive.
        [FormerlySerializedAs("_lastAppliedPreset")]
        [SerializeField, HideInInspector] private GraniteBlastEmitterPreset _lastAppliedBranching;
        [SerializeField, HideInInspector] private GraniteBlastEmitterPreset _lastAppliedBeam;
        [SerializeField, HideInInspector] private GraniteBlastEmitterPreset _lastAppliedBurst;
        [SerializeField, HideInInspector] private GraniteBlastEmitterPreset _lastAppliedMortar;

        [Header("Firing Mode Control")]
        [Tooltip("When true (default), the emitter auto-detects targets and fires on its own. When false, the emitter stays Idle until external code calls FireOnce() — used for scripted/triggered shots (e.g., GraniteBlastTrigger volumes).")]
        [SerializeField] private bool autoFire = true;

        [Header("Mode")]
        [SerializeField] private FireMode mode = FireMode.Branching;

        [Header("Distance-Based Mode")]
        [Tooltip("When true, the firing mode is re-selected at PreFire entry based on the slime's distance from the muzzle, using the three (close / mid / far) mode picks below. The static 'Mode' field above is used as the fallback when the target is missing.")]
        [SerializeField] private bool distanceBasedMode = false;
        [Tooltip("Distance threshold (m) — slime within this range fires Close mode.")]
        [SerializeField] private float closeRangeDistance = 5f;
        [Tooltip("Distance threshold (m) — slime within this range (and outside Close) fires Mid mode. Beyond this fires Far mode. Should be ≥ closeRangeDistance and ≤ detectionRange.")]
        [SerializeField] private float midRangeDistance = 12f;
        [Tooltip("Mode used when the slime is closer than closeRangeDistance.")]
        [SerializeField] private FireMode closeMode = FireMode.Burst;
        [Tooltip("Mode used when the slime sits between closeRangeDistance and midRangeDistance.")]
        [SerializeField] private FireMode midMode = FireMode.Beam;
        [Tooltip("Mode used when the slime is farther than midRangeDistance (still inside detectionRange).")]
        [SerializeField] private FireMode farMode = FireMode.Branching;
        [Tooltip("When true, crossing into Close range while in Cooldown immediately ends the cooldown so the next shot can fire without waiting.")]
        [SerializeField] private bool resetCooltimeOnEnterCloseRange = false;
        [Tooltip("When true, crossing into Mid range while in Cooldown immediately ends the cooldown so the next shot can fire without waiting.")]
        [SerializeField] private bool resetCooltimeOnEnterMidRange = false;

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

        [Header("Hit Feedback")]
        [Tooltip("Time.timeScale freeze duration (realtime sec) on first slime hit per burst. 0 = disabled. HitStopService is auto-vetoed during charge.")]
        [SerializeField] private float hitStopDuration = 0.06f;
        [Tooltip("Time.timeScale during the freeze. 0 = full freeze, 0.05 = 95% slow-mo.")]
        [Range(0f, 1f)] [SerializeField] private float hitStopFrozenScale = 0.0f;
        [Tooltip("Cinemachine impulse source emitted on slime hit. Leave null to skip.")]
        [SerializeField] private SlimeJumping.Effects.ImpulseEmitter hitShakeEmitter;
        [Tooltip("Impulse force multiplier for camera shake on slime hit.")]
        [SerializeField] private float hitShakeForce = 1.0f;

        [Header("Stun (optional)")]
        [Tooltip("Stun duration (sec) applied on slime hit. 0 = disabled (default — stun-less push channel). When > 0, the hit calls ApplyHazardEffect (legacy combined-channel path) instead of ApplyHazardPush.")]
        [SerializeField] private float stunOnHitDuration = 0f;
        [Tooltip("DamageType tag attached to the stun (visual / audio differentiation only — no gameplay effect).")]
        [SerializeField] private SlimeJumping.Interfaces.DamageType stunDamageType = SlimeJumping.Interfaces.DamageType.Lightning;

        [Header("Mode Configs (ScriptableObject)")]
        [Tooltip("Branching mode tuning. Required for Branching shots — leave assigned to a GraniteBlastBranchingConfig asset (Create → SlimeJumping → Granite Blast → Branching Config).")]
        [SerializeField] private GraniteBlastBranchingConfig branchingConfig;
        [Tooltip("Beam mode tuning. Required for Beam shots — leave assigned to a GraniteBlastBeamConfig asset.")]
        [SerializeField] private GraniteBlastBeamConfig beamConfig;
        [Tooltip("Burst mode tuning. Required for Burst shots — leave assigned to a GraniteBlastBurstConfig asset.")]
        [SerializeField] private GraniteBlastBurstConfig burstConfig;
        [Tooltip("Mortar mode tuning. Required for Mortar shots — leave assigned to a GraniteBlastMortarConfig asset.")]
        [SerializeField] private GraniteBlastMortarConfig mortarConfig;

        [Header("Branching Scene Refs")]
        [Tooltip("Optional A* navigation graph. When set, Branching tendrils route through navGraph.FindPath(muzzle, slime) ONLY IF a direct line from muzzle to slime is blocked by walls (navWallMask). Scene reference — kept on the emitter (cannot live in a ScriptableObject asset).")]
        [SerializeField] private GraniteBlastNavGraph navGraph;

        // --- config proxies: keep the original field names for the partial files.
        // Fall back to code defaults when no config asset is assigned.
        private LayerMask navWallMask => branchingConfig != null ? branchingConfig.navWallMask : (LayerMask)(1 << 7);
        private float navPathCostJitter => branchingConfig != null ? branchingConfig.navPathCostJitter : 0.35f;
        private float navEntryJitter => branchingConfig != null ? branchingConfig.navEntryJitter : 0.3f;
        private float tendrilPathNoiseAmplitude => branchingConfig != null ? branchingConfig.tendrilPathNoiseAmplitude : 0.5f;
        private float tendrilPathNoiseFrequency => branchingConfig != null ? branchingConfig.tendrilPathNoiseFrequency : 0.8f;
        private int tendrilCount => branchingConfig != null ? branchingConfig.tendrilCount : 7;
        private float targetRadius => branchingConfig != null ? branchingConfig.targetRadius : 0f;
        private float bowHeight => branchingConfig != null ? branchingConfig.bowHeight : 0f;
        private float bowDepthZ => branchingConfig != null ? branchingConfig.bowDepthZ : 0f;
        private float controlPointJitter => branchingConfig != null ? branchingConfig.controlPointJitter : 0.3f;
        private float controlPointZJitter => branchingConfig != null ? branchingConfig.controlPointZJitter : 0f;
        private float tendrilSegmentLength => branchingConfig != null ? branchingConfig.tendrilSegmentLength : 0.6f;
        private float tendrilJaggedness => branchingConfig != null ? branchingConfig.tendrilJaggedness : 0.35f;
        private float tendrilTravelSpeed => branchingConfig != null ? branchingConfig.tendrilTravelSpeed : 60f;
        private float tendrilDrawTime => branchingConfig != null ? branchingConfig.tendrilDrawTime : 0.2f;
        private float tendrilHoldTime => branchingConfig != null ? branchingConfig.tendrilHoldTime : 0.18f;
        private float tendrilFadeTime => branchingConfig != null ? branchingConfig.tendrilFadeTime : 0.28f;
        private float tendrilCoreWidth => branchingConfig != null ? branchingConfig.tendrilCoreWidth : 0.12f;
        private float branchingFireStartFraction => branchingConfig != null ? branchingConfig.branchingFireStartFraction : 0.3f;
        private float trailDuration => branchingConfig != null ? branchingConfig.trailDuration : 0.6f;
        private float trailLagOffset => branchingConfig != null ? branchingConfig.trailLagOffset : 0f;
        private float trailSampleInterval => branchingConfig != null ? branchingConfig.trailSampleInterval : 0.04f;

        private float beamFadeTime => beamConfig != null ? beamConfig.beamFadeTime : 0.18f;
        private float beamCoreWidth => beamConfig != null ? beamConfig.beamCoreWidth : 0.55f;
        private float beamTravelSpeed => beamConfig != null ? beamConfig.beamTravelSpeed : 80f;
        private float beamTrackAngularSpeed => beamConfig != null ? beamConfig.beamTrackAngularSpeed : 90f;
        private float beamTrackLag => beamConfig != null ? beamConfig.beamTrackLag : 0.25f;
        private BeamTrackMode beamTrackMode => beamConfig != null ? beamConfig.beamTrackMode : BeamTrackMode.InitialLagThenLive;
        private float beamGroundedSlideScale => beamConfig != null ? beamConfig.beamGroundedSlideScale : 0.3f;
        private bool beamOverwriteSlide => beamConfig != null ? beamConfig.beamOverwriteSlide : true;
        private float beamOverwritePinnedSpeed => beamConfig != null ? beamConfig.beamOverwritePinnedSpeed : 14f;

        private int burstFlashCount => burstConfig != null ? burstConfig.burstFlashCount : 6;
        private float burstSpreadDegrees => burstConfig != null ? burstConfig.burstSpreadDegrees : 32f;
        private float burstFlashDuration => burstConfig != null ? burstConfig.burstFlashDuration : 0.7f;
        private float burstFlashFadeTime => burstConfig != null ? burstConfig.burstFlashFadeTime : 0.25f;
        private float burstFlashCoreWidth => burstConfig != null ? burstConfig.burstFlashCoreWidth : 0.22f;
        private float burstFireStartFraction => burstConfig != null ? burstConfig.burstFireStartFraction : 1f;
        private float burstFlashInterval => burstConfig != null ? burstConfig.burstFlashInterval : 0f;
        private float burstFlashTravelSpeed => burstConfig != null ? burstConfig.burstFlashTravelSpeed : 150f;

        public GraniteBlastMortarConfig MortarConfig => mortarConfig;

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
        [SerializeField] private SlimeJumping.Audio.SoundDef detectSound;
        [Tooltip("발사 전 준비. Plays once when PreFire begins.")]
        [SerializeField] private SlimeJumping.Audio.SoundDef preFireSound;
        [Tooltip("발사 시작. Sharp punctuation at FireStart.")]
        [SerializeField] private SlimeJumping.Audio.SoundDef fireStartSound;
        [Tooltip("발사 이후. Optional residual sound at PostFire.")]
        [SerializeField] private SlimeJumping.Audio.SoundDef postFireSound;
        [Tooltip("Plays at each tendril/beam/burst-flash impact point on the ground. Branching = once per tendril; Beam = once per beam (first ground contact); Burst = once per flash.")]
        [SerializeField] private SlimeJumping.Audio.SoundDef groundImpactSound;

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

        [Header("Snow Impact")]
        [Tooltip("When true, ground hits on a SnowField host collider carve a dent (same channel the slime uses on landing). Off-snow hits are unaffected.")]
        [SerializeField] private bool snowCarveOnImpact = true;
        [Tooltip("Snow dent depth (world units) per ground hit. Mirrors the slime-landing carve scale; the slime's hard landing is ~0.20.")]
        [SerializeField] private float snowCarveDepth = 0.18f;
        [Tooltip("Snow dent radius (world units) per ground hit.")]
        [SerializeField] private float snowCarveRadius = 0.6f;

        // --- runtime FSM ---
        private State state = State.Idle;
        private float stateTimer;
        private float graceTimer;
        private bool indicatorOn;

        // -1 = unknown, 0 = Close, 1 = Mid, 2 = Far.
        private int lastRangeZone = -1;

        // Locked at PreFire entry so all phases use the same aim.
        private Vector3 lockedOrigin;
        private Vector2 lockedDir;

        private Vector3 chargeGlowBaseScale = Vector3.one;
        private bool chargeGlowCaptured;

        // Per-burst feedback gate: Branching fires many tendrils per burst, each would
        // otherwise trigger its own freeze + camera impulse.
        private int feedbackBurstId;
        private int lastFeedbackFiredBurstId = -1;

        // Only the Beam is tracked so Cancel() can kill it mid-fire; tendrils / burst
        // flashes are left to finish their natural lifetime by design.
        private GraniteBlastBeam activeBeam;
        internal void RegisterActiveBeam(GraniteBlastBeam beam) { activeBeam = beam; }

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
        public float NavPathCostJitter => navPathCostJitter;
        public float NavEntryJitter => navEntryJitter;
        public float TendrilPathNoiseAmplitude => tendrilPathNoiseAmplitude;
        public float TendrilPathNoiseFrequency => tendrilPathNoiseFrequency;
        public State CurrentState => state;
        public bool IsBusy => state != State.Idle && state != State.Cooldown;
        public Transform Target => target;
        public float BeamTrackAngularSpeed => beamTrackAngularSpeed;
        public float BeamTrackLag => beamTrackLag;
        public BeamTrackMode BeamTrackingMode => beamTrackMode;
        public float BeamGroundedSlideScale => beamGroundedSlideScale;
        public bool BeamOverwriteSlide => beamOverwriteSlide;
        public float BeamOverwritePinnedSpeed => beamOverwritePinnedSpeed;
        public float StunOnHitDuration => stunOnHitDuration;
        public SlimeJumping.Interfaces.DamageType StunDamageType => stunDamageType;

        // Routes the hit through the combined stun channel when stun is configured;
        // false = caller should fall back to its own ApplyHazardPush.
        public bool TryApplyStunOnHit(SlimeController slime, Vector2 pushDir, float impulseMagnitude, Vector2 hitPoint)
        {
            if (stunOnHitDuration <= 0f || slime == null) return false;
            // hitPoint overload: the 1-arg ApplyHazardEffect never fires OnHazardPushed, so
            // the burn-mark / hit-flash visuals would be skipped.
            slime.ApplyHazardEffect(new SlimeJumping.Interfaces.HazardEffect
            {
                knockbackForce = impulseMagnitude,
                knockbackDirection = pushDir,
                stunDuration = stunOnHitDuration,
                damageType = stunDamageType,
            }, hitPoint);
            return true;
        }

        // One HitStop + shake per burst — ungated, same-burst hits stack Cinemachine
        // impulses and keep re-extending the Time.timeScale dip.
        public void TryFireHitStopFeedback()
        {
            if (lastFeedbackFiredBurstId == feedbackBurstId) return;
            lastFeedbackFiredBurstId = feedbackBurstId;
            if (hitStopDuration <= 0f && hitShakeEmitter == null) return;
            SlimeJumping.Effects.HitStopService.Freeze(
                hitStopDuration, hitShakeEmitter, hitShakeForce, hitStopFrozenScale);
        }

        void Start()
        {
            // Detection-phase values must be sane before the first shot; EnterPreFire
            // re-applies once the actual fire mode is locked.
            ApplyPreset(GetPresetForMode(mode));

            if (target == null)
            {
                var slime = FindFirstObjectByType<SlimeController>(FindObjectsInactive.Exclude);
                if (slime != null) target = slime.transform;
            }
            ApplyIndicator(false);
            if (chargeGlow != null) StopChargeGlow();
            HideTelegraph();
        }

#if UNITY_EDITOR
        // Applies only the slot whose reference changed, so local field edits survive.
        // Multiple slots reassigned at once: inspector shows the last one applied.
        void OnValidate()
        {
            if (branchingPreset != _lastAppliedBranching)
            {
                _lastAppliedBranching = branchingPreset;
                if (branchingPreset != null) ApplyPreset(branchingPreset);
            }
            if (beamPreset != _lastAppliedBeam)
            {
                _lastAppliedBeam = beamPreset;
                if (beamPreset != null) ApplyPreset(beamPreset);
            }
            if (burstPreset != _lastAppliedBurst)
            {
                _lastAppliedBurst = burstPreset;
                if (burstPreset != null) ApplyPreset(burstPreset);
            }
            if (mortarPreset != _lastAppliedMortar)
            {
                _lastAppliedMortar = mortarPreset;
                if (mortarPreset != null) ApplyPreset(mortarPreset);
            }
        }
#endif

        private GraniteBlastEmitterPreset GetPresetForMode(FireMode m)
        {
            switch (m)
            {
                case FireMode.Branching: return branchingPreset;
                case FireMode.Beam: return beamPreset;
                case FireMode.Burst: return burstPreset;
                case FireMode.Mortar: return mortarPreset;
            }
            return null;
        }

        // Field-by-field copy rather than reflection — debuggable and rename-safe.
        private void ApplyPreset(GraniteBlastEmitterPreset p)
        {
            if (p == null) return;
            detectionRange = p.detectionRange;
            lineOfSightBlockers = p.lineOfSightBlockers;

            detectionDelay = p.detectionDelay;
            preFireDuration = p.preFireDuration;
            fireStartDuration = p.fireStartDuration;
            firingDuration = p.firingDuration;
            postFireDuration = p.postFireDuration;
            cooldownDuration = p.cooldownDuration;
            loseSightGrace = p.loseSightGrace;

            maxRange = p.maxRange;
            bulletHitMask = p.bulletHitMask;
            hitRadius = p.hitRadius;

            knockbackImpulse = p.knockbackImpulse;
            knockbackAcceleration = p.knockbackAcceleration;
            sustainedHit = p.sustainedHit;

            hitStopDuration = p.hitStopDuration;
            hitStopFrozenScale = p.hitStopFrozenScale;
            hitShakeForce = p.hitShakeForce;

            stunOnHitDuration = p.stunOnHitDuration;
            stunDamageType = p.stunDamageType;

            coreColor = p.coreColor;
            lineZOffset = p.lineZOffset;

            showPreFireTelegraph = p.showPreFireTelegraph;
            telegraphWidth = p.telegraphWidth;
            telegraphColor = p.telegraphColor;

            detectSound = p.detectSound;
            preFireSound = p.preFireSound;
            fireStartSound = p.fireStartSound;
            postFireSound = p.postFireSound;
            groundImpactSound = p.groundImpactSound;

            impactPrefab = p.impactPrefab;
            groundExplosionPrefab = p.groundExplosionPrefab;

            snowCarveOnImpact = p.snowCarveOnImpact;
            snowCarveDepth = p.snowCarveDepth;
            snowCarveRadius = p.snowCarveRadius;
        }

        void OnDisable()
        {
            ApplyIndicator(false);
            StopChargeGlow();
            HideTelegraph();
            state = State.Idle;
            graceTimer = 0f;
            lastRangeZone = -1;
            pendingTendrils.Clear();
            pendingBullets.Clear();
            slimeWorldTrail.Clear();
        }

        // External trigger entry: skips detection, straight to PreFire. Rejected while
        // already firing so chained triggers can't double-fire.
        public bool FireOnce()
        {
            if (state != State.Idle && state != State.Cooldown) return false;
            EnterPreFire();
            return true;
        }

        // External abort. Kills the active Beam; in-flight tendrils / burst flashes
        // keep their natural lifetime.
        public void Cancel()
        {
            HideTelegraph();
            StopChargeGlow();
            // StopChargeGlow only stops emission; on a cancel the lingering particles
            // read as "still charging", so wipe them too.
            if (chargeGlow != null) chargeGlow.Clear(true);
            if (muzzleFlash != null)
            {
                muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                muzzleFlash.Clear(true);
            }
            if (activeBeam != null)
            {
                Destroy(activeBeam.gameObject);
                activeBeam = null;
            }
            EnterIdle();
            // Halt Update, else the emitter re-enters Detecting while the slime is still
            // in range and the charge glow we just cleared respawns within detectionDelay.
            enabled = false;
        }

        // --- FSM ---

        void Update()
        {
            float dt = Time.deltaTime;

            SampleTrail();

            bool seen = autoFire && IsTargetVisible();
            graceTimer = seen ? loseSightGrace : Mathf.Max(0f, graceTimer - dt);
            bool effectivelySeen = seen || graceTimer > 0f;

            UpdateRangeZoneAndMaybeSkipCooldown();

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
                    // Burst interval mode spans FireStart + Firing, so drive it here too.
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
            mortarsSpawned = 0;
        }

        private void EnterDetecting()
        {
            state = State.Detecting;
            stateTimer = detectionDelay;
            ApplyIndicator(true);
            if (detectSound != null)
                SoundService.Play(detectSound, AimOrigin());
        }

        private void EnterPreFire()
        {
            state = State.PreFire;
            stateTimer = preFireDuration;
            ApplyIndicator(true);

            // Re-select before locking aim, so telegraph and all later phases agree on the mode.
            if (distanceBasedMode) mode = SelectModeByDistance();

            // Detection values applied here take effect on the NEXT detection cycle.
            ApplyPreset(GetPresetForMode(mode));

            // Lock aim at PreFire so the telegraph matches the shot spawned at FireStart.
            lockedOrigin = AimOrigin();
            lockedDir = AimDirection2D();

            StartChargeGlow();
            ShowTelegraph();
            if (preFireSound != null)
                SoundService.Play(preFireSound, lockedOrigin);
        }

        private void EnterFireStart()
        {
            state = State.FireStart;
            stateTimer = fireStartDuration;
            feedbackBurstId++;
            HideTelegraph();

            if (muzzleFlash != null) muzzleFlash.Play(true);
            // Branching/Mortar play fireStartSound per projectile inside their spawn loops.
            if (fireStartSound != null && mode != FireMode.Branching && mode != FireMode.Mortar)
                SoundService.Play(fireStartSound, lockedOrigin);

            // Endpoints resolve at spawn time against the live trail, so movement after
            // PreFire is still followed.
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
                        // Interval mode: first flash now, rest dispatched by SpawnFiringWaveIfDue.
                        SpawnBulletsUpTo(1);
                    }
                    else
                    {
                        SpawnBulletsUpTo(bulletsAtFireStart);
                    }
                    break;
                case FireMode.Mortar:
                    PlanMortar();
                    SpawnNextMortarIfDue();
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
            // Mortar deliberately does NOT flush — spawning stops when Firing ends so the
            // trailing projectiles still arrive inside the same fire window.

            StopChargeGlow();
            if (postFireSound != null)
                SoundService.Play(postFireSound, lockedOrigin);
        }

        private void EnterCooldown()
        {
            state = State.Cooldown;
            stateTimer = cooldownDuration;
            pendingTendrils.Clear();
            pendingBullets.Clear();
            mortarsSpawned = 0;
        }

        // On entering Close/Mid, optionally zeroes the Cooldown timer. Distance-based
        // emitters only.
        private void UpdateRangeZoneAndMaybeSkipCooldown()
        {
            if (!distanceBasedMode || target == null)
            {
                lastRangeZone = -1;
                return;
            }

            float dist = Vector3.Distance(target.position, AimOrigin());
            int zone = dist <= closeRangeDistance ? 0
                     : dist <= midRangeDistance ? 1
                     : 2;

            if (zone != lastRangeZone)
            {
                if (state == State.Cooldown)
                {
                    if (zone == 0 && resetCooltimeOnEnterCloseRange) stateTimer = 0f;
                    else if (zone == 1 && resetCooltimeOnEnterMidRange) stateTimer = 0f;
                }
                lastRangeZone = zone;
            }
        }

        // Falls back to the static `mode` with no target, keeping FireOnce() deterministic.
        private FireMode SelectModeByDistance()
        {
            if (target == null) return mode;
            float dist = Vector3.Distance(target.position, AimOrigin());
            if (dist <= closeRangeDistance) return closeMode;
            if (dist <= midRangeDistance) return midMode;
            return farMode;
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
                    // Branching staggers over the Firing window only; FireStart sent its share.
                    if (state != State.Firing) return;
                    float prog = firingDuration > 0f
                        ? Mathf.Clamp01(1f - (stateTimer / firingDuration))
                        : 1f;
                    int desired = tendrilsAtFireStart
                                  + Mathf.RoundToInt((total - tendrilsAtFireStart) * prog);
                    SpawnTendrilsUpTo(desired);
                    break;
                }
                case FireMode.Mortar:
                {
                    SpawnNextMortarIfDue();
                    return;
                }
                case FireMode.Burst:
                {
                    int total = pendingBullets.Count;
                    if (total <= 0) return;
                    int desired;
                    if (burstFlashInterval > 0f)
                    {
                        // Interval mode: 1 flash per burstFlashInterval since FireStart, across both phases.
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
