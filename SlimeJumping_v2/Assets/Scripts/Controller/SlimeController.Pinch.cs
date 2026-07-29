using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Pinch (caught between two converging platforms) — FORCE model ---
        // Owns neither position nor collision (the scripted family failed). Force Airborne ONCE —
        // only Airborne integrates applied velocity; Attached/Charging are teleport-driven and wipe it.
        // Then a compression-scaled lateral push toward the open side, plus launch-clearance and
        // attach-lockout every step, else the slime re-Attaches mid-squeeze and oscillates.

        bool pinchReportedThisStep;
        Vector2 pinchAxisReport;     // world unit, leader→partner (compression axis)
        Vector2 pinchTangentReport;  // world unit, perpendicular (squeeze-out direction)
        Vector2 pinchCenterReport;   // world gap-center point (between the two plate faces)
        float pinchGapReport;        // free gap between the plate faces
        float pinchHalfSpanReport;   // plate half-width along the tangent

        int pinchExtrudeSign = 1;    // which way along the tangent the slime squirts (chosen at catch)

        // Decoupled from BodyScaleRatio so the VISUAL stays full-size and is flattened by the shader
        // instead of shrinking ("작아짐"). Deliberately NOT folded into SlimeRadius — that stays the
        // logical (cut-only) radius the platform reads for fullR, so squeeze geometry is unaffected.
        float pinchColliderScale = 1f;
        const float MinPinchColliderScale = 0.05f;   // a transient pinned squeeze may shrink far below the cut floor

        void SetPinchColliderScale(float scale)
        {
            scale = Mathf.Clamp(scale, MinPinchColliderScale, 1f);
            if (scale == pinchColliderScale) return;
            pinchColliderScale = scale;
            ApplyBodyScale();
        }

        // Per-contact face normals (world) + compression: one per closing wall each frame, read by
        // SlimeVisual so the GPU deform flattens locally against each. Reset every SetPinch.
        public const int PinchMaxContacts = 3;
        readonly Vector2[] pinchContactNormals = new Vector2[PinchMaxContacts];
        readonly float[] pinchContactComps = new float[PinchMaxContacts];
        int pinchContactCount;
        public int PinchContactCount => pinchContactCount;
        public Vector2 GetPinchContactNormal(int i) => pinchContactNormals[i];
        public float GetPinchContactComp(int i) => pinchContactComps[i];
        // The visual clamps the squeeze planes to THIS, not the mesh origin — the body3d mesh drifts
        // off-center via the visual offset pipeline, which would shift the carved slab off the plates.
        public Vector2 PinchCenter => pinchCenterReport;

        /// <summary>Reported once per closing wall each FixedUpdate, after SetPinch resets the list.
        /// normalWorld points toward the slime; comp = how far that wall has squashed in (0..1).</summary>
        public void AddPinchContact(Vector2 normalWorld, float comp)
        {
            if (pinchContactCount >= PinchMaxContacts) return;
            if (normalWorld.sqrMagnitude < 1e-8f) return;
            pinchContactNormals[pinchContactCount] = normalWorld.normalized;
            pinchContactComps[pinchContactCount] = Mathf.Clamp01(comp);
            pinchContactCount++;
        }

        /// <summary>Reported by the pressing platform (WaypointPlatformV2 leader) each FixedUpdate while
        /// the slime is between its plates; the pinch auto-expires the frame it stops being reported.</summary>
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
                // A pinch-launch is an exit, not a Charging→Attached revert.
                if (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
                    ForceAirborneWithVelocity(velocity);
            }

            PinchAxis = pinchAxisReport;
            float comp = Mathf.Clamp01((2f * fullR - pinchGapReport) / (2f * fullR));
            PinchCompression01 = comp;

            // Collider-only shrink to FIT the gap: zero overlap means the solver has nothing to fling,
            // so the lateral exit comes purely from pinchExtrudeSpeed. The visual is not shrunk.
            float desiredColliderR = 0.5f * (pinchGapReport - settings.pinchSqueezeFitMargin);
            SetPinchColliderScale(desiredColliderR / Mathf.Max(1e-4f, SlimeRadius));

            // Speed from COMPRESSION, not wall speed — a pinched seed shoots out slow→fast. Pure
            // tangent, so the slime never drives into the walls.
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
            SetPinchColliderScale(1f);   // grow the collider back to full (the visual was never shrunk)
            // Residual lockout so the final separation frames finish before attach re-enables.
            attachLockoutTimer = settings.attachLockoutDuration;
        }
    }
}
