using UnityEngine;

namespace SlimeJumping.Interaction
{
    [CreateAssetMenu(menuName = "SlimeJumping/SitPose Profile", fileName = "SitPoseProfile")]
    public class SitPoseProfile : ScriptableObject
    {
        [Header("Sit Down")]
        public Vector3 sitScale = new Vector3(1.3f, 0.55f, 1.3f);
        public float sitDuration = 0.4f;
        [Tooltip("OutBack overshoot amplitude.")]
        public float sitOvershoot = 1.4f;
        public float sitAsymmetry = 0.15f;
        public float sitChargeDip = -0.3f;

        [Header("Breathing — shared timing")]
        public float breathFrequency = 0.45f;
        [Tooltip("Randomized amplitude jitter; clamped internally so it can't exceed Breath Amplitude. Applies to scale method only (A/C use pure sine).")]
        public float amplitudeVariation = 0.015f;

        [Header("Breathing — method D: scale modulation (pivot-shifted squash)")]
        [Tooltip("Y-axis scale oscillation amplitude. Set 0 to disable method D.")]
        public float breathAmplitude = 0.04f;
        [Tooltip("Which edge of the body stays fixed when scaling. 0 = top glued (bottom moves), 0.5 = center (symmetric), 1 = bottom glued (top moves).")]
        [Range(0f, 1f)] public float verticalAnchor = 1f;

        [Header("Breathing — method A: position bob (body stays same shape, shifts up/down)")]
        [Tooltip("Y-axis position oscillation in local units. Face follows automatically via UpdateFaceOffset (idle drift + blink stays active). Set 0 to disable.")]
        public float bobAmplitude = 0f;

        [Header("Breathing — method C: top swell (shader _Asymmetry oscillation)")]
        [Tooltip("Adds a sinusoidal offset to sitAsymmetry. Positive/negative values bulge top/bottom edge. Set 0 to disable.")]
        public float topSwellAmplitude = 0f;

        [Header("Get Up")]
        public Vector3 anticipationScale = new Vector3(0.85f, 1.25f, 0.85f);
        public float anticipationDuration = 0.12f;
        public float settleDuration = 0.35f;
        [Min(1f)] public float settleElasticity = 1.1f;
        public float settlePeriod = 0.35f;
    }
}
