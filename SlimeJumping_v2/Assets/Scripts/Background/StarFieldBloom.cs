using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlimeJumping.Background
{
    /// <summary>Gives the starfield the glow URP Bloom would normally provide, without
    /// letting bloom touch anything else. The scene camera cannot supply it: its renderer
    /// carries TranslucentImageBlurSource, which drops the color buffer to 8 bits, so a
    /// star at 32 reads back as 1.0 and no threshold can separate it from the white
    /// shrine — and turning post on there dims every stage. So the starfield quad moves to
    /// its own layer and a private camera draws it on a feature-free renderer, where HDR
    /// survives, with Bloom coming from a profile on a volume layer nothing else reads.
    /// That target is composited back additively. Bloom settings live on the profile
    /// asset; this component only owns the rig. Removing it (and putting
    /// StarFieldOverlay's quad back on BackgroundBlur) is a full rollback.</summary>
    [RequireComponent(typeof(StarFieldOverlay))]
    public class StarFieldBloom : MonoBehaviour
    {
        [Tooltip("Camera the starfield quad follows. Must match StarFieldOverlay's. Empty = Camera.main.")]
        [SerializeField] private UnityEngine.Camera targetCamera;
        [Tooltip("Camera that draws the composite back into the scene, i.e. the one rendering BackgroundBlur.")]
        [SerializeField] private UnityEngine.Camera compositeCamera;
        [Tooltip("Composite material (SlimeJumping/Star Field Composite).")]
        [SerializeField] private Material compositeMaterial;
        [Tooltip("Volume profile holding the Bloom override. This is where the glow is tuned.")]
        [SerializeField] private VolumeProfile bloomProfile;

        [Header("Rig")]
        [Tooltip("Index of the renderer without TranslucentImageBlurSource in the URP asset list.")]
        [SerializeField] private int rendererIndex = 4;
        [Tooltip("Star target size = screen / this. 1 keeps the cores pixel sharp.")]
        [SerializeField] private int downscale = 1;
        [Tooltip("Distance of the composite quad in front of the composite camera.")]
        [SerializeField] private float compositeDistance = 100f;

        private UnityEngine.Camera starCam;
        private RenderTexture starRT;
        private Transform compositeQuad;
        private MeshRenderer compositeRend;
        private Mesh compositeMesh;
        private MaterialPropertyBlock compositeMpb;
        private int starLayer = -1;
        private int volumeLayer = -1;
        private UnityEngine.Camera patchedTarget, patchedComposite;
        private bool targetHadStarBit, compositeHadStarBit;
        private int rtW, rtH;

        private static readonly int MainTex = Shader.PropertyToID("_MainTex");

        private void OnEnable()
        {
            if (targetCamera == null) targetCamera = UnityEngine.Camera.main;
            starLayer = LayerMask.NameToLayer("StarField");
            volumeLayer = LayerMask.NameToLayer("StarFieldVolume");
            var pipeline = UniversalRenderPipeline.asset;
            bool rendererOk = pipeline != null && rendererIndex >= 0 && rendererIndex < pipeline.rendererDataList.Length;
            if (targetCamera == null || compositeCamera == null || compositeMaterial == null
                || starLayer < 0 || volumeLayer < 0 || !rendererOk)
            {
                Debug.LogWarning("StarFieldBloom disabled: missing camera/material, StarField layers, or renderer index.", this);
                enabled = false;
                return;
            }

            // Scene cameras must not draw the starfield too, or the clamped copy doubles the bloomed one.
            patchedTarget = targetCamera;
            patchedComposite = compositeCamera;
            int starBit = 1 << starLayer;
            // Only this bit is remembered: BackgroundCameraOption rewrites these masks at runtime.
            targetHadStarBit = (patchedTarget.cullingMask & starBit) != 0;
            compositeHadStarBit = (patchedComposite.cullingMask & starBit) != 0;
            patchedTarget.cullingMask &= ~starBit;
            patchedComposite.cullingMask &= ~starBit;

            BuildStarCamera();
            BuildCompositeQuad();
        }

        private void OnDisable()
        {
            int restoreBit = starLayer >= 0 ? 1 << starLayer : 0;
            if (patchedTarget != null && targetHadStarBit) patchedTarget.cullingMask |= restoreBit;
            if (patchedComposite != null && compositeHadStarBit) patchedComposite.cullingMask |= restoreBit;
            patchedTarget = null;
            patchedComposite = null;

            ReleaseTarget();
            if (starCam != null) starCam.gameObject.SetActive(false);
            if (compositeQuad != null) compositeQuad.gameObject.SetActive(false);
            if (starCam != null) SafeDestroy(starCam.gameObject);
            if (compositeQuad != null) SafeDestroy(compositeQuad.gameObject);
            if (compositeMesh != null) SafeDestroy(compositeMesh);
            starCam = null;
            compositeQuad = null;
            compositeRend = null;
            compositeMesh = null;
        }

        /// <summary>Deferred Destroy is the only legal call during play, but the rig has to stop
        /// drawing this frame, so callers deactivate first.</summary>
        private static void SafeDestroy(UnityEngine.Object o)
        {
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        private void BuildStarCamera()
        {
            var go = new GameObject("StarFieldBloomCamera") { hideFlags = HideFlags.DontSave };
            go.transform.SetParent(targetCamera.transform, false);
            starCam = go.AddComponent<UnityEngine.Camera>();
            starCam.clearFlags = CameraClearFlags.SolidColor;
            starCam.backgroundColor = Color.clear;
            starCam.cullingMask = 1 << starLayer;
            starCam.allowHDR = true;
            starCam.allowMSAA = false;
            // Must finish before whichever scene camera samples the target.
            starCam.depth = Mathf.Min(targetCamera.depth, compositeCamera.depth) - 10f;
            starCam.nearClipPlane = targetCamera.nearClipPlane;
            starCam.farClipPlane = targetCamera.farClipPlane;

            var data = starCam.GetUniversalAdditionalCameraData();
            data.renderType = CameraRenderType.Base;
            data.SetRenderer(rendererIndex);
            data.renderPostProcessing = true;
            data.renderShadows = false;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
            data.volumeLayerMask = 1 << volumeLayer;

            // The volume rides the camera so its profile reaches this camera and no other.
            var volGO = new GameObject("StarFieldBloomVolume") { hideFlags = HideFlags.DontSave };
            volGO.layer = volumeLayer;
            volGO.transform.SetParent(go.transform, false);
            var vol = volGO.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 100f;
            if (bloomProfile != null) vol.sharedProfile = bloomProfile;
            data.volumeTrigger = volGO.transform;
        }

        private void BuildCompositeQuad()
        {
            compositeMesh = new Mesh
            {
                name = "StarFieldCompositeQuad",
                hideFlags = HideFlags.DontSave,
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f),
                    new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f)
                },
                triangles = new[] { 0, 2, 1, 1, 2, 3 }
            };
            compositeMesh.RecalculateBounds();

            var go = new GameObject("StarFieldComposite") { hideFlags = HideFlags.DontSave };
            go.layer = LayerMask.NameToLayer("BackgroundBlur");
            compositeQuad = go.transform;
            compositeQuad.SetParent(compositeCamera.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = compositeMesh;
            compositeRend = go.AddComponent<MeshRenderer>();
            compositeRend.sharedMaterial = compositeMaterial;
            compositeRend.shadowCastingMode = ShadowCastingMode.Off;
            compositeRend.receiveShadows = false;
            compositeRend.lightProbeUsage = LightProbeUsage.Off;
        }

        private void LateUpdate()
        {
            if (starCam == null || compositeQuad == null) return;

            starCam.fieldOfView = targetCamera.fieldOfView;
            starCam.orthographic = targetCamera.orthographic;
            starCam.orthographicSize = targetCamera.orthographicSize;

            int w = Mathf.Max(1, compositeCamera.pixelWidth / Mathf.Max(1, downscale));
            int h = Mathf.Max(1, compositeCamera.pixelHeight / Mathf.Max(1, downscale));
            if (starRT == null || rtW != w || rtH != h)
            {
                ReleaseTarget();
                starRT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
                {
                    name = "StarFieldBloomTarget",
                    hideFlags = HideFlags.DontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
                starRT.Create();
                rtW = w;
                rtH = h;
                starCam.targetTexture = starRT;
                // Property block, not the shared asset: the asset would be dirtied and left pointing at a freed target.
                if (compositeMpb == null) compositeMpb = new MaterialPropertyBlock();
                compositeRend.GetPropertyBlock(compositeMpb);
                compositeMpb.SetTexture(MainTex, starRT);
                compositeRend.SetPropertyBlock(compositeMpb);
            }

            float qh = 2f * compositeDistance * Mathf.Tan(compositeCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            compositeQuad.localPosition = new Vector3(0f, 0f, compositeDistance);
            compositeQuad.localScale = new Vector3(qh * compositeCamera.aspect, qh, 1f);
        }

        private void ReleaseTarget()
        {
            if (starCam != null) starCam.targetTexture = null;
            if (starRT == null) return;
            starRT.Release();
            SafeDestroy(starRT);
            starRT = null;
        }
    }
}
