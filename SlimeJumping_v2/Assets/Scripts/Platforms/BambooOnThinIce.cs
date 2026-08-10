using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Physics;
using SlimeJumping.Platforms.ThinIceV2;

namespace SlimeJumping.Platforms
{
    /// Couples a BambooPlatformV3 stalk to the ThinIcePlatformV2 it stands on: each
    /// landing on the stalk advances the ice's cracks by 1/jumpsToShatter; on Shattered
    /// the stalk is swapped for a physics "corpse" clone that topples, and restored on regen.
    /// Zero-touch: public surfaces + reflection (EnterShattered / timer) + CheckSpringThrow's slime-arm guard.
    /// Attach to the ICE, never the bamboo (it deactivates on collapse → polling stops).
    public class BambooOnThinIce : MonoBehaviour
    {
        [Tooltip("The stalk standing on the ice. Landings on THIS stalk crack the ice below it.")]
        [SerializeField] BambooPlatformV3 bamboo;

        [Tooltip("The ice the stalk stands on. Null = ThinIcePlatformV2 on this GameObject.")]
        [SerializeField] ThinIcePlatformV2 ice;

        [Tooltip("Optional override of the shared BambooOnThinIceConfig (corpse fall tuning). " +
                 "Leave null to auto-load Assets/Resources/BambooOnThinIceConfig.asset.")]
        [SerializeField] BambooOnThinIceConfig configOverride;

        static readonly int CrackProgressId = Shader.PropertyToID("_CrackProgress");
        static readonly int BendAngleId = Shader.PropertyToID("_BendAngleDeg");
        static readonly int BambooLengthId = Shader.PropertyToID("_BambooLength");

        // World-space distance between dust puffs along the pole centerline at poof.
        const float PoofSpacing = 0.45f;

        // Reflection into ThinIcePlatformV2.EnterShattered(bool) — the one non-public
        // touch point; logs loudly if renamed instead of silently doing nothing.
        static MethodInfo enterShatteredMethod;
        static bool enterShatteredSearched;

        // Reflection into ThinIcePlatformV2.timer: merges both damage paths into one pool
        // — else a direct touch restarts the timer and regresses stalk-earned cracks.
        static FieldInfo timerField;
        static bool timerFieldSearched;

        int landingCount;
        float offStalkSeconds = float.MaxValue;
        bool collapsed;
        float collapsedAt;   // Time.time of the topple — drives the respawn timer
        ThinIcePlatformV2.IceState prevIceState = ThinIcePlatformV2.IceState.Idle;
        GameObject corpse;
        Rigidbody2D corpseRb;
        Collider2D[] corpseColliders;
        float corpseRestSeconds;
        Collider2D zoneCollider;   // ice's ThinIceRespawnZone shape; null = orphan (corpse never poofs)
        Renderer iceRenderer;
        MaterialPropertyBlock mpb;
        SlimeController cachedSlime;
        SlimeController hookedSlime;
        BambooOnThinIceConfig config;

        void Awake()
        {
            if (ice == null) ice = GetComponent<ThinIcePlatformV2>();
            if (bamboo == null)
            {
                Debug.LogError("[BambooOnThinIce] No bamboo assigned — link disabled.", this);
                enabled = false;
                return;
            }
            if (bamboo.gameObject == gameObject)
            {
                Debug.LogError("[BambooOnThinIce] Must NOT sit on the bamboo GameObject itself " +
                               "(it gets deactivated on collapse and the corpse clone would copy this " +
                               "component). Attach it to the ice instead.", this);
                enabled = false;
                return;
            }
            if (ice == null)
            {
                Debug.LogError("[BambooOnThinIce] No ice assigned and none on this GameObject — link disabled.", this);
                enabled = false;
                return;
            }

            iceRenderer = ice.GetComponentInChildren<Renderer>();
            mpb = new MaterialPropertyBlock();

            // Fallback instance so a config-less project needs no null guard on every read.
            config = BambooOnThinIceConfig.Resolve(configOverride);
            if (config == null) config = ScriptableObject.CreateInstance<BambooOnThinIceConfig>();

            var zone = ice.GetComponentInParent<ThinIceRespawnZone>();
            zoneCollider = zone != null ? zone.Shape : null;
        }

