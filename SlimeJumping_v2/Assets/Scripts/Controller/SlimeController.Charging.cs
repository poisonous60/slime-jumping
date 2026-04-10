using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Physics;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- State: Charging ---

        private void FixedUpdateCharging(float dt)
        {
            // Aim logic (drift, WASD, angular clamp, auto-launch) — runs on fixed timestep for framerate independence

            // Auto-drift outward (compensated for expanded radius)
            float radiusRatio = activeChargeRadius / settings.chargeRadius;
            float activeDriftSpeed = settings.autoDriftSpeed * Mathf.Lerp(1f, radiusRatio, settings.aimDriftRadiusScale);

            if (aimOffset.sqrMagnitude > 0.0001f)
                aimOffset += aimOffset.normalized * activeDriftSpeed * dt;
            else
                aimOffset += driftDirection * activeDriftSpeed * dt;

            // WASD: 2D acceleration on aim offset
            Vector2 input = inputReader.AimInput;
            Vector2 prevAim = aimOffset;
            aimOffset += input * settings.inputAcceleration * dt;

            // Clamp angular speed
            if (prevAim.sqrMagnitude > 0.0001f && aimOffset.sqrMagnitude > 0.0001f)
            {
                float prevAngle = Mathf.Atan2(prevAim.y, prevAim.x) * Mathf.Rad2Deg;
                float newAngle = Mathf.Atan2(aimOffset.y, aimOffset.x) * Mathf.Rad2Deg;
                float delta = Mathf.DeltaAngle(prevAngle, newAngle);
                float maxDelta = settings.maxAimAngularSpeed * dt;
                delta = Mathf.Clamp(delta, -maxDelta, maxDelta);

                float clampedAngle = (prevAngle + delta) * Mathf.Deg2Rad;
                float radius = aimOffset.magnitude;
                aimOffset = new Vector2(Mathf.Cos(clampedAngle), Mathf.Sin(clampedAngle)) * radius;
            }

            // Auto-launch when reaching edge (with aim grace period)
            if (aimOffset.magnitude >= activeChargeRadius)
            {
                aimOffset = aimOffset.normalized * activeChargeRadius;
                chargeFullAimTimer += dt;
                if (chargeFullAimTimer >= settings.chargeFullAimTime)
                {
                    Launch();
                    return;
                }
            }

            // Volume charging: gravity only, fall cap applied by DragZone.FixedUpdate via ClampFallSpeed()
            if (isVolumeCharging)
            {
                velocity += GravityVector * dt;

                if (!IsInChargeVolume)
                {
                    isVolumeCharging = false;
                    CancelChargingToAirborne();
                }
                return;
            }

            // Surface/coyote physics — with probe hysteresis (absorb 1-frame misses)
            bool probeValid = isTouchingSurface
                && Vector2.Dot(surfaceNormal, gravityUp) >= settings.ceilingThreshold;

            bool onValidSurface;
            if (probeValid)
            {
                surfaceProbeMissFrames = 0;
                cachedSurfaceCollider = surfaceCollider;
                onValidSurface = true;
            }
            else if (surfaceProbeMissFrames < settings.chargingSurfaceProbeGraceFrames
                     && cachedSurfaceCollider != null)
            {
                surfaceProbeMissFrames++;
                surfaceNormal = lastSurfaceNormal;
                surfaceTangent = lastSurfaceTangent;
                surfaceCollider = cachedSurfaceCollider;
                onValidSurface = true;
            }
            else
            {
                onValidSurface = false;
            }

            if (onValidSurface)
            {
                // Rotate aim offset to follow surface normal change on curved surfaces
                float prevAngleRad = Mathf.Atan2(prevSurfaceNormal.y, prevSurfaceNormal.x);
                float currAngleRad = Mathf.Atan2(surfaceNormal.y, surfaceNormal.x);
                float deltaAngle = currAngleRad - prevAngleRad;
                if (deltaAngle > Mathf.PI) deltaAngle -= 2f * Mathf.PI;
                else if (deltaAngle < -Mathf.PI) deltaAngle += 2f * Mathf.PI;
                float maxRotation = settings.chargeNormalFollowSpeed * dt;
                deltaAngle = Mathf.Clamp(deltaAngle, -maxRotation, maxRotation);
                if (Mathf.Abs(deltaAngle) > 0.0001f)
                {
                    float cos = Mathf.Cos(deltaAngle);
                    float sin = Mathf.Sin(deltaAngle);
                    aimOffset = new Vector2(
                        aimOffset.x * cos - aimOffset.y * sin,
                        aimOffset.x * sin + aimOffset.y * cos
                    );
                    driftDirection = new Vector2(
                        driftDirection.x * cos - driftDirection.y * sin,
                        driftDirection.x * sin + driftDirection.y * cos
                    );
                }
                prevSurfaceNormal = surfaceNormal;

                // On surface: reset coyote, remember surface for grace period
                coyoteTimer = settings.chargingCoyoteTime;
                lastSurfaceNormal = surfaceNormal;
                lastSurfaceTangent = surfaceTangent;

                // Reset slide ramp when stick expires (smooth transition)
                if (wasStuck && stickTimer <= 0f)
                {
                    slideRampTimer = 0f;
                    wasStuck = false;
                    crawlVelocity = Vector2.Dot(velocity, surfaceTangent);
                }
                if (stickTimer > 0f) wasStuck = true;

                // Stick: delay before slide starts (aiming still runs via UpdateCharging)
                if (stickTimer > 0f)
                {
                    stickTimer -= dt;
                    if (settings.stickDecelerationTime > 0f && stickDeccelTimer < settings.stickDecelerationTime)
                    {
                        stickDeccelTimer += dt;
                        float t = stickDeccelTimer / settings.stickDecelerationTime;
                        velocity = surfaceTangent * Mathf.Lerp(impactTangentSpeed, 0f, t);
                    }
                    else
                    {
                        velocity = Vector2.zero;
                    }
                    SnapToSurface();
                }
                else
                {
                    // Slime still slides on walls during charging (same physics as Attached, no crawl input)
                    float angle = GetSurfaceAngle();
                    Vector2 gravityVec = GravityVector;
                    float gravAlongTangent = Vector2.Dot(gravityVec, surfaceTangent);
                    gravAlongTangent *= settings.chargingSlideGravityScale;
                    if (Mathf.Abs(gravAlongTangent) < settings.slideThreshold)
                        gravAlongTangent = 0f;
                    float slideSpeed = gravAlongTangent * settings.slideGravityScale * surfaceMod.slideGravity.Evaluate(angle);

                    if (Mathf.Abs(slideSpeed) > 0.001f)
                    {
                        float sign = Mathf.Sign(slideSpeed);
                        slideSpeed -= sign * settings.slideResistance * surfaceMod.slideResistance.Evaluate(angle);
                        if (Mathf.Sign(slideSpeed) != sign)
                            slideSpeed = 0f;
                    }

                    // Slide ramp-up: gradual acceleration after landing
                    slideRampTimer += dt;
                    float surfaceRamp = surfaceMod.slideRampUp.Evaluate(angle);
                    float rampUpTime = (surfaceRamp > 0f) ? surfaceRamp : settings.stickSlideTransitionRampTime;
                    float rampT = (rampUpTime > 0f) ? Mathf.Clamp01(slideRampTimer / rampUpTime) : 1f;
                    slideSpeed *= rampT;

                    float maxCrawl = settings.crawlSpeed * surfaceMod.crawlSpeed.Evaluate(angle);
                    float chargingDecel = Mathf.Abs(crawlVelocity) > maxCrawl
                        ? settings.crawlOverspeedDeceleration * surfaceMod.crawlOverspeedDecel.Evaluate(angle)
                        : settings.crawlDeceleration * surfaceMod.crawlDecel.Evaluate(angle);
                    crawlVelocity = Mathf.MoveTowards(crawlVelocity, 0f, chargingDecel * dt);

                    float finalTangent = slideSpeed + crawlVelocity;
                    velocity = surfaceTangent * finalTangent;

                    float normalVel = Vector2.Dot(velocity, surfaceNormal);
                    if (normalVel < 0f)
                        velocity -= surfaceNormal * normalVel;

                    SnapToSurface();
                }
            }
            else if (coyoteTimer > 0f)
            {
                // Coyote: off surface but still charging
                coyoteTimer -= dt;
                float elapsed = settings.chargingCoyoteTime - coyoteTimer;
                if (elapsed > settings.coyoteGraceTime)
                    velocity += GravityVector * dt;
                else
                    velocity = Vector2.zero; // Grace: freeze at edge
            }
            else
            {
                // Coyote expired: cancel to Airborne
                if (carrier != null) carrier.Detach();
                CancelChargingToAirborne();
            }
        }

        private void CancelChargingToAirborne()
        {
            crawlVelocity = 0f;
            chargeEntryCrawl = 0f;
            launchSpeed = 0f;
            launchPosition = rb.position;
            slideRampTimer = 0f;
            conversionCount = 0;
            surfaceMod = SurfaceModifiers.Default;
            ChangeState(SlimeState.Airborne);
            if (chargeVisual != null) chargeVisual.Hide();
        }
    }
}
