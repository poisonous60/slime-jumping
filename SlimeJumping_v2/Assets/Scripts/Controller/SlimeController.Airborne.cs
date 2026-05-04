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

            // Air Dash: suspend gravity + air control for the remainder of the dash window.
            // Landing logic below still runs, so touching a surface mid-dash lands normally
            // and ChangeState zeroes the timer.
            bool airDashSuppressesPhysics = airDashActiveTimer > 0f;
            if (airDashSuppressesPhysics)
            {
                airDashActiveTimer -= dt;
                if (airDashActiveTimer < 0f) airDashActiveTimer = 0f;
            }

            // Apex threshold locks air control while rising hard. In a fluid medium
            // (water etc.) the upward velocity comes from buoyancy, not a launch —
            // the player should still be able to steer. Bypass the apex gate while submerged.
            bool airControlActive = InFluid
                || (postLaunchAirControlLockTimer <= 0f
                    && Vector2.Dot(velocity, gravityUp) <= settings.apexThreshold);

            if (!airDashSuppressesPhysics)
            {
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
            }

            // Land on any surface
            if (isTouchingSurface)
            {
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
                {
                    // Energy conversion: invert the launch curve so rate=1 preserves height.
                    // Launch: power = (aim/chargeRadius)^chargeCurveExponent * baseLaunchPower
                    // Inverse: aim = chargeRadius * (impactSpeed/baseLaunchPower)^(1/exponent)
                    float heightDelta = Vector2.Dot(launchPosition - rb.position, gravityUp);
                    float shadowSpeedSq = launchSpeed * launchSpeed
                        + 2f * Mathf.Abs(settings.gravity) * heightDelta;
                    if (launchedByHop)
                        shadowSpeedSq += settings.hopConversionEnergyBonus * settings.baseLaunchPower * settings.baseLaunchPower;
                    float impactSpeed = shadowSpeedSq > 0f ? Mathf.Sqrt(shadowSpeedSq) : 0f;
                    float rate = (conversionCount == 0) ? settings.initialConversionRate : settings.subsequentConversionRate;
                    float speedRatio = impactSpeed / settings.baseLaunchPower;
                    float spawnDistance = settings.chargeRadius
                        * Mathf.Pow(speedRatio, 1f / settings.chargeCurveExponent)
                        * rate;
                    if (launchSpeed == 0f) spawnDistance *= settings.notJumpConversionBonus;
                    if (launchedByHop) spawnDistance *= settings.hopConversionBonus;
                    conversionCount++;

                    Vector2 relativeVel = velocity - landingPlatformVel;
                    float tangentSpeed = Vector2.Dot(relativeVel, surfaceTangent);
                    float normalSpeed = Mathf.Abs(Vector2.Dot(relativeVel, surfaceNormal));
                    impactTangentSpeed = tangentSpeed;
                    impactNormalSpeed = normalSpeed;
                    stickDeccelTimer = 0f;
                    // Rope impact: currentRope was already set this frame by
                    // UpdateRopeReference (line ~603), so we can hand it the
                    // pre-attach velocity here BEFORE the attach zeroes it.
                    if (currentRope != null)
                        currentRope.ApplyRiderImpact(rb.position, relativeVel);

                    if (carrier != null) carrier.Attach(surfaceCollider);
                    UpdateSurfaceModifiers(surfaceCollider);

                    float landingAngle = GetSurfaceAngle();
                    tangentSpeed *= surfaceMod.wallAttachDampen.Evaluate(landingAngle);
                    float redirectSign = Mathf.Sign(Vector2.Dot(gravityUp, surfaceTangent));
                    float redirectScale = 1f - Mathf.Abs(Vector2.Dot(surfaceNormal, gravityUp));
                    float effectiveTangent = tangentSpeed + normalSpeed * surfaceMod.impactRedirect.Evaluate(landingAngle) * redirectSign * redirectScale;
                    hopBufferTimer = 0f;
                    dashBufferTimer = 0f;
                    chargeBufferTimer = 0f;
                    float landedTangent = ComputeLandingCrawlVelocity(effectiveTangent, landingAngle);
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

                    if (surfaceUp >= settings.stickSlopeThreshold)
                        stickTimer = 0f;
                    else
                        stickTimer = CalculateStickDuration(impactSpeed) * surfaceMod.stickDuration.Evaluate(landingAngle);

                    activeChargeRadius = Mathf.Max(spawnDistance, settings.chargeRadius);
                    aimOffset = Vector2.zero;
                    chargeFullAimTimer = 0f;

                    slideRampTimer = 0f;
                    driftDirection = -surfaceNormal;
                    prevSurfaceNormal = surfaceNormal;
                    float conversionRatio = activeChargeRadius / settings.chargeRadius;
                    float hyperDashRetention = 1f - Mathf.InverseLerp(settings.hyperDashConversionDampingMin, settings.hyperDashConversionDampingMax, conversionRatio);
                    chargeEntryCrawl *= hyperDashRetention;
                    ChangeState(SlimeState.Charging);
                    lastSurfaceNormal = surfaceNormal;
                    lastSurfaceTangent = surfaceTangent;
                    surfaceProbeMissFrames = 0;
                    cachedSurfaceCollider = surfaceCollider;
                    SnapToSurface();
                    if (chargeVisual != null) chargeVisual.Show();
                    // Intentionally before CheckPlatformEffect: dust puff on JumpPad contact is natural.
                    OnLanded?.Invoke(surfaceCollider.ClosestPoint(rb.position), surfaceNormal);

                    // Platform effect check (after state is finalized).
                    // JumpPad etc. may call ForceAirborneWithVelocity here,
                    // which changes state from Charging back to Airborne.
                    CheckPlatformEffect(surfaceCollider);
                }
                else
                {
                    // Landing without Space: sticky landing, energy lost
                    Vector2 relativeVel = velocity - landingPlatformVel;
                    float landingSpeed = relativeVel.magnitude;
                    float tangentSpeed = Vector2.Dot(relativeVel, surfaceTangent);
                    float normalSpeed = Mathf.Abs(Vector2.Dot(relativeVel, surfaceNormal));
                    impactTangentSpeed = tangentSpeed;
                    impactNormalSpeed = normalSpeed;
                    stickDeccelTimer = 0f;
                    // Rope impact: see Space-held branch above for ordering rationale.
                    if (currentRope != null)
                        currentRope.ApplyRiderImpact(rb.position, relativeVel);

                    if (carrier != null) carrier.Attach(surfaceCollider);
                    UpdateSurfaceModifiers(surfaceCollider);

                    float landingAngle = GetSurfaceAngle();
                    tangentSpeed *= surfaceMod.wallAttachDampen.Evaluate(landingAngle);
                    float redirectSign = Mathf.Sign(Vector2.Dot(gravityUp, surfaceTangent));
                    float redirectScale = 1f - Mathf.Abs(Vector2.Dot(surfaceNormal, gravityUp));
                    float effectiveTangent = tangentSpeed + normalSpeed * surfaceMod.impactRedirect.Evaluate(landingAngle) * redirectSign * redirectScale;

                    if (hopBufferTimer > 0f)
                        preserveLandingVelocity = true;

                    float landedTangent = ComputeLandingCrawlVelocity(effectiveTangent, landingAngle);
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

                    // Energy-conversion coyote: compute the spawn distance a Space-held
                    // landing would have granted and stash it. If the player presses
                    // Space within energyConversionCoyoteTime, HandleSurfaceChargeEntry
                    // consumes this to grant the same expanded charge radius.
                    bool isFloorLanding = surfaceUp >= settings.stickSlopeThreshold;
                    bool coyoteArmed = false;
                    if (settings.energyConversionCoyoteTime > 0f)
                    {
                        float heightDelta = Vector2.Dot(launchPosition - rb.position, gravityUp);
                        float shadowSpeedSq = launchSpeed * launchSpeed
                            + 2f * Mathf.Abs(settings.gravity) * heightDelta;
                        if (launchedByHop)
                            shadowSpeedSq += settings.hopConversionEnergyBonus * settings.baseLaunchPower * settings.baseLaunchPower;
                        float impactSpeed = shadowSpeedSq > 0f ? Mathf.Sqrt(shadowSpeedSq) : 0f;
                        float rate = (conversionCount == 0) ? settings.initialConversionRate : settings.subsequentConversionRate;
                        float speedRatio = impactSpeed / settings.baseLaunchPower;
                        float spawnDistance = settings.chargeRadius
                            * Mathf.Pow(speedRatio, 1f / settings.chargeCurveExponent)
                            * rate;
                        if (launchSpeed == 0f) spawnDistance *= settings.notJumpConversionBonus;
                        if (launchedByHop) spawnDistance *= settings.hopConversionBonus;

                        float candidate = Mathf.Max(spawnDistance, settings.chargeRadius);
                        if (candidate > settings.chargeRadius)
                        {
                            pendingEnergyChargeRadius = candidate;
                            energyCoyoteTimer = settings.energyConversionCoyoteTime;
                            pendingFloorReset = isFloorLanding;
                            coyoteArmed = true;
                        }
                    }

                    // conversionCount resets on floor landing only. Wall chains keep subsequentConversionRate — intentional.
                    // When coyote is armed on a floor landing, the reset is deferred to pending expiry
                    // so a coyote-consumed conversion continues the chain (matches Space-held branch).
                    if (isFloorLanding)
                    {
                        stickTimer = 0f;
                        if (!coyoteArmed)
                            conversionCount = 0;
                    }
                    else
                        stickTimer = CalculateStickDuration(landingSpeed) * surfaceMod.stickDuration.Evaluate(landingAngle);

                    slideRampTimer = 0f;
                    ChangeState(SlimeState.Attached);
                    SnapToSurface();
                    OnLanded?.Invoke(surfaceCollider.ClosestPoint(rb.position), surfaceNormal);

                    // Platform effect check (after state is finalized).
                    // JumpPad etc. may call ForceAirborneWithVelocity here,
                    // which changes state from Attached back to Airborne.
                    CheckPlatformEffect(surfaceCollider);
                    if (currentState != SlimeState.Attached) return;

                    if (hopBufferTimer > 0f)
                    {
                        hopBufferTimer = 0f;
                        dashBufferTimer = 0f;
                        // On inertial surfaces landedTangent went into slideVelocity, so include it.
                        velocity = surfaceTangent * (crawlVelocity + slideVelocity);
                        stickTimer = 0f;
                        Hop();
                        return;
                    }

                    if (dashBufferTimer > 0f && dashPendingKeyboardSign != 0 && dashCooldownTimer <= 0f)
                    {
                        ApplyGroundDash(-dashPendingKeyboardSign);
                        dashBufferTimer = 0f;
                        dashPendingKeyboardSign = 0;
                    }
                }
                return;
                } // end else (blockAttach)
            }

            // Air control: horizontal acceleration with speed cap (last-input-wins)
            // "Horizontal" = perpendicular to effective gravity, not necessarily world-X.
            if (airControlActive && !airDashSuppressesPhysics)
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

        private float ComputeLandingCrawlVelocity(float effectiveTangent, float landingAngle)
        {
            if (preserveLandingVelocity)
            {
                preserveLandingVelocity = false;
                return effectiveTangent * settings.hopLandingRetention;
            }
            return effectiveTangent * surfaceMod.landingVelocityRetention.Evaluate(landingAngle);
        }

        private float CalculateStickDuration(float impactSpeed)
        {
            float t = 1f - 1f / (1f + impactSpeed * settings.stickSpeedScale);
            return Mathf.Lerp(settings.minStickDuration, settings.maxStickDuration, t);
        }
    }
}
