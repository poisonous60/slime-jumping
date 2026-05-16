using UnityEngine;
using UnityEngine.Rendering;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;

namespace SlimeJumping.Mechanisms
{
    // Stage-wide blizzard zone with a Jump King-style 4-phase wind cycle
    // (WindLeft → CalmAfterLeft → WindRight → CalmAfterRight). Direction and
    // magnitude lerp across a short transition window so visuals/physics ramp
    // instead of snapping. Reuses WindZone2D_v2's slime-push contract
    // (AddSustainedFlowImpulse + state multiplier + terminalSpeed falloff) with
    // a dynamic windAcceleration vector driven by the phase state machine.
    [RequireComponent(typeof(BoxCollider2D))]
    public class Snowstorm2D : MonoBehaviour
    {
        public enum Phase { WindLeft, CalmAfterLeft, WindRight, CalmAfterRight }

        [Header("Phase Timing")]
        [Min(0.1f)]
        [SerializeField] private float windDuration = 5f;

        [Min(0.1f)]
        [SerializeField] private float calmDuration = 2.5f;

        [Tooltip("Lerp duration when phase magnitude changes. 0 = instant snap.")]
        [Min(0f)]
        [SerializeField] private float transitionDuration = 0.8f;

        [SerializeField] private Phase startPhase = Phase.CalmAfterRight;

        [Tooltip("Seconds advanced into startPhase at scene start. Lets multi-zone scenes desync timers.")]
        [Min(0f)]
        [SerializeField] private float startPhaseOffset = 0f;

        [Header("Wind")]
        [Tooltip("Peak horizontal acceleration during wind phases (units/s²). Sign auto-derived from phase.")]
        [Min(0f)]
        [SerializeField] private float windAccelerationMagnitude = 18f;

        [Tooltip("Max speed along wind direction before falloff kills acceleration. 0 = unlimited.")]
        [SerializeField] private float terminalSpeed = 14f;

        [Header("State Multipliers")]
        [SerializeField] private float airborneMultiplier = 1f;
        [SerializeField] private float attachedMultiplier = 0.7f;
        [SerializeField] private float chargingMultiplier = 0.2f;

        [Header("Visual — Snow Force Field")]
        [Tooltip("Box ParticleSystemForceField that pushes snow particles. Auto-synced from BoxCollider2D bounds. Zone bounds-only — slime/snow outside the storm collider are unaffected.")]
        [SerializeField] private ParticleSystemForceField snowForceField;

        [Min(0f)]
        [SerializeField] private float snowDrag = 1.5f;

        [Header("Visual — Snow Density Boost")]
        [Tooltip("Main snowfall PS (typically the existing Snow.prefab in the scene, which already handles camera following + polygon area culling). At peak storm intensity its emission rate and start size are scaled up so the snowfall visibly thickens.")]
        [SerializeField] private ParticleSystem snowParticles;

        [Tooltip("Absolute emission rate at peak intensity (particles/sec). Linearly interpolated against the base rate captured from the PS at OnEnable. e.g. base 220 → peak 1800 ≈ 8× thicker snowfall during wind phases.")]
        [Min(0f)]
        [SerializeField] private float snowPeakRate = 1800f;

        [Tooltip("Relative size boost applied on top of the captured baseline at peak intensity. 1 = no change. e.g. base startSizeMultiplier=1, peak multiplier=1.8 → particles get 1.8× larger during peak wind.")]
        [Min(0.1f)]
        [SerializeField] private float snowPeakSizeMultiplier = 1.8f;

        [Header("Visual — Storm-owned Particles")]
        [Tooltip("Snowstorm-owned top-strip PS — spawns above the viewport, particles fall into view. Local sim space anchored to the camera. Covers the upper diagonal half of the viewport during wind (top-left → bottom-right at +X wind).")]
        [SerializeField] private ParticleSystem stormParticles;

        [Tooltip("Snowstorm-owned upwind-side-strip PS — spawns just outside the upwind viewport edge, particles drift across into view. Fills the lower diagonal half that the top-strip can't reach (because top-spawn particles fall out before drifting that far). Together with stormParticles this covers the full viewport during wind.")]
        [SerializeField] private ParticleSystem sideStormParticles;

        [Tooltip("Camera the storm PS follows. Falls back to Camera.main.")]
        [SerializeField] private UnityEngine.Camera targetCamera;

