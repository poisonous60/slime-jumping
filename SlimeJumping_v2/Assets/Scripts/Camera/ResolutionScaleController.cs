using UnityEngine;
using Unity.Cinemachine;

namespace SlimeJumping.Camera
{
    /// <summary>Keeps the framed world area constant: the rect defined by referenceFov at
    /// targetAspect stays fully visible at any resolution, expanding outward on other aspects.</summary>
    [DefaultExecutionOrder(-50)]
    public class ResolutionScaleController : MonoBehaviour
    {
        [Tooltip("Vertical FOV designed for targetAspect. Resolution-independent.")]
        [SerializeField] private float referenceFov = 40f;

        [Tooltip("Aspect the framing was authored at. 16:9 = 1.7778.")]
        [SerializeField] private float targetAspect = 16f / 9f;

        public float ReferenceFov => referenceFov;
        public float TargetAspect => targetAspect;

        /// <summary>Vertical FOV that keeps the authored rect fully in frame at the given screen aspect.</summary>
        public static float FovForAspect(float referenceFov, float targetAspect, float screenAspect)
        {
            float refTanHalf = Mathf.Tan(referenceFov * 0.5f * Mathf.Deg2Rad);
            // Narrower than target: widen vertically to keep the authored width in frame.
            float widthFitFov = 2f * Mathf.Atan(refTanHalf * targetAspect / Mathf.Max(0.01f, screenAspect)) * Mathf.Rad2Deg;
            return Mathf.Max(referenceFov, widthFitFov);
        }

        [Tooltip("Cinemachine cameras whose Lens.FieldOfView drives a live camera via Brain.")]
        [SerializeField] private CinemachineCamera[] vcams;

        [Tooltip("Plain cameras (not Cinemachine-driven) to keep in sync, e.g. BackgroundCamera.")]
        [SerializeField] private UnityEngine.Camera[] extraCameras;

        private int lastW, lastH;

        private void Start() => Apply();

        private void Update()
        {
            if (Screen.width == lastW && Screen.height == lastH) return;
            Apply();
        }

        private void Apply()
        {
            lastW = Screen.width;
            lastH = Screen.height;

            float aspect = Screen.width / Mathf.Max(1f, Screen.height);
            float newFov = FovForAspect(referenceFov, targetAspect, aspect);

            if (vcams != null)
            {
                for (int i = 0; i < vcams.Length; i++)
                {
                    var v = vcams[i];
                    if (v == null) continue;
                    var lens = v.Lens;
                    lens.FieldOfView = newFov;
                    v.Lens = lens;
                }
            }

            if (extraCameras != null)
            {
                for (int i = 0; i < extraCameras.Length; i++)
                {
                    var c = extraCameras[i];
                    if (c == null) continue;
                    c.fieldOfView = newFov;
                }
            }
        }
    }
}
