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

            // BlockAttach (e.g. water zone): cancel charging to airborne
            if (blockAttachCount > 0)
            {
                CancelChargingToAirborne();
                return;
            }

            // Surface/coyote physics — with probe hysteresis (absorb 1-frame misses)
            bool probeValid = isTouchingSurface
                && Vector2.Dot(surfaceNormal, gravityUp) >= surfaceMod.GetCeilingThreshold(settings);

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

                UpdateStickTransition();

                // Stick: delay before slide starts (aiming still runs)
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
                    // Slime still slides on walls during charging (same physics as Attached, no
                    // crawl input; charging slide gravity is scaled by chargingSlideGravityScale).
                    float angle = GetSurfaceAngle();
                    float slideSpeed = ComputeSlideSpeed(angle, dt, settings.chargingSlideGravityScale);
                    float chargingDecel = settings.crawlOverspeedDeceleration * surfaceMod.crawlOverspeedDecel.Evaluate(angle);
                    crawlVelocity = Mathf.MoveTowards(crawlVelocity, 0f, chargingDecel * dt);

                    float finalTangent = slideSpeed + crawlVelocity;
                    velocity = ComputeArcCompensatedTangent(surfaceTangent) * finalTangent;
                    surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);
                    SnapToSurface();
                    if (IsPlatformEffectAlive) activePlatformEffect.OnSlimeStay(this, dt);
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
            slideVelocity = 0f;
            chargeEntryCrawl = 0f;
            aimOffset = Vector2.zero;
            chargeFullAimTimer = 0f;
            launchSpeed = 0f;
            launchPosition = rb.position;
            slideRampTimer = 0f;
            conversionCount = 0;
            if (carrier != null) carrier.Detach();
            ClearPlatformEffect();
            surfaceMod = settings.BuildDefaultModifiers();
            ChangeState(SlimeState.Airborne);
        }
    }
}