        [Tooltip("Strip thickness (perpendicular to spawn edge) in local units. Used for both top-strip Y thickness and side-strip X thickness.")]
        [Min(0.1f)]
        [SerializeField] private float stripThickness = 2f;

        [Tooltip("Safety margin between the viewport edge and the spawn strip (local units). Larger = more pre-drift travel time before particles become visible, smoother on fast camera motion.")]
        [Min(0f)]
        [SerializeField] private float stripOffset = 1.5f;

        [Tooltip("Strip extent perpendicular to its spawn edge × this multiplier (top-strip horizontal width, side-strip vertical height = viewport extent × this). >1 keeps wind-drifted particles visible at viewport corners during peak storm.")]
        [Min(1f)]
        [SerializeField] private float coverageMultiplier = 1.4f;

        [Tooltip("Top-strip baseline emission (particles/sec) during calm — keeps snow falling when the wind is silent. The wind only stops; the snow does not. Set close enough to stormPeakRate that the calm phase doesn't visually feel like a sudden cliff drop in density.")]
        [Min(0f)]
        [SerializeField] private float stormCalmRate = 900f;

        [Tooltip("Top-strip peak emission rate (particles/sec) at intensity=1. Lerps from stormCalmRate up to this value as wind ramps in.")]
        [Min(0f)]
        [SerializeField] private float stormPeakRate = 1800f;

        [Tooltip("Side-strip peak emission rate (particles/sec) at intensity=1. No calm baseline — side strip only emits during wind because in calm there is no horizontal drift to carry particles into view, so they would just pile at the spawn edge.")]
        [Min(0f)]
        [SerializeField] private float sideStormPeakRate = 1000f;

        [Tooltip("Horizontal drift speed (units/s) applied to storm particles via velocityOverLifetime. Sign follows currentDirection, magnitude scales with storm intensity. Should roughly match terminalSpeed so particles look like they belong to the wind field.")]
        [Min(0f)]
        [SerializeField] private float stormDriftSpeed = 14f;

        [Tooltip("Constant downward velocity (units/s) added to every storm particle via velocityOverLifetime.y in local space. Applies every frame, not just at spawn — combined with gravityModifier the actual fall speed grows over the particle's lifetime. Higher value = snow falls visibly faster from the spawn strip into view.")]
        [Min(0f)]
        [SerializeField] private float stormFallBias = 3f;

        [Tooltip("Random ± fraction applied to per-particle vy so trajectories aren't parallel. 0 = all particles fall at the same speed (looks like ruled lines). 0.4 ≈ ±40% jitter on the fall bias.")]
        [Range(0f, 0.9f)]
        [SerializeField] private float stormFallJitter = 0.4f;

        [Header("Post-process")]
        [Tooltip("Global URP Volume holding the storm look (vignette + cool tint). weight is driven by slime presence × storm intensity.")]
        [SerializeField] private Volume postVolume;

        [Header("Slime Presence Fade")]
        [Tooltip("Fade rate (units/sec) for the whole storm visual (particle emission + post Volume + ambient audio) when slime enters/exits the zone. 1.5 ≈ 0.66s fade in or out. Single shared rate keeps all storm components visually in sync. Replaces the previous hard zone-bounds particle cull, which made particles vanish at the zone boundary.")]
        [Min(0.01f)]
        [SerializeField] private float presenceFadeSpeed = 1.5f;

        [Header("Camera Shake")]
        [Tooltip("CinemachineImpulseSource emitter pulsed once at every phase transition start. Force is scaled by the magnitude delta of the upcoming transition.")]
        [SerializeField] private ImpulseEmitter impulseEmitter;

        [Tooltip("0 disables phase-transition shake. Default off — playtesting felt the shake interrupted reads of the wind direction telegraph more than it helped.")]
        [Min(0f)]
        [SerializeField] private float shakeForce = 0f;

        [Header("Audio")]
        [SerializeField] private AudioClip ambientLoopClip;

        [Range(0f, 1f)]
        [SerializeField] private float ambientPeakVolume = 0.6f;

        // ---- Runtime (visible in inspector for debugging; do not hand-edit at play time) ----
        [Header("Runtime (read-only)")]
        [SerializeField] private Phase currentPhase;
        [SerializeField] private float phaseElapsed;
        [SerializeField] private float currentMagnitude;
        [SerializeField] private float currentDirection;
        [SerializeField] private bool slimeInside;
        [SerializeField] private float slimePresenceWeight;

