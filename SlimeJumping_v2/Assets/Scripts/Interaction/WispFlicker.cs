using UnityEngine;

namespace SlimeJumping.Interaction
{
    /// Drives a pixel-art wisp sprite:
    ///   - Spawn ("뿅"): scale from 0 with ease-out overshoot + alpha fade-in.
    ///   - Idle: subtle bob / drift / scale wobble / alpha flicker.
    ///   - Optionally drives a linked Light's intensity with the same ramp.
    [RequireComponent(typeof(SpriteRenderer))]
    public class WispFlicker : MonoBehaviour
    {
        [Header("Spawn (뿅)")]
        [SerializeField] private float spawnDuration = 0.9f;
        [Tooltip("Peak scale multiplier during the overshoot. 1 = no overshoot.")]
        [SerializeField] private float spawnOvershoot = 1.18f;

        [Header("Motion")]
        [SerializeField] private float bobFrequency = 2.2f;
        [SerializeField] private float bobAmplitude = 0.08f;
        [SerializeField] private float driftFrequency = 0.8f;
        [SerializeField] private float driftAmplitude = 0.04f;
        [SerializeField] private float scaleFrequency = 6f;
        [SerializeField] private float scaleAmplitude = 0.06f;
        [SerializeField] private float flickerFrequency = 9f;
        [SerializeField] private float flickerAmount = 0.12f;

        [Header("Light (optional)")]
        [SerializeField] private Light linkedLight;
        [SerializeField] private float lightIntensityRest = 2f;
        [SerializeField] private float lightIntensityFlicker = 0.4f;

        private SpriteRenderer sr;
        private Vector3 restLocalPos;
        private Vector3 restScale;
        private Color restColor;
        private float phase;
        private float spawnTimer;

        private void Awake()
        {
            sr = GetComponent<SpriteRenderer>();
            restLocalPos = transform.localPosition;
            restScale = transform.localScale;
            restColor = sr.color;
            phase = Random.value * 10f;
            spawnTimer = 0f;
        }

        private void OnEnable()
        {
            spawnTimer = 0f;
        }

        private void Update()
        {
            float dur = Mathf.Max(0.0001f, spawnDuration);
            spawnTimer = Mathf.Min(spawnTimer + Time.deltaTime, dur);
            float s = spawnTimer / dur;

            // Ease-out to overshoot by 75%, then settle to 1.
            float spawnMul;
            if (s < 0.75f)
            {
                float k = s / 0.75f;
                spawnMul = spawnOvershoot * (1f - (1f - k) * (1f - k));
            }
            else
            {
                float k = (s - 0.75f) / 0.25f;
                spawnMul = Mathf.Lerp(spawnOvershoot, 1f, Mathf.SmoothStep(0f, 1f, k));
            }
            float spawnAlpha = Mathf.SmoothStep(0f, 1f, s);

            float t = Time.time + phase;
            Vector3 p = restLocalPos;
            p.y += Mathf.Sin(t * bobFrequency) * bobAmplitude * spawnAlpha;
            p.x += Mathf.Sin(t * driftFrequency * 1.7f) * driftAmplitude * spawnAlpha;
            transform.localPosition = p;

            float sy = 1f + Mathf.Sin(t * scaleFrequency) * scaleAmplitude * spawnAlpha;
            float sx = 1f - Mathf.Sin(t * scaleFrequency * 0.9f) * (scaleAmplitude * 0.6f * spawnAlpha);
            transform.localScale = new Vector3(
                restScale.x * sx * spawnMul,
                restScale.y * sy * spawnMul,
                restScale.z);

            float baseA = restColor.a * spawnAlpha;
            float a = Mathf.Clamp01(baseA + Mathf.Sin(t * flickerFrequency) * flickerAmount * spawnAlpha);
            sr.color = new Color(restColor.r, restColor.g, restColor.b, a);

            if (linkedLight != null)
            {
                float f = Mathf.Sin(t * flickerFrequency * 0.8f) * lightIntensityFlicker;
                linkedLight.intensity = (lightIntensityRest + f) * spawnAlpha;
            }
        }
    }
}
