using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Physics;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Launch / Hop / Ground Dash ---

        private void Launch()
        {
            preserveLandingVelocity = false;
            hopBufferTimer = 0f;
            dashBufferTimer = 0f;
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
                    velocity += carrier.GetEffectiveVelocity() * surfaceMod.carrierFriction.Evaluate(GetSurfaceAngle());
                    carrier.Detach();
                }
            }
            // Volume launch: pure aim direction, no hyper dash, no carrier

            // Inside drag zone: reduce launch velocity. Exit multiplier restores on leaving.
            if (CurrentDragZone != null)
                velocity *= CurrentDragZone.launchMultiplier;

            isVolumeCharging = false;
            surfaceMod = SurfaceModifiers.Default;
            launchSpeed = velocity.magnitude;
            launchPosition = rb.position;

            ChangeState(SlimeState.Airborne, intentional: true);
            postLaunchTimer = settings.postLaunchIgnoreTime;

            if (chargeVisual != null) chargeVisual.Hide();
        }

        private void Hop()
        {
            // Precondition: must be called at end of FixedUpdateAttached,
            // after velocity = surfaceTangent * (slideSpeed + crawlVelocity).
            // During stick, velocity holds impact decel residual — start from zero.
            // During normal operation, velocity = surfaceTangent * (slide + crawl) — preserve it.
            chargeEntryCrawl = crawlVelocity;
            float angle = GetSurfaceAngle();
            float hopT = Mathf.InverseLerp(settings.hopMinAngle, settings.hopMaxAngle, angle);
            float power = Mathf.Lerp(settings.hopPower, settings.hopPowerMin, hopT);
            if (stickTimer > 0f)
            {
                // Always hop in gravity-up direction, not along surfaceNormal
                velocity = gravityUp * power;
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
                // Always hop in gravity-up direction, not along surfaceNormal
                velocity += gravityUp * power;
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
                velocity += carrier.GetEffectiveVelocity() * surfaceMod.carrierFriction.Evaluate(GetSurfaceAngle());
                carrier.Detach();
            }
            surfaceMod = SurfaceModifiers.Default;
            launchSpeed = velocity.magnitude;
            launchPosition = rb.position;
            hopCooldownTimer = settings.hopCooldown;

            preserveLandingVelocity = true;
            ChangeState(SlimeState.Airborne, intentional: true);
            postLaunchTimer = settings.postLaunchIgnoreTime;
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