        private SlimeController trackedSlime;
        private SoundHandle ambientHandle;
        private float prevMagnitudeAtPhaseStart;

        // Snow boost baselines, captured lazily from each assigned PS so designers can
        // tune the prefab freely and the storm peaks remain expressed as absolute target
        // rates rather than opaque multipliers. The full MinMaxCurve is stored (not just
        // .constant) so OnDisable can restore PS configured with TwoConstants/Curve rate
        // modes without permanently flattening them to a single value. A separate scalar
        // is extracted for the boost lerp.
        private bool snowBaselineCaptured;
        private ParticleSystem.MinMaxCurve snowBaselineRate;
        private float snowBaselineRateScalar;
        private float snowBaselineSizeMultiplier;


        void Reset()
        {
            GetComponent<BoxCollider2D>().isTrigger = true;
        }


        void OnValidate()
        {
            SyncForceFieldGeometry();
        }

        void Awake()
        {
            currentPhase = startPhase;
            phaseElapsed = Mathf.Max(0f, startPhaseOffset);
            // Multi-phase skip: if offset exceeds the current phase, advance through full
            // phases so designers can pre-roll the cycle by an arbitrary amount.
            int safety = 16;
            while (phaseElapsed >= PhaseDuration(currentPhase) && safety-- > 0)
            {
                phaseElapsed -= PhaseDuration(currentPhase);
                currentPhase = NextPhase(currentPhase);
            }
            prevMagnitudeAtPhaseStart = SignedTargetMagnitude(currentPhase);
            currentDirection = Mathf.Sign(SignedTargetMagnitude(currentPhase));
            if (currentDirection == 0f) currentDirection = 1f;
            currentMagnitude = Mathf.Abs(SignedTargetMagnitude(currentPhase));
        }

        void OnEnable()
        {
            SyncForceFieldGeometry();
            CaptureSnowBaselines();
        }

        void OnDisable()
        {
            StopAmbient();
            if (postVolume != null) postVolume.weight = 0f;
            // Restore snow PS to its captured baselines so a disabled storm doesn't
            // leave the rate/size stuck on peak, which would visually persist after the
            // zone goes away and contaminate any other zone that shares the same
            // Snow.prefab instance.
            if (snowBaselineCaptured) RestoreSnow(snowParticles, snowBaselineRate, snowBaselineSizeMultiplier);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            UpdatePresenceWeight(dt);
            AdvancePhase(dt);
            ApplyForceField();
            ApplySnowBoost();
            ApplyPostVolume();
            ApplyAmbient();
        }

        void UpdatePresenceWeight(float dt)
        {
            // Single weight gates all storm visuals (particle emission, post Volume,
            // ambient audio, ambient Snow.prefab boost). Replaces hard zone-bounds
            // particle culling — old particles complete their lifetime naturally as
            // emission winds down on slime exit, so the storm "calms" rather than
            // snapping off at the zone boundary.
            float target = slimeInside ? 1f : 0f;
            slimePresenceWeight = Mathf.MoveTowards(slimePresenceWeight, target, presenceFadeSpeed * dt);
        }

        void LateUpdate()
        {
            // LateUpdate so the spawn-strip read of currentDirection/currentMagnitude
            // sees the post-AdvancePhase values from this frame. ApplyStormParticles
            // also reads camera position which should be after Cinemachine's update —
            // Cinemachine runs in its own LateUpdate; if it runs after this one, the
            // strip will lag camera by 1 frame. Acceptable for storm visuals (no read
            // of slime input here), and avoids a hard ScriptExecutionOrder dependency.
            ApplyStormParticles();
        }

        void FixedUpdate()
        {
            PushSlime(Time.fixedDeltaTime);
        }

        // ---- Phase state machine ----------------------------------------------------------

