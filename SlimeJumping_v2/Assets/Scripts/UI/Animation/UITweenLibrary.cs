using PrimeTween;
using UnityEngine;

namespace SlimeJumping.UI.Animation
{
    /// Reusable one-shot effects. Each method returns the underlying Sequence
    /// so callers can chain OnComplete / await if they want, but most call
    /// sites can fire-and-forget.
    public static class UITweenLibrary
    {
        /// Pop a rect up to bumpScale + random-signed rotation, then settle.
        public static Sequence Bump(RectTransform rt, JuiceProfile profile)
        {
            if (rt == null || profile == null) return default;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
            float dir = Random.value < 0.5f ? -1f : 1f;
            float rot = profile.bumpRotationDeg * dir;
            var up = Sequence.Create()
                .Group(Tween.Scale(rt, Vector3.one * profile.bumpScale, profile.bumpDuration * 0.45f, profile.bumpEase))
                .Group(Tween.LocalRotation(rt, new Vector3(0f, 0f, rot), profile.bumpDuration * 0.45f, profile.bumpEase));
            var down = Sequence.Create()
                .Group(Tween.Scale(rt, Vector3.one, profile.bumpDuration * 0.55f, Ease.OutQuad))
                .Group(Tween.LocalRotation(rt, Vector3.zero, profile.bumpDuration * 0.55f, Ease.OutQuad));
            return Sequence.Create().Chain(up).Chain(down);
        }

        /// Pure scale punch — no rotation.
        public static Sequence PunchScale(RectTransform rt, float amplitude, float duration)
        {
            if (rt == null) return default;
            rt.localScale = Vector3.one;
            return Sequence.Create()
                .Chain(Tween.Scale(rt, Vector3.one * amplitude, duration * 0.4f, Ease.OutQuad))
                .Chain(Tween.Scale(rt, Vector3.one, duration * 0.6f, Ease.OutBack));
        }

        /// Slide a rect from `from` offset back to its stored anchored position.
        /// The caller is responsible for setting the original position before
        /// calling this.
        public static Tween SlideInFromOffset(RectTransform rt, Vector2 originalAnchored, Vector2 offset,
            float duration, Ease ease, float delay)
        {
            if (rt == null) return default;
            rt.anchoredPosition = originalAnchored + offset;
            return Tween.UIAnchoredPosition(rt, originalAnchored, duration, ease, startDelay: delay);
        }

        /// Fade a CanvasGroup from 0 to 1 with optional delay.
        public static Tween FadeIn(CanvasGroup cg, float duration, float delay = 0f)
        {
            if (cg == null) return default;
            cg.alpha = 0f;
            return Tween.Alpha(cg, 0f, 1f, duration, startDelay: delay);
        }
    }
}
