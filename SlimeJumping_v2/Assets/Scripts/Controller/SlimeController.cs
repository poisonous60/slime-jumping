using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.InputHandling;
using SlimeJumping.Physics;
using SlimeJumping.Platforms;
using SlimeJumping.Platforms.Snow;
using SlimeJumping.Mechanisms;
using SlimeJumping.Interfaces;
using SlimeJumping.Interaction;
using System.Collections.Generic;

namespace SlimeJumping.Controller
{
    public partial class SlimeController : MonoBehaviour
    {
        [SerializeField] private SlimeInputReader inputReader;
        [SerializeField] private SlimeSettings settings;
        [SerializeField] private PlatformCarrier carrier;
        [SerializeField] private InteractionDetector interactionDetector;

        private Rigidbody2D rb;
        private CircleCollider2D circleCollider;

        public enum SlimeState { Attached, Charging, Airborne, Stunned }
        private SlimeState currentState = SlimeState.Airborne;
        public SlimeState CurrentState => currentState;
        public bool IsTouchingSurface => isTouchingSurface;
        public bool IsSticking => stickTimer > 0f;
        public Vector2 SurfaceNormal => surfaceNormal;
        public Vector2 Position => rb.position;
        public Vector2 Velocity => velocity;
        /// Pinch state for SlimeVisual (set by SlimeController.Pinch.cs). PinchActive = caught
        /// between two platforms; PinchCompression01 = gap-driven squeeze (0..1 of diameter);
        /// PinchAxis = world compression axis. 0 / false when not pinched.
        public bool PinchActive { get; private set; }
        public float PinchCompression01 { get; private set; }
        public Vector2 PinchAxis { get; private set; } = Vector2.up;
        /// Tangential component of the platform-relative impact velocity recorded at the most
        /// recent landing. Already computed for the energy-conversion logic (Charging/Attached
        /// slide decay); OnLanded subscribers can read this to drive impact-scaled effects
        /// without a second velocity capture.
        public float ImpactTangentSpeed => impactTangentSpeed;
        /// Normal (surface-perpendicular) component of the platform-relative impact velocity
        /// recorded at the most recent landing. This is the "how hard the slime slammed into
        /// the surface" reading — good for landing-impact effects (dust burst, trail splat).
        /// Always non-negative (absolute value).
        public float ImpactNormalSpeed => impactNormalSpeed;
        public void AddExternalVelocity(Vector2 deltaV) { externalDeltaV += deltaV; }

        /// <summary>
        /// Sustained-flow push (water current, etc) that should INTEGRATE into the slime's
        /// persistent velocity channel instead of just contributing to one frame.
        ///
        /// Why a separate method from AddExternalVelocity:
        ///   - In Airborne/Stunned, AddExternalVelocity's per-frame add naturally compounds
        ///     into `velocity` because the state handler doesn't overwrite it. Push reaches
        ///     flowTerminalSpeed via accumulation across frames.
        ///   - In Attached/Charging, FixedUpdateAttached at line 168-169 overwrites
        ///     `velocity = (slideSpeed + crawlVelocity) * tangent` every frame. An external push
        ///     added to `velocity` is wiped on the next frame's state-handler pass — the
        ///     effective per-frame contribution is just flowAccel*dt (~0.16 m/s at flowAccel=8,
        ///     50Hz), making the same flowAccel produce a ~50× smaller speed than in Airborne.
        ///   - This method projects the delta onto surfaceTangent and integrates it into the
        ///     persistent channel (slideVelocity for inertial, crawlVelocity for legacy), so
        ///     the push compounds across frames identically in both states.
        ///
        /// Falloff and direction resolution remain the caller's responsibility — pass the
        /// final per-frame deltaV (e.g. dir * flowAccel * falloff * dt).
        /// </summary>
        public void AddSustainedFlowImpulse(Vector2 deltaV)
        {
            if (currentState == SlimeState.Airborne || currentState == SlimeState.Stunned)
            {
                externalDeltaV += deltaV;
                return;
            }
            float tangentDelta = Vector2.Dot(deltaV, surfaceTangent);
            if (surfaceMod.useInertialSlide)
                slideVelocity += tangentDelta;
            else
                crawlVelocity += tangentDelta;
        }

        public float CoyoteTimer => coyoteTimer;
        public float EnergyCoyoteTimer => energyCoyoteTimer;
        public float EnergyCoyoteDuration => energyCoyoteDuration;
        public float PostLaunchTimer => postLaunchTimer;
        public float StickTimer => stickTimer;
        public Collider2D SurfaceCollider => surfaceCollider;
        public Collider2D CachedSurfaceCollider => cachedSurfaceCollider;
        public Collider2D PrevSurfaceCollider => prevSurfaceCollider;
        public PlatformCarrier Carrier => carrier;
        public int AttachToggleCount => attachToggleCount;
        public float AttachLockoutTimer => attachLockoutTimer;
        public float StickDeccelTimer => stickDeccelTimer;
        public float SlideRampTimer => slideRampTimer;
        public float Gravity => settings.gravity;
        // Effective (body-scaled) radius — the slime's actual current size. Shrinks when cut,
        // regrows while healing. See SlimeController.BodyScale.cs.
        public float SlimeRadius => settings.slimeRadius * bodyScaleRatio;
        public float CrawlVelocity => crawlVelocity;
        public Vector2 SurfaceTangent => surfaceTangent;
        public SurfaceProbe Probe => surfaceProbe;
        public SurfaceAnchorInfo LastSurfaceAnchorInfo => lastSurfaceAnchorInfo;
        public bool LastSurfaceAnchorProviderConsumedCarry => lastSurfaceAnchorProviderConsumedCarry;
        public Vector2 AimDirection => aimOffset.sqrMagnitude > 0.0001f
            ? -aimOffset.normalized
            : surfaceNormal;
        public Vector2 AimOffset => aimOffset;
        public float ChargingProgress => activeChargeRadius > 0.0001f
            ? aimOffset.magnitude / activeChargeRadius
            : 0f;
        public float ActiveChargeRadius => activeChargeRadius;
        public bool IsAtChargeFullAim => chargeFullAimTimer > 0f;
        public float ChargeFullAimProgress => settings.chargeFullAimTime > 0.0001f
            ? Mathf.Clamp01(chargeFullAimTimer / settings.chargeFullAimTime)
            : (chargeFullAimTimer > 0f ? 1f : 0f);
        public float ChargingLaunchSpeed => LaunchPowerForRatio(
            activeChargeRadius > 0.0001f ? aimOffset.magnitude / settings.chargeRadius : 0f,
            activeChargeRadius > settings.chargeRadius);

        // Maps a normalized aim radius (aimOffset.magnitude / chargeRadius) to launch power.
        // Energy-conversion launches (activeChargeRadius expanded past chargeRadius) start the
        // curve from zero; regular launches start at minLaunchPower so a 0%-aim launch still moves.
        private float LaunchPowerForRatio(float ratio, bool isEnergyConversion)
        {
            float curved = Mathf.Pow(ratio, settings.chargeCurveExponent);
            return isEnergyConversion
                ? curved * settings.baseLaunchPower
                : settings.minLaunchPower + curved * (settings.baseLaunchPower - settings.minLaunchPower);
        }