        void AdvancePhase(float dt)
        {
            phaseElapsed += dt;
            float duration = PhaseDuration(currentPhase);
            if (phaseElapsed >= duration)
            {
                phaseElapsed -= duration;
                Phase next = NextPhase(currentPhase);
                prevMagnitudeAtPhaseStart = SignedCurrent();
                currentPhase = next;
                OnPhaseTransitionStart();
            }

            float targetSigned = SignedTargetMagnitude(currentPhase);
            float signedNow;
            if (transitionDuration <= 0f || phaseElapsed >= transitionDuration)
            {
                signedNow = targetSigned;
            }
            else
            {
                float t = phaseElapsed / transitionDuration;
                signedNow = Mathf.Lerp(prevMagnitudeAtPhaseStart, targetSigned, t);
            }

            currentMagnitude = Mathf.Abs(signedNow);
            // Keep last non-zero direction so the ForceField doesn't snap its sign to 0
            // mid-calm — the snow particles would briefly halt their horizontal drift,
            // looking like a stutter instead of a smooth fall-off into vertical fall.
            if (Mathf.Abs(signedNow) > 0.001f) currentDirection = Mathf.Sign(signedNow);
        }

        float SignedCurrent() => currentDirection * currentMagnitude;

        void OnPhaseTransitionStart()
        {
            if (impulseEmitter != null && shakeForce > 0f)
            {
                float prevAbs = Mathf.Abs(prevMagnitudeAtPhaseStart);
                float nextAbs = Mathf.Abs(SignedTargetMagnitude(currentPhase));
                float deltaNorm = windAccelerationMagnitude > 0.001f
                    ? Mathf.Abs(nextAbs - prevAbs) / windAccelerationMagnitude
                    : 0f;
                if (deltaNorm > 0.01f) impulseEmitter.Emit(shakeForce * deltaNorm);
            }
        }

        float PhaseDuration(Phase p)
        {
            switch (p)
            {
                case Phase.WindLeft:
                case Phase.WindRight:
                    return windDuration;
                default:
                    return calmDuration;
            }
        }

        float SignedTargetMagnitude(Phase p)
        {
            switch (p)
            {
                case Phase.WindLeft: return -windAccelerationMagnitude;
                case Phase.WindRight: return +windAccelerationMagnitude;
                default: return 0f;
            }
        }

        static Phase NextPhase(Phase p)
        {
            switch (p)
            {
                case Phase.WindLeft: return Phase.CalmAfterLeft;
                case Phase.CalmAfterLeft: return Phase.WindRight;
                case Phase.WindRight: return Phase.CalmAfterRight;
                default: return Phase.WindLeft;
            }
        }

        // ---- Slime physics ----------------------------------------------------------------

