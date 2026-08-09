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
        /// Pinch state for SlimeVisual (SlimeController.Pinch.cs): caught flag, gap squeeze 0..1 of
        /// diameter, world compression axis. 0 / false when not pinched.
        public bool PinchActive { get; private set; }
        public float PinchCompression01 { get; private set; }
        public Vector2 PinchAxis { get; private set; } = Vector2.up;
        /// Tangential component of the platform-relative impact velocity at the most recent landing.
        public float ImpactTangentSpeed => impactTangentSpeed;
        /// Normal component of the platform-relative impact velocity at the most recent landing. Always >= 0.
        public float ImpactNormalSpeed => impactNormalSpeed;
        public void AddExternalVelocity(Vector2 deltaV) { externalDeltaV += deltaV; }

        /// <summary>Sustained-flow push (water current). Separate from AddExternalVelocity because
        /// Attached/Charging overwrite `velocity` every frame — this integrates into the persistent
        /// slide/crawl channel instead. Caller passes the final per-frame deltaV.</summary>
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
        // Effective (body-scaled) radius — shrinks when cut, regrows while healing.
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

        // Energy-conversion launches start the curve from zero; regular launches start at
        // minLaunchPower so a 0%-aim launch still moves.
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
        public Vector2 GravityVector => -gravityUp * GravityMagnitude;

        public void PushGravityField(GravityField field)
        {
            if (!gravityFieldStack.Contains(field))
                gravityFieldStack.Add(field);
        }
        public void PopGravityField(GravityField field) { gravityFieldStack.Remove(field); }

        private Vector2 velocity;
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

        // Persistent inertial slide speed along surfaceTangent. Only used on materials with
        // useInertialSlide; otherwise stays 0 and the legacy steady-state formula is used.
        private float slideVelocity;

        // Active charge radius (expanded on energy conversion)
        private float activeChargeRadius;

        // Full-charge aim grace timer
        private float chargeFullAimTimer;

        // Energy-conversion coyote: on a no-Space landing the spawn distance a Space-held landing
        // would have granted is stashed here for HandleSurfaceChargeEntry to consume.
        private float energyCoyoteTimer;
        // Armed duration. SlimeVisual normalizes progress by this so a surface-overridden longer
        // window doesn't drive the squash curve out of range.
        private float energyCoyoteDuration;
        private float pendingEnergyChargeRadius;
        // Deferred floor conversionCount=0 reset, owed on pending expiry. Deferred because
        // consuming pending continues the chain (matching the Space-held branch).
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

        // Pre-launch tangent speed captured at Launch(): the "free zone" baseline for landingExcessGain,
        // cleared on the first landing so the cap fires once per flight. AimPower is aim-only (no
        // hyperDash) so a stationary launch doesn't collapse to zero nor a fast slide clamp to aim power.
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
        // One-shot latch: a ceiling contact spanning several frames raises OnCeilingBump once.
        private bool ceilingBumpLatched;

        // Surface probe hysteresis (absorb 1-frame probe misses in Charging)
        private int surfaceProbeMissFrames;
        private Collider2D cachedSurfaceCollider;

        // Surface normal low-pass filter (rotating platforms): damps EffectiveNormal
        // jitter when contact faces flicker in/out of active range as a wheel rotates.
        private Vector2 prevSmoothedSurfaceNormal;
        private bool hasPrevSmoothedNormal;
        // Previous frame's surfaceTangent, source for ComputeArcCompensatedTangent's ω estimate.
        // Reset implicitly on Airborne→Attached (first frame skips compensation).
        private Vector2 prevSmoothedSurfaceTangent;
        // Snapshot taken at the start of this FixedUpdate, before the prev fields are overwritten;
        // read later by the Attached/Charging handlers.
        private Vector2 arcPrevTangent;
        private bool arcHasPrevTangent;

        // Friction-based detach: v·ω vs (gravityHelp + cornerAdhesionAccel). sameSignFrames counts only
        // frames where dTheta and tangentSignedSpeed have opposite signs (CheckFrictionDetach's convex).
        private int sameSignFrames;
        private float cornerStressRatio;
        private float smoothedCornerStressRatio;

        // Attach debounce: prevent rapid Attached↔Airborne oscillation at corners/edges
        private int attachToggleCount;
        private float lastAttachToggleTime;
        private float attachLockoutTimer;

        // --- Stunned state ---
        private float stunTimer;
        // Total of the active stun, so the friction ramp can read progress from stunTimer.
        private float stunDuration;
        private DamageType lastDamageType;
        public bool IsStunned => currentState == SlimeState.Stunned;
        public bool IsCharging => currentState == SlimeState.Charging;
        public float StunTimer => stunTimer;
        public DamageType LastDamageType => lastDamageType;

        // --- Platform effect ---
        private IPlatformEffect activePlatformEffect;

        // --- Environmental zone flags ---
        // Set by zones (e.g. WaterZone) to block landing. Counter-based so overlapping zones stack.
        private int blockAttachCount;
        public bool BlockAttach => blockAttachCount > 0;
        public void PushBlockAttach() { blockAttachCount++; }
        public void PopBlockAttach() { blockAttachCount = Mathf.Max(0, blockAttachCount - 1); }

        // Fluid zones (water, viscous). Grants unconditional air control — the apex-threshold
        // lockout doesn't apply because vertical motion is buoyancy-driven, not a launch.
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

        // --- Ceiling bump event: head hit that never attaches, so OnLanded cannot cover it ---
        public event System.Action<Vector2, Vector2, float> OnCeilingBump; // (contactPoint, surfaceNormal, normalSpeed)

        // --- Launch event (for sound, etc.) ---
        // Player-intentional launches only (Launch/Hop). External-force paths go through
        // ForceAirborneWithVelocity and must raise their own cue.
        public event System.Action<Vector2, Vector2, bool> OnLaunch; // (position, velocity, wasHop)

        // --- Friction-based detach event ---
        // Fires on centrifugal/inertial detach only (not ceiling angle / contact loss).
        // Payload: (detachVelocity, surfaceNormal, overshoot = ratio - 1). OnLaunch also fires.
        public event System.Action<Vector2, Vector2, float> OnFrictionDetach;

        /// <summary>LP-filtered, sameSignFrames-gated corner stress ratio (required / available); 0 in transient frames.</summary>
        public float CornerStressRatio => smoothedCornerStressRatio;

        /// <summary>Signed tangent-direction speed (crawlVelocity + slideVelocity), read by SlimeVisual.</summary>
        public float TangentSpeed => crawlVelocity + slideVelocity;

        /// <summary>Friction-detach diagnostic snapshot. Pure pull — debug consumers never write back.</summary>
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
            // Lobes first: the ignore list captures the whole collider set, and a pair registered
            // later would leave the extra lobes colliding with what the base one passes through.
            CreateCutLobes();
            ignoredColliderList = new IgnoredColliderList(settings.maxIgnoreTime, bodyColliders);
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

        /// <summary>Swap the active surface probe at runtime; lastGoodNormal carries over so a
        /// mid-attach mode switch doesn't snap the slime off a ceiling/wall.</summary>
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
                // Play mode owns the collider through RebuildCutShape — writing a plain radius here
                // (OnValidate fires on any inspector click) would wipe a live cut shape.
                if (Application.isPlaying) { RebuildCutShape(); return; }
                float s = transform.localScale.x;
                float r = settings.slimeRadius * bodyScaleRatio;
                circleCollider.radius = s > 0.001f ? r / s : r;
            }
        }

        public static event System.Action<SlimeController> OnSpawned;
        public static event System.Action<SlimeController> OnDespawned;

        // Editor domain reload preserves static fields, so stale subscribers would accumulate
        // across Play→Stop→Play without this reset.
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

            // Must run BEFORE CheckSurface: the new probe fills contacts during CheckSurface and a
            // post-check swap discards them — on a landing frame that leaves the handler with empty
            // contacts and slide gravity teleports the slime past a thin edge.
            DecideProbeMode();

            // Rope lock must be pre-probe: rebound can move the contact node further than
            // (slimeRadius + contactSkin) per FU, the probe then reports no contact, Attached drops
            // to Airborne and the carry channel is state-gated off → visible attach/airborne oscillation.
            if (currentRope != null
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
                && currentRope.Edge != null)
                LockRopePosition();

            // Mirror of LockRopePosition — pre-probe so CheckSurface finds the tip segment even
            // after the bamboo bent away from last frame's position.
            if (currentBambooGrab != null
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
                LockBambooGrabPosition();

            // Physics clock, not the render clock the wound animates on: the probe and Box2D must
            // see one shape per step, not two or none.
            RebuildCutShape();

            UpdateSurfaceContact(dt);

            // Sticky after entry — the handle is only cleared in ChangeState on exiting Attached/Charging.
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

            // Snow drag — exponential velocity decay so the slime stops in deep coverage. Runs after
            // the state handler so it sees the velocity the state just set, before OutputVelocity.
            ApplySnowDrag(dt);

            // Fires while Attached OR Charging so the rope keeps sagging during charge. Rope consumes
            // the buffer on its next FixedUpdate (execution order -100) → designed 1-frame lag.
            if (currentRope != null
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
                currentRope.ReportRiderContact(surfaceCollider, rb.position, rb.mass, velocity);

            ApplyExternalDeltaV();

            OutputVelocity(dt);

            // Pinch owns the slime's position last — after OutputVelocity's Phase A depenetration
            // so it overrides the lateral squirt.
            UpdatePinch(dt);
        }

        // Effective gravity direction from the LIFO field stack (last entered wins). Radial zones
        // sample at the midpoint (pos + vel*dt/2) — forward Euler otherwise drifts energy.
        private void UpdateGravityDirection(float dt)
        {
            if (TryResolveGravityFieldV2(dt)) return; // V2 힘장이 방향·크기 모두 결정

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

        // Runs the probe, filters the contact normal, derives the tangent, and refreshes
        // carrier / surfaceMod / rope binding when the touched collider changes.
        private void UpdateSurfaceContact(float dt)
        {
            float surfaceMultiplier = (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
                ? settings.attachedCheckMultiplier : settings.airborneCheckMultiplier;
            // While tip-grabbed, A/D goes to bamboo tilt torque and is zeroed for the probe, else the
            // probe steers toward a phantom climb direction along the cap.
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

            // Low-pass the surface normal: EffectiveNormal swings 10°+/frame when the multi-probe
            // active set churns on curved surfaces, which transfers to velocity as rb.position judder.
            // Rotating platforms opt into a stronger rate via surfaceNormalSmoothRotatingMultiplier.
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
            // hasPrevSmoothedNormal here still reflects the PREVIOUS frame's touch state; it's
            // updated just below, so later readers in this same FixedUpdate see the frame-start value.
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
                // Same host, but snow or a zone override is present — re-blend each FU so carve/recovery
                // flows into slide gravity. Skipped on the change frame (UpdateSurfaceModifiers did it).
                RefreshSnowBlend();
            }

            // Tracks the touched surface regardless of state so the Airborne→Attached landing frame
            // registers the rope too; ReportRiderContact itself is state-gated.
            if (surfaceCollider != prevSurfaceCollider)
                UpdateRopeReference(surfaceCollider);

            prevSurfaceCollider = surfaceCollider;
        }

        // Additive in Airborne/Stunned; projected onto surfaceTangent while attached so the
        // handler's per-frame `velocity = tangent * speed` overwrite doesn't wipe it.
        private void ApplyExternalDeltaV()
        {
            // Tip-grabbed: drain without applying. Residual writes to `velocity` are read by
            // PlatformCarrier next FU and fed into the bamboo's impact-pump dv.
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

        // Composes final velocity (state + rope + platform carry) and writes it to the body.
        //   Multi  → teleport-then-snap: Unity's contact solver applies facet-boundary corrections
        //            we never see in SnapToSurface (crawl jitter on faceted terrain), but bypassing
        //            it also bypasses CCD — hence the manual sweep below.
        //   Single → rb.linearVelocity; Unity-native CCD handles moving thin colliders. Airborne/Stunned always use it.
        private void OutputVelocity(float dt)
        {
            lastSurfaceAnchorProviderConsumedCarry = false;
            lastSurfaceAnchorInfo = default;

            // Tip-grab owns rb.position via LockBambooGrabPosition — bypass the velocity path so
            // external deltaV and rope/carrier velocity can't drift the slime off the tip.
            if (currentBambooGrab != null)
            {
                rb.linearVelocity = Vector2.zero;
                return;
            }

            Vector2 platformVel = (carrier != null && carrier.IsOnPlatform)
                ? carrier.GetEffectiveVelocity() : Vector2.zero;
            float frictionAngle = isTouchingSurface ? GetSurfaceAngle() : 0f;

            // Rope isn't a PlatformMotor, so PlatformCarrier returns zero for it. Without sampling
            // rope-local velocity the rope sags out from under the slime, leaving it hanging mid-air.
            Vector2 ropeVel = Vector2.zero;
            if (currentRope != null && isTouchingSurface
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
            {
                ropeVel = currentRope.SampleVelocityAt(rb.position);
                float cap = currentRope.MaxReboundSpeed;
                if (cap > 0f && ropeVel.sqrMagnitude > cap * cap)
                    ropeVel = ropeVel.normalized * cap;
            }

            // Rope rider carry — V3-only positional coupling (v1/v2 return zero). Applied to
            // rb.position; the teleport branch zeroes linearVelocity so it can't double-count ropeVel.
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

            // useTeleport: SnapToSurface anchors to the platform's PRE-MovePosition collider
            //   (MovePosition is deferred to Physics.Simulate), so platformVel baked into finalVel
            //   gets undone by the snap — apply carry after the snap via ApplyPositionCarry instead.
            // non-teleport: Unity's contact solver carries the slime natively via linearVelocity.
            Vector2 finalVel = velocity + ropeVel;
            if (!useTeleport) finalVel += platformVel * carrierFrictionScale;

            if (useTeleport)
            {
                // CCD against thin moving colliders (SpringyBranch EdgeCollider2D) that a
                // rb.position teleport would otherwise skip past.
                Vector2 delta = finalVel * dt;
                Vector2 posBeforeTeleport = rb.position;
                float dist = delta.magnitude;
                if (dist > 1e-5f)
                {
                    Vector2 dir = delta / dist;
                    float clamped = dist;
                    // Sweep each lobe, not the full circle. QueriesStartInColliders is on, so a full
                    // circle sweeping from a cut body reports distance 0 against terrain sitting in
                    // the removed wedge and freezes the slime for the whole heal.
                    int lobes = CutLobeCount;
                    for (int L = 0; L < lobes; L++)
                    {
                        int hits = Physics2D.CircleCastNonAlloc(rb.position + GetCutLobeCentre(L),
                            GetCutLobeRadius(L), dir, ccdHitBuffer, dist, settings.groundLayer);
                        for (int i = 0; i < hits; i++)
                        {
                            var h = ccdHitBuffer[i];
                            if (h.collider == null) continue;
                            if (ignoredColliderList.Colliders.Contains(h.collider)) continue;
                            if (h.collider == surfaceCollider) continue;
                            // Small-ledge lip: pinning here stalls the slime flush against the face with
                            // net displacement ~0; the forward slide must carry it in so Phase A lifts it over.
                            if (surfaceProbe.IsLowLedgeCollider(h.collider)) continue;
                            if (h.distance < clamped) clamped = h.distance;
                        }
                    }
                    rb.position += dir * Mathf.Max(0f, clamped - settings.contactSkin);
                }
                // Tip-grab owns rb.position via LockBambooGrabPosition — skip the probe's
                // snap so it doesn't tug the slime onto a non-tip segment mid-bend.
                if (currentBambooGrab == null)
                    SnapToSurface();

                // Small-ledge passthrough can leave net displacement ~0 while `velocity` stays large,
                // so FixedUpdateAttached's velocity-magnitude wedge drain never fires and slideVelocity
                // erupts as a phantom launch on the next charge. Detect the stall positionally instead.
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

        // Independent from surfaceCheckDistance (which must hug the collider) — this only needs to
        // spot an override-bearing collider nearby so the mode swap happens before contact.
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

        /// <summary>Rotates the tangent by ω·dt/2 so interpolation draws the arc's chord midpoint;
        /// without it the drawn chord sits R(1−cos(Δθ/2)) inside the arc (~3–5 mm on Ice curves).</summary>
        private Vector2 ComputeArcCompensatedTangent(Vector2 currentTangent)
        {
            if (!settings.useSurfaceTangentArcCompensation) return currentTangent;
            if (!arcHasPrevTangent) return currentTangent;
            if (arcPrevTangent.sqrMagnitude < 0.5f) return currentTangent;

            float cross = arcPrevTangent.x * currentTangent.y - arcPrevTangent.y * currentTangent.x;
            float dot = arcPrevTangent.x * currentTangent.x + arcPrevTangent.y * currentTangent.y;
            float dTheta = Mathf.Atan2(cross, dot);

            // Reject spurious large rotations (re-attach, surface flip, very sharp corner).
            const float MAX_DTHETA = 0.5236f;
            if (dTheta > MAX_DTHETA) dTheta = MAX_DTHETA;
            else if (dTheta < -MAX_DTHETA) dTheta = -MAX_DTHETA;

            float halfTheta = dTheta * 0.5f;
            float c = Mathf.Cos(halfTheta);
            float s = Mathf.Sin(halfTheta);
            return new Vector2(currentTangent.x * c - currentTangent.y * s,
                               currentTangent.x * s + currentTangent.y * c);
        }

        // downhillGain when slideAccel pushes along the travel direction, uphillBrake when it
        // opposes, so a surface can tune descent and ascent independently.
        private float ResolveSlopeGain(float slideAccel, float slideVel, float angle)
        {
            if (Mathf.Abs(slideAccel) < 1e-4f || Mathf.Abs(slideVel) < 1e-4f) return 1f;
            return Mathf.Sign(slideAccel) == Mathf.Sign(slideVel)
                ? surfaceMod.slideDownhillGain.Evaluate(angle)
                : surfaceMod.slideUphillBrake.Evaluate(angle);
        }

        // Gravity-driven slide speed along surfaceTangent, shared by the Attached and Charging
        // handlers. useInertialSlide ON → integrates into slideVelocity with friction + drag;
        // OFF → legacy steady-state recompute each frame, ramped by slideRampUp after landing.
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

        // Stick→slide handoff: on stick expiry reset the ramp and seed the active tangent
        // channel from the velocity the stick decel left behind.
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

        // Rate-boost gate for the surfaceNormal low-pass on fast-rotating platforms.
        // The smoothing itself runs on every surface, boosted or not.
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

            // Tip-grab: the cached tip position is source of truth; re-snapping pulls the slime
            // onto a different segment as the bamboo bends.
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

        // executeDeferredReset=true when discarding the pending (timer expired) so the floor
        // landing's deferred conversionCount=0 takes effect; false when consuming it.
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

            // Must reset entering Attached or Charging: the LP filter only ticks in
            // FixedUpdateAttached, so otherwise the visual keeps the pre-charge banana-bend
            // frozen for the whole charge→launch sequence.
            if ((newState == SlimeState.Attached || newState == SlimeState.Charging)
                && currentState != newState)
            {
                sameSignFrames = 0;
                cornerStressRatio = 0f;
                smoothedCornerStressRatio = 0f;
            }

            // Tip-grab is bound to {Attached, Charging}; any exit drops the handle so the carrier
            // boost doesn't leak past the launching frame. Carrier inheritance already read it.
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

        /// <summary>Multiplicative velocity channel (drag, water resistance). Parallel to the additive AddExternalVelocity.</summary>
        public void MultiplyVelocity(float factor) { velocity *= factor; }

        /// Resets the airborne energy reference — call when an external event (water exit) counts as a new launch.
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

        /// <summary>Passthrough: IgnoreCollision + probe skip, for hazard bodies. Not for launch clearance.</summary>
        public void RegisterIgnoredCollider(Collider2D col, float duration)
        {
            ignoredColliderList.RegisterSingle(col, duration, IgnoreMode.Passthrough);
        }

        public void UnregisterIgnoredCollider(Collider2D col)
        {
            ignoredColliderList.Unregister(col);
        }

        /// <summary>TimedReattachGuard: probe skips it for the full duration but physics stays ON, so a
        /// surface chasing the slime after launch (BambooPlatformV3 whip-back) shoves it instead of re-attaching.</summary>
        public void RegisterReattachGuard(Collider2D col, float duration)
        {
            ignoredColliderList.RegisterSingle(col, duration, IgnoreMode.TimedReattachGuard);
        }
    }
}
