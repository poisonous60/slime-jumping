using System.Collections;
using SlimeJumping.Effects;
using UnityEngine;
using UnityEngine.UI;

namespace SlimeJumping.UI
{
    public abstract class FullscreenOverlayCanvas : MonoBehaviour
    {
        [SerializeField] protected CanvasGroup canvasGroup;
        [SerializeField] protected Image fullscreenImage;
        [SerializeField] protected ImpulseEmitter impulseEmitter;

        private Coroutine activeFade;

        protected void StartFade(float displaySeconds, float fadeSeconds)
        {
            if (activeFade != null) StopCoroutine(activeFade);
            activeFade = StartCoroutine(RunFade(displaySeconds, fadeSeconds));
        }

        void OnDisable()
        {
            // Unity kills running coroutines on disable without cleanup. Reset state so
            // re-enable starts from a clean invisible overlay instead of a stuck alpha.
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            activeFade = null;
        }

        IEnumerator RunFade(float displaySeconds, float fadeSeconds)
        {
            if (canvasGroup == null) { activeFade = null; yield break; }

            canvasGroup.alpha = 1f;

            if (displaySeconds > 0f)
                yield return new WaitForSeconds(displaySeconds);

            if (fadeSeconds <= 0f)
            {
                canvasGroup.alpha = 0f;
                activeFade = null;
                yield break;
            }

            float t = 0f;
            while (t < fadeSeconds)
            {
                t += Time.deltaTime;
                canvasGroup.alpha = 1f - (t / fadeSeconds);
                yield return null;
            }
            canvasGroup.alpha = 0f;
            activeFade = null;
        }
    }
}
