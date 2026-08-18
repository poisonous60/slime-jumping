using UnityEngine;

namespace SlimeJumping.Controller
{
    /// Shared VoidRespawn tuning; canonical asset <c>Assets/Resources/VoidRespawnConfig.asset</c>
    /// (auto-loaded). Kill height and respawn spot stay on the component — they are per-scene geometry.
    [CreateAssetMenu(fileName = "VoidRespawnConfig",
        menuName = "SlimeJumping/Void Respawn Config", order = 0)]
    public sealed class VoidRespawnConfig : ScriptableObject
    {
        [Tooltip("Seconds the screen takes to go fully black once the slime drops past the kill height.")]
        [Min(0f)] public float fadeOutSeconds = 1.2f;
        [Tooltip("Seconds the screen takes to clear again after the slime is placed at the respawn point.")]
        [Min(0f)] public float fadeInSeconds = 0.8f;

        [Header("Shape (x = 진행도 0~1, y = 검은 정도 0~1)")]
        [Tooltip("어두워지는 곡선. 뒤로 눕히면 한참 밝다가 마지막에 훅 꺼지고, 앞으로 세우면 곧바로 어두워진다.")]
        public AnimationCurve fadeOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("밝아지는 곡선. y 1 = 검정, 0 = 화면 그대로.")]
        public AnimationCurve fadeInCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);

        const string ResourcesPath = "VoidRespawnConfig";

        /// Override if assigned, else the Resources asset, else null — callers fall back to class defaults.
        public static VoidRespawnConfig Resolve(VoidRespawnConfig @override)
        {
            if (@override != null) return @override;
            return Resources.Load<VoidRespawnConfig>(ResourcesPath);
        }
    }
}
