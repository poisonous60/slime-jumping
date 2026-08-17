using UnityEngine;

namespace SlimeJumping.Audio
{
    // 풀 착지음 공유 튜닝. canonical asset = Assets/Resources/GrassFootstepConfig.asset (자동 로드).
    [CreateAssetMenu(fileName = "GrassFootstepConfig",
        menuName = "SlimeJumping/Grass Footstep/Config", order = 0)]
    public sealed class GrassFootstepConfig : ScriptableObject
    {
        [Tooltip("풀 위 지면에 착지했을 때 겹쳐 나는 소리. 지면 자체의 착지음은 SurfaceMaterial 몫.")]
        public SoundDef landSound;

        [Tooltip("풀 렌더러 바운즈 밖으로 넓히는 가로 여유(m).")]
        public float padX = 0.3f;
        [Tooltip("세로 여유(m). 슬라임 중심이 바운즈 세로 범위 + 이만큼 안이어야 판정.")]
        public float padY = 0.8f;
        [Range(0f, 2f)] public float volumeScale = 1f;

        const string ResourcesPath = "GrassFootstepConfig";
        static GrassFootstepConfig fallback;

        public static GrassFootstepConfig Resolve(GrassFootstepConfig @override)
        {
            if (@override != null) return @override;

            var asset = Resources.Load<GrassFootstepConfig>(ResourcesPath);
            if (asset != null) return asset;

            if (fallback == null)
            {
                fallback = CreateInstance<GrassFootstepConfig>();
                Debug.LogWarning("GrassFootstepConfig: no Assets/Resources/GrassFootstepConfig.asset found — " +
                                 "running on built-in defaults. Create one via Assets > Create > SlimeJumping > Grass Footstep > Config.");
            }
            return fallback;
        }
    }
}
