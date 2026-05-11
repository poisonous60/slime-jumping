using UnityEngine;
using SlimeJumping.Visual;

namespace SlimeJumping.Debugging
{
    /// On-screen overlay for SlimeFrostController.freezeT. Pull-only — no fields
    /// added to the controller. Attach to any GameObject in the scene; OnGUI
    /// renders in screen space so the host transform is irrelevant. Auto-finds
    /// the first SlimeFrostController if target is left null.
    ///
    /// 0.3 marker on the bar indicates the default frostCurve flat-zone edge —
    /// freezeT below 0.3 produces _FrostStrength=0 (invisible) with default curve.
    public class SlimeFrostDebug : MonoBehaviour
    {
        [SerializeField] SlimeFrostController target;

        [Header("Display")]
        [SerializeField] Vector2 screenPos = new Vector2(20f, 20f);
        [SerializeField] int fontSize = 22;
        [SerializeField] float barWidth = 320f;
        [SerializeField] float barHeight = 18f;
        [Tooltip("Reference marker on the bar. Default 0.3 = frostCurve flat-zone edge.")]
        [Range(0f, 1f)] [SerializeField] float thresholdMarker = 0.3f;

        void Awake()
        {
            if (target == null) target = FindFirstObjectByType<SlimeFrostController>();
        }

        void OnGUI()
        {
            if (target == null)
            {
                target = FindFirstObjectByType<SlimeFrostController>();
                if (target == null) return;
            }

            float t = target.FreezeT;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.cyan },
            };
            string txt = $"freezeT: {t:F3}   inZone: {target.IsInsideColdZone}";
            GUI.Label(new Rect(screenPos.x, screenPos.y, 600f, fontSize + 4f), txt, style);

            float barY = screenPos.y + fontSize + 8f;
            var barRect = new Rect(screenPos.x, barY, barWidth, barHeight);
            var prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(barRect, Texture2D.whiteTexture);

            GUI.color = new Color(0.5f, 0.85f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(barRect.x, barRect.y, barWidth * Mathf.Clamp01(t), barHeight),
                            Texture2D.whiteTexture);

            GUI.color = new Color(1f, 0.55f, 0.15f, 1f);
            GUI.DrawTexture(new Rect(barRect.x + barWidth * thresholdMarker - 1f,
                                     barRect.y - 3f, 2f, barHeight + 6f),
                            Texture2D.whiteTexture);

            GUI.color = prev;
        }
    }
}