        // Judgment circle charge state (aimOffset = relative to slime center)
        private Vector2 aimOffset;
        private Vector2 driftDirection;

        // Post-launch input ignore
        private float postLaunchTimer;

        // Post-charged-launch air control lockout (not set by ForceAirborneWithVelocity)
        private float postLaunchAirControlLockTimer;

        // Launch surface ignore
        private IgnoredColliderList ignoredColliderList;

        // --- Gravity Field ---
        // LIFO stack of active gravity zones. Empty = world gravity.
        private readonly List<GravityField> gravityFieldStack = new List<GravityField>();
        // Computed once per FixedUpdate, before state dispatch.
        private Vector2 gravityUp = Vector2.up;
        private Vector2 gravityRight = Vector2.right;

        public Vector2 GravityUp => gravityUp;
        public Vector2 GravityRight => gravityRight;
        public Vector2 GravityVector => -gravityUp * Mathf.Abs(settings.gravity);

        public void PushGravityField(GravityField field)
        {
            if (!gravityFieldStack.Contains(field))
                gravityFieldStack.Add(field);
        }
        public void PopGravityField(GravityField field) { gravityFieldStack.Remove(field); }

        // Manual velocity
        private Vector2 velocity;
        // Accumulated velocity delta from external sources (wind, conveyor, etc.).
        // Consumed and zeroed each FixedUpdate. Callers supply acceleration*dt.
        private Vector2 externalDeltaV;
        // Signed scalar along surfaceTangent; negative = against tangent direction
        private float impactTangentSpeed;
        private float impactNormalSpeed;   // platform-relative perpendicular impact speed, always ≥ 0
        private float stickDeccelTimer;
        private Vector2 prevSurfaceNormal;
        private SurfaceAnchorInfo lastSurfaceAnchorInfo;
        private bool lastSurfaceAnchorProviderConsumedCarry;
        private readonly List<MonoBehaviour> surfaceAnchorProviderScratch = new List<MonoBehaviour>(8);

        // Persistent crawl speed (accel/decel model)
        private float crawlVelocity;

        // Persistent inertial slide speed along surfaceTangent. Only used while attached
        // to a SurfaceTag whose material has useInertialSlide = true; otherwise stays 0
        // and the legacy steady-state slide formula is used. Integrates gravAlongTangent
        // as acceleration, and slideResistance acts as kinetic friction (m/s²).
        private float slideVelocity;

        // Active charge radius (expanded on energy conversion)
        private float activeChargeRadius;

        // Full-charge aim grace timer
        private float chargeFullAimTimer;

        // Energy-conversion coyote: on no-Space landing, the spawn distance that
        // a Space-held landing would have granted is stashed here. If the player
        // presses Space before energyCoyoteTimer expires, HandleSurfaceChargeEntry
        // consumes these values instead of using the default chargeRadius.
        private float energyCoyoteTimer;
        // Duration energyCoyoteTimer was armed with (per-surface override applies).
        // Visuals (SlimeVisual.ComputeCoyoteSquash) read this to normalize progress so
        // a longer surface-overridden window doesn't drive the squash curve out of range.
        private float energyCoyoteDuration;
        private float pendingEnergyChargeRadius;
        // True when the deferred floor conversionCount=0 reset is owed on pending expiry.
        // The reset is deferred because consuming pending continues the chain (matching
        // the Space-held branch, which doesn't reset conversionCount on floor landings).
        private bool pendingFloorReset;

        // Stick timer (delay before slide starts)
        private float stickTimer;
        private bool wasStuck;

        // Charge input buffer (press-just-before-landing grace)
        private float chargeBufferTimer;

        // Hyper dash: crawl captured at charge entry (immune to charging decel)
        private float chargeEntryCrawl;

        // Slide ramp-up timer (gradual slide acceleration after landing)
        private float slideRampTimer;
        private int conversionCount;

        // Energy-based conversion normalization (stored at launch)
        private float launchSpeed;
        private Vector2 launchPosition;

        // Pre-launch slide+crawl tangent speed captured at Launch(). Consumed by ComputeLandingCrawlVelocity
        // as the "free zone" baseline for landingExcessGain on charged-launch returns. launchOriginValid is
        // cleared on the first landing so the cap fires once per flight.
        // launchOriginAimPower is the aim-only launch magnitude (excluding hyperDash inheritance) so the
        // baseline preserves both pre-launch slide AND the player-chosen launch energy: a stationary launch
        // does not collapse to zero, and a fast slide does not get clamped down to aim power.
        private float launchOriginSlideV;
        private float launchOriginAimPower;
        private bool launchOriginValid;

        // Charging coyote time (grace period after leaving surface)
        private float coyoteTimer;
        private Vector2 lastSurfaceNormal;
        private Vector2 lastSurfaceTangent = Vector2.right;

        // Surface contact info
        private Vector2 surfaceNormal;
        private Vector2 surfaceTangent;
        private Collider2D surfaceCollider;
        private Collider2D prevSurfaceCollider;
        private bool isTouchingSurface;

        // Surface probe hysteresis (absorb 1-frame probe misses in Charging)
        private int surfaceProbeMissFrames;
        private Collider2D cachedSurfaceCollider;

        // Surface normal low-pass filter (rotating platforms): damps EffectiveNormal
        // jitter when contact faces flicker in/out of active range as a wheel rotates.
        private Vector2 prevSmoothedSurfaceNormal;
        private bool hasPrevSmoothedNormal;
        // Previous frame's surfaceTangent — used by ComputeArcCompensatedTangent to
        // estimate angular velocity ω of the smoothed tangent and aim each step at
        // the chord midpoint of the predicted arc. Reset implicitly on Airborne→
        // Attached transition (hasPrevSmoothedNormal goes false→true → first frame
        // skips compensation, second frame onwards has a valid prev).
        private Vector2 prevSmoothedSurfaceTangent;
        // Snapshot of prev-tangent state at start of current FixedUpdate, captured before
        // the prev fields are overwritten. Read by ComputeArcCompensatedTangent in the
        // state handlers (Attached/Charging) which run later in the same FixedUpdate.
        private Vector2 arcPrevTangent;
        private bool arcHasPrevTangent;

        // Friction-based detach state. ω = |Δθ|/dt measured between arcPrevTangent and
        // surfaceTangent each FU; v·ω compared against (gravityHelp + cornerAdhesionAccel).
        // sameSignFrames is the curve-sustained counter (only counted when convex,
        // i.e. tangent rotation sign matches tangentSpeed sign — concave wall rejects).
        // smoothedCornerStressRatio is the LP-filtered, sameSignFrames-gated visual exposure.
        private int sameSignFrames;
        private float cornerStressRatio;
        private float smoothedCornerStressRatio;

        // Attach debounce: prevent rapid Attached↔Airborne oscillation at corners/edges
        private int attachToggleCount;
        private float lastAttachToggleTime;
        private float attachLockoutTimer;

        // --- Stunned state ---
        private float stunTimer;
        private DamageType lastDamageType;
        public bool IsStunned => currentState == SlimeState.Stunned;
        public bool IsCharging => currentState == SlimeState.Charging;
        public float StunTimer => stunTimer;
        public DamageType LastDamageType => lastDamageType;

