using System.Collections;
using SlimeJumping.Effects;
using UnityEngine;
using UnityEngine.UI;

namespace SlimeJumping.UI
{
    public abstract class FullscreenOverlayCanvas : MonoBehaviour
    {
        [SerializeField] protected Image fullscreenImage;
        [SerializeField] protected AudioSource audioSource;
        [SerializeField] protected ImpulseEmitter impulseEmitter;

        protected IEnumerator RunFade(Sprite image, Color baseColor,
            float displaySeconds, float fadeSeconds)
        {
            if (fullscreenImage == null) yield break;

            if (image != null) fullscreenImage.sprite = image;
            fullscreenImage.color = baseColor;

            if (displaySeconds > 0f)
                yield return new WaitForSeconds(displaySeconds);

            if (fadeSeconds <= 0f)
            {
                fullscreenImage.color = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
                yield break;
            }

            float t = 0f;
            Color from = baseColor;
            Color to = new Color(baseColor.r, baseColor.g, baseColor.b, 0f);
            while (t < fadeSeconds)
            {
                t += Time.deltaTime;
                fullscreenImage.color = Color.Lerp(from, to, t / fadeSeconds);
                yield return null;
            }
            fullscreenImage.color = to;
        }
    }
}
