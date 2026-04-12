using UnityEngine;

namespace SlimeJumping.UI
{
    public class JumpscareCanvasController : FullscreenOverlayCanvas
    {
        public void Trigger(Sprite image, AudioClip sound,
            float displaySeconds = 0.4f, float fadeSeconds = 0.3f)
        {
            StopAllCoroutines();
            if (sound != null && audioSource != null) audioSource.PlayOneShot(sound);
            if (impulseEmitter != null) impulseEmitter.Emit(1.5f);
            StartCoroutine(RunFade(image, Color.white, displaySeconds, fadeSeconds));
        }
    }
}
