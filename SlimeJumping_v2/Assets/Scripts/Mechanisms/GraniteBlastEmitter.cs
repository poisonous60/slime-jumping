using UnityEngine;
using UnityEngine.Serialization;
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
        // Beam tracking strategies after spawn:
        //   InitialLagThenLive — initial dir snapshots the lagged trail position, then
        //                        deg/sec rotation tracks the LIVE slime (no lag).
        //   ContinuousLag      — every frame the rotation target is the lagged trail
        //                        position (slime's pos N seconds ago).
        public enum BeamTrackMode { InitialLagThenLive, ContinuousLag }

        [Header("Emitter Presets (per mode — each one overwrites the inspector blocks below as a set)")]
        [Tooltip("Preset applied when the active FireMode resolves to Branching (either via the static `mode` field or via distance-based selection). Same SO type as the Beam/Burst slots — pick a different SO per mode to give each mode its own Phase Timing / Audio / Feedback feel. Leave null to fall back to the inspector's inline values.")]
        [FormerlySerializedAs("preset")]
        [SerializeField] private GraniteBlastEmitterPreset branchingPreset;
        [Tooltip("Preset applied when the active FireMode resolves to Beam.")]
        [SerializeField] private GraniteBlastEmitterPreset beamPreset;
        [Tooltip("Preset applied when the active FireMode resolves to Burst.")]
        [SerializeField] private GraniteBlastEmitterPreset burstPreset;
        // Per-slot 'last applied' caches so OnValidate detects which slot's
        // reference changed and re-applies just that one — local field edits
        // (with the same preset still attached) survive untouched.
        [FormerlySerializedAs("_lastAppliedPreset")]
        [SerializeField, HideInInspector] private GraniteBlastEmitterPreset _lastAppliedBranching;
        [SerializeField, HideInInspector] private GraniteBlastEmitterPreset _lastAppliedBeam;
        [SerializeField, HideInInspector] private GraniteBlastEmitterPreset _lastAppliedBurst;

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

        [Header("Branching Scene Refs")]
        [Tooltip("Optional A* navigation graph. When set, Branching tendrils route through navGraph.FindPath(muzzle, slime) ONLY IF a direct line from muzzle to slime is blocked by walls (navWallMask). Scene reference — kept on the emitter (cannot live in a ScriptableObject asset).")]
        [SerializeField] private GraniteBlastNavGraph navGraph;

        // --- proxy accessors: keep original field names so partial files
        // (Branching/Beam/Burst/Targeting/Impact) read them unchanged. Each one
        // pulls from the assigned config asset; if no config is assigned the
        // proxy returns the original code default so a missing reference does
        // not produce a NullReferenceException at runtime.
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

        // Range zone tracking for the cooltime-skip toggles. -1 = unknown (first sample
        // after enable), 0 = Close, 1 = Mid, 2 = Far. Updated each Update; on Close/Mid
        // entry we optionally zero the Cooldown timer.
        private int lastRangeZone = -1;

        // Locked at PreFire entry so all phases use the same aim.
        private Vector3 lockedOrigin;
        private Vector2 lockedDir;

        // Charge-glow runtime — base scale captured on first Play. Lives here (Core)
        // because charge-glow drive is shared across modes.
        private Vector3 chargeGlowBaseScale = Vector3.one;
        private bool chargeGlowCaptured;

        // Per-burst feedback gate. Branching mode fires up to tendrilCount
        // tendrils per burst; without gating each one would fire its own
        // freeze + camera impulse. Increment at FireStart, consume once.
        private int feedbackBurstId;
        private int lastFeedbackFiredBurstId = -1;

        // Active Beam GameObject tracker — set by SpawnBeam, destroyed by Cancel.
        // Lets external code (GraniteBlastEmitterAttackable) kill the in-flight laser
        // mid-fire. Tendrils/burst flashes are NOT tracked here — they're allowed to
        // finish their natural lifetime per design.
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

        // Routes a slime hit through ApplyHazardEffect (combined-channel, legacy
        // stun path) when stun is configured, else returns false so the caller
        // falls back to its own ApplyHazardPush logic.
        public bool TryApplyStunOnHit(SlimeController slime, Vector2 pushDir, float impulseMagnitude, Vector2 hitPoint)
        {
            if (stunOnHitDuration <= 0f || slime == null) return false;
            // Routed through the (effect, hitPoint) overload so OnHazardPushed fires
            // with the contact point — burn-mark + hit-flash visuals trigger the same
            // way they do on the stun-less push channel.
            slime.ApplyHazardEffect(new SlimeJumping.Interfaces.HazardEffect
            {
                knockbackForce = impulseMagnitude,
                knockbackDirection = pushDir,
                stunDuration = stunOnHitDuration,
                damageType = stunDamageType,
            }, hitPoint);
            return true;
        }

        // One-time hit feedback (HitStop + camera shake) per burst. Branching
        // mode fires up to tendrilCount tendrils per burst — gating here
        // prevents Cinemachine impulse stacking and Time.timeScale dip
        // re-extension on every same-burst hit.
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
            // Apply the preset matching the currently-configured static `mode`
            // so detection-phase values (detectionDelay / loseSightGrace / etc.)
            // are sane before the first shot. EnterPreFire re-applies once the
            // actual fire mode is locked (relevant for distance-based selection).
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
        // Edit-time auto-apply per slot: only when *that slot's* reference
        // changes, so local field edits (with the same preset still attached)
        // survive. If multiple slots are reassigned in the same validate pass
        // they're applied in order — the inspector ends up showing the
        // last-applied slot's values, which is fine since each shot re-applies
        // the right one at runtime anyway.
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
        }
