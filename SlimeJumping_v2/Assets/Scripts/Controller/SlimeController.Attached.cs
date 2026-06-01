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
            // Tip-grab is sticky — the slime stays Attached even when the bamboo bends past
            // the ceiling-threshold dot. The grab's tip-lock keeps the slime physically on
            // the bamboo; the ordinary "surface lost" detach is suppressed until Launch (or
            // any other Attached/Charging exit, which ChangeState clears the grab from).
            // blockAttach (water etc.) still wins and forces detach to keep zone semantics.
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

            // Tip-grabbed: rb.position is owned by LockBambooGrabPosition, slide/crawl/
            // friction-detach are irrelevant. Zero motion channels and bail out before
            // the surface physics writes a tangent velocity that would tug the slime off
            // the tip between LockBambooGrabPosition refreshes.
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

            // 1. Slide (legacy steady-state or inertial integration; skipped while sticking)
            float angle = GetSurfaceAngle();
            float slideSpeed = stickTimer <= 0f ? ComputeSlideSpeed(angle, dt, 1f) : 0f;

            // 2. Crawl channel: residual landing-tangent momentum decays to 0. WASD surface
            //    movement is gone — slide gravity + the charge slingshot are the only mover now.
            float crawlDecel = settings.crawlOverspeedDeceleration * surfaceMod.crawlOverspeedDecel.Evaluate(angle);
            crawlVelocity = Mathf.MoveTowards(crawlVelocity, 0f, crawlDecel * dt);

            // 3. Final tangent velocity. Use arc-midpoint-compensated tangent so the
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

            // Friction-based detach. Velocity is now surface-tangent clipped, so if we
            // detach this frame the launch direction == current surface heading.
            if (CheckFrictionDetach(dt))
                return;

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
        }

        /// <summary>
        /// Centrifugal-admissibility + geometric-horizon detach test, run after velocity
        /// has been clipped to the surface tangent. Returns true (and has already called
        /// ForceAirborneWithVelocity) if the slime detached this frame — caller must bail
        /// out of FixedUpdateAttached. Also maintains the friction-stress visual ratio
        /// (smoothedCornerStressRatio) and the debug snapshot every frame.
        ///
        /// The check has two stages:
        ///   1. Centrifugal admissibility: required = v²/R must exceed available
        ///      adhesion. Slow slime stays glued no matter what the geometry does.
        ///   2. Geometric horizon: looks ahead along the current velocity for a
        ///      configurable distance. If the surface drops away (no contact along
        ///      the predicted free-flight path), detach. If a surface is still
        ///      ahead (slime would just keep wrapping), don't detach.
        ///
        /// Stage 2 replaces the earlier dTheta+convex test, which fired at any
        /// angle change regardless of whether the slime was actually leaving the
        /// surface. The horizon check encodes the user intent directly: "would
        /// the slime fly into empty space if it kept going straight?"
        /// </summary>
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
                // ω is still useful as a "stress" signal for the visual ratio,
                // but it no longer gates detach. Only horizon does.
                float dThetaDeg = Vector2.SignedAngle(arcPrevTangent, surfaceTangent);
                float dTheta = dThetaDeg * Mathf.Deg2Rad;
                float absDTheta = Mathf.Abs(dTheta);
                float omega = absDTheta / dt;
                dbgDThetaDeg = dThetaDeg;
                dbgOmega = omega;

                bool convex = (dTheta * tangentSignedSpeed) < 0f;

                float gravityHelp = Mathf.Abs(Vector2.Dot(GravityVector, surfaceNormal));
                float available = gravityHelp + settings.cornerAdhesionAccel;

                // Formula-driven required value. Each mode shapes the detach
                // region differently in the v × R plane (see SlimeSettings
                // inspector heatmap). VelocityTimesOmega = original v²/R.
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
                    // Project the slime forward by lookahead distance along its
                    // current velocity, then probe DOWN (= -surfaceNormal) from
                    // that hypothetical position. If no ground is found beneath
                    // the projected slime, the surface drops away ahead — detach.
                    // If ground is still under foot, the slime would just keep
                    // sliding — keep wrapping.
                    //
                    // The earlier "ray along velocity" version misfired on flat
                    // ground because the ray was launched at slime-center height
                    // and traveled horizontally; the floor is below that height,
                    // so every flat-ground frame read as "horizon clear".
                    Vector2 velDir = velocity / Mathf.Max(0.0001f, velocity.magnitude);
                    float lookahead = settings.cornerDetachHorizonLookaheadCurve.Evaluate(tangentSpeedAbs);
                    Vector2 projectedCenter = rb.position + velDir * lookahead;
                    // Probe distance = slimeRadius + a generous skin so a small
                    // bump in surface depth (chord-arc gap, polygon facets) still
                    // counts as "ground present".
                    float probeDist = settings.slimeRadius + settings.cornerDetachHorizonProbeSkin;
                    RaycastHit2D hit = Physics2D.Raycast(projectedCenter, -surfaceNormal, probeDist, settings.groundLayer);
                    // ignoredColliders (post-launch hop, etc.) shouldn't count
                    // as ground for this check — treat them as empty space.
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

                // Visual gate: ratio surfaces only when convex sign has held ≥ 1 frame.
                // A single-frame ω spike still increments to 1, but the LP filter (rate=25,
                // dt=0.02 → k≈0.4) bleeds a 0→raw step by ~40%; combined with the visual
                // SmoothDamp downstream, transient spikes don't reach perceptible stretch.
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
