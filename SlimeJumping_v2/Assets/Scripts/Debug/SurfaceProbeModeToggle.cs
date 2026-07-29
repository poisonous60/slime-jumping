using UnityEngine;
using SlimeJumping.Controller;
using SlimeJumping.Physics;

namespace SlimeJumping.Debugging
{
    /// <summary>Hotkey toggle between Single/Multi surface probe for live A/B comparison in Play mode.</summary>
    public class SurfaceProbeModeToggle : MonoBehaviour
    {
        [SerializeField] private SlimeController slime;
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;
        [SerializeField] private bool showLabel = true;
        [SerializeField] private Vector2 labelScreenPosition = new Vector2(10f, 10f);

        private GUIStyle labelStyle;

        private void Awake()
        {
            if (slime == null) slime = FindAnyObjectByType<SlimeController>();
        }

        private void Update()
        {
            if (slime == null) return;
            if (!Input.GetKeyDown(toggleKey)) return;

            var next = slime.CurrentProbeMode == SurfaceProbeMode.Single
                ? SurfaceProbeMode.Multi
                : SurfaceProbeMode.Single;
            slime.SetProbeMode(next);
            Debug.Log($"[SurfaceProbeMode] switched to {next}");
        }

        private void OnGUI()
        {
            if (!showLabel || slime == null) return;

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold,
                };
                labelStyle.normal.textColor = Color.white;
            }

            string text = $"Probe: {slime.CurrentProbeMode}  [{toggleKey}]";
            var rect = new Rect(labelScreenPosition.x, labelScreenPosition.y, 260f, 22f);
            var shadow = new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height);
            var prev = labelStyle.normal.textColor;
            labelStyle.normal.textColor = Color.black;
            GUI.Label(shadow, text, labelStyle);
            labelStyle.normal.textColor = prev;
            GUI.Label(rect, text, labelStyle);
        }
    }
}
