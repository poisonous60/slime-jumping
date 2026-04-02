using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Input;
using SlimeJumping.Debugging;

namespace SlimeJumping.Controller
{
    public class SlimeController : MonoBehaviour
    {
        [SerializeField] private SlimeInputReader inputReader;
        [SerializeField] private SlimeSettings settings;
        [SerializeField] private SlimeChargeVisual chargeVisual;

        private Rigidbody2D rb;
        private CircleCollider2D circleCollider;

        private enum SlimeState { Attached, Charging, Airborne }
        private SlimeState currentState = SlimeState.Airborne;

        // Judgment circle charge state (aimOffset = relative to slime center)
        private Vector2 aimOffset;
        private Vector2 driftDirection;

        // Post-launch input ignore
        private float postLaunchTimer;

        // Launch surface ignore
        private Collider2D ignoredCollider;
        private float ignoreTimer;

        // Manual velocity
        private Vector2 velocity;

        // Persistent crawl speed (accel/decel model)
        private float crawlVelocity;

        // Active charge radius (expanded on energy conversion)
        private float activeChargeRadius;

        // Stick timer (delay before slide starts)
        private float stickTimer;

        // Airborne duration (to distinguish corner flicker from real flight)
        private float airborneTimer;

        // Surface contact info
        private Vector2 surfaceNormal;
        private Vector2 surfaceTangent;
        private Collider2D surfaceCollider;
        private bool isTouchingSurface;

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

            if (currentState == SlimeState.Charging)
                UpdateCharging();
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            UpdateIgnoredCollider(dt);
            isTouchingSurface = CheckSurface(out surfaceNormal, out surfaceCollider);

            // Correct normal if slime is on the underside of a surface
            if (isTouchingSurface && surfaceCollider != null)
            {
                Vector2 pos = (Vector2)transform.position;
                Vector2 closest = surfaceCollider.ClosestPoint(pos);
                Vector2 toSlime = pos - closest;
                // If normal points away from slime, flip it
                if (Vector2.Dot(toSlime, surfaceNormal) < 0f)
                    surfaceNormal = -surfaceNormal;
            }

            surfaceTangent = new Vector2(-surfaceNormal.y, surfaceNormal.x);

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

            rb.linearVelocity = velocity;
        }

        // --- State: Attached (any surface) ---

