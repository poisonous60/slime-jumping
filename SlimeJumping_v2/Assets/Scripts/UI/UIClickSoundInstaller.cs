using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

namespace SlimeJumping.UI
{
    /// Attach under a Canvas to add a click SFX to every Button in its hierarchy.
    /// Uses its own pooled AudioSource (optionally routed through a mixer group) so
    /// it works in scenes that don't host a SoundService.
    public class UIClickSoundInstaller : MonoBehaviour
    {
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private AudioMixerGroup outputGroup;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
        [SerializeField, Range(0.5f, 2f)] private float pitch = 1f;

        private AudioSource source;

        private void Awake()
        {
            if (clickClip == null) return;

            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = outputGroup;
            source.pitch = pitch;

            // Include inactive so buttons inside collapsed panels get hooked too.
            var buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].onClick.AddListener(PlayClick);
        }

        private void PlayClick()
        {
            if (source == null || clickClip == null) return;
            source.PlayOneShot(clickClip, volume);
        }
    }
}
