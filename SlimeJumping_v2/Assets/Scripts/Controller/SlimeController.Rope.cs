using UnityEngine;
using SlimeJumping.Platforms;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Rope coupling (#4) ---
        // IRopePlatform so both v1 (sag-bonus Rope) and v2 (timing-bonus RopeV2)
        // hand the controller their own GetLaunchBonus formula polymorphically.
        private IRopePlatform currentRope;

        public IRopePlatform CurrentRope => currentRope;

        // Rope reference cache. Called on surface change.
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

        /// <summary>Snap rb.position to the rope's nearest surface point. See call
        /// site in main FixedUpdate for ordering rationale (must run pre-probe).</summary>
        private void LockRopePosition()
        {
            Vector2 ropeContact = currentRope.Edge.ClosestPoint(rb.position);
            Vector2 toSlime = rb.position - ropeContact;
            float ropeLockDist = toSlime.magnitude;
            // Bound the lock to ~1.5× slime radius — beyond that assume the
            // slime intentionally left the rope (crawled off the polyline end,
            // teleported, etc.) and let the probe handle detach normally.
            if (ropeLockDist < settings.slimeRadius * 1.5f)
            {
                Vector2 dir = ropeLockDist > 0.0001f
                    ? (toSlime / ropeLockDist)
                    : gravityUp;
                rb.position = ropeContact + dir * settings.slimeRadius;
            }
        }
    }
}
