using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Pinch (caught between two converging platforms) ---
        //
        // WaypointPlatformV2 (the pressing leader) reports the gap geometry each FixedUpdate via
        // SetPinch while the slime sits between its two plates. This subsystem then OWNS the slime's
        // position for the duration: it holds the slime centered between the plates (so the
        // gap-driven visual squash conforms exactly) and extrudes it out sideways at a controlled
        // speed — overriding Phase A depenetration's instant lateral squirt (the old failure). The
        // squash amount the visual reads is purely the measured compression — no intensity knob.
        //
        // Phases: Press (platform closes to depth, squash builds) → Hold (stays squashed) →
        // Extrude (slides out the open side at pinchExtrudeSpeed) → release. Runs at the very end of
        // FixedUpdate (after OutputVelocity) so it has the last word on position.

        enum PinchPhase { Press, Hold, Extrude }

        bool pinchReportedThisStep;
        Vector2 pinchAxisReport;     // world unit, leader→partner (compression axis)
        Vector2 pinchTangentReport;  // world unit, perpendicular (extrude direction)
        Vector2 pinchCenterReport;   // world gap-center point (between the two plate faces)
        float pinchGapReport;        // free gap between the plate faces
        float pinchHalfSpanReport;   // plate half-width along the tangent (release past this)
        Collider2D pinchLeaderCol;   // the two plate colliders — collision-suppressed while held
        Collider2D pinchPartnerCol;

        PinchPhase pinchPhase;
        float pinchPhaseTimer;
        float pinchLateral;          // signed offset from the gap center along the tangent
        int pinchExtrudeSign = 1;

        /// <summary>
        /// Reported by the pressing platform (WaypointPlatformV2 leader) each FixedUpdate while the
        /// slime is between its two plates. Latest report wins; the pinch auto-expires the frame it
        /// stops being reported. axis = compression direction, tangent = extrude direction,
        /// gapCenter = world point midway between the plate faces, gap = free space, halfSpan =
        /// plate half-width along the tangent.
        /// </summary>
        public void SetPinch(Vector2 axisWorld, Vector2 tangentWorld, Vector2 gapCenterWorld, float gap, float halfSpan, Collider2D leaderCol, Collider2D partnerCol)
        {
            pinchReportedThisStep = true;
            pinchAxisReport = axisWorld;
            pinchTangentReport = tangentWorld;
            pinchCenterReport = gapCenterWorld;
            pinchGapReport = gap;
            pinchHalfSpanReport = halfSpan;
            pinchLeaderCol = leaderCol;
            pinchPartnerCol = partnerCol;
        }

        private void UpdatePinch(float dt)
        {
            bool reported = pinchReportedThisStep;
            pinchReportedThisStep = false;

            if (!reported)
            {
                if (PinchActive) ReleasePinch();
                return;
            }

            float R = SlimeRadius;

            if (!PinchActive)
            {
                PinchActive = true;
                pinchPhase = PinchPhase.Press;
                pinchPhaseTimer = 0f;
                pinchLateral = Vector2.Dot(rb.position - pinchCenterReport, pinchTangentReport);
                pinchExtrudeSign = Mathf.Abs(pinchLateral) > 0.02f ? (int)Mathf.Sign(pinchLateral) : 1;
            }

            PinchAxis = pinchAxisReport;
            PinchCompression01 = Mathf.Clamp01((2f * R - pinchGapReport) / (2f * R));

            // Suppress Unity-solver collision vs both plates while held. The plates close to a
            // gap far smaller than the slime's diameter, so the dynamic body deeply overlaps them;
            // the contact solver would eject it sideways (a fast physics squirt, wrong direction)
            // and overpower the controlled extrude below. Refreshed each reported frame (Passthrough
            // is timer-only), released in ReleasePinch. Now extrude speed is the ONLY lateral motion.
            RegisterIgnoredCollider(pinchLeaderCol, 0.25f);
            RegisterIgnoredCollider(pinchPartnerCol, 0.25f);

            pinchPhaseTimer += dt;
            switch (pinchPhase)
            {
                case PinchPhase.Press:
                    if (PinchCompression01 >= settings.pinchPressTargetComp || pinchPhaseTimer >= settings.pinchPressMaxTime)
                    { pinchPhase = PinchPhase.Hold; pinchPhaseTimer = 0f; }
                    break;
                case PinchPhase.Hold:
                    if (pinchPhaseTimer >= settings.pinchHoldTime)
                    { pinchPhase = PinchPhase.Extrude; pinchPhaseTimer = 0f; }
                    break;
                case PinchPhase.Extrude:
                    pinchLateral += pinchExtrudeSign * settings.pinchExtrudeSpeed * dt;
                    if (Mathf.Abs(pinchLateral) >= pinchHalfSpanReport)
                    {
                        // Pop a radius clear of the deck edge so the platform stops reporting
                        // (otherwise it would re-detect the slime at the edge and re-pinch).
                        pinchLateral = pinchExtrudeSign * (pinchHalfSpanReport + R);
                        rb.position = pinchCenterReport + pinchTangentReport * pinchLateral;
                        ReleasePinch();
                        return;
                    }
                    break;
            }

            // Own the position: centered between the plates (gap center) + controlled lateral.
            // Overrides Phase A's lateral squirt — extrude speed is the only sideways motion.
            rb.position = pinchCenterReport + pinchTangentReport * pinchLateral;
            velocity = Vector2.zero;
            rb.linearVelocity = Vector2.zero;
        }

        private void ReleasePinch()
        {
            PinchActive = false;
            PinchCompression01 = 0f;
            UnregisterIgnoredCollider(pinchLeaderCol);
            UnregisterIgnoredCollider(pinchPartnerCol);
            pinchLeaderCol = null;
            pinchPartnerCol = null;
        }
    }
}
