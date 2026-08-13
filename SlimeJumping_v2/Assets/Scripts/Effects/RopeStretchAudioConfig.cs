using UnityEngine;

namespace SlimeJumping.Effects
{
    /// 로프 늘어남 사운드 공유 튜닝. canonical asset = Assets/Resources/RopeStretchAudioConfig.asset.
    /// 착지·발사음은 TautLine 의 Rope Surface Material 슬롯(SurfaceMaterial) 몫이고, 여기는
    /// 늘어난 정도에 따라 이어지는 소리만 다룬다.
    [CreateAssetMenu(fileName = "RopeStretchAudioConfig",
        menuName = "SlimeJumping/Rope Stretch Audio Config", order = 0)]
    public sealed class RopeStretchAudioConfig : ScriptableObject
    {
        [Tooltip("늘어나는 동안 이어지는 소리. 늘어난 깊이가 음량과 피치를 정한다.")]
        public SlimeJumping.Audio.SoundDef stretchSound;

        [Header("깊이 → 음량")]
        [Tooltip("이 깊이(m) 이하는 무음. 정지한 로프의 baseline 처짐이 계속 울지 않게 한다.")]
        [Min(0f)] public float startDepth = 0.05f;
        [Tooltip("최대 음량이 되는 깊이(m).")]
        [Min(0.01f)] public float fullDepth = 0.6f;

        [Header("깊이 → 피치")]
        public float pitchAtStart = 0.9f;
        public float pitchAtFull = 1.2f;

        const string ResourcesPath = "RopeStretchAudioConfig";

        public static RopeStretchAudioConfig Resolve(RopeStretchAudioConfig @override)
        {
            if (@override != null) return @override;
            return Resources.Load<RopeStretchAudioConfig>(ResourcesPath);
        }
    }
}
