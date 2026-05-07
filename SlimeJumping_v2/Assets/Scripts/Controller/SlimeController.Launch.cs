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
            // Friction-detach state must clear so the next attach starts a fresh
            // sustained-frame counter and the visual ratio doesn't bleed across flights.
            sameSignFrames = 0;
            cornerStressRatio = 0f;
            smoothedCornerStressRatio = 0f;
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
            launchOriginSlideV = preLaunchCrawl;
            launchOriginValid = true;
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
            launchOriginAimPower = power;
            Vector2 dir = aimOffset.sqrMagnitude > 0.0001f ? -aimOffset.normalized : lastSurfaceNormal;
            velocity = dir * power;

            if (!isVolumeCharging)
            {
                // Surface launch: hyper dash + carrier
                float excess = Mathf.Max(0f, Mathf.Abs(preLaunchCrawl) - settings.hyperDashDeduction);
                Vector2 inheritedVec = lastSurfaceTangent * Mathf.Sign(preLaunchCrawl) * excess * settings.tangentInheritanceFactor;
                velocity = BlendLaunchInertia(velocity, inheritedVec, surfaceMod);
                if (carrier != null)
                {
                    velocity += carrier.GetLaunchVelocity() * surfaceMod.carrierFriction.Evaluate(GetSurfaceAngle());
                    carrier.Detach();
                }
            }
            // Volume launch: pure aim direction, no hyper dash, no carrier

            // Rope rebound: rope owns its own bonus formula via IRopePlatform.GetLaunchBonus.
            //   v1 (Rope)   → sag × reboundSpeedPerMeter (timing-blind, ignores chargeRatio)
            //   v2 (RopeV2) → timingBonusCurve(proj) × chargeMultiplierCurve(chargeRatio)
            // Both implementations clamp to [0, MaxReboundSpeed] internally.
            if (currentRope != null)
            {
                Vector2 launchDir = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : dir;
                float bonusMag = currentRope.GetLaunchBonus(launchDir, rb.position, ratio);
                if (bonusMag > 0f)
                    velocity += launchDir * bonusMag;
            }

            // Inside drag zone: reduce launch velocity. Exit multiplier restores on leaving.
            if (CurrentDragZone != null)
                velocity *= CurrentDragZone.launchMultiplier;

            // Final pass on the launch vector — TautLine clamps the upward component up
            // to a minimum so a near-horizontal launch isn't immediately recaught by the
            // rope's spring rebound. Other rope variants pass through.
            if (currentRope != null)
                velocity = currentRope.PostprocessLaunchVelocity(velocity);

            isVolumeCharging = false;
            // Fire OnLaunch BEFORE FinalizeAirborneExit so listeners can read
            // ActiveSurfaceMaterial (still resolved while attached) — audio reads it
            // to play the surface-correct launch clip without re-deriving snow vs host.
            OnLaunch?.Invoke(rb.position, velocity, false);
            FinalizeAirborneExit();
            postLaunchAirControlLockTimer = settings.postLaunchAirControlLockTime;
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
            float power = Mathf.Lerp(settings.hopPower, settings.hopPowerMin, hopT)
                          * surfaceMod.hopPowerMultiplier.Evaluate(angle);
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
            // Fire OnLaunch BEFORE FinalizeAirborneExit so listeners can read
            // ActiveSurfaceMaterial (still resolved while attached) — audio reads it
            // to play the surface-correct hop clip without re-deriving snow vs host.
            // wasHop=true is passed directly via the event payload; the launchedByHop
            // field is assigned AFTER FinalizeAirborneExit (which resets it to false)
            // so post-launch code reading the field still sees hop semantics.
            OnLaunch?.Invoke(rb.position, velocity, true);
            FinalizeAirborneExit();
            launchedByHop = true;
        }

        // Blends the aim-launch vector with the inherited tangent vector per the surface's
        // LaunchInertiaBlendMode. Direction always follows (L+I); only magnitude differs.
        // Sum reproduces legacy `velocity += inherited` exactly (so untagged surfaces are
        // bit-identical to pre-feature behavior).
        private static Vector2 BlendLaunchInertia(Vector2 launchVec, Vector2 inheritedVec, SurfaceModifiers mod)
        {
            if (inheritedVec.sqrMagnitude < 1e-8f)
                return launchVec;
            Vector2 sum = launchVec + inheritedVec;
            if (sum.sqrMagnitude < 1e-8f)
                return launchVec;

            switch (mod.launchInertiaBlendMode)
            {
                case LaunchInertiaBlendMode.LerpMaxToSum:
                {
                    float maxMag = Mathf.Max(launchVec.magnitude, inheritedVec.magnitude);
                    float sumMag = sum.magnitude;
                    float blendedMag = Mathf.Lerp(maxMag, sumMag, Mathf.Clamp01(mod.launchInertiaLerpFactor));
                    return sum.normalized * blendedMag;
                }
                case LaunchInertiaBlendMode.PNorm:
                {
                    float p = Mathf.Max(1f, mod.launchInertiaPNorm);
                    float lMag = launchVec.magnitude;
                    float iMag = inheritedVec.magnitude;
                    float blendedMag = Mathf.Pow(Mathf.Pow(lMag, p) + Mathf.Pow(iMag, p), 1f / p);
                    return sum.normalized * blendedMag;
                }
                case LaunchInertiaBlendMode.Sum:
                default:
                    return sum;
            }
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
