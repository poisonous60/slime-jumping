using UnityEngine;

namespace SlimeJumping.Audio
{
    /// Scene-local BGM trigger. Drop into a stage scene, assign a clip, done —
    /// SoundService handles persistence across scene loads and crossfades
    /// automatically when the next scene's StageBgmPlayer calls PlayBgm with a
    /// different clip. Scenes without a StageBgmPlayer keep whatever BGM was
    /// already playing.
    [AddComponentMenu("SlimeJumping/Audio/Stage BGM Player")]
    public class StageBgmPlayer : MonoBehaviour
    {
        [SerializeField] AudioClip bgmClip;
        [Range(0f, 1f)] [SerializeField] float volume = 1f;
        [Min(0f)] [SerializeField] float fadeSeconds = 1.5f;
        [Tooltip("If bgmClip is null and this is true, StopBgm is called on Start " +
                 "(silent stage). Otherwise a null clip is ignored.")]
        [SerializeField] bool stopIfClipNull = false;

        void Start()
        {
            if (bgmClip != null)
                SoundService.PlayBgm(bgmClip, fadeSeconds, volume);
            else if (stopIfClipNull)
                SoundService.StopBgm(fadeSeconds);
        }
    }
}
