using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>Tuning preset for <see cref="IcicleHazard"/>; assigning one OVERWRITES the inline inspector values on OnValidate + Awake.</summary>
    /// Audio included: IcicleHazard (-100) applies before IcicleAudioManager (-50), so a scene manager's broadcast wins.
    [CreateAssetMenu(fileName = "IcicleHazardPreset",
        menuName = "SlimeJumping/Icicle/Hazard Preset", order = 0)]
    public class IcicleHazardPreset : ScriptableObject
    {
        [Header("Detection Box (local space, beneath icicle)")]
        public Vector2 detectionOffset = new Vector2(0f, -3f);
        public Vector2 detectionSize = new Vector2(1.5f, 6f);
        public LayerMask slimeLayer = ~0;

        [Header("Shake Phase")]
        public float shakeDuration = 0.8f;
        public float shakeAmplitude = 0.08f;
        public float shakeFrequency = 40f;

        [Header("Fall Phase")]
        public float fallSpeed = 6f;
        public LayerMask breakOnLayers = ~0;
        [Tooltip("Seconds after fall start during which break-on-ground is suppressed.")]
        public float breakImmunityDuration = 0.3f;

        [Header("Respawn")]
        [Tooltip("Negative = one-shot (no respawn).")]
        public float respawnTime = -1f;
        public float regenDuration = 0.6f;
        public LayerMask regenBlockingLayers = ~0;

        [Header("Audio (overridden by IcicleAudioManager when present)")]
        public AudioClip shakeStartSound;
        public AudioClip fallingSound;
        public AudioClip impactSound;
        [Range(0f, 1f)] public float soundVolume = 1f;
    }
}
