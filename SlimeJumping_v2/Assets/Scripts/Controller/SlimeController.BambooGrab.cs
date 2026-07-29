using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // Bamboo Tip-Grab — a nullable handle, not a state; the slime stays Attached/Charging.
        // The entry dot check runs once and is sticky afterwards, so the grip survives the
        // bamboo bending past the threshold. ChangeState clears it on exit from
        // Attached/Charging. Launch() calls carrier.GetLaunchVelocity() before that
        // ChangeState, so VelocityTransferScale still sees the grab.
        private BambooPlatform currentBambooGrab;

        /// <summary>The bamboo the slime is currently tip-grabbing, or null.</summary>
        public BambooPlatform CurrentBambooGrab => currentBambooGrab;
        public bool IsBambooGrabbed => currentBambooGrab != null;

        /// <summary>Set currentBambooGrab from surfaceCollider, once per FixedUpdate after UpdateSurfaceContact.</summary>
        private void UpdateBambooGrabReference()
        {
            if (currentBambooGrab != null) return; // sticky after entry
            if (!isTouchingSurface || surfaceCollider == null) return;
            if (currentState != SlimeState.Attached && currentState != SlimeState.Charging) return;

            var bamboo = surfaceCollider.GetComponentInParent<BambooPlatform>();
            if (bamboo == null) return;
            if (!bamboo.UsesTipGrab) return;
            // Compare against the stalk's own rest axis, not world up — a bamboo the
            // designer tilted with a Z rotation still enters grab on its own tip cap.
            if (Vector2.Dot(surfaceNormal, bamboo.StalkUp) < bamboo.GrabEnterDot) return;

            currentBambooGrab = bamboo;
            // Snap on entry, else the slime sits at its detection position for one FU.
            rb.position = ComputeBambooGrabAnchorPos(bamboo);
            // PlatformCarrier runs at order -200, so this frame's rider influence used the
            // pre-grab velocity; without the reset, next FU pumps the bamboo with
            // dv = 0 − prevVel even though the slime is now anchored at rest.
            bamboo.RemoveRider();
        }

        /// <summary>Pre-probe rb.position lock so the probe still finds the tip segment when the bamboo has bent; the tip cache is refreshed at PlatformMotor order −100, ahead of this.</summary>
        private void LockBambooGrabPosition()
        {
            if (currentBambooGrab == null) return;
            rb.position = ComputeBambooGrabAnchorPos(currentBambooGrab);
        }

        /// <summary>Anchor the collider just touching the tip cap — penetration would make the physics resolver fight the per-FU position lock.</summary>
        private Vector2 ComputeBambooGrabAnchorPos(BambooPlatform bamboo)
        {
            Vector2 tip = bamboo.GetTipWorldPos();
            Vector2 tipUp = bamboo.GetTipUpDirection();
            return tip + tipUp * SlimeRadius;
        }

        /// <summary>Route A/D into bamboo tilt torque while grabbed; true = consumed, caller must suppress crawl on the same axis.</summary>
        private bool TryRouteBambooTiltInput(float inputX)
        {
            if (currentBambooGrab == null) return false;
            currentBambooGrab.ApplyTiltTorque(inputX, currentState == SlimeState.Charging);
            return true;
        }
    }
}
