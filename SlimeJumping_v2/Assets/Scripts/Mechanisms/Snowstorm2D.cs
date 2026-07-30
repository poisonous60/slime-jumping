using UnityEngine;
using UnityEngine.Rendering;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;

namespace SlimeJumping.Mechanisms
{
    // Stage-wide blizzard zone: 4-phase wind cycle (WindLeft → CalmAfterLeft →
    // WindRight → CalmAfterRight), direction/magnitude lerped over a transition window.
    // Slime push reuses WindZone2D_v2's contract (AddSustainedFlowImpulse + state
    // multiplier + terminalSpeed falloff) with a phase-driven windAcceleration vector.
    [RequireComponent(typeof(PolygonCollider2D))]
    public class Snowstorm2D : MonoBehaviour
    {
        public enum Phase { WindLeft, CalmAfterLeft, WindRight, CalmAfterRight }

        [System.Serializable]
        public class StormSection
        {
            [Tooltip("Optional label for designers. Runtime logic does not read it.")]
            public string label;

            [Tooltip("Collider that marks this snowstorm section. Usually a child trigger BoxCollider2D. First matching section wins.")]
            public Collider2D area;

            [Tooltip("When enabled, this section keeps using the global idle/right/idle/left phase cycle.")]
            public bool usePhaseCycle;

            [Tooltip("Fixed horizontal wind acceleration inside this section when Use Phase Cycle is off. Only X is used. (-6,0)=weak left wind, (0,0)=snow-only calm.")]
            public Vector2 windAcceleration = Vector2.zero;

            [Tooltip("If off, the section changes visuals/particles but does not push the slime.")]
            public bool affectsSlime = true;

            [Tooltip("If off, ambient/storm visuals can still fade in, but the ParticleSystemForceField receives zero wind in this section.")]
            public bool affectsSnowParticles = true;
        }

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

        [Tooltip("On slime entry, reset the phase machine to startPhase + startPhaseOffset so the storm always begins from a known state. Set startPhase to a Calm* value to have the storm begin calm on every entry.")]
        [SerializeField] private bool resetPhaseOnSlimeEnter = true;

        [Header("Wind")]
        [Tooltip("Peak horizontal acceleration during wind phases (units/s²). Sign auto-derived from phase.")]
        [Min(0f)]
        [SerializeField] private float windAccelerationMagnitude = 18f;

        [Tooltip("Max speed along wind direction before falloff kills acceleration. 0 = unlimited.")]
        [SerializeField] private float terminalSpeed = 14f;

        [Header("Wind Sections")]
        [Tooltip("Optional per-area wind overrides. Empty = original 4-phase storm everywhere. Put child BoxCollider2D triggers here to make areas with weak fixed wind, snow-only calm, etc.")]
        [SerializeField] private StormSection[] sections;

        [Tooltip("Optional areas where the slime is sheltered from wind push (interiors, behind walls). Visual snowfall and storm audio/post remain driven by the active section.")]
        [SerializeField] private Collider2D[] windShelterAreas;

        [Header("State Multipliers")]
        [SerializeField] private float airborneMultiplier = 1f;
        [SerializeField] private float attachedMultiplier = 0.7f;
        [SerializeField] private float chargingMultiplier = 0.2f;

        [Header("Visual — Snow Force Field")]
        [Tooltip("Box ParticleSystemForceField that pushes ambient snow sideways. Camera-anchored and viewport-sized at runtime (NOT tied to the zone collider) so the whole screen's snow drifts while the slime is inside the zone. The PolygonCollider2D is only the on/off trigger; drift strength is gated by slime presence.")]
        [SerializeField] private ParticleSystemForceField snowForceField;

        [Min(0f)]
        [SerializeField] private float snowDrag = 1.5f;

