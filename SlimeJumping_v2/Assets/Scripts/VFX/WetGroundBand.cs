using UnityEngine;

namespace SlimeJumping.VFX
{
    /// <summary>
    /// Drives the wet-ground shader from the rain's own altitude band, so the floor is wet exactly where
    /// rain falls and dries as the band fades out.
    ///
    /// Every knob is pushed as a global shader value rather than living on the material: Quibli's
    /// UnityPerMaterial CBUFFER is third-party code we do not edit, and a material property declared
    /// outside it would drop the floor's 51 renderers off the SRP Batcher fast path. That also makes this
    /// component the single tuning surface — the material inspector shows no wet knobs at all.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("SlimeJumping/VFX/Wet Ground Band")]
    public sealed class WetGroundBand : MonoBehaviour
    {
        [SerializeField] private RainCameraEmitter rain;

        [Header("Textures")]
        [SerializeField] private Texture rippleNormal;

        [Header("Wetness")]
        [Tooltip("Wetness at the centre of the rain band.")]
        [Range(0f, 1f)][SerializeField] private float maxWetness = 1f;

        [Tooltip("Seconds for the ground to soak or dry, so crossing the band edge is not a step.")]
        [Min(0f)][SerializeField] private float soakSeconds = 3f;

        [Tooltip("How dark a fully wet surface gets. Lagarde's porous-surface value is 0.2; higher keeps the trail readable.")]
        [Range(0.1f, 1f)][SerializeField] private float darkenFloor = 0.45f;

        [Header("Surface Mask")]
        [Tooltip("Surface normal Y where wetness starts. Water pools on tops, not walls.")]
        [Range(-1f, 1f)][SerializeField] private float upFacingThreshold = 0.35f;

        [Range(0.01f, 1f)][SerializeField] private float upFacingSoftness = 0.35f;

        [Header("Ripples")]
        [Tooltip("Ripple pattern repeats per world unit. The floors are ~2.6 units deep in Z, so a period longer than that shows no variation across them.")]
        [Min(0.001f)][SerializeField] private float rippleTiling = 3f;
        [SerializeField] private float rippleSpeed = 0.35f;
        [Range(0f, 2f)][SerializeField] private float rippleStrength = 0.6f;

        [Header("Reflection")]
        [Tooltip("The baked probe is a night construction site (mean luminance 0.12), so this needs headroom to read.")]
        [Range(0f, 4f)][SerializeField] private float reflectionStrength = 2.5f;

        [Range(0f, 0.5f)][SerializeField] private float waterRoughness = 0.05f;
        [Range(1f, 8f)][SerializeField] private float fresnelPower = 5f;

        private static readonly int WetAmountId = Shader.PropertyToID("_Stage3WetAmount");
        private static readonly int RippleNormalId = Shader.PropertyToID("_WetRippleNormal");
        private static readonly int DarkenFloorId = Shader.PropertyToID("_WetDarkenFloor");
        private static readonly int UpFacingThresholdId = Shader.PropertyToID("_WetUpFacingThreshold");
        private static readonly int UpFacingSoftnessId = Shader.PropertyToID("_WetUpFacingSoftness");
        private static readonly int RippleTilingId = Shader.PropertyToID("_WetRippleTiling");
        private static readonly int RippleSpeedId = Shader.PropertyToID("_WetRippleSpeed");
        private static readonly int RippleStrengthId = Shader.PropertyToID("_WetRippleStrength");
        private static readonly int ReflectionStrengthId = Shader.PropertyToID("_WetReflectionStrength");
        private static readonly int RoughnessId = Shader.PropertyToID("_WetRoughness");
        private static readonly int FresnelPowerId = Shader.PropertyToID("_WetFresnelPower");

        private float current;
        private float velocity;

        private void OnEnable()
        {
            current = 0f;
            velocity = 0f;
            Shader.SetGlobalFloat(WetAmountId, 0f);
            PushLook();
        }

        // Cleared on disable, else the last value sticks globally and dry scenes stay wet.
        private void OnDisable()
        {
            Shader.SetGlobalFloat(WetAmountId, 0f);
        }

        private void OnValidate()
        {
            PushLook();
        }

        private void Update()
        {
            float target = rain != null ? rain.BandFactor * maxWetness : 0f;
            current = soakSeconds > 0f && Application.isPlaying
                ? Mathf.SmoothDamp(current, target, ref velocity, soakSeconds)
                : target;
            Shader.SetGlobalFloat(WetAmountId, current);
        }

        private void PushLook()
        {
            if (rippleNormal != null) Shader.SetGlobalTexture(RippleNormalId, rippleNormal);
            Shader.SetGlobalFloat(DarkenFloorId, darkenFloor);
            Shader.SetGlobalFloat(UpFacingThresholdId, upFacingThreshold);
            Shader.SetGlobalFloat(UpFacingSoftnessId, upFacingSoftness);
            Shader.SetGlobalFloat(RippleTilingId, rippleTiling);
            Shader.SetGlobalFloat(RippleSpeedId, rippleSpeed);
            Shader.SetGlobalFloat(RippleStrengthId, rippleStrength);
            Shader.SetGlobalFloat(ReflectionStrengthId, reflectionStrength);
            Shader.SetGlobalFloat(RoughnessId, waterRoughness);
            Shader.SetGlobalFloat(FresnelPowerId, fresnelPower);
        }
    }
}
