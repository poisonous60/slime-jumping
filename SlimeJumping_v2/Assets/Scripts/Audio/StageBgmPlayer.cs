using UnityEngine;

namespace SlimeJumping.Audio
{
    /// Scene-local BGM trigger. SoundService handles cross-scene persistence and crossfade;
    /// a scene without a StageBgmPlayer keeps whatever BGM is already playing.
    [AddComponentMenu("SlimeJumping/Audio/Stage BGM Player")]
    public class StageBgmPlayer : MonoBehaviour
    {
        [SerializeField] SoundDef bgm;
        [Range(0f, 1f)] [SerializeField] float volume = 1f;
        [Min(0f)] [SerializeField] float fadeSeconds = 1.5f;
        [Tooltip("If bgmClip is null and this is true, StopBgm is called on Start " +
                 "(silent stage). Otherwise a null clip is ignored.")]
        [SerializeField] bool stopIfClipNull = false;

        void Start()
        {
            if (bgm != null && bgm.HasClip)
                SoundService.PlayBgm(bgm.PickClip(), fadeSeconds, bgm.PickVolume() * volume);
            else if (stopIfClipNull)
                SoundService.StopBgm(fadeSeconds);
        }
    }
}