        [Header("Visual — Stage Snow Swap")]
        [Tooltip("Existing stage-wide snow PS (e.g. the always-on Snow.prefab covering the whole stage) that should fade OUT while the slime is inside this zone, so the stage's ambient snowfall and the storm visuals SWAP rather than stack. Inverse of slimePresenceWeight: full snow outside the zone, off inside. Emission is gated to baseline×(1−presence) and renderer alpha to (1−presence) so in-flight flakes clear in lockstep. Restored to baseline on disable. Distinct from snowParticles above, which is a presence-GATED boost target shown only INSIDE the zone.")]
        [SerializeField] private ParticleSystem[] suppressedSnowParticles;

        [Header("Visual — Storm-owned Particles")]
        [Tooltip("Snowstorm-owned PS — full-viewport volume spawn, Local sim space anchored to the camera. Emission-rate changes appear uniformly across the screen instead of as a density band falling in from the top; colorOverLifetime fade-in/out (applied at OnEnable) hides mid-screen spawn/death pops, same approach as Snow.prefab.")]
        [SerializeField] private ParticleSystem stormParticles;

        [Tooltip("Camera the storm PS follows. Falls back to Camera.main.")]
        [SerializeField] private UnityEngine.Camera targetCamera;

        [Tooltip("Spawn box extent = viewport extent × this multiplier (both axes). >1 keeps particles just outside the viewport already falling/drifting before they scroll into view on camera motion.")]
        [Min(1f)]
        [SerializeField] private float coverageMultiplier = 1.4f;

        [Tooltip("Particle lifetime (s). Also sizes the upstream spawn margin (= velocity × lifetime, added on the side particles come FROM), which guarantees the steady-state density at the viewport edge equals the saturated interior — without the margin the spawn-box edge shows a sparse band (same flaw the stage snow mitigates with its heightMultiplier). Longer = flakes visible longer but a larger off-screen box and particle pool.")]
        [Min(0.5f)]
        [SerializeField] private float stormParticleLifetime = 2.5f;

        [Tooltip("Baseline emission (particles/sec) during calm — keeps snow falling when the wind is silent. The wind only stops; the snow does not. Set close enough to stormPeakRate that the calm phase doesn't visually feel like a sudden cliff drop in density.")]
        [Min(0f)]
        [SerializeField] private float stormCalmRate = 900f;

        [Tooltip("Peak emission rate (particles/sec) at intensity=1. Lerps from stormCalmRate up to this value as wind ramps in.")]
        [Min(0f)]
        [SerializeField] private float stormPeakRate = 1800f;

        [Tooltip("Random ± fraction applied to per-particle vy so trajectories aren't parallel. 0 = all particles fall at the same speed (looks like ruled lines). 0.4 ≈ ±40% jitter on the fall bias.")]
        [Range(0f, 0.9f)]
        [SerializeField] private float stormFallJitter = 0.4f;

        [Header("Visual — Storm Particle Velocity (local PS space)")]
        [Tooltip("Storm-particle velocity (units/s) during calm phases (no wind). X = horizontal drift, Y = vertical fall (negative = down). Lerp anchor at intensity=0.")]
        [SerializeField] private Vector2 calmVelocity = new Vector2(0f, -3f);

        [Tooltip("Storm-particle velocity (units/s) at peak WindLeft (intensity=1, direction=-1). X should be negative (leftward drift). Y = vertical fall (negative = down).")]
        [SerializeField] private Vector2 leftPeakVelocity = new Vector2(-14f, -3f);

        [Tooltip("Storm-particle velocity (units/s) at peak WindRight (intensity=1, direction=+1). X should be positive (rightward drift). Y = vertical fall (negative = down).")]
        [SerializeField] private Vector2 rightPeakVelocity = new Vector2(14f, -3f);

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

