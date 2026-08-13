using UnityEngine;

namespace SlimeJumping.Visual
{
    // 배경 고래 공유 튜닝. canonical asset = Assets/Resources/BackgroundWhaleConfig.asset (자동 로드).
    [CreateAssetMenu(fileName = "BackgroundWhaleConfig",
        menuName = "SlimeJumping/Visual/Background Whale Config", order = 0)]
    public sealed class BackgroundWhaleConfig : ScriptableObject
    {
        [Tooltip("비행 속도 (월드 유닛/초). 진행 방향은 인스턴스의 바라보는 방향.")]
        [Min(0f)] public float flySpeed = 2.5f;

        [Tooltip("왕복 없이 순환하는 비행 구간 폭 (월드 유닛). 끝에 닿으면 반대쪽에서 다시 나온다.")]
        [Min(1f)] public float travelSpan = 120f;

        [Tooltip("상하 출렁임 진폭 (월드 유닛).")]
        [Min(0f)] public float bobAmplitude = 1.5f;

        [Tooltip("상하 출렁임 주기 (사이클/초).")]
        [Min(0f)] public float bobFrequency = 0.08f;

        [Tooltip("Swim 애니메이션 재생 배속.")]
        [Min(0f)] public float swimAnimSpeed = 0.5f;

        [Tooltip("첫 추종자가 고래 중심에서 뒤처지는 경로 거리 (월드 유닛).")]
        [Min(0f)] public float followStartOffset = 15f;

        [Tooltip("추종자 사이 경로 간격 (월드 유닛).")]
        [Min(0f)] public float followSpacing = 5f;

        [Tooltip("추종자 Swim 애니메이션 재생 배속.")]
        [Min(0f)] public float followerSwimAnimSpeed = 1f;

        [Tooltip("회전 방향을 뽑는 경로 전후 표본 거리 (월드 유닛). 클수록 완만하게 선회.")]
        [Min(0.1f)] public float turnLookAhead = 6f;

        const string ResourcesPath = "BackgroundWhaleConfig";
        static BackgroundWhaleConfig fallback;

        public static BackgroundWhaleConfig Resolve(BackgroundWhaleConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<BackgroundWhaleConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null) fallback = CreateInstance<BackgroundWhaleConfig>();
            return fallback;
        }
    }
}
