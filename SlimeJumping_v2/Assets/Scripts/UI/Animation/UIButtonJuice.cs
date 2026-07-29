using PrimeTween;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using SlimeJumping.UI;

namespace SlimeJumping.UI.Animation
{
    /// Hover squash+stretch+rotate + press punch. Neutralises the Selectable ColorBlock in
    /// Awake so Unity's color tint doesn't fight the tweens.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public class UIButtonJuice : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private JuiceProfile profile;

        private RectTransform rt;
        private Button button;
        private UIClickSoundInstaller sfx;
        private Tween scaleTween;
        private Tween rotationTween;
        private bool isPointerOver;

        public void SetProfile(JuiceProfile p) => profile = p;

        private void Awake()
        {
            rt = GetComponent<RectTransform>();
            button = GetComponent<Button>();
            sfx = GetComponentInParent<UIClickSoundInstaller>(includeInactive: true);
            if (button != null) NeutraliseColorBlock(button);
        }

        private static void NeutraliseColorBlock(Button b)
        {
            // White tints so Unity's color lerp can't race the scale tween.
            var c = b.colors;
            c.normalColor = Color.white;
            c.highlightedColor = Color.white;
            c.pressedColor = Color.white;
            c.selectedColor = Color.white;
            c.disabledColor = new Color(1f, 1f, 1f, 0.4f);
            c.colorMultiplier = 1f;
            c.fadeDuration = 0f;
            b.colors = c;
        }

        public void OnPointerEnter(PointerEventData _)
        {
            isPointerOver = true;
            PlayHover(true);
        }

        public void OnPointerExit(PointerEventData _)
        {
            isPointerOver = false;
            PlayHover(false);
        }

        public void OnPointerDown(PointerEventData _)
        {
            if (profile == null) return;
            scaleTween.Stop();
            scaleTween = Tween.Scale(rt, Vector3.one * profile.pressScale, profile.pressDuration, Ease.OutQuad);
        }

        public void OnPointerUp(PointerEventData _)
        {
            if (profile == null) return;
            scaleTween.Stop();
            // Pointer dragged off before release settles to neutral, not hover-on.
            PlayHover(isPointerOver);
        }

        private Sequence hoverSequence;

        private void PlayHover(bool entering)
        {
            if (profile == null || rt == null) return;

            scaleTween.Stop();
            rotationTween.Stop();
            hoverSequence.Stop();

            if (entering)
            {
                if (sfx != null) sfx.PlayHover();
                float power = PowerScale();

                if (profile.hoverUseSlimeBoing)
                {
                    // Slime landing: squash, then elastic settle back to (1,1).
                    float sx = Mathf.Lerp(1f, profile.hoverSquashX, power);
                    float sy = Mathf.Lerp(1f, profile.hoverSquashY, power);
                    hoverSequence = Sequence.Create()
                        .Chain(Tween.Scale(rt, new Vector3(sx, sy, 1f),
                            profile.hoverSquashDuration, Ease.OutQuad))
                        .Chain(Tween.Scale(rt, Vector3.one,
                            profile.hoverSettleDuration, profile.hoverSettleEase));
                }
                else
                {
                    Vector3 target = new Vector3(
                        Mathf.Lerp(1f, profile.hoverScaleX, power),
                        Mathf.Lerp(1f, profile.hoverScaleY, power),
                        1f);
                    scaleTween = Tween.Scale(rt, target, profile.hoverDuration, profile.hoverEase);
                }

                if (profile.hoverRotationMaxDeg > 0f)
                {
                    float dir = Random.value < 0.5f ? -1f : 1f;
                    float amp = Random.Range(profile.hoverRotationMinDeg, profile.hoverRotationMaxDeg) * dir * power;
                    rotationTween = Tween.LocalRotation(rt, new Vector3(0f, 0f, amp),
                        profile.hoverDuration, profile.hoverEase);
                }
                else
                {
                    // wipe leftover rotation when rotation is disabled
                    rt.localRotation = Quaternion.identity;
                }
            }
            else
            {
                if (profile.hoverUseSlimeBoing)
                {
                    // On exit, no boing — just smooth return.
                    scaleTween = Tween.Scale(rt, Vector3.one, profile.hoverReturnDuration, profile.hoverReturnEase);
                }
                else
                {
                    scaleTween = Tween.Scale(rt, Vector3.one, profile.hoverReturnDuration, profile.hoverReturnEase);
                }

                if (profile.hoverRotationMaxDeg > 0f)
                {
                    rotationTween = Tween.LocalRotation(rt, Vector3.zero,
                        profile.hoverReturnDuration, profile.hoverReturnEase);
                }
            }
        }

        /// Amplitude falls off with width, else a 600px button's corners fling on a 7° rotation.
        private float PowerScale()
        {
            if (profile == null) return 1f;
            float width = rt.rect.width;
            if (width <= profile.hoverRotationRefWidth) return 1f;
            float p = profile.hoverRotationRefWidth / width;
            return Mathf.Clamp(p, profile.hoverPowerFloor, 1f);
        }

        private void OnDisable()
        {
            scaleTween.Stop();
            rotationTween.Stop();
            hoverSequence.Stop();
            isPointerOver = false;
            if (rt != null)
            {
                rt.localScale = Vector3.one;
                rt.localRotation = Quaternion.identity;
            }
        }
    }
}
