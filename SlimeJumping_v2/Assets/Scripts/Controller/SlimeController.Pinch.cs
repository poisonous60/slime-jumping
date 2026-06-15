using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Pinch (caught between two converging platforms) — FORCE model ---
        //
        // The pressing platform (WaypointPlatformV2 leader) reports the pinch geometry each
        // FixedUpdate via SetPinch while the slime is between its two plates. This subsystem does
        // NOT own the slime's position and does NOT suppress collision (both of those were the old
        // failed scripted family). Instead, a genuine force interaction:
        //   - On catch, force the slime Airborne ONCE — the only state where applied velocity
        //     actually integrates (Attached/Charging are teleport-controlled and wipe external
        //     velocity). A pinch-launch is an exit, not a Charging→Attached revert.
        //   - Each step add a LATERAL spring push (AddExternalVelocity) toward the open side, scaled
        //     by compression so it ACCELERATES (slow while lightly pressed → fast when squeezed hard)
        //     — the squeeze-out two opposing flat plates cannot generate on their own.
        //   - Real collision stays ON: the plates physically press the slime, and the plate's own
        //     reaction back-off (WaypointPlatformV2) keeps the overlap shallow so the solver doesn't
        //     fling it. The slime deforms (visual) against the contact normals reported below.
        //   - Refresh launch-clearance + attach-lockout every step so the slime can't re-Attach to a
        //     plate mid-squeeze-out (kills the old release/recatch oscillation).
        // Release is purely geometric: once the slime clears the plate span the platform stops
        // reporting and the pinch expires once. No scripted edge teleport, no recatch loop.

        bool pinchReportedThisStep;
        Vector2 pinchAxisReport;     // world unit, leader→partner (compression axis)
        Vector2 pinchTangentReport;  // world unit, perpendicular (squeeze-out direction)
        Vector2 pinchCenterReport;   // world gap-center point (between the two plate faces)
        float pinchGapReport;        // free gap between the plate faces
        float pinchHalfSpanReport;   // plate half-width along the tangent

        int pinchExtrudeSign = 1;    // which way along the tangent the slime squirts (chosen at catch)
        float pinchPrevBodyScale = 1f;   // BodyScaleRatio before the squeeze, restored on release

        // Per-contact face normals (world) + compression for the generalized visual squash — the
        // platform reports one per closing wall each frame; SlimeVisual reads them and the GPU deform
        // flattens the slime locally against each (vertical / X / 3-side). Reset every SetPinch.
        public const int PinchMaxContacts = 3;
        readonly Vector2[] pinchContactNormals = new Vector2[PinchMaxContacts];
        readonly float[] pinchContactComps = new float[PinchMaxContacts];
        int pinchContactCount;
        public int PinchContactCount => pinchContactCount;
        public Vector2 GetPinchContactNormal(int i) => pinchContactNormals[i];
        public float GetPinchContactComp(int i) => pinchContactComps[i];

        /// <summary>Reported by the pressing platform once per closing wall each FixedUpdate (after
        /// SetPinch, which resets the list). normalWorld = the wall's face normal pointing toward the
        /// slime; comp = how far that wall has squashed in (0..1).</summary>
        public void AddPinchContact(Vector2 normalWorld, float comp)
        {
            if (pinchContactCount >= PinchMaxContacts) return;
            if (normalWorld.sqrMagnitude < 1e-8f) return;
            pinchContactNormals[pinchContactCount] = normalWorld.normalized;
            pinchContactComps[pinchContactCount] = Mathf.Clamp01(comp);
            pinchContactCount++;
        }

        /// <summary>
        /// Reported by the pressing platform (WaypointPlatformV2 leader) each FixedUpdate while the
        /// slime is between its two plates. Latest report wins; the pinch auto-expires the frame it
        /// stops being reported.
        /// </summary>
        public void SetPinch(Vector2 axisWorld, Vector2 tangentWorld, Vector2 gapCenterWorld, float gap, float halfSpan)
        {
            pinchReportedThisStep = true;
            pinchContactCount = 0;   // platform re-adds its walls via AddPinchContact this same step
            pinchAxisReport = axisWorld;
            pinchTangentReport = tangentWorld;
            pinchCenterReport = gapCenterWorld;
            pinchGapReport = gap;
            pinchHalfSpanReport = halfSpan;
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

            float fullR = settings.slimeRadius;   // full (unshrunk) radius — the geometry reference

            if (!PinchActive)
            {
                PinchActive = true;
                // Squirt toward whichever side the slime already leans (dead-centre → +1; the
                // reactive deck tilt breaks symmetry in practice).
                float lat0 = Vector2.Dot(rb.position - pinchCenterReport, pinchTangentReport);
                pinchExtrudeSign = Mathf.Abs(lat0) > 0.02f ? (int)Mathf.Sign(lat0) : 1;
                pinchPrevBodyScale = BodyScaleRatio;
                // Only Airborne integrates applied velocity (Attached/Charging are teleport-driven
                // and strip it). A pinch-launch is an exit, not a Charging→Attached revert.
                if (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
                    ForceAirborneWithVelocity(velocity);
            }

            PinchAxis = pinchAxisReport;
            float comp = Mathf.Clamp01((2f * fullR - pinchGapReport) / (2f * fullR));
            PinchCompression01 = comp;

            // NO-PENETRATION via collider SHRINK: the circle collider shrinks to FIT the gap (minus a
            // small margin) — so it never overlaps the plates → the solver has nothing to fling. This
            // is the existing cut-system path (SetBodyScale shrinks the real collider + probe reach).
            // Down to pinchSqueezeMinScale, where the plates' reaction stops them (WaypointPlatformV2).
            float fit = Mathf.Clamp((pinchGapReport - settings.pinchSqueezeFitMargin) / (2f * fullR),
                                    settings.pinchSqueezeMinScale, pinchPrevBodyScale);
            SetBodyScale(fit);

            // Squeeze-out velocity from COMPRESSION: the harder the squeeze, the faster it shoots out
            // (slow→fast), independent of the wall's instantaneous speed — like a pinched seed. Pure
            // tangent (no axial component → the slime does not drive into the walls). Physical velocity,
            // not owned position; when it clears the span the platform stops reporting → release.
            float extrude = Mathf.Min(settings.pinchExtrudeSpeed * comp / Mathf.Max(0.1f, 1f - comp),
                                      settings.pinchMaxExtrudeSpeed);
            velocity = pinchTangentReport * (pinchExtrudeSign * extrude);
            rb.linearVelocity = velocity;

            // Block re-Attach to a plate while squeezing out (no recatch loop).
            RegisterLaunchClearance();
            attachLockoutTimer = settings.attachLockoutDuration;
        }

        private void ReleasePinch()
        {
            PinchActive = false;
            PinchCompression01 = 0f;
            pinchContactCount = 0;
            SetBodyScale(pinchPrevBodyScale);   // grow the collider back to its pre-squeeze size
            // Residual lockout so the final separation frames finish before attach re-enables.
            attachLockoutTimer = settings.attachLockoutDuration;
        }
    }
}
