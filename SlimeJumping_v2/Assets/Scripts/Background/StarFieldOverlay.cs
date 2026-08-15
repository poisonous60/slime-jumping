using UnityEngine;

namespace SlimeJumping.Background
{
    /// <summary>Manager-style object. Builds a camera-facing quad on the
    /// StarField layer, far behind the background decor, running the starfield
    /// shader, and reveals it by camera height — independent of the skybox
    /// crossfade. Reveal is a plain function of the current height, so one altitude
    /// always shows one sky, and it grows the star *count* (the shader gates each
    /// star on its own hashed birth threshold) instead of dimming the whole field.
    /// revealCurve shapes that ramp across the band; its default is the SmoothStep it replaced.
    /// Below fadeStartY nothing is drawn at all. The quad only has to cover the view;
    /// the shader samples in screen space, so the FOV that sizes it stays out of
    /// which stars come out. Star look
    /// knobs live on the material asset; this component only drives placement,
    /// reveal and the size distribution (via MaterialPropertyBlock, so the asset is never
    /// mutated). sizeDistribution is drawn as star count against star size and baked to an
    /// inverse-CDF lookup, because a shader can only draw a flat random number and needs the
    /// mapping that turns it into the drawn proportions. Runs in
    /// edit mode so the look knobs can be tuned against the real bloom rig instead of
    /// through a throwaway camera; everything it spawns is DontSave, so the scene file
    /// never picks it up. Removing
    /// the object is a full rollback.</summary>
    [ExecuteAlways]
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

        [Header("Size distribution")]
        [Tooltip("x = star size as a fraction of the material's Star Size Max, y = how many stars get that size (relative). Only the shape matters; it is normalised.")]
        [SerializeField] private AnimationCurve sizeDistribution = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.08f, 1f), new Keyframe(0.35f, 0.06f), new Keyframe(1f, 0.006f));

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
        private Texture2D sizeLut;
        private float reveal;

        private const int LutSize = 256;
        private static readonly int Reveal = Shader.PropertyToID("_Reveal");
        private static readonly int SizeCurve = Shader.PropertyToID("_SizeCurve");

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
                hideFlags = HideFlags.DontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                    new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f)
                },
                triangles = new[] { 0, 2, 1, 1, 2, 3 }
            };

            var go = new GameObject("StarFieldQuad") { hideFlags = HideFlags.DontSave };
            // OverlayBloomCamera's private camera owns this layer; its target is composited back over the background.
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
            BakeSizeLut();
            // Edit mode only ticks the player loop on demand, so size and place the quad up front.
            LateUpdate();
        }

        private void OnValidate()
        {
            if (mpb != null) BakeSizeLut();
        }

        /// <summary>Turns the drawn counts-per-size into the inverse CDF the shader indexes with a
        /// flat per-star hash, which is the only mapping that reproduces the drawn proportions.</summary>
        private void BakeSizeLut()
        {
            if (sizeLut == null)
            {
                sizeLut = new Texture2D(LutSize, 1, TextureFormat.RFloat, false, true)
                {
                    name = "StarSizeLUT",
                    hideFlags = HideFlags.DontSave,
                    // Point, or filtering across a quantile jump emits sizes the curve gave no stars to.
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var weights = new float[LutSize];
            float total = 0f;
            if (sizeDistribution != null)
            {
                for (int i = 0; i < LutSize; i++)
                {
                    weights[i] = Mathf.Max(0f, sizeDistribution.Evaluate(i / (LutSize - 1f)));
                    total += weights[i];
                }
            }

            var px = new Color[LutSize];
            // A curve asking for no stars at any size means no stars, not stars at full size.
            if (total <= 0f)
            {
                for (int i = 0; i < LutSize; i++) px[i] = new Color(0f, 0f, 0f, 0f);
            }
            else
            {
                float running = 0f;
                int src = 0;
                for (int j = 0; j < LutSize; j++)
                {
                    float target = j / (LutSize - 1f) * total;
                    while (src < LutSize - 1 && running + weights[src] < target)
                    {
                        running += weights[src];
                        src++;
                    }
                    px[j] = new Color(src / (LutSize - 1f), 0f, 0f, 0f);
                }
            }

            sizeLut.SetPixels(px);
            sizeLut.Apply(false, false);
            mpb.SetTexture(SizeCurve, sizeLut);
            if (rend != null) rend.SetPropertyBlock(mpb);
        }

        private void LateUpdate()
        {
            if (cam == null || quad == null || rend == null)
            {
                enabled = false;
                return;
            }

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
            if (quad != null) SafeDestroy(quad.gameObject);
            if (quadMesh != null) SafeDestroy(quadMesh);
            if (sizeLut != null) SafeDestroy(sizeLut);
            quad = null;
            quadMesh = null;
            sizeLut = null;
            rend = null;
            mpb = null;
        }

        /// <summary>Deferred Destroy is the only legal call during play, and illegal outside it.</summary>
        private static void SafeDestroy(Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
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
