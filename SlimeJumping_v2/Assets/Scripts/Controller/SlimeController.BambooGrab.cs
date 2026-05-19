using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Bamboo Tip-Grab (Rope-pattern flag) ---
        //
        // Sub-mode of Attached / Charging where the slime is anchored at the bamboo tip
        // and player A/D input is rerouted from crawl into bamboo tilt torque. Slime
        // never leaves SlimeState.Attached or Charging while grabbed; the grab is a
        // nullable handle, not a state.
        //
        // Lifecycle:
        //   - Enter:  UpdateBambooGrabReference sets currentBambooGrab when the slime is
        //             Attached/Charging on a bamboo segment with dot(normal, gravityUp)
        //             above the bamboo's grabEnterDot.
        //   - Sticky: once set, the dot check is NOT re-evaluated — the slime stays
        //             grabbed even when the bamboo bends past the dot threshold. This is
        //             the whole point: tilt past the threshold without losing the grip.
        //   - Exit:   ChangeState's transition-out-of-{Attached,Charging} hook clears
        //             the handle. This covers Launch (→ Airborne) and CancelCharging-
        //             ToAirborne, and any future exits.
        //
        // Carrier inheritance: PlatformCarrier reads BambooPlatform.VelocityTransferScale
        // at Launch via cachedSlime.CurrentBambooGrab == this, multiplying by
        // grabModeCarrierScale. The order in Launch() is carrier.GetLaunchVelocity()
        // BEFORE FinalizeAirborneExit's ChangeState — so the grab is still set when
        // the boost reads the multiplier.
        private BambooPlatform currentBambooGrab;

        /// <summary>The bamboo the slime is currently tip-grabbing, or null. Read by
        /// BambooPlatform.VelocityTransferScale to apply grabModeCarrierScale.</summary>
        public BambooPlatform CurrentBambooGrab => currentBambooGrab;
        public bool IsBambooGrabbed => currentBambooGrab != null;

        /// <summary>
        /// Refresh currentBambooGrab from the current surfaceCollider. Called once per
        /// FixedUpdate after UpdateSurfaceContact, before the state handler runs. Only
        /// sets the handle — once set, it remains until ChangeState clears it on exit
        /// from Attached/Charging.
        /// </summary>
        private void UpdateBambooGrabReference()
        {
            if (currentBambooGrab != null) return; // sticky after entry
            if (!isTouchingSurface || surfaceCollider == null) return;
            if (currentState != SlimeState.Attached && currentState != SlimeState.Charging) return;

            var bamboo = surfaceCollider.GetComponentInParent<BambooPlatform>();
            if (bamboo == null) return;
            if (Vector2.Dot(surfaceNormal, gravityUp) < bamboo.GrabEnterDot) return;

            currentBambooGrab = bamboo;
            // Snap to the offset anchor on entry so the slime doesn't sit at its detection
            // position for one FU before the next LockBambooGrabPosition runs.
            rb.position = ComputeBambooGrabAnchorPos(bamboo);
            // Reset bamboo's impact-pump bookkeeping. PlatformCarrier.FixedUpdate runs
            // at execution order -200 (before the slime FixedUpdate that just set the
            // grab), so the rider influence it applied this frame used the pre-grab
            // rb.position and the pre-grab slime velocity. Without resetting, NEXT FU's
            // ApplyRiderInfluence reads hasPrevRiderVel=true with previousRiderVel set
            // to that pre-grab velocity and pumps the bamboo with dv = 0 − prevVel
            // (non-zero) even though the slime is now anchored at zero velocity.
            // RemoveRider clears riderAttached/hasPrevRiderVel/detachTimer; the next
            // ApplyRiderInfluence call enters its firstAttach + slam-suppress branch
            // with |vx|=0, so no spurious pump fires.
            bamboo.RemoveRider();
        }

        /// <summary>
        /// Pre-probe rb.position lock — mirrors LockRopePosition. Snaps the slime to the
        /// bamboo's cached tip world position so the subsequent surface probe finds the
        /// tip segment as the primary contact even when the bamboo has bent. Bamboo's
        /// tip cache is refreshed in OnAfterMotion at PlatformMotor execution order
        /// −100, which runs before SlimeController's default order 0, so the cache is
        /// current for this frame.
        /// </summary>
        private void LockBambooGrabPosition()
        {
            if (currentBambooGrab == null) return;
            rb.position = ComputeBambooGrabAnchorPos(currentBambooGrab);
        }

        /// <summary>
        /// World-space anchor position for the grabbed slime. The tip world pos already
        /// sits at the apex of the topmost capsule's cap (segLen/2 above the segment
        /// center, the cap surface at y=bambooLength locally). So a slimeRadius offset
        /// along the bamboo's local-up at the tip places the slime's CircleCollider
        /// just touching the cap — no penetration (which would let the physics resolver
        /// fight the per-FU position lock) and no visible gap.
        /// </summary>
        private Vector2 ComputeBambooGrabAnchorPos(BambooPlatform bamboo)
        {
            Vector2 tip = bamboo.GetTipWorldPos();
            Vector2 tipUp = bamboo.GetTipUpDirection();
            return tip + tipUp * settings.slimeRadius;
        }

        /// <summary>
        /// Route raw A/D input into the bamboo's tilt torque channel while grabbed.
        /// Returns true if input was consumed (so the caller can suppress crawl/probe
        /// consumption of the same axis).
        /// </summary>
        private bool TryRouteBambooTiltInput(float inputX)
        {
            if (currentBambooGrab == null) return false;
            currentBambooGrab.ApplyTiltTorque(inputX, currentState == SlimeState.Charging);
            return true;
        }
    }
}
