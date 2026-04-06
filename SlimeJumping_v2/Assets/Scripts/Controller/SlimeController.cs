using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Input;
using SlimeJumping.Debugging;
using SlimeJumping.Physics;
using SlimeJumping.Platforms;

namespace SlimeJumping.Controller
{
    public class SlimeController : MonoBehaviour
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

        // Judgment circle charge state (aimOffset = relative to slime center)
        private Vector2 aimOffset;
        private Vector2 driftDirection;

        // Post-launch input ignore
        private float postLaunchTimer;

        // Launch surface ignore
        private readonly List<Collider2D> ignoredColliders = new();
        private readonly List<float> ignoreTimers = new();

        // Manual velocity
        private Vector2 velocity;
        private Vector2 impactVelocity;
        private float stickDeccelTimer;
        private Vector2 prevSurfaceNormal;

        // Persistent crawl speed (accel/decel model)
        private float crawlVelocity;

        // Active charge radius (expanded on energy conversion)
        private float activeChargeRadius;

        // Stick timer (delay before slide starts)
        private float stickTimer;

        // Slide ramp-up timer (gradual slide acceleration after landing)
        private float slideRampTimer;

        // Energy-based conversion normalization (stored at launch)
        private float launchSpeed;
        private float launchHeight;

        // Charging coyote time (grace period after leaving surface)
        private float coyoteTimer;
        private Vector2 lastSurfaceNormal;

        // Surface contact info
        private Vector2 surfaceNormal;
        private Vector2 surfaceTangent;
        private Collider2D surfaceCollider;
        private Collider2D prevSurfaceCollider;
        private bool isTouchingSurface;

        private SurfaceModifiers surfaceMod = SurfaceModifiers.Default;

