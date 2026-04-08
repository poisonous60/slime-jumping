using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Diagnostic logger for curved surface landing stutter.
    /// Captures per-frame data for ~15 frames after Airborne->Attached/Charging transition.
    /// Logs: position delta, normal/tangent rotation, Phase A convergence,
    /// velocity, stick decel timer, slide ramp timer.
    /// </summary>
    public class CurveLandingLogger : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private int captureFrames = 15;

        private Rigidbody2D rb;
        private SlimeController.SlimeState prevState;

        private int remainingFrames;
        private Vector2 prevPosition;
        private Vector2 prevNormal;

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

            // Detect Airborne -> Attached/Charging transition
            if (prevState == SlimeController.SlimeState.Airborne
                && (state == SlimeController.SlimeState.Attached
                    || state == SlimeController.SlimeState.Charging))
            {
                remainingFrames = captureFrames;
                prevPosition = rb.position;
                prevNormal = slimeController.SurfaceNormal;

                var col = slimeController.SurfaceCollider;
                string colType = col != null ? col.GetType().Name : "null";
                Debug.Log($"[F{Time.frameCount}] CURVE LAND | {prevState}->{state}" +
                    $" | collider={col?.name} ({colType})");
            }
            prevState = state;

            if (remainingFrames <= 0) return;
            remainingFrames--;

            Vector2 pos = rb.position;
            Vector2 normal = slimeController.SurfaceNormal;
            Vector2 tangent = slimeController.SurfaceTangent;
            Vector2 vel = slimeController.Velocity;
            Vector2 rbVel = rb.linearVelocity;
            var probe = slimeController.Probe;

            // Position delta
            Vector2 posDelta = pos - prevPosition;

            // Normal rotation (signed angle change)
            float normalAngleDeg = Vector2.SignedAngle(prevNormal, normal);

            // Phase A data
            int phaseAIter = probe.PhaseAIterations;
            float phaseARem = probe.PhaseARemainingPenetration;

            // Timers
            float stickT = slimeController.StickTimer;
            float decelT = slimeController.StickDeccelTimer;
            float rampT = slimeController.SlideRampTimer;
            float crawlV = slimeController.CrawlVelocity;

            var sb = new System.Text.StringBuilder(512);
            sb.Append($"[F{Time.frameCount}] CURVE");
            sb.Append($" | dPos=({posDelta.x:F4},{posDelta.y:F4})");
            sb.Append($" | n=({normal.x:F3},{normal.y:F3}) dN={normalAngleDeg:F2}deg");
            sb.Append($" | PhA={phaseAIter}i rem={phaseARem:F4}");
            sb.Append($" | vel=({vel.x:F3},{vel.y:F3})");
            sb.Append($" | rbVel=({rbVel.x:F3},{rbVel.y:F3})");
            sb.Append($" | stick={stickT:F3} decel={decelT:F3} ramp={rampT:F3} crawl={crawlV:F2}");
            Debug.Log(sb.ToString());

            prevPosition = pos;
            prevNormal = normal;
        }
    }
}
