using System.Collections;
using SlimeJumping.Controller;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SlimeJumping.UI
{
    /// Celeste-style scene transition: fade to black → load → iris opens on the slime → iris expands.
    /// Prefab and its iris material come from Tools/SlimeJumping/Setup Scene Transition.
    public class SceneTransitionController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image irisImage;
        [SerializeField] private Image fadeImage;
        [SerializeField] private AudioSource audioSource;

        [Header("Audio")]
        [SerializeField] private AudioClip fadeOutClip;
        [SerializeField] private AudioClip irisOpenClip;
        [SerializeField] private AudioClip irisExpandClip;

        [Header("Timings (seconds, unscaled)")]
        [SerializeField] private float fadeOutDuration = 0.35f;
        [SerializeField] private float preIrisHold = 0.15f;
        [SerializeField] private float irisOpenDuration = 0.35f;
        [SerializeField] private float slimeRevealHold = 0.4f;
        [SerializeField] private float irisExpandDuration = 0.6f;

        [Header("Iris")]
        [Tooltip("UV radius at the end of the open phase (slime visible, rest black). 0.1–0.2 is typical.")]
        [SerializeField] private float irisInnerRadius = 0.14f;
        [Tooltip("UV radius that guarantees full-screen transparency after aspect correction. >2.0 is safe.")]
        [SerializeField] private float irisExpandRadius = 2.2f;
        [SerializeField] private float edgeSoftness = 0.008f;

        private static SceneTransitionController _active;
        public static bool IsRunning => _active != null;

        public static void Play(string sceneName)
        {
            if (_active != null) return;

            var prefab = Resources.Load<SceneTransitionController>("SceneTransition");
            if (prefab == null)
            {
                Debug.LogError("[SceneTransition] Prefab missing at Resources/SceneTransition. " +
                               "Run Tools/SlimeJumping/Setup Scene Transition. Falling back to direct load.");
                SceneManager.LoadScene(sceneName);
                return;
            }

            var instance = Instantiate(prefab);
            DontDestroyOnLoad(instance.gameObject);
            _active = instance;
            instance.StartCoroutine(instance.Run(sceneName));
        }

        /// Reverse of Play: iris closes on a world-space center, loads, then fades back in.
        public static void PlayReverse(string sceneName, Vector3 worldCenter)
        {
            if (_active != null) return;

            var prefab = Resources.Load<SceneTransitionController>("SceneTransition");
            if (prefab == null)
            {
                Debug.LogError("[SceneTransition] Prefab missing at Resources/SceneTransition. Falling back to direct load.");
                SceneManager.LoadScene(sceneName);
                return;
            }

            var instance = Instantiate(prefab);
            DontDestroyOnLoad(instance.gameObject);
            _active = instance;
            instance.StartCoroutine(instance.RunReverse(sceneName, worldCenter));
        }

        private void Awake()
        {
            // Clone the iris material so _Radius/_Center edits don't write back to the asset.
            if (irisImage != null && irisImage.material != null)
                irisImage.material = new Material(irisImage.material);
        }

        private void OnDestroy()
        {
            if (irisImage != null && irisImage.material != null)
                Destroy(irisImage.material);
            if (_active == this) _active = null;
        }

        private IEnumerator Run(string sceneName)
        {
            // --- Phase 1: fade the current view to black. ---
            fadeImage.gameObject.SetActive(true);
            fadeImage.color = new Color(0f, 0f, 0f, 0f);
            irisImage.gameObject.SetActive(false);
            if (audioSource != null && fadeOutClip != null) audioSource.PlayOneShot(fadeOutClip);

            float t = 0f;
            while (t < fadeOutDuration)
            {
                t += Time.unscaledDeltaTime;
                float a = Mathf.Clamp01(t / fadeOutDuration);
                fadeImage.color = new Color(0f, 0f, 0f, a);
                yield return null;
            }
            fadeImage.color = Color.black;

            // --- Phase 2: load the target scene while the screen is black. ---
            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            load.allowSceneActivation = false;
            while (load.progress < 0.9f) yield return null;
            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;

            // Let Awake/Start/LateUpdate run so Cinemachine can frame the slime.
            yield return null;
            yield return null;
            yield return null;

            // --- Phase 3: compute iris center at the slime's screen position. ---
            Vector2 center = new Vector2(0.5f, 0.5f);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            SlimeController slime = FindObjectOfType<SlimeController>();
            if (cam != null && slime != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(slime.transform.position);
                center = new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
            }

            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.7777f;
            Material mat = irisImage.material;
            mat.SetColor("_Color", Color.black);
            mat.SetVector("_Center", new Vector4(center.x, center.y, 0f, 0f));
            mat.SetFloat("_Aspect", aspect);
            mat.SetFloat("_Edge", edgeSoftness);
            mat.SetFloat("_Radius", 0f);

            irisImage.gameObject.SetActive(true);
            fadeImage.gameObject.SetActive(false);

            if (preIrisHold > 0f)
                yield return new WaitForSecondsRealtime(preIrisHold);

            // --- Phase 4: iris open. Small circle reveals the slime. ---
            if (audioSource != null && irisOpenClip != null) audioSource.PlayOneShot(irisOpenClip);
            t = 0f;
            while (t < irisOpenDuration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / irisOpenDuration));
                mat.SetFloat("_Radius", Mathf.Lerp(0f, irisInnerRadius, p));
                yield return null;
            }
            mat.SetFloat("_Radius", irisInnerRadius);

            // --- Phase 5: hold on the slime-in-spotlight. ---
            if (slimeRevealHold > 0f)
                yield return new WaitForSecondsRealtime(slimeRevealHold);

            // --- Phase 6: iris expand. Opens up to full screen. ---
            if (audioSource != null && irisExpandClip != null) audioSource.PlayOneShot(irisExpandClip);
            t = 0f;
            while (t < irisExpandDuration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / irisExpandDuration));
                mat.SetFloat("_Radius", Mathf.Lerp(irisInnerRadius, irisExpandRadius, p));
                yield return null;
            }
            mat.SetFloat("_Radius", irisExpandRadius);

            Destroy(gameObject);
        }

        private IEnumerator RunReverse(string sceneName, Vector3 worldCenter)
        {
            // --- Phase 1: iris starts wide-open (current scene fully visible), centered on worldCenter. ---
            Vector2 center = new Vector2(0.5f, 0.5f);
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(worldCenter);
                center = new Vector2(Mathf.Clamp01(vp.x), Mathf.Clamp01(vp.y));
            }

            float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 1.7777f;
            fadeImage.gameObject.SetActive(false);
            irisImage.gameObject.SetActive(true);

            Material mat = irisImage.material;
            mat.SetColor("_Color", Color.black);
            mat.SetVector("_Center", new Vector4(center.x, center.y, 0f, 0f));
            mat.SetFloat("_Aspect", aspect);
            mat.SetFloat("_Edge", edgeSoftness);
            mat.SetFloat("_Radius", irisExpandRadius);

            // --- Phase 2: iris shrinks to the inner radius around the center. ---
            if (audioSource != null && irisExpandClip != null) audioSource.PlayOneShot(irisExpandClip);
            float t = 0f;
            while (t < irisExpandDuration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / irisExpandDuration));
                mat.SetFloat("_Radius", Mathf.Lerp(irisExpandRadius, irisInnerRadius, p));
                yield return null;
            }
            mat.SetFloat("_Radius", irisInnerRadius);

            // --- Phase 3: hold on the spotlight. ---
            if (slimeRevealHold > 0f)
                yield return new WaitForSecondsRealtime(slimeRevealHold);

            // --- Phase 4: iris closes from inner radius to 0 (fully black). ---
            if (audioSource != null && irisOpenClip != null) audioSource.PlayOneShot(irisOpenClip);
            t = 0f;
            while (t < irisOpenDuration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / irisOpenDuration));
                mat.SetFloat("_Radius", Mathf.Lerp(irisInnerRadius, 0f, p));
                yield return null;
            }
            mat.SetFloat("_Radius", 0f);

            // --- Phase 5: swap iris for flat black, load target scene. ---
            fadeImage.gameObject.SetActive(true);
            fadeImage.color = Color.black;
            irisImage.gameObject.SetActive(false);

            if (preIrisHold > 0f)
                yield return new WaitForSecondsRealtime(preIrisHold);

            AsyncOperation load = SceneManager.LoadSceneAsync(sceneName);
            load.allowSceneActivation = false;
            while (load.progress < 0.9f) yield return null;
            load.allowSceneActivation = true;
            while (!load.isDone) yield return null;

            // Let the loaded scene's Awake/Start run a frame.
            yield return null;
            yield return null;

            // --- Phase 6: fade from black to clear (end-scene reveal). ---
            if (audioSource != null && fadeOutClip != null) audioSource.PlayOneShot(fadeOutClip);
            t = 0f;
            while (t < fadeOutDuration)
            {
                t += Time.unscaledDeltaTime;
                float a = 1f - Mathf.Clamp01(t / fadeOutDuration);
                fadeImage.color = new Color(0f, 0f, 0f, a);
                yield return null;
            }
            fadeImage.color = new Color(0f, 0f, 0f, 0f);

            Destroy(gameObject);
        }
    }
}