        void FixedUpdate()
        {
            // Cracking-entry sync: a direct touch starts a fresh timer, so fast-forward it
            // to the damage stalk landings already earned (one shared pool).
            var st = ice.State;
            if (st != prevIceState)
            {
                Debug.Log($"[BambooOnThinIce {name}] ice {prevIceState} -> {st} " +
                          $"(landings {landingCount}/{config.jumpsToShatter}, collapsed={collapsed})", this);
                if (!collapsed && st == ThinIcePlatformV2.IceState.Cracking && landingCount > 0)
                    SyncTimerToLandingDamage();
            }
            prevIceState = st;

            if (!collapsed)
            {
                CountLandings();
                MaybeResetCracksOnSlimeLeft();
                MaybeHealCracks();
                // Covers every shatter cause, not just our Nth landing.
                if (ice.State == ThinIcePlatformV2.IceState.Shattered) Collapse();
            }
            else if (ice.State == ThinIcePlatformV2.IceState.Regenerating ||
                     ice.State == ThinIcePlatformV2.IceState.Idle)
            {
                Restore();
            }
            else
            {
                MaybeRegenOnTimer();
                MaybeRegenOnSlimeLeft();
            }

            // Outside the collapsed branch: a fallen pole and a respawned stalk can coexist.
            TickCorpseLifetime();
        }

        // RequestRegen only acts on Shattered ice, so this and the zone-exit route below are
        // idempotent — whichever fires first wins.
        void MaybeRegenOnTimer()
        {
            if (config.respawnDelaySeconds <= 0f) return;
            if (ice.State != ThinIcePlatformV2.IceState.Shattered) return;
            if (Time.time - collapsedAt < config.respawnDelaySeconds) return;

            Debug.Log($"[BambooOnThinIce {name}] respawn timer elapsed " +
                      $"({config.respawnDelaySeconds}s) — requesting regen", this);
            ice.RequestRegen();
        }

        // The zone only regens on OnTriggerExit2D, but the Nth landing happens at the stalk TIP
        // above the box, so that exit event is already spent — watch for the exit ourselves.
        void MaybeRegenOnSlimeLeft()
        {
            if (ice.State != ThinIcePlatformV2.IceState.Shattered) return;
            if (!SlimeOutsideZone()) return;

            Debug.Log($"[BambooOnThinIce {name}] slime left the zone area — requesting regen", this);
            ice.RequestRegen();
        }

        // Partial damage resets on zone exit, matching the zone's own regen semantics.
        void MaybeResetCracksOnSlimeLeft()
        {
            if (landingCount == 0) return;
            if (!SlimeOutsideZone()) return;
            ResetCracks("slime left the zone area");
        }

        // Partial damage also heals in place after crackHealSeconds of no contact.
        void MaybeHealCracks()
        {
            if (landingCount == 0) return;
            if (config.crackHealSeconds <= 0f) return;
            if (offStalkSeconds < config.crackHealSeconds) return;
            ResetCracks($"no contact for {config.crackHealSeconds}s");
        }

        void ResetCracks(string reason)
        {
            Debug.Log($"[BambooOnThinIce {name}] {reason} — cracks reset " +
                      $"({landingCount}/{config.jumpsToShatter} dropped)", this);
            landingCount = 0;
            // MaxValue, not 0: else the landing debounce eats the next contact.
            offStalkSeconds = float.MaxValue;
            if (ice.State == ThinIcePlatformV2.IceState.Idle && iceRenderer != null)
            {
                iceRenderer.GetPropertyBlock(mpb);
                mpb.SetFloat(CrackProgressId, 0f);
                iceRenderer.SetPropertyBlock(mpb);
            }
        }

        // "Outside" = sideways out of the shape or below it; ABOVE still counts as inside.
        // ClosestPoint keeps that rule on any shape (inside → returns the point itself).
        bool SlimeOutsideZone()
        {
            if (zoneCollider == null) return false;
            var slime = ResolveSlime();
            if (slime == null) return false;

            Vector2 pos = slime.transform.position;
            Vector2 nearest = zoneCollider.ClosestPoint(pos);
            const float Margin = 0.5f;
            return Mathf.Abs(nearest.x - pos.x) > Margin || pos.y < nearest.y - Margin;
        }

        // --- Landing → crack ------------------------------------------------------

        // Landing = the slime's surface collider, NOT RiderAttached (corpse weight coupling
        // raises it too). Debounced: attach flickers off/on and double-counts one landing.
        void CountLandings()
        {
            if (IsSlimeOnStalk())
            {
                if (offStalkSeconds >= config.landingDebounceSeconds) OnStalkLanding();
                offStalkSeconds = 0f;
            }
            else
            {
                offStalkSeconds += Time.fixedDeltaTime;
            }
        }

        bool IsSlimeOnStalk()
        {
            var slime = ResolveSlime();
            if (slime == null) return false;
            var col = slime.SurfaceCollider;
            return col != null && col.transform.IsChildOf(bamboo.transform);
        }