        // --- Platform effect ---
        private IPlatformEffect activePlatformEffect;

        // --- Environmental zone flags ---
        // Set by zones (e.g. WaterZone) to block landing while inside.
        // Counter-based: multiple overlapping zones stack correctly.
        private int blockAttachCount;
        public bool BlockAttach => blockAttachCount > 0;
        public void PushBlockAttach() { blockAttachCount++; }
        public void PopBlockAttach() { blockAttachCount = Mathf.Max(0, blockAttachCount - 1); }

        // Set by fluid zones (water, future honey/viscous fluids) where the slime is
        // suspended in a medium. Grants unconditional air control — apex-threshold lockout
        // doesn't apply because vertical motion is driven by buoyancy, not a launch.
        private int inFluidCount;
        public bool InFluid => inFluidCount > 0;
        public void PushInFluid() { inFluidCount++; }
        public void PopInFluid() { inFluidCount = Mathf.Max(0, inFluidCount - 1); }

        private SpriteRenderer spriteRenderer;

        private SurfaceProbe surfaceProbe;

        // CCD scratch for the Attached/Charging position-teleport sweep.
        private readonly RaycastHit2D[] ccdHitBuffer = new RaycastHit2D[8];

        // --- Landing event (for VFX, sound, etc.) ---
        public event System.Action<Vector2, Vector2> OnLanded; // (contactPoint, surfaceNormal)

        // --- Launch event (for sound, etc.) ---
        // Fires from player-intentional launches only: Launch() and Hop(). External-force paths
        // (JumpPad, Catapult) go through ForceAirborneWithVelocity and should raise their own cue.
        public event System.Action<Vector2, Vector2, bool> OnLaunch; // (position, velocity, wasHop)

        // --- Friction-based detach event ---
        // Fires when the slime is detached by centrifugal/inertial limit (NOT by ceiling
        // angle / contact loss). Payload: (detachVelocity, surfaceNormal at detach,
        // overshoot = ratio - 1). OnLaunch is also raised in the same call for audio
        // and ignoredColliders bookkeeping; subscribe here for stress-specific reactions
        // (pong overlay, dust burst).
        public event System.Action<Vector2, Vector2, float> OnFrictionDetach;

        /// <summary>LP-filtered + sameSignFrames-gated stress ratio (= required / available)
        /// driven by FixedUpdateAttached. 0 when toggle off, in transient frame, or convex
        /// sign hasn't held for at least one frame. Read by SlimeVisual for Phase 1 stretch.</summary>
        public float CornerStressRatio => smoothedCornerStressRatio;

        /// <summary>Signed tangent-direction speed snapshot (crawlVelocity + slideVelocity).
        /// Read by SlimeVisual to align the shader's frontMask with motion.</summary>
        public float TangentSpeed => crawlVelocity + slideVelocity;

        /// <summary>Last-frame friction-detach diagnostic snapshot. Updated each
        /// FixedUpdateAttached for debug UIs. Pure pull — debug consumers never write back.</summary>
        public FrictionDetachDebug FrictionDebug => frictionDebug;
        private FrictionDetachDebug frictionDebug;

        public readonly struct FrictionDetachDebug
        {
            public readonly bool toggleOn;
            public readonly bool inAttached;
            public readonly bool transientFrame;
            public readonly bool transientNoPrevTangent;
            public readonly bool transientColliderChanged;
            public readonly bool transientStickActive;
            public readonly bool transientBelowMinSpeed;
            public readonly float tangentSignedSpeed;
            public readonly float dThetaDeg;
            public readonly float omega;
            public readonly float required;
            public readonly float gravityHelp;
            public readonly float available;
            public readonly float rawRatio;
            public readonly bool convex;
            public readonly int sameSignFrames;
            public readonly bool detachFiredThisFrame;

            public FrictionDetachDebug(
                bool toggleOn, bool inAttached, bool transientFrame,
                bool tNoPrev, bool tColliderChanged, bool tStick, bool tBelowMinSpeed,
                float tangentSignedSpeed, float dThetaDeg, float omega,
                float required, float gravityHelp, float available, float rawRatio,
                bool convex, int sameSignFrames, bool detachFiredThisFrame)
            {
                this.toggleOn = toggleOn;
                this.inAttached = inAttached;
                this.transientFrame = transientFrame;
                this.transientNoPrevTangent = tNoPrev;
                this.transientColliderChanged = tColliderChanged;
                this.transientStickActive = tStick;
                this.transientBelowMinSpeed = tBelowMinSpeed;
                this.tangentSignedSpeed = tangentSignedSpeed;
                this.dThetaDeg = dThetaDeg;
                this.omega = omega;
                this.required = required;
                this.gravityHelp = gravityHelp;
                this.available = available;
                this.rawRatio = rawRatio;
                this.convex = convex;
                this.sameSignFrames = sameSignFrames;
                this.detachFiredThisFrame = detachFiredThisFrame;
            }
        }

        // Internal write helper used by Attached's friction-detach block.
        internal void SetFrictionDebug(in FrictionDetachDebug snap) => frictionDebug = snap;

        private void Awake()
        {
            Debug.Assert(interactionDetector != null, "[SlimeController] InteractionDetector is required on Slime prefab");
            rb = GetComponent<Rigidbody2D>();
            circleCollider = GetComponent<CircleCollider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();

            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            SyncRadius();
            ignoredColliderList = new IgnoredColliderList(settings.maxIgnoreTime, circleCollider);
            surfaceProbe = CreateProbe(settings.surfaceProbeMode);
            currentProbeMode = settings.surfaceProbeMode;
            launchPosition = rb.position;
        }

        private SurfaceProbe CreateProbe(SurfaceProbeMode mode)
        {
            return mode == SurfaceProbeMode.Multi
                ? (SurfaceProbe)new MultiContactSurfaceProbe(rb, settings, ignoredColliderList.Colliders)
                : new SingleContactSurfaceProbe(rb, settings, ignoredColliderList.Colliders);
        }

        private SurfaceProbeMode currentProbeMode;
        public SurfaceProbeMode CurrentProbeMode => currentProbeMode;

        /// <summary>
        /// Swap the active surface probe at runtime. lastGoodNormal/gravity context
        /// carries over so a mode switch while attached to a ceiling/wall doesn't
        /// snap the slime off. The transient contact buffer is refilled on the
        /// next FixedUpdate.
        /// </summary>
        public void SetProbeMode(SurfaceProbeMode mode)
        {
            if (mode == currentProbeMode) return;
            var newProbe = CreateProbe(mode);
            newProbe.TransferStateFrom(surfaceProbe);
            surfaceProbe = newProbe;
            currentProbeMode = mode;
        }

        private void OnValidate()
        {
            if (settings != null)
                SyncRadius();
        }

        private void SyncRadius()
        {
            if (settings == null) return;

            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && spriteRenderer.sprite != null)
            {
                spriteRenderer.drawMode = SpriteDrawMode.Simple;
                float nativeDiameter = spriteRenderer.sprite.bounds.size.x;
                float scale = (settings.slimeRadius * 2f) / nativeDiameter;
                transform.localScale = new Vector3(scale, scale, scale);
            }

