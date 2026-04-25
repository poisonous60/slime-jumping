using UnityEngine;
using SlimeJumping.Audio;
using SlimeJumping.Controller;
using SlimeJumping.Effects;

namespace SlimeJumping.Visual
{
    /// <summary>
    /// Celeste-style dash feedback for SlimeAirDash. Wires three independent feel channels:
    /// 1) VFX prefab spawn at dash start (mirrors LandingDustSpawner pattern)
    /// 2) HitStop freeze + camera impulse via HitStopService
    /// 3) Dash-availability color shift via MaterialPropertyBlock (red = ready, grey = spent)
    /// Each channel is optional — drop a field and that channel no-ops.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public class SlimeAirDashVisual : MonoBehaviour
    {
        [SerializeField] private SlimeAirDash airDash;

        [Header("VFX")]
        [Tooltip("Prefab instantiated on dash. Rotated so local +X points along the dash direction. " +
                 "If Parent To Slime is true, it follows the slime; otherwise it stays at spawn point.")]
        [SerializeField] private GameObject dashVfxPrefab;
        [SerializeField] private bool parentToSlime = false;
        [Tooltip("Seconds before the spawned VFX is destroyed. 0 = never destroy (rely on prefab's StopAction).")]
        [SerializeField] private float vfxLifetime = 0.6f;

        [Header("Hit Stop")]
        [Tooltip("Duration in realtime seconds that Time.timeScale is dropped on dash start. 0 = skip freeze.")]
        [SerializeField] private float freezeDuration = 0.05f;
        [SerializeField, Range(0f, 0.3f)] private float freezeScale = 0f;
        [Tooltip("Optional camera impulse emitter triggered alongside the freeze.")]
        [SerializeField] private ImpulseEmitter cameraImpulse;
        [SerializeField] private float impulseForce = 0.4f;

        [Header("Color Feedback (dashAvailable)")]
        [Tooltip("Renderers tinted to signal dash resource state. Uses MaterialPropertyBlock, no material instantiation.")]
        [SerializeField] private Renderer[] colorTargets;
        [SerializeField] private string colorProperty = "_BaseColor";
        [SerializeField] private Color availableColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color exhaustedColor = new Color(0.55f, 0.55f, 0.65f, 1f);
        [SerializeField] private Color flashColor = new Color(1f, 1f, 1f, 1f);
        [Tooltip("Seconds the flashColor is held at full strength after a dash before fading back to availability tint.")]
        [SerializeField] private float flashDuration = 0.12f;

        [Header("SFX")]
        [SerializeField] private AudioClip dashSfx;
        [SerializeField] private SoundCategory sfxCategory = SoundCategory.SFX_Slime;
        [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.9f;
        [SerializeField, Range(0f, 0.3f)] private float sfxPitchJitter = 0.06f;

        private MaterialPropertyBlock mpb;
        private int colorPropertyId;
        private float flashTimer;

        private void Reset()
        {
            airDash = GetComponent<SlimeAirDash>();
        }

        private void Awake()
        {
            if (airDash == null) airDash = GetComponent<SlimeAirDash>();
            mpb = new MaterialPropertyBlock();
            colorPropertyId = Shader.PropertyToID(colorProperty);
        }

        private void OnEnable()
        {
            if (airDash != null) airDash.OnDash += HandleDash;
        }

        private void OnDisable()
        {
            if (airDash != null) airDash.OnDash -= HandleDash;
            flashTimer = 0f;
            ApplyColor(availableColor);
        }

        private void HandleDash(Vector2 dashVelocity)
        {
            Vector3 pos = airDash != null && airDash.SlimeController != null
                ? (Vector3)airDash.SlimeController.Position
                : transform.position;

            if (dashVfxPrefab != null)
            {
                Quaternion rot = Quaternion.identity;
                if (dashVelocity.sqrMagnitude > 0.0001f)
                {
                    float deg = Mathf.Atan2(dashVelocity.y, dashVelocity.x) * Mathf.Rad2Deg;
                    rot = Quaternion.Euler(0f, 0f, deg);
                }

                Transform parent = parentToSlime ? transform : null;
                var go = Instantiate(dashVfxPrefab, pos, rot, parent);
                var ps = go.GetComponent<ParticleSystem>();
                if (ps != null) ps.Play();
                if (vfxLifetime > 0f) Destroy(go, vfxLifetime);
            }

            if (freezeDuration > 0f || (cameraImpulse != null && impulseForce > 0f))
            {
                HitStopService.Freeze(freezeDuration, cameraImpulse, impulseForce, freezeScale);
            }

            if (dashSfx != null)
            {
                float pitch = 1f + Random.Range(-sfxPitchJitter, sfxPitchJitter);
                SoundService.PlayAt(dashSfx, pos, sfxCategory, sfxVolume, pitch);
            }

            flashTimer = flashDuration;
        }

        private void LateUpdate()
        {
            if (colorTargets == null || colorTargets.Length == 0) return;

            Color baseTint = (airDash != null && airDash.DashAvailable) ? availableColor : exhaustedColor;
            Color current = baseTint;
            if (flashTimer > 0f)
            {
                flashTimer -= Time.unscaledDeltaTime;
                float t = flashDuration > 0.0001f ? Mathf.Clamp01(flashTimer / flashDuration) : 0f;
                current = Color.Lerp(baseTint, flashColor, t);
            }

            ApplyColor(current);
        }

        private void ApplyColor(Color c)
        {
            if (colorTargets == null) return;
            for (int i = 0; i < colorTargets.Length; i++)
            {
                var r = colorTargets[i];
                if (r == null) continue;
                r.GetPropertyBlock(mpb);
                mpb.SetColor(colorPropertyId, c);
                r.SetPropertyBlock(mpb);
            }
        }
    }
}
