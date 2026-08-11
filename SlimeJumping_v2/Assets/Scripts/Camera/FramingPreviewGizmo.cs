using UnityEngine;
using Unity.Cinemachine;

namespace SlimeJumping.Camera
{
    /// <summary>Scene-view preview of the world rect each aspect ratio frames around the slime.
    /// Reads the live ResolutionScaleController values, so what it draws is what Play mode frames.
    /// Resolution alone never changes a rect — only the width:height ratio does.
    /// Drawn only while this GameObject is selected.</summary>
    public class FramingPreviewGizmo : MonoBehaviour
    {
        [Tooltip("What the rects are centered on. The camera frames this.")]
        [SerializeField] private Transform target;
        [SerializeField] private ResolutionScaleController scaler;
        [Tooltip("Supplies the camera distance and orientation the rects are measured at.")]
        [SerializeField] private CinemachinePositionComposer composer;

        [Tooltip("Ratios to preview as width:height. Order sets the color ramp.")]
        [SerializeField] private Vector2Int[] previewAspects =
        {
            new Vector2Int(4, 3),
            new Vector2Int(3, 2),
            new Vector2Int(16, 10),
            new Vector2Int(16, 9),
            new Vector2Int(21, 9),
            new Vector2Int(32, 9),
        };

        [Tooltip("Also outline the aspect the Game view is currently set to.")]
        [SerializeField] private bool drawCurrentGameView = true;
        [SerializeField] private bool drawLabels = true;

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (scaler == null || composer == null || target == null) return;

            float refFov = scaler.ReferenceFov;
            float targetAspect = scaler.TargetAspect;
            float distance = composer.CameraDistance;
            Gizmos.matrix = Matrix4x4.TRS(target.position, composer.transform.rotation, Vector3.one);

            for (int i = 0; i < previewAspects.Length; i++)
            {
                var wh = previewAspects[i];
                if (wh.x <= 0 || wh.y <= 0) continue;
                float hue = previewAspects.Length <= 1 ? 0f : (float)i / previewAspects.Length;
                DrawRect((float)wh.x / wh.y, refFov, targetAspect, distance,
                    Color.HSVToRGB(hue, 0.75f, 1f), wh.x + ":" + wh.y, i * 0.6f);
            }

            if (drawCurrentGameView)
            {
                Vector2 gv = UnityEditor.Handles.GetMainGameViewSize();
                DrawRect(gv.x / Mathf.Max(1f, gv.y), refFov, targetAspect, distance,
                    Color.white, "Game view " + (int)gv.x + "x" + (int)gv.y, -0.6f);
            }

            // Authored rect: the guaranteed-visible core every other rect encloses.
            DrawRect(targetAspect, refFov, targetAspect, distance, new Color(1f, 0.9f, 0.2f), null, 0f);
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.35f);
            Gizmos.DrawSphere(Vector3.zero, distance * 0.008f);
            Gizmos.matrix = Matrix4x4.identity;
        }

        private void DrawRect(float aspect, float refFov, float targetAspect, float distance,
            Color color, string label, float labelStagger)
        {
            float fov = ResolutionScaleController.FovForAspect(refFov, targetAspect, aspect);
            float halfH = distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * aspect;

            Gizmos.color = color;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(halfW * 2f, halfH * 2f, 0f));

            if (!drawLabels || string.IsNullOrEmpty(label)) return;
            var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
            style.normal.textColor = color;
            UnityEditor.Handles.matrix = Gizmos.matrix;
            UnityEditor.Handles.Label(new Vector3(-halfW, halfH + labelStagger, 0f),
                label + "  fov " + fov.ToString("0.0") + "°  " +
                (halfW * 2f).ToString("0.0") + "x" + (halfH * 2f).ToString("0.0") + "u", style);
            UnityEditor.Handles.matrix = Matrix4x4.identity;
        }
#endif
    }
}
