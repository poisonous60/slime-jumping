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

        // Storm PS renderer + shared MPB. Used to fade particle alpha by
        // slimePresenceWeight without touching the prefab's shared material. Emission
        // runs at full rate at all times so a steady-state population already exists
        // when the slime enters; only opacity fades in/out.
        private ParticleSystemRenderer stormRenderer;
        private MaterialPropertyBlock stormMpb;
        private static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int s_ColorId = Shader.PropertyToID("_Color");
        private static readonly int s_TintColorId = Shader.PropertyToID("_TintColor");

        // Stage-snow swap baselines. Each suppressed PS's emission rate is captured so the
        // inverse-presence gate scales from the real baseline; the full MinMaxCurve is
        // retained for mode-safe restore on disable, mirroring the snow-boost capture.
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
            CaptureSuppressedSnowBaselines();
            ConfigureStormForFade();
        }

        void ConfigureStormForFade()
        {
            // Enable prewarm so the PS fills the viewport at scene start. Without this,
            // first-entry into the zone shows an empty viewport until ~lifetime seconds
            // of emission has filled it.
            if (stormParticles != null)
            {
                var m = stormParticles.main;
                m.prewarm = true;
                // Pool headroom for the worst case: peak rate × upstream-margin area
                // scale (~3×) × lifetime. Hitting maxParticles silently throttles
                // emission, which would reintroduce the sparse-spawn look.
                m.maxParticles = Mathf.Max(m.maxParticles, 20000);
                stormRenderer = stormParticles.GetComponent<ParticleSystemRenderer>();

                // Per-particle alpha fade-in/out over lifetime — hides the pop of
                // particles spawning/dying mid-screen, which the full-viewport volume
                // spawn makes possible (the old top-strip spawned off-screen only).
                // Same key layout as Snow.prefab's colorOverLifetime.
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
            StopAmbient();
            if (postVolume != null) postVolume.weight = 0f;
            // Restore the stage snow PS so a disabled storm doesn't leave the existing
            // always-on snowfall stuck suppressed (emission + alpha) — it must resume full
            // snowfall everywhere once this zone is gone.
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

            // Stage snow swap: fade the existing always-on snowfall OUT as presence rises —
            // inverse of the storm fade-in — so in-flight flakes clear in lockstep with the
            // emission gate (ApplySuppressedSnow). Lazy-capture guards late inspector wire-up.
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

        // Pick a representative scalar for lerping from the captured rate. Constant /
        // TwoConstants modes give a clean answer; for Curve / TwoCurves we approximate
        // with curveMultiplier (the upper-bound scale) so the gate still lerps to a
        // sensible value. The restore path always writes back the original MinMaxCurve
        // so the mode/curve shape is preserved verbatim — the scalar approximation only
        // matters during the lerp.
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
        // The existing stage-wide ambient snow (a SEPARATE always-on Snow.prefab) should
        // hand the screen over to the storm while the slime is inside the zone, instead of
        // both snowfalls stacking. Driven by the SAME slimePresenceWeight that fades the
        // storm IN, but inverted, so the two cross-fade: full stage snow outside, off
        // inside. Emission gate (here) + renderer alpha fade (ApplyStormAlpha) so in-flight
        // flakes clear too, matching how the ambient/storm particles fade.

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
            // Lazy capture handles inspector wire-up after OnEnable, same as ApplySnowBoost.
            CaptureSuppressedSnowBaselines();
            if (!suppressedBaselinesCaptured || suppressedSnowParticles == null) return;

            float keep = 1f - slimePresenceWeight; // outside zone: 1 → stage snow on; inside: 0 → off
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
        // Camera-anchored Local-space PS. The transform follows the camera every
        // LateUpdate; particles simulate in PS-local coordinates, so they are visually
        // glued to the viewport regardless of how fast the slime/camera moves — solves
        // the "snow gets left behind on fast jump" problem of World-space sim.
        //
        // Full-viewport volume spawn (replaced the old top+side strip pair): emission
        // rate changes (calm→wind ramps, zone-entry phase reset) appear uniformly
        // across the whole screen at once. With strip spawning, a rate change only
        // injected at the strip, so the new density arrived as a visible band falling
        // in from the top of the screen ("snow spawning all at once"). Volume spawn
        // also covers the viewport at every wind angle, so no side strip is needed.
        // Mid-screen spawn/death pops are hidden by the colorOverLifetime fade
        // (ConfigureStormForFade), the same technique Snow.prefab uses.
        //
        // Wind drift = horizontal local-space velocity, scaled by storm intensity.
        // During calm, emission holds at stormCalmRate — the wind stops, the snow
        // does not.

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

            // Upstream spawn margin. A uniform volume spawn reaches its saturated
            // steady-state density only one lifetime of travel (|v|×L) downstream of
            // the box edge — at the edge itself density is zero and ramps up linearly.
            // Extending the box by exactly |v|×L on the side particles come FROM
            // (above for fall, upwind for drift) puts that ramp entirely off-screen,
            // so the visible viewport sits in the saturated zone. Same idea as the
            // stage snow's heightMultiplier spawn-box overshoot, sized exactly.
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
            // Center shifted upstream (against velocity) by half the margin so the
            // base box stays centered on the camera and only the margin side grows.
            shape.position = new Vector3(-vel.x * life * 0.5f, -vel.y * life * 0.5f, 0f);
            shape.scale = new Vector3(sizeX, sizeY, 0f);

            // Always emit at full rate so a steady-state particle population exists
            // around the camera at all times. Visibility is gated by alpha (MPB on the
            // renderer), so the snowfall is already on screen the instant the slime
            // enters the zone — only its opacity fades in.
            // Rate is scaled by the box growth so the configured calm/peak rates keep
            // meaning "on-screen density" no matter how far the box extends upstream
            // (otherwise peak wind would enlarge the box and visibly THIN the snow).
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
        // Stage Snow" to overwrite all three Y components (calm/left/right) with the
        // stage-wide snow's fall speed, so the boundary cross-fade reads as the same
        // snowfall intensifying rather than a different effect swapping in. Snow.prefab
        // falls via velocityOverLifetime (startSpeed=0), so read that module first.
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
