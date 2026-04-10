using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Physics;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        // --- State: Attached (any surface) ---

        private void FixedUpdateAttached(float dt)
        {
            if (!isTouchingSurface || Vector2.Dot(surfaceNormal, gravityUp) < settings.ceilingThreshold)
            {
                hopRequested = false;
                crawlVelocity = 0f;
                launchSpeed = 0f;
                launchHeight = Vector2.Dot(rb.position, gravityUp);
                launchGravityUp = gravityUp;
                slideRampTimer = 0f;
                conversionCount = 0;
                if (carrier != null) carrier.Detach();
                surfaceMod = SurfaceModifiers.Default;
                ChangeState(SlimeState.Airborne);
                return;
            }

            // Reset slide ramp when stick expires (smooth transition)
            if (wasStuck && stickTimer <= 0f)
            {
                slideRampTimer = 0f;
                wasStuck = false;
                crawlVelocity = Vector2.Dot(velocity, surfaceTangent);
            }
            if (stickTimer > 0f) wasStuck = true;

            // Stick: delay before slide starts, with optional deceleration
            if (stickTimer > 0f)
            {
                stickTimer -= dt;
                if (settings.stickDecelerationTime > 0f && stickDeccelTimer < settings.stickDecelerationTime)
                {
                    stickDeccelTimer += dt;
                    float t = stickDeccelTimer / settings.stickDecelerationTime;
                    velocity = surfaceTangent * Mathf.Lerp(impactTangentSpeed, 0f, t);
                    SnapToSurface();
                    return;
                }
            }

            // 1. Slide: fresh each frame, no accumulation (skip during stick)
            float angle = GetSurfaceAngle();
            float slideSpeed = 0f;
            if (stickTimer <= 0f)
            {
                Vector2 gravityVec = GravityVector;
                float gravAlongTangent = Vector2.Dot(gravityVec, surfaceTangent);
                if (Mathf.Abs(gravAlongTangent) < settings.slideThreshold)
                    gravAlongTangent = 0f;
                slideSpeed = gravAlongTangent * settings.slideGravityScale * surfaceMod.slideGravity.Evaluate(angle);

                // Apply resistance to slideSpeed (not gravAlongTangent — crawl still fights full gravity)
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
            }

            // 2. Crawl: accel/decel model (weakened during stick)
            float crawlScale = (stickTimer > 0f) ? settings.stickCrawlMultiplier : 1f;
            float inputX = inputReader.LastInputMove.x;
            float maxCrawlSpeed = settings.crawlSpeed * surfaceMod.crawlSpeed.Evaluate(angle);
            float targetSpeed = -inputX * maxCrawlSpeed * crawlScale;
            bool overspeed = Mathf.Abs(crawlVelocity) > maxCrawlSpeed;
            float decelValue = overspeed
                ? settings.crawlOverspeedDeceleration * surfaceMod.crawlOverspeedDecel.Evaluate(angle)
                : settings.crawlDeceleration * surfaceMod.crawlDecel.Evaluate(angle);
            if (crawlVelocity != 0f && targetSpeed != 0f && Mathf.Sign(targetSpeed) != Mathf.Sign(crawlVelocity))
            {
                // Opposite direction: decel to 0, then accel toward new direction
                float absBefore = Mathf.Abs(crawlVelocity);
                crawlVelocity = Mathf.MoveTowards(crawlVelocity, 0f, decelValue * dt);
                if (crawlVelocity == 0f)
                {
                    float dtLeft = dt - absBefore / decelValue;
                    if (dtLeft > 0f)
                    {
                        float accelRate = settings.crawlAcceleration * surfaceMod.crawlAccel.Evaluate(angle);
                        crawlVelocity = Mathf.MoveTowards(0f, targetSpeed, accelRate * dtLeft);
                    }
                }
            }
            else if (Mathf.Abs(targetSpeed) > Mathf.Abs(crawlVelocity))
                crawlVelocity = Mathf.MoveTowards(crawlVelocity, targetSpeed, settings.crawlAcceleration * surfaceMod.crawlAccel.Evaluate(angle) * dt);
            else
                crawlVelocity = Mathf.MoveTowards(crawlVelocity, targetSpeed, decelValue * dt);

            // 3. Ground Dash: S impulse with input buffer
            if (dashBufferTimer > 0f && Mathf.Abs(inputX) > 0.01f && dashCooldownTimer <= 0f)
            {
                ApplyGroundDash(-Mathf.Sign(inputX));
                dashBufferTimer = 0f;
            }

            // 4. Final tangent velocity
            float finalTangent = slideSpeed + crawlVelocity;
            velocity = surfaceTangent * finalTangent;

            // Cancel velocity into surface
            float normalVel = Vector2.Dot(velocity, surfaceNormal);
            if (normalVel < 0f)
                velocity -= surfaceNormal * normalVel;

            SnapToSurface();

            if (hopRequested)
            {
                hopRequested = false;
                Hop();
            }
        }
    }
}
