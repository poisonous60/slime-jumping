using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.UI
{
    public class JumpscareCanvasController : FullscreenOverlayCanvas
    {
        public void Trigger(Sprite image, AudioClip sound,
            float displaySeconds = 0.4f, float fadeSeconds = 0.3f)
        {
            if (fullscreenImage != null)
            {
                if (image != null) fullscreenImage.sprite = image;
                fullscreenImage.color = Color.white;
            }
            SoundService.Play2D(sound, SoundCategory.UI, 1f);
            if (impulseEmitter != null) impulseEmitter.Emit(1.5f);
            StartFade(displaySeconds, fadeSeconds);
        }
    }
}
