using UnityEngine;
using SlimeJumping.Settings;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Input Callbacks ---

        private void HandleJumpPressed()
        {
            if (postLaunchTimer > 0f) return;

            if (currentState == SlimeState.Attached)
            {
                HandleSurfaceChargeEntry();
            }
            else if (currentState == SlimeState.Airborne && IsInChargeVolume)
            {
                EnterVolumeCharging();
            }
        }

        private void HandleSurfaceChargeEntry()
        {
            hopRequested = false;

            ChangeState(SlimeState.Charging);
            chargeEntryCrawl = crawlVelocity;
            aimOffset = Vector2.zero;
            driftDirection = -surfaceNormal;
            prevSurfaceNormal = surfaceNormal;

            // Charge entry stick grace: artificial stick event for smooth decel + slide suppression
            impactTangentSpeed = Vector2.Dot(velocity, surfaceTangent);
            stickTimer += settings.chargeEntryStickBonus;
            stickDeccelTimer = 0f;
            slideRampTimer = 0f;
            wasStuck = true;
            velocity = surfaceTangent * impactTangentSpeed;

            activeChargeRadius = settings.chargeRadius;
            chargeFullAimTimer = 0f;

            SnapToSurface();
            coyoteTimer = settings.chargingCoyoteTime;
            lastSurfaceNormal = surfaceNormal;
            lastSurfaceTangent = surfaceTangent;
            surfaceProbeMissFrames = 0;
            cachedSurfaceCollider = surfaceCollider;
            if (carrier != null) carrier.Attach(surfaceCollider);
            UpdateSurfaceModifiers(surfaceCollider);

            if (chargeVisual != null) chargeVisual.Show();
        }

        private void EnterVolumeCharging()
        {
            isVolumeCharging = true;

            surfaceNormal = gravityUp;
            surfaceTangent = gravityRight;
            lastSurfaceNormal = gravityUp;
            lastSurfaceTangent = gravityRight;
            prevSurfaceNormal = gravityUp;

            aimOffset = Vector2.zero;
            driftDirection = -gravityUp;
            activeChargeRadius = settings.chargeRadius;
            chargeFullAimTimer = 0f;
            chargeEntryCrawl = 0f;
            crawlVelocity = 0f;

            ChangeState(SlimeState.Charging, intentional: true);
            if (chargeVisual != null) chargeVisual.Show();
        }

        private void HandleJumpReleased()
        {
            if (currentState != SlimeState.Charging) return;
            Launch();
        }

        private void HandleVerticalDownPressed()
        {
            if (currentState == SlimeState.Attached || currentState == SlimeState.Airborne)
                dashBufferTimer = settings.groundDashInputBuffer;
        }

        private void HandleVerticalUpPressed()
        {
            if (currentState == SlimeState.Attached)
            {
                if (postLaunchTimer > 0f) return;
                if (hopCooldownTimer > 0f) return;
                hopRequested = true;
            }
            else if (currentState == SlimeState.Airborne)
            {
                hopBufferTimer = settings.hopBufferDuration;
            }
        }
    }
}
