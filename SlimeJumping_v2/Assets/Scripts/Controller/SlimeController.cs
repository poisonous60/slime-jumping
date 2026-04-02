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

        // Charge state
        private float chargeTimer;
        private float chargeRatio;
        private float initialAimAngle;
        private float currentAimOffset;
        private float aimAngle;

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

            rb.gravityScale = 0f; // Manual gravity
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
            {
                UpdateCharging();
            }
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

        // --- State: Charging ---

        private void UpdateCharging()
        {
            chargeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(chargeTimer / settings.fullChargeTime);
            chargeRatio = t;

            // Aim rotation with inertia
            float rotSpeed = settings.baseRotationSpeed * (1f - chargeRatio * settings.aimResistFactor);
            currentAimOffset -= inputReader.MoveInput.x * rotSpeed * Time.deltaTime;
            currentAimOffset = Mathf.Clamp(currentAimOffset, -settings.maxAimAngle, settings.maxAimAngle);
            aimAngle = initialAimAngle + currentAimOffset;

            if (chargeVisual != null)
                chargeVisual.UpdateVisual(chargeRatio, aimAngle, transform.position);

            // Auto-launch at full charge
            if (chargeTimer >= settings.fullChargeTime)
                Launch();
        }

        private void FixedUpdateCharging()
        {
            velocity = Vector2.zero;

            // Cancel charge if ground is lost
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
                // Zero out downward velocity on landing
                if (velocity.y < 0f) velocity.y = 0f;
                currentState = SlimeState.Grounded;
                return;
            }

            // Air control: weak horizontal steering
            float inputX = inputReader.MoveInput.x;
            if (Mathf.Abs(inputX) > 0.01f)
            {
                velocity.x += inputX * settings.airControlAccel * dt;
            }
        }

        // --- Input Callbacks ---

        private void HandleJumpPressed()
        {
            if (currentState != SlimeState.Grounded || postLaunchTimer > 0f) return;

            currentState = SlimeState.Charging;
            chargeTimer = 0f;
            chargeRatio = 0f;
            currentAimOffset = 0f;

            // Initial aim = surface normal angle (straight up on flat ground)
            initialAimAngle = Mathf.Atan2(groundNormal.y, groundNormal.x) * Mathf.Rad2Deg;
            aimAngle = initialAimAngle;

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

            float launchSpeed = settings.baseLaunchPower * chargeRatio;
            Vector2 dir = AngleToVector2(aimAngle);
            velocity = dir * launchSpeed;

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

            foreach (var hit in hits)
            {
                if (hit.collider == ignoredCollider) continue;
                if (hit.normal.y > 0.7f)
                {
                    normal = hit.normal;
                    return true;
                }
            }

            normal = Vector2.up;
            return false;
        }

        // --- Helpers ---

        private static Vector2 AngleToVector2(float degrees)
        {
            float rad = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
        }
    }
}
