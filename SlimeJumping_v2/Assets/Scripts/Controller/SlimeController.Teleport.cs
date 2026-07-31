using UnityEngine;
using SlimeJumping.Physics;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        public void TeleportTo(Vector2 worldPos, Vector2 newVelocity)
        {
            // Trigger exit never fires for the old location — stale fields would stick forever.
            gravityFieldStack.Clear();
            gravityUp = Vector2.up;
            gravityRight = Vector2.right;

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
            hasPrevSmoothedNormal = false;

            if (carrier != null) carrier.Detach();
            ClearPlatformEffect();
            surfaceMod = settings.BuildDefaultModifiers();

            ignoredColliderList.Clear();

            stickTimer = 0f;
            stickDeccelTimer = 0f;
            slideRampTimer = 0f;
            crawlVelocity = 0f;
            slideVelocity = 0f;
            chargeEntryCrawl = 0f;
            postLaunchTimer = 0f;
            postLaunchAirControlLockTimer = 0f;
            chargeBufferTimer = 0f;
            wasStuck = false;
            stunTimer = 0f;
            blockAttachCount = 0;
            inFluidCount = 0;

            rb.position = worldPos;
            velocity = newVelocity;
            externalDeltaV = Vector2.zero;
            rb.linearVelocity = newVelocity;

            launchSpeed = newVelocity.magnitude;
            launchPosition = worldPos;
            conversionCount = 0;

            // Unity fires no trigger callbacks after teleport — drop the old candidates, rescan at the new spot
            interactionDetector.ClearAndRescanDeferred();

            ChangeState(SlimeState.Airborne, intentional: true);
        }
    }
}
