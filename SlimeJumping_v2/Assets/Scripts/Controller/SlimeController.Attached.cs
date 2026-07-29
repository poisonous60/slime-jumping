using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Physics;

namespace SlimeJumping.Controller
{
    public partial class SlimeController
    {
        private void FixedUpdateAttached(float dt)
        {
            // Tip-grab is sticky: the ordinary "surface lost" detach is suppressed until the
            // grab clears. blockAttach (water etc.) still wins and forces detach.
            bool grabKeepsAttached = currentBambooGrab != null;
            bool surfaceLost = !isTouchingSurface
                || Vector2.Dot(surfaceNormal, gravityUp) < surfaceMod.GetCeilingThreshold(settings);
            if ((!grabKeepsAttached && surfaceLost) || blockAttachCount > 0)
            {
                crawlVelocity = 0f;
                slideVelocity = 0f;
                launchSpeed = 0f;
                launchPosition = rb.position;
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

            // Tip-grabbed: rb.position is owned by LockBambooGrabPosition. Bail before the
            // surface physics writes a tangent velocity that tugs the slime off the tip.
            if (currentBambooGrab != null)
            {
                crawlVelocity = 0f;
                slideVelocity = 0f;
                velocity = Vector2.zero;
                stickTimer = 0f;
                stickDeccelTimer = 0f;
                slideRampTimer = 0f;
                if (IsPlatformEffectAlive) activePlatformEffect.OnSlimeStay(this, dt);
                return;
            }

            UpdateStickTransition();

            // Decel target is crawlVelocity, not 0 — with SurfaceTag retention > 0 the crawl
            // path resumes at surfaceTangent * crawlVelocity and a 0-target endpoint jumps.
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

            float angle = GetSurfaceAngle();
            float slideSpeed = stickTimer <= 0f ? ComputeSlideSpeed(angle, dt, 1f) : 0f;

            float crawlDecel = settings.crawlOverspeedDeceleration * surfaceMod.crawlOverspeedDecel.Evaluate(angle);
            crawlVelocity = Mathf.MoveTowards(crawlVelocity, 0f, crawlDecel * dt);

            // Arc-midpoint-compensated tangent: otherwise the chord Rigidbody2D.Interpolate
            // draws follows the leading-edge tangent and fast rotation shows visible sagitta.
            float finalTangent = slideSpeed + crawlVelocity;
            velocity = ComputeArcCompensatedTangent(surfaceTangent) * finalTangent;

            // Cancel velocity into every active contact; a single-normal clip rubber-bands at
            // concave corners. commandedMag (rotation-only, == |finalTangent|) feeds the wedge test.
            float commandedMag = velocity.magnitude;
            surfaceProbe.ProjectVelocityAgainstContacts(ref velocity, surfaceNormal);

            // Runs after the tangent clip so a detach this frame launches along the surface heading.
            if (CheckFrictionDetach(dt))
                return;

            // Wedged in a concave corner: the projection eats most of the commanded tangent while
            // ComputeSlideSpeed keeps banking slideVelocity → phantom launch when the wedge breaks.
            // Detect via magnitude drop, not |velocity| — banked slideVelocity keeps that large.
            const float wedgeClipRetention = 0.85f;
            if (surfaceMod.useInertialSlide
                && commandedMag > 0.001f
                && velocity.magnitude < commandedMag * wedgeClipRetention)
            {
                slideVelocity = Vector2.Dot(velocity, surfaceTangent) - crawlVelocity;
            }

            SnapToSurface();

            if (IsPlatformEffectAlive) activePlatformEffect.OnSlimeStay(this, dt);
        }

        /// <summary>Centrifugal-admissibility + geometric-horizon detach test; true means it already called ForceAirborneWithVelocity and the caller must bail.</summary>
        private bool CheckFrictionDetach(float dt)
        {
            if (!settings.useFrictionBasedDetachment)
            {
                smoothedCornerStressRatio = 0f;
                sameSignFrames = 0;
                SetFrictionDebug(new FrictionDetachDebug(
                    false, true, false, false, false, false, false,
                    0f, 0f, 0f, 0f, 0f, 0f, 0f,
                    false, 0, false));
                return false;
            }

            float tangentSignedSpeed = Vector2.Dot(velocity, surfaceTangent);
            float tangentSpeedAbs = Mathf.Abs(tangentSignedSpeed);

            bool tNoPrev = !arcHasPrevTangent;
            bool tColliderChanged = HasSurfaceCarrierHostChanged(surfaceCollider, prevSurfaceCollider);
            bool tStick = stickTimer > 0f;
            bool tBelowMinSpeed = tangentSpeedAbs < settings.cornerDetachMinSpeed;
            bool transientFrame = tNoPrev || tColliderChanged || tStick || tBelowMinSpeed;

            float rawRatio = 0f;
            bool gateOn = false;
            float dbgDThetaDeg = 0f, dbgOmega = 0f, dbgRequired = 0f;
            float dbgGravityHelp = 0f, dbgAvailable = 0f;
            bool dbgConvex = false;

            if (!transientFrame)
            {
                // ω feeds the visual stress ratio and the centrifugal test; the horizon probe is the second gate.
                float dThetaDeg = Vector2.SignedAngle(arcPrevTangent, surfaceTangent);
                float dTheta = dThetaDeg * Mathf.Deg2Rad;
                float absDTheta = Mathf.Abs(dTheta);
                float omega = absDTheta / dt;
                dbgDThetaDeg = dThetaDeg;
                dbgOmega = omega;

                bool convex = (dTheta * tangentSignedSpeed) < 0f;

                float gravityHelp = Mathf.Abs(Vector2.Dot(GravityVector, surfaceNormal));
                float available = gravityHelp + settings.cornerAdhesionAccel;

                float required;
                bool centrifugalAdmissible;
                switch (settings.frictionDetachFormula)
                {
                    case FrictionDetachFormula.ConstantTimesOmega:
                        required = settings.cornerVelocityThreshold * omega;
                        centrifugalAdmissible = required > available;
                        break;
                    case FrictionDetachFormula.VelocityTimesOmegaWithMinAngle:
                    {
                        required = tangentSpeedAbs * omega;
                        float minAngleRad = settings.cornerMinAngleDeg * Mathf.Deg2Rad;
                        centrifugalAdmissible = (absDTheta > minAngleRad) && (required > available);
                        break;
                    }
                    case FrictionDetachFormula.AngularRateThreshold:
                        required = omega;                               // ratio = ω / threshold
                        centrifugalAdmissible = omega > settings.cornerOmegaThreshold;
                        break;
                    case FrictionDetachFormula.VelocityTimesOmega:
                    default:
                        required = tangentSpeedAbs * omega;
                        centrifugalAdmissible = required > available;
                        break;
                }

                // Visual ratio: normalized so 1.0 = at-threshold across all modes.
                if (settings.frictionDetachFormula == FrictionDetachFormula.AngularRateThreshold)
                    rawRatio = (settings.cornerOmegaThreshold > 0.001f) ? omega / settings.cornerOmegaThreshold : 0f;
                else
                    rawRatio = (available > 0.001f) ? required / available : 0f;

                if (convex) sameSignFrames++;
                else sameSignFrames = 0;

                dbgRequired = required;
                dbgGravityHelp = gravityHelp;
                dbgAvailable = available;
                dbgConvex = convex;

                bool horizonClear = false;
                if (centrifugalAdmissible && tangentSpeedAbs > 0.001f)
                {
                    // Probe DOWN from the projected position: a "ray along velocity" version
                    // travels at slime-center height, so flat ground always read as clear.
                    Vector2 velDir = velocity / Mathf.Max(0.0001f, velocity.magnitude);
                    float lookahead = settings.cornerDetachHorizonLookaheadCurve.Evaluate(tangentSpeedAbs);
                    Vector2 projectedCenter = rb.position + velDir * lookahead;
                    // Skin absorbs chord-arc gaps / polygon facets so they still read as ground.
                    float probeDist = SlimeRadius + settings.cornerDetachHorizonProbeSkin;
                    RaycastHit2D hit = Physics2D.Raycast(projectedCenter, -surfaceNormal, probeDist, settings.groundLayer);
                    // ignoredColliders (post-launch hop) are empty space for this check.
                    if (hit.collider != null && ignoredColliderList != null
                        && ignoredColliderList.Contains(hit.collider))
                    {
                        hit = default;
                    }
                    horizonClear = hit.collider == null;
                }

                if (centrifugalAdmissible && horizonClear)
                {
                    Vector2 detachVel = velocity;
                    float overshoot = rawRatio - 1f;

                    SetFrictionDebug(new FrictionDetachDebug(
                        true, true, false,
                        tNoPrev, tColliderChanged, tStick, tBelowMinSpeed,
                        tangentSignedSpeed, dbgDThetaDeg, dbgOmega,
                        dbgRequired, dbgGravityHelp, dbgAvailable, rawRatio,
                        dbgConvex, sameSignFrames, true));
                    OnFrictionDetach?.Invoke(detachVel, surfaceNormal, overshoot);
                    ForceAirborneWithVelocity(detachVel);
                    return true;
                }

                // Visual gate only: single-frame ω spikes are bled off by the LP filter plus the
                // downstream SmoothDamp, so they never reach perceptible stretch.
                gateOn = sameSignFrames >= 1;
            }
            else
            {
                sameSignFrames = 0;
            }

            float exposedRatio = gateOn ? rawRatio : 0f;
            float k = 1f - Mathf.Exp(-settings.cornerStressRatioSmoothRate * dt);
            smoothedCornerStressRatio = Mathf.Lerp(smoothedCornerStressRatio, exposedRatio, k);
            cornerStressRatio = rawRatio;

            SetFrictionDebug(new FrictionDetachDebug(
                true, true, transientFrame,
                tNoPrev, tColliderChanged, tStick, tBelowMinSpeed,
                tangentSignedSpeed, dbgDThetaDeg, dbgOmega,
                dbgRequired, dbgGravityHelp, dbgAvailable, rawRatio,
                dbgConvex, sameSignFrames, false));
            return false;
        }
    }
}
