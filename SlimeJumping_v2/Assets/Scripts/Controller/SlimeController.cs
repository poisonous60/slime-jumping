using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.InputHandling;
using SlimeJumping.Debugging;
using SlimeJumping.Physics;
using SlimeJumping.Platforms;
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
        [SerializeField] private SlimeChargeVisual chargeVisual;
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
        public float CoyoteTimer => coyoteTimer;
        public float EnergyCoyoteTimer => energyCoyoteTimer;
        public float PostLaunchTimer => postLaunchTimer;
        public float StickTimer => stickTimer;
        public Collider2D SurfaceCollider => surfaceCollider;
        public int AttachToggleCount => attachToggleCount;
        public float AttachLockoutTimer => attachLockoutTimer;
        public float StickDeccelTimer => stickDeccelTimer;
        public float SlideRampTimer => slideRampTimer;
        public float Gravity => settings.gravity;
        public float SlimeRadius => settings.slimeRadius;
        public float CrawlVelocity => crawlVelocity;
        public Vector2 SurfaceTangent => surfaceTangent;
        public SurfaceProbe Probe => surfaceProbe;
        public Vector2 AimDirection => aimOffset.sqrMagnitude > 0.0001f
            ? -aimOffset.normalized
            : surfaceNormal;
        public float ChargingProgress => activeChargeRadius > 0.0001f
            ? aimOffset.magnitude / activeChargeRadius
            : 0f;
        public float ActiveChargeRadius => activeChargeRadius;
        public bool IsAtChargeFullAim => chargeFullAimTimer > 0f;
        public float ChargeFullAimProgress => settings.chargeFullAimTime > 0.0001f
            ? Mathf.Clamp01(chargeFullAimTimer / settings.chargeFullAimTime)
            : (chargeFullAimTimer > 0f ? 1f : 0f);
        public float ChargingLaunchSpeed
        {
            get
            {
                float ratio = activeChargeRadius > 0.0001f
                    ? aimOffset.magnitude / settings.chargeRadius
                    : 0f;
                float curved = Mathf.Pow(ratio, settings.chargeCurveExponent);
                return activeChargeRadius > settings.chargeRadius
                    ? curved * settings.baseLaunchPower
                    : settings.minLaunchPower + curved * (settings.baseLaunchPower - settings.minLaunchPower);
            }
        }

        // Judgment circle charge state (aimOffset = relative to slime center)
        private Vector2 aimOffset;
        private Vector2 driftDirection;

        // Post-launch input ignore
        private float postLaunchTimer;

        // Post-charged-launch air control lockout (not set by Hop/ForceAirborneWithVelocity)
        private float postLaunchAirControlLockTimer;

        // Launch surface ignore
        private IgnoredColliderList ignoredColliderList;

        // Volume charging flag (sand: Airborne → Charging without surface)
        private bool isVolumeCharging;

        // Current drag zone (set by DragZone.OnTriggerEnter/Exit, single zone only)
        public DragZone CurrentDragZone { get; private set; }
        public bool IsInChargeVolume => CurrentDragZone != null && CurrentDragZone.allowCharging;

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

        // Persistent crawl speed (accel/decel model)
        private float crawlVelocity;

        // Active charge radius (expanded on energy conversion)
        private float activeChargeRadius;

        // Full-charge aim grace timer
        private float chargeFullAimTimer;

        // Energy-conversion coyote: on no-Space landing, the spawn distance that
        // a Space-held landing would have granted is stashed here. If the player
        // presses Space before energyCoyoteTimer expires, HandleSurfaceChargeEntry
        // consumes these values instead of using the default chargeRadius.
        private float energyCoyoteTimer;
        private float pendingEnergyChargeRadius;
        // True when the deferred floor conversionCount=0 reset is owed on pending expiry.
        // The reset is deferred because consuming pending continues the chain (matching
        // the Space-held branch, which doesn't reset conversionCount on floor landings).
        private bool pendingFloorReset;

        // Stick timer (delay before slide starts)
        private float stickTimer;
        private bool wasStuck;
        private bool preserveLandingVelocity;

        // Ground Dash (S impulse boost)
        private float dashCooldownTimer;
        private float dashBufferTimer;

        // Air Dash (Shift, driven by external SlimeAirDash component).
        // Non-zero = FixedUpdateAirborne suspends gravity + air control for the remainder.
        private float airDashActiveTimer;
        public bool IsAirDashing => airDashActiveTimer > 0f;

        // Hop cooldown
        private float hopCooldownTimer;
        private float hopBufferTimer;
        private bool hopRequested;

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
        // True when the current airborne flight was started by Hop (not Launch / walk-off / ForceAirborne).
        // Consumed by the landing energy-conversion formula to apply hopConversionBonus / hopConversionEnergyBonus.
        private bool launchedByHop;

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

        // Attach debounce: prevent rapid Attached↔Airborne oscillation at corners/edges
        private int attachToggleCount;
        private float lastAttachToggleTime;
        private float attachLockoutTimer;

        private SurfaceModifiers surfaceMod = SurfaceModifiers.Default;

        // --- Stunned state ---
        private float stunTimer;
        private DamageType lastDamageType;
        public bool IsStunned => currentState == SlimeState.Stunned;
        public bool IsCharging => currentState == SlimeState.Charging;
        public float StunTimer => stunTimer;
        public DamageType LastDamageType => lastDamageType;

        // --- External controller override ---
        private IExternalController _override;
        public bool HasOverride => _override != null;

        // --- Platform effect ---
        private IPlatformEffect activePlatformEffect;

        // --- Rope coupling (#4) ---
        private Rope currentRope;

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

        // --- Landing event (for VFX, sound, etc.) ---
        public event System.Action<Vector2, Vector2> OnLanded; // (contactPoint, surfaceNormal)

        // --- Launch event (for sound, etc.) ---
        // Fires from player-intentional launches only: Launch() and Hop(). External-force paths
        // (JumpPad, Catapult) go through ForceAirborneWithVelocity and should raise their own cue.
        public event System.Action<Vector2, Vector2, bool> OnLaunch; // (position, velocity, wasHop)

        // --- Air Dash event ---
        // Payload = velocity written into the slime at dash start (direction × speed).
        // Consumers: SlimeAirDashVisual (trail/particles), audio, analytics.
        public event System.Action<Vector2> OnAirDashStarted;

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
                circleCollider.radius = s > 0.001f ? settings.slimeRadius / s : settings.slimeRadius;
            }
        }

        private void OnEnable()
        {
            inputReader.OnJumpPressed += HandleJumpPressed;
            inputReader.OnJumpReleased += HandleJumpReleased;
            inputReader.OnVerticalDownPressed += HandleVerticalDownPressed;
            inputReader.OnVerticalUpPressed += HandleVerticalUpPressed;
            inputReader.OnInteractPressed += HandleInteractPressed;
        }

        private void OnDisable()
        {
            inputReader.OnJumpPressed -= HandleJumpPressed;
            inputReader.OnJumpReleased -= HandleJumpReleased;
            inputReader.OnVerticalDownPressed -= HandleVerticalDownPressed;
            inputReader.OnVerticalUpPressed -= HandleVerticalUpPressed;
            inputReader.OnInteractPressed -= HandleInteractPressed;
        }

        private void Update()
        {
            if (_override != null)
            {
                _override.ExternalUpdate(this);
                return;
            }

            if (hopCooldownTimer > 0f) hopCooldownTimer -= Time.deltaTime;
            if (hopBufferTimer > 0f) hopBufferTimer -= Time.deltaTime;
            if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.deltaTime;
            if (dashBufferTimer > 0f) dashBufferTimer -= Time.deltaTime;
            if (chargeBufferTimer > 0f) chargeBufferTimer -= Time.deltaTime;

            if (postLaunchTimer > 0f)
            {
                postLaunchTimer -= Time.deltaTime;
                return;
            }

            if (currentState == SlimeState.Charging && chargeVisual != null)
                chargeVisual.UpdateVisual(rb.position, rb.position + aimOffset, activeChargeRadius);

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

            // Compute effective gravity direction (LIFO stack: last entered zone wins)
            // For radial zones, evaluate at midpoint (pos + vel*dt/2) to get second-order
            // accuracy and prevent systematic energy drift from forward Euler.
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

            ignoredColliderList.Tick(dt, rb.position, settings.slimeRadius, settings.launchClearanceThreshold);
            float surfaceMultiplier = (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
                ? settings.attachedCheckMultiplier : settings.airborneCheckMultiplier;
            float inputX = (currentState == SlimeState.Charging) ? 0f : inputReader.LastInputMove.x;
            Vector2 primaryNormal;
            isTouchingSurface = surfaceProbe.CheckSurface(out primaryNormal, out surfaceCollider, surfaceMultiplier, inputX, surfaceCollider, gravityUp, gravityRight);

            // Aggregate normal for slide/crawl/aim/impact continuity. `surfaceCollider`
            // still carries primary identity for carrier/surfaceMod/effect attribution.
            Vector2 rawSurfaceNormal = isTouchingSurface ? surfaceProbe.EffectiveNormal : gravityUp;

            // Low-pass filter surfaceNormal on fast-rotating platforms. On a spinning
            // wheel, contact faces flicker in/out of activeContactSkin as the wheel turns,
            // which causes EffectiveNormal to jump ~90° between frames. The Attached
            // velocity write (`velocity = surfaceTangent * (slideSpeed + crawlVelocity)`)
            // faithfully transfers that jump to rb.linearVelocity → dPos oscillates.
            // Filtering the normal only when the carrier is spinning leaves static
            // geometry responsiveness untouched.
            bool smoothingActive = isTouchingSurface
                && settings.surfaceNormalSmoothRate > 0.001f
                && IsOnFastRotatingSurface();

            if (smoothingActive && hasPrevSmoothedNormal)
            {
                float k = 1f - Mathf.Exp(-settings.surfaceNormalSmoothRate * dt);
                Vector2 mixed = Vector2.Lerp(prevSmoothedSurfaceNormal, rawSurfaceNormal, k);
                surfaceNormal = mixed.sqrMagnitude > 1e-6f ? mixed.normalized : rawSurfaceNormal;
            }
            else
            {
                surfaceNormal = rawSurfaceNormal;
            }
            prevSmoothedSurfaceNormal = surfaceNormal;
            hasPrevSmoothedNormal = isTouchingSurface;

            surfaceTangent = new Vector2(-surfaceNormal.y, surfaceNormal.x);

            if (isTouchingSurface && surfaceCollider != prevSurfaceCollider
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
            {
                if (carrier != null) carrier.Attach(surfaceCollider);
                UpdateSurfaceModifiers(surfaceCollider);
            }

            // Rope reference tracks the touched surface regardless of state, so the
            // Airborne→Attached landing frame (carrier.Attach happens inside the
            // Airborne handler, not here) correctly registers the rope too.
            // ReportRiderContact itself is gated on state==Attached.
            if (surfaceCollider != prevSurfaceCollider)
                UpdateRopeReference(surfaceCollider);

            prevSurfaceCollider = surfaceCollider;

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

            // Rope rider contact — fires while attached OR charging so the rope keeps
            // sagging under slime weight during charge. The stored displacement is
            // what Launch() reads via GetMaxSagDistance() to add rebound speed.
            // Rope consumes the buffer on its next FixedUpdate (execution order -100),
            // giving the designed 1-frame lag.
            if (currentRope != null
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
                currentRope.ReportRiderContact(surfaceCollider, rb.position, rb.mass, velocity);

            // External forces (wind, conveyor, etc.)
            if (externalDeltaV.sqrMagnitude > 0.0001f)
            {
                if (currentState == SlimeState.Airborne || currentState == SlimeState.Stunned)
                    velocity += externalDeltaV;
                else
                    velocity += surfaceTangent * Vector2.Dot(externalDeltaV, surfaceTangent);
                externalDeltaV = Vector2.zero;
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

            rb.linearVelocity = velocity
                + platformVel * surfaceMod.carrierFriction.Evaluate(frictionAngle)
                + ropeVel;
        }

        // --- Surface Modifiers ---

        private void UpdateSurfaceModifiers(Collider2D col)
        {
            SurfaceTag tag = null;
            if (col != null)
            {
                var tags = col.GetComponentsInParent<SurfaceTag>();
                for (int i = 0; i < tags.Length; i++)
                {
                    if (tags[i].enabled) { tag = tags[i]; break; }
                }
            }
            surfaceMod = tag != null ? tag.GetModifiers() : SurfaceModifiers.Default;
        }

        // Rope reference cache. Called on surface change.
        private void UpdateRopeReference(Collider2D col)
        {
            Rope newRope = col != null ? col.GetComponentInParent<Rope>() : null;
            if (newRope == currentRope) return;

            currentRope = newRope;

            if (currentRope != null)
            {
                currentRope.NotifyFirstAttach(rb.position);
            }
        }

        // --- Drag Zone API (called by DragZone.cs) ---

        public void OnDragZoneEnter(DragZone zone)
        {
            velocity *= zone.entryMultiplier;
            CurrentDragZone = zone;

            if (currentState == SlimeState.Airborne && zone.allowCharging
                && inputReader.IsJumpHeld && postLaunchTimer <= 0f)
            {
                EnterVolumeCharging();
            }
        }

        public void OnDragZoneExit(DragZone zone)
        {
            if (CurrentDragZone != zone) return;
            float savedFall = Vector2.Dot(velocity, gravityUp); // negative when falling
            velocity *= zone.exitMultiplier;
            if (savedFall < 0f)
            {
                float hSpeed = Vector2.Dot(velocity, gravityRight);
                velocity = gravityUp * savedFall + gravityRight * hSpeed;
            }
            CurrentDragZone = null;
        }

        public void ApplyDragFactor(float factor) { velocity *= factor; }
        public void ClampFallSpeed(float max)
        {
            float fallSpeed = -Vector2.Dot(velocity, gravityUp); // positive when falling
            if (fallSpeed > max)
            {
                float hSpeed = Vector2.Dot(velocity, gravityRight);
                velocity = gravityUp * (-max) + gravityRight * hSpeed;
            }
        }

        private float GetSurfaceAngle()
        {
            return SurfaceProbe.GetSurfaceAngle(surfaceNormal, gravityUp);
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
            surfaceProbe.SnapToSurface(surfaceCollider, surfaceNormal);
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
            pendingFloorReset = false;
        }

        private void ChangeState(SlimeState newState, bool intentional = false)
        {
            if (currentState == newState) return;

            if (intentional)
                attachToggleCount = 0;
            else
                RegisterAttachToggle();

            // Leaving Airborne ends any in-flight air dash (timer only ticks in
            // FixedUpdateAirborne, so a stale value would fire on re-entry).
            if (currentState == SlimeState.Airborne && newState != SlimeState.Airborne)
                airDashActiveTimer = 0f;

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
            launchedByHop = false;
            conversionCount = 0;
            ClearPendingEnergyConversion(executeDeferredReset: false);
        }

        /// <summary>
        /// Writes the given velocity and suspends gravity + air control for `activeDuration`
        /// seconds. Called by the optional SlimeAirDash component.
        /// Works from Attached (flings off the surface into Airborne) and Airborne.
        /// Blocked from Charging (would violate "charge must end in launch") and Stunned.
        /// Dash velocity flows through the standard landing path, so subsequent tangent
        /// inheritance at Charging entry (hyper dash) preserves the boost into the next
        /// Launch automatically.
        /// </summary>
        public bool ApplyAirDash(Vector2 dashVelocity, float activeDuration)
        {
            if (_override != null) return false;
            if (activeDuration <= 0f) return false;
            if (currentState == SlimeState.Charging) return false;
            if (currentState == SlimeState.Stunned) return false;

            if (currentState == SlimeState.Attached)
            {
                // Reuse the external-force path: detaches carrier, clears stick/crawl/rope
                // state, registers launch-clearance ignore, transitions to Airborne via
                // FinalizeAirborneExit (which resets the launch-reference for energy conversion).
                ForceAirborneWithVelocity(dashVelocity);
            }
            else // Airborne
            {
                velocity = dashVelocity;
                ResetLaunchReference();
            }

            airDashActiveTimer = activeDuration;
            postLaunchAirControlLockTimer = 0f;
            OnAirDashStarted?.Invoke(dashVelocity);
            return true;
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

        // --- Hazard API ---

        /// <summary>
        /// Single entry point for all hazard contact. Transitions to Stunned state
        /// with configurable knockback and duration.
        /// </summary>
        public void ApplyHazardEffect(HazardEffect effect)
        {
            if (_override != null) return;
            // TODO: time-based i-frame (stunIFrameDuration) for hazard variety.
            // Current policy: stunned = immune. Sufficient for Phase 0-1.
            // For Phase 5+ (lightning after saw blade), split into time-based window
            // so stun expiry can be immediately followed by a new hazard hit.
            if (currentState == SlimeState.Stunned) return;

            // Clean up charging state
            if (currentState == SlimeState.Charging)
            {
                isVolumeCharging = false;
                if (chargeVisual != null) chargeVisual.Hide();
            }

            // Detach
            if (carrier != null) carrier.Detach();
            ClearPlatformEffect();
            surfaceMod = SurfaceModifiers.Default;

            // Compute knockback direction
            Vector2 kbDir = effect.knockbackDirection;
            if (kbDir.sqrMagnitude < 0.0001f)
                kbDir = gravityUp;

            // Apply knockback velocity
            velocity = kbDir.normalized * effect.knockbackForce;

            // Reset state fields
            crawlVelocity = 0f;
            chargeEntryCrawl = 0f;
            aimOffset = Vector2.zero;
            chargeFullAimTimer = 0f;
            coyoteTimer = 0f;
            ClearPendingEnergyConversion(executeDeferredReset: false);
            stickTimer = 0f;
            stickDeccelTimer = 0f;
            slideRampTimer = 0f;
            hopRequested = false;
            hopBufferTimer = 0f;
            dashBufferTimer = 0f;
            chargeBufferTimer = 0f;
            postLaunchTimer = 0f;
            postLaunchAirControlLockTimer = 0f;
            wasStuck = false;
            preserveLandingVelocity = false;
            blockAttachCount = 0;
            inFluidCount = 0;

            // Enter Stunned
            stunTimer = effect.stunDuration;
            lastDamageType = effect.damageType;
            launchSpeed = 0f;
            launchPosition = rb.position;
            launchedByHop = false;
            conversionCount = 0;
            ignoredColliderList.RegisterOverlapping(
                rb.position,
                settings.slimeRadius + settings.launchClearanceThreshold,
                settings.groundLayer);

            ChangeState(SlimeState.Stunned, intentional: true);
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

        // --- External Controller Override ---

        /// <summary>
        /// Direct velocity write. Only callable while HasOverride==true.
        /// Use AddExternalVelocity / MultiplyVelocity for normal velocity changes.
        /// NOTE: OverridePosition is not yet needed — add when #5/#27/#31 require
        /// path-based slime movement during override.
        /// </summary>
        public void OverrideVelocity(Vector2 v)
        {
            if (!HasOverride)
            {
                Debug.LogError("[SlimeController] OverrideVelocity called outside of override context");
                return;
            }
            velocity = v;
        }

        public void SetOverride(IExternalController controller)
        {
            if (_override != null)
            {
                Debug.LogWarning("[SlimeController] Override already set. Releasing previous before setting new.");
                _override.OnReleaseControl(this);
            }

            // Clean up current state
            if (currentState == SlimeState.Charging)
            {
                isVolumeCharging = false;
                if (chargeVisual != null) chargeVisual.Hide();
            }
            if (carrier != null) carrier.Detach();
            ClearPlatformEffect();
            surfaceMod = SurfaceModifiers.Default;
            blockAttachCount = 0;
            inFluidCount = 0;
            stunTimer = 0f;
            // Pending energy-conversion coyote would otherwise survive the override
            // (timer only ticks in FixedUpdateAttached) and fire a spurious energy
            // conversion on the next Attached landing after release.
            ClearPendingEnergyConversion(executeDeferredReset: false);

            // Unity doesn't fire OnTriggerExit2D after override — clear stale candidates
            interactionDetector.ClearCandidates();

            _override = controller;
            _override.OnTakeControl(this);
        }

        public void ReleaseOverride(IExternalController controller)
        {
            if (_override != controller) return;

            _override.OnReleaseControl(this);
            _override = null;

            // SetOverride cleared detector candidates; if we're still overlapping
            // the same interactable (e.g. standing up from a bench), Unity won't
            // re-fire OnTriggerEnter2D. Rescan current overlaps so W works again.
            interactionDetector.Rescan();

            // Clear stale gravity fields (slime may have moved during override).
            // NOTE: If slime is still physically inside a GravityField after release,
            // that zone won't re-fire OnTriggerEnter2D. The IExternalController
            // implementation is responsible for moving the slime outside gravity zones
            // before releasing, or the zone will be silently orphaned until re-entry.
            gravityFieldStack.Clear();
            gravityUp = Vector2.up;
            gravityRight = Vector2.right;

            // Resume as Airborne
            launchSpeed = 0f;
            launchPosition = rb.position;
            launchedByHop = false;
            conversionCount = 0;
            stunTimer = 0f;
            ChangeState(SlimeState.Airborne, intentional: true);
        }
    }
}
