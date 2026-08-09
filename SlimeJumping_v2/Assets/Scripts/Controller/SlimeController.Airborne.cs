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

            // Apex gate locks air control while rising hard, but in fluid the rise is buoyancy,
            // not a launch — steering must still work, so bypass it while submerged.
            bool airControlActive = InFluid
                || (postLaunchAirControlLockTimer <= 0f
                    && Vector2.Dot(velocity, gravityUp) <= settings.apexThreshold);

            // W/S variant picked by actual velocity direction, so steering still works above apex.
            // In fluid the sign is ignored: buoyancy has no clean apex, so a rising/falling split
            // makes submerged steering feel inconsistent.
            float holdGravityScale = 1f;
            float inputYRaw = inputReader.LastInputMove.y;
            if (InFluid)
            {
                if (inputYRaw > 0.1f) holdGravityScale = settings.floatGravityScaleInFluid;
                else if (inputYRaw < -0.1f) holdGravityScale = settings.diveGravityScaleInFluid;
            }
            else
            {
                bool isRisingByVelocity = Vector2.Dot(velocity, gravityUp) > 0f;
                if (inputYRaw > 0.1f)
                    holdGravityScale = isRisingByVelocity ? settings.floatGravityScaleRising : settings.floatGravityScaleFalling;
                else if (inputYRaw < -0.1f)
                    holdGravityScale = isRisingByVelocity ? settings.diveGravityScaleRising : settings.diveGravityScaleFalling;
            }

            // Asymmetric gravity reuses the apex-gated airControlActive split.
            float asymScale = GetAsymGravityScale(airControlActive);
            velocity += -gravityUp * GravityMagnitude * holdGravityScale * asymScale * dt;

            if (!isTouchingSurface) ceilingBumpLatched = false;

            if (isTouchingSurface)
            {
                // EdgeCollider2D is a polyline with no inherent outside; fast vertical motion can
                // land the slime on the underside. Offset is exactly slimeRadius (not + contactSkin)
                // so next FU's probe still overlaps — otherwise detach → fall through → re-teleport
                // every FU, visible as up-down oscillation.
                if (currentRope != null && surfaceCollider == currentRope.Edge
                    && Vector2.Dot(surfaceNormal, gravityUp) < 0f)
                {
                    Vector2 ropeContact = surfaceCollider.ClosestPoint(rb.position);
                    rb.position = ropeContact + gravityUp * SlimeRadius;
                    surfaceNormal = gravityUp;
                    surfaceTangent = new Vector2(-surfaceNormal.y, surfaceNormal.x);
                }

                float surfaceUp = Vector2.Dot(surfaceNormal, gravityUp);

                // surfaceMod still reflects the previous attach — resolve from the new collider.
                SurfaceTag.ResolveCeilingThresholds(surfaceCollider, settings,
                    out _, out float landingReattachThreshold);

                // Ceiling never sticky-attaches. A buffered/held charge instead starts a brief charge
                // whose aim drifts into the ceiling, so the launch plunges downward.
                if (surfaceUp < landingReattachThreshold)
                {
                    bool ceilingChargeRequested = chargeBufferTimer > 0f
                        || (settings.allowChargeAirHold && inputReader.IsJumpHeld);
                    if (ceilingChargeRequested && attachLockoutTimer <= 0f && blockAttachCount == 0)
                    {
                        Vector2 ceilingPlatformVel = GetSurfaceVelocity(surfaceCollider);
                        EnterChargingFromLanding(ceilingPlatformVel, surfaceUp);
                        return;
                    }
                    if (!ceilingBumpLatched)
                    {
                        ceilingBumpLatched = true;
                        // Pre-projection, platform-relative: the same quantity CaptureLandingImpact reports.
                        Vector2 bumpRel = velocity - GetSurfaceVelocity(surfaceCollider);
                        OnCeilingBump?.Invoke(surfaceCollider.ClosestPoint(rb.position), surfaceNormal,
                            Mathf.Abs(Vector2.Dot(bumpRel, surfaceNormal)));
                    }
                    surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);
                    return;
                }

                // Attach debounce: block landing during lockout, cancel normal velocity only
                if (attachLockoutTimer > 0f)
                {
                    surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);
                    return;
                }

                // BlockAttach (e.g. water zone): cancel normal velocity but do NOT return — fall
                // through to air control so the slime can move away.
                if (blockAttachCount > 0)
                {
                    surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);
                }
                else
                {

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

        // Shared landing capture: platform-relative impact, carrier attach, surface material,
        // then wall dampening + impact-redirect. Returns the landing surface angle.
        private float CaptureLandingImpact(Vector2 landingPlatformVel, out float impactMagnitude, out float effectiveTangent)
        {
            Vector2 relativeVel = velocity - landingPlatformVel;
            impactMagnitude = relativeVel.magnitude;
            float tangentSpeed = Vector2.Dot(relativeVel, surfaceTangent);
            float normalSpeed = Mathf.Abs(Vector2.Dot(relativeVel, surfaceNormal));
            impactTangentSpeed = tangentSpeed;
            impactNormalSpeed = normalSpeed;
            stickDeccelTimer = 0f;
            // Hand the rope the pre-attach velocity BEFORE the attach below zeroes it.
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

        // Routes landed tangent speed into the surface's channel (inertial → slideVelocity, else crawlVelocity).
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

        // Inverts the launch power curve so a rate=1 conversion preserves launch height.
        // Reads conversionCount for the rate; does NOT increment it.
        private float ComputeEnergyConversionSpawnDistance(out float impactSpeed)
        {
            float heightDelta = Vector2.Dot(launchPosition - rb.position, gravityUp);
            float shadowSpeedSq = launchSpeed * launchSpeed + 2f * GravityMagnitude * heightDelta;
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

        // No Space at landing → sticky landing; arms the energy-conversion coyote so a late Space
        // press still grants the would-have-been expanded radius.
        private void AttachFromLanding(Vector2 landingPlatformVel, float surfaceUp)
        {
            float landingAngle = CaptureLandingImpact(landingPlatformVel, out float landingSpeed, out float effectiveTangent);
            TryArmBambooV2Rebound(landingSpeed);
            RouteLandedTangent(ComputeLandingCrawlVelocity(effectiveTangent, landingAngle));

            // SurfaceMaterial.overrideEnergyConversionCoyoteTime lets springy surfaces extend the window.
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

            // conversionCount resets on floor landing only (wall chains keep subsequentConversionRate);
            // deferred while coyote is armed so a coyote-consumed conversion continues the chain.
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

            // Cap only the EXCESS over max(|preLaunchSlideV|, aimPower): pre-launch slide and the aim
            // power the player spent must survive; only the residual gain is dialed.
            // External-arrival flights leave launchOriginValid=false, keeping Inertial Slide intact.
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

            // Optional non-linear remap (saturating curve for ice); toggle keeps authored keys
            // when disabled. Applied last so it covers both landing paths.
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
