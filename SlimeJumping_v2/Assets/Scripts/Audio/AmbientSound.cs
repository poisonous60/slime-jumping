using UnityEngine;

namespace SlimeJumping.Audio
{
    /// Looping ambience (waterfall, wind, machinery) played through SoundService instead of an
    /// AudioSource dropped on the object: the mixer group follows the category, so it can never be
    /// mis-wired by hand. Its own falloff distances are kept because ambience is usually audible far
    /// past the pool's point-sound defaults.
    [AddComponentMenu("SlimeJumping/Audio/Ambient Sound")]
    public class AmbientSound : MonoBehaviour
    {
        [SerializeField] private AudioClip clip;
        [SerializeField] private SoundCategory category = SoundCategory.SFX_Environment;
        [Range(0f, 1f)] [SerializeField] private float volume = 1f;

        [Header("3D")]
        [SerializeField] private bool spatial3D = true;
        [Tooltip("Full volume within this distance. Negative keeps the SoundSettings default.")]
        [SerializeField] private float minDistance = -1f;
        [Tooltip("Fully attenuated beyond this distance. Negative keeps the SoundSettings default.")]
        [SerializeField] private float maxDistance = -1f;

        private SoundHandle handle;

        void OnEnable()
        {
            handle = SoundService.PlayLooping(clip, transform, category, volume, spatial3D,
                                              minDistance, maxDistance);
        }

        void OnDisable()
        {
            SoundService.StopLoop(handle);
            handle = default;
        }

        void OnValidate()
        {
            if (Application.isPlaying && handle.IsValid) handle.SetVolume(volume);
        }
    }
}
