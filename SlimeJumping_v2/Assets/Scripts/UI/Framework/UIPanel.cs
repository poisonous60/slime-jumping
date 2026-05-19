using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using SlimeJumping.UI.Animation;

namespace SlimeJumping.UI.Framework
{
    /// Base class for every screen-space panel. Handles CanvasGroup fade and a
    /// PrimeTween scale-in pop, raycast toggling, and registration with the
    /// scene's UIManager. Concrete panels override OnAfterAwake/OnAfterOpen/
    /// OnAfterClose as needed.
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
        [SerializeField] protected JuiceProfile juiceProfile;

        private Sequence openSequence;
        private RectTransform contentRect;
        private readonly Dictionary<UISlotType, UISlot> slotsByType = new Dictionary<UISlotType, UISlot>();

        public bool IsOpen { get; private set; }
        public UILayer Layer => layer;
        public bool IsModal => isModal;
        public bool OpenOnEscape => openOnEscape;
        public JuiceProfile JuiceProfile => juiceProfile;

        protected virtual void Awake()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            contentRect = transform as RectTransform;
            if (contentRect != null) contentRect.localScale = Vector3.one;
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
            openSequence.Stop();
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
                slotsByType[s.slotType] = s;
            }
        }

        protected T FindSlot<T>(UISlotType type) where T : Component
        {
            if (!slotsByType.TryGetValue(type, out var slot) || slot == null) return null;
            return slot.GetComponent<T>();
        }

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
            PlayOpenAnimation();
            TriggerStaggers();
            OnAfterOpen();
        }

        private void TriggerStaggers()
        {
            var staggers = GetComponentsInChildren<UIContainerStagger>(includeInactive: false);
            for (int i = 0; i < staggers.Length; i++)
            {
                var s = staggers[i];
                if (s == null) continue;
                if (s.ManualTriggerOnly) continue;
                s.Play();
            }
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            OnBeforeClose();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            PlayCloseAnimation();
            OnAfterClose();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        private void PlayOpenAnimation()
        {
            openSequence.Stop();
            if (contentRect != null)
            {
                float from = juiceProfile != null ? juiceProfile.panelOpenScaleFrom : 0.92f;
                contentRect.localScale = Vector3.one * from;
            }
            canvasGroup.alpha = 0f;

            float dur = juiceProfile != null ? juiceProfile.panelOpenDuration : 0.22f;
            Ease ease = juiceProfile != null ? juiceProfile.panelOpenEase : Ease.OutBack;

            var seq = Sequence.Create();
            seq.Group(Tween.Alpha(canvasGroup, 0f, 1f, dur));
            if (contentRect != null)
                seq.Group(Tween.Scale(contentRect, Vector3.one, dur, ease));
            seq.OnComplete(this, p =>
            {
                if (p == null || !p.IsOpen) return;
                p.canvasGroup.interactable = true;
                p.canvasGroup.blocksRaycasts = true;
            });
            openSequence = seq;
        }

        private void PlayCloseAnimation()
        {
            openSequence.Stop();
            float dur = juiceProfile != null ? juiceProfile.panelCloseDuration : 0.14f;
            Ease ease = juiceProfile != null ? juiceProfile.panelCloseEase : Ease.InOutQuad;
            float scaleTo = juiceProfile != null ? juiceProfile.panelCloseScaleTo : 0.96f;

            var seq = Sequence.Create();
            seq.Group(Tween.Alpha(canvasGroup, canvasGroup.alpha, 0f, dur));
            if (contentRect != null)
                seq.Group(Tween.Scale(contentRect, Vector3.one * scaleTo, dur, ease));
            seq.OnComplete(this, p =>
            {
                if (p == null || p.IsOpen) return;
                p.gameObject.SetActive(false);
                if (p.contentRect != null) p.contentRect.localScale = Vector3.one;
            });
            openSequence = seq;
        }

        protected virtual void OnAfterAwake() { }
        protected virtual void OnBeforeOpen() { }
        protected virtual void OnAfterOpen() { }
        protected virtual void OnBeforeClose() { }
        protected virtual void OnAfterClose() { }
    }
}
