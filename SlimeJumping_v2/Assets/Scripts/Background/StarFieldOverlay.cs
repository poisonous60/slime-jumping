using UnityEngine;

namespace SlimeJumping.Background
{
    /// <summary>Manager-style object. Builds a camera-facing quad on the
    /// BackgroundBlur layer, far behind the background decor, running the starfield
    /// shader, and reveals it by camera height — independent of the skybox
    /// crossfade. Reveal is a plain function of the current height, so one altitude
    /// always shows one sky, and it grows the star *count* (the shader gates each
    /// star on its own hashed birth threshold) instead of dimming the whole field.
    /// revealCurve shapes that ramp across the band; its default is the SmoothStep it replaced.
    /// Below fadeStartY nothing is drawn at all. The quad only has to cover the view;
    /// the shader samples in screen space, so the FOV that sizes it stays out of
    /// which stars come out. Star look
    /// knobs live on the material asset; this component only drives placement and
    /// reveal (via MaterialPropertyBlock, so the asset is never mutated). Removing
    /// the object is a full rollback.</summary>
    public class StarFieldOverlay : MonoBehaviour
    {
        [Tooltip("Camera the quad follows. Empty = Camera.main.")]
        [SerializeField] private UnityEngine.Camera targetCamera;
        [Tooltip("Starfield material (SlimeJumping/Star Field Overlay). Look knobs live on the asset.")]
        [SerializeField] private Material material;

        [Header("Reveal (world Y)")]
        [Tooltip("Camera height where the first stars appear. Nothing is drawn below it.")]
        [SerializeField] private float fadeStartY = 420f;
        [Tooltip("Camera height where every star is out.")]
        [SerializeField] private float fadeEndY = 470f;
        [Tooltip("Shapes the reveal across the fade band: x = 0 at fadeStartY to 1 at fadeEndY, y = fraction of stars out. Default matches the old SmoothStep.")]
        [SerializeField] private AnimationCurve revealCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Placement")]
        [Tooltip("Quad distance in front of the camera. Keep beyond the farthest background decor (~256).")]
        [SerializeField] private float distance = 280f;
        [Tooltip("Extra world units around the camera view on every side.")]
        [SerializeField] private float margin = 20f;
        [Tooltip("Half-width of the fade gizmo lines.")]
        [SerializeField] private float gizmoHalfWidth = 120f;

        private UnityEngine.Camera cam;
        private MeshRenderer rend;
        private Transform quad;
        private Mesh quadMesh;
        private MaterialPropertyBlock mpb;
        private float reveal;

        private static readonly int Reveal = Shader.PropertyToID("_Reveal");

        private void OnEnable()
        {
            cam = targetCamera != null ? targetCamera : UnityEngine.Camera.main;
            if (cam == null || material == null)
            {
                enabled = false;
                return;
            }

            quadMesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                    new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f)
                },
                triangles = new[] { 0, 2, 1, 1, 2, 3 }
            };

            var go = new GameObject("StarFieldQuad");
            // StarFieldBloom's private camera owns this layer; its target is composited back over the background.
            go.layer = LayerMask.NameToLayer("StarField");
            quad = go.transform;
            quad.SetParent(cam.transform, false);
            quad.localPosition = new Vector3(0f, 0f, distance);
            go.AddComponent<MeshFilter>().sharedMesh = quadMesh;
            rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = material;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mpb = new MaterialPropertyBlock();
            reveal = 0f;
        }

        private void LateUpdate()
        {
            float band = Mathf.InverseLerp(fadeStartY, fadeEndY, cam.transform.position.y);
            // Clamped because curve tangents overshoot, and the shader reads _Reveal as a 0..1 fraction.
            reveal = Mathf.Clamp01(revealCurve.Evaluate(band));
            if (reveal <= 0.0005f)
            {
                if (rend.enabled) rend.enabled = false;
                return;
            }

            float h = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) + margin * 2f;
            float w = h * cam.aspect + margin * 2f;
            quad.localPosition = new Vector3(0f, 0f, distance);
            quad.localScale = new Vector3(w, h, 1f);

            rend.enabled = true;
            mpb.SetFloat(Reveal, reveal);
            rend.SetPropertyBlock(mpb);
        }

        private void OnDisable()
        {
            if (quad != null) Destroy(quad.gameObject);
            if (quadMesh != null) Destroy(quadMesh);
            quad = null;
            quadMesh = null;
            rend = null;
        }

        private void OnDrawGizmosSelected()
        {
            float x = transform.position.x;
            Gizmos.color = new Color(1f, 1f, 0.4f, 0.35f);
            var a = new Vector3(x - gizmoHalfWidth, fadeStartY, 0f);
            Gizmos.DrawLine(a, a + Vector3.right * (gizmoHalfWidth * 2f));
            Gizmos.color = new Color(1f, 1f, 0.4f, 1f);
            var b = new Vector3(x - gizmoHalfWidth, fadeEndY, 0f);
            Gizmos.DrawLine(b, b + Vector3.right * (gizmoHalfWidth * 2f));
        }
    }
}