        private void FixedUpdateAttached(float dt)
        {
            if (!isTouchingSurface || Vector2.Dot(surfaceNormal, Vector2.up) < settings.ceilingThreshold)
            {
                crawlVelocity = 0f;
                airborneTimer = 0f;
                currentState = SlimeState.Airborne;
                return;
            }

            // Stick: delay before slide starts
            if (stickTimer > 0f)
            {
                stickTimer -= dt;
                velocity = Vector2.zero;
                SnapToSurface();
                return;
            }

            // 1. Slide: fresh each frame, no accumulation
            Vector2 gravityVec = new Vector2(0f, settings.gravity);
            float gravAlongTangent = Vector2.Dot(gravityVec, surfaceTangent);
            if (Mathf.Abs(gravAlongTangent) < settings.slideThreshold)
                gravAlongTangent = 0f;
            float slideSpeed = gravAlongTangent * settings.slideGravityScale;

            // Apply resistance to slideSpeed (not gravAlongTangent — crawl still fights full gravity)
            if (Mathf.Abs(slideSpeed) > 0.001f)
            {
                float sign = Mathf.Sign(slideSpeed);
                slideSpeed -= sign * settings.slideResistance;
                if (Mathf.Sign(slideSpeed) != sign)
                    slideSpeed = 0f;
            }

            // 2. Crawl: accel/decel model
            float inputX = inputReader.MoveInput.x;
            float targetSpeed = -inputX * settings.crawlSpeed;
            if (Mathf.Abs(targetSpeed) > Mathf.Abs(crawlVelocity)
                || (targetSpeed != 0f && Mathf.Sign(targetSpeed) != Mathf.Sign(crawlVelocity)))
                crawlVelocity = Mathf.MoveTowards(crawlVelocity, targetSpeed, settings.crawlAcceleration * dt);
            else
                crawlVelocity = Mathf.MoveTowards(crawlVelocity, targetSpeed, settings.crawlDeceleration * dt);

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

        private void UpdateCharging()
        {
            float dt = Time.deltaTime;
            Vector2 center = (Vector2)transform.position;

            // Auto-drift outward (compensated for expanded radius)
            float radiusRatio = activeChargeRadius / settings.chargeRadius;
            float activeDriftSpeed = settings.autoDriftSpeed * Mathf.Lerp(1f, radiusRatio, settings.driftCompensation);

            if (aimOffset.sqrMagnitude > 0.0001f)
                aimOffset += aimOffset.normalized * activeDriftSpeed * dt;
            else
                aimOffset += driftDirection * activeDriftSpeed * dt;

            // WASD: 2D acceleration on aim offset
            Vector2 input = inputReader.MoveInput;
            aimOffset += input * settings.inputAcceleration * dt;

            // Auto-launch when reaching edge
            if (aimOffset.magnitude >= activeChargeRadius)
            {
                aimOffset = aimOffset.normalized * activeChargeRadius;
                Launch();
                return;
            }

            if (chargeVisual != null)
                chargeVisual.UpdateVisual(center, center + aimOffset, activeChargeRadius);
        }

        private void FixedUpdateCharging(float dt)
        {
            if (!isTouchingSurface || Vector2.Dot(surfaceNormal, Vector2.up) < settings.ceilingThreshold)
            {
                crawlVelocity = 0f;
                airborneTimer = 0f;
                currentState = SlimeState.Airborne;
                if (chargeVisual != null) chargeVisual.Hide();
                return;
            }

            // Stick: delay before slide starts (aiming still runs via UpdateCharging)
            if (stickTimer > 0f)
            {
                stickTimer -= dt;
                velocity = Vector2.zero;
                SnapToSurface();
            }
            else
            {
                // Slime still slides on walls during charging (same physics as Attached, no crawl input)
                Vector2 gravityVec = new Vector2(0f, settings.gravity);
                float gravAlongTangent = Vector2.Dot(gravityVec, surfaceTangent);
                if (Mathf.Abs(gravAlongTangent) < settings.slideThreshold)
                    gravAlongTangent = 0f;
                float slideSpeed = gravAlongTangent * settings.slideGravityScale;

                if (Mathf.Abs(slideSpeed) > 0.001f)
                {
                    float sign = Mathf.Sign(slideSpeed);
                    slideSpeed -= sign * settings.slideResistance;
                    if (Mathf.Sign(slideSpeed) != sign)
                        slideSpeed = 0f;
                }

                crawlVelocity = Mathf.MoveTowards(crawlVelocity, 0f, settings.crawlDeceleration * dt);

                float finalTangent = slideSpeed + crawlVelocity;
                velocity = surfaceTangent * finalTangent;

                float normalVel = Vector2.Dot(velocity, surfaceNormal);
                if (normalVel < 0f)
                    velocity -= surfaceNormal * normalVel;

                SnapToSurface();
            }
        }

        // --- State: Airborne ---

        private void FixedUpdateAirborne(float dt)
        {
            // Gravity always applied
            velocity.y += settings.gravity * dt;
            airborneTimer += dt;

            // Land on any surface
            if (isTouchingSurface)
            {
                float surfaceUp = Vector2.Dot(surfaceNormal, Vector2.up);

                // Ceiling: don't attach, just cancel normal velocity and stay airborne
                if (surfaceUp < settings.ceilingThreshold)
                {
                    float normalVel = Vector2.Dot(velocity, surfaceNormal);
                    if (normalVel < 0f)
                        velocity -= surfaceNormal * normalVel;
                    return;
                }

                bool isRealFlight = airborneTimer > 0.1f;

                if (isRealFlight && inputReader.IsJumpHeld)
                {
                    // Energy conversion: momentum → expanded charge circle
                    float impactSpeed = velocity.magnitude;
                    float spawnDistance = impactSpeed * settings.conversionRate;

                    velocity = Vector2.zero;
                    crawlVelocity = 0f;

                    if (surfaceUp >= settings.stickSlopeThreshold)
                        stickTimer = 0f;
                    else
                        stickTimer = settings.chargingStickDuration;

                    activeChargeRadius = Mathf.Max(spawnDistance, settings.chargeRadius);
                    aimOffset = Vector2.zero;

                    driftDirection = -surfaceNormal;
                    currentState = SlimeState.Charging;
                    SnapToSurface();
                    if (chargeVisual != null) chargeVisual.Show();
                }
                else if (isRealFlight)
                {
                    // Real landing without Space: sticky landing, energy lost
                    velocity = Vector2.zero;
                    crawlVelocity = 0f;

                    if (surfaceUp >= settings.stickSlopeThreshold)
                        stickTimer = 0f;
                    else
                        stickTimer = settings.attachedStickDuration;

                    currentState = SlimeState.Attached;
                    SnapToSurface();
                }
                else
                {
                    // Corner flicker: preserve tangent velocity (old behavior)
                    float normalVel = Vector2.Dot(velocity, surfaceNormal);
                    if (normalVel < 0f)
                        velocity -= surfaceNormal * normalVel;

                    currentState = SlimeState.Attached;
                    SnapToSurface();
                }
                return;
            }

            // Air control: rotate velocity direction, preserve speed
            float inputX = inputReader.MoveInput.x;
            float speed = velocity.magnitude;
            if (speed > 0.01f && Mathf.Abs(inputX) > 0.01f)
            {
                float angle = Mathf.Atan2(velocity.y, velocity.x);
                float maxDelta = settings.airControlAngularSpeed * Mathf.Deg2Rad * dt / speed;
                maxDelta = Mathf.Min(maxDelta, settings.maxAirControlDelta * Mathf.Deg2Rad);
                angle += inputX * maxDelta;
                velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            }
        }

        // --- Input Callbacks ---

        private void HandleJumpPressed()
        {
            if (currentState != SlimeState.Attached || postLaunchTimer > 0f) return;

            currentState = SlimeState.Charging;
            aimOffset = Vector2.zero;
            driftDirection = -surfaceNormal;
            velocity = Vector2.zero;
            stickTimer = 0f;
            activeChargeRadius = settings.chargeRadius;

            if (chargeVisual != null) chargeVisual.Show();
        }

        private void HandleJumpReleased()
        {
            if (currentState != SlimeState.Charging) return;

            float ratio = aimOffset.magnitude / settings.chargeRadius;
            if (ratio < settings.minLaunchRatio)
            {
                Vector2 dir = aimOffset.sqrMagnitude > 0.0001f
                    ? aimOffset.normalized
                    : driftDirection;
                aimOffset = dir * settings.chargeRadius * settings.minLaunchRatio;
            }

            Launch();
        }

        // --- Launch ---

        private void Launch()
        {
            crawlVelocity = 0f;
            ignoredCollider = surfaceCollider;
            ignoreTimer = settings.maxIgnoreTime;

            float ratio = aimOffset.magnitude / settings.chargeRadius;
            float power = ratio * settings.baseLaunchPower;
            Vector2 dir = aimOffset.sqrMagnitude > 0.0001f ? -aimOffset.normalized : surfaceNormal;
            velocity = dir * power;

            currentState = SlimeState.Airborne;
            airborneTimer = 0f;
            postLaunchTimer = settings.postLaunchIgnoreTime;

            if (chargeVisual != null) chargeVisual.Hide();
        }

        private void UpdateIgnoredCollider(float dt)
        {
            if (ignoredCollider == null) return;

            if (!ignoredCollider.enabled || !ignoredCollider.gameObject.activeInHierarchy)
            {
                ignoredCollider = null;
                return;
            }

            ignoreTimer -= dt;

            Vector2 closest = ignoredCollider.ClosestPoint(transform.position);
            float dist = Vector2.Distance((Vector2)transform.position, closest);

            if (dist > settings.slimeRadius + settings.launchClearanceThreshold || ignoreTimer <= 0f)
                ignoredCollider = null;
        }

        // --- Surface Detection (any angle) ---

        private bool CheckSurface(out Vector2 normal, out Collider2D col)
        {
            float radius = settings != null ? settings.slimeRadius : 0.4f;
            float dist = settings != null ? settings.surfaceCheckDistance : 0.05f;
            LayerMask mask = settings != null ? settings.groundLayer : (LayerMask)(1 << 7);

            Vector2 pos = (Vector2)transform.position;
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
                    if (h.collider == ignoredCollider) continue;

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

            Vector2 pos = (Vector2)transform.position;
            Vector2 closest = surfaceCollider.ClosestPoint(pos);
            float currentDist = Vector2.Distance(pos, closest);
            float targetDist = settings.slimeRadius;

            if (currentDist < targetDist)
            {
                Vector2 pushDir = (pos - closest).normalized;
                if (pushDir.sqrMagnitude < 0.001f) pushDir = surfaceNormal;
                transform.position = (Vector3)(closest + pushDir * targetDist);
            }
        }
    }
}
