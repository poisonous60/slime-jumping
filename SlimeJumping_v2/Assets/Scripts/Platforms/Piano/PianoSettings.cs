using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// Tunable feel/dynamics for a PianoKeyboard. Shown inline on the keyboard component
    /// so designers can shape the velocity->loudness and velocity->depth response with the
    /// inspector curve editor, dial the return-spring "stiffness" (뻑뻑함), the sustained held
    /// depth while the slime rests on a key, and the note's damper fade-out on release.
    ///
    /// Depths are in WORLD units. The key is a PlatformMotor that sinks by this much and carries
    /// the slime down with it (keep depths small relative to a key's world width).
    [System.Serializable]
    public class PianoSettings
    {
        [Header("Impact velocity range (world units/sec)")]
        [Tooltip("Slime speed that maps to curve input 0.")]
        public float minImpactSpeed = 1f;
        [Tooltip("Slime speed that maps to curve input 1 (faster is clamped).")]
        public float maxImpactSpeed = 25f;

        [Header("Dynamics curves  (x = normalized impact 0..1)")]
        [Tooltip("velocity -> note volume. 셈여림(강약).")]
        public AnimationCurve velocityToVolume = AnimationCurve.Linear(0f, 0.3f, 1f, 1f);
        [Tooltip("velocity -> STRIKE dip factor (0..1, scaled by Strike Depth). 눌리는 정도(타격 순간).")]
        public AnimationCurve velocityToDepth = AnimationCurve.Linear(0f, 0.4f, 1f, 1f);

        [Header("Key travel (world units — the key sinks this far and carries the slime)")]
        [Tooltip("Sink depth at a full-speed strike (the velocity curve scales toward this). 세게 밟을 때 깊이.")]
        public float strikeDepth = 0.15f;
        [Tooltip("Minimum sink held while the slime rests ON the key. 슬라임 올라가 있을 때 최소 눌림 깊이.")]
        public float holdDepth = 0.09f;

        [Header("Return spring (피아노 뻑뻑함 / 출렁임)")]
        [Tooltip("Higher = snappier, stiffer return. 돌아오는 속도.")]
        [Min(0f)] public float springStiffness = 120f;
        [Tooltip("Higher = settles fast (stiff). Lower = bouncy wobble (출렁이는 느낌).")]
        [Min(0f)] public float springDamping = 12f;

        [Header("Audio")]
        [Range(0f, 1f)] public float masterVolume = 1f;
        [Tooltip("Seconds to fade the note out when the slime leaves (damper release). 건반 뗄 때 음이 사라지는 시간.")]
        [Min(0.01f)] public float releaseFade = 0.35f;

        /// 0..1 position of an impact speed inside [minImpactSpeed, maxImpactSpeed].
        public float NormalizedImpact(float speed)
            => Mathf.Clamp01(Mathf.InverseLerp(minImpactSpeed, maxImpactSpeed, speed));
    }
}
