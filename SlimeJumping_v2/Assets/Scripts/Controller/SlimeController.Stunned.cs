using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- State: Stunned ---
        // Reuses Airborne collision response (gravity + normal velocity cancellation)
        // but blocks Attached/Charging transitions while stunTimer > 0.
        // No air control, no W/S gravity modulation.

        private void FixedUpdateStunned(float dt)
        {
            stunTimer -= dt;

            // Gravity (1x, no W/S modulation, no asymmetric rising/falling scale)
            velocity += -gravityUp * Mathf.Abs(settings.gravity) * dt;

            // Surface collision: cancel normal velocity only (same as Airborne ceiling hit).
            // Never attach — stay airborne until stun expires.
            if (isTouchingSurface)
            {
                float normalVel = Vector2.Dot(velocity, surfaceNormal);
                if (normalVel < 0f)
                    velocity -= surfaceNormal * normalVel;
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
