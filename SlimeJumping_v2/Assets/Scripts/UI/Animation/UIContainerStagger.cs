using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

namespace SlimeJumping.UI.Animation
{
    /// Fades immediate children back in with an increasing per-child delay. Only alpha and
    /// position change — toggling visibility would make the layout group reflow mid-animation.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class UIContainerStagger : MonoBehaviour
    {
        public enum StaggerEffect { SlideUp, ScalePop, FadeOnly }

        [SerializeField] private JuiceProfile profile;
        [SerializeField] private StaggerEffect effect = StaggerEffect.ScalePop;
        [Tooltip("Play automatically every time the GameObject becomes active. Disable when a UIPanel will trigger Play() explicitly in its OnAfterOpen.")]
        [SerializeField] private bool playOnEnable = false;
        [Tooltip("Skip children that are inactive when the stagger starts.")]
        [SerializeField] private bool skipInactive = true;
        [Tooltip("When true, UIPanel.TriggerStaggers skips this instance. Use for staggers that are driven only by explicit PlayForList() calls.")]
        [SerializeField] private bool manualTriggerOnly = false;

        public bool ManualTriggerOnly => manualTriggerOnly;

        private readonly List<Sequence> running = new List<Sequence>();
        private readonly List<Vector2> originalPositions = new List<Vector2>();

        public void SetProfile(JuiceProfile p) => profile = p;

        private void OnEnable()
        {
            if (!playOnEnable) return;
            Play();
        }

        public void Play()
        {
            if (profile == null) return;

            var rt = (RectTransform)transform;
            // Settle the layout before snapshotting child positions, otherwise
            // the first frame after a panel opens leaves children at (0,0).
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

            StopRunning();
            ResetAllChildren(rt);

            int childCount = rt.childCount;
            float delay = profile.staggerStartDelay;

            for (int i = 0; i < childCount; i++)
            {
                var child = rt.GetChild(i) as RectTransform;
                if (child == null) continue;
                if (skipInactive && !child.gameObject.activeSelf) continue;

                var cg = EnsureCanvasGroup(child.gameObject);
                cg.alpha = 0f;

                Sequence seq;
                switch (effect)
                {
                    case StaggerEffect.SlideUp:
                        // Offset rides on top of a snapshot of the layout-assigned position,
                        // else the tween fights the layout group and never lands clean.
                        Vector2 originalPos = child.anchoredPosition;
                        child.anchoredPosition = originalPos + profile.staggerSlideOffset;
                        seq = Sequence.Create()
                            .Group(Tween.Alpha(cg, 0f, 1f, profile.staggerChildDuration, startDelay: delay))
                            .Group(Tween.UIAnchoredPosition(child, originalPos, profile.staggerChildDuration,
                                profile.staggerChildEase, startDelay: delay));
                        break;
                    case StaggerEffect.ScalePop:
                        child.localScale = Vector3.one * 0.7f;
                        seq = Sequence.Create()
                            .Group(Tween.Alpha(cg, 0f, 1f, profile.staggerChildDuration, startDelay: delay))
                            .Group(Tween.Scale(child, Vector3.one, profile.staggerChildDuration,
                                profile.staggerChildEase, startDelay: delay));
                        break;
                    default:
                        seq = Sequence.Create()
                            .Group(Tween.Alpha(cg, 0f, 1f, profile.staggerChildDuration, startDelay: delay));
                        break;
                }
                running.Add(seq);
                delay += profile.staggerPerChild;
            }
        }

        /// Resets children to baseline before the stagger; a tween canceled mid-flight
        /// (panel closed while fading in) otherwise leaves CanvasGroup.alpha at 0 forever.
        private static void ResetAllChildren(RectTransform parent)
        {
            int childCount = parent.childCount;
            for (int i = 0; i < childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child == null) continue;
                child.localScale = Vector3.one;
                var cg = child.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = 1f;
            }
        }

        public void PlayForList(IList<GameObject> rows)
        {
            if (profile == null || rows == null) return;
            StopRunning();
            float delay = profile.staggerStartDelay;
            for (int i = 0; i < rows.Count; i++)
            {
                var go = rows[i];
                if (go == null) continue;
                var rt = go.transform as RectTransform;
                if (rt == null) continue;
                var cg = EnsureCanvasGroup(go);
                cg.alpha = 0f;
                Vector2 original = rt.anchoredPosition;

                Sequence seq;
                switch (effect)
                {
                    case StaggerEffect.SlideUp:
                        rt.anchoredPosition = original + profile.staggerSlideOffset;
                        seq = Sequence.Create()
                            .Group(Tween.Alpha(cg, 0f, 1f, profile.staggerChildDuration, startDelay: delay))
                            .Group(Tween.UIAnchoredPosition(rt, original, profile.staggerChildDuration,
                                profile.staggerChildEase, startDelay: delay));
                        break;
                    case StaggerEffect.ScalePop:
                        rt.localScale = Vector3.one * 0.6f;
                        seq = Sequence.Create()
                            .Group(Tween.Alpha(cg, 0f, 1f, profile.staggerChildDuration, startDelay: delay))
                            .Group(Tween.Scale(rt, Vector3.one, profile.staggerChildDuration,
                                profile.staggerChildEase, startDelay: delay));
                        break;
                    default:
                        seq = Sequence.Create()
                            .Group(Tween.Alpha(cg, 0f, 1f, profile.staggerChildDuration, startDelay: delay));
                        break;
                }
                running.Add(seq);
                delay += profile.staggerPerChild;
            }
        }

        private static CanvasGroup EnsureCanvasGroup(GameObject go)
        {
            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            return cg;
        }

        private void StopRunning()
        {
            for (int i = 0; i < running.Count; i++) running[i].Stop();
            running.Clear();
        }

        private void OnDisable()
        {
            StopRunning();
        }
    }
}