        [Header("Runtime (read-only)")]
        [SerializeField] private Phase currentPhase;
        [SerializeField] private float phaseElapsed;
        [SerializeField] private float currentMagnitude;
        [SerializeField] private float currentDirection;
        [SerializeField] private Vector2 currentWindAcceleration;
        [SerializeField] private int activeSectionIndex = -1;
        [SerializeField] private bool activeSectionAffectsSlime = true;
        [SerializeField] private bool activeSectionAffectsSnowParticles = true;
        [SerializeField] private bool slimeInside;
        [SerializeField] private float slimePresenceWeight;

        private SlimeController trackedSlime;
        private int overlappingColliderCount;
        private SoundHandle ambientHandle;
        private Vector2 windTransitionStart;
        private Vector2 windTransitionTarget;
        private float windTransitionElapsed;

        // Alpha fades via MPB, never the prefab's shared material. Emission runs at full
        // rate always so a steady-state population already exists when the slime enters.
        private ParticleSystemRenderer stormRenderer;
        private MaterialPropertyBlock stormMpb;
        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_TintColorId = Shader.PropertyToID("_TintColor");

        // Full MinMaxCurve kept alongside the scalar so restore-on-disable preserves the
        // original curve mode.
        private bool suppressedBaselinesCaptured;
        private ParticleSystem.MinMaxCurve[] suppressedBaselineRates;
        private float[] suppressedBaselineRateScalars;
        private ParticleSystemRenderer[] suppressedSnowRenderers;


        void Reset()
        {
            GetComponent<PolygonCollider2D>().isTrigger = true;
        }


        void OnValidate()
        {
            NormalizeSectionWindInputs();
            SyncForceFieldGeometry();
        }

        void NormalizeSectionWindInputs()
        {
            if (sections == null) return;
            for (int i = 0; i < sections.Length; i++)
            {
                StormSection section = sections[i];
                if (section == null) continue;
                section.windAcceleration.y = 0f;
            }
        }

        void Awake()
        {
            InitPhaseFromStart();
        }

        void InitPhaseFromStart()
        {
            currentPhase = startPhase;
            phaseElapsed = Mathf.Max(0f, startPhaseOffset);
            int safety = 16;
            while (phaseElapsed >= PhaseDuration(currentPhase) && safety-- > 0)
            {
                phaseElapsed -= PhaseDuration(currentPhase);
                currentPhase = NextPhase(currentPhase);
            }
            Vector2 initialWind = ResolveDesiredWind();
            windTransitionStart = initialWind;
            windTransitionTarget = initialWind;
            windTransitionElapsed = transitionDuration;
            currentWindAcceleration = initialWind;
            // Keep last non-zero direction when calm, else the force field snaps its sign.
            if (Mathf.Abs(initialWind.x) > 0.001f) currentDirection = Mathf.Sign(initialWind.x);
            else if (currentDirection == 0f) currentDirection = 1f;
            currentMagnitude = initialWind.magnitude;
        }

        void OnEnable()
        {
            SyncForceFieldGeometry();
            CaptureSuppressedSnowBaselines();
            ConfigureStormForFade();
        }

        void ConfigureStormForFade()
        {
            // prewarm, else the first zone entry shows an empty viewport for ~lifetime seconds.
            if (stormParticles != null)
            {
                var m = stormParticles.main;
                m.prewarm = true;
                // Hitting maxParticles silently throttles emission → sparse-spawn look.
                m.maxParticles = Mathf.Max(m.maxParticles, 20000);
                stormRenderer = stormParticles.GetComponent<ParticleSystemRenderer>();

                // Per-particle alpha fade hides the spawn/death pop of mid-screen particles
                // that the full-viewport volume spawn creates.
                var col = stormParticles.colorOverLifetime;
                col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[]
                    {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(1f, 0.15f),
                        new GradientAlphaKey(1f, 0.85f),
                        new GradientAlphaKey(0f, 1f),
                    });
                col.color = new ParticleSystem.MinMaxGradient(grad);
            }
            if (stormMpb == null) stormMpb = new MaterialPropertyBlock();
        }

