using UnityEngine;

namespace SlimeJumping.Rendering
{
    /// <summary>
    /// base material 을 runtime clone 으로 교체하고 per-instance 텍스처/컬러/키워드 push.
    /// 원본 .mat 자산은 read-only 유지. SnowField 와 `_GradientRamp` 공유하면 그늘 톤 통일.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(Renderer))]
    public class SnowballMaterialBinder : MonoBehaviour
    {
        [Tooltip("템플릿 머티리얼 — Snowball_Quibli.shader 사용. 자산 자체는 수정 안 됨.")]
        [SerializeField] Material baseMaterial;

        [Header("Tone (SnowField 와 동일한 ramp 공유 권장)")]
        [Tooltip("Quibli toon ramp 텍스처. blue→white 그라데이션을 SnowField 와 공유하면 자연스러운 일치.")]
        [SerializeField] Texture2D shadowRamp;
        [Tooltip("Quibli normal map. 노이즈 노멀맵으로 surface granularity (눈 알갱이 느낌).")]
        [SerializeField] Texture2D bumpMap;
        [Tooltip("순백 권장 — 컬러 톤은 shadowRamp 가 결정.")]
        [SerializeField] Color baseColor = Color.white;

        [Header("Fresnel rim — subtle 권장")]
        [SerializeField] bool fresnelEnabled = true;
        [ColorUsage(true, true)]
        [SerializeField] Color fresnelColor = new Color(0.7f, 0.85f, 1.0f, 1f);

        [Header("Glitter sparkle — 카툰 룩에선 OFF 권장")]
        [SerializeField] bool glitterEnabled = false;
        [SerializeField] Texture2D glitterNoise;
        [ColorUsage(true, true)]
        [SerializeField] Color glitterColor = new Color(0.7f, 0.85f, 1.0f, 1f);

        Renderer cachedRenderer;
        Material runtimeMaterial;

        static readonly int IdGradientRamp        = Shader.PropertyToID("_GradientRamp");
        static readonly int IdBumpMap             = Shader.PropertyToID("_BumpMap");
        static readonly int IdBaseColor           = Shader.PropertyToID("_BaseColor");
        static readonly int IdFresnelColor        = Shader.PropertyToID("_SnowballFresnelColor");
        static readonly int IdGlitterNoise        = Shader.PropertyToID("_SnowballGlitterNoise");
        static readonly int IdGlitterColor        = Shader.PropertyToID("_SnowballGlitterColor");
        const string KwFresnel   = "_SNOWBALL_FRESNEL";
        const string KwGlitter   = "_SNOWBALL_GLITTER";
        const string KwNormalMap = "_NORMALMAP";

        void OnEnable()  { Rebuild(); }
        void OnDisable() { Dispose(); }
        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            Rebuild();
        }

        void Rebuild()
        {
            Dispose();
            if (baseMaterial == null) return;
            if (cachedRenderer == null) cachedRenderer = GetComponent<Renderer>();
            if (cachedRenderer == null) return;

            runtimeMaterial = new Material(baseMaterial)
            {
                name = baseMaterial.name + " (Snowball Runtime)",
                hideFlags = HideFlags.DontSave
            };

            if (shadowRamp   != null) runtimeMaterial.SetTexture(IdGradientRamp, shadowRamp);
            if (glitterNoise != null) runtimeMaterial.SetTexture(IdGlitterNoise, glitterNoise);
            if (bumpMap      != null)
            {
                runtimeMaterial.SetTexture(IdBumpMap, bumpMap);
                runtimeMaterial.EnableKeyword(KwNormalMap);
            }
            else
            {
                runtimeMaterial.DisableKeyword(KwNormalMap);
            }
            runtimeMaterial.SetColor(IdBaseColor,    baseColor);
            runtimeMaterial.SetColor(IdFresnelColor, fresnelColor);
            runtimeMaterial.SetColor(IdGlitterColor, glitterColor);

            ToggleKeyword(runtimeMaterial, KwFresnel, fresnelEnabled);
            ToggleKeyword(runtimeMaterial, KwGlitter, glitterEnabled);

            cachedRenderer.sharedMaterial = runtimeMaterial;
        }

        void Dispose()
        {
            if (runtimeMaterial == null) return;
            if (Application.isPlaying) Destroy(runtimeMaterial);
            else                       DestroyImmediate(runtimeMaterial);
            runtimeMaterial = null;
        }

        static void ToggleKeyword(Material m, string kw, bool on)
        {
            if (on) m.EnableKeyword(kw);
            else    m.DisableKeyword(kw);
        }
    }
}
