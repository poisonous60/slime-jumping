using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Physics;
using SlimeJumping.Platforms.ThinIceV2;

namespace SlimeJumping.Platforms
{
    /// Couples a BambooPlatformV3 stalk to the ThinIcePlatformV2 it stands on.
    ///
    /// Loop: each slime landing on the stalk advances the ice's cracks by
    /// 1/jumpsToShatter (crack sound + shader progress); the Nth landing shatters the
    /// ice. When the ice is Shattered — by the stalk OR by the slime stepping on the
    /// ice directly — the standing stalk is swapped for a physics "corpse" clone that
    /// topples under gravity (FallingTrainPlatform pattern), inheriting the spring's
    /// current swing. When the ice regenerates (its ThinIceRespawnZone fired), the
    /// corpse is removed and the original stalk is re-activated where it stood.
    ///
    /// DELIBERATELY ZERO-TOUCH: this file talks to both systems exclusively through
    /// their public surface (RiderAttached / State / ResolvedConfig / carrier
    /// contract) plus one reflection call into ThinIcePlatformV2.EnterShattered.
    /// Deleting this file (and its test setup) restores stock behavior exactly.
    /// One user-approved exception: BambooPlatformV3.CheckSpringThrow gained a
    /// "slime is riding THIS stalk" guard (harmless without this feature).
    ///
    /// Placement: attach to the ICE GameObject (or any GameObject that stays active),
    /// NEVER to the bamboo itself — the bamboo is deactivated while collapsed, which
    /// would stop this component's polling, and the corpse clone would inherit a
    /// second copy of it.
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

        // Reflection handle into ThinIcePlatformV2.EnterShattered(bool jumpInherit).
        // The one non-public touch point (user-approved tradeoff for keeping the ice
        // file untouched). If the method is ever renamed this logs loudly instead of
        // silently doing nothing.
        static MethodInfo enterShatteredMethod;
        static bool enterShatteredSearched;

        // Second reflection touch point: ThinIcePlatformV2.timer (private float, the
        // Cracking countdown). Needed to MERGE the two damage paths into one pool —
        // without it, stepping on the ice directly restarts its timer from zero and
        // visually regresses the cracks the stalk landings already earned, and stalk
        // landings during Cracking contribute nothing.
        static FieldInfo timerField;
        static bool timerFieldSearched;

        int landingCount;
        float offStalkSeconds = float.MaxValue;
        bool collapsed;
        float collapsedAt;   // Time.time of the topple — drives the respawn timer
        ThinIcePlatformV2.IceState prevIceState = ThinIcePlatformV2.IceState.Idle;
        GameObject corpse;
        Collider2D[] corpseColliders;
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

            // BambooV3 EnsureSettings pattern: a config-less project state still works
            // on the class defaults instead of null-guarding every read.
            config = BambooOnThinIceConfig.Resolve(configOverride);
            if (config == null) config = ScriptableObject.CreateInstance<BambooOnThinIceConfig>();

