using UnityEngine;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        /// Hazard push: wind/conveyor external-velocity channel plus an OnHazardPushed signal for visuals. No stun.
        /// <param name="hitPoint">World contact point for decals; pass Position when there is no real contact.</param>
        public void ApplyHazardPush(Vector2 deltaV, Vector2 hitPoint)
        {
            externalDeltaV += deltaV;
            OnHazardPushed?.Invoke(deltaV, hitPoint);
        }

        /// Overwrite variant: while Attached/Charging, pins crawl/slide to deltaV's tangent instead of
        /// accumulating, so a continuous beam can't stack onto existing slide momentum. Airborne = plain push.
        public void ApplyHazardPushOverwriteSlide(Vector2 deltaV, Vector2 hitPoint)
        {
            if (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
            {
                Vector2 t = surfaceTangent;
                float setSpeed = t.sqrMagnitude > 0.0001f ? Vector2.Dot(deltaV, t) : 0f;
                crawlVelocity = setSpeed;
                slideVelocity = 0f;
            }
            else
            {
                externalDeltaV += deltaV;
            }
            OnHazardPushed?.Invoke(deltaV, hitPoint);
        }

        /// Payload: (deltaV, worldHitPoint). Subscriber: SlimeBurnVisual.
        public event System.Action<Vector2, Vector2> OnHazardPushed;

        // --- Hazard API ---

        /// <summary>Hazard contact → Stunned, and also fires OnHazardPushed at the contact point.</summary>
        /// Already-stunned slime fires neither the stun nor the burn (i-frame guard).
        public void ApplyHazardEffect(HazardEffect effect, Vector2 hitPoint)
        {
            if (currentState == SlimeState.Stunned) return;
            ApplyHazardEffect(effect);
            // Knockback doubles as the deltaV payload (m/s, same units as the push channel).
            Vector2 deltaV = effect.knockbackDirection.sqrMagnitude > 0.0001f
                ? effect.knockbackDirection.normalized * effect.knockbackForce
                : Vector2.up * effect.knockbackForce;
            OnHazardPushed?.Invoke(deltaV, hitPoint);
        }

        public void ApplyHazardEffect(HazardEffect effect)
        {
            if (_override != null) return;
            // TODO: time-based i-frame window; current policy is "stunned = fully immune",
            // which blocks a second hazard (lightning right after a saw blade) from landing.
            if (currentState == SlimeState.Stunned) return;

            if (carrier != null) carrier.Detach();
            ClearPlatformEffect();
            surfaceMod = settings.BuildDefaultModifiers();

            Vector2 kbDir = effect.knockbackDirection;
            if (kbDir.sqrMagnitude < 0.0001f)
                kbDir = gravityUp;

            velocity = kbDir.normalized * effect.knockbackForce;

            crawlVelocity = 0f;
            slideVelocity = 0f;
            chargeEntryCrawl = 0f;
            aimOffset = Vector2.zero;
            chargeFullAimTimer = 0f;
            coyoteTimer = 0f;
            ClearPendingEnergyConversion(executeDeferredReset: false);
            stickTimer = 0f;
            stickDeccelTimer = 0f;
            slideRampTimer = 0f;
            chargeBufferTimer = 0f;
            postLaunchTimer = 0f;
            postLaunchAirControlLockTimer = 0f;
            wasStuck = false;
            blockAttachCount = 0;
            inFluidCount = 0;
            surfaceMaterialOverrideStack.Clear();
            hasOverrideMods = false;

            stunTimer = effect.stunDuration;
            stunDuration = effect.stunDuration;
            lastDamageType = effect.damageType;
            launchSpeed = 0f;
            launchPosition = rb.position;
            conversionCount = 0;
            RegisterLaunchClearance();

            ChangeState(SlimeState.Stunned, intentional: true);
        }
    }
}
