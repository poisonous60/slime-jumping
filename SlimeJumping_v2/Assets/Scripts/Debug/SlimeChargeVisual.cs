using UnityEngine;
using UnityEngine.UI;

namespace SlimeJumping.Debugging
{
    public class SlimeChargeVisual : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform circleImage;
        [SerializeField] private RectTransform aimDotImage;
        [SerializeField] private Image aimLineImage;

        [Header("Visual Settings")]
        [SerializeField] private Color unchargedColor = Color.green;
        [SerializeField] private Color fullChargeColor = Color.red;
        [SerializeField] private float circleScreenRadius = 150f;

        private UnityEngine.Camera cam;
        private RectTransform canvasRect;

        private void Awake()
        {
            cam = UnityEngine.Camera.main;
            if (canvas != null)
                canvasRect = canvas.GetComponent<RectTransform>();
        }

        public void Show()
        {
            if (circleImage != null) circleImage.gameObject.SetActive(true);
            if (aimDotImage != null) aimDotImage.gameObject.SetActive(true);
            if (aimLineImage != null) aimLineImage.gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (circleImage != null) circleImage.gameObject.SetActive(false);
            if (aimDotImage != null) aimDotImage.gameObject.SetActive(false);
            if (aimLineImage != null) aimLineImage.gameObject.SetActive(false);
        }

        public void UpdateVisual(Vector2 worldCenter, Vector2 worldAimPoint, float chargeRadius)
        {
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
