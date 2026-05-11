using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- State: Stunned ---
        // Sticks to any contacted surface (like Attached) until stun expires.
        // No air control, no W/S gravity modulation, no state transitions.

        private void FixedUpdateStunned(float dt)
        {
            stunTimer -= dt;

            bool stickable = isTouchingSurface
                && surfaceCollider != null
                && !ignoredColliderList.Contains(surfaceCollider);

            if (stickable)
            {
                velocity = Vector2.zero;
                SnapToSurface();
            }
            else
            {
                velocity += -gravityUp * Mathf.Abs(settings.gravity) * dt;
            }

            // Timer expired: recover to Airborne
            if (stunTimer <= 0f)
            {
                stunTimer = 0f;
                launchSpeed = 0f;
                launchPosition = rb.position;
                conversionCount = 0;
                ChangeState(SlimeState.Airborne, intentional: true);
            }
        }
    }
}
