using System.Collections;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Effects
{
    /// Drives the fullscreen impact-frame post-effect. Hard-cut timing (no fade):
    /// frame 0 is a white pre-flash with radial chroma aberration; frames 1..N hold
    /// the binary manga composition; end snaps to intensity 0.
    ///
    /// Subscribes to HitStopService.OnImpactFrameStarted — a separate channel from
    /// OnFreezeStarted so callers can request the visual without a time freeze (and
    /// vice versa). Resolves the radial center to the slime's screen-space UV so
    /// speed lines emanate from the subject, not the screen center. All other tuning
    /// lives on the shared Material and is pushed on enable / OnValidate / each trigger.
    ///
    /// Setup (one-time): URP Renderer Asset must carry a FullScreenPassRendererFeature
    /// pointing at `Assets/Art/Materials/M_ImpactFrameGrayscale.mat` at injection point
    /// AfterRenderingPostProcessing, with requirements=Depth (the
    /// ImpactFrameRendererFeatureSetup editor utility wires this idempotently).
    [DisallowMultipleComponent]
    public class ImpactFrameVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Material material;
        [Tooltip("Optional. When assigned, radial speed lines emanate from the slime's screen position instead of the Inspector fallback.")]
        [SerializeField] private SlimeController slime;

        [Header("Timing")]
        [Tooltip("When true, the first frame of each freeze is a pure white flash with chroma aberration.")]
        [SerializeField] private bool whiteFlashEnabled = true;

        [Header("Pre-flash")]
        [Tooltip("Radial UV offset applied to R/B channels during the pre-flash frame.")]
        [SerializeField, Range(0f, 0.02f)] private float chromaShift = 0.008f;

        [Header("Posterize")]
        [Tooltip("Fallback luma threshold used only when Use Depth Silhouette is off.")]
        [SerializeField, Range(0f, 1f)] private float threshold = 0.5f;
        [SerializeField, Range(0f, 0.2f)] private float softness = 0.02f;
        [SerializeField] private bool invert = false;
        [Tooltip("Ink step cutoff — forces output to binary black or white (no gray mid-tones).")]
        [SerializeField, Range(0f, 1f)] private float inkThreshold = 0.25f;

        [Header("Subject Silhouette")]
        [Tooltip("Use camera depth for the silhouette mask. Falls back to luma threshold when off.")]
        [SerializeField] private bool useDepthSilhouette = true;
        [Tooltip("Pixels closer than this eye-depth value become the subject silhouette.")]
        [SerializeField] private float subjectMaxDepth = 20f;
        [SerializeField, Range(0f, 4f)] private float subjectDepthSoftness = 0.5f;

        [Header("Accent")]
        [Tooltip("HDR accent color painted inside the silhouette and along the rim. Alpha = interior fill strength.")]
        [ColorUsage(true, true)]
        [SerializeField] private Color accentColor = new Color(0.4f, 1.0f, 0.55f, 0.85f);
        [Tooltip("Reserved. Luma gate for future accent-from-highlights mode.")]
        [SerializeField, Range(0f, 1f)] private float accentThreshold = 0.7f;
        [SerializeField, Range(0f, 1f)] private float accentStrength = 1.0f;

        [Header("Edge Outlines")]
        [SerializeField, Range(0f, 4f)] private float edgeStrength = 2.0f;
        [SerializeField, Range(0f, 0.5f)] private float edgeThreshold = 0.08f;

        [Header("Radial Speed Lines")]
        [SerializeField, Range(0f, 1f)] private float radialStrength = 0.9f;
        [Tooltip("Fallback radial center (screen UV) when slime reference is not set.")]
        [SerializeField] private Vector2 radialCenter = new Vector2(0.5f, 0.5f);
        [SerializeField, Range(4f, 128f)] private float radialLineCount = 40f;
        [SerializeField, Range(0f, 1f)] private float radialSharpness = 0.5f;
        [SerializeField, Range(0f, 0.6f)] private float radialInnerRadius = 0.1f;
        [SerializeField, Range(0.001f, 0.6f)] private float radialFadeWidth = 0.15f;

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int WhiteFlashId = Shader.PropertyToID("_WhiteFlash");
        private static readonly int ChromaActiveId = Shader.PropertyToID("_ChromaActive");
        private static readonly int ChromaShiftId = Shader.PropertyToID("_ChromaShift");
        private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        private static readonly int InvertId = Shader.PropertyToID("_Invert");
        private static readonly int InkThresholdId = Shader.PropertyToID("_InkThreshold");
        private static readonly int UseDepthSilhouetteId = Shader.PropertyToID("_UseDepthSilhouette");
        private static readonly int SubjectMaxDepthId = Shader.PropertyToID("_SubjectMaxDepth");
        private static readonly int SubjectDepthSoftnessId = Shader.PropertyToID("_SubjectDepthSoftness");
        private static readonly int AccentColorId = Shader.PropertyToID("_AccentColor");
        private static readonly int AccentThresholdId = Shader.PropertyToID("_AccentThreshold");
        private static readonly int AccentStrengthId = Shader.PropertyToID("_AccentStrength");
        private static readonly int EdgeStrengthId = Shader.PropertyToID("_EdgeStrength");
        private static readonly int EdgeThresholdId = Shader.PropertyToID("_EdgeThreshold");
        private static readonly int RadialStrengthId = Shader.PropertyToID("_RadialStrength");
        private static readonly int RadialCenterId = Shader.PropertyToID("_RadialCenter");
        private static readonly int RadialLineCountId = Shader.PropertyToID("_RadialLineCount");
        private static readonly int RadialSharpnessId = Shader.PropertyToID("_RadialSharpness");
        private static readonly int RadialInnerRadiusId = Shader.PropertyToID("_RadialInnerRadius");
        private static readonly int RadialFadeWidthId = Shader.PropertyToID("_RadialFadeWidth");

        private UnityEngine.Camera cam;
        private Coroutine running;

        private void OnEnable()
        {
            ApplyStaticProperties();
            SetIdle();
            cam = UnityEngine.Camera.main;
            HitStopService.OnImpactFrameStarted += HandleFreezeStarted;
        }

        private void OnDisable()
        {
            HitStopService.OnImpactFrameStarted -= HandleFreezeStarted;
            if (running != null) { StopCoroutine(running); running = null; }
            SetIdle();
        }

        private void OnValidate()
        {
            if (material != null) ApplyStaticProperties();
        }

        private void HandleFreezeStarted(float duration)
        {
            if (material == null || duration <= 0f) return;
            ApplyStaticProperties();
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(RunSequence(duration));
        }

        private IEnumerator RunSequence(float duration)
        {
            float endReal = Time.realtimeSinceStartup + duration;

            // Refresh Camera.main lazily in case the scene swapped cameras.
            if (cam == null) cam = UnityEngine.Camera.main;

            // Resolve radial center to the slime's screen-space UV.
            if (slime != null && cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(slime.Position);
                material.SetVector(RadialCenterId,
                    new Vector4(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y), 0f, 0f));
            }

            // Frame 0: white pre-flash with chroma aberration.
            float flash = whiteFlashEnabled ? 1f : 0f;
            material.SetFloat(WhiteFlashId, flash);
            material.SetFloat(ChromaActiveId, flash);
            material.SetFloat(IntensityId, 1f);
            yield return null;

            // Frames 1..N: binary ink hold until realtime end.
            material.SetFloat(WhiteFlashId, 0f);
            material.SetFloat(ChromaActiveId, 0f);
            material.SetFloat(IntensityId, 1f);
            while (Time.realtimeSinceStartup < endReal)
                yield return null;

            // Hard cut off.
            SetIdle();
            running = null;
        }

        private void SetIdle()
        {
            if (material == null) return;
            material.SetFloat(IntensityId, 0f);
            material.SetFloat(WhiteFlashId, 0f);
            material.SetFloat(ChromaActiveId, 0f);
        }

        private void ApplyStaticProperties()
        {
            if (material == null) return;
            material.SetFloat(ChromaShiftId, chromaShift);
            material.SetFloat(ThresholdId, threshold);
            material.SetFloat(SoftnessId, softness);
            material.SetFloat(InvertId, invert ? 1f : 0f);
            material.SetFloat(InkThresholdId, inkThreshold);
            material.SetFloat(UseDepthSilhouetteId, useDepthSilhouette ? 1f : 0f);
            material.SetFloat(SubjectMaxDepthId, subjectMaxDepth);
            material.SetFloat(SubjectDepthSoftnessId, subjectDepthSoftness);
            material.SetColor(AccentColorId, accentColor);
            material.SetFloat(AccentThresholdId, accentThreshold);
            material.SetFloat(AccentStrengthId, accentStrength);
            material.SetFloat(EdgeStrengthId, edgeStrength);
            material.SetFloat(EdgeThresholdId, edgeThreshold);
            material.SetFloat(RadialStrengthId, radialStrength);
            material.SetVector(RadialCenterId, new Vector4(radialCenter.x, radialCenter.y, 0f, 0f));
            material.SetFloat(RadialLineCountId, radialLineCount);
            material.SetFloat(RadialSharpnessId, radialSharpness);
            material.SetFloat(RadialInnerRadiusId, radialInnerRadius);
            material.SetFloat(RadialFadeWidthId, radialFadeWidth);
        }
    }
}