            if (circleCollider == null) circleCollider = GetComponent<CircleCollider2D>();
            if (circleCollider != null)
            {
                float s = transform.localScale.x;
                float r = settings.slimeRadius * bodyScaleRatio;
                circleCollider.radius = s > 0.001f ? r / s : r;
            }
        }

        public static event System.Action<SlimeController> OnSpawned;
        public static event System.Action<SlimeController> OnDespawned;

        // Editor domain reload preserves static fields by default. ResetStatics nulls them
        // before the next play session so a forgotten unsubscribe in some other system can't
        // accumulate stale subscribers across Play→Stop→Play cycles. Mirrors the project
        // pattern in SoundService / WorldTrailManager / TrailColliderRegistry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            OnSpawned = null;
            OnDespawned = null;
        }

        private void OnEnable()
        {
            inputReader.OnJumpPressed += HandleJumpPressed;
            inputReader.OnJumpReleased += HandleJumpReleased;
            inputReader.OnInteractPressed += HandleInteractPressed;
            OnSpawned?.Invoke(this);
        }

        private void OnDisable()
        {
            inputReader.OnJumpPressed -= HandleJumpPressed;
            inputReader.OnJumpReleased -= HandleJumpReleased;
            inputReader.OnInteractPressed -= HandleInteractPressed;
            OnDespawned?.Invoke(this);
        }

        private void Update()
        {
            if (_override != null)
            {
                _override.ExternalUpdate(this);
                return;
            }

            if (chargeBufferTimer > 0f) chargeBufferTimer -= Time.deltaTime;

            if (postLaunchTimer > 0f)
                postLaunchTimer -= Time.deltaTime;
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            if (_override != null)
            {
                _override.ExternalFixedUpdate(this, dt);
                rb.linearVelocity = velocity;
                return;
            }

            UpdateGravityDirection(dt);

            ignoredColliderList.Tick(dt, rb.position, SlimeRadius, settings.launchClearanceThreshold);

            // Probe-mode decision must happen BEFORE CheckSurface — the new probe
            // populates contacts during CheckSurface, and a post-check swap would
            // discard those (TransferStateFrom only carries lastGoodNormal). On a
            // landing frame that swap-too-late path leaves the state handler with
            // empty contacts, ProjectVelocityAgainstContacts no-ops, slide gravity
            // teleports the slime past a thin edge before Phase A can catch it.
            DecideProbeMode();

            // Rope position-lock (pre-probe). When still bound to a rope from the
            // previous FU and the rope-rider channel was active, snap rb.position
            // onto the rope's current nearest surface point BEFORE CheckSurface
            // runs. Why pre-probe: the rope's spring rebound can move the contact
            // node by more than (slimeRadius + contactSkin) per FU, which leaves
            // the slime out of overlap range — the probe then reports no contact,
            // FixedUpdateAttached transitions to Airborne, and the carry channel
            // (which would have tracked the slime to the rope's new position) is
            // gated on state ∈ {Attached, Charging} so it never fires that frame.
            // Result: visible attach/airborne oscillation. Locking pre-probe keeps
            // the slime stuck to the rope's surface so the probe finds contact and
            // the state machine stays consistent.
            if (currentRope != null
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
                && currentRope.Edge != null)
                LockRopePosition();

            // Bamboo tip-grab position lock — mirror of LockRopePosition. Must run pre-probe
            // so UpdateSurfaceContact's CheckSurface finds the tip segment as primary
            // contact even after the bamboo has bent away from where the slime was last frame.
            if (currentBambooGrab != null
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
                LockBambooGrabPosition();

            UpdateSurfaceContact(dt);

            // Refresh grab handle after surface info is current. Sticky after entry — the
            // handle is only cleared in ChangeState when exiting Attached/Charging.
            UpdateBambooGrabReference();

            switch (currentState)
            {
                case SlimeState.Attached:
                    FixedUpdateAttached(dt);
                    break;
                case SlimeState.Charging:
                    FixedUpdateCharging(dt);
                    break;
                case SlimeState.Airborne:
                    FixedUpdateAirborne(dt);
                    break;
                case SlimeState.Stunned:
                    FixedUpdateStunned(dt);
                    break;
            }

            // Snow drag — exponential velocity decay while sliding on snow so the
            // slime actually stops in deep coverage. Drag-only (no continuous carve,
            // no airborne probe). Runs after the state handler so it operates on
            // the velocity / slideVelocity / crawlVelocity values the state just
            // set, before finalVel is composed in OutputVelocity.
            ApplySnowDrag(dt);

            // Rope rider contact — fires while attached OR charging so the rope keeps
            // sagging under slime weight during charge. The stored displacement is
            // what Launch() reads via GetMaxSagDistance() to add rebound speed.
            // Rope consumes the buffer on its next FixedUpdate (execution order -100),
            // giving the designed 1-frame lag.
            if (currentRope != null
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
                currentRope.ReportRiderContact(surfaceCollider, rb.position, rb.mass, velocity);

            ApplyExternalDeltaV();

            OutputVelocity(dt);

            // Pinch hold/extrude OWNS the slime's position last — after OutputVelocity (and its
            // Phase A depenetration) so it overrides the lateral squirt. See SlimeController.Pinch.cs.
            UpdatePinch(dt);
        }

        // Computes the effective gravity direction (gravityUp/gravityRight) from the
        // LIFO gravity-field stack (last entered zone wins). For radial zones, evaluate
        // at midpoint (pos + vel*dt/2) for second-order accuracy — prevents systematic
        // energy drift from forward Euler.
        private void UpdateGravityDirection(float dt)
        {
            if (gravityFieldStack.Count > 0)
            {
                var topField = gravityFieldStack[gravityFieldStack.Count - 1];
                Vector2 samplePos = rb.position + velocity * (dt * 0.5f);
                Vector2 newUp = -topField.GetDownDirection(samplePos);
                if (newUp.sqrMagnitude < 0.0001f)
                    gravityUp = Vector2.up; // NaN guard: designer error (pivot reachable)
                else
                    gravityUp = newUp.normalized;
            }
            else
            {
                gravityUp = Vector2.up;
            }
            gravityRight = new Vector2(gravityUp.y, -gravityUp.x);
        }

        // Runs the surface probe, low-pass-filters the contact normal, derives the
        // tangent (+ arc-compensation snapshot), and refreshes carrier / surfaceMod /
        // rope binding when the touched collider changes. On exit, surfaceNormal /
        // surfaceTangent / surfaceCollider / isTouchingSurface are ready for the state handler.
        private void UpdateSurfaceContact(float dt)
        {
            float surfaceMultiplier = (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
                ? settings.attachedCheckMultiplier : settings.airborneCheckMultiplier;
            // While tip-grabbed, A/D is rerouted to bamboo tilt torque and zeroed for the
            // surface probe (otherwise the probe would steer toward a phantom climb direction
            // along the cap). Aim during Charging is unaffected — it reads inputReader.AimInput,
            // a separate channel.
            float rawInputX = inputReader.LastInputMove.x;
            if (currentBambooGrab != null)
                TryRouteBambooTiltInput(rawInputX);
            float inputX = (currentBambooGrab != null || currentState == SlimeState.Charging)
                ? 0f : rawInputX;
            Vector2 primaryNormal;
            isTouchingSurface = surfaceProbe.CheckSurface(out primaryNormal, out surfaceCollider, surfaceMultiplier, inputX, surfaceCollider, gravityUp, gravityRight);

            // Aggregate normal for slide/crawl/aim/impact continuity. `surfaceCollider`
            // still carries primary identity for carrier/surfaceMod/effect attribution.
            Vector2 rawSurfaceNormal = isTouchingSurface ? surfaceProbe.EffectiveNormal : gravityUp;

            // Low-pass filter surfaceNormal. EffectiveNormal flickers on:
            //   (a) fast-rotating platforms — contact faces flicker as the wheel turns
            //   (b) static curved surfaces at high tangent speed — multi-probe active set
            //       churns (aC=1↔3↔0 between frames), which swings the weighted average
            //       normal by 10°+ per frame. Without filtering this swing transfers to
            //       velocity = surfaceTangent * speed, producing rb.position judder
            //       (visible especially on Ice / low-decel materials).
            // Always smoothing once attached fixes (b). Rotating platforms can opt into
            // a stronger rate via surfaceNormalSmoothRotatingMultiplier.
            float smoothRate = settings.surfaceNormalSmoothRate;
            if (smoothRate > 0.001f && IsOnFastRotatingSurface())
                smoothRate *= settings.surfaceNormalSmoothRotatingMultiplier;
            bool smoothingActive = isTouchingSurface && smoothRate > 0.001f;

            if (smoothingActive && hasPrevSmoothedNormal)
            {
                float k = 1f - Mathf.Exp(-smoothRate * dt);
                Vector2 mixed = Vector2.Lerp(prevSmoothedSurfaceNormal, rawSurfaceNormal, k);
                surfaceNormal = mixed.sqrMagnitude > 1e-6f ? mixed.normalized : rawSurfaceNormal;
            }
            else
            {
                surfaceNormal = rawSurfaceNormal;
            }
            prevSmoothedSurfaceNormal = surfaceNormal;
            // Capture tangent for next-frame arc compensation. Note: hasPrevSmoothedNormal
            // here still reflects the PREVIOUS frame's touch state (set at line below);
            // updated to current state right after, so consumers reading it later in
            // this same FixedUpdate see "did we have a valid prev tangent at frame start".
            Vector2 lastFrameTangent = prevSmoothedSurfaceTangent;
            bool hadPrevTangent = hasPrevSmoothedNormal;
            hasPrevSmoothedNormal = isTouchingSurface;

            surfaceTangent = new Vector2(-surfaceNormal.y, surfaceNormal.x);
            prevSmoothedSurfaceTangent = surfaceTangent;
            // Stash for ComputeArcCompensatedTangent (read by Attached/Charging handlers)
            arcPrevTangent = lastFrameTangent;
            arcHasPrevTangent = hadPrevTangent;

            bool surfaceHostChanged = HasSurfaceCarrierHostChanged(surfaceCollider, prevSurfaceCollider);

            if (isTouchingSurface && surfaceHostChanged
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
            {
                if (carrier != null) carrier.Attach(surfaceCollider);
                UpdateSurfaceModifiers(surfaceCollider);
            }
            else if (isTouchingSurface && (hasCachedSnowMods || hasOverrideMods)
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
            {
                // Same host as last frame, but either the host has snow on it OR a zone has
                // pushed a SurfaceMaterial override — re-blend so carve/recovery progress
                // flows into slide gravity dynamically AND override stays applied per-FU.
                // Skipped on the surface-change frame because UpdateSurfaceModifiers already
                // called RefreshSnowBlend with the fresh host mods.
                RefreshSnowBlend();
            }

            // Rope reference tracks the touched surface regardless of state, so the
            // Airborne→Attached landing frame (carrier.Attach happens inside the
            // Airborne handler, not here) correctly registers the rope too.
            // ReportRiderContact itself is gated on state==Attached.
            if (surfaceCollider != prevSurfaceCollider)
                UpdateRopeReference(surfaceCollider);

            prevSurfaceCollider = surfaceCollider;
        }

        // External forces (wind, conveyor, etc.). Additive in Airborne/Stunned;
        // projected onto surfaceTangent while attached so the state handler's
        // per-frame `velocity = tangent * speed` overwrite doesn't wipe it.
        private void ApplyExternalDeltaV()
        {
            // Tip-grabbed: drain the accumulator without applying it. Otherwise the
            // residual writes to `velocity` are read by PlatformCarrier next FU (carrier
            // pre-tick reads slime.Velocity) and fed into the bamboo's impact-pump dv
            // even though OutputVelocity zeros rb.linearVelocity for grab anchoring.
            if (currentBambooGrab != null)
            {
                externalDeltaV = Vector2.zero;
                return;
            }

            if (externalDeltaV.sqrMagnitude > 0.0001f)
            {
                if (currentState == SlimeState.Airborne || currentState == SlimeState.Stunned)
                    velocity += externalDeltaV;
                else
                    velocity += surfaceTangent * Vector2.Dot(externalDeltaV, surfaceTangent);
                externalDeltaV = Vector2.zero;
            }
        }

        // Composes the final velocity (state velocity + rope sample + platform carry)
        // and writes it to the body. Two movement models, selected by probe mode:
        //
        //   Multi  → teleport-then-snap. Letting Unity integrate `linearVelocity * dt`
        //            and run its contact solver applies per-frame position corrections
        //            at facet boundaries we never see in SnapToSurface (visible as
        //            crawl jitter on faceted polygon terrain). This is the ice-tremor
        //            fix. Trade-off: bypasses Unity's CCD + contact solver, so moving
        //            thin colliders that *are* the slime's surface (rotating SpringyBranch)
        //            can phase the slime through — hence the manual CCD sweep below.
        //
        //   Single → legacy `rb.linearVelocity = finalVel`. Unity-native physics with
        //            CCD enabled handles moving thin colliders correctly. Used per-platform
        //            via SurfaceProbeModeOverride. linearVelocity is *not* zeroed in this
        //            branch — Unity drives movement from it.
        //
        // Airborne/Stunned always use the physics-driven path so projectile motion stays
        // Unity-native.
        private void OutputVelocity(float dt)
        {
            lastSurfaceAnchorProviderConsumedCarry = false;
            lastSurfaceAnchorInfo = default;

            // Tip-grab owns rb.position via LockBambooGrabPosition (pre-probe). Bypass the
            // velocity-driven motion path entirely so external deltaV (ApplyExternalDeltaV)
            // and rope/carrier velocity contributions can't drift the slime off the tip
            // between LockBambooGrabPosition refreshes. The bamboo itself isn't a translating
            // platform (kinematic base pivot is fixed), so ApplyPositionCarry would be a
            // no-op anyway; skipping it is safe.
            if (currentBambooGrab != null)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }

            Vector2 platformVel = (carrier != null && carrier.IsOnPlatform)
                ? carrier.GetEffectiveVelocity() : Vector2.zero;
            float frictionAngle = isTouchingSurface ? GetSurfaceAngle() : 0f;

            // Rope isn't a PlatformMotor, so PlatformCarrier's motor-based velocity
            // path returns zero for it. Sample rope-local velocity at the contact
            // point and inherit it directly — otherwise the slime lands, weighs the
            // rope down via ReportRiderContact, but the rope's collider sags out
            // from under the slime while the slime sits at the old y, leaving it
            // hanging in mid-air above the rope.
            // Capped by rope.MaxReboundSpeed so a wildly whipping rope can't fling
            // the slime at arbitrary speeds.
            Vector2 ropeVel = Vector2.zero;
            if (currentRope != null && isTouchingSurface
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
            {
                ropeVel = currentRope.SampleVelocityAt(rb.position);
                float cap = currentRope.MaxReboundSpeed;
                if (cap > 0f && ropeVel.sqrMagnitude > cap * cap)
                    ropeVel = ropeVel.normalized * cap;
            }

            // Rope rider carry — V3-only positional coupling. v1/v2 return zero;
            // V3 returns the world-space displacement the rider's contact segment
            // underwent this FixedUpdate. Applied directly to rb.position so the
            // slime travels through the dip-and-rebound cycle with the rope rather
            // than letting the rope sag out from under it (the failure mode that
            // detached the slime mid-landing on V2). Assumes Multi probe mode —
            // the teleport branch below zeroes rb.linearVelocity, so carry is the
            // sole position channel and doesn't double-count with ropeVel.
            if (currentRope != null && isTouchingSurface
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
            {
                Vector2 carry = currentRope.ConsumeRiderCarryDelta(rb.position);
                if (carry.sqrMagnitude > 0f)
                    rb.position += carry;
            }

            bool useTeleport = (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
                && currentProbeMode == SurfaceProbeMode.Multi;

            float carrierFrictionScale = surfaceMod.carrierFriction.Evaluate(frictionAngle);

            // Platform carry channel selection:
            //   useTeleport: SnapToSurface's ClosestPoint anchors to the platform's
            //     *pre-MovePosition* collider (kinematic MovePosition is deferred to
            //     Physics.Simulate, so within this FixedUpdate the platform collider is
            //     still at its old position). Baking platformVel into finalVel here gets
            //     undone by that snap. Apply carry via ApplyPositionCarry *after* the
            //     snap instead — the slime's new position is then consistent with the
            //     platform's new pose once physics simulates.
            //   non-teleport: Unity's contact solver carries the slime natively when
            //     linearVelocity includes platformVel.
            Vector2 finalVel = velocity + ropeVel;
            if (!useTeleport) finalVel += platformVel * carrierFrictionScale;

            if (useTeleport)
            {
                // CCD against thin moving colliders (SpringyBranch EdgeCollider2D) that
                // teleport-by-rb.position would otherwise skip past. Cast only when the
                // step is non-trivial and clamp to the first non-ignored hit.
                Vector2 delta = finalVel * dt;
                Vector2 posBeforeTeleport = rb.position;
                float dist = delta.magnitude;
                if (dist > 1e-5f)
                {
                    int hits = Physics2D.CircleCastNonAlloc(rb.position, SlimeRadius,
                        delta / dist, ccdHitBuffer, dist, settings.groundLayer);
                    float clamped = dist;
                    for (int i = 0; i < hits; i++)
                    {
                        var h = ccdHitBuffer[i];
                        if (h.collider == null) continue;
                        if (ignoredColliderList.Colliders.Contains(h.collider)) continue;
                        if (h.collider == surfaceCollider) continue;
                        // Small-ledge lip: don't let the CCD pin the slime against the lip
                        // face — the forward slide needs to carry it in so Phase A's
                        // tangent-stripped radial push can lift it over (matches the clip
                        // skipped in ProjectVelocityAgainstContacts). Without this exemption
                        // the slime stalls flush against the lip with net displacement ~0.
                        if (surfaceProbe.IsLowLedgeCollider(h.collider)) continue;
                        if (h.distance < clamped) clamped = h.distance;
                    }
                    rb.position += (delta / dist) * Mathf.Max(0f, clamped - settings.contactSkin);
                }
                // Tip-grab owns rb.position via LockBambooGrabPosition — skip the probe's
                // snap so it doesn't tug the slime onto a non-tip segment mid-bend.
                if (currentBambooGrab == null)
                    SnapToSurface();

                // Position-based wedge drain for the small-ledge passthrough: when the
                // passthrough kept the forward slide alive (clip skipped) but SnapToSurface's
                // Phase A pushed the slime almost straight back, net displacement is ~0 even
                // though `velocity` is large — so the velocity-magnitude wedge drain in
                // FixedUpdateAttached never fires and slideVelocity keeps integrating, then
                // erupts as a phantom launch on the next charge. Detect the stall by net
                // displacement vs intended and drain here. Gated on a passthrough actually
                // having happened this frame so normal sliding/curve chords are untouched.
                if (surfaceMod.useInertialSlide && surfaceProbe.LowLedgePassthroughCount > 0)
                {
                    Vector2 netDelta = rb.position - posBeforeTeleport;
                    if (delta.sqrMagnitude > 1e-4f && netDelta.sqrMagnitude < delta.sqrMagnitude * 0.16f)
                        slideVelocity = 0f;
                }

                if (carrier != null && carrier.IsOnPlatform && !lastSurfaceAnchorProviderConsumedCarry)
                    carrier.ApplyPositionCarry(carrierFrictionScale);

                rb.linearVelocity = Vector2.zero;
            }
            else
            {
                rb.linearVelocity = finalVel;
            }
        }

        // Scratch for the per-frame probe-mode broadphase scan.
        private readonly Collider2D[] modeProbeBuffer = new Collider2D[8];

        // Probe-mode broadphase scan range. Independent from surfaceCheckDistance — that
        // governs *contact* detection (must hug the collider tightly), whereas this only
        // needs to find an override-bearing collider in the slime's neighborhood so the
        // mode swap happens well before contact. Wide = safe.
        private const float PROBE_MODE_SCAN_RADIUS = 4f;

        private void DecideProbeMode()
        {
            Vector2 pos = rb.position;
            int n = Physics2D.OverlapCircleNonAlloc(pos, PROBE_MODE_SCAN_RADIUS, modeProbeBuffer, settings.groundLayer);
            SurfaceProbeMode desired = settings.surfaceProbeMode;
            for (int i = 0; i < n; i++)
            {
                var c = modeProbeBuffer[i];
                if (c == null) continue;
                if (ignoredColliderList.Colliders.Contains(c)) continue;
                var ovr = c.GetComponentInParent<SurfaceProbeModeOverride>();
                if (ovr != null) { desired = ovr.mode; break; }
            }
            if (desired != currentProbeMode) SetProbeMode(desired);
        }

        private float GetSurfaceAngle()
        {
            return SurfaceProbe.GetSurfaceAngle(surfaceNormal, gravityUp);
        }

        /// <summary>
        /// Predicts the chord-midpoint of the arc the slime will trace this fixed step.
        /// Without this, `velocity = surfaceTangent * speed` writes the leading-edge
        /// tangent; Unity's interpolation then draws a chord that lies inside the arc
        /// by `R(1−cos(Δθ/2))` (~3–5 mm at the U-bottom of an Ice curve). Rotating the
        /// tangent by ω·dt/2 (ω estimated from prev-frame Δθ, constant-curvature) puts
        /// the velocity vector at the midpoint of the predicted arc, halving the visible
        /// chord-arc gap. Reset implicit on Attached entry (hasPrevSmoothedNormal goes
        /// false→true → arcHasPrevTangent is false on first frame).
        /// </summary>
        private Vector2 ComputeArcCompensatedTangent(Vector2 currentTangent)
        {
            if (!settings.useSurfaceTangentArcCompensation) return currentTangent;
            if (!arcHasPrevTangent) return currentTangent;
            if (arcPrevTangent.sqrMagnitude < 0.5f) return currentTangent;

            float cross = arcPrevTangent.x * currentTangent.y - arcPrevTangent.y * currentTangent.x;
            float dot = arcPrevTangent.x * currentTangent.x + arcPrevTangent.y * currentTangent.y;
            float dTheta = Mathf.Atan2(cross, dot);

            // Reject spurious large rotations (re-attach, surface flip, very sharp corner).
            // 30° per frame is well above any plausible smooth-curve curvature.
            const float MAX_DTHETA = 0.5236f;
            if (dTheta > MAX_DTHETA) dTheta = MAX_DTHETA;
            else if (dTheta < -MAX_DTHETA) dTheta = -MAX_DTHETA;

            float halfTheta = dTheta * 0.5f;
            float c = Mathf.Cos(halfTheta);
            float s = Mathf.Sin(halfTheta);
            return new Vector2(currentTangent.x * c - currentTangent.y * s,
                               currentTangent.x * s + currentTangent.y * c);
        }

        // Returns 1.0 on flat motion or zero accel; otherwise downhillGain when slideAccel pushes
        // the slime in its current travel direction, uphillBrake when it opposes. Lets a surface
        // tune descent speed-up and ascent slow-down independently.
        private float ResolveSlopeGain(float slideAccel, float slideVel, float angle)
        {
            if (Mathf.Abs(slideAccel) < 1e-4f || Mathf.Abs(slideVel) < 1e-4f) return 1f;
            return Mathf.Sign(slideAccel) == Mathf.Sign(slideVel)
                ? surfaceMod.slideDownhillGain.Evaluate(angle)
                : surfaceMod.slideUphillBrake.Evaluate(angle);
        }

        // Per-FixedUpdate gravity-driven slide speed along surfaceTangent. Shared by the
        // Attached and Charging state handlers (Charging passes gravityScale =
        // chargingSlideGravityScale; Attached passes 1).
        //   useInertialSlide ON  → gravity-along-tangent integrates into slideVelocity as
        //     acceleration; slideResistance is kinetic friction (m/s²); slideLinearDrag is
        //     an exponential drag; ResolveSlopeGain shapes descent/ascent asymmetry.
        //   useInertialSlide OFF → legacy steady-state: slideSpeed recomputed each frame from
        //     current tangent gravity, minus resistance, ramped up by slideRampUp after landing.
        // Always ticks slideRampTimer for state consistency.
        private float ComputeSlideSpeed(float angle, float dt, float gravityScale)
        {
            float gravAlongTangent = Vector2.Dot(GravityVector, surfaceTangent) * gravityScale;

            if (surfaceMod.useInertialSlide)
            {
                float slideAccel = gravAlongTangent * settings.slideGravityScale * surfaceMod.slideGravity.Evaluate(angle);
                slideAccel *= ResolveSlopeGain(slideAccel, slideVelocity, angle);
                slideVelocity += slideAccel * dt;
                float linearDrag = surfaceMod.slideLinearDrag.Evaluate(angle);
                if (linearDrag > 0f)
                    slideVelocity *= Mathf.Max(0f, 1f - linearDrag * dt);
                float frictionDecel = settings.slideResistance * surfaceMod.slideResistance.Evaluate(angle);
                if (frictionDecel > 0f)
                    slideVelocity = Mathf.MoveTowards(slideVelocity, 0f, frictionDecel * dt);
                slideRampTimer += dt;
                return slideVelocity;
            }

            if (Mathf.Abs(gravAlongTangent) < settings.slideThreshold)
                gravAlongTangent = 0f;
            float slideSpeed = gravAlongTangent * settings.slideGravityScale * surfaceMod.slideGravity.Evaluate(angle);
            // Resistance applies to slideSpeed only (not gravAlongTangent — crawl still fights full gravity)
            if (Mathf.Abs(slideSpeed) > 0.001f)
            {
                float sign = Mathf.Sign(slideSpeed);
                slideSpeed -= sign * settings.slideResistance * surfaceMod.slideResistance.Evaluate(angle);
                if (Mathf.Sign(slideSpeed) != sign)
                    slideSpeed = 0f;
            }
            slideRampTimer += dt;
            float surfaceRamp = surfaceMod.slideRampUp.Evaluate(angle);
            float rampUpTime = (surfaceRamp > 0f) ? surfaceRamp : settings.stickSlideTransitionRampTime;
            float rampT = (rampUpTime > 0f) ? Mathf.Clamp01(slideRampTimer / rampUpTime) : 1f;
            return slideSpeed * rampT;
        }

        // Stick→slide handoff, run at the top of the Attached / Charging surface handler:
        // when the stick timer just expired, reset the slide ramp and seed the active
        // tangent channel from the velocity the stick decel left behind; then re-arm
        // wasStuck while stick is still active.
        private void UpdateStickTransition()
        {
            if (wasStuck && stickTimer <= 0f)
            {
                slideRampTimer = 0f;
                wasStuck = false;
                float postStickTangent = Vector2.Dot(velocity, surfaceTangent);
                if (surfaceMod.useInertialSlide)
                {
                    slideVelocity = postStickTangent;
                    crawlVelocity = 0f;
                }
                else
                {
                    crawlVelocity = postStickTangent;
                }
            }
            if (stickTimer > 0f) wasStuck = true;
        }

        private Vector2 GetSurfaceVelocity(Collider2D col)
        {
            var motor = col != null ? col.GetComponentInParent<PlatformMotor>() : null;
            if (motor == null) return Vector2.zero;

            Vector2 vel = motor.Velocity;
            if (Mathf.Abs(motor.AngularVelocity) > 0.001f)
            {
                float radPerSec = motor.AngularVelocity * Mathf.Deg2Rad;
                Vector2 arm = rb.position - (Vector2)motor.transform.position;
                vel += new Vector2(-arm.y, arm.x) * radPerSec;
            }
            return vel;
        }

        private static PlatformMotor GetSurfacePlatformMotor(Collider2D col)
        {
            if (col == null) return null;
            return col.GetComponentInParent<PlatformMotor>();
        }

        private static bool HasSurfaceCarrierHostChanged(Collider2D current, Collider2D previous)
        {
            if (current == previous) return false;

            PlatformMotor currentMotor = GetSurfacePlatformMotor(current);
            PlatformMotor previousMotor = GetSurfacePlatformMotor(previous);
            if (currentMotor != null || previousMotor != null)
                return currentMotor != previousMotor;

            return true;
        }

        // True when the primary surface belongs to a platform rotating faster than the
        // configured threshold. Gate for surfaceNormal smoothing — static/linearly-moving
        // surfaces don't need filtering and would only lose responsiveness.
        private bool IsOnFastRotatingSurface()
        {
            if (surfaceCollider == null) return false;
            var motor = surfaceCollider.GetComponentInParent<PlatformMotor>();
            if (motor == null) return false;
            return Mathf.Abs(motor.AngularVelocity) > settings.surfaceNormalSmoothAngularThreshold;
        }

        private void SnapToSurface()
        {
            lastSurfaceAnchorProviderConsumedCarry = false;
            lastSurfaceAnchorInfo = default;

            // Tip-grab makes the cached tip position the source of truth — letting the
            // probe re-snap can pull the slime onto a different segment as the bamboo
            // bends, undoing LockBambooGrabPosition.
            if (currentBambooGrab != null) return;

            ISurfaceAnchorProvider provider = GetSurfaceAnchorProvider(surfaceCollider);
            if (provider != null
                && provider.TryGetSurfaceAnchor(surfaceCollider, rb.position, SlimeRadius,
                    out SurfaceAnchorInfo anchorInfo))
            {
                rb.position = anchorInfo.anchorWorldPos;
                surfaceNormal = anchorInfo.normalWorld;
                surfaceTangent = anchorInfo.tangentWorld.sqrMagnitude > 1e-8f
                    ? anchorInfo.tangentWorld.normalized
                    : new Vector2(-surfaceNormal.y, surfaceNormal.x);
                lastSurfaceAnchorInfo = anchorInfo;
                lastSurfaceAnchorProviderConsumedCarry = anchorInfo.consumesPlatformCarry;
                return;
            }
            surfaceProbe.SnapToSurface(surfaceCollider, surfaceNormal);
        }

        private ISurfaceAnchorProvider GetSurfaceAnchorProvider(Collider2D col)
        {
            if (col == null) return null;
            surfaceAnchorProviderScratch.Clear();
            col.GetComponentsInParent(false, surfaceAnchorProviderScratch);
            for (int i = 0; i < surfaceAnchorProviderScratch.Count; i++)
            {
                ISurfaceAnchorProvider provider = surfaceAnchorProviderScratch[i] as ISurfaceAnchorProvider;
                if (provider != null)
                {
                    surfaceAnchorProviderScratch.Clear();
                    return provider;
                }
            }
            surfaceAnchorProviderScratch.Clear();
            return null;
        }

        // Clears pending energy-conversion coyote state. Pass executeDeferredReset=true
        // when the pending is being discarded (timer expired, chain abandoned) so the
        // floor landing's deferred conversionCount=0 reset takes effect. Pass false
        // when consuming the pending (HandleSurfaceChargeEntry) or when the caller has
        // already reset conversionCount for other reasons (stun, teleport, state loss).
        private void ClearPendingEnergyConversion(bool executeDeferredReset)
        {
            if (executeDeferredReset && pendingFloorReset)
                conversionCount = 0;
            pendingEnergyChargeRadius = 0f;
            energyCoyoteTimer = 0f;
            energyCoyoteDuration = 0f;
            pendingFloorReset = false;
        }

        private void ChangeState(SlimeState newState, bool intentional = false)
        {
            if (currentState == newState) return;

            if (intentional)
                attachToggleCount = 0;
            else
                RegisterAttachToggle();

            // Friction-detach state must reset on entry to Attached or Charging.
            // Attached: new attach starts with sameSignFrames=0; the Attached check's
            //   transientFrame guard covers the first frame anyway, but keeping these
            //   in sync avoids stale stress ratio bleeding into the visual on rapid
            //   land/launch chains.
            // Charging: the friction check only runs in FixedUpdateAttached, so during
            //   Charging the smoothedCornerStressRatio LP filter never updates and the
            //   value freezes at whatever it was on the last Attached frame. Without
            //   this reset, the visual shader keeps showing the pre-charge banana-bend
            //   for the entire charge → launch sequence.
            if ((newState == SlimeState.Attached || newState == SlimeState.Charging)
                && currentState != newState)
            {
                sameSignFrames = 0;
                cornerStressRatio = 0f;
                smoothedCornerStressRatio = 0f;
            }

            // Tip-grab is bound to {Attached, Charging}. Any transition out of that pair
            // (Launch, charging cancel, stun, ...) drops the grab handle so the carrier
            // boost doesn't leak past the launching frame and so re-attach on a different
            // surface starts fresh. Carrier inheritance has already read VelocityTransferScale
            // by the time FinalizeAirborneExit invokes ChangeState, so the boost is preserved.
            if ((currentState == SlimeState.Attached || currentState == SlimeState.Charging)
                && newState != SlimeState.Attached && newState != SlimeState.Charging)
            {
                currentBambooGrab = null;
                ClearBambooV2Rebound();
            }

            currentState = newState;
        }

        private void RegisterAttachToggle()
        {
            if (Time.time - lastAttachToggleTime < settings.attachToggleWindow)
                attachToggleCount++;
            else
                attachToggleCount = 1;

            lastAttachToggleTime = Time.time;

            if (attachToggleCount >= settings.attachToggleLockoutThreshold)
            {
                attachLockoutTimer = settings.attachLockoutDuration;
                attachToggleCount = 0;
            }
        }

        // --- Multiplicative velocity channel ---

        /// <summary>
        /// Multiplicative velocity channel (drag, water resistance, rain slowdown).
        /// Parallel to AddExternalVelocity (additive channel).
        /// ApplyDragFactor remains as alias for DragZone compatibility.
        /// </summary>
        public void MultiplyVelocity(float factor) { velocity *= factor; }

        /// Resets the airborne energy reference to the current velocity and position.
        /// Call when an external event (water exit, etc.) constitutes a new launch for
        /// the purpose of energy-conversion on the next landing.
        public void ResetLaunchReference()
        {
            launchSpeed = velocity.magnitude;
            launchPosition = rb.position;
            conversionCount = 0;
            ClearPendingEnergyConversion(executeDeferredReset: false);
        }

        // --- Platform Effect ---

        // Unity pitfall: interface references to destroyed MonoBehaviours are not null
        // in the CLR sense, only via Unity's overloaded == operator. Cast to Object first.
        private bool IsPlatformEffectAlive =>
            activePlatformEffect != null && (activePlatformEffect as Object) != null;

        private void CheckPlatformEffect(Collider2D col)
        {
            var newEffect = col != null ? col.GetComponentInParent<IPlatformEffect>() : null;
            if (newEffect == activePlatformEffect) return;

            if (IsPlatformEffectAlive)
                activePlatformEffect.OnSlimeDetach(this);

            activePlatformEffect = newEffect;

            if (IsPlatformEffectAlive)
                activePlatformEffect.OnSlimeAttach(this);
        }

        private void ClearPlatformEffect()
        {
            if (IsPlatformEffectAlive)
                activePlatformEffect.OnSlimeDetach(this);
            activePlatformEffect = null;
        }

        /// <summary>
        /// Registers a collider as Passthrough — slime physically transits through it
        /// (Physics2D.IgnoreCollision) AND SurfaceProbe skips it. For hazard body
        /// pass-through (KnifeHazard etc.); not for launch clearance.
        /// </summary>
        public void RegisterIgnoredCollider(Collider2D col, float duration)
        {
            ignoredColliderList.RegisterSingle(col, duration, IgnoreMode.Passthrough);
        }

        public void UnregisterIgnoredCollider(Collider2D col)
        {
            ignoredColliderList.Unregister(col);
        }
    }
}
