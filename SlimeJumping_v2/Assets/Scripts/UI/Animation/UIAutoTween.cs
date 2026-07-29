using PrimeTween;
using UnityEngine;

namespace SlimeJumping.UI.Animation
{
    /// Legacy-UI retrofit: plays a one-shot effect on enable. New code routes visibility through UIPanel.Open()/Close().
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class UIAutoTween : MonoBehaviour
    {
        public enum Effect { Bump, PunchScale, SlideInFromBottom, ScalePop }

        [SerializeField] private JuiceProfile profile;
        [SerializeField] private Effect effect = Effect.ScalePop;
        [Min(0f)] [SerializeField] private float duration = 0.28f;
        [SerializeField] private Vector2 slideOffset = new Vector2(0f, 40f);

        private RectTransform rt;

        private void Awake() => rt = GetComponent<RectTransform>();

        // OnEnable only — an OnDisable hook would race PrimeTween against a deactivating
        // GameObject; UIPanel.Close() sequences the tween before SetActive(false).
        private void OnEnable() => Play();

        public void Play()
        {
            if (rt == null) rt = GetComponent<RectTransform>();
            switch (effect)
            {
                case Effect.Bump:
                    UITweenLibrary.Bump(rt, profile);
                    break;
                case Effect.PunchScale:
                    UITweenLibrary.PunchScale(rt, 1.15f, duration);
                    break;
                case Effect.ScalePop:
                    rt.localScale = Vector3.one * 0.7f;
                    Tween.Scale(rt, Vector3.one, duration, Ease.OutBack);
                    break;
                case Effect.SlideInFromBottom:
                    Vector2 target = rt.anchoredPosition;
                    UITweenLibrary.SlideInFromOffset(rt, target, slideOffset, duration, Ease.OutBack, delay: 0f);
                    break;
            }
        }
    }
}
