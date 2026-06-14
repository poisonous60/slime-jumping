using UnityEngine;

namespace SlimeJumping.Platforms.Piano
{
    /// Single tuning asset for a whole piano — keys, keyboard detection, and pedals. Referenced by
    /// PianoKeyboard and the PianoPedal motors so one .asset drives the entire instrument (mirrors
    /// the project's other platform config assets, e.g. BambooV2Settings / SoundSettings).
    [CreateAssetMenu(fileName = "PianoConfig", menuName = "SlimeJumping/Piano Config")]
    public class PianoConfig : ScriptableObject
    {
        [Header("Keys — dynamics & feel")]
        public PianoSettings keys = new PianoSettings();

        [Header("Keyboard — multi-key detection")]
        [Tooltip("Min horizontal overlap (world units) between slime and a key to press it. Lower = easier multi-key.")]
        public float overlapThreshold = 0.06f;
        [Tooltip("Max vertical gap (world units) between the slime's underside and a key top to count as resting.")]
        public float verticalTolerance = 0.14f;

        [Header("Pedals — press feel")]
        [Tooltip("Downward sink at full press (world units). This carries the slime down.")]
        public float pedalMaxDip = 0.1f;
        [Tooltip("Cosmetic X tilt at full press (degrees, into the screen).")]
        public float pedalMaxAngle = 7f;
        [Min(0f)] public float pedalSpringStiffness = 120f;
        [Min(0f)] public float pedalSpringDamping = 11f;

        [Header("Pedals — sound")]
        [Tooltip("Played once when the slime steps onto a pedal.")]
        public AudioClip pedalPressSound;
        [Tooltip("Master volume multiplier on top of the velocity curve.")]
        [Range(0f, 1f)] public float pedalSoundVolume = 1f;
        [Tooltip("Slime impact-speed range (world u/s) mapped to the volume curve's 0..1 input.")]
        public float pedalMinImpactSpeed = 1f;
        public float pedalMaxImpactSpeed = 25f;
        [Tooltip("Press speed (normalized 0..1) -> sound volume (0..1). 밟는 속도별 소리 크기.")]
        public AnimationCurve pedalVelocityToVolume = AnimationCurve.Linear(0f, 0.3f, 1f, 1f);

        /// 0..1 position of a pedal impact speed inside [pedalMinImpactSpeed, pedalMaxImpactSpeed].
        public float PedalNormalizedImpact(float speed)
            => Mathf.Clamp01(Mathf.InverseLerp(pedalMinImpactSpeed, pedalMaxImpactSpeed, speed));
    }
}
