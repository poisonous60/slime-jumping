using SlimeJumping.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace SlimeJumping.UI
{
    /// Click/hover SFX for every Button under this hierarchy. PlayHover is called by UIButtonJuice.
    public class UIClickSoundInstaller : MonoBehaviour
    {
        [SerializeField] private SoundDef clickSound;
        [SerializeField] private SoundDef hoverSound;

        private void Awake()
        {
            // Include inactive so buttons inside collapsed panels get hooked too.
            var buttons = GetComponentsInChildren<Button>(true);
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].onClick.AddListener(PlayClick);
        }

        private void PlayClick()
        {
            SoundService.Play2D(clickSound);
        }

        public void PlayHover()
        {
            SoundService.Play2D(hoverSound);
        }
    }
}