        private SpriteRenderer spriteRenderer;

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
            launchHeight = rb.position.y;
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
        }

        private void OnDisable()
        {
            inputReader.OnJumpPressed -= HandleJumpPressed;
            inputReader.OnJumpReleased -= HandleJumpReleased;
        }

        private void Update()
        {
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

            UpdateIgnoredColliders(dt);
            float surfaceMultiplier = (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
                ? settings.attachedCheckMultiplier : settings.airborneCheckMultiplier;
            isTouchingSurface = CheckSurface(out surfaceNormal, out surfaceCollider, surfaceMultiplier);

            // Correct normal if slime is on the underside of a surface
            if (isTouchingSurface && surfaceCollider != null)
            {
                Vector2 pos = rb.position;
                Vector2 closest = surfaceCollider.ClosestPoint(pos);
                Vector2 toSlime = pos - closest;
                // If normal points away from slime, flip it
                if (Vector2.Dot(toSlime, surfaceNormal) < 0f)
                    surfaceNormal = -surfaceNormal;
            }

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

            Vector2 platformVel = (carrier != null && carrier.IsOnPlatform)
                ? carrier.GetEffectiveVelocity() : Vector2.zero;
            float frictionAngle = isTouchingSurface ? GetSurfaceAngle() : 0f;
            rb.linearVelocity = velocity + platformVel * surfaceMod.carrierFriction.Evaluate(frictionAngle);
        }

        // --- State: Attached (any surface) ---

        private void FixedUpdateAttached(float dt)
        {
            if (!isTouchingSurface || Vector2.Dot(surfaceNormal, Vector2.up) < settings.ceilingThreshold)
            {
                crawlVelocity = 0f;
                launchSpeed = 0f;
                launchHeight = rb.position.y;
                if (carrier != null) carrier.Detach();
                surfaceMod = SurfaceModifiers.Default;
                currentState = SlimeState.Airborne;
                return;
            }

            // Stick: delay before slide starts, with optional deceleration
            if (stickTimer > 0f)
            {
                stickTimer -= dt;
                if (settings.stickDecelerationTime > 0f && stickDeccelTimer < settings.stickDecelerationTime)
                {
                    stickDeccelTimer += dt;
                    float t = stickDeccelTimer / settings.stickDecelerationTime;
                    velocity = Vector2.Lerp(impactVelocity, Vector2.zero, t);
                    SnapToSurface();
                    return;
                }
            }

            // 1. Slide: fresh each frame, no accumulation (skip during stick)
            float angle = GetSurfaceAngle();
            float slideSpeed = 0f;
            if (stickTimer <= 0f)
            {
                Vector2 gravityVec = new Vector2(0f, settings.gravity);
                float gravAlongTangent = Vector2.Dot(gravityVec, surfaceTangent);
                if (Mathf.Abs(gravAlongTangent) < settings.slideThreshold)
                    gravAlongTangent = 0f;
                slideSpeed = gravAlongTangent * settings.slideGravityScale * surfaceMod.slideGravity.Evaluate(angle);

                // Apply resistance to slideSpeed (not gravAlongTangent — crawl still fights full gravity)
                if (Mathf.Abs(slideSpeed) > 0.001f)
                {
                    float sign = Mathf.Sign(slideSpeed);
                    slideSpeed -= sign * settings.slideResistance * surfaceMod.slideResistance.Evaluate(angle);
                    if (Mathf.Sign(slideSpeed) != sign)
                        slideSpeed = 0f;
                }

                // Slide ramp-up: gradual acceleration after landing
                slideRampTimer += dt;
                float rampUpTime = surfaceMod.slideRampUp.Evaluate(angle);
                float rampT = (rampUpTime > 0f) ? Mathf.Clamp01(slideRampTimer / rampUpTime) : 1f;
                slideSpeed *= rampT;
            }

            // 2. Crawl: accel/decel model (weakened during stick)
            float crawlScale = (stickTimer > 0f) ? settings.stickCrawlMultiplier : 1f;
            float inputX = inputReader.MoveInput.x;
            float targetSpeed = -inputX * settings.crawlSpeed * surfaceMod.crawlSpeed.Evaluate(angle) * crawlScale;
            if (Mathf.Abs(targetSpeed) > Mathf.Abs(crawlVelocity)
                || (targetSpeed != 0f && Mathf.Sign(targetSpeed) != Mathf.Sign(crawlVelocity)))
                crawlVelocity = Mathf.MoveTowards(crawlVelocity, targetSpeed, settings.crawlAcceleration * surfaceMod.crawlAccel.Evaluate(angle) * dt);
            else
                crawlVelocity = Mathf.MoveTowards(crawlVelocity, targetSpeed, settings.crawlDeceleration * surfaceMod.crawlDecel.Evaluate(angle) * dt);

            // 3. Final tangent velocity
            float finalTangent = slideSpeed + crawlVelocity;
            velocity = surfaceTangent * finalTangent;

            // Cancel velocity into surface
            float normalVel = Vector2.Dot(velocity, surfaceNormal);
            if (normalVel < 0f)
                velocity -= surfaceNormal * normalVel;

            SnapToSurface();
        }

        // --- State: Charging ---

        private void FixedUpdateCharging(float dt)
        {
            // Aim logic (drift, WASD, angular clamp, auto-launch) — runs on fixed timestep for framerate independence

            // Auto-drift outward (compensated for expanded radius)
            float radiusRatio = activeChargeRadius / settings.chargeRadius;
            float activeDriftSpeed = settings.autoDriftSpeed * Mathf.Lerp(1f, radiusRatio, settings.driftCompensation);

            if (aimOffset.sqrMagnitude > 0.0001f)
                aimOffset += aimOffset.normalized * activeDriftSpeed * dt;
            else
                aimOffset += driftDirection * activeDriftSpeed * dt;

            // WASD: 2D acceleration on aim offset
            Vector2 input = inputReader.MoveInput;
            Vector2 prevAim = aimOffset;
            aimOffset += input * settings.inputAcceleration * dt;

            // Clamp angular speed
            if (prevAim.sqrMagnitude > 0.0001f && aimOffset.sqrMagnitude > 0.0001f)
            {
                float prevAngle = Mathf.Atan2(prevAim.y, prevAim.x) * Mathf.Rad2Deg;
                float newAngle = Mathf.Atan2(aimOffset.y, aimOffset.x) * Mathf.Rad2Deg;
                float delta = Mathf.DeltaAngle(prevAngle, newAngle);
                float maxDelta = settings.maxAimAngularSpeed * dt;
                delta = Mathf.Clamp(delta, -maxDelta, maxDelta);

                float clampedAngle = (prevAngle + delta) * Mathf.Deg2Rad;
                float radius = aimOffset.magnitude;
                aimOffset = new Vector2(Mathf.Cos(clampedAngle), Mathf.Sin(clampedAngle)) * radius;
            }

            // Auto-launch when reaching edge
            if (aimOffset.magnitude >= activeChargeRadius)
            {
                aimOffset = aimOffset.normalized * activeChargeRadius;
                Launch();
                return;
            }

            // Surface/coyote physics
            bool onValidSurface = isTouchingSurface
                && Vector2.Dot(surfaceNormal, Vector2.up) >= settings.ceilingThreshold;

            if (onValidSurface)
            {
                // Rotate aim offset to follow surface normal change on curved surfaces
                float prevAngle = Mathf.Atan2(prevSurfaceNormal.y, prevSurfaceNormal.x);
                float currAngle = Mathf.Atan2(surfaceNormal.y, surfaceNormal.x);
                float deltaAngle = currAngle - prevAngle;
                if (deltaAngle > Mathf.PI) deltaAngle -= 2f * Mathf.PI;
                else if (deltaAngle < -Mathf.PI) deltaAngle += 2f * Mathf.PI;
                if (Mathf.Abs(deltaAngle) > 0.0001f)
                {
                    float cos = Mathf.Cos(deltaAngle);
                    float sin = Mathf.Sin(deltaAngle);
                    aimOffset = new Vector2(
                        aimOffset.x * cos - aimOffset.y * sin,
                        aimOffset.x * sin + aimOffset.y * cos
                    );
                    driftDirection = new Vector2(
                        driftDirection.x * cos - driftDirection.y * sin,
                        driftDirection.x * sin + driftDirection.y * cos
                    );
                }
                prevSurfaceNormal = surfaceNormal;

                // On surface: reset coyote, remember surface for grace period
                coyoteTimer = settings.chargingCoyoteTime;
                lastSurfaceNormal = surfaceNormal;

                // Stick: delay before slide starts (aiming still runs via UpdateCharging)
                if (stickTimer > 0f)
                {
                    stickTimer -= dt;
                    if (settings.stickDecelerationTime > 0f && stickDeccelTimer < settings.stickDecelerationTime)
                    {
                        stickDeccelTimer += dt;
                        float t = stickDeccelTimer / settings.stickDecelerationTime;
                        velocity = Vector2.Lerp(impactVelocity, Vector2.zero, t);
                    }
                    else
                    {
                        velocity = Vector2.zero;
                    }
                    SnapToSurface();
                }
                else
                {
                    // Slime still slides on walls during charging (same physics as Attached, no crawl input)
                    float angle = GetSurfaceAngle();
                    Vector2 gravityVec = new Vector2(0f, settings.gravity);
                    float gravAlongTangent = Vector2.Dot(gravityVec, surfaceTangent);
                    if (Mathf.Abs(gravAlongTangent) < settings.slideThreshold)
                        gravAlongTangent = 0f;
                    float slideSpeed = gravAlongTangent * settings.slideGravityScale * surfaceMod.slideGravity.Evaluate(angle);

                    if (Mathf.Abs(slideSpeed) > 0.001f)
                    {
                        float sign = Mathf.Sign(slideSpeed);
                        slideSpeed -= sign * settings.slideResistance * surfaceMod.slideResistance.Evaluate(angle);
                        if (Mathf.Sign(slideSpeed) != sign)
                            slideSpeed = 0f;
                    }

                    // Slide ramp-up: gradual acceleration after landing
                    slideRampTimer += dt;
                    float rampUpTime = surfaceMod.slideRampUp.Evaluate(angle);
                    float rampT = (rampUpTime > 0f) ? Mathf.Clamp01(slideRampTimer / rampUpTime) : 1f;
                    slideSpeed *= rampT;

                    crawlVelocity = Mathf.MoveTowards(crawlVelocity, 0f, settings.crawlDeceleration * surfaceMod.crawlDecel.Evaluate(angle) * dt);

                    float finalTangent = slideSpeed + crawlVelocity;
                    velocity = surfaceTangent * finalTangent;

                    float normalVel = Vector2.Dot(velocity, surfaceNormal);
                    if (normalVel < 0f)
                        velocity -= surfaceNormal * normalVel;

                    SnapToSurface();
                }
            }
            else if (coyoteTimer > 0f)
            {
                // Coyote: off surface but still charging
                coyoteTimer -= dt;
                float elapsed = settings.chargingCoyoteTime - coyoteTimer;
                if (elapsed > settings.coyoteGraceTime)
                    velocity.y += settings.gravity * dt;
                else
                    velocity = Vector2.zero; // Grace: freeze at edge
            }
            else
            {
                // Coyote expired: cancel to Airborne
                crawlVelocity = 0f;
                launchSpeed = 0f;
                launchHeight = rb.position.y;
                if (carrier != null) carrier.Detach();
                surfaceMod = SurfaceModifiers.Default;
                currentState = SlimeState.Airborne;
                if (chargeVisual != null) chargeVisual.Hide();
            }
        }

        // --- State: Airborne ---

        private void FixedUpdateAirborne(float dt)
        {
            bool airControlActive = velocity.y <= settings.apexThreshold;

            // Gravity
            float gravityScale = 1f;
            float asymScale;
            if (airControlActive)
            {
                float inputY = inputReader.LastInputMove.y;
                if (inputY > 0.1f) gravityScale = settings.floatGravityScale;
                else if (inputY < -0.1f) gravityScale = settings.diveGravityScale;
                asymScale = settings.fallingGravityScale;
            }
            else
            {
                asymScale = settings.risingGravityScale;
            }
            velocity.y += settings.gravity * gravityScale * asymScale * dt;

            // Land on any surface
            if (isTouchingSurface)
            {
                float surfaceUp = Vector2.Dot(surfaceNormal, Vector2.up);

                // Ceiling: don't attach, just cancel normal velocity and stay airborne
                if (surfaceUp < settings.ceilingReattachThreshold)
                {
                    float normalVel = Vector2.Dot(velocity, surfaceNormal);
                    if (normalVel < 0f)
                        velocity -= surfaceNormal * normalVel;
                    return;
                }

                // Get landing platform velocity (including angular contribution) before attaching
                Vector2 landingPlatformVel = GetSurfaceVelocity(surfaceCollider);

                if (inputReader.IsJumpHeld)
                {
                    // Energy conversion: use energy conservation (v² = v₀² + 2gh)
                    // so W/S gravity control doesn't inflate/deflate conversion
                    float heightDelta = launchHeight - rb.position.y;
                    float shadowSpeedSq = launchSpeed * launchSpeed
                        + 2f * Mathf.Abs(settings.gravity) * heightDelta;
                    float impactSpeed = shadowSpeedSq > 0f ? Mathf.Sqrt(shadowSpeedSq) : 0f;
                    float speedRatio = Mathf.Sqrt(settings.heightRestorationRatio);
                    float effectiveRate = speedRatio * (settings.chargeRadius / settings.baseLaunchPower);
                    float spawnDistance = impactSpeed * effectiveRate;

                    Vector2 relativeVel = velocity - landingPlatformVel;
                    float tangentSpeed = Vector2.Dot(relativeVel, surfaceTangent);
                    float normalSpeed = Mathf.Abs(Vector2.Dot(relativeVel, surfaceNormal));
                    impactVelocity = surfaceTangent * tangentSpeed;
                    stickDeccelTimer = 0f;

                    if (carrier != null) carrier.Attach(surfaceCollider);
                    UpdateSurfaceModifiers(surfaceCollider);

                    float landingAngle = GetSurfaceAngle();
                    tangentSpeed *= surfaceMod.wallAttachDampen.Evaluate(landingAngle);
                    float redirectSign = Mathf.Sign(Vector2.Dot(Vector2.up, surfaceTangent));
                    float redirectScale = 1f - Mathf.Abs(Vector2.Dot(surfaceNormal, Vector2.up));
                    float effectiveTangent = tangentSpeed + normalSpeed * surfaceMod.impactRedirect.Evaluate(landingAngle) * redirectSign * redirectScale;
                    crawlVelocity = effectiveTangent * surfaceMod.landingVelocityRetention.Evaluate(landingAngle);
                    velocity = Vector2.zero;

                    if (surfaceUp >= settings.stickSlopeThreshold)
                        stickTimer = 0f;
                    else
                        stickTimer = CalculateStickDuration(impactSpeed) * surfaceMod.stickDuration.Evaluate(landingAngle);

                    activeChargeRadius = Mathf.Max(spawnDistance, settings.chargeRadius);
                    aimOffset = Vector2.zero;

                    slideRampTimer = 0f;
                    driftDirection = -surfaceNormal;
                    prevSurfaceNormal = surfaceNormal;
                    currentState = SlimeState.Charging;
                    SnapToSurface();
                    if (chargeVisual != null) chargeVisual.Show();
                }
                else
                {
                    // Landing without Space: sticky landing, energy lost
                    Vector2 relativeVel = velocity - landingPlatformVel;
                    float landingSpeed = relativeVel.magnitude;
                    float tangentSpeed = Vector2.Dot(relativeVel, surfaceTangent);
                    float normalSpeed = Mathf.Abs(Vector2.Dot(relativeVel, surfaceNormal));
                    impactVelocity = surfaceTangent * tangentSpeed;
                    stickDeccelTimer = 0f;

                    if (carrier != null) carrier.Attach(surfaceCollider);
                    UpdateSurfaceModifiers(surfaceCollider);

                    float landingAngle = GetSurfaceAngle();
                    tangentSpeed *= surfaceMod.wallAttachDampen.Evaluate(landingAngle);
                    float redirectSign = Mathf.Sign(Vector2.Dot(Vector2.up, surfaceTangent));
                    float redirectScale = 1f - Mathf.Abs(Vector2.Dot(surfaceNormal, Vector2.up));
                    float effectiveTangent = tangentSpeed + normalSpeed * surfaceMod.impactRedirect.Evaluate(landingAngle) * redirectSign * redirectScale;
                    crawlVelocity = effectiveTangent * surfaceMod.landingVelocityRetention.Evaluate(landingAngle);
                    velocity = Vector2.zero;

                    if (surfaceUp >= settings.stickSlopeThreshold)
                        stickTimer = 0f;
                    else
                        stickTimer = CalculateStickDuration(landingSpeed) * surfaceMod.stickDuration.Evaluate(landingAngle);

                    slideRampTimer = 0f;
                    currentState = SlimeState.Attached;
                    SnapToSurface();
                }
                return;
            }

            // Air control: horizontal acceleration with speed cap (last-input-wins)
            if (airControlActive)
            {
                float inputX = inputReader.LastInputMove.x;
                if (Mathf.Abs(inputX) > 0.01f)
                {
                    float targetVx = inputX * settings.airMaxHorizontalSpeed;
                    float accel = (inputX * velocity.x >= 0f)
                        ? settings.airAcceleration
                        : settings.airDeceleration;

                    if (Mathf.Abs(velocity.x) > settings.airMaxHorizontalSpeed)
                    {
                        // Already faster than cap (from launch) — only allow braking
                        if (inputX * velocity.x < 0)
                            velocity.x = Mathf.MoveTowards(velocity.x, targetVx, settings.airAcceleration * dt);
                    }
                    else
                    {
                        velocity.x = Mathf.MoveTowards(velocity.x, targetVx, accel * dt);
                    }
                }
            }
        }

        // --- Input Callbacks ---

        private void HandleJumpPressed()
        {
            if (currentState != SlimeState.Attached || postLaunchTimer > 0f) return;

            currentState = SlimeState.Charging;
            aimOffset = Vector2.zero;
            driftDirection = -surfaceNormal;
            prevSurfaceNormal = surfaceNormal;
            float tangentSpd = Vector2.Dot(velocity, surfaceTangent);
            impactVelocity = surfaceTangent * tangentSpd;
            stickDeccelTimer = 0f;
            velocity = Vector2.zero;
            activeChargeRadius = settings.chargeRadius;

            SnapToSurface();
            coyoteTimer = settings.chargingCoyoteTime;
            lastSurfaceNormal = surfaceNormal;
            if (carrier != null) carrier.Attach(surfaceCollider);
            UpdateSurfaceModifiers(surfaceCollider);

            if (chargeVisual != null) chargeVisual.Show();
        }

        private void HandleJumpReleased()
        {
            if (currentState != SlimeState.Charging) return;
            Launch();
        }

        private float CalculateStickDuration(float impactSpeed)
        {
            float t = 1f - 1f / (1f + impactSpeed * settings.stickSpeedScale);
            return Mathf.Lerp(settings.minStickDuration, settings.maxStickDuration, t);
        }

        // --- Launch ---

        private void Launch()
        {
            crawlVelocity = 0f;
            ignoredColliders.Clear();
            ignoreTimers.Clear();
            Collider2D[] overlaps = Physics2D.OverlapCircleAll(
                rb.position,
                settings.slimeRadius + settings.launchClearanceThreshold,
                settings.groundLayer);
            foreach (var c in overlaps)
            {
                ignoredColliders.Add(c);
                ignoreTimers.Add(settings.maxIgnoreTime);
            }

            float ratio = aimOffset.magnitude / settings.chargeRadius;
            float curvedRatio = Mathf.Pow(ratio, settings.chargeCurveExponent);
            float power = settings.minLaunchPower + curvedRatio * (settings.baseLaunchPower - settings.minLaunchPower);
            Vector2 dir = aimOffset.sqrMagnitude > 0.0001f ? -aimOffset.normalized : lastSurfaceNormal;
            velocity = dir * power;
            if (carrier != null)
            {
                velocity += carrier.GetEffectiveVelocity() * surfaceMod.carrierFriction.Evaluate(GetSurfaceAngle());
                carrier.Detach();
            }
            surfaceMod = SurfaceModifiers.Default;
            launchSpeed = velocity.magnitude;
            launchHeight = rb.position.y;

            currentState = SlimeState.Airborne;
            postLaunchTimer = settings.postLaunchIgnoreTime;

            if (chargeVisual != null) chargeVisual.Hide();
        }

        private void UpdateIgnoredColliders(float dt)
        {
            for (int i = ignoredColliders.Count - 1; i >= 0; i--)
            {
                ignoreTimers[i] -= dt;
                var c = ignoredColliders[i];
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy)
                {
                    ignoredColliders.RemoveAt(i);
                    ignoreTimers.RemoveAt(i);
                    continue;
                }
                float dist = Vector2.Distance(rb.position, c.ClosestPoint(rb.position));
                if (dist > settings.slimeRadius + settings.launchClearanceThreshold || ignoreTimers[i] <= 0f)
                {
                    ignoredColliders.RemoveAt(i);
                    ignoreTimers.RemoveAt(i);
                }
            }
        }

        // --- Surface Modifiers ---

        private void UpdateSurfaceModifiers(Collider2D col)
        {
            var tag = col != null ? col.GetComponentInParent<SurfaceTag>() : null;
            surfaceMod = tag != null ? tag.GetModifiers() : SurfaceModifiers.Default;
        }

        private float GetSurfaceAngle()
        {
            return Mathf.Acos(Mathf.Abs(Vector2.Dot(surfaceNormal, Vector2.up))) * Mathf.Rad2Deg;
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

        // --- Surface Detection (any angle) ---

        private bool CheckSurface(out Vector2 normal, out Collider2D col, float distMultiplier = 1f)
        {
            float radius = settings != null ? settings.slimeRadius : 0.4f;
            float dist = (settings != null ? settings.surfaceCheckDistance : 0.05f) * distMultiplier;
            LayerMask mask = settings != null ? settings.groundLayer : (LayerMask)(1 << 7);

            Vector2 pos = rb.position;
            Vector2[] directions = {
                Vector2.down, Vector2.up, Vector2.left, Vector2.right,
                new Vector2(-0.707f, -0.707f), new Vector2(0.707f, -0.707f),
                new Vector2(-0.707f, 0.707f), new Vector2(0.707f, 0.707f)
            };

            normal = Vector2.up;
            col = null;
            float bestDist = float.MaxValue;
            float bestScore = -10f;
            float distTolerance = dist * 0.5f;
            float inputX = inputReader.MoveInput.x;

            foreach (var d in directions)
            {
                RaycastHit2D[] hits = Physics2D.CircleCastAll(pos, radius * 0.9f, d, dist, mask);
                foreach (var h in hits)
                {
                    if (ignoredColliders.Contains(h.collider)) continue;

                    // How far below the slime is this contact point
                    Vector2 toHit = h.point - pos;
                    float below = toHit.sqrMagnitude > 0.0001f
                        ? Vector2.Dot(toHit.normalized, Vector2.down)
                        : 0f;

                    // Input alignment: crawling in input dir goes uphill on this surface
                    // tangent.y = normal.x, score = -tangent.y * inputX = -normal.x * inputX
                    float inputAlign = Mathf.Abs(inputX) > 0.01f ? -h.normal.x * inputX : 0f;

                    float score = below + inputAlign * 0.5f;

                    if (col == null || h.distance < bestDist - distTolerance)
                    {
                        bestDist = h.distance;
                        bestScore = score;
                        normal = h.normal;
                        col = h.collider;
                    }
                    else if (h.distance < bestDist + distTolerance && score > bestScore)
                    {
                        bestDist = h.distance;
                        bestScore = score;
                        normal = h.normal;
                        col = h.collider;
                    }
                }
            }

            return col != null;
        }

        private void SnapToSurface()
        {
            if (surfaceCollider == null) return;

            Vector2 pos = rb.position;
            Vector2 closest = surfaceCollider.ClosestPoint(pos);
            float currentDist = Vector2.Distance(pos, closest);
            float targetDist = settings.slimeRadius;

            if (Mathf.Abs(currentDist - targetDist) > 0.001f)
            {
                Vector2 pushDir = (pos - closest).normalized;
                if (pushDir.sqrMagnitude < 0.001f) pushDir = surfaceNormal;
                rb.position = closest + pushDir * targetDist;
            }
        }
    }
}
