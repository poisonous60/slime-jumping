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

        // ---- Runtime (visible in inspector for debugging; do not hand-edit at play time) ----
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
        private SoundHandle ambientHandle;
        private Vector2 windTransitionStart;
        private Vector2 windTransitionTarget;
        private float windTransitionElapsed;

        // Storm PS renderers + shared MPB. Used to fade particle alpha by
        // slimePresenceWeight without touching the prefab's shared material. Emission
        // runs at full rate at all times so a steady-state population already exists
        // when the slime enters; only opacity fades in/out.
        private ParticleSystemRenderer stormRenderer;
        private ParticleSystemRenderer sideStormRenderer;
        private ParticleSystemRenderer ambientSnowRenderer;
        private MaterialPropertyBlock stormMpb;
        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_TintColorId = Shader.PropertyToID("_TintColor");

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
            // Multi-phase skip: if offset exceeds the current phase, advance through full
            // phases so designers can pre-roll the cycle by an arbitrary amount.
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
            // Keep last non-zero direction if wind=0 (calm) so force field doesn't
            // snap its sign mid-frame.
            if (Mathf.Abs(initialWind.x) > 0.001f) currentDirection = Mathf.Sign(initialWind.x);
            else if (currentDirection == 0f) currentDirection = 1f;
            currentMagnitude = initialWind.magnitude;
        }

        void OnEnable()
        {
            SyncForceFieldGeometry();
            CaptureSnowBaselines();
            ConfigureStormForFade();
        }

        void ConfigureStormForFade()
        {
            // Enable prewarm so the PS fills the viewport at scene start. Without this,
            // first-entry into the zone shows an empty top strip until ~lifetime seconds
            // of emission has filled the viewport.
            if (stormParticles != null)
            {
                var m = stormParticles.main;
                m.prewarm = true;
                stormRenderer = stormParticles.GetComponent<ParticleSystemRenderer>();
            }
            if (sideStormParticles != null)
            {
                var m = sideStormParticles.main;
                m.prewarm = true;
                sideStormRenderer = sideStormParticles.GetComponent<ParticleSystemRenderer>();
            }
            if (stormMpb == null) stormMpb = new MaterialPropertyBlock();
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
            // Same reasoning for the presence alpha multiplier: clear it so a storm
            // disabled while the slime is outside doesn't leave the shared Snow.prefab
            // instance stuck transparent.
            if (ambientSnowRenderer != null)
            {
                if (stormMpb == null) stormMpb = new MaterialPropertyBlock();
                ApplyAlphaToRenderer(ambientSnowRenderer, Color.white);
            }
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
            ApplyStormAlpha();
        }

        void ApplyStormAlpha()
        {
            // MPB alpha multiplier so the renderer fades smoothly with slime presence.
            // Setting all three common color property names (URP Particles _BaseColor,
            // built-in Standard _Color, legacy ParticleSystem _TintColor) so the same
            // code works regardless of which shader the prefab uses — MPB silently
            // ignores properties the shader doesn't expose.
            if (stormMpb == null) stormMpb = new MaterialPropertyBlock();
            Color c = new Color(1f, 1f, 1f, slimePresenceWeight);
            ApplyAlphaToRenderer(stormRenderer, c);
            ApplyAlphaToRenderer(sideStormRenderer, c);
            // Ambient Snow.prefab fades with presence too. Its emission is also gated to
            // 0 outside the zone (ApplySnowBoost), but emission alone leaves in-flight
            // particles falling for their full 5-9s lifetime — visible as stray snow well
            // beyond the storm. The alpha fade clears them in lockstep with presence so
            // the ambient snowfall vanishes with the rest of the storm at the boundary.
            // Lazy capture handles late inspector wire-up of snowParticles.
            if (ambientSnowRenderer == null && snowParticles != null)
                ambientSnowRenderer = snowParticles.GetComponent<ParticleSystemRenderer>();
            ApplyAlphaToRenderer(ambientSnowRenderer, c);
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
            // Keep last non-zero direction so the ForceField doesn't snap its sign to 0
            // mid-calm — the snow particles would briefly halt their horizontal drift,
            // looking like a stutter instead of a smooth fall-off into vertical fall.
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

            // Same contract as WindZone2D_v2: AddSustainedFlowImpulse so push integrates
            // into slideVelocity/crawlVelocity in Attached/Charging instead of being
            // overwritten next tick by the state handler.
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

        // Static force field config only (shape/drag/layer). Geometry is NOT derived from
        // the zone collider — the PolygonCollider2D is purely the on/off trigger. The
        // field follows the camera and covers the whole viewport (see
        // ApplyForceFieldToViewport) so ambient Snow.prefab blows sideways across the
        // entire screen while the slime is inside the zone, not only within the collider.
        void SyncForceFieldGeometry()
        {
            if (snowForceField == null) return;
            var ff = snowForceField;
            ff.shape = ParticleSystemForceFieldShape.Box;
            // endRange=0.5 with localScale=size → field half-extents = size/2 (verified in
            // WindZone2D_v2). Without this the field is 2× too large.
            ff.endRange = 0.5f;
            // Snow is pushed SIDEWAYS by directionX/Y (wind) — never radially. Zero the
            // radial gravity/rotation so the field can't suck particles toward its center.
            // Critical now that the field follows the camera: a non-zero gravity would
            // read as a gravity well centered on the slime (snow clumping inward).
            ff.gravity = new ParticleSystem.MinMaxCurve(0f);
            ff.rotationSpeed = new ParticleSystem.MinMaxCurve(0f);
            ff.multiplyDragByParticleVelocity = false;
            ff.multiplyDragByParticleSize = false;
            ff.drag = new ParticleSystem.MinMaxCurve(snowDrag);

            int layer = LayerMask.NameToLayer("WindForceField");
            if (layer >= 0 && ff.gameObject.layer != layer)
                ff.gameObject.layer = layer;
        }

        // Camera-anchored, viewport-sized force field. Driven every LateUpdate from the
        // same cam/halfW/halfH as the storm strips so the ambient-snow sideways drift is
        // full-screen, matching the camera-anchored storm particles. coverageMultiplier
        // margin keeps particles just outside the viewport already drifting before they
        // scroll into view (no seam at the screen edge on camera motion).
        void ApplyForceFieldToViewport(Vector3 cam, float halfW, float halfH)
        {
            if (snowForceField == null) return;
            var ffT = snowForceField.transform;
            ffT.position = new Vector3(cam.x, cam.y, 0f);
            ffT.rotation = Quaternion.identity;
            // Target is a WORLD size (viewport units). position/rotation are world too, so
            // convert to localScale by dividing out the parent's lossyScale — a designer
            // scaling the storm GameObject then no longer shrinks/grows the field.
            // Z=100 → effectively infinite depth so Z-noise stays inside.
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
            // Gate by slime presence so the sideways drift fades in/out with the rest of
            // the storm as the slime crosses the zone boundary: straight-down fall when
            // outside, full horizontal blow when inside.
            wind *= slimePresenceWeight;
            snowForceField.directionX = new ParticleSystem.MinMaxCurve(wind.x);
            snowForceField.directionY = new ParticleSystem.MinMaxCurve(wind.y);
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

            // intensity drives the calm→peak ramp (rate target + size); presence gates
            // the whole emission. Outside the zone presence=0 → ambient snow stops
            // emitting, so it no longer appears as stray snowfall beyond the storm
            // bounds. Inside-but-calm (presence=1, intensity=0) keeps the unmodified
            // baseline rate — the wind stopped, not the snow.
            float intensity = NormalizedIntensity();
            BoostSnow(snowParticles, snowBaselineCaptured, snowBaselineRateScalar, snowBaselineSizeMultiplier, snowPeakRate, snowPeakSizeMultiplier, intensity, slimePresenceWeight);
        }

        static void BoostSnow(ParticleSystem ps, bool captured, float baseRateScalar, float baseSizeMultiplier, float peakRate, float peakSizeMultiplier, float intensity, float presence)
        {
            if (ps == null || !captured) return;

            var emis = ps.emission;
            emis.rateOverTime = new ParticleSystem.MinMaxCurve(Mathf.Lerp(baseRateScalar, peakRate, intensity) * presence);

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
            float referenceMagnitude = ReferenceWindMagnitude();
            return referenceMagnitude > 0.001f
                ? Mathf.Clamp01(currentMagnitude / referenceMagnitude)
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
            ApplyForceFieldToViewport(cam, halfW, halfH);
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

            // Always emit at full rate so a steady-state particle population exists
            // around the camera at all times. Visibility is gated by alpha (MPB on the
            // renderer), so the snowfall is already on screen the instant the slime
            // enters the zone — only its opacity fades in.
            var emis = stormParticles.emission;
            emis.rateOverTime = new ParticleSystem.MinMaxCurve(
                Mathf.Lerp(stormCalmRate, stormPeakRate, intensity));

            // Auto-size lifetime so particles survive the full vertical traversal
            // (spawn-strip → bottom of viewport). Without this the prefab's fixed
            // startLifetime expires mid-screen when vy is low → particles fade out
            // visibly near the center.
            Vector2 vel = GetStormVelocity(intensity);
            float topTravel = halfH * 2f + stripOffset * 2f + stripThickness;
            var topMain = stormParticles.main;
            topMain.startLifetime = new ParticleSystem.MinMaxCurve(
                ComputeLifetime(topTravel, Mathf.Abs(vel.y)));

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
            // would just pile at the side edge and look wrong. No slime-presence gating
            // here either; alpha fade handles entry/exit (see ApplyStormAlpha).
            var emis = sideStormParticles.emission;
            emis.rateOverTime = new ParticleSystem.MinMaxCurve(
                sideStormPeakRate * intensity);

            // Lifetime sized for horizontal traversal (spawn edge → opposite edge).
            Vector2 vel = GetStormVelocity(intensity);
            float sideTravel = halfW * 2f + stripOffset * 2f + stripThickness;
            var sideMain = sideStormParticles.main;
            sideMain.startLifetime = new ParticleSystem.MinMaxCurve(
                ComputeLifetime(sideTravel, Mathf.Abs(vel.x)));

            ApplyStormVelocity(sideStormParticles, intensity);
        }

        Vector2 GetStormVelocity(float intensity)
        {
            Vector2 peak = currentDirection >= 0f ? rightPeakVelocity : leftPeakVelocity;
            return Vector2.Lerp(calmVelocity, peak, intensity);
        }

        // Returns travel/speed * safety margin, capped so a near-zero speed doesn't
        // produce a multi-minute lifetime that bloats the particle pool.
        const float kLifetimeSafetyMargin = 1.5f;
        const float kLifetimeMaxCap = 30f;
        static float ComputeLifetime(float travel, float speed)
        {
            if (speed < 0.01f) return kLifetimeMaxCap;
            return Mathf.Min(travel / speed * kLifetimeSafetyMargin, kLifetimeMaxCap);
        }

        void ApplyStormVelocity(ParticleSystem ps, float intensity)
        {
            // Make velocityOverLifetime the sole velocity source. Without this, the
            // prefab's gravityModifier + startSpeed silently add downward velocity on
            // top of our Vy — a positive (upward) Vy in the inspector then still
            // produces a falling particle because gravity dominates.
            var main = ps.main;
            main.gravityModifier = new ParticleSystem.MinMaxCurve(0f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0f);

            // Local-space velocity. PS transform tracks camera, so local +X = world +X
            // (no rotation), but particles move with PS when camera moves. Lerp from
            // calmVelocity → directional peak by intensity; currentDirection picks the
            // left vs right peak. Y carries the fall speed (negative = down); jitter
            // spreads per-particle vy so trajectories aren't visibly parallel.
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

        // Designer convenience: right-click the component header → "Sync Storm Vy To
        // Outer Snow" to overwrite all three Y components (calm/left/right) with the
        // outer snowParticles' startSpeed, so the storm-owned snow falls at the same
        // visual rate as the ambient Snow.prefab.
        [ContextMenu("Sync Storm Vy To Outer Snow")]
        void SyncStormVyToOuterSnow()
        {
            if (snowParticles == null)
            {
                Debug.LogWarning("[Snowstorm2D] snowParticles not assigned — cannot sync Vy.", this);
                return;
            }
            // startSpeed is the spawn-time speed magnitude; snow falls downward, so
            // negate to express it in our "Vy negative = down" convention.
            float fallSpeed = -Mathf.Abs(snowParticles.main.startSpeed.constant);
            calmVelocity.y = fallSpeed;
            leftPeakVelocity.y = fallSpeed;
            rightPeakVelocity.y = fallSpeed;
            Debug.Log($"[Snowstorm2D] Storm Vy synced to {fallSpeed} (matches '{snowParticles.name}' startSpeed).", this);
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
            var poly = GetComponent<PolygonCollider2D>();
            if (poly == null) return;
            // Polygon outline is drawn by Unity's own collider gizmo; we only add the
            // wind-direction arrows, centered on the polygon's bounds.

            float magForGizmo = Application.isPlaying ? currentMagnitude : windAccelerationMagnitude;
            float dirForGizmo = Application.isPlaying ? currentDirection : 1f;
            if (magForGizmo < 0.001f) return;

            Vector3 center = poly.bounds.center;
            // Wind is always world-X (PhaseWindAcceleration uses ±X), so the arrow must
            // point along world-X regardless of any Z-rotation on the zone object.
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
