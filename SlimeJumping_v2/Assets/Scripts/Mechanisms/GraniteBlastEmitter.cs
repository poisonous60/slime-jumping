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
        [Tooltip("Layers the blast can hit. Default + SlimeNode (6) + Environment (7).")]
        [SerializeField] private LayerMask bulletHitMask = (1 << 0) | (1 << 6) | (1 << 7);
        [Tooltip("Layers used as 'ground' when snapping Branching tendril endpoints. If 0, falls back to bulletHitMask.")]
        [SerializeField] private LayerMask groundSnapMask = 0;
        [Tooltip("CircleCast radius used by all three modes for slime hit detection. Bigger = more forgiving 'sweep' feel; match visual line width for strict detection.")]
        [SerializeField] private float hitRadius = 0.4f;

        [Header("Hazard")]
        [SerializeField] private float knockbackForce = 18f;
        [SerializeField] private float stunDuration = 0.5f;

        [Header("Branching Mode")]
        [SerializeField] private int tendrilCount = 7;
        [Tooltip("Lateral spread of impact points along the slime's surface tangent. Each tendril picks a random offset in [-targetRadius, +targetRadius].")]
        [SerializeField] private float targetRadius = 3.2f;
        [Tooltip("How far sideways each tendril is launched (degrees off the aim axis at the muzzle). Higher = wider initial fan.")]
        [SerializeField] private float launchSpreadDegrees = 75f;
        [Tooltip("Initial launch distance from the muzzle along the lateral kick — sets Bezier P1, controlling the curve's outward kick.")]
        [SerializeField] private float tendrilLaunchDistance = 2.5f;
        [Tooltip("Distance the curve bends upward above the target before diving. Bigger = taller arc.")]
        [SerializeField] private float bendHeight = 3.5f;
        [Tooltip("World-space distance per Bezier segment. Total segments per tendril = path-length / this, clamped to [6, 48]. Lets long-range tendrils stay smooth and short ones stay cheap.")]
        [SerializeField] private float tendrilSegmentLength = 0.6f;
        [Tooltip("Max perpendicular noise (units) along the curve, scaled by segment length.")]
        [SerializeField] private float tendrilJaggedness = 0.35f;
        [SerializeField] private float tendrilDrawTime = 0.2f;
        [SerializeField] private float tendrilHoldTime = 0.18f;
        [SerializeField] private float tendrilFadeTime = 0.28f;
        [Tooltip("Core line width for tendrils.")]
        [SerializeField] private float tendrilCoreWidth = 0.12f;
        [Tooltip("Fraction of tendrils spawned at FireStart. The rest are staggered through Firing.")]
        [Range(0f, 1f)] [SerializeField] private float branchingFireStartFraction = 0.3f;

        [Header("Beam Mode")]
        [Tooltip("Time the beam takes to fade out at the end of its life.")]
        [SerializeField] private float beamFadeTime = 0.18f;
        [Tooltip("Core line width for the beam.")]
        [SerializeField] private float beamCoreWidth = 0.55f;

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
        [Range(0f, 1f)] [SerializeField] private float detectVolume = 0.6f;
        [Range(0f, 1f)] [SerializeField] private float preFireVolume = 0.7f;
        [Range(0f, 1f)] [SerializeField] private float fireStartVolume = 1f;
        [Range(0f, 1f)] [SerializeField] private float postFireVolume = 0.55f;

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

        // Branching scheduling: precomputed control points + segment count, consumed
        // across FireStart + Firing.
        private struct PendingTendril
        {
            public Vector3 p0, p1, p2, p3;
            public int segments;
        }
        private readonly List<PendingTendril> pendingTendrils = new List<PendingTendril>(16);
        private int tendrilsSpawned;
        private int tendrilsAtFireStart;

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

        // --- FSM ---

        void Update()
        {
            float dt = Time.deltaTime;

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
            if (fireStartSound != null)
                SoundService.PlayAt(fireStartSound, lockedOrigin, SoundCategory.SFX_Object, fireStartVolume);

            // Pre-plan all spawns now, then dispatch the FireStart wave.
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

        // --- PreFire telegraph (thin warning line from muzzle along locked aim) ---

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
            float reach = maxRange;
            if (bulletHitMask != 0)
            {
                var hit = RaycastExcludingSelf(lockedOrigin, lockedDir, maxRange, bulletHitMask);
                if (hit.collider != null) reach = hit.distance;
            }
            Vector3 a = new Vector3(lockedOrigin.x, lockedOrigin.y, lockedOrigin.z + lineZOffset);
            Vector3 b = new Vector3(lockedOrigin.x + lockedDir.x * reach,
                                    lockedOrigin.y + lockedDir.y * reach,
                                    lockedOrigin.z + lineZOffset);
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

        private void PlanBranching()
        {
            pendingTendrils.Clear();
            tendrilsSpawned = 0;

            int n = Mathf.Max(1, tendrilCount);
            tendrilsAtFireStart = Mathf.Clamp(Mathf.RoundToInt(n * branchingFireStartFraction), 1, n);

            // Each tendril emits straight from the muzzle along its own curved path.
            // No central trunk / burst point — the lateral launch fans them out from P0.
            //   P0 = muzzle origin
            //   P1 = origin + lateral kick (random angle off aim axis)
            //   P2 = endpoint + upward bend (arches above target then dives)
            //   P3 = ground-snapped point near target
            // Segment count is derived per-tendril from the path length so far-away
            // tendrils stay smooth and close-range ones stay cheap.
            Vector3 origin = lockedOrigin;
            Vector2 origin2D = new Vector2(origin.x, origin.y);
            float baseAngle = Mathf.Atan2(lockedDir.y, lockedDir.x);
            float segLen = Mathf.Max(0.1f, tendrilSegmentLength);
            float launchDist = Mathf.Max(0.5f, tendrilLaunchDistance);

            for (int i = 0; i < n; i++)
            {
                Vector2 endpoint2D = SampleGroundEndpoint();
                Vector3 endpoint = new Vector3(endpoint2D.x, endpoint2D.y, origin.z);

                float lateralRad = Random.Range(-launchSpreadDegrees, launchSpreadDegrees) * Mathf.Deg2Rad;
                float launchAngle = baseAngle + lateralRad;
                Vector2 p1 = origin2D + new Vector2(Mathf.Cos(launchAngle), Mathf.Sin(launchAngle)) * launchDist;

                Vector2 p2 = endpoint2D + Vector2.up * Mathf.Max(0f, bendHeight);

                // Control polygon length is an upper bound on the actual Bezier arc —
                // good enough to drive segment density.
                float pathLen = (p1 - origin2D).magnitude
                              + (p2 - p1).magnitude
                              + (endpoint2D - p2).magnitude;
                int segments = Mathf.Clamp(Mathf.RoundToInt(pathLen / segLen), 6, 48);

                pendingTendrils.Add(new PendingTendril
                {
                    p0 = origin,
                    p1 = new Vector3(p1.x, p1.y, origin.z),
                    p2 = new Vector3(p2.x, p2.y, origin.z),
                    p3 = endpoint,
                    segments = segments,
                });
            }
        }

        private Vector2 SampleGroundEndpoint()
        {
            if (target == null)
            {
                // No target — fire along the locked aim into the air. maxRange is a
                // sane fallback distance.
                Vector2 d = lockedDir;
                return new Vector2(lockedOrigin.x, lockedOrigin.y) + d * Mathf.Min(maxRange, 12f);
            }
            Vector2 t2D = new Vector2(target.position.x, target.position.y);
            float lateral = Random.Range(-targetRadius, targetRadius);
            Vector2 sampleX = new Vector2(t2D.x + lateral, t2D.y);

            // Probe from well above the target so we cross the ground even on
            // tall platforms or while the slime is airborne.
            float probeUp = 12f;
            float probeDown = probeUp + 24f;
            Vector2 probeOrigin = new Vector2(sampleX.x, sampleX.y + probeUp);
            LayerMask snapMask = groundSnapMask != 0 ? groundSnapMask : bulletHitMask;

            // RaycastAll + skip the slime's own colliders and our own colliders so
            // the snap can't land on the target itself or on the muzzle.
            var hits = Physics2D.RaycastAll(probeOrigin, Vector2.down, probeDown, snapMask);
            for (int i = 0; i < hits.Length; i++)
            {
                var col = hits[i].collider;
                if (col == null) continue;
                if (col.transform.IsChildOf(target)) continue;
                if (col.transform.IsChildOf(transform)) continue;
                return hits[i].point;
            }

            // Fallback: drop a fixed offset below the target so endpoints still
            // visibly fall toward the floor even if no collider was found.
            return new Vector2(sampleX.x, sampleX.y - 2.5f);
        }

        private void SpawnTendrilsUpTo(int targetCount)
        {
            int target = Mathf.Min(targetCount, pendingTendrils.Count);
            while (tendrilsSpawned < target)
            {
                var t = pendingTendrils[tendrilsSpawned++];
                SpawnTendril(t.p0, t.p1, t.p2, t.p3,
                    segments: t.segments,
                    jaggedness: tendrilJaggedness,
                    coreWidth: tendrilCoreWidth,
                    drawTime: tendrilDrawTime,
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
                coreWidth: beamCoreWidth);
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
                    coreWidth: burstFlashCoreWidth);
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
