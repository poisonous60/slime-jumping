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
            surfaceMod = settings.BuildDefaultModifiers();
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
            slideVelocity = 0f;
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
            float preLaunchCrawl = settings.hyperDashInertiaSource == HyperDashInertiaSource.LaunchMoment
                ? (crawlVelocity + slideVelocity)
                : chargeEntryCrawl;
            chargeEntryCrawl = 0f;
            crawlVelocity = 0f;
            slideVelocity = 0f;

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
            // Snow burial nerf: depthToLaunchScale curve on the platform's SnowSettings
            // maps current snow coverage (0..1) → power scalar. Default flat 1.0 means
            // no effect; designers pull the right side down to weaken jumps from
            // deeply-covered snow. snowDepthRatio01 was refreshed by the most recent
            // FixedUpdate's RefreshSnowBlend.
            if (cachedSnowField != null)
            {
                var snowSettings = cachedSnowField.settings;
                if (snowSettings != null && snowSettings.depthToLaunchScale != null)
                {
                    float scale = snowSettings.depthToLaunchScale.Evaluate(snowDepthRatio01);
                    if (scale < 0f) scale = 0f;
                    power *= scale;
                }
            }
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
            chargeEntryCrawl = crawlVelocity + slideVelocity;
            float angle = GetSurfaceAngle();
            float hopT = Mathf.InverseLerp(settings.hopMinAngle, settings.hopMaxAngle, angle);
            float power = Mathf.Lerp(settings.hopPower, settings.hopPowerMin, hopT);
            Vector2 hopDir = settings.hopAlongSurfaceNormal ? surfaceNormal : gravityUp;

            // Tangent and normal components of incoming velocity are scaled independently:
            //   tangent: hopVelocityRetention   (1 = ice preserves tangent, 0 = sticky mud wipes)
            //   normal:  hopNormalToTangentRatio (0 = discard normal speed [legacy],
            //                                      >0 = redirect |normal| into tangent at this ratio)
            // Normal-redirect direction follows the existing tangent sign, falling back to input
            // direction when tangent speed is zero so a perpendicular drop into a flat surface
            // still slingshots in the player's intended direction.
            float tangentSpeed = Vector2.Dot(velocity, surfaceTangent);
            float normalSpeed = Vector2.Dot(velocity, surfaceNormal);
            float retention = surfaceMod.hopVelocityRetention.Evaluate(angle);
            float normalToTangent = surfaceMod.hopNormalToTangentRatio.Evaluate(angle);
            float preservedTangent = tangentSpeed * retention;
            if (normalToTangent > 0f)
            {
                float sign = tangentSpeed != 0f
                    ? Mathf.Sign(tangentSpeed)
                    : -Mathf.Sign(inputReader.LastInputMove.x);
                preservedTangent += sign * Mathf.Abs(normalSpeed) * normalToTangent;
            }
            velocity = surfaceTangent * preservedTangent;

            if (stickTimer <= 0f)
            {
                // Hop momentum:
                //   same-direction input   → add bonus (capped by hopMomentumMaxSpeed).
                //   opposing-direction input → surface-defined:
                //       hopInstantFlip = false → decelerate toward 0 by bonus (inertial, e.g. ice).
                //       hopInstantFlip = true  → zero existing + bonus in input direction (arcade, e.g. grass).
                //   neutral input → no change.
                // SurfaceMaterial.overrideHopMomentum = true → use the SurfaceMaterial's own
                // hopMomentumBonus / hopMomentumMaxSpeed (per-surface tuning, e.g. icier ice).
                float inputX = inputReader.LastInputMove.x;
                float bonus = surfaceMod.overrideHopMomentum ? surfaceMod.hopMomentumBonus : settings.hopMomentumBonus;
                float maxSpeed = surfaceMod.overrideHopMomentum ? surfaceMod.hopMomentumMaxSpeed : settings.hopMomentumMaxSpeed;
                if (bonus > 0f && Mathf.Abs(inputX) > 0.01f)
                {
                    float inputSign = -Mathf.Sign(inputX);
                    float currentSpeedSign = Mathf.Sign(preservedTangent);
                    float newTangent;
                    if (currentSpeedSign == 0f || currentSpeedSign == inputSign)
                    {
                        float currentInDir = preservedTangent * inputSign;
                        float boosted = Mathf.Min(currentInDir + bonus, maxSpeed);
                        newTangent = inputSign * boosted;
                    }
                    else if (surfaceMod.hopInstantFlip)
                    {
                        float boosted = Mathf.Min(bonus, maxSpeed);
                        newTangent = inputSign * boosted;
                    }
                    else
                    {
                        newTangent = Mathf.MoveTowards(preservedTangent, 0f, bonus);
                    }
                    velocity += surfaceTangent * (newTangent - preservedTangent);
                }
            }
            velocity += hopDir * power;
            crawlVelocity = 0f;
            slideVelocity = 0f;
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
            // SurfaceMaterial.overrideGroundDash = true → use the SurfaceMaterial's own
            // impulse / angle window / falloff (per-surface tuning, e.g. ice with longer falloff).
            // groundDashScale (multiplier) still applies on top so existing 0-on-this-surface
            // disables remain functional.
            float impulse = surfaceMod.overrideGroundDash ? surfaceMod.groundDashImpulse : settings.groundDashImpulse;
            float angleMin = surfaceMod.overrideGroundDash ? surfaceMod.groundDashAngleMin : settings.groundDashAngleMin;
            float angleMax = surfaceMod.overrideGroundDash ? surfaceMod.groundDashAngleMax : settings.groundDashAngleMax;
            float falloffStart = surfaceMod.overrideGroundDash ? surfaceMod.dashFalloffStart : settings.dashFalloffStart;
            float falloffEnd = surfaceMod.overrideGroundDash ? surfaceMod.dashFalloffEnd : settings.dashFalloffEnd;

            // Override surfaces evaluate angleScale through their own AnimationCurve. When
            // override is off, fall back to SlimeSettings' global curve. Empty/null curves
            // collapse to legacy 1 - InverseLerp(min, max, angle) for backward compatibility.
            AnimationCurve angleCurve = surfaceMod.overrideGroundDash
                ? surfaceMod.groundDashAngleFalloffCurve
                : settings.groundDashAngleFalloffCurve;
            float angleT = Mathf.InverseLerp(angleMin, angleMax, dashAngle);
            float dashAngleScale = (angleCurve != null && angleCurve.length > 0)
                ? angleCurve.Evaluate(angleT)
                : 1f - angleT;
            float dashStrength = impulse * dashAngleScale * surfaceMod.groundDashScale.Evaluate(dashAngle);
            if (dashStrength > 0f)
            {
                // Inertial surfaces: read falloff from combined speed and impulse the inertial
                // channel so the dash boost persists as kinetic energy through curves.
                float currentTangent = surfaceMod.useInertialSlide ? (crawlVelocity + slideVelocity) : crawlVelocity;
                float sameDirSpeed = Mathf.Max(0f, inputSign * currentTangent);
                bool skipFalloff = surfaceMod.overrideGroundDash
                    ? surfaceMod.disableDashFalloff
                    : settings.disableDashFalloff;
                AnimationCurve speedCurve = surfaceMod.overrideGroundDash
                    ? surfaceMod.dashSpeedFalloffCurve
                    : settings.dashSpeedFalloffCurve;
                float falloff;
                if (skipFalloff)
                {
                    falloff = 1f;
                }
                else
                {
                    float t = Mathf.InverseLerp(falloffStart, falloffEnd, sameDirSpeed);
                    falloff = (speedCurve != null && speedCurve.length > 0)
                        ? speedCurve.Evaluate(t)
                        : 1f - t;
                }
                if (surfaceMod.useInertialSlide)
                    slideVelocity += inputSign * dashStrength * falloff;
                else
                    crawlVelocity += inputSign * dashStrength * falloff;
                dashCooldownTimer = settings.groundDashCooldown;
            }
        }
    }
}