        void OnDisable()
        {
            // No OnTriggerExit2D arrives while disabled — treat disable as exit, else a stale
            // count keeps slimeInside true forever.
            trackedSlime = null;
            slimeInside = false;
            overlappingColliderCount = 0;

            StopAmbient();
            if (postVolume != null) postVolume.weight = 0f;
            // Restore, else a disabled storm leaves the stage snowfall stuck suppressed.
            if (suppressedBaselinesCaptured && suppressedSnowParticles != null)
            {
                if (stormMpb == null) stormMpb = new MaterialPropertyBlock();
                for (int i = 0; i < suppressedSnowParticles.Length; i++)
                {
                    ParticleSystem ps = suppressedSnowParticles[i];
                    if (ps == null) continue;
                    var emis = ps.emission;
                    emis.rateOverTime = suppressedBaselineRates[i];
                    ApplyAlphaToRenderer(suppressedSnowRenderers[i], Color.white);
                }
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            UpdatePresenceWeight(dt);
            AdvancePhase(dt);
            ApplyForceField();
            ApplySuppressedSnow();
            ApplyPostVolume();
            ApplyAmbient();
        }

        void UpdatePresenceWeight(float dt)
        {
            // One weight gates every storm visual. A hard zone-bounds particle cull was
            // rejected: it snapped the snow off at the boundary instead of calming down.
            float target = slimeInside ? 1f : 0f;
            slimePresenceWeight = Mathf.MoveTowards(slimePresenceWeight, target, presenceFadeSpeed * dt);
        }

        void LateUpdate()
        {
            // LateUpdate: sees this frame's post-AdvancePhase values and the camera after
            // Cinemachine. If Cinemachine's LateUpdate runs later the visuals lag 1 frame —
            // accepted, avoids a hard ScriptExecutionOrder dependency.
            ApplyStormParticles();
            ApplyStormAlpha();
        }

        void ApplyStormAlpha()
        {
            // Write all three color property names (_BaseColor/_Color/_TintColor) so it works
            // whatever shader the prefab uses — MPB ignores properties a shader lacks.
            if (stormMpb == null) stormMpb = new MaterialPropertyBlock();
            Color c = new Color(1f, 1f, 1f, slimePresenceWeight);
            ApplyAlphaToRenderer(stormRenderer, c);

            // Stage snow fades OUT on the same weight (inverted) so in-flight flakes clear
            // in lockstep with the emission gate. Lazy capture guards late inspector wire-up.
            if (!suppressedBaselinesCaptured) CaptureSuppressedSnowBaselines();
            if (suppressedSnowRenderers != null)
            {
                Color keepColor = new Color(1f, 1f, 1f, 1f - slimePresenceWeight);
                for (int i = 0; i < suppressedSnowRenderers.Length; i++)
                    ApplyAlphaToRenderer(suppressedSnowRenderers[i], keepColor);
            }
        }

        void ApplyAlphaToRenderer(ParticleSystemRenderer r, Color c)
        {
            if (r == null) return;
            r.GetPropertyBlock(stormMpb);
            stormMpb.SetColor(s_BaseColorId, c);
            stormMpb.SetColor(s_ColorId, c);
            stormMpb.SetColor(s_TintColorId, c);
            r.SetPropertyBlock(stormMpb);
        }

        void FixedUpdate()
        {
            RefreshWindTargetForCurrentPosition();
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
                currentPhase = NextPhase(currentPhase);
            }

            Vector2 desiredWind = ResolveDesiredWind();
            if ((desiredWind - windTransitionTarget).sqrMagnitude > 0.0001f)
            {
                Vector2 previousTarget = windTransitionTarget;
                windTransitionStart = currentWindAcceleration;
                windTransitionTarget = desiredWind;
                windTransitionElapsed = 0f;
                OnWindTransitionStart(previousTarget, desiredWind);
            }

            if (transitionDuration <= 0f)
            {
                currentWindAcceleration = windTransitionTarget;
            }
            else
            {
                windTransitionElapsed += dt;
                float t = Mathf.Clamp01(windTransitionElapsed / transitionDuration);
                currentWindAcceleration = Vector2.Lerp(windTransitionStart, windTransitionTarget, t);
            }

            currentMagnitude = currentWindAcceleration.magnitude;
            // Keep last non-zero direction, else the drift sign snaps to 0 mid-calm and the
            // snow's horizontal motion stutters instead of easing into vertical fall.
            if (Mathf.Abs(currentWindAcceleration.x) > 0.001f)
                currentDirection = Mathf.Sign(currentWindAcceleration.x);
        }

