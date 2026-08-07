using UnityEngine;

namespace SlimeJumping.Platforms
{
    // 말랑 구름 공유 튜닝. canonical asset = Assets/Resources/SquishyCloudConfig.asset (자동 로드).
    // 인스턴스엔 숫자 없음, 특수 변종만 configOverride 지정 (CloudPlatformConfig 패턴).
    // 깊이류 값은 전부 "구름 높이 비율" — 크기가 다른 구름에도 같은 에셋이 먹는다.
    [CreateAssetMenu(fileName = "SquishyCloudConfig",
        menuName = "SlimeJumping/Squishy Cloud/Config", order = 0)]
    public sealed class SquishyCloudConfig : ScriptableObject
    {
        [Header("전신 압축 (Puffy Lift식 지속 하중 + Celeste식 착지 임펄스)")]
        public float loadCompression = 0.08f;            // 탑승 중 유지되는 눌림.
        public float impactCompressionPerSpeed = 0.015f; // 착지 속도 1u/s 당 추가 눌림.
        public float impactCompressionMax = 0.25f;       // 전신 눌림 상한.
        public float bodyFrequency = 2.2f;               // 복귀 스프링 Hz.
        [Range(0.05f, 1f)] public float bodyDamping = 0.3f; // <1 = 이탈 후 오버슈트 잔동.
        public float bulgeRatio = 0.6f;                  // 눌린 만큼 옆구리가 부푸는 비율.

        [Header("국소 파동 (발밑 dent 가 실루엣을 타고 퍼짐)")]
        public float dentDepth = 0.12f;          // 착지 발자국 깊이.
        public float dentDepthPerSpeed = 0.008f; // 착지 속도 1u/s 당 추가 깊이.
        public float dentRadius = 1.6f;          // 발자국 반경(슬라임 반지름 배수).
        public float restDentDepth = 0.05f;      // 탑승 중 발밑에 유지되는 눌림.
        public float rollWavePerSpeed = 0.15f;   // 탑승 이동 속도 비례 트레일 물결 세기.
        public float popWaveDepth = 0.08f;       // 팝 순간 바깥으로 퍼지는 물결.
        public float waveSpread = 80f;           // 이웃 전파 — 클수록 물결이 빨리 번짐.
        public float waveSpring = 35f;           // 원위치 복원력.
        public float waveDamping = 5f;           // 감쇠 — 작을수록 오래 출렁.

        [Header("정점 매핑 / 한계")]
        public float visualInfluenceRadius = 0.2f; // 윤곽 영향 반경(world). 구름 반두께 이하로.
        public int mappingArcWindow = 8;           // 정점이 참조하는 윤곽 arc 폭(±점 개수). 다른 lobe 오염 차단.
        public float maxTotalDisplacement = 0.45f; // 정점당 합성 변위 상한.
        public float maxSurfaceSpeed = 3f;         // 표면 최대 이동 속도(world u/s) — probe 가 추적 가능해야 함.

        [Header("Perf")]
        public float restEpsilon = 0.0015f;   // 이하 진폭이면 완전 휴면.

        const string ResourcesPath = "SquishyCloudConfig";
        static SquishyCloudConfig fallback;

        public static SquishyCloudConfig Resolve(SquishyCloudConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<SquishyCloudConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<SquishyCloudConfig>();
                Debug.LogWarning("SquishyCloudConfig: no Assets/Resources/SquishyCloudConfig.asset found — " +
                                 "running on built-in defaults. Create one via Assets > Create > SlimeJumping > Squishy Cloud > Config.");
            }
            return fallback;
        }
    }
}
