using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Drives a SlimeJumping/SpaceWarp_Distortion material on a target Renderer.
    /// Call <see cref="Trigger"/> to fire a one-shot shockwave pulse (e.g. from a
    /// JumpPad). The persistent-warp portion is configured directly on the material
    /// — this component only animates the pulse phase via MaterialPropertyBlock.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("SlimeJumping/Mechanisms/Space Warp Effect")]
    public class SpaceWarpEffect : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Renderer using SlimeJumping/SpaceWarp_Distortion. Defaults to Renderer on this GameObject.")]
        [SerializeField] private Renderer targetRenderer;

        [Header("Pulse")]
        [Tooltip("Seconds for one shockwave to expand from center to edge.")]
        [Min(0.05f)] [SerializeField] private float pulseDuration = 0.45f;
        [Tooltip("Peak amplitude of the shockwave UV offset.")]
        [Range(0f, 0.4f)] [SerializeField] private float pulseStrength = 0.18f;
        [Tooltip("Auto-disable the renderer between pulses (so the persistent warp doesn't show). " +
                 "Disable when this object IS a persistent zone — leave on for jump-pad style usage.")]
        [SerializeField] private bool hideWhenIdle = true;

        static readonly int IdPulsePhase    = Shader.PropertyToID("_PulsePhase");
        static readonly int IdPulseStrength = Shader.PropertyToID("_PulseStrength");

        MaterialPropertyBlock mpb;
        float pulseT = -1f; // <0 → not active

        void Reset()
        {
            if (targetRenderer == null) targetRenderer = GetComponent<Renderer>();
        }

        void OnEnable()
        {
            if (mpb == null) mpb = new MaterialPropertyBlock();
            ApplyPulse(0f, 0f);
            if (hideWhenIdle && targetRenderer != null) targetRenderer.enabled = false;
        }

        void OnDisable()
        {
            pulseT = -1f;
        }

        public void Trigger()
        {
            if (targetRenderer == null) return;
            pulseT = 0f;
            if (hideWhenIdle) targetRenderer.enabled = true;
        }

        void Update()
        {
            if (pulseT < 0f) return;

            pulseT += Time.deltaTime;
            float phase = Mathf.Clamp01(pulseT / pulseDuration);
            ApplyPulse(phase, pulseStrength);

            if (phase >= 1f)
            {
                pulseT = -1f;
                ApplyPulse(0f, 0f);
                if (hideWhenIdle && targetRenderer != null) targetRenderer.enabled = false;
            }
        }

        void ApplyPulse(float phase, float strength)
        {
            if (targetRenderer == null) return;
            if (mpb == null) mpb = new MaterialPropertyBlock();
            targetRenderer.GetPropertyBlock(mpb);
            mpb.SetFloat(IdPulsePhase, phase);
            mpb.SetFloat(IdPulseStrength, strength);
            targetRenderer.SetPropertyBlock(mpb);
        }
    }
}