#endif

        private GraniteBlastEmitterPreset GetPresetForMode(FireMode m)
        {
            switch (m)
            {
                case FireMode.Branching: return branchingPreset;
                case FireMode.Beam: return beamPreset;
                case FireMode.Burst: return burstPreset;
            }
            return null;
        }

        // Copies every preset field onto the emitter's serialized field of the
        // same name. No-op when source is null. Field-by-field (not reflection)
        // so it stays trivially debuggable and rename-safe.
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
            detectVolume = p.detectVolume;
            preFireVolume = p.preFireVolume;
            fireStartVolume = p.fireStartVolume;
            postFireVolume = p.postFireVolume;
            groundImpactVolume = p.groundImpactVolume;
            groundImpactPitchJitter = p.groundImpactPitchJitter;
            fireStartPitchJitter = p.fireStartPitchJitter;

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

        // External shutdown entry point. Aborts the firing FSM and destroys the
        // currently active Beam GameObject (if any). In-flight tendrils / burst
        // flashes are NOT touched — they keep their natural lifetime.
        // Used by GraniteBlastEmitterAttackable when the slime contacts the emitter.
        public void Cancel()
        {
            HideTelegraph();
            StopChargeGlow();
            // StopChargeGlow uses StopEmitting so live particles play out their
            // remaining lifetime — fine for normal PostFire, but on a hostile
            // cancel the lingering charge glow looks like the blast is still
            // winding up. Wipe the in-flight particles too.
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
            // Halt the FSM Update so the emitter cannot re-enter Detecting → PreFire
            // while the slime is still inside detectionRange. Without this, the
            // charge glow we just cleared would re-spawn within detectionDelay.
            // OnDisable also runs StopChargeGlow + state reset as a safety net.
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

            // Re-select mode by current distance before locking origin/direction so
            // the telegraph (and all downstream FSM phases) commit to the chosen mode.
            if (distanceBasedMode) mode = SelectModeByDistance();

            // Apply the per-mode emitter preset now that the firing mode is
            // locked — Phase Timing / Hit / Knockback / Audio / Visual / etc.
            // all swap to whatever the chosen mode's preset specifies. Detection
            // values applied here also take effect for the NEXT detection cycle.
            ApplyPreset(GetPresetForMode(mode));

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
            feedbackBurstId++;
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

        // Tracks which range zone (Close/Mid/Far) the target currently sits in and,
        // when the zone transitions into Close or Mid, optionally zeroes the
        // Cooldown timer so the next shot can fire immediately. Only the
        // distance-based mode setup uses these toggles; static-mode emitters
        // ignore the zone change.
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

        // Distance-based mode pick. Falls back to the inspector-static `mode` when
        // the target is missing — keeps FireOnce() trigger shots deterministic.
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
