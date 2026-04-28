using UnityEngine;
using SlimeJumping.Settings;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Input Callbacks ---

        private void HandleJumpPressed()
        {
            if (_override != null || currentState == SlimeState.Stunned) return;
            if (postLaunchTimer > 0f) return;

            if (currentState == SlimeState.Attached)
            {
                HandleSurfaceChargeEntry();
            }
            else if (currentState == SlimeState.Airborne && IsInChargeVolume)
            {
                EnterVolumeCharging();
            }
            else if (currentState == SlimeState.Airborne)
            {
                // Buffer a charge request: lets a press just before landing still
                // trigger charging, matching hop/ground-dash input buffering.
                chargeBufferTimer = settings.chargeInputBuffer;
            }
        }

        private void HandleSurfaceChargeEntry()
        {
            hopRequested = false;

            ChangeState(SlimeState.Charging);
            // Hyper-dash seed: capture full tangent speed at charge entry. On inertial
            // surfaces the half-pipe momentum lives in slideVelocity, so include it.
            chargeEntryCrawl = crawlVelocity + slideVelocity;
            aimOffset = Vector2.zero;
            driftDirection = -surfaceNormal;
            prevSurfaceNormal = surfaceNormal;

            // Charge entry stick grace: artificial stick event for smooth decel + slide suppression.
            // Capture from the inertia channels (crawl + slide) rather than `velocity`: on a
            // frame-perfect Airborne→Attached→Charging transition the Attached entry zeroed
            // `velocity` and the next FixedUpdateAttached hasn't run yet to rebuild it from the
            // channels, so reading `velocity` would falsely capture 0 and stick-lerp the slime
            // to a halt despite live tangent inertia.
            impactTangentSpeed = crawlVelocity + slideVelocity;
            stickTimer += settings.chargeEntryStickBonus;
            stickDeccelTimer = 0f;
            slideRampTimer = 0f;
            wasStuck = true;
            velocity = surfaceTangent * impactTangentSpeed;

            // Energy-conversion coyote: consume the pending spawn distance stashed
            // by the no-Space landing branch (Airborne.cs) if it's still live.
            // Space-held landings leave chargeEntryCrawl=0 (Launch zeroed it on the prior
            // flight exit, and the Airborne→Charging branch doesn't re-seed it from the
            // post-landing crawl) — zero it here too so coyote doesn't grant a strictly
            // better hyper-dash tangent inheritance than holding Space at the landing frame.
            if (energyCoyoteTimer > 0f && pendingEnergyChargeRadius > settings.chargeRadius)
            {
                activeChargeRadius = pendingEnergyChargeRadius;
                conversionCount++;
                chargeEntryCrawl = 0f;
            }
            else
            {
                activeChargeRadius = settings.chargeRadius;
            }
            ClearPendingEnergyConversion(executeDeferredReset: false);
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
            slideVelocity = 0f;

            ChangeState(SlimeState.Charging, intentional: true);
            if (chargeVisual != null) chargeVisual.Show();
        }

        private void HandleJumpReleased()
        {
            if (_override != null) return;
            if (currentState != SlimeState.Charging) return;
            Launch();
        }

        private void HandleGroundDashRequested(int keyboardSign)
        {
            if (_override != null || currentState == SlimeState.Stunned) return;
            if (currentState == SlimeState.Attached || currentState == SlimeState.Airborne)
            {
                dashBufferTimer = settings.groundDashInputBuffer;
                dashPendingKeyboardSign = keyboardSign;
            }
        }

        private void HandleVerticalUpPressed()
        {
            if (_override != null || currentState == SlimeState.Stunned) return;

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

        private void HandleInteractPressed()
        {
            if (_override != null || currentState == SlimeState.Stunned) return;

            if (interactionDetector.HasInteractable
                && currentState == SlimeState.Attached)
            {
                interactionDetector.TryInteract();
            }
        }
    }
}
