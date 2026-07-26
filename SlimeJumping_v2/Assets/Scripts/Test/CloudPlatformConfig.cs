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
        public float sinkTime = 0.14f;     // 바닥까지 시간. 퍼펙트 점프 타이밍 = 이 순간.
        public float launchDelay = 0.05f;  // 바닥 후 발사까지(리바운드).

        [Header("발사 (타이밍)")]
        public float baseHopSpeed = 4.5f;      // 점프 안 누르면.
        public float maxLaunchSpeed = 14f;     // 완벽 타이밍.
        public float timingWindow = 0.16f;     // 이상 시점 ± 관대함(초).
        public KeyCode jumpKey = KeyCode.Space;

        [Header("발사 후 구름 상승/소멸")]
        public float riseVisualHeight = 1.1f;
        public float riseVisualTime = 0.9f;

        [Header("Respawn")]
        public bool respawn = true;
        public float respawnDelay = 1.5f;

        [Header("조각 팝 (disperse)")]
        public float spread = 1.0f;       // 바깥으로 튀는 거리.
        public float rise = 0.9f;         // 위로.
        public float life = 0.42f;        // 조각 수명(짧고 snappy).
        [Range(0f, 0.5f)] public float stagger = 0.06f; // 조각별 시작 지연 폭.
        public float spin = 220f;         // deg/s 빠른 회전.
        [Range(1f, 1.6f)] public float popScale = 1.25f; // 초반 살짝 부풀기.

        // ---------------------------------------------------------------------

        const string ResourcesPath = "CloudPlatformConfig";
        static CloudPlatformConfig fallback;

        // override 지정 시 그걸, 아니면 Assets/Resources/CloudPlatformConfig.asset,
        // 둘 다 없으면 위 기본값의 공유 인스턴스(새 씬에서도 동작하게).
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