        void RefreshWindTargetForCurrentPosition()
        {
            Vector2 desiredWind = ResolveDesiredWind();
            if ((desiredWind - windTransitionTarget).sqrMagnitude <= 0.0001f) return;

            Vector2 previousTarget = windTransitionTarget;
            windTransitionStart = currentWindAcceleration;
            windTransitionTarget = desiredWind;
            windTransitionElapsed = 0f;
            OnWindTransitionStart(previousTarget, desiredWind);
        }

        void OnWindTransitionStart(Vector2 previousWind, Vector2 nextWind)
        {
            if (impulseEmitter != null && shakeForce > 0f)
            {
                float referenceMagnitude = ReferenceWindMagnitude();
                float deltaNorm = referenceMagnitude > 0.001f
                    ? Mathf.Abs(nextWind.magnitude - previousWind.magnitude) / referenceMagnitude
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

        Vector2 ResolveDesiredWind()
        {
            activeSectionIndex = -1;
            activeSectionAffectsSlime = true;
            activeSectionAffectsSnowParticles = true;

            StormSection section = FindActiveSection();
            if (section != null)
            {
                activeSectionAffectsSlime = section.affectsSlime;
                activeSectionAffectsSnowParticles = section.affectsSnowParticles;
                if (!section.usePhaseCycle) return HorizontalWind(section.windAcceleration);
            }

            return PhaseWindAcceleration(currentPhase);
        }

        static Vector2 HorizontalWind(Vector2 wind)
        {
            return new Vector2(wind.x, 0f);
        }

        StormSection FindActiveSection()
        {
            if (trackedSlime == null || sections == null) return null;
            Vector2 point = trackedSlime.Position;
            for (int i = 0; i < sections.Length; i++)
            {
                StormSection section = sections[i];
                if (section == null || section.area == null) continue;
                if (!section.area.OverlapPoint(point)) continue;
                activeSectionIndex = i;
                return section;
            }
            return null;
        }

        Vector2 PhaseWindAcceleration(Phase p)
        {
            switch (p)
            {
                case Phase.WindLeft: return new Vector2(-windAccelerationMagnitude, 0f);
                case Phase.WindRight: return new Vector2(+windAccelerationMagnitude, 0f);
                default: return Vector2.zero;
            }
        }

        float ReferenceWindMagnitude()
        {
            float max = windAccelerationMagnitude;
            if (sections != null)
            {
                for (int i = 0; i < sections.Length; i++)
                {
                    StormSection section = sections[i];
                    if (section == null || section.usePhaseCycle) continue;
                    max = Mathf.Max(max, section.windAcceleration.magnitude);
                }
            }
            return max;
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
                bool wasInside = slimeInside;
                overlappingColliderCount++;
                trackedSlime = slime;
                slimeInside = true;
                // Reset only on true outside→inside transitions so child colliders
                // re-entering the trigger don't re-roll the phase mid-stay.
                if (!wasInside && resetPhaseOnSlimeEnter) InitPhaseFromStart();
            }
        }

        void OnTriggerExit2D(Collider2D other)
        {
            var slime = other.GetComponentInParent<SlimeController>();
            if (slime != null && slime == trackedSlime)
            {
                overlappingColliderCount--;
                if (overlappingColliderCount > 0) return; // other colliders still inside

                trackedSlime = null;
                slimeInside = false;
            }
        }

        void PushSlime(float dt)
        {
            if (trackedSlime == null) return;
            if (!activeSectionAffectsSlime) return;
            if (IsInWindShelter(trackedSlime.Position)) return;
            if (currentMagnitude < 0.001f) return;

            Vector2 worldDir = currentWindAcceleration / currentMagnitude;

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

            // AddSustainedFlowImpulse (WindZone2D_v2 contract), else the Attached/Charging
            // state handler overwrites the push next tick.
            Vector2 deltaV = worldDir * (currentMagnitude * multiplier * falloff * dt);
            trackedSlime.AddSustainedFlowImpulse(deltaV);
        }

        bool IsInWindShelter(Vector2 point)
        {
            if (windShelterAreas == null) return false;
            for (int i = 0; i < windShelterAreas.Length; i++)
            {
                Collider2D shelter = windShelterAreas[i];
                if (shelter == null) continue;
                if (shelter.OverlapPoint(point)) return true;
            }
            return false;
        }

        // ---- ForceField sync --------------------------------------------------------------

        // Static config only. Geometry is NOT from the zone collider — the PolygonCollider2D
        // is just the on/off trigger; the field follows the camera (ApplyForceFieldToViewport).
        void SyncForceFieldGeometry()
        {
            if (snowForceField == null) return;
            var ff = snowForceField;
            ff.shape = ParticleSystemForceFieldShape.Box;
            // endRange=0.5 with localScale=size → half-extents = size/2; else 2× too large.
            ff.endRange = 0.5f;
            // Zero radial gravity/rotation, else the camera-following field reads as a
            // gravity well clumping snow toward the slime.
            ff.gravity = new ParticleSystem.MinMaxCurve(0f);
            ff.rotationSpeed = new ParticleSystem.MinMaxCurve(0f);
            ff.multiplyDragByParticleVelocity = false;
            ff.multiplyDragByParticleSize = false;
            ff.drag = new ParticleSystem.MinMaxCurve(snowDrag);

            int layer = LayerMask.NameToLayer("WindForceField");
            if (layer >= 0 && ff.gameObject.layer != layer)
                ff.gameObject.layer = layer;
        }

        // Camera-anchored and viewport-sized so ambient snow drifts full-screen; the
        // coverageMultiplier margin avoids a seam at the screen edge on camera motion.
        void ApplyForceFieldToViewport(Vector3 cam, float halfW, float halfH)
        {
            if (snowForceField == null) return;
            var ffT = snowForceField.transform;
            ffT.position = new Vector3(cam.x, cam.y, 0f);
            ffT.rotation = Quaternion.identity;
            // Target is a WORLD size, so divide out parent lossyScale — scaling the storm
            // GameObject must not resize the field. Z=100 keeps Z-noise inside.
            Vector3 parentScale = ffT.parent != null ? ffT.parent.lossyScale : Vector3.one;
            ffT.localScale = new Vector3(
                halfW * 2f * coverageMultiplier / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                halfH * 2f * coverageMultiplier / Mathf.Max(0.0001f, Mathf.Abs(parentScale.y)),
                100f / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
        }

        void ApplyForceField()
        {
            if (snowForceField == null) return;
            Vector2 wind = activeSectionAffectsSnowParticles ? currentWindAcceleration : Vector2.zero;
            wind *= slimePresenceWeight;
            snowForceField.directionX = new ParticleSystem.MinMaxCurve(wind.x);
            snowForceField.directionY = new ParticleSystem.MinMaxCurve(wind.y);
            snowForceField.directionZ = new ParticleSystem.MinMaxCurve(0f);
        }

        // Curve / TwoCurves have no exact scalar — approximated by curveMultiplier for the
        // lerp only; restore writes back the original MinMaxCurve verbatim.
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

        // ---- Stage snow swap -------------------------------------------------------------
        //
        // The separate always-on stage Snow.prefab cross-fades against the storm on the same
        // slimePresenceWeight, inverted, so the two snowfalls swap instead of stacking:
        // emission gate here + renderer alpha fade in ApplyStormAlpha.

        void CaptureSuppressedSnowBaselines()
        {
            if (suppressedBaselinesCaptured || suppressedSnowParticles == null) return;
            int n = suppressedSnowParticles.Length;
            suppressedBaselineRates = new ParticleSystem.MinMaxCurve[n];
            suppressedBaselineRateScalars = new float[n];
            suppressedSnowRenderers = new ParticleSystemRenderer[n];
            for (int i = 0; i < n; i++)
            {
                ParticleSystem ps = suppressedSnowParticles[i];
                if (ps == null) continue;
                suppressedBaselineRates[i] = ps.emission.rateOverTime;
                suppressedBaselineRateScalars[i] = ExtractRateScalar(ps.emission.rateOverTime);
                suppressedSnowRenderers[i] = ps.GetComponent<ParticleSystemRenderer>();
            }
            suppressedBaselinesCaptured = true;
        }

        void ApplySuppressedSnow()
        {
            // Lazy capture handles inspector wire-up after OnEnable.
            CaptureSuppressedSnowBaselines();
            if (!suppressedBaselinesCaptured || suppressedSnowParticles == null) return;

            float keep = 1f - slimePresenceWeight;
            for (int i = 0; i < suppressedSnowParticles.Length; i++)
            {
                ParticleSystem ps = suppressedSnowParticles[i];
                if (ps == null) continue;
                var emis = ps.emission;
                emis.rateOverTime = new ParticleSystem.MinMaxCurve(suppressedBaselineRateScalars[i] * keep);
            }
        }

        float NormalizedIntensity()
        {
            float referenceMagnitude = ReferenceWindMagnitude();
            return referenceMagnitude > 0.001f
                ? Mathf.Clamp01(currentMagnitude / referenceMagnitude)
                : 0f;
        }

        // ---- Storm-owned particles -------------------------------------------------------
        //
        // Camera-anchored Local-space PS: World-space sim leaves the snow behind on a fast
        // jump. Full-viewport volume spawn replaced top+side strips, where a rate change
        // arrived as a visible band falling in from the top; mid-screen spawn/death pops are
        // hidden by the colorOverLifetime fade (ConfigureStormForFade). Calm keeps emitting
        // at stormCalmRate — the wind stops, the snow does not.

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

            ApplyStormVolume(cam, halfW, halfH, intensity);
            ApplyForceFieldToViewport(cam, halfW, halfH);
        }

        void ApplyStormVolume(Vector3 cam, float halfW, float halfH, float intensity)
        {
            if (stormParticles == null) return;

            stormParticles.transform.SetPositionAndRotation(
                new Vector3(cam.x, cam.y, 0f), Quaternion.identity);

            // Upstream spawn margin: a volume spawn only reaches saturated density |v|×L
            // downstream of the box edge, so extend the box by |v|×L on the upstream side
            // to push that sparse ramp off-screen.
            Vector2 vel = GetStormVelocity(intensity);
            float life = stormParticleLifetime;
            float baseW = halfW * 2f * coverageMultiplier;
            float baseH = halfH * 2f * coverageMultiplier;
            float sizeX = baseW + Mathf.Abs(vel.x) * life;
            float sizeY = baseH + Mathf.Abs(vel.y) * life;

            var shape = stormParticles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.rotation = Vector3.zero;
            shape.boxThickness = Vector3.zero;
            // Shift center upstream by half the margin so the base box stays camera-centered.
            shape.position = new Vector3(-vel.x * life * 0.5f, -vel.y * life * 0.5f, 0f);
            shape.scale = new Vector3(sizeX, sizeY, 0f);

            // Always emit at full rate (visibility is alpha-gated) so snow is already on
            // screen at zone entry. areaScale holds on-screen density constant as the
            // upstream margin grows — else peak wind visibly THINS the snow.
            float areaScale = (sizeX * sizeY) / (baseW * baseH);
            var emis = stormParticles.emission;
            emis.rateOverTime = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(stormCalmRate, stormPeakRate, intensity) * areaScale);

            var main = stormParticles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life);

