using UnityEngine;
using SlimeJumping.Physics;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        public void TeleportTo(Vector2 worldPos, Vector2 newVelocity)
        {
            // Clear stale gravity fields (trigger exit won't fire for old location)
            gravityFieldStack.Clear();
            gravityUp = Vector2.up;
            gravityRight = Vector2.right;

            // Charging cleanup
            isVolumeCharging = false;
            if (CurrentDragZone != null)
            {
                CurrentDragZone.ForceExit();
                CurrentDragZone = null;
            }
            aimOffset = Vector2.zero;
            chargeFullAimTimer = 0f;
            coyoteTimer = 0f;
            activeChargeRadius = settings.chargeRadius;
            driftDirection = Vector2.zero;
            surfaceProbeMissFrames = 0;
            cachedSurfaceCollider = null;
            if (chargeVisual != null) chargeVisual.Hide();

            // Carrier detach
            if (carrier != null) carrier.Detach();
            surfaceMod = SurfaceModifiers.Default;

            // Clear ignored colliders (old location irrelevant)
            ignoredColliderList.Clear();

            // Timers
            stickTimer = 0f;
            stickDeccelTimer = 0f;
            slideRampTimer = 0f;
            crawlVelocity = 0f;
            chargeEntryCrawl = 0f;
            postLaunchTimer = 0f;
            hopRequested = false;
            hopBufferTimer = 0f;
            dashBufferTimer = 0f;
            wasStuck = false;
            preserveLandingVelocity = false;

            // Position and velocity
            rb.position = worldPos;
            velocity = newVelocity;
            externalDeltaV = Vector2.zero;
            rb.linearVelocity = newVelocity;

            // Energy tracking
            launchSpeed = newVelocity.magnitude;
            launchHeight = Vector2.Dot(worldPos, gravityUp);
            launchGravityUp = gravityUp;
            conversionCount = 0;

            // Force Airborne
            ChangeState(SlimeState.Airborne, intentional: true);
        }
    }
}