        void OnStalkLanding()
        {
            landingCount++;
            Debug.Log($"[BambooOnThinIce {name}] stalk landing {landingCount}/{config.jumpsToShatter} (ice {ice.State})", this);

            if (landingCount >= config.jumpsToShatter)
            {
                ShatterIce();
                return;
            }

            PlayCrackSound();

            if (ice.State == ThinIcePlatformV2.IceState.Idle && iceRenderer != null)
            {
                // Crack visual while Idle: we own the shader progress.
                float progress = Mathf.Clamp01((float)landingCount / config.jumpsToShatter);
                iceRenderer.GetPropertyBlock(mpb);
                mpb.SetFloat(CrackProgressId, progress);
                iceRenderer.SetPropertyBlock(mpb);
            }
            else if (ice.State == ThinIcePlatformV2.IceState.Cracking)
            {
                // Ice already counting down from a direct touch: bite one landing's worth
                // out of its timer so both damage paths share the pool.
                ReduceTimer(CrackingDuration() / Mathf.Max(1, config.jumpsToShatter));
            }
        }

        void ShatterIce()
        {
            if (ice.State != ThinIcePlatformV2.IceState.Idle &&
                ice.State != ThinIcePlatformV2.IceState.Cracking) return;

            if (!enterShatteredSearched)
            {
                enterShatteredSearched = true;
                enterShatteredMethod = typeof(ThinIcePlatformV2).GetMethod(
                    "EnterShattered", BindingFlags.Instance | BindingFlags.NonPublic);
                if (enterShatteredMethod == null)
                {
                    Debug.LogError("[BambooOnThinIce] Reflection target ThinIcePlatformV2.EnterShattered(bool) " +
                                   "not found — was it renamed? Bamboo-driven shatter is disabled.", this);
                }
            }
            if (enterShatteredMethod == null) return;

            // jumpInherit: false — the slime is on the stalk, not jumping off the ice.
            enterShatteredMethod.Invoke(ice, new object[] { false });
            Debug.Log($"[BambooOnThinIce {name}] shatter fired via reflection (ice now {ice.State})", this);
            // Collapse() fires this same FixedUpdate via the State poll above.
        }

        // --- Merged damage pool (timer reflection) ---------------------------------

        // Known limitation: reads the shared config, so a platform with overrideBehavior on
        // uses a different duration internally and the landing bite is scaled slightly off.
        float CrackingDuration()
        {
            var config = ice.ResolvedConfig;
            return config != null ? Mathf.Max(0.0001f, config.crackingDuration) : 2f;
        }

        bool TryGetTimerField()
        {
            if (!timerFieldSearched)
            {
                timerFieldSearched = true;
                timerField = typeof(ThinIcePlatformV2).GetField(
                    "timer", BindingFlags.Instance | BindingFlags.NonPublic);
                if (timerField == null)
                {
                    Debug.LogError("[BambooOnThinIce] Reflection target ThinIcePlatformV2.timer " +
                                   "not found — was it renamed? Damage-pool merging is disabled.", this);
                }
            }
            return timerField != null;
        }

        // Direct touch entered Cracking with a full timer: fast-forward it to the
        // damage the stalk landings already earned. Only ever shortens, never extends.
        void SyncTimerToLandingDamage()
        {
            if (!TryGetTimerField()) return;
            float remaining = (1f - Mathf.Clamp01((float)landingCount / config.jumpsToShatter)) * CrackingDuration();
            float current = (float)timerField.GetValue(ice);
            if (current > remaining) timerField.SetValue(ice, remaining);
        }

        void ReduceTimer(float amount)
        {
            if (!TryGetTimerField()) return;
            float current = (float)timerField.GetValue(ice);
            // Timer <= 0 makes the ice's own UpdateCracking shatter on its next tick.
            timerField.SetValue(ice, current - amount);
        }

        // Known limitation: shared config only — a platform with overrideAudio on still cracks
        // with the config's clips; honoring it needs more reflection, not worth it for a sound.
        void PlayCrackSound()
        {
            var config = ice.ResolvedConfig;
            if (config == null) return;
            SoundService.Play(config.crackSound, ice.transform.position);
        }

        // --- Collapse: hide the original, drop a physics corpse --------------------