            ApplyStormVelocity(stormParticles, intensity);
        }

        Vector2 GetStormVelocity(float intensity)
        {
            Vector2 peak = currentDirection >= 0f ? rightPeakVelocity : leftPeakVelocity;
            return Vector2.Lerp(calmVelocity, peak, intensity);
        }

        void ApplyStormVelocity(ParticleSystem ps, float intensity)
        {
            // Zero gravityModifier/startSpeed, else the prefab's downward velocity adds on
            // top of Vy and an upward Vy still falls.
            var main = ps.main;
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;

            Vector2 actual = GetStormVelocity(intensity);

            float vyA = actual.y * (1f + stormFallJitter);
            float vyB = actual.y * (1f - stormFallJitter);
            float vyMin = Mathf.Min(vyA, vyB);
            float vyMax = Mathf.Max(vyA, vyB);

            // All three axes must share the same MinMaxCurve mode (TwoConstants),
            // else Unity throws "Particle Velocity curves must all be in the same mode".
            vel.x = new ParticleSystem.MinMaxCurve(actual.x, actual.x);
            vel.y = new ParticleSystem.MinMaxCurve(vyMin, vyMax);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        }

        // Overwrites calm/left/right Vy with the stage snow's fall speed so the boundary
        // cross-fade reads as one snowfall. Snow.prefab falls via velocityOverLifetime
        // (startSpeed=0), so read that module, not startSpeed.
        [ContextMenu("Sync Storm Vy To Stage Snow")]
        void SyncStormVyToStageSnow()
        {
            ParticleSystem stageSnow =
                suppressedSnowParticles != null && suppressedSnowParticles.Length > 0
                    ? suppressedSnowParticles[0]
                    : null;
            if (stageSnow == null)
            {
                Debug.LogWarning("[Snowstorm2D] suppressedSnowParticles[0] not assigned — cannot sync Vy.", this);
                return;
            }
            var vol = stageSnow.velocityOverLifetime;
            float fallSpeed = vol.enabled
                ? -Mathf.Abs(ExtractRateScalar(vol.y))
                : -Mathf.Abs(stageSnow.main.startSpeed.constant);
            calmVelocity.y = fallSpeed;
            leftPeakVelocity.y = fallSpeed;
            rightPeakVelocity.y = fallSpeed;
            Debug.Log($"[Snowstorm2D] Storm Vy synced to {fallSpeed} (matches '{stageSnow.name}').", this);
        }


        // ---- Post Volume ------------------------------------------------------------------

        void ApplyPostVolume()
        {
            if (postVolume == null) return;
            postVolume.weight = slimePresenceWeight * NormalizedIntensity();
        }

        // ---- Audio ------------------------------------------------------------------------

        void ApplyAmbient()
        {
            if (ambientLoopClip == null) return;

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
                // Release the pooled AudioSource during calm, else it stays locked for the
                // lifetime of the zone.
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
            var poly = GetComponent<PolygonCollider2D>();
            if (poly == null) return;
            // Unity's collider gizmo already draws the outline; only add wind arrows.

            float magForGizmo = Application.isPlaying ? currentMagnitude : windAccelerationMagnitude;
            float dirForGizmo = Application.isPlaying ? currentDirection : 1f;
            if (magForGizmo < 0.001f) return;

            Vector3 center = poly.bounds.center;
            // Wind is always world-X, so the arrow ignores any Z-rotation on the zone object.
            Vector3 dir = Vector3.right * dirForGizmo;
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
