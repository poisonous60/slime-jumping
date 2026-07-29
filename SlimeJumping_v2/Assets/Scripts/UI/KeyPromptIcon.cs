using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SlimeJumping.UI
{
    /// <summary>World-space keycap prompt; fades in, holds, fades out (holdDuration = 0 = persistent).</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class KeyPromptIcon : MonoBehaviour
    {
        [Header("Content")]
        [SerializeField] private string keyLabel = "W";
        [SerializeField] private Vector2 size = new Vector2(1f, 1f);
        [SerializeField] private float fontSize = 4f;

        [Header("Appearance")]
        [Tooltip("Glass-tinted background (rounded rect).")]
        [SerializeField] private Color backgroundTint = new Color(0.80f, 0.92f, 1f, 0.40f);
        [SerializeField] private Color textColor = new Color(1f, 1f, 1f, 0.95f);
        [Tooltip("Background sprite (defaults to Unity built-in UISprite rounded rect).")]
        [SerializeField] private Sprite backgroundSprite;

        [Header("Sorting")]
        [SerializeField] private string sortingLayerName = "Default";
        [SerializeField] private int backgroundOrder = 498;
        [SerializeField] private int textOrder = 499;

        [Header("Fade (Play mode)")]
        [SerializeField] private float fadeInDelay = 0.2f;
        [SerializeField] private float fadeInDuration = 0.5f;
        [Tooltip("Seconds to hold fully visible. 0 = stay forever (no auto fade-out).")]
        [SerializeField] private float holdDuration = 6f;
        [SerializeField] private float fadeOutDuration = 1.2f;

        private SpriteRenderer bgRenderer;
        private TextMeshPro tmp;
        private float elapsed;
        private float currentAlpha;

        private void Reset()
        {
            TryAssignBuiltinSprite();
            EnsureChildren();
            ApplyAppearance();
        }

        private void OnEnable()
        {
            EnsureChildren();
            ApplyAppearance();
            elapsed = 0f;
            currentAlpha = 0f;
            ApplyAlpha(0f);
        }

        private void OnValidate()
        {
            // SpriteRenderer.size dispatches SendMessage, which Unity forbids during OnValidate;
            // defer the restyle one editor tick.
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += DeferredOnValidate;
#endif
        }

#if UNITY_EDITOR
        private void DeferredOnValidate()
        {
            UnityEditor.EditorApplication.delayCall -= DeferredOnValidate;
            if (this == null) return; // destroyed between the schedule and the fire

            if (bgRenderer == null)
            {
                var bgT = transform.Find("BG");
                if (bgT != null) bgRenderer = bgT.GetComponent<SpriteRenderer>();
            }
            if (tmp == null)
            {
                var labelT = transform.Find("Label");
                if (labelT != null) tmp = labelT.GetComponent<TextMeshPro>();
            }
            ApplyAppearance();
            if (!Application.isPlaying) ApplyAlpha(1f); // preview fully visible in editor
        }
#endif

        private void Update()
        {
            if (!Application.isPlaying)
            {
                ApplyAlpha(1f);
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            float a = ComputeAlpha(elapsed);
            currentAlpha = a;
            ApplyAlpha(a);
        }

        private float ComputeAlpha(float t)
        {
            float fadeInEnd = fadeInDelay + Mathf.Max(0f, fadeInDuration);
            if (t < fadeInDelay) return 0f;
            if (t < fadeInEnd)
                return fadeInDuration <= 0f ? 1f : (t - fadeInDelay) / fadeInDuration;

            if (holdDuration <= 0f) return 1f; // persistent

            float holdEnd = fadeInEnd + holdDuration;
            if (t < holdEnd) return 1f;

            float fadeOutEnd = holdEnd + Mathf.Max(0f, fadeOutDuration);
            if (t < fadeOutEnd)
                return fadeOutDuration <= 0f ? 0f : 1f - (t - holdEnd) / fadeOutDuration;

            return 0f;
        }

        private void EnsureChildren()
        {
            var bgT = transform.Find("BG");
            if (bgT == null)
            {
                var bg = new GameObject("BG");
                bg.transform.SetParent(transform, worldPositionStays: false);
                bgRenderer = bg.AddComponent<SpriteRenderer>();
                bgRenderer.drawMode = SpriteDrawMode.Sliced;
            }
            else
            {
                bgRenderer = bgT.GetComponent<SpriteRenderer>();
                if (bgRenderer == null) bgRenderer = bgT.gameObject.AddComponent<SpriteRenderer>();
            }

            var labelT = transform.Find("Label");
            if (labelT == null)
            {
                var lbl = new GameObject("Label");
                lbl.transform.SetParent(transform, worldPositionStays: false);
                tmp = lbl.AddComponent<TextMeshPro>();
            }
            else
            {
                tmp = labelT.GetComponent<TextMeshPro>();
                if (tmp == null) tmp = labelT.gameObject.AddComponent<TextMeshPro>();
            }
        }

        private void ApplyAppearance()
        {
            if (backgroundSprite == null) TryAssignBuiltinSprite();

            if (bgRenderer != null)
            {
                if (bgRenderer.sprite != backgroundSprite) bgRenderer.sprite = backgroundSprite;
                if (bgRenderer.drawMode != SpriteDrawMode.Sliced) bgRenderer.drawMode = SpriteDrawMode.Sliced;
                bgRenderer.size = size;
                bgRenderer.sortingLayerName = sortingLayerName;
                bgRenderer.sortingOrder = backgroundOrder;
            }

            if (tmp != null)
            {
                tmp.text = keyLabel;
                tmp.fontSize = fontSize;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.fontStyle = FontStyles.Bold;
                tmp.sortingOrder = textOrder;
                tmp.rectTransform.sizeDelta = size;
                tmp.rectTransform.localPosition = new Vector3(0f, 0f, -0.01f); // nudge forward
                var mr = tmp.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    mr.sortingLayerName = sortingLayerName;
                    mr.sortingOrder = textOrder;
                }
            }
        }

        private void ApplyAlpha(float a)
        {
            if (bgRenderer != null)
            {
                var c = backgroundTint; c.a = backgroundTint.a * a;
                bgRenderer.color = c;
            }
            if (tmp != null)
            {
                var c = textColor; c.a = textColor.a * a;
                tmp.color = c;
            }
        }

        private void TryAssignBuiltinSprite()
        {
            if (backgroundSprite != null) return;
#if UNITY_EDITOR
            backgroundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
#endif
            if (backgroundSprite == null)
                backgroundSprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        }
    }
}
