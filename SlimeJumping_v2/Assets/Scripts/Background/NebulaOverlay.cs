using UnityEngine;

namespace SlimeJumping.Background
{
    /// <summary>Manager-style object, same shape as StarFieldOverlay: builds a camera-facing
    /// quad on its own layer so a dedicated OverlayBloomCamera picks it up and only the parts
    /// pushed above 1 glow. It gets its own bloom rig rather than sharing the starfield's,
    /// because the cloud is low frequency enough to render downscaled and needs a far higher
    /// bloom threshold — on the star profile most of the cloud crossed it. Colour is authored
    /// per height as four HDR gradients,
    /// one per layer of the shader's composition, evaluated at the camera's position in the
    /// band — the alpha key doubles as that layer's strength, so a layer can fade out
    /// entirely at some altitudes. Height decides colour and nothing else: the two cubemap
    /// rotations drift on a clock instead, at different rates so the layers shear apart rather
    /// than sliding as one sheet. Driving them from height made the cloud swim backwards
    /// whenever the player fell, which reads as the sky moving rather than the player.
    /// Look knobs live on the
    /// material; height, gradients and drift live here and are pushed through a
    /// MaterialPropertyBlock, so the material asset is never mutated. Runs in edit mode so the
    /// gradients can be authored against the real bloom rig; everything it spawns is DontSave,
    /// so the scene file never picks it up. Removing the object is
    /// a full rollback.</summary>
    [ExecuteAlways]
    public class NebulaOverlay : MonoBehaviour
    {
        [Tooltip("Camera the quad follows. Empty = Camera.main.")]
        [SerializeField] private UnityEngine.Camera targetCamera;
        [Tooltip("Nebula material (SlimeJumping/Nebula Overlay).")]
        [SerializeField] private Material material;
        [Tooltip("Layer the quad lives on. Must match the OverlayBloomCamera that renders it.")]
        [SerializeField] private string overlayLayerName = "Nebula";

        [Header("Height band (world Y)")]
        [Tooltip("Camera height mapped to gradient position 0.")]
        [SerializeField] private float fadeStartY = 366f;
        [Tooltip("Camera height mapped to gradient position 1.")]
        [SerializeField] private float fadeEndY = 542f;

        [Header("Colour by height (alpha = that layer's strength)")]
        [Tooltip("Thin edges of the cloud.")]
        [GradientUsage(true)] [SerializeField] private Gradient ambientOverHeight = new Gradient();
        [Tooltip("Dense body of the cloud.")]
        [GradientUsage(true)] [SerializeField] private Gradient basementOverHeight = new Gradient();
        [Tooltip("First ripple pass.")]
        [GradientUsage(true)] [SerializeField] private Gradient ripples1OverHeight = new Gradient();
        [Tooltip("Second ripple pass.")]
        [GradientUsage(true)] [SerializeField] private Gradient ripples2OverHeight = new Gradient();

        [Header("Drift (time, not height)")]
        [Tooltip("Degrees the density cubemap turns per second.")]
        [SerializeField] private float densityDriftPerSecond = 0.4f;
        [Tooltip("Degrees the diffusion cubemap turns per second. Differ from density or the layers move as one sheet.")]
        [SerializeField] private float diffusionDriftPerSecond = 0.18f;
        [Tooltip("Axis the cloud turns about, in world space.")]
        [SerializeField] private Vector3 travelAxis = new Vector3(0.2f, 1f, 0.1f);

        [Header("Placement")]
        [Tooltip("Quad distance in front of the camera. Keep beyond the farthest background decor.")]
        [SerializeField] private float distance = 300f;
        [Tooltip("Extra world units around the camera view on every side.")]
        [SerializeField] private float margin = 20f;

        private UnityEngine.Camera cam;
        private MeshRenderer rend;
        private Transform quad;
        private Mesh quadMesh;
        private MaterialPropertyBlock mpb;

        private static readonly int AmbientTint = Shader.PropertyToID("_AmbientTint");
        private static readonly int BasementTint = Shader.PropertyToID("_BasementTint");
        private static readonly int RipplesTint1 = Shader.PropertyToID("_RipplesTint1");
        private static readonly int RipplesTint2 = Shader.PropertyToID("_RipplesTint2");
        private static readonly int DensityRotation = Shader.PropertyToID("_DensityRotation");
        private static readonly int DiffusionRotation = Shader.PropertyToID("_DiffusionRotation");

        private void OnEnable()
        {
            cam = targetCamera != null ? targetCamera : UnityEngine.Camera.main;
            int layer = LayerMask.NameToLayer(overlayLayerName);
            if (cam == null || material == null || layer < 0)
            {
                Debug.LogWarning("NebulaOverlay disabled: missing camera/material or overlay layer.", this);
                enabled = false;
                return;
            }

            quadMesh = new Mesh
            {
                hideFlags = HideFlags.DontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                    new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f)
                },
                triangles = new[] { 0, 2, 1, 1, 2, 3 }
            };

            var go = new GameObject("NebulaQuad") { hideFlags = HideFlags.DontSave };
            go.layer = layer;
            quad = go.transform;
            quad.SetParent(cam.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
            rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = material;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mpb = new MaterialPropertyBlock();
            // Edit mode only ticks the player loop on demand, so size and tint the quad up front.
            LateUpdate();
        }

        private void LateUpdate()
        {
            if (cam == null || quad == null || rend == null)
            {
                enabled = false;
                return;
            }

            float t = Mathf.InverseLerp(fadeStartY, fadeEndY, cam.transform.position.y);

            float h = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) + margin * 2f;
            quad.localPosition = new Vector3(0f, 0f, distance);
            quad.localScale = new Vector3(h * cam.aspect + margin * 2f, h, 1f);

            var axis = travelAxis.sqrMagnitude > 1e-6f ? travelAxis.normalized : Vector3.up;
            mpb.SetColor(AmbientTint, ambientOverHeight.Evaluate(t));
            mpb.SetColor(BasementTint, basementOverHeight.Evaluate(t));
            mpb.SetColor(RipplesTint1, ripples1OverHeight.Evaluate(t));
            mpb.SetColor(RipplesTint2, ripples2OverHeight.Evaluate(t));
            // Monotonic, so falling never rewinds the cloud the way a height-driven angle did.
            float clock = Time.time;
            mpb.SetMatrix(DensityRotation, Matrix4x4.Rotate(Quaternion.AngleAxis(clock * densityDriftPerSecond, axis)));
            mpb.SetMatrix(DiffusionRotation, Matrix4x4.Rotate(Quaternion.AngleAxis(clock * diffusionDriftPerSecond, axis)));
            rend.SetPropertyBlock(mpb);
        }

        private void OnDisable()
        {
            if (quad != null) SafeDestroy(quad.gameObject);
            if (quadMesh != null) SafeDestroy(quadMesh);
            quad = null;
            quadMesh = null;
            rend = null;
        }

        /// <summary>Deferred Destroy is the only legal call during play, and illegal outside it.</summary>
        private static void SafeDestroy(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }
    }
}
