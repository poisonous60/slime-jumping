using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace SlimeJumping.UI
{
    /// Click/hover SFX for every Button under this hierarchy, via its own AudioSource
    /// so scenes without a SoundService still get UI sound. PlayHover is called by UIButtonJuice.
    public class UIClickSoundInstaller : MonoBehaviour
    {
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private AudioClip hoverClip;
        [SerializeField] private AudioMixerGroup outputGroup;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float hoverVolume = 0.45f;
        [SerializeField, Range(0.5f, 2f)] private float pitch = 1f;
        [SerializeField, Range(0.5f, 2f)] private float hoverPitchJitter = 0.06f;

        private AudioSource source;

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = outputGroup;
            source.pitch = pitch;

            // Include inactive so buttons inside collapsed panels get hooked too.
            if (clickClip != null)
            {
                var buttons = GetComponentsInChildren<Button>(true);
                for (int i = 0; i < buttons.Length; i++)
                    buttons[i].onClick.AddListener(PlayClick);
            }
        }

        private void PlayClick()
        {
            if (source == null || clickClip == null) return;
            source.pitch = pitch;
            source.PlayOneShot(clickClip, volume);
        }

        public void PlayHover()
        {
            if (source == null || hoverClip == null) return;
            source.pitch = pitch + Random.Range(-hoverPitchJitter, hoverPitchJitter);
            source.PlayOneShot(hoverClip, hoverVolume);
        }
    }
}
