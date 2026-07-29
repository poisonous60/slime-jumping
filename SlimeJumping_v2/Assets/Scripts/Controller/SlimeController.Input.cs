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
            else if (currentState == SlimeState.Airborne)
            {
                // Buffer a charge request: lets a press just before landing still trigger charging.
                chargeBufferTimer = settings.chargeInputBuffer;
            }
        }

        private void HandleSurfaceChargeEntry()
        {
            ChangeState(SlimeState.Charging);
            // Hyper-dash seed: capture full tangent speed at charge entry. On inertial
            // surfaces the half-pipe momentum lives in slideVelocity, so include it.
            chargeEntryCrawl = crawlVelocity + slideVelocity;
            aimOffset = Vector2.zero;
            driftDirection = -surfaceNormal;
            prevSurfaceNormal = surfaceNormal;

            // Charge entry stick grace. Capture from the channels, not `velocity`: on a frame-perfect
            // Airborne→Attached→Charging the Attached entry zeroed `velocity` → stick halts the slime.
            impactTangentSpeed = crawlVelocity + slideVelocity;
            stickTimer += settings.chargeEntryStickBonus;
            stickDeccelTimer = 0f;
            slideRampTimer = 0f;
            wasStuck = true;
            velocity = surfaceTangent * impactTangentSpeed;

            // Energy-conversion coyote: consume the radius stashed by the no-Space landing branch.
            // chargeEntryCrawl=0 here, else coyote beats holding Space for hyper-dash inheritance.
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
        }

        private void HandleJumpReleased()
        {
            if (_override != null) return;
            if (currentState != SlimeState.Charging) return;
            Launch();
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
