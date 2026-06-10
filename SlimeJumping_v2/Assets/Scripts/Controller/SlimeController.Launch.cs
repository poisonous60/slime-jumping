using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Physics;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- Launch ---

        // Registers every ground-layer collider currently overlapping the slime as a
        // launch-clearance ignore, so the slime can punch out of a surface it was glued
        // to without immediately re-colliding. Shared by Launch / ForceAirborneWithVelocity
        // / hazard knockback.
        private void RegisterLaunchClearance()
        {
            ignoredColliderList.RegisterOverlapping(
                rb.position,
                SlimeRadius + settings.launchClearanceThreshold,
                settings.groundLayer);
        }

        /// <summary>
        /// Shared tail for all exit-to-airborne transitions (Launch, ForceAirborneWithVelocity).
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

            if (currentState == SlimeState.Charging)
                aimOffset = Vector2.zero;

            if (carrier != null) carrier.Detach();

            RegisterLaunchClearance();

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
            // Bamboo tip-grab: the player aims the launch with WASD exactly like a ground
            // charge — aimOffset already integrates AimInput during Charging — so dir stays
            // the aim vector computed above. Only the power knob is grab-specific. The
            // bamboo's swing velocity is still added as momentum via the carrier inheritance
            // below, so a fast swing skews the aimed jump sideways; with no aim input the
            // drift (seeded from -surfaceNormal ≈ tip-up) still launches off the top.
            if (currentBambooGrab != null)
            {
                power *= currentBambooGrab.GrabLaunchPowerScale;
            }
            TryApplyBambooV2ChargedLaunch(ref dir, ref power);
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

            TryApplyBambooV2LaunchReleaseKick(velocity);

            // Fire OnLaunch BEFORE FinalizeAirborneExit so listeners can read
            // ActiveSurfaceMaterial (still resolved while attached) — audio reads it
            // to play the surface-correct launch clip without re-deriving snow vs host.
            OnLaunch?.Invoke(rb.position, velocity, false);
            FinalizeAirborneExit();
            postLaunchAirControlLockTimer = settings.postLaunchAirControlLockTime;
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
    }
}
