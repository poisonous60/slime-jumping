using UnityEngine;
using UnityEngine.UI;
using SlimeJumping.Settings;

namespace SlimeJumping.Debugging
{
    public class SlimeChargeVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform circleImage;
        [SerializeField] private RectTransform aimDotImage;
        [SerializeField] private Image aimLineImage;

        [Header("Toggle")]
        [SerializeField] private bool showCircle = true;
        [SerializeField] private bool showAimDot = true;

        [Header("Visual Settings")]
        [SerializeField] private Color unchargedColor = Color.green;
        [SerializeField] private Color fullChargeColor = Color.red;
        [SerializeField] private float circleScreenRadius = 150f;

        private UnityEngine.Camera cam;
        private RectTransform canvasRect;

        // Tracks the controller's "should be shown" intent independent of the
        // debug option. This way the option toggle can hide/restore visuals
        // mid-charge without losing what the controller asked for.
        private bool requestedVisible;
        private bool optionEnabled = true;

        private void Awake()
        {
            cam = UnityEngine.Camera.main;
            if (canvas != null)
                canvasRect = canvas.GetComponent<RectTransform>();
        }

        private void OnEnable()
        {
            optionEnabled = GameOptions.ChargeVisualEnabled;
            GameOptions.ChargeVisualEnabledChanged += OnOptionChanged;
            ApplyVisibility();
        }

        private void OnDisable()
        {
            GameOptions.ChargeVisualEnabledChanged -= OnOptionChanged;
        }

        private void OnOptionChanged(bool on)
        {
            optionEnabled = on;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            bool visible = requestedVisible && optionEnabled;
            if (circleImage != null) circleImage.gameObject.SetActive(visible && showCircle);
            if (aimDotImage != null) aimDotImage.gameObject.SetActive(visible && showAimDot);
            if (aimLineImage != null) aimLineImage.gameObject.SetActive(visible && showAimDot);
        }

        public void Show()
        {
            requestedVisible = true;
            ApplyVisibility();
        }

        public void Hide()
        {
            requestedVisible = false;
            ApplyVisibility();
        }

        public void UpdateVisual(Vector2 worldCenter, Vector2 worldAimPoint, float chargeRadius)
        {
            if (!optionEnabled || !requestedVisible) return;
            if (cam == null || canvasRect == null) return;

            // World → screen → canvas local position
            Vector2 screenCenter = cam.WorldToScreenPoint(worldCenter);
            Vector2 screenAim = cam.WorldToScreenPoint(worldAimPoint);

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenCenter, null, out Vector2 canvasCenter);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenAim, null, out Vector2 canvasAim);

            // Compute scale: how many screen pixels per world unit at slime depth
            Vector2 screenEdge = cam.WorldToScreenPoint(worldCenter + Vector2.right * chargeRadius);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect, screenEdge, null, out Vector2 canvasEdge);
            float canvasRadius = Vector2.Distance(canvasCenter, canvasEdge);

            // Circle
            if (circleImage != null)
            {
                circleImage.anchoredPosition = canvasCenter;
                float diameter = canvasRadius * 2f;
                circleImage.sizeDelta = new Vector2(diameter, diameter);
            }

            // Aim dot
            if (aimDotImage != null)
            {
                aimDotImage.anchoredPosition = canvasAim;
            }

            // Aim line (stretched Image from center to aim point)
            Vector2 offset = worldAimPoint - worldCenter;
            float ratio = Mathf.Clamp(offset.magnitude / chargeRadius, 0f, 2f);

            if (aimLineImage != null)
            {
                Vector2 canvasOffset = canvasAim - canvasCenter;
                float length = canvasOffset.magnitude;
                float angle = Mathf.Atan2(canvasOffset.y, canvasOffset.x) * Mathf.Rad2Deg;

                var rt = aimLineImage.rectTransform;
                rt.anchoredPosition = canvasCenter;
                rt.sizeDelta = new Vector2(length, 3f);
                rt.pivot = new Vector2(0f, 0.5f);
                rt.localRotation = Quaternion.Euler(0f, 0f, angle);

                aimLineImage.color = Color.Lerp(unchargedColor, fullChargeColor, ratio);
            }
        }
    }
}