        void Collapse()
        {
            // One corpse slot: a previous pole may still be lying around (lifetime outlives respawn).
            if (corpse != null) PoofCorpse("replaced by a new fall");

            collapsed = true;
            collapsedAt = Time.time;

            // Detach before the surface disappears; ForceAirborneWithVelocity's launch
            // clearance guard prevents an instant re-stick, then the slime rides the corpse down.
            DetachSlimeFrom(bamboo.transform);

            // Snapshot the live spring pose before hiding the original.
            float bendDeg = bamboo.CurrentDeflectionDeg;
            float length = bamboo.BambooLength;
            float swingOmegaDeg = bamboo.AngularVelocity;

            // Deactivate FIRST so the clone is born inactive and its BambooPlatformV3 can be
            // removed synchronously — else PlatformCarrier can resolve the dead spring.
            bamboo.gameObject.SetActive(false);
            SpawnCorpse(bendDeg, length, swingOmegaDeg);

            HookLaunchRecoil();

            Debug.Log($"[BambooOnThinIce {name}] collapse: corpse '{corpse.name}' spawned, " +
                      $"zone={(zoneCollider != null ? zoneCollider.name : "none (permanent debris)")}", this);
        }

        void SpawnCorpse(float bendDeg, float length, float swingOmegaDeg)
        {
            // Explicit world pose: the bamboo may be a child of the ice or a moving parent, and
            // Instantiate(go) alone re-roots local values. Source inactive → clone inactive.
            corpse = Instantiate(bamboo.gameObject, bamboo.transform.position, bamboo.transform.rotation);
            corpse.name = bamboo.name + " (Fallen)";

            // Remove synchronously while the clone is inactive: the corpse must never be seen
            // with two PlatformMotors on it.
            var cloneStalk = corpse.GetComponent<BambooPlatformV3>();
            if (cloneStalk != null)
            {
                DestroyImmediate(cloneStalk);
            }

            // Instantiate does NOT copy MaterialPropertyBlock — re-push the bend or the
            // corpse snaps straight.
            var visual = corpse.transform.Find("Visual");
            if (visual != null)
            {
                var mr = visual.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.GetPropertyBlock(mpb);
                    mpb.SetFloat(BendAngleId, bendDeg);
                    mpb.SetFloat(BambooLengthId, length);
                    mr.SetPropertyBlock(mpb);
                }
            }

            var corpseMotor = corpse.AddComponent<FallingBambooCorpseMotor>();
            corpseMotor.InitShape(bendDeg, length, bamboo.SegmentRadius);
            corpseMotor.bambooMass = config.bambooMass;
            corpseMotor.ridingSlimeMass = config.ridingSlimeMass;
            corpseMotor.landingDebounce = config.landingDebounceSeconds;
            // The corpse is still a bamboo: underhang slip runs off the stalk's own knobs.
            corpseMotor.bambooSettings = bamboo.Settings;

            // PlatformMotor.Awake resets the body to Kinematic, so the Dynamic switch must follow.
            corpse.SetActive(true);

            // Segment colliders carry the bent pose — zone-exit tests against these, not an AABB.
            corpseColliders = corpse.GetComponentsInChildren<Collider2D>();
            corpseRestSeconds = 0f;

            // Continuous, else the pole tunnels through thin terrain. Seeding angular velocity
            // carries the spring's visible swing into the topple instead of a frozen pose.
            var rb = corpse.GetComponent<Rigidbody2D>();
            corpseRb = rb;
            rb.bodyType = RigidbodyType2D.Dynamic;
            rb.mass = config.slimeMass;
            rb.gravityScale = config.corpseGravityScale;
            rb.linearDamping = config.corpseLinearDamping;
            rb.angularDamping = config.corpseAngularDamping;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.constraints = RigidbodyConstraints2D.None;
            rb.angularVelocity = swingOmegaDeg;
        }

        // --- Restore: ice regenerated → stalk stands back up -----------------------

        void Restore()
        {
            // Only deactivated, never mutated — the spring resumes from its collapse bend.
            bamboo.gameObject.SetActive(true);
            // Same dust burst the pole leaves behind, now covering the stalk popping in.
            SpawnPuffs(bamboo.transform, bamboo.CurrentDeflectionDeg, bamboo.BambooLength);
            Debug.Log($"[BambooOnThinIce {name}] restore: stalk reactivated (ice {ice.State})", this);

            landingCount = 0;
            offStalkSeconds = float.MaxValue;
            collapsed = false;
        }

        // --- Corpse poof: dust puffs along the pole, corpse destroyed same frame ----

