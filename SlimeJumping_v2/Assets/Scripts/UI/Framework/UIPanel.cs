using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SlimeJumping.UI.Framework
{
    /// Base class for every screen-space panel. Handles CanvasGroup fade, raycast
    /// toggling, and registration with the scene's UIManager. Concrete panels
    /// override OnAfterAwake/OnAfterOpen/OnAfterClose as needed.
    [RequireComponent(typeof(CanvasGroup))]
    [RequireComponent(typeof(RectTransform))]
    public abstract class UIPanel : MonoBehaviour
    {
        [SerializeField] protected CanvasGroup canvasGroup;
        [SerializeField] protected UILayer layer = UILayer.Menu;
        [SerializeField] protected bool isModal = false;
        [Tooltip("If true, opens automatically on scene enter.")]
        [SerializeField] protected bool openOnStart = false;
        [Tooltip("If true, UIManager opens this panel when ESC is pressed with an empty modal stack.")]
        [SerializeField] protected bool openOnEscape = false;
        [SerializeField, Min(0f)] protected float fadeInDuration = 0.15f;
        [SerializeField, Min(0f)] protected float fadeOutDuration = 0.12f;

        private Coroutine fadeRoutine;
        private readonly Dictionary<UISlotType, UISlot> slotsByType = new Dictionary<UISlotType, UISlot>();

        public bool IsOpen { get; private set; }
        public UILayer Layer => layer;
        public bool IsModal => isModal;
        public bool OpenOnEscape => openOnEscape;

        protected virtual void Awake()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            // NOTE: don't SetActive(false) here — Unity skips Start on GameObjects
            // deactivated during their own Awake, so registration would never run.
            // The CanvasGroup above already hides the panel visually.

            CacheSlots();
            OnAfterAwake();
        }

        protected virtual void Start()
        {
            var mgr = UIManager.Instance;
            if (mgr != null) mgr.Register(this);
            else Debug.LogWarning($"[UIPanel] {name}: no UIManager in scene — panel will not respond to ESC or modal gate.", this);

            if (openOnStart)
            {
                // Route through UIManager so modal panels that auto-open also
                // push onto the modal stack and engage the input gate.
                if (mgr != null) mgr.Open(this);
                else Open();
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        protected virtual void OnDestroy()
        {
            if (UIManager.Instance != null) UIManager.Instance.Unregister(this);
        }

        private void CacheSlots()
        {
            slotsByType.Clear();
            var slots = GetComponentsInChildren<UISlot>(includeInactive: true);
            for (int i = 0; i < slots.Length; i++)
            {
                var s = slots[i];
                if (s.slotType == UISlotType.None) continue;
                // Last one wins if duplicates exist — a warning is noisy during prefab setup.
                slotsByType[s.slotType] = s;
            }
        }

        /// Resolve a tagged child by slot type. Returns null when absent.
        protected T FindSlot<T>(UISlotType type) where T : Component
        {
            if (!slotsByType.TryGetValue(type, out var slot) || slot == null) return null;
            return slot.GetComponent<T>();
        }

        /// Same as FindSlot but returns the GameObject itself.
        protected GameObject FindSlotGameObject(UISlotType type)
        {
            if (!slotsByType.TryGetValue(type, out var slot) || slot == null) return null;
            return slot.gameObject;
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            gameObject.SetActive(true);
            OnBeforeOpen();
            StartFade(1f, fadeInDuration, setRaycastWhenDone: true);
            OnAfterOpen();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            OnBeforeClose();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            StartFade(0f, fadeOutDuration, setRaycastWhenDone: false, deactivateWhenDone: true);
            OnAfterClose();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        private void StartFade(float target, float duration, bool setRaycastWhenDone, bool deactivateWhenDone = false)
        {
            if (fadeRoutine != null) StopCoroutine(fadeRoutine);
            fadeRoutine = StartCoroutine(FadeRoutine(target, duration, setRaycastWhenDone, deactivateWhenDone));
        }

        private IEnumerator FadeRoutine(float target, float duration, bool setRaycastWhenDone, bool deactivateWhenDone)
        {
            float start = canvasGroup.alpha;
            if (duration <= 0f)
            {
                canvasGroup.alpha = target;
            }
            else
            {
                float t = 0f;
                while (t < duration)
                {
                    t += Time.unscaledDeltaTime;
                    canvasGroup.alpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / duration));
                    yield return null;
                }
                canvasGroup.alpha = target;
            }

            if (setRaycastWhenDone)
            {
                canvasGroup.interactable = true;
                canvasGroup.blocksRaycasts = true;
            }
            if (deactivateWhenDone && !IsOpen)
            {
                gameObject.SetActive(false);
            }

            fadeRoutine = null;
        }

        protected virtual void OnAfterAwake() { }
        protected virtual void OnBeforeOpen() { }
        protected virtual void OnAfterOpen() { }
        protected virtual void OnBeforeClose() { }
        protected virtual void OnAfterClose() { }
    }
}
