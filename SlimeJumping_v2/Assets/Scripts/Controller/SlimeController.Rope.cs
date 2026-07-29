using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // IRopePlatform so v1 (sag bonus) and v2 (timing bonus) each supply their own GetLaunchBonus.
        private IRopePlatform currentRope;

        public IRopePlatform CurrentRope => currentRope;

        private void UpdateRopeReference(Collider2D col)
        {
            IRopePlatform newRope = col != null ? col.GetComponentInParent<IRopePlatform>() : null;
            if (ReferenceEquals(newRope, currentRope)) return;

            currentRope = newRope;

            if (currentRope != null)
            {
                currentRope.NotifyFirstAttach(rb.position);
            }
        }

        /// <summary>Snap rb.position to the rope's nearest surface point; must run pre-probe (see FixedUpdate call site).</summary>
        private void LockRopePosition()
        {
            Vector2 ropeContact = currentRope.Edge.ClosestPoint(rb.position);
            Vector2 toSlime = rb.position - ropeContact;
            float ropeLockDist = toSlime.magnitude;
            // Bound to ~1.5× radius: past that the slime really left the rope (crawled off the
            // polyline end, teleported) and the probe must handle detach instead.
            if (ropeLockDist < SlimeRadius * 1.5f)
            {
                Vector2 dir = ropeLockDist > 0.0001f
                    ? (toSlime / ropeLockDist)
                    : gravityUp;
                rb.position = ropeContact + dir * SlimeRadius;
            }
        }
    }
}