        // Three exits, whichever comes first: too old, at rest, or out of the zone.
        void TickCorpseLifetime()
        {
            if (corpse == null) return;

            if (config.corpseMaxLifeSeconds > 0f && Time.time - collapsedAt >= config.corpseMaxLifeSeconds)
            {
                PoofCorpse($"{config.corpseMaxLifeSeconds}s since the fall");
                return;
            }

            // "Stopped" = Box2D's own rest verdict, so no stillness threshold to tune.
            bool atRest = corpseRb != null &&
                          (corpseRb.bodyType == RigidbodyType2D.Static || corpseRb.IsSleeping());
            corpseRestSeconds = atRest ? corpseRestSeconds + Time.fixedDeltaTime : 0f;
            if (config.corpseRestPoofSeconds > 0f && corpseRestSeconds >= config.corpseRestPoofSeconds)
            {
                PoofCorpse($"at rest for {config.corpseRestPoofSeconds}s");
                return;
            }

            MaybePoofOutOfZone();
        }

        // Shape-exact both sides: real zone collider vs each segment collider, not bounds.
        void MaybePoofOutOfZone()
        {
            if (corpse == null || zoneCollider == null || corpseColliders == null) return;
            for (int i = 0; i < corpseColliders.Length; i++)
            {
                var c = corpseColliders[i];
                if (c == null || !c.isActiveAndEnabled) continue;
                if (Physics2D.Distance(zoneCollider, c).isOverlapped) return;
            }
            PoofCorpse("left the respawn zone");
        }

        void PoofCorpse(string reason)
        {
            if (corpse == null) return;

            Debug.Log($"[BambooOnThinIce {name}] corpse poof ({reason})", this);

            DetachSlimeFrom(corpse.transform);
            var motor = corpse.GetComponent<FallingBambooCorpseMotor>();
            if (motor != null) SpawnPuffs(corpse.transform, motor.BendAngleDeg, motor.StalkLength);

            Destroy(corpse);
            corpse = null;
            corpseRb = null;
            corpseColliders = null;
        }

        // Dust puffs along a bent pole's centerline — used by both poof and restore.
        void SpawnPuffs(Transform root, float bendDeg, float length)
        {
            if (config.corpsePoofPrefab == null) return;

            int puffs = Mathf.Max(2, Mathf.CeilToInt(length / PoofSpacing) + 1);
            for (int i = 0; i < puffs; i++)
            {
                float t = (float)i / (puffs - 1);
                Vector2 pos = root.TransformPoint(
                    FallingBambooCorpseMotor.ArcLocalPoint(bendDeg, length, t));
                var go = Instantiate(config.corpsePoofPrefab, pos, Quaternion.identity);
                // Prefab contract: playOnAwake OFF (explicit Play), stopAction=Destroy.
                var ps = go.GetComponent<ParticleSystem>();
                if (ps != null) ps.Play();
            }
        }

        // --- Launch recoil (explicit — launch clearance ignores kill the contact impulse) ---

        void HookLaunchRecoil()
        {
            var slime = ResolveSlime();
            if (slime == null || slime == hookedSlime) return;
            if (hookedSlime != null) hookedSlime.OnLaunch -= OnSlimeLaunched;
            slime.OnLaunch += OnSlimeLaunched;
            hookedSlime = slime;
        }

        void OnDestroy()
        {
            if (hookedSlime != null) hookedSlime.OnLaunch -= OnSlimeLaunched;
        }

        void OnSlimeLaunched(Vector2 pos, Vector2 vel, bool wasHop)
        {
            if (corpse == null) return;
            var slime = ResolveSlime();
            if (slime == null) return;
            // SurfaceCollider still set here — OnLaunch fires before the state exit.
            var col = slime.SurfaceCollider;
            if (col == null || !col.transform.IsChildOf(corpse.transform)) return;
            var crb = corpse.GetComponent<Rigidbody2D>();
            if (crb == null || crb.bodyType != RigidbodyType2D.Dynamic) return;
            crb.AddForceAtPosition(-vel * config.corpseRecoilImpulse, pos, ForceMode2D.Impulse);

            // Whatever the pole rests on feels the jump too (recoil half of TransmitToStalks).
            var motor = corpse.GetComponent<FallingBambooCorpseMotor>();
            if (motor != null) motor.TransmitToStalks(-vel, config.ridingSlimeMass);
        }

        void DetachSlimeFrom(Transform surfaceRoot)
        {
            var slime = ResolveSlime();
            if (slime == null) return;
            var col = slime.SurfaceCollider;
            if (col == null) return;
            if (!col.transform.IsChildOf(surfaceRoot)) return;
            slime.ForceAirborneWithVelocity(slime.Velocity);
        }

