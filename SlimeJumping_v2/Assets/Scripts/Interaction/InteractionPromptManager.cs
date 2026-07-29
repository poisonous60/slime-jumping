using TMPro;
using UnityEngine;
using SlimeJumping.InputHandling;
using SlimeJumping.Interfaces;

namespace SlimeJumping.Interaction
{
    /// <summary>World-space "E" prompt above the current interactable; stays hidden after a press until the slime re-enters.</summary>
    /// TMP must sit on this same GameObject — a runtime child Transform would survive a domain reload as a stale ref.
    [RequireComponent(typeof(TextMeshPro))]
    public class InteractionPromptManager : MonoBehaviour
    {
        [SerializeField] private InteractionDetector detector;
        [SerializeField] private SlimeInputReader inputReader;

        [Header("Appearance")]
        [SerializeField] private string promptText = "E";
        [SerializeField] private float fontSize = 4f;
        [SerializeField] private Color promptColor = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] private Vector3 worldOffset = new Vector3(0f, 1.0f, -0.1f);
        [SerializeField] private float fadeDuration = 0.12f;
        [SerializeField] private int sortingOrder = 500;
        [SerializeField] private string sortingLayerName = "Default";
        [Tooltip("Optional child Transform name to use as prompt anchor on interactables; falls back to interactable.transform.position + worldOffset.")]
        [SerializeField] private string anchorChildName = "PromptAnchor";

        private TextMeshPro promptTmp;
        private MeshRenderer promptRenderer;

        private Transform activeAnchor;          // transform the prompt follows (root or child)
        private bool activeAnchorIsChild;        // if true, use anchor position as-is (no worldOffset)
        private IInteractable activeTarget;      // the interactable whose prompt we're showing (or null)
        private IInteractable suppressedTarget;  // target is hidden until slime leaves it
        private float currentAlpha;

        private void Awake()
        {
            if (detector == null) detector = FindFirstObjectByType<InteractionDetector>();
            if (inputReader == null) inputReader = FindFirstObjectByType<SlimeInputReader>();
            ConfigureTMP();
        }

        private void OnEnable()
        {
            if (detector != null)
            {
                detector.OnTargetChanged += HandleTargetChanged;
                detector.OnInteracted += HandleInteracted;
            }
        }

        private void OnDisable()
        {
            if (detector != null)
            {
                detector.OnTargetChanged -= HandleTargetChanged;
                detector.OnInteracted -= HandleInteracted;
            }
        }

        private void ConfigureTMP()
        {
            promptTmp = GetComponent<TextMeshPro>();
            promptRenderer = GetComponent<MeshRenderer>();
            if (promptTmp == null) return;

            promptTmp.text = promptText;
            promptTmp.fontSize = fontSize;
            promptTmp.alignment = TextAlignmentOptions.Center;
            promptTmp.fontStyle = FontStyles.Bold;
            promptTmp.color = new Color(promptColor.r, promptColor.g, promptColor.b, 0f);
            promptTmp.sortingOrder = sortingOrder;

            if (promptRenderer != null)
            {
                promptRenderer.sortingLayerName = sortingLayerName;
                promptRenderer.sortingOrder = sortingOrder;
                promptRenderer.enabled = false;
            }

            currentAlpha = 0f;
        }

        private void HandleTargetChanged(IInteractable previous, IInteractable next)
        {
            // Leaving the suppressed target clears suppression so re-entering re-shows the prompt.
            if (suppressedTarget != null && !ReferenceEquals(next, suppressedTarget))
                suppressedTarget = null;

            if (next != null && !ReferenceEquals(next, suppressedTarget))
            {
                var mb = next as MonoBehaviour;
                if (mb != null)
                {
                    Transform child = string.IsNullOrEmpty(anchorChildName)
                        ? null : mb.transform.Find(anchorChildName);
                    activeAnchor = child != null ? child : mb.transform;
                    activeAnchorIsChild = child != null;
                    activeTarget = next;
                    if (promptRenderer != null) promptRenderer.enabled = true;
                    return;
                }
            }

            activeTarget = null;
            activeAnchor = null;
            activeAnchorIsChild = false;
        }

        private void HandleInteracted(IInteractable target)
        {
            // Hide immediately; keep suppressed until the slime leaves this target.
            suppressedTarget = target;
            activeTarget = null;
            activeAnchor = null;
            activeAnchorIsChild = false;
        }

        private void LateUpdate()
        {
            if (promptTmp == null) return;

            // Handle destroyed interactable (Unity fake-null).
            if (activeTarget != null && (activeTarget as UnityEngine.Object) == null)
            {
                activeTarget = null;
                activeAnchor = null;
                activeAnchorIsChild = false;
            }
            if (activeAnchor == null) activeTarget = null;

            bool show = activeTarget != null && activeAnchor != null;
            float targetAlpha = show ? promptColor.a : 0f;

            if (fadeDuration > 0f)
            {
                float rate = promptColor.a / fadeDuration;
                currentAlpha = Mathf.MoveTowards(currentAlpha, targetAlpha, rate * Time.unscaledDeltaTime);
            }
            else
            {
                currentAlpha = targetAlpha;
            }
            promptTmp.color = new Color(promptColor.r, promptColor.g, promptColor.b, currentAlpha);

            if (show)
            {
                Vector3 basePos = activeAnchor.position;
                transform.position = activeAnchorIsChild ? basePos : basePos + worldOffset;
            }

            if (promptRenderer != null)
                promptRenderer.enabled = currentAlpha > 0.001f;
        }
    }
}
