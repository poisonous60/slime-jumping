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
            ClearPendingEnergyConversion(executeDeferredReset: false);
            activeChargeRadius = settings.chargeRadius;
            driftDirection = Vector2.zero;
            surfaceProbeMissFrames = 0;
            cachedSurfaceCollider = null;
            if (chargeVisual != null) chargeVisual.Hide();

            // Carrier detach
            if (carrier != null) carrier.Detach();
            ClearPlatformEffect();
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
            postLaunchAirControlLockTimer = 0f;
            hopRequested = false;
            hopBufferTimer = 0f;
            dashBufferTimer = 0f;
            wasStuck = false;
            preserveLandingVelocity = false;
            stunTimer = 0f;
            blockAttachCount = 0;
            inFluidCount = 0;

            // Position and velocity
            rb.position = worldPos;
            velocity = newVelocity;
            externalDeltaV = Vector2.zero;
            rb.linearVelocity = newVelocity;

            // Energy tracking
            launchSpeed = newVelocity.magnitude;
            launchPosition = worldPos;
            launchedByHop = false;
            conversionCount = 0;

            // Unity doesn't fire OnTriggerExit2D after teleport — clear stale candidates
            interactionDetector.ClearCandidates();

            // Force Airborne
            ChangeState(SlimeState.Airborne, intentional: true);
        }
    }
}
