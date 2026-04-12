using UnityEngine;

namespace SlimeJumping.UI
{
    public class FlashCanvasController : FullscreenOverlayCanvas
    {
        [SerializeField] private float whiteTintAlpha = 0.5f;

        public void Trigger(float displaySeconds = 0.05f, float fadeSeconds = 0.25f)
        {
            if (fullscreenImage != null)
            {
                fullscreenImage.sprite = null;
                fullscreenImage.color = new Color(1f, 1f, 1f, whiteTintAlpha);
            }
            StartFade(displaySeconds, fadeSeconds);
        }
    }
}
