using UnityEngine;
using Unity.Cinemachine;

namespace SlimeJumping.Camera
{
    /// Keeps on-screen object sizes (slime, platforms, UI-less world art) roughly
    /// constant across resolutions by scaling perspective FOV with Screen.height.
    ///
    /// Formula: tan(newFov/2) = tan(referenceFov/2) * (Screen.height / referenceHeight).
    /// At the reference resolution the FOV stays at its designed value. Above it the FOV
    /// widens so more world is visible, below it the FOV narrows so the visible world
    /// shrinks — in both cases pixels-per-world-unit at the slime plane stays constant.
    ///
    /// The reference camera distance is implicit in the unchanged vcam/transform positions.
    [DefaultExecutionOrder(-50)]
    public class ResolutionScaleController : MonoBehaviour
    {
        [SerializeField] private float referenceHeight = 1080f;
        [Tooltip("FOV designed for referenceHeight. Used as the 1:1 anchor point.")]
        [SerializeField] private float referenceFov = 40f;

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

            float h = Mathf.Max(1, Screen.height);
            float refTanHalf = Mathf.Tan(referenceFov * 0.5f * Mathf.Deg2Rad);
            float newFov = 2f * Mathf.Atan(refTanHalf * (h / referenceHeight)) * Mathf.Rad2Deg;

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
