using UnityEngine;

namespace SlimeJumping.VFX
{
    // 바위 각인 공유 튜닝. canonical asset = Assets/Resources/RockEngravingConfig.asset (자동 로드).
    [CreateAssetMenu(fileName = "RockEngravingConfig",
        menuName = "SlimeJumping/VFX/Rock Engraving Config", order = 0)]
    public sealed class RockEngravingConfig : ScriptableObject
    {
        [Tooltip("마스크 RT 가로 해상도. 세로는 라벨 비율을 따라간다.")]
        [Min(64)] public int maskWidth = 512;

        [Tooltip("글자 가장자리 부드러움 (SDF 임계 폭). 클수록 파인 벽이 넓고 완만해진다.")]
        [Range(0.005f, 0.3f)] public float edgeSoftness = 0.14f;

        [Tooltip("파인 벽의 노멀 기울기 세기 — 클수록 깊게 파인 듯 음영이 진해진다.")]
        [Min(0f)] public float carveDepth = 10f;

        [Tooltip("파인 바닥을 어둡게 하는 양 0..1.")]
        [Range(0f, 1f)] public float floorDarken = 0.6f;

        [Tooltip("위쪽 벽이 바닥에 드리우는 내부 그림자 세기 0..1. 깊이 체감의 핵심.")]
        [Range(0f, 1f)] public float innerShadow = 0.55f;

        [Tooltip("내부 그림자가 위 모서리에서 드리우는 길이 (월드 단위).")]
        [Min(0f)] public float innerShadowLength = 0.06f;

        [Tooltip("라벨 평면에서 앞뒤로 각인이 닿는 거리 (월드 단위).")]
        [Min(0.01f)] public float projectionDepth = 1.5f;

        [Tooltip("마스크 굽기 셰이더. 에셋 참조라야 빌드에 포함된다 — Shader.Find 는 빌드에서 스트립돼 null.")]
        public Shader maskShader;

        const string ResourcesPath = "RockEngravingConfig";
        static RockEngravingConfig fallback;

        public static RockEngravingConfig Resolve(RockEngravingConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<RockEngravingConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null) fallback = CreateInstance<RockEngravingConfig>();
            return fallback;
        }
    }
}
