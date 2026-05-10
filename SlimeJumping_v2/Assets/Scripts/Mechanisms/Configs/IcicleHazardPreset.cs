using UnityEngine;

namespace SlimeJumping.Mechanisms
{
    /// <summary>
    /// Tuning preset for <see cref="IcicleHazard"/> — detection box, shake,
    /// fall, and respawn. When assigned to an icicle, the preset's values
    /// OVERWRITE the inline inspector values as one set on OnValidate
    /// (edit-time, when the reference changes) and again on Awake (runtime).
    /// Mirrors the GraniteBlastEmitterPreset pattern.
    ///
    /// Excluded by design:
    ///   - Visual asset refs (mesh / material) — vary per icicle instance.
    ///   - Scene refs (visualRoot, mesh/renderer, solidCollider, breakParticles)
    ///     — must live on the component.
    ///
    /// Audio is included. IcicleHazard runs at execution order -100 so its
    /// preset apply happens BEFORE IcicleAudioManager (-50) — when a manager
    /// is present in the scene, the manager's broadcast wins; otherwise the
    /// preset's audio fields are the final values.
    /// </summary>
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
