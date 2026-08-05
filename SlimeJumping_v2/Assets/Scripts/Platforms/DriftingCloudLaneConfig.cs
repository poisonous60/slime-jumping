using UnityEngine;

namespace SlimeJumping.Platforms
{
    // 흘러가는 구름 레인 공유 튜닝. canonical asset = Assets/Resources/DriftingCloudLaneConfig.asset.
    // 인스턴스엔 숫자 없음, 특수 변종만 configOverride 지정 (CloudPlatformConfig 패턴).
    [CreateAssetMenu(fileName = "DriftingCloudLaneConfig",
        menuName = "SlimeJumping/Cloud Platform/Drifting Lane Config", order = 1)]
    public sealed class DriftingCloudLaneConfig : ScriptableObject
    {
        [Header("밴드")]
        public Vector2 bandSize = new Vector2(18f, 11f);   // 가로가 곧 순환 주기 거리.

        [Header("생성 / 소멸")]
        public float spawnFadeTime = 1.25f;     // 왼쪽 경계에서 자라나는 시간(초).
        public float despawnFadeTime = 1.25f;   // 오른쪽 경계에서 줄어드는 시간(초).

        const string ResourcesPath = "DriftingCloudLaneConfig";
        static DriftingCloudLaneConfig fallback;

        public static DriftingCloudLaneConfig Resolve(DriftingCloudLaneConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<DriftingCloudLaneConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<DriftingCloudLaneConfig>();
                Debug.LogWarning("DriftingCloudLaneConfig: no Assets/Resources/DriftingCloudLaneConfig.asset found — " +
                                 "running on built-in defaults. Create one via Assets > Create > SlimeJumping > Cloud Platform > Drifting Lane Config.");
            }
            return fallback;
        }
    }
}
