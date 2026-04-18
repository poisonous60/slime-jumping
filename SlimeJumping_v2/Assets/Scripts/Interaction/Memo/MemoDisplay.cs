using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using SlimeJumping.Controller;

namespace SlimeJumping.Interaction
{
    public class MemoDisplay : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;

        private MemoProfile currentProfile;
        private GameObject panelInstance;
        private Transform panelRoot;
        private CanvasGroup canvasGroup;
        private Image backgroundImage;
        private Image illustrationImage;
        private TMP_Text titleText;
        private TMP_Text bodyText;

        private Coroutine fadeRoutine;
        private Vector2 currentOffset;
        private bool visible;
        private Color backgroundBaseColor = Color.white;

        private void Awake()
        {
            if (slime == null) slime = FindFirstObjectByType<SlimeController>();
        }

        public void Show(MemoProfile profile)
        {
            if (profile == null) return;
            EnsureInstance(profile);
            ApplyProfile(profile);
            visible = true;
            panelInstance.SetActive(true);
            StartFade(1f, profile.fadeInDuration);
        }

        public void Hide(MemoProfile profile)
        {
            if (panelInstance == null) return;
            visible = false;
            float duration = profile != null ? profile.fadeOutDuration : 0.3f;
            StartFade(0f, duration);
        }

        private void EnsureInstance(MemoProfile profile)
        {
            if (panelInstance != null && currentProfile != null && currentProfile.layoutPrefab == profile.layoutPrefab)
                return;

            if (panelInstance != null)
            {
                Destroy(panelInstance);
                panelInstance = null;
            }

            if (profile.layoutPrefab == null)
            {
                Debug.LogError($"[MemoDisplay] {name}: MemoProfile '{profile.name}' has no layoutPrefab.", this);
                return;
            }

            panelInstance = Instantiate(profile.layoutPrefab, transform);
            BindSlots(panelInstance);
            currentProfile = profile;
        }

        private void BindSlots(GameObject root)
        {
            panelRoot = root.transform;
            canvasGroup = root.GetComponentInChildren<CanvasGroup>(true);
            if (canvasGroup == null) canvasGroup = root.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            backgroundImage = null;
            illustrationImage = null;
            titleText = null;
            bodyText = null;

            var slots = root.GetComponentsInChildren<MemoSlot>(true);
            foreach (var slot in slots)
            {
                switch (slot.slotType)
                {
                    case MemoSlotType.PanelRoot:
                        panelRoot = slot.transform;
                        break;
                    case MemoSlotType.Background:
                        backgroundImage = slot.GetComponent<Image>();
                        if (backgroundImage != null) backgroundBaseColor = backgroundImage.color;
                        break;
                    case MemoSlotType.Illustration:
                        illustrationImage = slot.GetComponent<Image>();
                        break;
                    case MemoSlotType.Title:
                        titleText = slot.GetComponent<TMP_Text>();
                        break;
                    case MemoSlotType.Body:
                        bodyText = slot.GetComponent<TMP_Text>();
                        break;
                }
            }
        }

        private void ApplyProfile(MemoProfile profile)
        {
            currentProfile = profile;

            if (backgroundImage != null)
            {
                if (profile.backgroundSprite != null)
                    backgroundImage.sprite = profile.backgroundSprite;
                backgroundImage.color = backgroundBaseColor * profile.backgroundTint;
            }

            if (titleText != null)
            {
                bool has = !string.IsNullOrEmpty(profile.titleText);
                titleText.gameObject.SetActive(has);
                if (has) titleText.text = profile.titleText;
            }

            if (bodyText != null)
            {
                bool has = !string.IsNullOrEmpty(profile.bodyText);
                bodyText.gameObject.SetActive(has);
                if (has) bodyText.text = profile.bodyText;
            }

            if (illustrationImage != null)
            {
                bool has = profile.illustration != null;
                illustrationImage.gameObject.SetActive(has);
                if (has) illustrationImage.sprite = profile.illustration;
            }
        }

        private void StartFade(float targetAlpha, float duration)
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(FadeRoutine(targetAlpha, duration));
        }

        private IEnumerator FadeRoutine(float targetAlpha, float duration)
        {
            if (canvasGroup == null) yield break;
            float startAlpha = canvasGroup.alpha;
            if (duration <= 0f)
            {
                canvasGroup.alpha = targetAlpha;
            }
            else
            {
                float t = 0f;
                while (t < duration)
                {
                    t += Time.unscaledDeltaTime;
                    canvasGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, Mathf.Clamp01(t / duration));
                    yield return null;
                }
                canvasGroup.alpha = targetAlpha;
            }

            if (!visible && Mathf.Approximately(targetAlpha, 0f) && panelInstance != null)
                panelInstance.SetActive(false);

            fadeRoutine = null;
        }

        private void LateUpdate()
        {
            if (panelInstance == null || !panelInstance.activeSelf || currentProfile == null)
                return;

            if (slime == null)
            {
                slime = FindFirstObjectByType<SlimeController>();
                if (slime == null) return;
            }

            Vector2 anchorPos = transform.position;
            Vector2 desiredOffset = Vector2.ClampMagnitude(
                (slime.Position - anchorPos) * currentProfile.followStrength,
                currentProfile.maxFollowOffset);

            float k = 1f - Mathf.Exp(-currentProfile.followSmoothing * Time.unscaledDeltaTime);
            currentOffset = Vector2.Lerp(currentOffset, desiredOffset, k);

            if (panelRoot != null)
            {
                Vector3 p = transform.position;
                p.x += currentOffset.x;
                p.y += currentOffset.y;
                panelRoot.position = p;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.9f);
            Gizmos.DrawSphere(transform.position, 0.08f);
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.25f);
            Gizmos.DrawWireCube(transform.position, new Vector3(3f, 2f, 0f));
        }
#endif
    }
}
