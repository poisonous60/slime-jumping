using UnityEngine;
using UnityEngine.Serialization;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Drives a SlimeJumping/SpaceWarp_Distortion material on a target Renderer.
    /// Two interaction modes:
    ///   • <see cref="Trigger"/>          — one-shot stationary shockwave (a centered ripple).
    ///   • <see cref="TriggerStretch"/>   — membrane that follows a moving target, stretching
    ///                                       between an anchor and the target, then pops at
    ///                                       maxStretchDuration / maxStretchLength.
    /// Both modes drive the shader via MaterialPropertyBlock; stretch mode also writes the
    /// quad's world transform each frame to span anchor → target.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Mechanisms/Space Warp Effect")]
    public class SpaceWarpEffect : MonoBehaviour
    {
        enum WarpState { Idle, PulseOnly, Stretching, Popping }

        [Header("Target")]
        [Tooltip("Renderer using SlimeJumping/SpaceWarp_Distortion. Defaults to Renderer on this GameObject.")]
        [SerializeField] private Renderer targetRenderer;

        [Header("Pulse (Trigger)")]
        [Tooltip("Seconds for one stationary shockwave to expand from center to edge.")]
        [Min(0.05f)] [SerializeField] private float pulseDuration = 0.45f;
        [Tooltip("Peak amplitude of the shockwave UV offset.")]
        [Range(0f, 0.4f)] [SerializeField] private float pulseStrength = 0.18f;

        [Header("Stretch (TriggerStretch)")]
        [Tooltip("World-space anchor for the membrane's u=0 (base) end. Defaults to this transform's parent.")]
        [SerializeField] private Transform anchor;
        [Tooltip("Hard cap on stretch length. Once the target moves further, the membrane pops.")]
        [Min(0.1f)] [SerializeField] private float maxStretchLength = 8f;
        [Tooltip("Hard cap on stretch duration. Pop fires after this regardless of distance.")]
        [Min(0.05f)] [SerializeField] private float maxStretchDuration = 0.4f;
        [Tooltip("Membrane width at the BASE (anchor) end before any stretching.")]
        [Min(0.1f)] [SerializeField] private float baseWidth = 1.5f;
        [Tooltip("How much the base flares as the target moves away. " +
                 "1 = constant base width. 2 = base doubles by max stretch. Higher = stretchier membrane.")]
        [Range(0.5f, 6f)] [SerializeField] private float flareScale = 2.5f;
        [Tooltip("Easing for the base flare (x = stretch progress 0..1, y = lerp factor between baseWidth and baseWidth*flareScale).")]
        [SerializeField] private AnimationCurve flareCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("World-space width at the TIP (slime) end. Stays constant — the shader auto-tapers " +
                 "down to this width regardless of how flared the base gets. Should be < baseWidth.")]
        [Min(0.05f)] [SerializeField] private float tipWidth = 0.4f;
        [Tooltip("World-space distance the membrane tip extends BEYOND the slime along the launch axis. " +
                 "0 = tip sits exactly at the slime. Higher values place the tip ahead of the slime so the " +
                 "slime stays inside the bulb instead of poking out of it.")]
        [FormerlySerializedAs("endPadding")]
        [Min(0f)] [SerializeField] private float tipLead = 0.8f;

        [Header("V2 Horn Shape — Widths (world units)")]
        [Tooltip("BulbShatter / Horn mode. Bottom (anchor flat back) half-width in WORLD units at ZERO stretch — sets how thin the back is right after launch.")]
        [Min(0.0f)] [SerializeField] private float anchorHalfWidthMin = 0.13f;
        [Tooltip("BulbShatter / Horn mode. Bottom (anchor flat back) half-width in WORLD units at FULL stretch — the widest the back ever gets. Driver lerps Min→Max across stretch progress and writes _NeckWidth as a v-fraction every frame, overriding the material value. Set Min == Max to disable the bottom widening.")]
        [Min(0.0f)] [SerializeField] private float anchorHalfWidthMax = 0.45f;
        [Tooltip("BulbShatter / Horn mode. TOP (bulb cap) radius in WORLD units. Stays constant — the slime is the same size throughout the launch, so its enclosing bulb shouldn't grow with stretch. Driver writes _BulbRadius as a v-fraction every frame, overriding the material value.")]
        [Min(0.0f)] [SerializeField] private float bulbRadiusWorld = 0.65f;

        [Header("Pop")]
        [Tooltip("Seconds for the membrane burst (alpha fade + ripple sweep).")]
        [Min(0.05f)] [SerializeField] private float popDuration = 0.22f;

        [Header("Lifecycle")]
        [Tooltip("Auto-disable the renderer between events. Disable when this object IS a persistent zone.")]
        [SerializeField] private bool hideWhenIdle = true;

        static readonly int IdPulsePhase    = Shader.PropertyToID("_PulsePhase");
        static readonly int IdPulseStrength = Shader.PropertyToID("_PulseStrength");
        static readonly int IdPopPhase      = Shader.PropertyToID("_PopPhase");
        static readonly int IdTaper         = Shader.PropertyToID("_Taper");
        // V2 (BulbShatter) shader uses _BulbCenter as the slime UV along the
        // long axis. Driver writes this every stretch frame so the bulb stays
        // centered on the slime regardless of stretch length. Harmless on the
        // legacy SpaceWarp_Distortion shader (property is just ignored).
        static readonly int IdBulbCenter    = Shader.PropertyToID("_BulbCenter");
        // V2 Horn shape — _NeckWidth and _BulbRadius are rewritten live each
        // frame (v-fractions converted from world-unit inspector fields). The
        // material values are completely overridden during stretching/popping.
        // _QuadAspect = quadLen / (quadHeight*0.5) lets the bulb cap stay a
        // true world circle instead of a squished ellipse.
        static readonly int IdNeckWidth     = Shader.PropertyToID("_NeckWidth");
        static readonly int IdBulbRadius    = Shader.PropertyToID("_BulbRadius");
        static readonly int IdQuadAspect    = Shader.PropertyToID("_QuadAspect");

        MaterialPropertyBlock mpb;
        WarpState state = WarpState.Idle;
        float stateT;
        bool autoPopEnabled = true;     // false = caller owns pop timing via Pop()
        Transform stretchTarget;
        Vector3 anchorWorld;
        float currentTaper;
        float currentBulbCenter = 0.85f;
        float currentNeckWidth   = 0.45f;
        float currentBulbRadius  = 0.65f;
        float currentQuadAspect  = 1f;
        Vector3 idleLocalPos;
        Quaternion idleLocalRot;
        Vector3 idleLocalScale;
        bool idleCached;

        void Reset()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        }

        void OnEnable()
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            CacheIdleTransform();
            if (anchor == null) anchor = transform.parent != null ? transform.parent : transform;
            ApplyMpb(0f, 0f, 0f);
            if (hideWhenIdle && targetRenderer != null) targetRenderer.enabled = false;
        }

        void OnDisable()
        {
            state = WarpState.Idle;
        }

        // -------- public API --------

        public float PopDuration => popDuration;

        public void Trigger()
        {
            if (targetRenderer == null) return;
            state = WarpState.PulseOnly;
            stateT = 0f;
            currentTaper = 0f; // radial pulse → no taper
            if (hideWhenIdle) targetRenderer.enabled = true;
        }

        public void TriggerStretch(Transform target)
        {
            TriggerStretchInternal(target, autoPop: true);
        }

        /// <summary>
        /// Like <see cref="TriggerStretch"/> but the stretch never auto-pops on
        /// length / duration limits — the caller owns the shatter timing via
        /// <see cref="Pop"/>. Used when an external system (e.g. a slime
        /// pass-through drive) should align the shatter with its own end-of-window
        /// rather than the warp's internal max.
        /// </summary>
        public void TriggerStretchHeld(Transform target)
        {
            TriggerStretchInternal(target, autoPop: false);
        }

        /// <summary>
        /// Force-transition into the shatter (Popping) phase. No-op outside of
        /// PulseOnly / Stretching. Lets external drivers fire the burst on a
        /// schedule decoupled from <see cref="maxStretchLength"/> /
        /// <see cref="maxStretchDuration"/>.
        /// </summary>
        public void Pop()
        {
            if (state != WarpState.Stretching && state != WarpState.PulseOnly) return;
            state = WarpState.Popping;
            stateT = 0f;
        }

        private void TriggerStretchInternal(Transform target, bool autoPop)
        {
            if (target == null) { Trigger(); return; }
            if (targetRenderer == null) return;

            CacheIdleTransform();
            stretchTarget = target;
            anchorWorld = anchor != null ? anchor.position : transform.position;
            autoPopEnabled = autoPop;
            state = WarpState.Stretching;
            stateT = 0f;

            if (hideWhenIdle) targetRenderer.enabled = true;
            UpdateStretchTransform();
            ApplyMpb(0f, 0f, 0f);
        }

        // -------- update loop --------

        void Update()
        {
            switch (state)
            {
                case WarpState.PulseOnly:
                {
                    stateT += Time.deltaTime;
                    float phase = Mathf.Clamp01(stateT / pulseDuration);
                    ApplyMpb(0f, phase, pulseStrength);
                    if (phase >= 1f) EnterIdle();
                    break;
                }
                case WarpState.Stretching:
                {
                    stateT += Time.deltaTime;
                    UpdateStretchTransform();
                    // Push the live shape values (BulbCenter / NeckWidth /
                    // QuadAspect / Taper) to the GPU every frame. Without this,
                    // the v2 Horn shape would keep stale values from the
                    // TriggerStretch entry and the bulb wouldn't track the slime.
                    ApplyMpb(0f, 0f, 0f);
                    Vector3 axis = (stretchTarget != null ? stretchTarget.position : anchorWorld) - anchorWorld;
                    float len = axis.magnitude;
                    bool autoPopHit = autoPopEnabled && (stateT >= maxStretchDuration || len >= maxStretchLength);
                    if (stretchTarget == null || autoPopHit)
                    {
                        state = WarpState.Popping;
                        stateT = 0f;
                    }
                    break;
                }
                case WarpState.Popping:
                {
                    stateT += Time.deltaTime;
                    float p = Mathf.Clamp01(stateT / popDuration);
                    // Pulse rides the same curve so the burst ring sweeps as alpha fades.
                    ApplyMpb(p, p, pulseStrength);
                    if (p >= 1f) EnterIdle();
                    break;
                }
            }
        }

        void EnterIdle()
        {
            state = WarpState.Idle;
            stretchTarget = null;
            currentTaper = 0f;
            ApplyMpb(0f, 0f, 0f);
            RestoreIdleTransform();
            if (hideWhenIdle && targetRenderer != null) targetRenderer.enabled = false;
        }

        // -------- stretch transform --------

        void UpdateStretchTransform()
        {
            if (stretchTarget == null) return;
            Vector3 to = stretchTarget.position;
            // Keep z aligned with the cached idle plane so the stretch stays flat in 2D.
            to.z = anchorWorld.z;

            Vector3 axis = to - anchorWorld;
            float len = Mathf.Min(axis.magnitude, maxStretchLength);
            Vector3 dir = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.right;

            // Quad spans [anchor → slime + tipLead·dir]. u=0 sits exactly on the anchor (pad);
            // u=1 sits ahead of the slime by tipLead so the slime is enclosed by the bulb
            // instead of poking out the front. Quad center is the midpoint of that span.
            float totalLen = Mathf.Max(len + tipLead, 0.5f);
            Vector3 quadCenter = anchorWorld + dir * (totalLen * 0.5f);

            transform.position = quadCenter;
            transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

            // Base width grows with stretch progress (curve-eased). Object Y scale = base width
            // because the shader's u=0 row matches scale.y at full extent. The tip stays at
            // tipWidth via auto-computed _Taper below.
            float stretchT = Mathf.Clamp01(len / maxStretchLength);
            float flareT   = Mathf.Clamp01(flareCurve.Evaluate(stretchT));
            float padWidthLegacy = Mathf.Lerp(baseWidth, baseWidth * flareScale, flareT);

            // V2 Horn shape — quad must be tall enough to contain the inspector-
            // specified anchor and bulb world widths. Without this floor the
            // _NeckWidth v-fraction silently clamps to 1.0 once the anchor world
            // half-width exceeds the legacy padWidth/2, so cranking "Anchor Half
            // Width Max" past that threshold produced no further widening.
            float anchorHwW   = Mathf.Lerp(anchorHalfWidthMin, anchorHalfWidthMax, stretchT);
            float padWidthHorn = 2f * Mathf.Max(anchorHwW, bulbRadiusWorld);
            float padWidth     = Mathf.Max(padWidthLegacy, padWidthHorn);
            transform.localScale = new Vector3(totalLen, padWidth, idleLocalScale.z);

            // _Taper such that tip-end world width = tipWidth.
            // Object-space width at u=0 is scale.y * 1; at u=1 it's scale.y * (1 - taper).
            // → taper = 1 - tipWidth / padWidth (clamped so the tip can't be wider than the base).
            currentTaper = padWidth > 1e-4f
                ? Mathf.Clamp01(1f - tipWidth / padWidth)
                : 0f;

            // _BulbCenter (v2 BulbShatter shader) — slime UV along the long axis.
            // Quad spans [anchor → slime + tipLead]; slime sits at (len / totalLen).
            currentBulbCenter = totalLen > 1e-4f ? Mathf.Clamp(len / totalLen, 0.5f, 0.98f) : 0.85f;
            // _NeckWidth — anchor flat-back half-width as a v-fraction. Quad has
            // already been auto-grown above to contain anchorHwW, so the divide
            // here yields ≤ 1.0 even at extreme inspector values.
            float halfQuad    = padWidth * 0.5f;
            currentNeckWidth  = halfQuad > 1e-4f ? Mathf.Clamp01(anchorHwW / halfQuad) : 0.0f;
            // _BulbRadius — top (cap) world radius converted to v-fraction. Stays
            // constant across stretch (bulb sized to slime, not to stretch length).
            currentBulbRadius = halfQuad > 1e-4f ? Mathf.Clamp01(bulbRadiusWorld / halfQuad) : 0.0f;
            // _QuadAspect — long-axis world span / short-axis world span. Used by
            // the bulb cap math to keep the hemisphere a true world circle when
            // the quad is much longer than tall.
            currentQuadAspect = halfQuad > 1e-4f ? totalLen / halfQuad : 1f;
        }

        void CacheIdleTransform()
        {
            if (idleCached) return;
            idleLocalPos   = transform.localPosition;
            idleLocalRot   = transform.localRotation;
            idleLocalScale = transform.localScale;
            idleCached = true;
        }

        void RestoreIdleTransform()
        {
            if (!idleCached) return;
            transform.localPosition = idleLocalPos;
            transform.localRotation = idleLocalRot;
            transform.localScale    = idleLocalScale;
        }

        // -------- MPB --------

        void ApplyMpb(float popPhase, float pulsePhase, float pulseAmp)
        {
            if (targetRenderer == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(IdPopPhase,      popPhase);
            mpb.SetFloat(IdPulsePhase,    pulsePhase);
            mpb.SetFloat(IdPulseStrength, pulseAmp);
            mpb.SetFloat(IdTaper,         currentTaper);
            mpb.SetFloat(IdBulbCenter,    currentBulbCenter);
            mpb.SetFloat(IdNeckWidth,     currentNeckWidth);
            mpb.SetFloat(IdBulbRadius,    currentBulbRadius);
            mpb.SetFloat(IdQuadAspect,    currentQuadAspect);
            targetRenderer.SetPropertyBlock(mpb);
        }
    }
}
