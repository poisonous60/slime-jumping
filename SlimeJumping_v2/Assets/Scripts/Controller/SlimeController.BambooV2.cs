using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // Bamboo V2 side-rebound: a side hit arms a charged-jump bonus. No-Space
        // landings stay attached; only a charging release adds bamboo bonus power.
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

        // No charge-power bonus by design: the whole boost is carrier inheritance of
        // the visible swing (BambooPlatform.VelocityTransferScale, speed-gated).
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
