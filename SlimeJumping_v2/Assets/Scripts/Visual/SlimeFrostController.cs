using UnityEngine;
using SlimeJumping.Rendering;

namespace SlimeJumping.Visual
{
    /// <summary>
    /// Drives the slime body's surface frost crystals while the slime is in
    /// any number of <see cref="SlimeJumping.Mechanisms.ColdZone"/> triggers
    /// (refcounted). freezeT advances 0→1 inside, 1→0 outside, with
    /// asymmetric durations so a brief exit doesn't reset progress.
    /// frostCurve maps freezeT → _FrostStrength so authoring can shape the
    /// onset (e.g. crystals don't show until freezeT crosses 0.3).
    ///
    /// Body color does NOT change. An earlier design swapped in a "frozen"
    /// palette but Quibli's lit colors couldn't be cleanly mirrored via MPB
    /// overrides — the result lost cell-shading detail. The frozen look is
    /// just the same slime with frost crystals on top.
    ///
    /// Also publishes freezeT to <see cref="WorldTrailManager.SetFreezeT"/>
    /// for the dual-color trail routing (warm/cold trail RTs split-write).
    ///
    /// [ExecuteAlways] so the material's Frost Strength preview slider
    /// reflects in the scene view at edit time too. Execution order 110 keeps
    /// this strictly after <see cref="SlimeBurnVisual"/> (100) — both write
    /// disjoint properties to the same MPB and the GetPropertyBlock round-
    /// trip preserves whichever ran first.
    /// </summary>
    [DefaultExecutionOrder(110)]
    [ExecuteAlways]
    public class SlimeFrostController : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private SlimeVisual slimeVisual;
        [Tooltip("Body3D root. Auto-resolved from slimeVisual.Body3d in Awake when null.")]
        [SerializeField] private Transform body3d;

        [Header("Timing (seconds)")]
        [Tooltip("Seconds inside any ColdZone to ramp freezeT 0 → 1.")]
        [Min(0.1f)] [SerializeField] private float freezeDuration = 8f;
        [Tooltip("Seconds outside every ColdZone to ramp freezeT 1 → 0. Asymmetric so a brief exit doesn't reset progress.")]
        [Min(0.1f)] [SerializeField] private float thawDuration = 15f;

        [Header("Curve")]
        [Tooltip("freezeT → _FrostStrength. Default leaves crystals invisible until freezeT≈0.3.")]
        [SerializeField] private AnimationCurve frostCurve = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.3f, 0f), new Keyframe(1f, 1f));

        // --- runtime ---
        private int coldZoneRefCount;
        private float freezeT;
        private MeshRenderer bodyMR;
        private MaterialPropertyBlock mpb;

        private static readonly int FrostStrengthID = Shader.PropertyToID("_FrostStrength");

        public float FreezeT => freezeT;
        public bool IsInsideColdZone => coldZoneRefCount > 0;

        public void PushColdZone() => coldZoneRefCount++;
        public void PopColdZone() => coldZoneRefCount = Mathf.Max(0, coldZoneRefCount - 1);

        /// <summary>
        /// One-shot bump to freezeT, independent of ColdZone refcount. Used by
        /// hazards (e.g. Snowball side impact) to spike progress without
        /// leaving the slime "inside a zone" — the natural ramp/thaw resumes
        /// from the new value on the next Update.
        /// </summary>
        public void AddFreezeT(float amount) => freezeT = Mathf.Clamp01(freezeT + amount);

        private void Awake()
        {
            ResolveWiring();
            if (mpb == null) mpb = new MaterialPropertyBlock();
        }

        private void Reset()
        {
            if (slimeVisual == null) slimeVisual = GetComponentInParent<SlimeVisual>();
            frostCurve = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.3f, 0f), new Keyframe(1f, 1f));
        }

        private void OnEnable()
        {
#if UNITY_EDITOR
            // SceneView only repaints on activity; pump our own tick so the
            // material's Frost Strength slider tracks live without the user
            // having to wiggle the camera to force a SceneView redraw.
            UnityEditor.EditorApplication.update -= EditorPreviewTick;
            UnityEditor.EditorApplication.update += EditorPreviewTick;
#endif
        }

        private void ResolveWiring()
        {
            if (slimeVisual == null)
            {
                slimeVisual = GetComponentInParent<SlimeVisual>();
                if (slimeVisual == null && transform.root != null)
                {
                    slimeVisual = transform.root.GetComponentInChildren<SlimeVisual>();
                }
            }
            if (body3d == null && slimeVisual != null && slimeVisual.Body3d != null)
                body3d = slimeVisual.Body3d;
            if (body3d != null)
            {
                var mf = body3d.GetComponentInChildren<MeshFilter>();
                if (mf != null) bodyMR = mf.GetComponent<MeshRenderer>();
            }
        }

        private void Update()
        {
            // Edit mode: freezeT stays at 0; the material slider drives the
            // shader's _FrostStrength directly. Play mode: refcount drives.
            if (!Application.isPlaying) return;

            float dt = Time.deltaTime;
            float rate = IsInsideColdZone
                ? (1f / Mathf.Max(0.01f, freezeDuration))
                : -(1f / Mathf.Max(0.01f, thawDuration));
            freezeT = Mathf.Clamp01(freezeT + rate * dt);

            // Publish to manager so World/Local trail stamps split between
            // warm and cold RTs (single-slime stage assumption).
            var mgr = WorldTrailManager.Instance;
            if (mgr != null) mgr.SetFreezeT(freezeT);
        }

        private void LateUpdate()
        {
            if (bodyMR == null)
            {
                ResolveWiring();
                if (bodyMR == null) return;
            }
            if (mpb == null) mpb = new MaterialPropertyBlock();

            // Edit mode: leave _FrostStrength alone so the material slider in
            // the inspector preview is the source of truth. Play mode: push
            // frostCurve(freezeT) so the slider is overridden by gameplay.
            if (!Application.isPlaying) return;

            bodyMR.GetPropertyBlock(mpb);
            mpb.SetFloat(FrostStrengthID, Mathf.Clamp01(frostCurve.Evaluate(freezeT)));
            bodyMR.SetPropertyBlock(mpb);
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.update -= EditorPreviewTick;
#endif
            // Clear MPB override so a disabled controller doesn't leave the
            // body locked in a frosted state.
            if (bodyMR == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            bodyMR.GetPropertyBlock(mpb);
            mpb.SetFloat(FrostStrengthID, 0f);
            bodyMR.SetPropertyBlock(mpb);
        }

#if UNITY_EDITOR
        private void EditorPreviewTick()
        {
            if (Application.isPlaying) return;
            if (this == null || gameObject == null) return;
            if (!enabled || !gameObject.activeInHierarchy) return;

            // Mostly a no-op in edit mode (LateUpdate early-returns when not
            // playing) — still useful to repaint the scene view so the
            // material slider drag is visible immediately.
            if (UnityEditor.SceneView.lastActiveSceneView != null)
                UnityEditor.SceneView.lastActiveSceneView.Repaint();
        }
#endif
    }
}