        SlimeController ResolveSlime()
        {
            if (cachedSlime == null) cachedSlime = FindFirstObjectByType<SlimeController>();
            return cachedSlime;
        }
    }

    /// Runtime-only motor for the fallen stalk corpse. Physics owns the Dynamic body;
    /// hides PlatformMotor.FixedUpdate so MovePosition never touches it. Sleeps on plain
    /// ground → snapped Static; on a live stalk it stays Dynamic and loads that spring.
    /// ISurfaceAnchorProvider uses the frozen arc centerline: SurfaceProbe's ClosestPoint
    /// pass jitters across the capsule chain, which played as "can't jump off it".
    public class FallingBambooCorpseMotor : PlatformMotor, ISurfaceAnchorProvider
    {
        bool snapped;
        float bendAngleDeg;
        float stalkLength = 1f;
        float stalkRadius = 0.15f;

        /// Mass a live stalk feels from this pole (config bambooMass); independent of rb.mass.
        public float bambooMass = 2f;

        /// Extra mass a supporting stalk feels while the slime rides this pole.
        public float ridingSlimeMass = 1f;

        /// Seconds off this pole before a touch counts as a new landing (flicker is not one).
        public float landingDebounce = 0.1f;

        /// Stalk tuning the corpse inherits at spawn (underhang cling + reland lockout).
        public Settings.BambooV3Settings bambooSettings;

        Controller.SlimeController cachedSlime;
        Collider2D[] ownColliders;
        Collider2D ignoredSlimeCollider;
        BambooPlatformV3.UnderhangSlip underhang;
        float offPoleSeconds = float.MaxValue;

        public override Vector2 Velocity => rb.linearVelocity;
        public override float AngularVelocity => rb.angularVelocity;

        public float StalkLength => stalkLength;
        public float BendAngleDeg => bendAngleDeg;

        /// World-space point on the frozen bent centerline, t = 0 (base) .. 1 (tip).
        public Vector2 CenterlineWorldPoint(float t) => transform.TransformPoint(CenterlinePoint(t));

        /// Frozen bent-pose shape, captured from the live stalk at collapse.
        public void InitShape(float bendDeg, float length, float radius)
        {
            bendAngleDeg = bendDeg;
            stalkLength = Mathf.Max(0.01f, length);
            stalkRadius = Mathf.Max(0.001f, radius);
        }

        // Hides PlatformMotor.FixedUpdate (Unity calls only the most-derived): the base tick
        // MovePositions a Dynamic body, stomping integrated velocity so it never sleeps.
        void FixedUpdate()
        {
            TickRiderCollisionIgnore();
            // throwCapable: false — a fallen pole has no spring, so the cling window is the only way down.
            if (bambooSettings != null)
                underhang.Tick(cachedSlime, transform, bambooSettings.underhangClingTime,
                    bambooSettings.relandLockoutTime, false, Time.fixedDeltaTime);
            ScanTouchedStalks();
            MaybeSnapStatic();
            TickStalkWeightCoupling();
            TickRiderLandingTransfer();
        }

        // --- Rider contact: the slime must not brake the fall ----------------------

        // SlimeController overwrites its rigidbody velocity every FixedUpdate, so every impulse
        // the corpse spends on its own rider is energy gone — a clinging slime braked the fall
        // to a crawl. Narrow: only while Dynamic and only against the attached collider.
        void TickRiderCollisionIgnore()
        {
            if (cachedSlime == null) cachedSlime = Object.FindFirstObjectByType<Controller.SlimeController>();

            // Tracked as the suppressed collider, not a bool: a slime destroyed and replaced
            // mid-fall would leave the flag set and the new rider would keep braking the corpse.
            Collider2D want = null;
            if (cachedSlime != null && rb.bodyType == RigidbodyType2D.Dynamic)
            {
                var surf = cachedSlime.SurfaceCollider;
                if (surf != null && surf.transform.IsChildOf(transform))
                    want = cachedSlime.GetComponent<CircleCollider2D>();
            }
            if (want == ignoredSlimeCollider) return;

            SetRiderCollisionIgnored(ignoredSlimeCollider, false);
            SetRiderCollisionIgnored(want, true);
            ignoredSlimeCollider = want;
        }

        void SetRiderCollisionIgnored(Collider2D slimeCol, bool ignore)
        {
            if (slimeCol == null) return;
            if (ownColliders == null) ownColliders = GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < ownColliders.Length; i++)
                if (ownColliders[i] != null) Physics2D.IgnoreCollision(slimeCol, ownColliders[i], ignore);
        }

        void MaybeSnapStatic()
        {
            if (snapped || rb.bodyType != RigidbodyType2D.Dynamic || !rb.IsSleeping()) return;
            // On a live stalk stay Dynamic — a Static corpse gets no contacts against
            // the kinematic stalk, so the spring would unload and the pole would float.
            if (touchedStalks.Count > 0) return;
            snapped = true;
            rb.bodyType = RigidbodyType2D.Static;
        }

        // Never called (base FixedUpdate hidden) — abstract contract only.
        protected override (Vector2 position, float rotation) ComputeMotion(float dt)
        {
            return (rb.position, rb.rotation);
        }

        // --- Weight coupling: corpse resting on a live stalk bends its spring -------

        static readonly ContactPoint2D[] ContactScratch = new ContactPoint2D[32];
        readonly List<BambooPlatformV3> touchedStalks = new List<BambooPlatformV3>();
        readonly List<Vector2> touchedPoints = new List<Vector2>();

        void ScanTouchedStalks()
        {
            touchedStalks.Clear();
            touchedPoints.Clear();
            if (rb.bodyType != RigidbodyType2D.Dynamic) return;

            int n = rb.GetContacts(ContactScratch);
            for (int i = 0; i < n; i++)
            {
                var col = ContactScratch[i].collider;
                if (col == null) continue;
                var stalk = col.GetComponentInParent<BambooPlatformV3>();
                if (stalk == null || !stalk.isActiveAndEnabled) continue;
                if (touchedStalks.Contains(stalk)) continue;
                touchedStalks.Add(stalk);
                touchedPoints.Add(ContactScratch[i].point);
            }
        }

        // --- Slime on the pole -> the stalks holding the pole up ---------------------

        // A Kinematic stalk feels nothing through contact, and ApplyRiderInfluence only fires on
        // the firstAttach edge which this pole holds — so each channel is handed over explicitly.
        void TickRiderLandingTransfer()
        {
            if (!SlimeIsRiding())
            {
                offPoleSeconds += Time.fixedDeltaTime;
                return;
            }

            // Debounced: attach flickers across this pole's overlapping capsules, and an
            // undebounced edge injects the same landing into the supports over and over.
            if (offPoleSeconds >= landingDebounce) TransmitToStalks(SlimeImpactVelocity(), ridingSlimeMass);
            offPoleSeconds = 0f;
        }

        // PlatformMotor runs at order -100, so SlimeController zeroes the landing velocity AFTER
        // this tick — read its captured impact instead (normal unsigned, tangent signed).
        Vector2 SlimeImpactVelocity()
        {
            if (cachedSlime == null) return Vector2.zero;
            return -cachedSlime.SurfaceNormal * cachedSlime.ImpactNormalSpeed
                   + cachedSlime.SurfaceTangent * cachedSlime.ImpactTangentSpeed;
        }

        /// <summary>Hands an impact down to every live stalk holding this pole up, split between them.</summary>
        public void TransmitToStalks(Vector2 velocity, float mass)
        {
            if (mass <= 0f || touchedStalks.Count == 0) return;
            float share = mass / touchedStalks.Count;
            for (int i = 0; i < touchedStalks.Count; i++)
                touchedStalks[i].ApplyExternalImpact(touchedPoints[i], velocity, share);
        }

        bool SlimeIsRiding()
        {
            if (cachedSlime == null) return false;
            var surf = cachedSlime.SurfaceCollider;
            return surf != null && surf.transform.IsChildOf(transform);
        }

        // Dead weight, not a rider: ApplyExternalLoad leaves the stalk's single rider slot to the
        // slime — holding it made the stalk stop reacting to slime landings (firstAttach never
        // fired) and made every contact flicker a RemoveRider, registering a reland lockout.
        void TickStalkWeightCoupling()
        {
            if (touchedStalks.Count == 0) return;

            // A slime on this pole is weight the supports carry too, split like the impact channel.
            float mass = bambooMass;
            if (SlimeIsRiding()) mass += ridingSlimeMass / touchedStalks.Count;

            for (int i = 0; i < touchedStalks.Count; i++)
            {
                var stalk = touchedStalks[i];
                stalk.ApplyExternalLoad(touchedPoints[i], mass);

                // GetPointVelocity, not rb.linearVelocity: a pole rocking on its other support
                // has a moving centre but a near-still contact point, and that is what lands.
                if (RecentContactIndex(stalk) < 0)
                    stalk.ApplyExternalImpact(touchedPoints[i], rb.GetPointVelocity(touchedPoints[i]), bambooMass);
            }

            TickRecentContacts();
        }

        // Contact with one stalk can blink out for a frame while the pole rests across several;
        // treating the reappearance as a fresh slam pumps the spring (the impulse has no reaction).
        readonly List<BambooPlatformV3> recentStalks = new List<BambooPlatformV3>();
        readonly List<float> recentOffSeconds = new List<float>();

        int RecentContactIndex(BambooPlatformV3 stalk) => recentStalks.IndexOf(stalk);

        void TickRecentContacts()
        {
            for (int i = recentStalks.Count - 1; i >= 0; i--)
            {
                if (recentStalks[i] == null)
                {
                    recentStalks.RemoveAt(i);
                    recentOffSeconds.RemoveAt(i);
                    continue;
                }
                if (touchedStalks.Contains(recentStalks[i]))
                {
                    recentOffSeconds[i] = 0f;
                    continue;
                }
                recentOffSeconds[i] += Time.fixedDeltaTime;
                if (recentOffSeconds[i] < landingDebounce) continue;
                recentStalks.RemoveAt(i);
                recentOffSeconds.RemoveAt(i);
            }

            for (int i = 0; i < touchedStalks.Count; i++)
            {
                if (recentStalks.Contains(touchedStalks[i])) continue;
                recentStalks.Add(touchedStalks[i]);
                recentOffSeconds.Add(0f);
            }
        }

        // --- Analytic surface anchor (mirrors BambooPlatformV3's private math) ------

        Vector2 CenterlinePoint(float t) => ArcLocalPoint(bendAngleDeg, stalkLength, t);

        /// Local-space point on the bent centerline, t = 0 (base) .. 1 (tip); same arc as BambooBend.shader.
        public static Vector2 ArcLocalPoint(float bendDeg, float length, float t)
        {
            float bendRad = bendDeg * Mathf.Deg2Rad;
            if (Mathf.Abs(bendRad) < 1e-4f)
                return new Vector2(0f, t * length);

            float invK = length / bendRad;
            float u = bendRad * t;
            return new Vector2(invK * (Mathf.Cos(u) - 1f), invK * Mathf.Sin(u));
        }

        Vector2 CenterlineTangent(float t)
        {
            float bendRad = bendAngleDeg * Mathf.Deg2Rad;
            if (Mathf.Abs(bendRad) < 1e-4f)
                return Vector2.up;

            float u = bendRad * t;
            return new Vector2(-Mathf.Sin(u), Mathf.Cos(u)).normalized;
        }

        float ClosestCenterlineT(Vector2 localPoint)
        {
            const int Samples = 32;
            float bestT = 0f;
            float bestSqr = float.PositiveInfinity;
            Vector2 prev = CenterlinePoint(0f);

            for (int i = 0; i < Samples; i++)
            {
                float t0 = (float)i / Samples;
                float t1 = (float)(i + 1) / Samples;
                Vector2 next = CenterlinePoint(t1);
                Vector2 seg = next - prev;
                float segSqr = seg.sqrMagnitude;
                float u = segSqr > 1e-8f
                    ? Mathf.Clamp01(Vector2.Dot(localPoint - prev, seg) / segSqr)
                    : 0f;
                Vector2 closest = prev + seg * u;
                float sqr = (localPoint - closest).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestT = Mathf.Lerp(t0, t1, u);
                }
                prev = next;
            }

            return Mathf.Clamp01(bestT);
        }

        public bool TryGetSurfaceAnchor(Collider2D sourceCollider, Vector2 riderWorldPos, float riderRadius,
            out SurfaceAnchorInfo anchorInfo)
        {
            anchorInfo = default;
            if (sourceCollider == null) return false;
            if (!sourceCollider.enabled) return false;
            if (!sourceCollider.gameObject.activeInHierarchy) return false;

            Vector2 local = transform.InverseTransformPoint(riderWorldPos);
            float t = ClosestCenterlineT(local);
            Vector2 center = CenterlinePoint(t);
            Vector2 outward = local - center;
            if (outward.sqrMagnitude < 1e-8f)
            {
                Vector2 tangent = CenterlineTangent(t);
                outward = new Vector2(tangent.y, -tangent.x);
            }

            outward.Normalize();
            Vector2 anchorLocal = center + outward * (stalkRadius + Mathf.Max(0f, riderRadius));

            Vector2 anchorWorldPos = transform.TransformPoint(anchorLocal);
            Vector2 normalWorld = ((Vector2)transform.TransformVector(outward)).normalized;
            Vector2 tangentWorld = new Vector2(-normalWorld.y, normalWorld.x);
            // consumesPlatformCarry: the anchor is recomputed against the corpse's current
            // transform every probe, so the carrier's rigid-TRS carry would double-correct.
            anchorInfo = new SurfaceAnchorInfo(anchorWorldPos, normalWorld, tangentWorld, true, name);
            return true;
        }
    }
}
