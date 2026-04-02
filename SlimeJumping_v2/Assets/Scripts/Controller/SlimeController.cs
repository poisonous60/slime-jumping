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

        private enum SlimeState { Grounded, Charging, Airborne }
        private SlimeState currentState;

        // Judgment circle charge state
        private Vector2 aimPoint;
        private Vector2 driftDirection;

        // Post-launch input ignore
        private float postLaunchTimer;

        // Launch surface ignore
        private Collider2D ignoredCollider;
        private float ignoreTimer;

        // Manual velocity (no Rigidbody gravity)
        private Vector2 velocity;

        // Cached ground info
        private Vector2 groundNormal = Vector2.up;
        private bool isGrounded;

        private Transform visualTransform;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            circleCollider = GetComponent<CircleCollider2D>();
            visualTransform = transform.Find("SlimeVisual");

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

            if (circleCollider == null) circleCollider = GetComponent<CircleCollider2D>();
            if (circleCollider != null) circleCollider.radius = settings.slimeRadius;

            if (visualTransform == null) visualTransform = transform.Find("SlimeVisual");
            if (visualTransform != null)
            {
                float d = settings.slimeRadius * 2f;
                visualTransform.localScale = new Vector3(d, d, d);
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
            isGrounded = CheckGround(out groundNormal);

            switch (currentState)
            {
                case SlimeState.Grounded:
                    FixedUpdateGrounded();
                    break;
                case SlimeState.Charging:
                    FixedUpdateCharging();
                    break;
                case SlimeState.Airborne:
                    FixedUpdateAirborne(dt);
                    break;
            }

            rb.linearVelocity = velocity;
        }

        // --- State: Grounded ---

        private void FixedUpdateGrounded()
        {
            if (!isGrounded)
            {
                currentState = SlimeState.Airborne;
                return;
            }

            float moveX = inputReader.MoveInput.x;
            velocity = new Vector2(moveX * settings.groundMoveSpeed, 0f);
        }

        // --- State: Charging (Judgment Circle) ---

        private void UpdateCharging()
        {
            float dt = Time.deltaTime;
            Vector2 center = (Vector2)transform.position;

            // Auto-drift: aim point moves toward surface (opposite of normal)
            aimPoint += driftDirection * settings.autoDriftSpeed * dt;

            // WASD: 2D acceleration on aim point
            Vector2 input = inputReader.MoveInput;
            aimPoint += input * settings.inputAcceleration * dt;

            // Check if aim point reached circle edge → auto-launch
            Vector2 offset = aimPoint - center;
            if (offset.magnitude >= settings.chargeRadius)
            {
                // Clamp to edge before launching
                aimPoint = center + offset.normalized * settings.chargeRadius;
                Launch();
                return;
            }

            if (chargeVisual != null)
                chargeVisual.UpdateVisual(center, aimPoint, settings.chargeRadius);
        }

        private void FixedUpdateCharging()
        {
            velocity = Vector2.zero;

            if (!isGrounded)
            {
                currentState = SlimeState.Airborne;
                if (chargeVisual != null) chargeVisual.Hide();
            }
        }

        // --- State: Airborne ---

        private void FixedUpdateAirborne(float dt)
        {
            // Manual gravity
            velocity.y += settings.gravity * dt;

            if (isGrounded)
            {
                if (velocity.y < 0f) velocity.y = 0f;
                currentState = SlimeState.Grounded;
                return;
            }

            // Air control: rotate velocity direction, preserve speed
            float inputX = inputReader.MoveInput.x;
            float speed = velocity.magnitude;
            if (speed > 0.01f && Mathf.Abs(inputX) > 0.01f)
            {
                float angle = Mathf.Atan2(velocity.y, velocity.x);
                float maxDelta = settings.airControlAngularSpeed * Mathf.Deg2Rad * dt / speed;
                angle += inputX * maxDelta;
                velocity = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed;
            }
        }

        // --- Input Callbacks ---

        private void HandleJumpPressed()
        {
            if (currentState != SlimeState.Grounded || postLaunchTimer > 0f) return;

            currentState = SlimeState.Charging;
            aimPoint = (Vector2)transform.position;
            driftDirection = -groundNormal; // drift toward surface

            velocity = Vector2.zero;

            if (chargeVisual != null) chargeVisual.Show();
        }

        private void HandleJumpReleased()
        {
            if (currentState != SlimeState.Charging) return;
            Launch();
        }

        // --- Launch ---

        private void Launch()
        {
            // Record the surface collider to ignore until cleared
            RaycastHit2D hit = Physics2D.CircleCast(
                (Vector2)transform.position,
                settings.slimeRadius * 0.8f,
                Vector2.down,
                settings.groundCheckDistance,
                settings.groundLayer
            );
            ignoredCollider = hit.collider;
            ignoreTimer = settings.maxIgnoreTime;

            // Judgment circle launch: direction = opposite of offset, power = proportional to distance
            Vector2 center = (Vector2)transform.position;
            Vector2 offset = aimPoint - center;
            float ratio = offset.magnitude / settings.chargeRadius;
            float power = ratio * settings.baseLaunchPower;
            Vector2 dir = offset.sqrMagnitude > 0.0001f ? -offset.normalized : Vector2.up;
            velocity = dir * power;

            currentState = SlimeState.Airborne;
            postLaunchTimer = settings.postLaunchIgnoreTime;

            if (chargeVisual != null) chargeVisual.Hide();
        }

        private void UpdateIgnoredCollider(float dt)
        {
            if (ignoredCollider == null) return;

            ignoreTimer -= dt;

            Vector2 closest = ignoredCollider.ClosestPoint(transform.position);
            float dist = Vector2.Distance((Vector2)transform.position, closest);

            if (dist > settings.slimeRadius + settings.launchClearanceThreshold || ignoreTimer <= 0f)
                ignoredCollider = null;
        }

        // --- Ground Detection ---

        private bool CheckGround(out Vector2 normal)
        {
            float radius = settings != null ? settings.slimeRadius : 0.4f;
            float distance = settings != null ? settings.groundCheckDistance : 0.1f;
            LayerMask mask = settings != null ? settings.groundLayer : (LayerMask)(1 << 7);

            RaycastHit2D[] hits = Physics2D.CircleCastAll(
                (Vector2)transform.position,
                radius * 0.8f,
                Vector2.down,
                distance,
                mask
            );

            foreach (var h in hits)
            {
                if (h.collider == ignoredCollider) continue;
                if (h.normal.y > 0.7f)
                {
                    normal = h.normal;
                    return true;
                }
            }

            normal = Vector2.up;
            return false;
        }
    }
}
