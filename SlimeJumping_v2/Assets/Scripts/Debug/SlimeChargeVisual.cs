using UnityEngine;

namespace SlimeJumping.Debugging
{
    public class SlimeChargeVisual : MonoBehaviour
    {
        [SerializeField] private LineRenderer aimLine;

        [Header("Visual Settings")]
        [SerializeField] private Color unchargedColor = Color.green;
        [SerializeField] private Color fullChargeColor = Color.red;
        [SerializeField] private float maxLineLength = 2f;
        [SerializeField] private float lineWidth = 0.05f;

        private void Awake()
        {
            if (aimLine != null)
            {
                aimLine.positionCount = 2;
                aimLine.startWidth = lineWidth;
                aimLine.endWidth = lineWidth;
                aimLine.enabled = false;
            }
        }

        public void Show()
        {
            if (aimLine != null)
                aimLine.enabled = true;
        }

        public void Hide()
        {
            if (aimLine != null)
                aimLine.enabled = false;
        }

        public void UpdateVisual(float chargeRatio, float aimAngleDegrees, Vector2 origin)
        {
            if (aimLine == null) return;

            float rad = aimAngleDegrees * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            float length = Mathf.Max(chargeRatio * maxLineLength, 0.1f);
            Vector2 end = origin + dir * length;

            aimLine.SetPosition(0, new Vector3(origin.x, origin.y, 0f));
            aimLine.SetPosition(1, new Vector3(end.x, end.y, 0f));

            Color c = Color.Lerp(unchargedColor, fullChargeColor, chargeRatio);
            aimLine.startColor = c;
            aimLine.endColor = c;
        }
    }
}
