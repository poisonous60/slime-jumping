using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Physics;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // Ignores overlapping ground colliders so a launch can punch out of the surface it was
        // glued to without instantly re-colliding.
        private void RegisterLaunchClearance()
        {
            ignoredColliderList.RegisterOverlapping(
                rb.position,
                SlimeRadius + settings.launchClearanceThreshold,
                settings.groundLayer);
        }

        /// <summary>Shared tail for all exit-to-airborne transitions; caller sets velocity and does state-specific cleanup first.</summary>
        private void FinalizeAirborneExit()
        {
            if (currentRope != null)
                currentRope.NotifyLaunchRebound(rb.position);

            ClearPlatformEffect();
            surfaceMod = settings.BuildDefaultModifiers();
            launchSpeed = velocity.magnitude;
            launchPosition = rb.position;
            // Clear friction-detach state, else the frame counter and visual ratio bleed into the next flight.
            sameSignFrames = 0;
            cornerStressRatio = 0f;
            smoothedCornerStressRatio = 0f;
            ChangeState(SlimeState.Airborne, intentional: true);
            postLaunchTimer = settings.postLaunchIgnoreTime;
        }

        /// <summary>Forces the slime into Airborne with the given velocity (JumpPad, Catapult, other IPlatformEffect).</summary>
        public void ForceAirborneWithVelocity(Vector2 vel)
        {
            velocity = vel;
            crawlVelocity = 0f;
            slideVelocity = 0f;
            chargeEntryCrawl = 0f;
            stickTimer = 0f;
            stickDeccelTimer = 0f;
            slideRampTimer = 0f;

            if (currentState == SlimeState.Charging)
                aimOffset = Vector2.zero;

            if (carrier != null) carrier.Detach();

            RegisterLaunchClearance();

            conversionCount = 0;
            // Skip the deferred floor reset: a pending one from the prior no-Space landing
            // leaks into the next flight and fires mid-chain for the wrong landing.
            ClearPendingEnergyConversion(executeDeferredReset: false);
            FinalizeAirborneExit();
        }

        private void Launch()
        {
            chargeBufferTimer = 0f;
            float preLaunchCrawl = settings.hyperDashInertiaSource == HyperDashInertiaSource.LaunchMoment
                ? (crawlVelocity + slideVelocity)
                : chargeEntryCrawl;
            launchOriginSlideV = preLaunchCrawl;
            launchOriginValid = true;
            chargeEntryCrawl = 0f;
            crawlVelocity = 0f;
            slideVelocity = 0f;

            RegisterLaunchClearance();

            bool isEnergyConversion = activeChargeRadius > settings.chargeRadius;
            activeChargeRadius = settings.chargeRadius;
            float ratio = aimOffset.magnitude / settings.chargeRadius;
            float power = LaunchPowerForRatio(ratio, isEnergyConversion);
            // Wounded (mid-slice-regen) → reduced jump. 1 = normal (see SlimeController.Split.cs).
            power *= SplitLaunchMultiplier;
            launchOriginAimPower = power;
            Vector2 dir = aimOffset.sqrMagnitude > 0.0001f ? -aimOffset.normalized : lastSurfaceNormal;
            // Bamboo tip-grab aims like a ground charge (aimOffset already integrated), so only
            // the power knob is grab-specific; the swing speed arrives via carrier inheritance below.
            if (currentBambooGrab != null)
            {
                power *= currentBambooGrab.GrabLaunchPowerScale;
            }
            velocity = dir * power;

            // Hyper dash: inherit pre-launch tangent inertia (above the deadzone), blended per surface.
            float excess = Mathf.Max(0f, Mathf.Abs(preLaunchCrawl) - settings.hyperDashDeduction);
            Vector2 inheritedVec = lastSurfaceTangent * Mathf.Sign(preLaunchCrawl) * excess * settings.tangentInheritanceFactor;
            velocity = BlendLaunchInertia(velocity, inheritedVec, surfaceMod);
            if (carrier != null)
            {
                velocity += carrier.GetLaunchVelocity() * surfaceMod.carrierFriction.Evaluate(GetSurfaceAngle());
                carrier.Detach();
            }

            // Rope owns its bonus formula (IRopePlatform.GetLaunchBonus); every impl clamps internally.
            if (currentRope != null)
            {
                Vector2 launchDir = velocity.sqrMagnitude > 0.0001f ? velocity.normalized : dir;
                float bonusMag = currentRope.GetLaunchBonus(launchDir, rb.position, ratio);
                if (bonusMag > 0f)
                    velocity += launchDir * bonusMag;
            }

            if (CurrentDragZone != null)
                velocity *= CurrentDragZone.launchMultiplier;

            // TautLine raises the upward component to a minimum here, else a near-horizontal
            // launch is immediately recaught by the rope's spring rebound.
            if (currentRope != null)
                velocity = currentRope.PostprocessLaunchVelocity(velocity);

            TryApplyBambooV2LaunchReleaseKick(velocity);

            // Fire before FinalizeAirborneExit while ActiveSurfaceMaterial is still resolved —
            // audio needs it to pick the surface-correct launch clip.
            OnLaunch?.Invoke(rb.position, velocity, false);
            FinalizeAirborneExit();
            postLaunchAirControlLockTimer = settings.postLaunchAirControlLockTime;
        }

        // Blends aim + inherited tangent per the surface's LaunchInertiaBlendMode.
        // Direction always follows (L+I); only magnitude differs.
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
    }
}
