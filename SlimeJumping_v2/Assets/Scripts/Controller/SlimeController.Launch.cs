using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Physics;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Launch / Hop / Ground Dash ---

        /// <summary>
        /// Shared tail for all exit-to-airborne transitions (Launch, Hop, ForceAirborneWithVelocity).
        /// Caller must set velocity and handle carrier/state-specific cleanup before calling.
        /// </summary>
        private void FinalizeAirborneExit()
        {
            if (currentRope != null)
                currentRope.NotifyLaunchRebound(rb.position);

            ClearPlatformEffect();
            surfaceMod = SurfaceModifiers.Default;
            launchSpeed = velocity.magnitude;
            launchPosition = rb.position;
            launchedByHop = false;
            ChangeState(SlimeState.Airborne, intentional: true);
            postLaunchTimer = settings.postLaunchIgnoreTime;
        }

        /// <summary>
        /// Forces the slime into Airborne with the given velocity.
        /// Used by IPlatformEffect implementations (JumpPad, Catapult, etc.).
        /// Single code path shared with Launch/Hop via FinalizeAirborneExit.
        /// </summary>
        public void ForceAirborneWithVelocity(Vector2 vel)
        {
            velocity = vel;
            crawlVelocity = 0f;
            chargeEntryCrawl = 0f;
            stickTimer = 0f;
            stickDeccelTimer = 0f;
            slideRampTimer = 0f;
            hopRequested = false;

            if (currentState == SlimeState.Charging)
            {
                isVolumeCharging = false;
                aimOffset = Vector2.zero;
                if (chargeVisual != null) chargeVisual.Hide();
            }

            if (carrier != null) carrier.Detach();

            ignoredColliderList.RegisterOverlapping(
                rb.position,
                settings.slimeRadius + settings.launchClearanceThreshold,
                settings.groundLayer);

            conversionCount = 0;
            // conversionCount already zeroed above, so skip the deferred floor reset —
            // otherwise the pending from the prior no-Space landing would leak into the
            // next flight (timer only decrements in FixedUpdateAttached) and eventually
            // fire ClearPendingEnergyConversion(true) mid-chain for the wrong landing.
            ClearPendingEnergyConversion(executeDeferredReset: false);
            FinalizeAirborneExit();
        }

        private void Launch()
        {
            preserveLandingVelocity = false;
            hopBufferTimer = 0f;
            dashBufferTimer = 0f;
            chargeBufferTimer = 0f;
            float preLaunchCrawl = chargeEntryCrawl;
            chargeEntryCrawl = 0f;
            crawlVelocity = 0f;

            if (!isVolumeCharging)
            {
                ignoredColliderList.RegisterOverlapping(
                    rb.position,
                    settings.slimeRadius + settings.launchClearanceThreshold,
                    settings.groundLayer);
            }

            bool isEnergyConversion = activeChargeRadius > settings.chargeRadius;
            activeChargeRadius = settings.chargeRadius;
            float ratio = aimOffset.magnitude / settings.chargeRadius;
            float curvedRatio = Mathf.Pow(ratio, settings.chargeCurveExponent);
            float power = isEnergyConversion
                ? curvedRatio * settings.baseLaunchPower
                : settings.minLaunchPower + curvedRatio * (settings.baseLaunchPower - settings.minLaunchPower);
            Vector2 dir = aimOffset.sqrMagnitude > 0.0001f ? -aimOffset.normalized : lastSurfaceNormal;
            velocity = dir * power;

            if (!isVolumeCharging)
            {
                // Surface launch: hyper dash + carrier
                float excess = Mathf.Max(0f, Mathf.Abs(preLaunchCrawl) - settings.hyperDashDeduction);
                velocity += lastSurfaceTangent * Mathf.Sign(preLaunchCrawl) * excess * settings.tangentInheritanceFactor;
                if (carrier != null)
                {
                    velocity += carrier.GetLaunchVelocity() * surfaceMod.carrierFriction.Evaluate(GetSurfaceAngle());
                    carrier.Detach();
                }
            }
            // Volume launch: pure aim direction, no hyper dash, no carrier

            // Rope rebound: add stored-sag displacement as a launch-direction speed bonus.
            // Freeze was removed from Charging, so the rope keeps sagging under slime weight
            // through the charge; GetMaxSagDistance() reads that stored displacement.
            // Capped by rope.MaxReboundSpeed so deep sag × high multiplier can't produce
            // unbounded launches — mirrors SpringyBranchPlatform.maxTipSpeed.
            if (currentRope != null)
            {
                float sag = currentRope.GetMaxSagDistance();
                if (sag > 1e-4f)
                {
                    Vector2 launchDir = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : dir;
                    float bonusMag = sag * currentRope.ReboundSpeedPerMeter;
                    float cap = currentRope.MaxReboundSpeed;
                    if (cap > 0f && bonusMag > cap) bonusMag = cap;
                    velocity += launchDir * bonusMag;
                }
            }

            // Inside drag zone: reduce launch velocity. Exit multiplier restores on leaving.
            if (CurrentDragZone != null)
                velocity *= CurrentDragZone.launchMultiplier;

            isVolumeCharging = false;
            FinalizeAirborneExit();
            postLaunchAirControlLockTimer = settings.postLaunchAirControlLockTime;
            if (chargeVisual != null) chargeVisual.Hide();
            OnLaunch?.Invoke(rb.position, velocity, false);
        }

        private void Hop()
        {
            // Precondition: must be called at end of FixedUpdateAttached,
            // after velocity = surfaceTangent * (slideSpeed + crawlVelocity) — purely tangent.
            // Hop abandons the charge chain, so discard any pending coyote conversion
            // and apply the deferred floor reset that the no-Space landing skipped.
            ClearPendingEnergyConversion(executeDeferredReset: true);
            chargeEntryCrawl = crawlVelocity;
            float angle = GetSurfaceAngle();
            float hopT = Mathf.InverseLerp(settings.hopMinAngle, settings.hopMaxAngle, angle);
            float power = Mathf.Lerp(settings.hopPower, settings.hopPowerMin, hopT);
            Vector2 hopDir = settings.hopAlongSurfaceNormal ? surfaceNormal : gravityUp;

            // Surface-defined tangent retention. 1 = preserve (ice), 0 = wipe (sticky mud).
            float retention = surfaceMod.hopVelocityRetention.Evaluate(angle);
            float tangentSpeed = Vector2.Dot(velocity, surfaceTangent);
            float preservedTangent = tangentSpeed * retention;
            velocity = surfaceTangent * preservedTangent;

            if (stickTimer <= 0f)
            {
                // Hop momentum:
                //   same-direction input   → add bonus (capped by hopMomentumMaxSpeed).
                //   opposing-direction input → surface-defined:
                //       hopInstantFlip = false → decelerate toward 0 by bonus (inertial, e.g. ice).
                //       hopInstantFlip = true  → zero existing + bonus in input direction (arcade, e.g. grass).
                //   neutral input → no change.
                float inputX = inputReader.LastInputMove.x;
                if (settings.hopMomentumBonus > 0f && Mathf.Abs(inputX) > 0.01f)
                {
                    float inputSign = -Mathf.Sign(inputX);
                    float currentSpeedSign = Mathf.Sign(preservedTangent);
                    float newTangent;
                    if (currentSpeedSign == 0f || currentSpeedSign == inputSign)
                    {
                        float currentInDir = preservedTangent * inputSign;
                        float boosted = Mathf.Min(currentInDir + settings.hopMomentumBonus,
                                                  settings.hopMomentumMaxSpeed);
                        newTangent = inputSign * boosted;
                    }
                    else if (surfaceMod.hopInstantFlip)
                    {
                        float boosted = Mathf.Min(settings.hopMomentumBonus, settings.hopMomentumMaxSpeed);
                        newTangent = inputSign * boosted;
                    }
                    else
                    {
                        newTangent = Mathf.MoveTowards(preservedTangent, 0f, settings.hopMomentumBonus);
                    }
                    velocity += surfaceTangent * (newTangent - preservedTangent);
                }
            }
            velocity += hopDir * power;
            crawlVelocity = 0f;
            stickTimer = 0f;
            stickDeccelTimer = 0f;

            ignoredColliderList.RegisterOverlapping(
                rb.position,
                settings.slimeRadius + settings.launchClearanceThreshold,
                settings.groundLayer);

            if (carrier != null)
            {
                velocity += carrier.GetLaunchVelocity() * surfaceMod.carrierFriction.Evaluate(GetSurfaceAngle());
                carrier.Detach();
            }
            hopCooldownTimer = settings.hopCooldown;
            preserveLandingVelocity = true;
            FinalizeAirborneExit();
            launchedByHop = true;
            OnLaunch?.Invoke(rb.position, velocity, true);
        }

        private void ApplyGroundDash(float inputSign)
        {
            float dashAngle = GetSurfaceAngle();
            float dashAngleScale = 1f - Mathf.InverseLerp(settings.groundDashAngleMin, settings.groundDashAngleMax, dashAngle);
            float dashStrength = settings.groundDashImpulse * dashAngleScale * surfaceMod.groundDashScale.Evaluate(dashAngle);
            if (dashStrength > 0f)
            {
                float sameDirSpeed = Mathf.Max(0f, inputSign * crawlVelocity);
                float falloff = 1f - Mathf.InverseLerp(settings.dashFalloffStart, settings.dashFalloffEnd, sameDirSpeed);
                crawlVelocity += inputSign * dashStrength * falloff;
                dashCooldownTimer = settings.groundDashCooldown;
            }
        }
    }
}
