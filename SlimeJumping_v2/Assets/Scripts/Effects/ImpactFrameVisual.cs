using System.Collections;
using UnityEngine;

namespace SlimeJumping.Effects
{
    /// Drives the fullscreen impact-frame post-effect during hitstop. Subscribes to
    /// HitStopService.OnFreezeStarted, pushes the static visual parameters onto the
    /// shared material, and animates the master _Intensity property over the freeze
    /// duration using unscaled time. Combines three anime impact-frame components:
    /// luma posterize (B&amp;W), Sobel edge outlines, and radial speed lines from
    /// _RadialCenter. Drop this on any scene GO; only one active instance per scene.
    [DisallowMultipleComponent]
    public class ImpactFrameVisual : MonoBehaviour
    {
        [SerializeField] private Material material;

        [Header("Timing")]
        [Tooltip("Intensity curve over normalized freeze time (x=0 start, x=1 end).")]
        [SerializeField]
        private AnimationCurve intensityOverTime = new AnimationCurve(
            new Keyframe(0f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f));

        [Header("Posterize")]
        [SerializeField, Range(0f, 1f)] private float threshold = 0.5f;
        [SerializeField, Range(0f, 0.2f)] private float softness = 0.02f;
        [SerializeField] private bool invert = false;

        [Header("Edge Outlines")]
        [Tooltip("Sobel gain — higher = thicker/more visible black outlines.")]
        [SerializeField, Range(0f, 4f)] private float edgeStrength = 2.0f;
        [Tooltip("Minimum edge magnitude to count as an outline. Lower = more sensitive.")]
        [SerializeField, Range(0f, 0.5f)] private float edgeThreshold = 0.08f;

        [Header("Radial Speed Lines")]
        [SerializeField, Range(0f, 1f)] private float radialStrength = 0.9f;
        [Tooltip("Screen UV of the impact point the rays emanate from (0.5,0.5 = center).")]
        [SerializeField] private Vector2 radialCenter = new Vector2(0.5f, 0.5f);
        [Tooltip("Number of rays around the circle. Higher = denser spokes.")]
        [SerializeField, Range(4f, 128f)] private float radialLineCount = 40f;
        [Tooltip("0 = soft sunbeam, 1 = needle-thin spikes.")]
        [SerializeField, Range(0f, 1f)] private float radialSharpness = 0.5f;
        [Tooltip("Rays fade to zero inside this UV radius around _RadialCenter.")]
        [SerializeField, Range(0f, 0.6f)] private float radialInnerRadius = 0.1f;
        [Tooltip("Width of the inner fade band (larger = softer fade).")]
        [SerializeField, Range(0.001f, 0.6f)] private float radialFadeWidth = 0.15f;

        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        private static readonly int InvertId = Shader.PropertyToID("_Invert");
        private static readonly int EdgeStrengthId = Shader.PropertyToID("_EdgeStrength");
        private static readonly int EdgeThresholdId = Shader.PropertyToID("_EdgeThreshold");
        private static readonly int RadialStrengthId = Shader.PropertyToID("_RadialStrength");
        private static readonly int RadialCenterId = Shader.PropertyToID("_RadialCenter");
        private static readonly int RadialLineCountId = Shader.PropertyToID("_RadialLineCount");
        private static readonly int RadialSharpnessId = Shader.PropertyToID("_RadialSharpness");
        private static readonly int RadialInnerRadiusId = Shader.PropertyToID("_RadialInnerRadius");
        private static readonly int RadialFadeWidthId = Shader.PropertyToID("_RadialFadeWidth");

        private Coroutine running;

        private void OnEnable()
        {
            ApplyStaticProperties();
            SetIntensity(0f);
            HitStopService.OnFreezeStarted += HandleFreezeStarted;
        }

        private void OnDisable()
        {
            HitStopService.OnFreezeStarted -= HandleFreezeStarted;
            if (running != null) { StopCoroutine(running); running = null; }
            SetIntensity(0f);
        }

        private void OnValidate()
        {
            // Push changes immediately while tuning in Inspector.
            if (material != null) ApplyStaticProperties();
        }

        private void HandleFreezeStarted(float duration)
        {
            if (material == null || duration <= 0f) return;
            ApplyStaticProperties();
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(RunCurve(duration));
        }

        private IEnumerator RunCurve(float duration)
        {
            float t = 0f;
            while (t < duration)
            {
                float n = duration > 0f ? t / duration : 1f;
                SetIntensity(intensityOverTime.Evaluate(n));
                t += Time.unscaledDeltaTime;
                yield return null;
            }
            SetIntensity(0f);
            running = null;
        }

        private void ApplyStaticProperties()
        {
            if (material == null) return;
            material.SetFloat(ThresholdId, threshold);
            material.SetFloat(SoftnessId, softness);
            material.SetFloat(InvertId, invert ? 1f : 0f);
            material.SetFloat(EdgeStrengthId, edgeStrength);
            material.SetFloat(EdgeThresholdId, edgeThreshold);
            material.SetFloat(RadialStrengthId, radialStrength);
            material.SetVector(RadialCenterId, new Vector4(radialCenter.x, radialCenter.y, 0f, 0f));
            material.SetFloat(RadialLineCountId, radialLineCount);
            material.SetFloat(RadialSharpnessId, radialSharpness);
            material.SetFloat(RadialInnerRadiusId, radialInnerRadius);
            material.SetFloat(RadialFadeWidthId, radialFadeWidth);
        }

        private void SetIntensity(float v)
        {
            if (material == null) return;
            material.SetFloat(IntensityId, Mathf.Clamp01(v));
        }
    }
}
