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

        MaterialPropertyBlock mpb;
        WarpState state = WarpState.Idle;
        float stateT;
        Transform stretchTarget;
        Vector3 anchorWorld;
        float currentTaper;
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
            if (target == null) { Trigger(); return; }
            if (targetRenderer == null) return;

            CacheIdleTransform();
            stretchTarget = target;
            anchorWorld = anchor != null ? anchor.position : transform.position;
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
                    Vector3 axis = (stretchTarget != null ? stretchTarget.position : anchorWorld) - anchorWorld;
                    float len = axis.magnitude;
                    if (stretchTarget == null || stateT >= maxStretchDuration || len >= maxStretchLength)
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
            float padWidth = Mathf.Lerp(baseWidth, baseWidth * flareScale, flareT);
            transform.localScale = new Vector3(totalLen, padWidth, idleLocalScale.z);

            // _Taper such that tip-end world width = tipWidth.
            // Object-space width at u=0 is scale.y * 1; at u=1 it's scale.y * (1 - taper).
            // → taper = 1 - tipWidth / padWidth (clamped so the tip can't be wider than the base).
            currentTaper = padWidth > 1e-4f
                ? Mathf.Clamp01(1f - tipWidth / padWidth)
                : 0f;
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
            targetRenderer.SetPropertyBlock(mpb);
        }
    }
}
