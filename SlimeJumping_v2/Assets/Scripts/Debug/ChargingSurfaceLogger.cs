using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Diagnostic logger for charging surface detection issues (corner oscillation bug).
    /// Logs charging state details every FixedUpdate and state transitions.
    /// Attach to the same GameObject as SlimeController, or assign via inspector.
    /// </summary>
    public class ChargingSurfaceLogger : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;

        private Rigidbody2D rb;
        private SlimeController.SlimeState prevState;

        private void Awake()
        {
            if (slimeController == null)
                slimeController = GetComponent<SlimeController>();
            rb = slimeController.GetComponent<Rigidbody2D>();
            prevState = slimeController.CurrentState;
        }

        private void FixedUpdate()
        {
            if (slimeController == null || rb == null) return;

            var state = slimeController.CurrentState;

            // State transition log
            if (state != prevState)
            {
                Debug.Log($"[F{Time.frameCount}] State: {prevState} → {state}");
                prevState = state;
            }

            // Charging detail log
            if (state == SlimeController.SlimeState.Charging)
            {
                var col = slimeController.SurfaceCollider;
                Debug.Log($"[F{Time.frameCount}] Charging: pos={rb.position}, vel={slimeController.Velocity}, " +
                          $"isTouching={slimeController.IsTouchingSurface}, normal={slimeController.SurfaceNormal}, " +
                          $"coyote={slimeController.CoyoteTimer:F3}, stick={slimeController.StickTimer:F3}, " +
                          $"collider={(col != null ? col.name : "null")}");
            }
        }
    }
}
