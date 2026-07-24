using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Bamboo V2 Side-Rebound ---
        //
        // V2 keeps BambooPlatform's visual/collider bending, but removes the top
        // tip-grab loop. Side hits arm a charged-jump bonus: no-Space landings stay
        // attached, while Space-held/pressed charging releases add bamboo bonus power.
        private BambooPlatform currentBambooV2Rebound;
        private Vector2 bambooV2ReboundNormal;
        private float bambooV2ImpactSpeed;

        public BambooPlatform CurrentBambooV2Rebound => currentBambooV2Rebound;

        private bool TryArmBambooV2Rebound(float impactSpeed)
        {
            if (!isTouchingSurface || surfaceCollider == null) return false;

            var bamboo = surfaceCollider.GetComponentInParent<BambooPlatform>();
            if (bamboo == null) return false;
            if (!bamboo.IsV2SideContact(surfaceNormal)) return false;
            if (!bamboo.CanArmV2ReboundAt(rb.position)) return false;

            currentBambooV2Rebound = bamboo;
            bambooV2ReboundNormal = surfaceNormal.sqrMagnitude > 1e-6f ? surfaceNormal.normalized : gravityUp;
            bambooV2ImpactSpeed = impactSpeed;
            return true;
        }

        // No charge-power launch bonus: a V2 launch's whole boost is the carrier
        // inheritance of the swing the player can see (BambooPlatform.VelocityTransferScale
        // → ω × arm × v2CarrierScale, speed-gated), matching SpringyBranchPlatform.
        private void TryApplyBambooV2LaunchReleaseKick(Vector2 launchVelocity)
        {
            if (currentBambooV2Rebound == null) return;
            currentBambooV2Rebound.ApplyV2LaunchReleaseKick(rb.position, launchVelocity);
        }

        private void ClearBambooV2Rebound()
        {
            currentBambooV2Rebound = null;
            bambooV2ReboundNormal = Vector2.zero;
            bambooV2ImpactSpeed = 0f;
        }
    }
}