        void OnTriggerEnter2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime != null)
            {
                trackedSlime = slime;
                slimeInside = true;
            }
        }

        void OnTriggerExit2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime != null && slime == trackedSlime)
            {
                trackedSlime = null;
                slimeInside = false;
            }
        }

        void PushSlime(float dt)
        {
            if (trackedSlime == null) return;
            if (currentMagnitude < 0.001f) return;

            Vector2 worldDir = new Vector2(currentDirection, 0f);

            float multiplier;
            switch (trackedSlime.CurrentState)
            {
                case SlimeController.SlimeState.Airborne:
                case SlimeController.SlimeState.Stunned:
                    multiplier = airborneMultiplier; break;
                case SlimeController.SlimeState.Charging:
                    multiplier = chargingMultiplier; break;
                default:
                    multiplier = attachedMultiplier; break;
            }
            if (multiplier <= 0f) return;

            float falloff = 1f;
            if (terminalSpeed > 0f)
            {
                float alongWind = Vector2.Dot(trackedSlime.Velocity, worldDir);
                if (alongWind > 0f)
                    falloff = Mathf.Clamp01(1f - alongWind / terminalSpeed);
            }

            // Same contract as WindZone2D_v2: AddSustainedFlowImpulse so push integrates
            // into slideVelocity/crawlVelocity in Attached/Charging instead of being
            // overwritten next tick by the state handler.
            Vector2 deltaV = worldDir * (currentMagnitude * multiplier * falloff * dt);
            trackedSlime.AddSustainedFlowImpulse(deltaV);
        }

        // ---- ForceField sync --------------------------------------------------------------

        void SyncForceFieldGeometry()
        {
            if (snowForceField == null) return;
            var box = GetComponent<BoxCollider2D>();
            if (box == null) return;

            var ff = snowForceField;
            ff.shape = ParticleSystemForceFieldShape.Box;
            // endRange=0.5 with localScale=size → field half-extents match the visible
            // collider bounds (verified in WindZone2D_v2). Without this the field is 2×
            // too large.
            ff.endRange = 0.5f;

            var ffT = ff.transform;
            if (ffT.parent == transform)
            {
                ffT.localPosition = new Vector3(box.offset.x, box.offset.y, 0f);
                ffT.localRotation = Quaternion.identity;
                // Z=100 → effectively infinite depth so 3D-noise drifts in Z don't fall
                // outside the field.
                ffT.localScale = new Vector3(box.size.x, box.size.y, 100f);
            }

            ff.multiplyDragByParticleVelocity = false;
            ff.multiplyDragByParticleSize = false;
            ff.drag = new ParticleSystem.MinMaxCurve(snowDrag);

            int layer = LayerMask.NameToLayer("WindForceField");
            if (layer >= 0 && ff.gameObject.layer != layer)
                ff.gameObject.layer = layer;
        }

        void ApplyForceField()
        {
            if (snowForceField == null) return;
            float signed = SignedCurrent();
            snowForceField.directionX = new ParticleSystem.MinMaxCurve(signed);
            snowForceField.directionY = new ParticleSystem.MinMaxCurve(0f);
            snowForceField.directionZ = new ParticleSystem.MinMaxCurve(0f);
        }

        // ---- Snow boost ------------------------------------------------------------------
        //
        // Reference: Celeste & Jump King wind levels both express the storm via *more
        // dense round snowflakes blowing horizontally*, NOT stretched white line
        // streaks. We drive emission rate + size on the existing Snow.prefab instances
        // so the scene's snowfall thickens during wind phases and visibly goes back to
        // normal during calm. Horizontal motion comes from the snowForceField above.

        void CaptureSnowBaselines()
        {
            TryCapturePsBaseline(snowParticles, ref snowBaselineCaptured, ref snowBaselineRate, ref snowBaselineRateScalar, ref snowBaselineSizeMultiplier);
        }

        static void TryCapturePsBaseline(ParticleSystem ps, ref bool captured, ref ParticleSystem.MinMaxCurve baselineRate, ref float baselineRateScalar, ref float baselineSizeMultiplier)
        {
            if (ps == null || captured) return;
            baselineRate = ps.emission.rateOverTime;
            baselineRateScalar = ExtractRateScalar(baselineRate);
            baselineSizeMultiplier = ps.main.startSizeMultiplier;
            captured = true;
        }

        // Pick a representative scalar for lerping from the captured rate. Constant /
        // TwoConstants modes give a clean answer; for Curve / TwoCurves we approximate
        // with curveMultiplier (the upper-bound scale) so the boost still lerps to a
        // sensible peak. RestoreSnow always writes back the original MinMaxCurve so the
        // mode/curve shape is preserved verbatim — the scalar approximation only matters
        // during the lerp.
        static float ExtractRateScalar(ParticleSystem.MinMaxCurve c)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant: return c.constant;
                case ParticleSystemCurveMode.TwoConstants: return (c.constantMin + c.constantMax) * 0.5f;
                case ParticleSystemCurveMode.Curve:
                case ParticleSystemCurveMode.TwoCurves: return c.curveMultiplier;
                default: return c.constant;
            }
        }

        void ApplySnowBoost()
        {
            // Lazy capture: handles late inspector wire-up after OnEnable. Without this,
            // assigning snowParticles in the inspector at runtime would silently leave
            // the baseline uncaptured and the boost a no-op.
            TryCapturePsBaseline(snowParticles, ref snowBaselineCaptured, ref snowBaselineRate, ref snowBaselineRateScalar, ref snowBaselineSizeMultiplier);

            // Boost intensity gated by slime presence — ambient snow returns to its
            // unmodified baseline when the slime is outside the zone.
            float gatedIntensity = NormalizedIntensity() * slimePresenceWeight;
            BoostSnow(snowParticles, snowBaselineCaptured, snowBaselineRateScalar, snowBaselineSizeMultiplier, snowPeakRate, snowPeakSizeMultiplier, gatedIntensity);
        }

        static void BoostSnow(ParticleSystem ps, bool captured, float baseRateScalar, float baseSizeMultiplier, float peakRate, float peakSizeMultiplier, float intensity)
        {
            if (ps == null || !captured) return;

            var emis = ps.emission;
            emis.rateOverTime = new ParticleSystem.MinMaxCurve(Mathf.Lerp(baseRateScalar, peakRate, intensity));

            var main = ps.main;
            main.startSizeMultiplier = Mathf.Lerp(baseSizeMultiplier, baseSizeMultiplier * peakSizeMultiplier, intensity);
        }

        static void RestoreSnow(ParticleSystem ps, ParticleSystem.MinMaxCurve baselineRate, float baseSizeMultiplier)
        {
            if (ps == null) return;
            var emis = ps.emission;
            emis.rateOverTime = baselineRate;
            var main = ps.main;
            main.startSizeMultiplier = baseSizeMultiplier;
        }

        float NormalizedIntensity()
        {
            return windAccelerationMagnitude > 0.001f
                ? Mathf.Clamp01(currentMagnitude / windAccelerationMagnitude)
                : 0f;
        }

        // ---- Storm-owned particles -------------------------------------------------------
        //
        // Camera-anchored Local-space PS pair. Both transforms follow the camera every
        // LateUpdate; particles simulate in PS-local coordinates, so they are visually
        // glued to the viewport regardless of how fast the slime/camera moves — solves
        // the "snow gets left behind on fast jump" problem of World-space sim.
        //
        // Two strips: top (above viewport, particles fall in) + side (just outside the
        // upwind viewport edge, particles drift in). Top alone leaves the lower-half
        // of the downwind diagonal empty because top-spawn particles fall out of view
        // before drifting that far horizontally; the side strip fills that gap.
        //
        // Wind drift = horizontal local-space velocity, scaled by storm intensity.
        // During calm: top emission held at stormCalmRate (snow keeps falling), side
        // emission goes to zero (no horizontal drift to carry particles in, would
        // just pile at the spawn edge).

        void ApplyStormParticles()
        {
            if (targetCamera == null) targetCamera = UnityEngine.Camera.main;
            if (targetCamera == null) return;

            // Viewport extents (orthographic — perspective fallback for completeness).
            float halfH = targetCamera.orthographic
                ? targetCamera.orthographicSize
                : Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad)
                  * Mathf.Max(Mathf.Abs(targetCamera.transform.position.z), 0.01f);
            float halfW = halfH * targetCamera.aspect;
            float intensity = NormalizedIntensity();
            Vector3 cam = targetCamera.transform.position;

            ApplyTopStrip(cam, halfW, halfH, intensity);
            ApplySideStrip(cam, halfW, halfH, intensity);
        }

        void ApplyTopStrip(Vector3 cam, float halfW, float halfH, float intensity)
        {
            if (stormParticles == null) return;

            stormParticles.transform.SetPositionAndRotation(
                new Vector3(cam.x, cam.y, 0f), Quaternion.identity);

            var shape = stormParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.rotation = Vector3.zero;
            shape.boxThickness = Vector3.zero;
            shape.position = new Vector3(0f, halfH + stripOffset + stripThickness * 0.5f, 0f);
            shape.scale = new Vector3(halfW * 2f * coverageMultiplier, stripThickness, 0f);

            // Calm baseline + peak ramp, then gated by slime presence so the snowfall
            // fades in/out smoothly as the slime enters/leaves the zone.
            var emis = stormParticles.emission;
            emis.rateOverTime = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(stormCalmRate, stormPeakRate, intensity) * slimePresenceWeight);

            ApplyStormVelocity(stormParticles, intensity);
        }

        void ApplySideStrip(Vector3 cam, float halfW, float halfH, float intensity)
        {
            if (sideStormParticles == null) return;

            sideStormParticles.transform.SetPositionAndRotation(
                new Vector3(cam.x, cam.y, 0f), Quaternion.identity);

            var shape = sideStormParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.rotation = Vector3.zero;
            shape.boxThickness = Vector3.zero;
            // Strip on the upwind side — opposite the wind direction. Wind=+X → strip
            // on left (−X). Particles spawn there and drift +X into the viewport.
            shape.position = new Vector3(
                -currentDirection * (halfW + stripOffset + stripThickness * 0.5f), 0f, 0f);
            shape.scale = new Vector3(stripThickness, halfH * 2f * coverageMultiplier, 0f);

            // Side strip only emits during wind — calm has vx=0, so spawned particles
            // would just pile at the side edge and look wrong. Also gated by slime
            // presence so the side fill fades with the rest of the storm.
            var emis = sideStormParticles.emission;
            emis.rateOverTime = new ParticleSystem.MinMaxCurve(
                sideStormPeakRate * intensity * slimePresenceWeight);

            ApplyStormVelocity(sideStormParticles, intensity);
        }

        void ApplyStormVelocity(ParticleSystem ps, float intensity)
        {
            // Local-space velocity. PS transform tracks camera, so local +X = world +X
            // (no rotation), but particles move with PS when camera moves. vx scales
            // with intensity so calm has zero horizontal drift; vy is a downward bias
            // (with random jitter so trajectories don't form visible parallel lines)
            // on top of gravityModifier so snow falls visibly even when the wind is
            // silent.
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            // All three axes must share the same MinMaxCurve mode (TwoConstants),
            // else Unity throws "Particle Velocity curves must all be in the same mode".
            float vx = currentDirection * stormDriftSpeed * intensity;
            float vyMin = -stormFallBias * (1f + stormFallJitter);
            float vyMax = -stormFallBias * (1f - stormFallJitter);
            vel.x = new ParticleSystem.MinMaxCurve(vx, vx);
            vel.y = new ParticleSystem.MinMaxCurve(vyMin, vyMax);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }


        // ---- Post Volume ------------------------------------------------------------------

        void ApplyPostVolume()
        {
            if (postVolume == null) return;
            // slimePresenceWeight already provides the smooth in/out fade; no extra
            // MoveTowards needed. Direct assignment so post weight tracks particles
            // exactly in lockstep.
            postVolume.weight = slimePresenceWeight * NormalizedIntensity();
        }

        // ---- Audio ------------------------------------------------------------------------

        void ApplyAmbient()
        {
            if (ambientLoopClip == null) return;

            // Volume gated by slime presence so the loop fades in/out with the rest
            // of the storm when the slime crosses the zone boundary.
            float volume = NormalizedIntensity() * slimePresenceWeight * ambientPeakVolume;

            if (volume > 0.001f)
            {
                if (!ambientHandle.IsValid)
                {
                    ambientHandle = SoundService.PlayLooping(
                        ambientLoopClip,
                        follow: transform,
                        category: SoundCategory.SFX_Environment,
                        volume: volume,
                        spatial3D: false);
                }
                else
                {
                    ambientHandle.SetVolume(volume);
                }
            }
            else
            {
                // Release the pooled AudioSource during calm so it isn't held with
                // locked=true for the lifetime of the zone.
                StopAmbient();
            }
        }

        void StopAmbient()
        {
            if (!ambientHandle.IsValid) return;
            SoundService.StopLoop(ambientHandle);
            ambientHandle = default;
        }

        // ---- Gizmos -----------------------------------------------------------------------

        void OnDrawGizmosSelected()
        {
            var box = GetComponent<BoxCollider2D>();
            if (box == null) return;

            Color fill = new Color(0.6f, 0.85f, 1f, 0.10f);
            Color outline = new Color(0.6f, 0.85f, 1f, 0.55f);

            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(
                transform.TransformPoint(box.offset),
                transform.rotation, transform.lossyScale);
            Gizmos.color = fill;
            Gizmos.DrawCube(Vector3.zero, box.size);
            Gizmos.color = outline;
            Gizmos.DrawWireCube(Vector3.zero, box.size);
            Gizmos.matrix = old;

            float magForGizmo = Application.isPlaying ? currentMagnitude : windAccelerationMagnitude;
            float dirForGizmo = Application.isPlaying ? currentDirection : 1f;
            if (magForGizmo < 0.001f) return;

            Vector3 center = transform.TransformPoint(box.offset);
            Vector3 dir = transform.right * dirForGizmo;
            float len = Mathf.Clamp(magForGizmo * 0.1f, 0.5f, 4f);
            Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;

            Gizmos.color = new Color(0.5f, 0.85f, 1f, 0.7f);
            for (int i = -2; i <= 2; i++)
            {
                Vector3 offset = perp * (i * 0.6f);
                Vector3 start = center + offset;
                Vector3 tip = start + dir * len;
                Gizmos.DrawLine(start, tip);
                Gizmos.DrawLine(tip, tip - dir * 0.3f + perp * 0.15f);
                Gizmos.DrawLine(tip, tip - dir * 0.3f - perp * 0.15f);
            }
        }
    }
}
