using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Diagnostic logger for corner oscillation bug.
    /// Logs 5 checks: active contacts, primary flip, attach toggle/lockout,
    /// Phase A convergence, and per-contact surface angles.
    /// Attach to the same GameObject as SlimeController, or assign via inspector.
    /// </summary>
    public class CornerDiagnosticLogger : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private bool logEveryFrame;

        private Collider2D prevPrimaryCollider;

        private void Awake()
        {
            if (slimeController == null)
                slimeController = GetComponent<SlimeController>();
        }

        private void FixedUpdate()
        {
            if (slimeController == null) return;

            var probe = slimeController.Probe;
            int active = probe.ActiveContactCount;
            var currentPrimary = slimeController.SurfaceCollider;

            bool cornerSituation = active >= 2;
            bool lockoutActive = slimeController.AttachLockoutTimer > 0f;
            bool primaryFlipped = currentPrimary != prevPrimaryCollider
                && prevPrimaryCollider != null && currentPrimary != null;

            if (cornerSituation || lockoutActive || logEveryFrame)
            {
                LogDiagnostic(probe, active, currentPrimary, primaryFlipped);
            }
            else if (primaryFlipped && slimeController.IsTouchingSurface)
            {
                Debug.LogWarning($"[F{Time.frameCount}] PRIMARY FLIP: " +
                    $"{prevPrimaryCollider.name} -> {currentPrimary.name}");
            }

            prevPrimaryCollider = currentPrimary;
        }

        private void LogDiagnostic(SurfaceProbe probe, int active,
            Collider2D primary, bool flipped)
        {
            int total = probe.ContactCount;

            // Main summary line
            var sb = new System.Text.StringBuilder(512);
            sb.Append($"[F{Time.frameCount}] CORNER | State={slimeController.CurrentState}");
            sb.Append($" | Contacts: {active}/{total}");
            sb.Append($" | Toggle: {slimeController.AttachToggleCount}");
            sb.Append($" | Lockout: {slimeController.AttachLockoutTimer:F3}s");
            sb.Append($" | PhaseA: {probe.PhaseAIterations}iter rem={probe.PhaseARemainingPenetration:F4}");
            sb.Append($" | Primary: {(primary != null ? primary.name : "null")}");
            if (flipped) sb.Append(" [FLIPPED]");
            Debug.Log(sb.ToString());

            // Per-contact detail
            for (int i = 0; i < total; i++)
            {
                var col = probe.GetContactCollider(i);
                var n = probe.GetContactNormal(i);
                float dist = probe.GetContactDistance(i);
                bool isActive = probe.IsContactActive(i);
                float angle = SurfaceProbe.GetSurfaceAngle(n, slimeController.GravityUp);
                float surfaceUp = Vector2.Dot(n, slimeController.GravityUp);
                bool isPrimary = i == probe.SelectedIndex;

                Debug.Log($"  [{i}] {col.name}: angle={angle:F1} surfaceUp={surfaceUp:F3}" +
                    $" dist={dist:F4} active={isActive}{(isPrimary ? " *PRIMARY" : "")}");
            }
        }
    }
}
