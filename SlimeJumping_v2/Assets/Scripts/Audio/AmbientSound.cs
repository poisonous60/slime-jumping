using UnityEngine;

namespace SlimeJumping.Audio
{
    /// Looping ambience (waterfall, wind, machinery) played through SoundService instead of an
    /// AudioSource dropped on the object: the mixer group, level and falloff all come from the
    /// SoundDef, so nothing about the sound is wired by hand on the scene object.
    [AddComponentMenu("SlimeJumping/Audio/Ambient Sound")]
    public class AmbientSound : MonoBehaviour
    {
        [SerializeField] private SoundDef sound;

        private SoundHandle handle;

        void OnEnable()
        {
            handle = SoundService.PlayLooping(sound, transform);
        }

        void OnDisable()
        {
            SoundService.StopLoop(handle);
            handle = default;
        }
    }
}
