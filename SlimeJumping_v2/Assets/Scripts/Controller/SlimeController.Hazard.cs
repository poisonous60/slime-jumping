using UnityEngine;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        /// Hazard-driven push channel. Adds the deltaV through the same external-velocity
        /// channel as wind/conveyors, but ALSO fires OnHazardPushed so visual systems
        /// (SlimeBurnVisual etc.) can react. Used by GraniteBlast hits — no stun, no
        /// input lockout, just pushes the slime around with a side signal.
        /// hitPoint is the world position of the actual contact (used by visuals to
        /// place burn-mark decals); pass slime.Position when no specific contact exists.
        public void ApplyHazardPush(Vector2 deltaV, Vector2 hitPoint)
        {
            externalDeltaV += deltaV;
            OnHazardPushed?.Invoke(deltaV, hitPoint);
        }

        /// Hazard-driven OVERWRITE channel. While Attached/Charging, replaces the
        /// persistent surface-slide momentum (crawlVelocity / slideVelocity) with the
        /// deltaV's tangent component instead of accumulating onto it — so a slime
        /// already sliding at full speed gets pinned to the new push value, not
        /// pushed-on-top-of-existing-motion. Airborne/Stunned: behaves identically to
        /// ApplyHazardPush (the slide channels aren't active there). Gravity-driven
        /// slope slideSpeed is still recomputed each frame by FixedUpdateAttached, so
        /// downhill momentum returns naturally once the hazard stops calling.
        /// Used by GraniteBlast Beam mode while a slime stays inside a tracking beam.
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

        /// Fired whenever ApplyHazardPush / ApplyHazardPushOverwriteSlide is called. Payload: (deltaV, worldHitPoint).
        /// Subscribers: SlimeBurnVisual (places burn-mark decals at hit point + drives
        /// stuck-compression visual).
        public event System.Action<Vector2, Vector2> OnHazardPushed;

        // --- Hazard API ---

        /// <summary>
        /// Single entry point for all hazard contact. Transitions to Stunned state
        /// with configurable knockback and duration.
        /// </summary>
        /// Overload that also fires OnHazardPushed at a known contact point so visual
        /// systems (SlimeBurnVisual, etc.) can react to stun-channel hits the same way
        /// they do to push-channel hits. The Stunned i-frame guard still applies — if
        /// the slime is already stunned, neither the stun nor the burn fires.
        public void ApplyHazardEffect(HazardEffect effect, Vector2 hitPoint)
        {
            if (currentState == SlimeState.Stunned) return;
            ApplyHazardEffect(effect);
            // Use the knockback as the deltaV payload so subscribers (e.g. burn flash
            // intensity) can scale by impact strength. Sized to match push-channel
            // semantics: deltaV in m/s.
            Vector2 deltaV = effect.knockbackDirection.sqrMagnitude > 0.0001f
                ? effect.knockbackDirection.normalized * effect.knockbackForce
                : Vector2.up * effect.knockbackForce;
            OnHazardPushed?.Invoke(deltaV, hitPoint);
        }

        public void ApplyHazardEffect(HazardEffect effect)
        {
            if (_override != null) return;
            // TODO: time-based i-frame (stunIFrameDuration) for hazard variety.
            // Current policy: stunned = immune. Sufficient for Phase 0-1.
            // For Phase 5+ (lightning after saw blade), split into time-based window
            // so stun expiry can be immediately followed by a new hazard hit.
            if (currentState == SlimeState.Stunned) return;

            // Detach
            if (carrier != null) carrier.Detach();
            ClearPlatformEffect();
            surfaceMod = settings.BuildDefaultModifiers();

            // Compute knockback direction
            Vector2 kbDir = effect.knockbackDirection;
            if (kbDir.sqrMagnitude < 0.0001f)
                kbDir = gravityUp;

            // Apply knockback velocity
            velocity = kbDir.normalized * effect.knockbackForce;

            // Reset state fields
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

            // Enter Stunned
            stunTimer = effect.stunDuration;
            lastDamageType = effect.damageType;
            launchSpeed = 0f;
            launchPosition = rb.position;
            conversionCount = 0;
            RegisterLaunchClearance();

            ChangeState(SlimeState.Stunned, intentional: true);
        }
    }
}
