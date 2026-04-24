using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;

namespace SlimeJumping.Visual
{
    /// <summary>
    /// Plays effects when SlimeAirDash fires: a one-shot particle burst, a trail that
    /// lingers for the dash window, a color flash on target renderers, and an optional
    /// slime SFX. Decoupled from SlimeController / SlimeVisual so toggling this
    /// component on/off flips only the dash visuals.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlimeAirDashVisual : MonoBehaviour
    {
        [SerializeField] private SlimeAirDash airDash;

        [Header("Particle burst")]
        [SerializeField] private ParticleSystem burstParticle;
        [SerializeField] private int burstCount = 18;
        [Tooltip("If true, orients the burst prefab to face the dash direction before emit.")]
        [SerializeField] private bool orientBurstToDashDirection = true;

        [Header("Trail")]
        [SerializeField] private TrailRenderer dashTrail;
        [Tooltip("Extra seconds kept beyond SlimeAirDash.DashActiveDuration for a tail fade-out.")]
        [SerializeField] private float trailTailExtension = 0.1f;

        [Header("Color flash")]
        [SerializeField] private Renderer[] flashTargets;
        [SerializeField] private string colorProperty = "_BaseColor";
        [SerializeField] private Color flashColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private float flashDuration = 0.15f;
        [Tooltip("Curve evaluated over [0,1] of flashDuration. 1 = full flash, 0 = original.")]
        [SerializeField] private AnimationCurve flashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

        [Header("Audio")]
        [SerializeField] private AudioClip dashSfx;
        [SerializeField] private SoundCategory sfxCategory = SoundCategory.SFX_Slime;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 1f;
        [SerializeField] private float sfxPitchMin = 0.95f;
        [SerializeField] private float sfxPitchMax = 1.05f;

        private MaterialPropertyBlock propertyBlock;
        private int colorPropertyId;
        private Color[] originalColors;
        private float flashElapsed = -1f;
        private float trailStopAt = -1f;

        private void Reset()
        {
            airDash = GetComponent<SlimeAirDash>();
        }

        private void Awake()
        {
            if (airDash == null) airDash = GetComponent<SlimeAirDash>();
            propertyBlock = new MaterialPropertyBlock();
            colorPropertyId = Shader.PropertyToID(colorProperty);
            CacheOriginalColors();
            if (dashTrail != null) dashTrail.emitting = false;
        }

        private void CacheOriginalColors()
        {
            if (flashTargets == null) { originalColors = null; return; }
            originalColors = new Color[flashTargets.Length];
            for (int i = 0; i < flashTargets.Length; i++)
            {
                var r = flashTargets[i];
                if (r == null) { originalColors[i] = Color.white; continue; }
                var mat = r.sharedMaterial;
                originalColors[i] = (mat != null && mat.HasProperty(colorPropertyId))
                    ? mat.GetColor(colorPropertyId)
                    : Color.white;
            }
        }

        private void OnEnable()
        {
            if (airDash != null) airDash.OnDash += HandleDash;
        }

        private void OnDisable()
        {
            if (airDash != null) airDash.OnDash -= HandleDash;
            if (dashTrail != null) dashTrail.emitting = false;
            flashElapsed = -1f;
            trailStopAt = -1f;
            ApplyFlash(0f);
        }

        private void HandleDash(Vector2 dashVelocity)
        {
            if (burstParticle != null)
            {
                if (orientBurstToDashDirection && dashVelocity.sqrMagnitude > 0.0001f)
                {
                    float deg = Mathf.Atan2(dashVelocity.y, dashVelocity.x) * Mathf.Rad2Deg;
                    burstParticle.transform.rotation = Quaternion.Euler(0f, 0f, deg);
                }
                burstParticle.Emit(burstCount);
            }

            if (dashTrail != null)
            {
                dashTrail.Clear();
                dashTrail.emitting = true;
                float window = airDash != null ? airDash.DashActiveDuration : 0.15f;
                trailStopAt = Time.time + window + Mathf.Max(0f, trailTailExtension);
            }

            flashElapsed = 0f;

            if (dashSfx != null)
            {
                float pitch = Random.Range(sfxPitchMin, sfxPitchMax);
                SoundService.PlayAt(dashSfx, transform.position, sfxCategory, sfxVolume, pitch);
            }
        }

        private void Update()
        {
            if (trailStopAt > 0f && Time.time >= trailStopAt)
            {
                if (dashTrail != null) dashTrail.emitting = false;
                trailStopAt = -1f;
            }

            if (flashElapsed >= 0f)
            {
                flashElapsed += Time.deltaTime;
                float t = flashDuration > 0.0001f ? flashElapsed / flashDuration : 1f;
                if (t >= 1f)
                {
                    ApplyFlash(0f);
                    flashElapsed = -1f;
                }
                else
                {
                    ApplyFlash(flashCurve.Evaluate(t));
                }
            }
        }

        private void ApplyFlash(float weight)
        {
            if (flashTargets == null || originalColors == null) return;
            for (int i = 0; i < flashTargets.Length; i++)
            {
                var r = flashTargets[i];
                if (r == null) continue;
                r.GetPropertyBlock(propertyBlock);
                Color mixed = Color.Lerp(originalColors[i], flashColor, weight);
                propertyBlock.SetColor(colorPropertyId, mixed);
                r.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
