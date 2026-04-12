using UnityEngine;

namespace SlimeJumping.UI
{
    public class FlashCanvasController : FullscreenOverlayCanvas
    {
        public void Trigger(float displaySeconds = 0.05f, float fadeSeconds = 0.25f)
        {
            StopAllCoroutines();
            StartCoroutine(RunFade(null, Color.white, displaySeconds, fadeSeconds));
        }
    }
}
