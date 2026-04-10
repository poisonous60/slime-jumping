using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Input;
using SlimeJumping.Debugging;
using SlimeJumping.Physics;
using SlimeJumping.Platforms;
using SlimeJumping.Mechanisms;
using System.Collections.Generic;

namespace SlimeJumping.Controller
{
    public partial class SlimeController : MonoBehaviour
    {
        [SerializeField] private SlimeInputReader inputReader;
        [SerializeField] private SlimeSettings settings;
        [SerializeField] private SlimeChargeVisual chargeVisual;
        [SerializeField] private PlatformCarrier carrier;

        private Rigidbody2D rb;
        private CircleCollider2D circleCollider;

        public enum SlimeState { Attached, Charging, Airborne }
        private SlimeState currentState = SlimeState.Airborne;
        public SlimeState CurrentState => currentState;
        public bool IsTouchingSurface => isTouchingSurface;
        public bool IsSticking => stickTimer > 0f;
        public Vector2 SurfaceNormal => surfaceNormal;
        public Vector2 Velocity => velocity;
        public void AddExternalVelocity(Vector2 deltaV) { externalDeltaV += deltaV; }
        public float CoyoteTimer => coyoteTimer;
        public float StickTimer => stickTimer;
        public Collider2D SurfaceCollider => surfaceCollider;
        public int AttachToggleCount => attachToggleCount;
        public float AttachLockoutTimer => attachLockoutTimer;
        public float StickDeccelTimer => stickDeccelTimer;
        public float SlideRampTimer => slideRampTimer;
        public float Gravity => settings.gravity;
        public float CrawlVelocity => crawlVelocity;
        public Vector2 SurfaceTangent => surfaceTangent;
        public SurfaceProbe Probe => surfaceProbe;
        public Vector2 AimDirection => aimOffset.sqrMagnitude > 0.0001f
            ? -aimOffset.normalized
            : surfaceNormal;
        public float ChargingProgress => activeChargeRadius > 0.0001f
            ? aimOffset.magnitude / activeChargeRadius
            : 0f;
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
        private float stickDeccelTimer;
        private Vector2 prevSurfaceNormal;

        // Persistent crawl speed (accel/decel model)
        private float crawlVelocity;

        // Active charge radius (expanded on energy conversion)
        private float activeChargeRadius;

        // Full-charge aim grace timer
        private float chargeFullAimTimer;

        // Stick timer (delay before slide starts)
        private float stickTimer;
        private bool wasStuck;
        private bool preserveLandingVelocity;

        // Ground Dash (S impulse boost)
        private float dashCooldownTimer;
        private float dashBufferTimer;

        // Hop cooldown
        private float hopCooldownTimer;
        private float hopBufferTimer;
        private bool hopRequested;

        // Hyper dash: crawl captured at charge entry (immune to charging decel)
        private float chargeEntryCrawl;

        // Slide ramp-up timer (gradual slide acceleration after landing)
        private float slideRampTimer;
        private int conversionCount;

        // Energy-based conversion normalization (stored at launch)
        private float launchSpeed;
        private float launchHeight;
        private Vector2 launchGravityUp = Vector2.up;

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

        // Attach debounce: prevent rapid Attached↔Airborne oscillation at corners/edges
        private int attachToggleCount;
        private float lastAttachToggleTime;
        private float attachLockoutTimer;

        private SurfaceModifiers surfaceMod = SurfaceModifiers.Default;

        private SpriteRenderer spriteRenderer;

        private SurfaceProbe surfaceProbe;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            circleCollider = GetComponent<CircleCollider2D>();
            spriteRenderer = GetComponent<SpriteRenderer>();

            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            SyncRadius();
            ignoredColliderList = new IgnoredColliderList(settings.maxIgnoreTime);
            surfaceProbe = new SurfaceProbe(rb, settings, ignoredColliderList.Colliders);
            launchHeight = Vector2.Dot(rb.position, gravityUp);
            launchGravityUp = gravityUp;
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
        }

        private void OnDisable()
        {
            inputReader.OnJumpPressed -= HandleJumpPressed;
            inputReader.OnJumpReleased -= HandleJumpReleased;
            inputReader.OnVerticalDownPressed -= HandleVerticalDownPressed;
            inputReader.OnVerticalUpPressed -= HandleVerticalUpPressed;
        }

        private void Update()
        {
            if (hopCooldownTimer > 0f) hopCooldownTimer -= Time.deltaTime;
            if (hopBufferTimer > 0f) hopBufferTimer -= Time.deltaTime;
            if (dashCooldownTimer > 0f) dashCooldownTimer -= Time.deltaTime;
            if (dashBufferTimer > 0f) dashBufferTimer -= Time.deltaTime;

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

            // Compute effective gravity direction (LIFO stack: last entered zone wins)
            if (gravityFieldStack.Count > 0)
            {
                var topField = gravityFieldStack[gravityFieldStack.Count - 1];
                Vector2 newUp = -topField.GetDownDirection(rb.position);
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
            isTouchingSurface = surfaceProbe.CheckSurface(out surfaceNormal, out surfaceCollider, surfaceMultiplier, inputX, surfaceCollider, gravityUp, gravityRight);

            surfaceTangent = new Vector2(-surfaceNormal.y, surfaceNormal.x);

            if (isTouchingSurface && surfaceCollider != prevSurfaceCollider
                && (currentState == SlimeState.Attached || currentState == SlimeState.Charging))
            {
                if (carrier != null) carrier.Attach(surfaceCollider);
                UpdateSurfaceModifiers(surfaceCollider);
            }
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
            }

            // External forces (wind, conveyor, etc.)
            if (externalDeltaV.sqrMagnitude > 0.0001f)
            {
                if (currentState == SlimeState.Airborne)
                    velocity += externalDeltaV;
                else
                    velocity += surfaceTangent * Vector2.Dot(externalDeltaV, surfaceTangent);
                externalDeltaV = Vector2.zero;
            }

            Vector2 platformVel = (carrier != null && carrier.IsOnPlatform)
                ? carrier.GetEffectiveVelocity() : Vector2.zero;
            float frictionAngle = isTouchingSurface ? GetSurfaceAngle() : 0f;
            rb.linearVelocity = velocity + platformVel * surfaceMod.carrierFriction.Evaluate(frictionAngle);
        }

        // --- Surface Modifiers ---

        private void UpdateSurfaceModifiers(Collider2D col)
        {
            var tag = col != null ? col.GetComponentInParent<SurfaceTag>() : null;
            surfaceMod = tag != null ? tag.GetModifiers() : SurfaceModifiers.Default;
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

        private void SnapToSurface()
        {
            surfaceProbe.SnapToSurface(surfaceCollider, surfaceNormal);
        }

        private void ChangeState(SlimeState newState, bool intentional = false)
        {
            if (currentState == newState) return;

            if (intentional)
                attachToggleCount = 0;
            else
                RegisterAttachToggle();

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
    }
}
