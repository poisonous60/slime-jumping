using UnityEngine;
using UnityEngine.InputSystem;
using SlimeJumping.InputHandling;

namespace SlimeJumping.Controller
{
    /// <summary>
    /// Celeste-style shift dash. Attach alongside SlimeController.
    /// Shift press from Attached or Airborne triggers an 8-way dash (direction from WASD).
    /// From Attached the dash flings the slime off into Airborne via ForceAirborneWithVelocity.
    /// Blocked while Charging (would cancel the aim) or Stunned.
    /// One charge; any surface landing (OnLanded) refills it.
    /// Disable this component to turn the feature off without touching the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlimeAirDash : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private SlimeInputReader inputReader;

        [Header("Dash tuning")]
        [Tooltip("World units per second applied as the dash velocity.")]
        [SerializeField] private float dashSpeed = 22f;
        [Tooltip("Seconds that gravity and air control are suspended after dash start.")]
        [SerializeField] private float dashActiveDuration = 0.15f;
        [Tooltip("Seconds after dash before another dash is allowed (safety net; landing also refills).")]
        [SerializeField] private float dashCooldown = 0.2f;
        [Tooltip("Seconds a Shift press is buffered while conditions aren't met (pre-landing, during postLaunch).")]
        [SerializeField] private float dashInputBuffer = 0.12f;

        [Header("Input")]
        [SerializeField] private Key dashKey = Key.LeftShift;

        public event System.Action<Vector2> OnDash;
        public bool DashAvailable => dashAvailable;
        public float DashActiveDuration => dashActiveDuration;
        public SlimeController SlimeController => slimeController;

        private bool dashAvailable = true;
        private float cooldownTimer;
        private float pressBufferTimer;
        private float lastHorizontalSign = 1f;

        private void Reset()
        {
            slimeController = GetComponent<SlimeController>();
            if (inputReader == null) inputReader = GetComponent<SlimeInputReader>();
        }

        private void Awake()
        {
            if (slimeController == null) slimeController = GetComponent<SlimeController>();
            if (inputReader == null) inputReader = GetComponent<SlimeInputReader>();
        }

        private void OnEnable()
        {
            if (slimeController != null)
                slimeController.OnLanded += HandleLanded;
        }

        private void OnDisable()
        {
            if (slimeController != null)
                slimeController.OnLanded -= HandleLanded;
            cooldownTimer = 0f;
            pressBufferTimer = 0f;
        }

        private void HandleLanded(Vector2 point, Vector2 normal)
        {
            dashAvailable = true;
            cooldownTimer = 0f;
        }

        /// <summary>Public refill hook for external dash-restoring sources (pickups, zones, etc.).</summary>
        public void RefillDash()
        {
            dashAvailable = true;
            cooldownTimer = 0f;
        }

        private void Update()
        {
            if (cooldownTimer > 0f) cooldownTimer -= Time.deltaTime;
            if (pressBufferTimer > 0f) pressBufferTimer -= Time.deltaTime;

            if (inputReader != null)
            {
                float mvx = inputReader.LastInputMove.x;
                if (Mathf.Abs(mvx) > 0.1f) lastHorizontalSign = Mathf.Sign(mvx);
            }

            var kb = Keyboard.current;
            bool pressed = kb != null && kb[dashKey].wasPressedThisFrame;
            if (pressed) pressBufferTimer = dashInputBuffer;

            if (pressBufferTimer > 0f)
            {
                if (TryDash()) pressBufferTimer = 0f;
            }
        }

        private bool TryDash()
        {
            if (slimeController == null) return false;
            if (!dashAvailable) return false;
            if (cooldownTimer > 0f) return false;
            if (slimeController.HasOverride) return false;
            if (slimeController.PostLaunchTimer > 0f) return false;
            // Allowed from Attached + Airborne. Blocked from Charging (would cancel charge
            // mid-aim) and Stunned. Matches the state guard inside ApplyAirDash.
            var state = slimeController.CurrentState;
            if (state == SlimeController.SlimeState.Charging) return false;
            if (state == SlimeController.SlimeState.Stunned) return false;

            Vector2 dir = ResolveDashDirection();
            Vector2 dashVel = dir * dashSpeed;
            if (!slimeController.ApplyAirDash(dashVel, dashActiveDuration)) return false;

            dashAvailable = false;
            cooldownTimer = dashCooldown;
            OnDash?.Invoke(dashVel);
            return true;
        }

        private Vector2 ResolveDashDirection()
        {
            Vector2 mv = inputReader != null ? inputReader.LastInputMove : Vector2.zero;
            float h = Mathf.Abs(mv.x) > 0.1f ? Mathf.Sign(mv.x) : 0f;
            float v = Mathf.Abs(mv.y) > 0.1f ? Mathf.Sign(mv.y) : 0f;
            if (h == 0f && v == 0f)
            {
                float sign = lastHorizontalSign;
                if (sign == 0f)
                {
                    float vx = slimeController.Velocity.x;
                    sign = Mathf.Abs(vx) > 0.01f ? Mathf.Sign(vx) : 1f;
                }
                return new Vector2(sign, 0f);
            }
            return new Vector2(h, v).normalized;
        }
    }
}
