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

            bool airControlActive = Vector2.Dot(velocity, gravityUp) <= settings.apexThreshold;

            // Gravity
            float gravityScale = 1f;
            float asymScale;
            if (airControlActive)
            {
                float inputY = inputReader.LastInputMove.y;
                if (inputY > 0.1f) gravityScale = settings.floatGravityScale;
                else if (inputY < -0.1f) gravityScale = settings.diveGravityScale;
                asymScale = settings.fallingGravityScale;
            }
            else
            {
                asymScale = settings.risingGravityScale;
            }
            velocity += -gravityUp * Mathf.Abs(settings.gravity) * gravityScale * asymScale * dt;

            // Land on any surface
            if (isTouchingSurface)
            {
                float surfaceUp = Vector2.Dot(surfaceNormal, gravityUp);

                // Ceiling: don't attach, just cancel normal velocity and stay airborne
                if (surfaceUp < settings.ceilingReattachThreshold)
                {
                    float normalVel = Vector2.Dot(velocity, surfaceNormal);
                    if (normalVel < 0f)
                        velocity -= surfaceNormal * normalVel;
                    return;
                }

                // Attach debounce: block landing during lockout, cancel normal velocity only
                if (attachLockoutTimer > 0f)
                {
                    float normalVel2 = Vector2.Dot(velocity, surfaceNormal);
                    if (normalVel2 < 0f)
                        velocity -= surfaceNormal * normalVel2;
                    return;
                }

                // Get landing platform velocity (including angular contribution) before attaching
                Vector2 landingPlatformVel = GetSurfaceVelocity(surfaceCollider);

                if (inputReader.IsJumpHeld)
                {
                    // Energy conversion: use energy conservation (v² = v₀² + 2gh)
                    // so W/S gravity control doesn't inflate/deflate conversion
                    float heightDelta = Vector2.Dot(launchPosition - rb.position, gravityUp);
                    float shadowSpeedSq = launchSpeed * launchSpeed
                        + 2f * Mathf.Abs(settings.gravity) * heightDelta;
                    float impactSpeed = shadowSpeedSq > 0f ? Mathf.Sqrt(shadowSpeedSq) : 0f;
                    float speedRatio = Mathf.Sqrt(settings.initialConversionRate);
                    float effectiveRate = speedRatio * (settings.chargeRadius / settings.baseLaunchPower);
                    float spawnDistance = impactSpeed * effectiveRate;
                    float conversionScale = (conversionCount == 0) ? 1f : settings.subsequentConversionRate;
                    spawnDistance *= conversionScale;
                    if (launchSpeed == 0f) spawnDistance *= settings.notJumpConversionBonus;
                    conversionCount++;

                    Vector2 relativeVel = velocity - landingPlatformVel;
                    float tangentSpeed = Vector2.Dot(relativeVel, surfaceTangent);
                    float normalSpeed = Mathf.Abs(Vector2.Dot(relativeVel, surfaceNormal));
                    impactTangentSpeed = tangentSpeed;
                    stickDeccelTimer = 0f;

                    if (carrier != null) carrier.Attach(surfaceCollider);
                    UpdateSurfaceModifiers(surfaceCollider);

                    float landingAngle = GetSurfaceAngle();
                    tangentSpeed *= surfaceMod.wallAttachDampen.Evaluate(landingAngle);
                    float redirectSign = Mathf.Sign(Vector2.Dot(gravityUp, surfaceTangent));
                    float redirectScale = 1f - Mathf.Abs(Vector2.Dot(surfaceNormal, gravityUp));
                    float effectiveTangent = tangentSpeed + normalSpeed * surfaceMod.impactRedirect.Evaluate(landingAngle) * redirectSign * redirectScale;
                    hopBufferTimer = 0f;
                    dashBufferTimer = 0f;
                    crawlVelocity = ComputeLandingCrawlVelocity(effectiveTangent, landingAngle);
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
                }
                else
                {
                    // Landing without Space: sticky landing, energy lost
                    Vector2 relativeVel = velocity - landingPlatformVel;
                    float landingSpeed = relativeVel.magnitude;
                    float tangentSpeed = Vector2.Dot(relativeVel, surfaceTangent);
                    float normalSpeed = Mathf.Abs(Vector2.Dot(relativeVel, surfaceNormal));
                    impactTangentSpeed = tangentSpeed;
                    stickDeccelTimer = 0f;

                    if (carrier != null) carrier.Attach(surfaceCollider);
                    UpdateSurfaceModifiers(surfaceCollider);

                    float landingAngle = GetSurfaceAngle();
                    tangentSpeed *= surfaceMod.wallAttachDampen.Evaluate(landingAngle);
                    float redirectSign = Mathf.Sign(Vector2.Dot(gravityUp, surfaceTangent));
                    float redirectScale = 1f - Mathf.Abs(Vector2.Dot(surfaceNormal, gravityUp));
                    float effectiveTangent = tangentSpeed + normalSpeed * surfaceMod.impactRedirect.Evaluate(landingAngle) * redirectSign * redirectScale;

                    if (hopBufferTimer > 0f)
                        preserveLandingVelocity = true;

                    crawlVelocity = ComputeLandingCrawlVelocity(effectiveTangent, landingAngle);
                    velocity = Vector2.zero;

                    // conversionCount resets on floor landing only. Wall chains keep subsequentConversionRate — intentional.
                    if (surfaceUp >= settings.stickSlopeThreshold)
                    {
                        stickTimer = 0f;
                        conversionCount = 0;
                    }
                    else
                        stickTimer = CalculateStickDuration(landingSpeed) * surfaceMod.stickDuration.Evaluate(landingAngle);

                    slideRampTimer = 0f;
                    ChangeState(SlimeState.Attached);
                    SnapToSurface();

                    if (hopBufferTimer > 0f)
                    {
                        hopBufferTimer = 0f;
                        dashBufferTimer = 0f;
                        velocity = surfaceTangent * crawlVelocity;
                        stickTimer = 0f;
                        Hop();
                        return;
                    }

                    if (dashBufferTimer > 0f && Mathf.Abs(inputReader.LastInputMove.x) > 0.01f && dashCooldownTimer <= 0f)
                    {
                        ApplyGroundDash(-Mathf.Sign(inputReader.LastInputMove.x));
                        dashBufferTimer = 0f;
                    }
                }
                return;
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