            var zone = ice.GetComponentInParent<ThinIceRespawnZone>();
            zoneCollider = zone != null ? zone.Shape : null;
        }

        void FixedUpdate()
        {
            // Central ice-state watch: diagnostic transition log + the Cracking-entry
            // damage-pool sync (slime touched the ice directly while stalk landings
            // had already cracked it partway — fast-forward the fresh touch timer so
            // both paths pour into ONE pool).
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
                // Covers every shatter cause: our Nth landing, the slime stepping on
                // the ice directly (touch timer / departure), anything.
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
                MaybePoofOutOfZone();
            }
        }

        // The stalk stands back up on its own respawnDelaySeconds after it fell, wherever the
        // slime is. Same public call as the zone-exit route below (RequestRegen only acts on
        // Shattered ice, so whichever fires first wins and the other is a no-op).
        void MaybeRegenOnTimer()
        {
            if (config.respawnDelaySeconds <= 0f) return;
            if (ice.State != ThinIcePlatformV2.IceState.Shattered) return;
            if (Time.time - collapsedAt < config.respawnDelaySeconds) return;

            Debug.Log($"[BambooOnThinIce {name}] respawn timer elapsed " +
                      $"({config.respawnDelaySeconds}s) — requesting regen", this);
            ice.RequestRegen();
        }

        // The zone only regens on an OnTriggerExit2D event. In the bamboo path the
        // Nth landing happens at the stalk TIP — often ABOVE the zone box — so the
        // slime's exit event was already spent while the ice was still Idle, and if
        // the throw carries the slime away without re-entering the box, the zone
        // never fires again. The link watches for "slime left the area" itself and
        // calls the ice's public RequestRegen (idempotent with the zone's own call).
        // "Left" = outside in X or fell below — being ABOVE the box still counts as
        // inside, so the regen doesn't fire mid-bounce over the trial area.
        void MaybeRegenOnSlimeLeft()
        {
            if (ice.State != ThinIcePlatformV2.IceState.Shattered) return;
            if (!SlimeOutsideZone()) return;

            Debug.Log($"[BambooOnThinIce {name}] slime left the zone area — requesting regen", this);
            ice.RequestRegen();
        }

        // Partial landing damage resets when the slime leaves the zone area, matching
        // the zone's own regen semantics for shattered ice.
        void MaybeResetCracksOnSlimeLeft()
        {
            if (landingCount == 0) return;
            if (!SlimeOutsideZone()) return;
            ResetCracks("slime left the zone area");
        }

        // ...and it also heals in place: leave the stalk alone for crackHealSeconds and the
        // partial damage is gone, so a run that stalls does not silently keep its progress.
        // offStalkSeconds is the same counter the landing debounce uses (CountLandings).
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
            // MaxValue, not 0: the next contact must count as a fresh landing rather than
            // being eaten by the landing debounce.
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

        // Landing = the slime's surface collider, NOT RiderAttached — the corpse
        // weight coupling also raises RiderAttached, which miscounted landings.
        // Debounced (config landingDebounceSeconds): attach can flicker off/on within
        // a frame or two, double-counting one physical landing.
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
                // Ice already counting down from direct touch: a stalk landing bites
                // one landing's worth (duration/N) out of the timer, so both damage
                // paths share the pool. The ice's own tick renders the progress.
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

            // jumpInherit: false — shards get no launch-velocity kick; the slime is on
            // the stalk, not jumping off the ice.
            enterShatteredMethod.Invoke(ice, new object[] { false });
            Debug.Log($"[BambooOnThinIce {name}] shatter fired via reflection (ice now {ice.State})", this);
            // Collapse() fires this same FixedUpdate via the State poll above.
        }

        // --- Merged damage pool (timer reflection) ---------------------------------

        // Known limitation (same class as the audio one below): reads the shared
        // config's crackingDuration — a platform with overrideBehavior flipped on
        // uses its local duration internally, so the landing bite would be scaled
        // slightly off there. The test scenes don't override behavior.
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

        // Known limitation: reads the shared config only — a platform with its
        // per-instance audio override (overrideAudio) flipped on will crack with the
        // config's clips here, not its own. Honoring the override would need another
        // reflection surface into private fields; not worth it for a sound.
        void PlayCrackSound()
        {
            var config = ice.ResolvedConfig;
            if (config == null || config.crackSounds == null || config.crackSounds.Length == 0) return;
            var clip = config.crackSounds[Random.Range(0, config.crackSounds.Length)];
            if (clip == null) return;
            SoundService.PlayAt(clip, ice.transform.position, SoundCategory.SFX_Object, config.soundVolume);
        }

        // --- Collapse: hide the original, drop a physics corpse --------------------

        void Collapse()
        {
            collapsed = true;
            collapsedAt = Time.time;

            // Detach a rider cleanly before its surface disappears. Keeps its current
            // velocity; the launch clearance guard inside ForceAirborneWithVelocity
            // prevents an instant re-stick, and the slime then lands on the corpse
            // and rides it down (carrier picks up the corpse motor).
            DetachSlimeFrom(bamboo.transform);

            // Snapshot the live spring pose before hiding the original — the corpse
            // must keep the exact bend and inherit the swing.
            float bendDeg = bamboo.CurrentDeflectionDeg;
            float length = bamboo.BambooLength;
            float swingOmegaDeg = bamboo.AngularVelocity;

            // Deactivate FIRST so the clone is born inactive: its BambooPlatformV3
            // then never runs Awake and can be removed synchronously below. Without
            // this there is a one-frame Destroy-deferral window in which the corpse
            // carries BOTH the dead spring and the corpse motor, and the corpse
            // colliders have no reattach guard — PlatformCarrier's
            // GetComponentInParent<PlatformMotor> (and the probe's
            // ISurfaceAnchorProvider lookup) could resolve the dead spring.
            bamboo.gameObject.SetActive(false);
            SpawnCorpse(bendDeg, length, swingOmegaDeg);

            HookLaunchRecoil();

            Debug.Log($"[BambooOnThinIce {name}] collapse: corpse '{corpse.name}' spawned, " +
                      $"zone={(zoneCollider != null ? zoneCollider.name : "none (permanent debris)")}", this);
        }

        void SpawnCorpse(float bendDeg, float length, float swingOmegaDeg)
        {
            // Explicit world pose: the bamboo may be authored as a child of the ice
            // or a moving parent, and Instantiate(go) alone re-roots local values.
            // The source is inactive, so the clone spawns inactive too — no component
            // on it runs Awake until the SetActive(true) at the end.
            corpse = Instantiate(bamboo.gameObject, bamboo.transform.position, bamboo.transform.rotation);
            corpse.name = bamboo.name + " (Fallen)";

            // The clone must not BE a spring. Removing the component synchronously
            // while the clone is still inactive means the corpse is never seen with
            // two PlatformMotors on it. Segment child transforms keep the bent pose
            // they were cloned with.
            var cloneStalk = corpse.GetComponent<BambooPlatformV3>();
            if (cloneStalk != null)
            {
                DestroyImmediate(cloneStalk);
            }

            // Bend is normally pushed per-frame via MaterialPropertyBlock, which
            // Instantiate does NOT copy — re-push it once so the corpse keeps the
            // exact bent shape instead of snapping straight.
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

            // Activate: the corpse motor's Awake runs here (PlatformMotor resets the
            // body to Kinematic), so the Dynamic switch below must come after.
            corpse.SetActive(true);

            // Segment colliders carry the bent pose — zone-exit tests against these, not an AABB.
            corpseColliders = corpse.GetComponentsInChildren<Collider2D>();

            // FallingTrainPlatform recipe: Dynamic, heavy, continuous collision so the
            // pole cannot tunnel through thin terrain. Seeding the rigidbody with the
            // spring's current angular velocity carries the visible swing straight
            // into the topple instead of starting the fall from a frozen pose.
            var rb = corpse.GetComponent<Rigidbody2D>();
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
            // The old pole poofs into dust as the ice re-forms and the stalk stands
            // back up — the burst covers the swap so it doesn't read as a pop-out.
            if (corpse != null) PoofCorpse("ice regenerating");

            // The original was only deactivated, never mutated: still Kinematic, pivot
            // and serialized state untouched. Its spring resumes from the bend it had
            // at the moment of collapse and settles on its own damping.
            bamboo.gameObject.SetActive(true);
            Debug.Log($"[BambooOnThinIce {name}] restore: stalk reactivated (ice {ice.State})", this);

            landingCount = 0;
            offStalkSeconds = float.MaxValue;
            collapsed = false;
        }

        // --- Corpse poof: dust puffs along the pole, corpse destroyed same frame ----

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
            SpawnPoofPuffs();

            Destroy(corpse);
            corpse = null;
            corpseColliders = null;
        }

        void SpawnPoofPuffs()
        {
            if (config.corpsePoofPrefab == null) return;

            var motor = corpse.GetComponent<FallingBambooCorpseMotor>();
            if (motor == null) return;

            int puffs = Mathf.Max(2, Mathf.CeilToInt(motor.StalkLength / PoofSpacing) + 1);
            for (int i = 0; i < puffs; i++)
            {
                float t = (float)i / (puffs - 1);
                var go = Instantiate(config.corpsePoofPrefab,
                    motor.CenterlineWorldPoint(t), Quaternion.identity);
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

            // Whatever the pole is resting on feels the jump too — the recoil half of the
            // same channel that hands landings down (see TransmitToStalks).
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

    /// Runtime-only motor for the fallen stalk corpse, added by BambooOnThinIce at
    /// collapse. Physics owns the Dynamic body; hides PlatformMotor.FixedUpdate so
    /// MovePosition never touches it (see below). Sleeps on plain ground → snapped
    /// Static (walkable debris). Resting on a live stalk → stays Dynamic and feeds
    /// bambooMass into that stalk's spring via the public rider contract.
    ///
    /// Implements ISurfaceAnchorProvider with the SAME closed-form arc centerline as
    /// BambooPlatformV3 / BambooBend.shader, frozen at the collapse bend angle. The
    /// corpse keeps the stalk's overlapping-capsule collider chain, and SurfaceProbe's
    /// default ClosestPoint pass jitters across those caps — that flicker breaks
    /// attach stability on the fallen pole, which played as "can't jump off it".
    /// Local-space math rides the rigidbody transform, so the anchor stays valid
    /// however the pole tumbled.
    public class FallingBambooCorpseMotor : PlatformMotor, ISurfaceAnchorProvider
    {
        bool snapped;
        float bendAngleDeg;
        float stalkLength = 1f;
        float stalkRadius = 0.15f;

        /// Mass a live stalk feels from this pole (config bambooMass); independent of rb.mass.
        public float bambooMass = 2f;

        /// Extra mass a supporting stalk feels while the slime rides this pole (config
        /// ridingSlimeMass) — weight, landing kick and launch recoil all scale on it.
        public float ridingSlimeMass = 1f;

        /// Seconds the slime must be off this pole before touching it counts as a new landing
        /// (config landingDebounceSeconds) — attach flicker is not a second landing.
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

        /// World-space point on the frozen bent centerline, t = 0 (base) .. 1 (tip).
        public Vector2 CenterlineWorldPoint(float t) => transform.TransformPoint(CenterlinePoint(t));

        /// Frozen bent-pose shape, captured from the live stalk at collapse.
        public void InitShape(float bendDeg, float length, float radius)
        {
            bendAngleDeg = bendDeg;
            stalkLength = Mathf.Max(0.01f, length);
            stalkRadius = Mathf.Max(0.001f, radius);
        }

        // Hides PlatformMotor.FixedUpdate (Unity calls only the most-derived). The base
        // tick MovePositions every frame, which on a Dynamic body stomps the integrated
        // velocity each step: no gravity accel, phantom rb velocity, never sleeps.
        void FixedUpdate()
        {
            TickRiderCollisionIgnore();
            // throwCapable: false — a fallen pole has no spring, so no swing is ever coming to
            // throw a slime off its underside. The cling window is the only way down.
            if (bambooSettings != null)
                underhang.Tick(cachedSlime, transform, bambooSettings.underhangClingTime,
                    bambooSettings.relandLockoutTime, false, Time.fixedDeltaTime);
            ScanTouchedStalks();
            MaybeSnapStatic();
            TickStalkWeightCoupling();
            TickRiderLandingTransfer();
        }

        // --- Rider contact: the slime must not brake the fall ----------------------

        // SlimeController owns the slime's rigidbody and overwrites its velocity every
        // FixedUpdate, so every contact impulse the corpse spends against its own rider
        // is energy that never comes back — a slime stuck to the underside braked the
        // fall to a crawl. Every other platform is Kinematic and never feels its rider;
        // the corpse is the one Dynamic platform, so it opts out explicitly. The rider
        // is held by the surface anchor, not by contact.
        //
        // Narrow on purpose: only while FALLING (Dynamic) and only against the collider
        // the slime is actually attached to this pole with. A settled Static corpse
        // keeps stock collision, and a tumbling corpse still shoves a slime that is
        // standing somewhere else.
        void TickRiderCollisionIgnore()
        {
            if (cachedSlime == null) cachedSlime = Object.FindFirstObjectByType<Controller.SlimeController>();

            // Tracked as the collider pair we actually suppressed, not a bool: a slime that
            // gets destroyed and replaced mid-fall would otherwise leave the flag set and the
            // NEW rider would keep braking the corpse.
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

        // A stalk is Kinematic and driven by its own ODE, so nothing the slime does on this
        // pole reaches it through contact physics, and the rider contract cannot carry it
        // either: ApplyRiderInfluence only kicks on the firstAttach edge, and the pole holds
        // that slot for as long as it rests there. So the pole hands each channel over
        // explicitly — weight below (mass term), landing here, launch recoil from
        // BambooOnThinIce.OnSlimeLaunched.
        void TickRiderLandingTransfer()
        {
            if (!SlimeIsRiding())
            {
                offPoleSeconds += Time.fixedDeltaTime;
                return;
            }

            // Debounced like the ice's landing count (same knob): attach flickers off and back
            // on across this pole's overlapping capsules, and an undebounced edge would inject
            // the same landing into the supporting stalks over and over.
            if (offPoleSeconds >= landingDebounce) TransmitToStalks(SlimeImpactVelocity(), ridingSlimeMass);
            offPoleSeconds = 0f;
        }

        // The landing velocity is already gone from slime.Velocity by the time this edge is
        // visible — PlatformMotor runs at order -100, so SlimeController lands and zeroes its
        // velocity AFTER this tick. SlimeController keeps the captured impact instead, split
        // into surface-relative normal (unsigned, into the surface) and tangent (signed).
        Vector2 SlimeImpactVelocity()
        {
            if (cachedSlime == null) return Vector2.zero;
            return -cachedSlime.SurfaceNormal * cachedSlime.ImpactNormalSpeed
                   + cachedSlime.SurfaceTangent * cachedSlime.ImpactTangentSpeed;
        }

        /// <summary>
        /// Hands an impact down to every live stalk currently holding this pole up, split
        /// between them — a pole bridging two stalks loads each with half. No-op once the
        /// pole has settled Static on the ground (it touches no stalks then).
        /// </summary>
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

        // Dead weight, not a rider: ApplyExternalLoad leaves the stalk's single rider slot to
        // the slime. Holding that slot is what made a stalk with a pole on it stop reacting to
        // the slime landing on it (firstAttach never fired), and made every contact flicker a
        // RemoveRider — which registers the stalk's reland lockout on the slime.
        //
        // Nothing to release: stop calling and the load is gone next tick. Contact flicker just
        // drops the weight for a frame, and the new-contact kick below reads ~0 once the pole
        // has settled (its velocity relative to the stalk surface is what feeds the impulse).
        void TickStalkWeightCoupling()
        {
            if (touchedStalks.Count == 0) return;

            // A slime standing on this pole is weight the supports carry too, split between
            // them exactly like the impact channel.
            float mass = bambooMass;
            if (SlimeIsRiding()) mass += ridingSlimeMass / touchedStalks.Count;

            for (int i = 0; i < touchedStalks.Count; i++)
            {
                var stalk = touchedStalks[i];
                stalk.ApplyExternalLoad(touchedPoints[i], mass);

                // Fresh contact = the pole arriving. That slam is the impulse the old
                // firstAttach landing kick used to deliver. GetPointVelocity, not
                // rb.linearVelocity: a pole rocking on its other support has a moving centre
                // but a nearly still contact point, and the local approach speed is what the
                // stalk actually receives.
                if (RecentContactIndex(stalk) < 0)
                    stalk.ApplyExternalImpact(touchedPoints[i], rb.GetPointVelocity(touchedPoints[i]), bambooMass);
            }

            TickRecentContacts();
        }

        // Contact with one stalk can blink out for a frame or two while the pole rests across
        // several, and treating the reappearance as a fresh slam would pump the spring — the
        // impulse has no reaction on the pole, so a repeat is free energy. A stalk stays
        // "recently touched" for landingDebounce (same knob as the ice landing count).
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

        Vector2 CenterlinePoint(float t)
        {
            float bendRad = bendAngleDeg * Mathf.Deg2Rad;
            if (Mathf.Abs(bendRad) < 1e-4f)
                return new Vector2(0f, t * stalkLength);

            float invK = stalkLength / bendRad;
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
            // consumesPlatformCarry: the anchor is recomputed against the corpse's
            // CURRENT transform every probe, so stacking the carrier's rigid-TRS carry
            // on top would double-correct while the pole is still moving.
            anchorInfo = new SurfaceAnchorInfo(anchorWorldPos, normalWorld, tangentWorld, true, name);
            return true;
        }
    }
}
