using UnityEngine;
using SlimeJumping.Settings;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    public class SlimeChargeVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SlimeController controller;
        [SerializeField] private Transform chargeRing;
        [SerializeField] private SpriteRenderer chargeRingRenderer;
        [SerializeField] private LineRenderer aimLine;

        [Header("Visual Settings")]
        [SerializeField] private Color unchargedColor = new Color(1f, 1f, 1f, 0.7f);
        [SerializeField] private Color fullChargeColor = new Color(1f, 0.4f, 0.4f, 0.9f);
        [SerializeField] private float aimLineWidth = 0.025f;

        private bool optionEnabled = true;
        private MaterialPropertyBlock mpb;

        private void Awake()
        {
            mpb = new MaterialPropertyBlock();

            if (aimLine != null)
            {
                aimLine.useWorldSpace = true;
                aimLine.positionCount = 2;
                aimLine.startWidth = aimLineWidth;
                aimLine.endWidth = aimLineWidth;
            }
        }

        private void OnEnable()
        {
            optionEnabled = GameOptions.ChargeVisualEnabled;
            GameOptions.ChargeVisualEnabledChanged += OnOptionChanged;
            ApplyVisibility(false);
        }

        private void OnDisable()
        {
            GameOptions.ChargeVisualEnabledChanged -= OnOptionChanged;
        }

        private void OnOptionChanged(bool on)
        {
            optionEnabled = on;
        }

        private void LateUpdate()
        {
            bool show = optionEnabled
                && controller != null
                && controller.CurrentState == SlimeController.SlimeState.Charging;

            if (!show)
            {
                ApplyVisibility(false);
                return;
            }

            ApplyVisibility(true);
            UpdateRing();
            UpdateAimLine();
        }

        private void ApplyVisibility(bool visible)
        {
            if (chargeRing != null && chargeRing.gameObject.activeSelf != visible)
                chargeRing.gameObject.SetActive(visible);
            if (aimLine != null && aimLine.gameObject.activeSelf != visible)
                aimLine.gameObject.SetActive(visible);
        }

        private void UpdateRing()
        {
            if (chargeRing == null) return;

            // Unit-square sprite mesh spans 2 world units at scale=1, so localScale = radius
            // yields a final diameter of 2 * radius (matches charge area).
            float radius = controller.ActiveChargeRadius;
            chargeRing.localScale = new Vector3(radius, radius, 1f);

            if (chargeRingRenderer != null)
            {
                Color c = Color.Lerp(unchargedColor, fullChargeColor,
                    Mathf.Clamp01(controller.ChargingProgress));
                chargeRingRenderer.GetPropertyBlock(mpb);
                mpb.SetColor("_Color", c);
                chargeRingRenderer.SetPropertyBlock(mpb);
            }
        }

        private void UpdateAimLine()
        {
            if (aimLine == null) return;

            Vector3 worldCenter = controller.transform.position;
            Vector3 worldAim = worldCenter + (Vector3)controller.AimOffset;

            aimLine.SetPosition(0, worldCenter);
            aimLine.SetPosition(1, worldAim);

            Color c = Color.Lerp(unchargedColor, fullChargeColor,
                Mathf.Clamp01(controller.ChargingProgress));
            aimLine.startColor = c;
            aimLine.endColor = c;
        }
    }
}
