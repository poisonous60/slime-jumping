using UnityEngine;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- State: Stunned ---
        // Slides along surfaces, never attaches. No air control, no W/S gravity modulation.

        private void FixedUpdateStunned(float dt)
        {
            stunTimer -= dt;

            velocity += -gravityUp * Mathf.Abs(settings.gravity) * dt;

            // Cancel normal velocity only — tangent survives.
            if (isTouchingSurface)
                surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);

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
