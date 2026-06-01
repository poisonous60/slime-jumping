using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Physics;
using SlimeJumping.Platforms;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- State: Airborne ---

        private void FixedUpdateAirborne(float dt)
        {
            if (attachLockoutTimer > 0f)
                attachLockoutTimer -= dt;
            if (postLaunchAirControlLockTimer > 0f)
                postLaunchAirControlLockTimer -= dt;

            // Apex threshold locks air control while rising hard. In a fluid medium
            // (water etc.) the upward velocity comes from buoyancy, not a launch —
            // the player should still be able to steer. Bypass the apex gate while submerged.
            bool airControlActive = InFluid
                || (postLaunchAirControlLockTimer <= 0f
                    && Vector2.Dot(velocity, gravityUp) <= settings.apexThreshold);

            // Gravity
            // W/S scale picks rising vs falling variant by actual velocity direction —
            // independent of airControlActive, so steering still works while above apex.
            // In fluid: intentionally ignore velocity sign and use the dedicated in-fluid
            // values instead. Water movement is buoyancy-driven and has no clean apex, so
            // a rising/falling split would make submerged steering feel inconsistent.
            float gravityScale = 1f;
            float inputYRaw = inputReader.LastInputMove.y;
            if (InFluid)
            {
                if (inputYRaw > 0.1f) gravityScale = settings.floatGravityScaleInFluid;
                else if (inputYRaw < -0.1f) gravityScale = settings.diveGravityScaleInFluid;
            }
            else
            {
                bool isRisingByVelocity = Vector2.Dot(velocity, gravityUp) > 0f;
                if (inputYRaw > 0.1f)
                    gravityScale = isRisingByVelocity ? settings.floatGravityScaleRising : settings.floatGravityScaleFalling;
                else if (inputYRaw < -0.1f)
                    gravityScale = isRisingByVelocity ? settings.diveGravityScaleRising : settings.diveGravityScaleFalling;
            }

            // Asymmetric gravity uses the existing airControlActive split (apex-gated),
            // unchanged from before so rising/falling multipliers behave identically.
            float asymScale = airControlActive ? settings.fallingGravityScale : settings.risingGravityScale;
            velocity += -gravityUp * Mathf.Abs(settings.gravity) * gravityScale * asymScale * dt;

            // Land on any surface
            if (isTouchingSurface)
            {
                // Rope contact correction. EdgeCollider2D is a 1-D polyline with no
                // inherent outside; fast vertical motion can land the slime on the
                // underside. Detect that case by the contact normal pointing into
                // gravity, then place the slime's center exactly slimeRadius above
                // the rope's nearest point and re-orient the contact frame upward.
                // Position offset is exactly slimeRadius (not + contactSkin) so the
                // next FU's surface probe still registers overlap — otherwise the
                // slime detaches, gravity pulls it back through the polyline, and
                // we re-teleport every FU (visible as up-down oscillation).
                if (currentRope != null && surfaceCollider == currentRope.Edge
                    && Vector2.Dot(surfaceNormal, gravityUp) < 0f)
                {
                    Vector2 ropeContact = surfaceCollider.ClosestPoint(rb.position);
                    rb.position = ropeContact + gravityUp * settings.slimeRadius;
                    surfaceNormal = gravityUp;
                    surfaceTangent = new Vector2(-surfaceNormal.y, surfaceNormal.x);
                }

                float surfaceUp = Vector2.Dot(surfaceNormal, gravityUp);

                // Resolve per-surface ceiling reattach threshold from the candidate's
                // SurfaceTag — surfaceMod still reflects the previous attach, so we look
                // up from the new collider directly.
                SurfaceTag.ResolveCeilingThresholds(surfaceCollider, settings,
                    out _, out float landingReattachThreshold);

                // Ceiling: don't attach, just cancel normal velocity and stay airborne
                if (surfaceUp < landingReattachThreshold)
                {
                    surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);
                    return;
                }

                // Attach debounce: block landing during lockout, cancel normal velocity only
                if (attachLockoutTimer > 0f)
                {
                    surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);
                    return;
                }

                // BlockAttach (e.g. water zone): don't attach, cancel normal velocity.
                // Don't return — fall through to air control so slime can move away.
                if (blockAttachCount > 0)
                {
                    surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);
                }
                else
                {

                // Get landing platform velocity (including angular contribution) before attaching
                Vector2 landingPlatformVel = GetSurfaceVelocity(surfaceCollider);

                bool chargeRequested = chargeBufferTimer > 0f
                    || (settings.allowChargeAirHold && inputReader.IsJumpHeld);
                if (chargeRequested)
                    EnterChargingFromLanding(landingPlatformVel, surfaceUp);
                else
                    AttachFromLanding(landingPlatformVel, surfaceUp);
                return;
                } // end else (blockAttach)
            }

            // Air control: horizontal acceleration with speed cap (last-input-wins)
            // "Horizontal" = perpendicular to effective gravity, not necessarily world-X.
            if (airControlActive)
            {
                float inputX = inputReader.LastInputMove.x;
                if (Mathf.Abs(inputX) > 0.01f)
                {
                    float currentH = Vector2.Dot(velocity, gravityRight);
                    float targetH = inputX * settings.airMaxHorizontalSpeed;
                    float accel = (inputX * currentH >= 0f)
                        ? settings.airAcceleration
                        : settings.airDeceleration;

                    float newH;
                    if (Mathf.Abs(currentH) > settings.airMaxHorizontalSpeed)
                    {
                        // Already faster than cap (from launch) — only allow braking
                        if (inputX * currentH < 0)
                            newH = Mathf.MoveTowards(currentH, targetH, settings.airAcceleration * dt);
                        else
                            newH = currentH;
                    }
                    else
                    {
                        newH = Mathf.MoveTowards(currentH, targetH, accel * dt);
                    }

                    float gravComponent = Vector2.Dot(velocity, gravityUp);
                    velocity = gravityUp * gravComponent + gravityRight * newH;
                }
            }


        }

        // Common impact capture for the landing→Charging and landing→Attached transitions.
        // Records platform-relative impact velocity, attaches the carrier, resolves the new
        // surface material, then applies wall-attach dampening + impact-redirect. Returns the
        // landing surface angle; outputs the impact magnitude (for stick scaling) and the
        // post-redirect tangent speed.
        private float CaptureLandingImpact(Vector2 landingPlatformVel, out float impactMagnitude, out float effectiveTangent)
        {
            Vector2 relativeVel = velocity - landingPlatformVel;
            impactMagnitude = relativeVel.magnitude;
            float tangentSpeed = Vector2.Dot(relativeVel, surfaceTangent);
            float normalSpeed = Mathf.Abs(Vector2.Dot(relativeVel, surfaceNormal));
            impactTangentSpeed = tangentSpeed;
            impactNormalSpeed = normalSpeed;
            stickDeccelTimer = 0f;
            // currentRope was already set this frame by UpdateRopeReference — hand it the
            // pre-attach velocity here BEFORE the attach zeroes it.
            if (currentRope != null)
                currentRope.ApplyRiderImpact(rb.position, relativeVel);

            if (carrier != null) carrier.Attach(surfaceCollider);
            UpdateSurfaceModifiers(surfaceCollider);

            float landingAngle = GetSurfaceAngle();
            tangentSpeed *= surfaceMod.wallAttachDampen.Evaluate(landingAngle);
            float redirectSign = Mathf.Sign(Vector2.Dot(gravityUp, surfaceTangent));
            float redirectScale = 1f - Mathf.Abs(Vector2.Dot(surfaceNormal, gravityUp));
            effectiveTangent = tangentSpeed + normalSpeed * surfaceMod.impactRedirect.Evaluate(landingAngle) * redirectSign * redirectScale;
            return landingAngle;
        }

        // Routes the post-retention landed tangent speed into whichever channel the surface uses
        // (inertial → slideVelocity, legacy → crawlVelocity) and zeroes the airborne velocity.
        private void RouteLandedTangent(float landedTangent)
        {
            if (surfaceMod.useInertialSlide)
            {
                slideVelocity = landedTangent;
                crawlVelocity = 0f;
            }
            else
            {
                crawlVelocity = landedTangent;
                slideVelocity = 0f;
            }
            velocity = Vector2.zero;
        }

        // Shadow-velocity → energy-conversion spawn distance: inverts the launch power curve so
        // a rate=1 conversion preserves launch height. impactSpeed is the velocity the launch
        // energy would have if flown back down to the current height (a launch-rise-land returns
        // less than a launch-fall-land). Reads conversionCount for the rate; does NOT increment.
        private float ComputeEnergyConversionSpawnDistance(out float impactSpeed)
        {
            float heightDelta = Vector2.Dot(launchPosition - rb.position, gravityUp);
            float shadowSpeedSq = launchSpeed * launchSpeed + 2f * Mathf.Abs(settings.gravity) * heightDelta;
            impactSpeed = shadowSpeedSq > 0f ? Mathf.Sqrt(shadowSpeedSq) : 0f;
            float rate = (conversionCount == 0) ? settings.initialConversionRate : settings.subsequentConversionRate;
            float speedRatio = impactSpeed / settings.baseLaunchPower;
            float spawnDistance = settings.chargeRadius
                * Mathf.Pow(speedRatio, 1f / settings.chargeCurveExponent)
                * rate;
            if (launchSpeed == 0f) spawnDistance *= settings.notJumpConversionBonus;
            return spawnDistance;
        }

        // Space held (buffered) at landing → enter Charging with an energy-conversion-expanded radius.
        private void EnterChargingFromLanding(Vector2 landingPlatformVel, float surfaceUp)
        {
            chargeBufferTimer = 0f;
            float spawnDistance = ComputeEnergyConversionSpawnDistance(out float impactSpeed);
            conversionCount++;

            float landingAngle = CaptureLandingImpact(landingPlatformVel, out _, out float effectiveTangent);
            RouteLandedTangent(ComputeLandingCrawlVelocity(effectiveTangent, landingAngle));

            stickTimer = (surfaceUp >= settings.stickSlopeThreshold)
                ? 0f
                : CalculateStickDuration(impactSpeed) * surfaceMod.stickDuration.Evaluate(landingAngle);

            activeChargeRadius = Mathf.Max(spawnDistance, settings.chargeRadius);
            TryArmBambooV2Rebound(impactSpeed);
            aimOffset = Vector2.zero;
            chargeFullAimTimer = 0f;
            slideRampTimer = 0f;
            driftDirection = -surfaceNormal;
            prevSurfaceNormal = surfaceNormal;
            float conversionRatio = activeChargeRadius / settings.chargeRadius;
            chargeEntryCrawl *= 1f - Mathf.InverseLerp(settings.hyperDashConversionDampingMin, settings.hyperDashConversionDampingMax, conversionRatio);
            ChangeState(SlimeState.Charging);
            lastSurfaceNormal = surfaceNormal;
            lastSurfaceTangent = surfaceTangent;
            surfaceProbeMissFrames = 0;
            cachedSurfaceCollider = surfaceCollider;
            SnapToSurface();
            // Intentionally before CheckPlatformEffect: dust puff on JumpPad contact is natural.
            OnLanded?.Invoke(surfaceCollider.ClosestPoint(rb.position), surfaceNormal);
            // JumpPad etc. may call ForceAirborneWithVelocity here, flipping state back to Airborne.
            CheckPlatformEffect(surfaceCollider);
        }

        // No Space at landing → sticky landing (energy lost), and arm the energy-conversion coyote
        // so a Space press within the grace window still grants the would-have-been expanded radius.
        private void AttachFromLanding(Vector2 landingPlatformVel, float surfaceUp)
        {
            float landingAngle = CaptureLandingImpact(landingPlatformVel, out float landingSpeed, out float effectiveTangent);
            TryArmBambooV2Rebound(landingSpeed);
            RouteLandedTangent(ComputeLandingCrawlVelocity(effectiveTangent, landingAngle));

            // Per-surface override (SurfaceMaterial.overrideEnergyConversionCoyoteTime) lets springy
            // surfaces extend the post-landing window for late-Space energy conversion.
            bool isFloorLanding = surfaceUp >= settings.stickSlopeThreshold;
            bool coyoteArmed = false;
            float coyoteWindow = surfaceMod.GetEnergyConversionCoyoteTime(settings);
            if (coyoteWindow > 0f)
            {
                float candidate = Mathf.Max(ComputeEnergyConversionSpawnDistance(out _), settings.chargeRadius);
                if (candidate > settings.chargeRadius)
                {
                    pendingEnergyChargeRadius = candidate;
                    energyCoyoteTimer = coyoteWindow;
                    energyCoyoteDuration = coyoteWindow;
                    pendingFloorReset = isFloorLanding;
                    coyoteArmed = true;
                }
            }

            // conversionCount resets on floor landing only. Wall chains keep subsequentConversionRate.
            // When coyote is armed on a floor landing, the reset is deferred to pending expiry so a
            // coyote-consumed conversion continues the chain (matches the Space-held branch).
            if (isFloorLanding)
            {
                stickTimer = 0f;
                if (!coyoteArmed) conversionCount = 0;
            }
            else
                stickTimer = CalculateStickDuration(landingSpeed) * surfaceMod.stickDuration.Evaluate(landingAngle);

            slideRampTimer = 0f;
            ChangeState(SlimeState.Attached);
            SnapToSurface();
            OnLanded?.Invoke(surfaceCollider.ClosestPoint(rb.position), surfaceNormal);
            // JumpPad etc. may call ForceAirborneWithVelocity here, flipping state back to Airborne.
            CheckPlatformEffect(surfaceCollider);
        }

        private float ComputeLandingCrawlVelocity(float effectiveTangent, float landingAngle)
        {
            float landed = effectiveTangent * surfaceMod.landingVelocityRetention.Evaluate(landingAngle);

            // Charged-launch return: cap the EXCESS over the player's "explicit speed budget" by
            // landingExcessGain. Baseline = max(|preLaunchSlideV|, aimPower) so BOTH pre-launch slide
            // (natural inertia, must not be clipped on a fast-slide launch) AND the aim power the
            // player chose to spend (stationary launches must not collapse to zero) are preserved.
            // Only the residual gain — projection efficiency on curved surfaces, frictionless airtime,
            // gravity-from-height — is dialed.
            //
            // External-arrival flights leave launchOriginValid=false so this is a no-op for them,
            // preserving Inertial Slide / impact-redirect "ride up the curve" intact.
            if (launchOriginValid)
            {
                float baseline = Mathf.Max(Mathf.Abs(launchOriginSlideV), launchOriginAimPower);
                float absL = Mathf.Abs(landed);
                if (absL > baseline)
                {
                    float excess = absL - baseline;
                    float gain = surfaceMod.landingExcessGain.Evaluate(landingAngle);
                    landed = Mathf.Sign(landed) * (baseline + excess * gain);
                }
                launchOriginValid = false;
            }

            // Optional non-linear remap of final tangent speed (saturating curve for ice etc.).
            // Toggle gates the feature so authored curves can be temporarily disabled without
            // losing keys. Applied last so it covers regular and charged-launch landings.
            if (surfaceMod.enableLandingTangentSpeedRemap)
            {
                var remap = surfaceMod.landingTangentSpeedRemapCurve;
                if (remap != null && remap.length > 0)
                {
                    landed = Mathf.Sign(landed) * remap.Evaluate(Mathf.Abs(landed));
                }
            }

            return landed;
        }

        private float CalculateStickDuration(float impactSpeed)
        {
            float t = 1f - 1f / (1f + impactSpeed * settings.stickSpeedScale);
            return Mathf.Lerp(settings.minStickDuration, settings.maxStickDuration, t);
        }
    }
}
