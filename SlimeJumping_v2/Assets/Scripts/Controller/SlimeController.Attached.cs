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
            if (!isTouchingSurface || Vector2.Dot(surfaceNormal, gravityUp) < settings.ceilingThreshold
                || blockAttachCount > 0)
            {
                hopRequested = false;
                crawlVelocity = 0f;
                slideVelocity = 0f;
                launchSpeed = 0f;
                launchPosition = rb.position;
                launchedByHop = false;
                slideRampTimer = 0f;
                conversionCount = 0;
                ClearPendingEnergyConversion(executeDeferredReset: false);
                if (carrier != null) carrier.Detach();
                ClearPlatformEffect();
                surfaceMod = settings.BuildDefaultModifiers();
                ChangeState(SlimeState.Airborne);
                return;
            }

            if (energyCoyoteTimer > 0f)
            {
                energyCoyoteTimer -= dt;
                if (energyCoyoteTimer <= 0f)
                    ClearPendingEnergyConversion(executeDeferredReset: true);
            }

            // Reset slide ramp when stick expires (smooth transition)
            if (wasStuck && stickTimer <= 0f)
            {
                slideRampTimer = 0f;
                wasStuck = false;
                float postStickTangent = Vector2.Dot(velocity, surfaceTangent);
                if (surfaceMod.useInertialSlide)
                {
                    slideVelocity = postStickTangent;
                    crawlVelocity = 0f;
                }
                else
                {
                    crawlVelocity = postStickTangent;
                }
            }
            if (stickTimer > 0f) wasStuck = true;

            // Stick: delay before slide starts, with optional deceleration.
            // Decel target is crawlVelocity (landingVelocityRetention * effectiveTangent), not 0.
            // Why: without retention crawlVelocity is 0 so this matches old behavior, but with
            // SurfaceTag retention > 0, post-decel stick frames set velocity = surfaceTangent *
            // crawlVelocity via the crawl path — a 0-target lerp endpoint would jump up to it.
            if (stickTimer > 0f)
            {
                stickTimer -= dt;
                if (settings.stickDecelerationTime > 0f && stickDeccelTimer < settings.stickDecelerationTime)
                {
                    stickDeccelTimer += dt;
                    float t = stickDeccelTimer / settings.stickDecelerationTime;
                    velocity = surfaceTangent * Mathf.Lerp(impactTangentSpeed, crawlVelocity, t);
                    SnapToSurface();
                    return;
                }
            }

            // 1. Slide: legacy = fresh each frame; inertial = integrated (skip during stick)
            float angle = GetSurfaceAngle();
            float slideSpeed = 0f;
            if (stickTimer <= 0f)
            {
                Vector2 gravityVec = GravityVector;
                float gravAlongTangent = Vector2.Dot(gravityVec, surfaceTangent);

                if (surfaceMod.useInertialSlide)
                {
                    // Inertial: gravity is acceleration, slideResistance is kinetic friction (m/s²).
                    // No threshold deadzone — every bit of tangent gravity matters for KE↔height
                    // exchange on a halfpipe; friction handles drift on near-flat ground.
                    float slideAccel = gravAlongTangent * settings.slideGravityScale * surfaceMod.slideGravity.Evaluate(angle);
                    slideAccel *= ResolveSlopeGain(slideAccel, slideVelocity, angle);
                    slideVelocity += slideAccel * dt;
                    float linearDrag = surfaceMod.slideLinearDrag.Evaluate(angle);
                    if (linearDrag > 0f)
                        slideVelocity *= Mathf.Max(0f, 1f - linearDrag * dt);
                    float frictionDecel = settings.slideResistance * surfaceMod.slideResistance.Evaluate(angle);
                    if (frictionDecel > 0f)
                        slideVelocity = Mathf.MoveTowards(slideVelocity, 0f, frictionDecel * dt);
                    slideSpeed = slideVelocity;
                    slideRampTimer += dt; // kept ticking for state consistency (used by debug readouts)
                }
                else
                {
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

            // 3. Ground Dash: A/D double-tap impulse with input buffer.
            // Direction is latched on the event from the keyboard sign — we don't read
            // live inputX here so a quick double-tap (release before next FixedUpdate) still fires.
            if (dashBufferTimer > 0f && dashPendingKeyboardSign != 0 && dashCooldownTimer <= 0f)
            {
                ApplyGroundDash(-dashPendingKeyboardSign);
                dashBufferTimer = 0f;
                dashPendingKeyboardSign = 0;
            }

            // 4. Final tangent velocity. Use arc-midpoint-compensated tangent so the
            // chord drawn between this frame and next by Rigidbody2D.Interpolate lands
            // on the predicted arc midpoint instead of the leading-edge tangent. Without
            // compensation, fast tangent rotation (5–8°/frame at U-bottom of Ice curves)
            // produces visible chord-arc sagitta even when rb.position is anchored each frame.
            float finalTangent = slideSpeed + crawlVelocity;
            velocity = ComputeArcCompensatedTangent(surfaceTangent) * finalTangent;

            // Cancel velocity into all active contacts (multi-constraint). At concave
            // corners this zeroes velocity into every tangent surface, avoiding the
            // Phase-A-pushes-back rubber-banding of the single-normal clip.
            surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);

            // Drain inertial slideVelocity when the slime is *wedged* — actual motion
            // clipped to ~0 by ProjectVelocityAgainstContacts while gravAlongTangent
            // keeps integrating into slideVelocity each frame. Without this drain the
            // stored energy releases as a phantom launch when the wedge breaks (player
            // crawls free, geometry shifts, etc.).
            //
            // Threshold-based, not proportional: a proportional clip-ratio drain bleeds
            // slideVelocity on free curve sliding because ComputeArcCompensatedTangent
            // rotates velocity by ω·dt/2, dropping Dot(velocity, surfaceTangent) by
            // ~0.5% per frame even when nothing was actually clipped — that compounds
            // into ~22%/sec loss on continuous curves. Free sliding and slope→flat
            // redirection both preserve |velocity|, so guarding on velocity magnitude
            // leaves them untouched and only fires when motion truly stalls.
            if (surfaceMod.useInertialSlide && velocity.sqrMagnitude < 0.01f)
                slideVelocity = 0f;

            SnapToSurface();

            if (IsPlatformEffectAlive) activePlatformEffect.OnSlimeStay(this, dt);

            if (hopRequested)
            {
                hopRequested = false;
                Hop();
            }
        }
    }
}
