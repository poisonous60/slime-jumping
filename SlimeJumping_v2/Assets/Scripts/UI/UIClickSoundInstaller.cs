using SlimeJumping.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace SlimeJumping.UI
{
    /// Click/hover SFX for every Button under this hierarchy. Playback goes through SoundService so
    /// the UI mixer group follows SoundCategory.UI; a scene without a SoundService is silent here.
    /// PlayHover is called by UIButtonJuice.
    public class UIClickSoundInstaller : MonoBehaviour
    {
        [SerializeField] private AudioClip clickClip;
        [SerializeField] private AudioClip hoverClip;
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
        [SerializeField, Range(0f, 1f)] private float hoverVolume = 0.45f;
        [SerializeField, Range(0.5f, 2f)] private float pitch = 1f;
        [SerializeField, Range(0.5f, 2f)] private float hoverPitchJitter = 0.06f;

        private void Awake()
        {
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
            SoundService.Play2D(clickClip, SoundCategory.UI, volume, pitch);
        }

        public void PlayHover()
        {
            SoundService.Play2D(hoverClip, SoundCategory.UI, hoverVolume,
                                pitch + Random.Range(-hoverPitchJitter, hoverPitchJitter));
        }
    }
}
