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
            // after velocity = surfaceTangent * (slideSpeed + crawlVelocity).
            // During stick, velocity holds impact decel residual — start from zero.
            // During normal operation, velocity = surfaceTangent * (slide + crawl) — preserve it.
            // Hop abandons the charge chain, so discard any pending coyote conversion
            // and apply the deferred floor reset that the no-Space landing skipped.
            ClearPendingEnergyConversion(executeDeferredReset: true);
            chargeEntryCrawl = crawlVelocity;
            float angle = GetSurfaceAngle();
            float hopT = Mathf.InverseLerp(settings.hopMinAngle, settings.hopMaxAngle, angle);
            float power = Mathf.Lerp(settings.hopPower, settings.hopPowerMin, hopT);
            Vector2 hopDir = settings.hopAlongSurfaceNormal ? surfaceNormal : gravityUp;
            if (stickTimer > 0f)
            {
                velocity = hopDir * power;
            }
            else
            {
                // Hop momentum: boost in A/D input direction, neutral = no bonus
                float inputX = inputReader.LastInputMove.x;
                if (settings.hopMomentumBonus > 0f && Mathf.Abs(inputX) > 0.01f)
                {
                    float tangentSpeed = Vector2.Dot(velocity, surfaceTangent);
                    float inputSign = -Mathf.Sign(inputX);
                    float currentInDir = tangentSpeed * inputSign;
                    float boosted = Mathf.Min(Mathf.Max(currentInDir, 0f) + settings.hopMomentumBonus,
                                              settings.hopMomentumMaxSpeed);
                    velocity += surfaceTangent * (inputSign * boosted - tangentSpeed);
                }
                velocity += hopDir * power;
            }
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
