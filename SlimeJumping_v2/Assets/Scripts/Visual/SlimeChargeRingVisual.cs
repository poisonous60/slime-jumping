using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

namespace SlimeJumping.Visual
{
    /// Floor-ellipse arc ring drawn while Charging. Pure visual leaf — reads Controller state in
    /// LateUpdate, writes transform + MaterialPropertyBlock, never physics.
    ///  * Fill arc stays continuous: WASD aim can lower ChargingProgress, and quantizing it flickers.
    ///  * Rotation Slerps toward target so sliding across angle breaks doesn't snap the ring.
    public class SlimeChargeRingVisual : MonoBehaviour
    {
        [SerializeField] private SlimeController controller;
        [SerializeField] private SlimeSettings slimeSettings;
        [SerializeField] private SlimeVisualSettings settings;
        [SerializeField] private MeshRenderer ringRenderer;
        [SerializeField] private Transform ringTransform;

        private MaterialPropertyBlock mpb;
        private float fade;
        private float fadeVel;
        private bool wasChargingLastFrame;
        private bool prevFullAim;
        private float fullAimPulseTimer;

        private static readonly int ThicknessID     = Shader.PropertyToID("_Thickness");
        private static readonly int FillRatioID     = Shader.PropertyToID("_FillRatio");
        private static readonly int FullAimTickID   = Shader.PropertyToID("_FullAimTick");
        private static readonly int AlphaID         = Shader.PropertyToID("_Alpha");
        private static readonly int OccludedAlphaID = Shader.PropertyToID("_OccludedAlpha");
        private static readonly int EdgeSoftnessID  = Shader.PropertyToID("_EdgeSoftness");
        private static readonly int PosterizeID     = Shader.PropertyToID("_PosterizeSteps");
        private static readonly int BaseColorID     = Shader.PropertyToID("_BaseColor");
        private static readonly int FullAimColorID  = Shader.PropertyToID("_FullAimColor");

        private void Awake()
        {
            mpb = new MaterialPropertyBlock();
            if (ringTransform == null) ringTransform = transform;
            if (ringRenderer != null) ringRenderer.enabled = false;
        }

        private void LateUpdate()
        {
            if (settings == null || controller == null || ringRenderer == null)
            {
                if (ringRenderer != null) ringRenderer.enabled = false;
                return;
            }

            if (!settings.chargeRingEnabled)
            {
                ringRenderer.enabled = false;
                return;
            }

            bool isChargingNow = controller.CurrentState == SlimeController.SlimeState.Charging;
            bool justEntered = isChargingNow && !wasChargingLastFrame;

            float targetFade = isChargingNow ? 1f : 0f;
            float smoothTime = isChargingNow ? settings.chargeRingFadeInTime : settings.chargeRingFadeOutTime;
            if (smoothTime <= 0.0001f)
            {
                fade = targetFade;
                fadeVel = 0f;
            }
            else
            {
                fade = Mathf.SmoothDamp(fade, targetFade, ref fadeVel, smoothTime);
            }

            if (fade < 0.001f && !isChargingNow)
            {
                ringRenderer.enabled = false;
                wasChargingLastFrame = isChargingNow;
                prevFullAim = false;
                fullAimPulseTimer = 0f;
                return;
            }

            ringRenderer.enabled = true;

            float footOffset = slimeSettings != null
                ? slimeSettings.slimeRadius
                : settings.chargeRingFootOffsetFallback;

            Vector2 n2 = controller.SurfaceNormal;
            if (n2.sqrMagnitude < 0.0001f) n2 = Vector2.up;

            Vector3 targetPos = new Vector3(
                controller.Position.x - n2.x * footOffset,
                controller.Position.y - n2.y * footOffset,
                ringTransform.position.z);

            float angleZdeg = Mathf.Atan2(n2.y, n2.x) * Mathf.Rad2Deg - 90f;
            Quaternion spin = Quaternion.Euler(0f, 0f, angleZdeg);
            Quaternion tilt = Quaternion.Euler(settings.chargeRingSurfaceTiltEuler);
            Quaternion targetRot = spin * tilt;

            if (justEntered)
            {
                ringTransform.position = targetPos;
                ringTransform.rotation = targetRot;
            }
            else
            {
                ringTransform.position = targetPos;
                ringTransform.rotation = Quaternion.Slerp(
                    ringTransform.rotation,
                    targetRot,
                    Mathf.Clamp01(settings.chargeRingNormalFollowSpeed * Time.deltaTime));
            }

            float activeRadius = Mathf.Max(controller.ActiveChargeRadius, 0.0001f);
            float worldDiameter = 2f * activeRadius * settings.chargeRingRadiusScale;
            ringTransform.localScale = new Vector3(worldDiameter, worldDiameter, 1f);

            bool fullAimNow = controller.IsAtChargeFullAim;
            if (fullAimNow && !prevFullAim)
            {
                fullAimPulseTimer = settings.chargeRingFullAimPulseDuration;
            }
            prevFullAim = fullAimNow;
            if (fullAimPulseTimer > 0f)
            {
                fullAimPulseTimer -= Time.deltaTime;
                if (fullAimPulseTimer < 0f) fullAimPulseTimer = 0f;
            }
            float pulseNorm = settings.chargeRingFullAimPulseDuration > 0.0001f
                ? fullAimPulseTimer / settings.chargeRingFullAimPulseDuration
                : 0f;
            float thicknessWorld = settings.chargeRingThickness * (1f + pulseNorm * settings.chargeRingFullAimPulseAmount);
            float thicknessUV = worldDiameter > 0.0001f ? thicknessWorld / worldDiameter : settings.chargeRingThickness;

            mpb.Clear();
            mpb.SetFloat(ThicknessID, thicknessUV);
            mpb.SetFloat(FillRatioID, Mathf.Clamp01(controller.ChargingProgress));
            mpb.SetFloat(FullAimTickID, controller.ChargeFullAimProgress);
            mpb.SetFloat(AlphaID, fade);
            mpb.SetFloat(OccludedAlphaID, settings.chargeRingOccludedAlpha);
            mpb.SetFloat(EdgeSoftnessID, settings.chargeRingEdgeSoftness);
            mpb.SetFloat(PosterizeID, Mathf.Max(2, settings.chargeRingPosterizeSteps));
            mpb.SetColor(BaseColorID, settings.chargeRingBaseColor);
            mpb.SetColor(FullAimColorID, settings.chargeRingFullAimColor);
            ringRenderer.SetPropertyBlock(mpb);

            wasChargingLastFrame = isChargingNow;
        }
    }
}
