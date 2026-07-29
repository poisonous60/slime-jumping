using UnityEngine;
using SlimeJumping.Rendering;

namespace SlimeJumping.Visual
{
    /// <summary>ColdZone refcount drives freezeT (0→1 inside, 1→0 outside) → frostCurve → _FrostStrength on the body.</summary>
    // Body color stays unchanged: a "frozen" palette via MPB override lost Quibli's cell-shading.
    // Order 110 keeps this after SlimeBurnVisual (100) — both round-trip the same MPB, disjoint props.
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

        private int coldZoneRefCount;
        private float freezeT;
        private MeshRenderer bodyMR;
        private MaterialPropertyBlock mpb;

        private static readonly int FrostStrengthID = Shader.PropertyToID("_FrostStrength");

        public float FreezeT => freezeT;
        public bool IsInsideColdZone => coldZoneRefCount > 0;

        public void PushColdZone() => coldZoneRefCount++;
        public void PopColdZone() => coldZoneRefCount = Mathf.Max(0, coldZoneRefCount - 1);

        /// <summary>One-shot freezeT bump independent of the ColdZone refcount; the normal ramp/thaw resumes from the new value.</summary>
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
            // SceneView only repaints on activity — pump a tick or the Frost Strength slider
            // won't track until the user wiggles the camera.
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
            // Edit mode: the material slider drives _FrostStrength. Play mode: refcount drives.
            if (!Application.isPlaying) return;

            float dt = Time.deltaTime;
            float rate = IsInsideColdZone
                ? (1f / Mathf.Max(0.01f, freezeDuration))
                : -(1f / Mathf.Max(0.01f, thawDuration));
            freezeT = Mathf.Clamp01(freezeT + rate * dt);

            // Publish for the warm/cold trail RT split (single-slime stage assumption).
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

            // Edit mode: don't touch _FrostStrength so the inspector slider preview stays authoritative.
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
            // Clear the override, else a disabled controller leaves the body stuck frosted.
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

            // Repaint so a material slider drag shows immediately.
            if (UnityEditor.SceneView.lastActiveSceneView != null)
                UnityEditor.SceneView.lastActiveSceneView.Repaint();
        }
#endif
    }
}
