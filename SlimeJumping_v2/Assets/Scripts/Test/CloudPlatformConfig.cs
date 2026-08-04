using UnityEngine;

namespace SlimeJumping.Platforms
{
    // 구름 점프대 공유 튜닝. canonical asset = Assets/Resources/CloudPlatformConfig.asset (자동 로드).
    // 인스턴스엔 숫자 없음, 특수 변종만 configOverride 지정 (SoftCloudConfig/ThinIceV2Config 패턴).
    [CreateAssetMenu(fileName = "CloudPlatformConfig",
        menuName = "SlimeJumping/Cloud Platform/Config", order = 0)]
    public sealed class CloudPlatformConfig : ScriptableObject
    {
        [Header("눌림 / 리바운드")]
        public float sinkDepth = 0.3f;     // 눌리는 깊이.
        public float sinkTime = 0.14f;     // 바닥까지 시간.
        public float reboundTime = 0.05f;  // 바닥에서 rest 로 돌아오는 시간. 순수 비주얼, 퓨즈와 무관.

        [Header("퓨즈")]
        public float idleFuseTime = 0.35f;   // Space 안 누른 채 버티는 시간.
        public float holdFuseTime = 0.25f;   // Space 누른 뒤 버티는 시간.
                                             // 슬라임 자체 오토런치(chargeRadius/autoDriftSpeed + chargeFullAimTime)보다
                                             // 짧아야 "계속 누름" 단계가 성립. 길면 슬라임이 먼저 발사돼 "뗌" 으로 붕괴.

        [Header("점프력 3단계")]
        public float idlePopSpeed = 4.5f;       // 안 누름 — 조준이 없으니 수직, 작게.
        public float releaseFloorSpeed = 9f;    // 뗌 — 내 차지와 max, 조준 방향 유지.
        public float holdPopSpeed = 14f;        // 계속 누름 — 조준 방향, 최대.

        [Header("발사 후 구름 상승/소멸")]
        public float riseVisualHeight = 1.1f;
        public float riseVisualTime = 0.9f;

        [Header("Respawn")]
        public bool respawn = true;
        public float respawnDelay = 1.5f;   // 소멸 후 다시 모이기 시작까지.
        public float regenTime = 0.7f;      // 구름이 중심에서 부풀어 오르는 시간. 이 동안 콜라이더 off.
        public LayerMask regenBlockLayers = 1;   // 겹쳐 있으면 재생성 보류(Default = 슬라임).

        [Header("조각 팝 (disperse)")]
        public float spread = 1.0f;       // 바깥으로 튀는 거리.
        public float rise = 0.9f;         // 위로.
        public float life = 0.42f;        // 조각 수명(짧고 snappy).
        [Range(0f, 0.5f)] public float stagger = 0.06f; // 조각별 시작 지연 폭.
        public float spin = 220f;         // deg/s 빠른 회전.
        [Range(1f, 1.6f)] public float popScale = 1.25f; // 초반 살짝 부풀기.


        const string ResourcesPath = "CloudPlatformConfig";
        static CloudPlatformConfig fallback;

        public static CloudPlatformConfig Resolve(CloudPlatformConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<CloudPlatformConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<CloudPlatformConfig>();
                Debug.LogWarning("CloudPlatformConfig: no Assets/Resources/CloudPlatformConfig.asset found — " +
                                 "running on built-in defaults. Create one via Assets > Create > SlimeJumping > Cloud Platform > Config.");
            }
            return fallback;
        }
    }
}
