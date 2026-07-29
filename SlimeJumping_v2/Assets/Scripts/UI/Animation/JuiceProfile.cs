using PrimeTween;
using UnityEngine;

namespace SlimeJumping.UI.Animation
{
    /// Tunable knobs for every UI juice effect; panels/buttons may assign their own
    /// profile. Values live on an SO so rebuilt prefabs keep tuning when setup re-runs.
    [CreateAssetMenu(menuName = "SlimeJumping/UI/Juice Profile", fileName = "JuiceProfile")]
    public class JuiceProfile : ScriptableObject
    {
        [Header("Button Hover (slime boing)")]
        [Tooltip("When true, hover plays a quick squash-then-elastic-settle that mimics a slime landing. When false, falls back to a plain scale-up.")]
        public bool hoverUseSlimeBoing = true;
        [Tooltip("Horizontal scale at the bottom of the squash. >1 = wider while squashing.")]
        [Range(0.5f, 2f)] public float hoverSquashX = 1.18f;
        [Tooltip("Vertical scale at the bottom of the squash. <1 = shorter while squashing.")]
        [Range(0.3f, 1f)] public float hoverSquashY = 0.82f;
        [Tooltip("How long the initial squash takes before the elastic settle starts.")]
        [Min(0f)] public float hoverSquashDuration = 0.08f;
        [Tooltip("How long the elastic settle (boing) lasts.")]
        [Min(0f)] public float hoverSettleDuration = 0.55f;
        public Ease hoverSettleEase = Ease.OutElastic;
        [Tooltip("Plain scale-up multipliers used only when hoverUseSlimeBoing is false.")]
        [Min(1f)] public float hoverScaleX = 1.08f;
        [Min(1f)] public float hoverScaleY = 1.14f;
        [Min(0f)] public float hoverDuration = 0.22f;
        public Ease hoverEase = Ease.OutBack;
        [Min(0f)] public float hoverReturnDuration = 0.18f;
        public Ease hoverReturnEase = Ease.OutQuad;

        [Header("Button Hover Rotation (optional — set Max=0 to disable)")]
        [Tooltip("Max degrees of rotation amplitude. Sign chosen randomly. Set to 0 to disable rotation entirely.")]
        [Range(0f, 25f)] public float hoverRotationMaxDeg = 0f;
        [Range(0f, 25f)] public float hoverRotationMinDeg = 0f;
        [Tooltip("Button width (px) at which rotation amplitude is at full power. Larger buttons reduce amplitude proportionally so the corners don't fling.")]
        [Min(1f)] public float hoverRotationRefWidth = 240f;
        [Tooltip("Minimum scale multiplier the power-scaling will clamp to.")]
        [Range(0.05f, 1f)] public float hoverPowerFloor = 0.25f;

        [Header("Button Press")]
        [Range(0.5f, 1f)] public float pressScale = 0.94f;
        [Min(0f)] public float pressDuration = 0.08f;
        [Min(0f)] public float pressReleaseDuration = 0.16f;

        [Header("Panel Open / Close (scale + fade)")]
        [Range(0.5f, 1f)] public float panelOpenScaleFrom = 0.92f;
        [Min(0f)] public float panelOpenDuration = 0.22f;
        public Ease panelOpenEase = Ease.OutBack;
        [Range(0.5f, 1f)] public float panelCloseScaleTo = 0.96f;
        [Min(0f)] public float panelCloseDuration = 0.14f;
        public Ease panelCloseEase = Ease.InOutQuad;

        [Header("Container Stagger")]
        [Min(0f)] public float staggerStartDelay = 0.05f;
        [Min(0.01f)] public float staggerPerChild = 0.04f;
        [Min(0f)] public float staggerChildDuration = 0.32f;
        public Ease staggerChildEase = Ease.OutBack;
        public Vector2 staggerSlideOffset = new Vector2(0f, 32f);

        [Header("Value Bump")]
        [Min(1f)] public float bumpScale = 1.18f;
        [Range(0f, 25f)] public float bumpRotationDeg = 6f;
        [Min(0f)] public float bumpDuration = 0.22f;
        public Ease bumpEase = Ease.OutBack;
    }
}
