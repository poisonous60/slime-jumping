using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    // 점 중력원 공유 튜닝. canonical asset = Assets/Resources/GravityPointConfig.asset (자동 로드).
    // 인스턴스엔 숫자 없음, 반경/세기가 다른 변종만 configOverride 지정 (SquishyCloudConfig 패턴).
    [CreateAssetMenu(fileName = "GravityPointConfig",
        menuName = "SlimeJumping/Gravity Field V2/Point Config", order = 0)]
    public sealed class GravityPointConfig : ScriptableObject
    {
        [Tooltip("영향 반경 (월드 단위). 반경 밖은 정확히 0.")]
        [Min(0.01f)] public float radius = 6f;

        [Tooltip("힘 크기 상한. forceProfile y=1 지점에서 이 값.")]
        [Min(0f)] public float peakAccel = 20f;

        [Tooltip("반경 내 힘 분포. x = 중심(0)→반경(1), y = peakAccel 배율 0..1.")]
        public AnimationCurve forceProfile = DefaultProfile();

        // Wendland 모양 근사: 중심 0 → 0.25R 최대 → 경계 0 (양 끝·피크 평탄 = C1)
        public static AnimationCurve DefaultProfile()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(0.25f, 1f, 0f, 0f),
                new Keyframe(1f, 0f, 0f, 0f));
        }

        const string ResourcesPath = "GravityPointConfig";
        static GravityPointConfig fallback;

        public static GravityPointConfig Resolve(GravityPointConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<GravityPointConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<GravityPointConfig>();
                Debug.LogWarning("GravityPointConfig: no Assets/Resources/GravityPointConfig.asset found — " +
                                 "running on built-in defaults. Create one via Assets > Create > SlimeJumping > Gravity Field V2 > Point Config.");
            }
            return fallback;
        }
    }
}
