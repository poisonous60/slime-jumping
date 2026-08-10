using UnityEngine;

namespace SlimeJumping.Background
{
    /// <summary>Manager-style object. Builds a camera-facing quad on the
    /// BackgroundBlur layer, far behind the background decor, running the starfield
    /// shader, and fades it in from camera height — independent of the skybox
    /// crossfade. Star look knobs live on the material asset; this component only
    /// drives placement and the height fade (via MaterialPropertyBlock, so the
    /// asset is never mutated). Removing the object is a full rollback.</summary>
    public class StarFieldOverlay : MonoBehaviour
    {
        [Tooltip("Camera the quad follows. Empty = Camera.main.")]
        [SerializeField] private UnityEngine.Camera targetCamera;
        [Tooltip("Starfield material (SlimeJumping/Star Field Overlay). Look knobs live on the asset.")]
        [SerializeField] private Material material;

        [Header("Reveal (world Y)")]
        [Tooltip("Camera height where stars start to appear.")]
        [SerializeField] private float fadeStartY = 420f;
        [Tooltip("Camera height where stars are fully visible.")]
        [SerializeField] private float fadeEndY = 470f;

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
        private MaterialPropertyBlock mpb;

        private static readonly int MasterAlpha = Shader.PropertyToID("_MasterAlpha");
        private static readonly int UvScale = Shader.PropertyToID("_UvScale");

        private void OnEnable()
        {
            cam = targetCamera != null ? targetCamera : UnityEngine.Camera.main;
            if (cam == null || material == null)
            {
                enabled = false;
                return;
            }

            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                    new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f)
                },
                uv = new[]
                {
                    new Vector2(0f, 0f), new Vector2(1f, 0f),
                    new Vector2(0f, 1f), new Vector2(1f, 1f)
                },
                triangles = new[] { 0, 2, 1, 1, 2, 3 }
            };

            var go = new GameObject("StarFieldQuad");
            go.layer = LayerMask.NameToLayer("BackgroundBlur");
            quad = go.transform;
            quad.SetParent(cam.transform, false);
            quad.localPosition = new Vector3(0f, 0f, distance);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            rend = go.AddComponent<MeshRenderer>();
            rend.sharedMaterial = material;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            rend.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mpb = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            float master = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(fadeStartY, fadeEndY, cam.transform.position.y));
            if (master <= 0f)
            {
                if (rend.enabled) rend.enabled = false;
                return;
            }

            float h = 2f * distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) + margin * 2f;
            float w = h * cam.aspect + margin * 2f;
            quad.localPosition = new Vector3(0f, 0f, distance);
            quad.localScale = new Vector3(w, h, 1f);

            rend.enabled = true;
            mpb.SetFloat(MasterAlpha, master);
            mpb.SetFloat(UvScale, w / h);
            rend.SetPropertyBlock(mpb);
        }

        private void OnDisable()
        {
            if (quad != null) Destroy(quad.gameObject);
            quad = null;
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
