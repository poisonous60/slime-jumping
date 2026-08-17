using UnityEngine;
using UnityEngine.InputSystem;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

namespace SlimeJumping.Debugging
{
    /// <summary>Debug helper: hotkey saves the slime position, another teleports back with cleared physics state.
    /// 옵션창의 savepoint 체크박스로 끈다 — 끄면 단축키도 Retry 표시도 죽는다.</summary>
    public class TeleportDebugger : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private Key saveKey = Key.E;
        [SerializeField] private Key recallKey = Key.LeftShift;
        [SerializeField] private Key resetCountKey = Key.Q;

        [Header("Gizmo")]
        [SerializeField] private Color gizmoColor = new Color(0.2f, 1f, 0.4f, 0.9f);
        [SerializeField] private float gizmoRadius = 0.4f;

        [Header("UI")]
        [SerializeField] private bool showRetryUI = true;
        [SerializeField] private int fontSize = 22;
        [SerializeField] private float topMargin = 16f;
        [SerializeField] private Color uiColor = new Color(1f, 0.85f, 0.2f, 1f);

        private Vector2 savedPosition;
        private bool hasSavedPosition;
        private int retryCount;
        private GUIStyle labelStyle;

        private void Reset()
        {
            slimeController = GetComponent<SlimeController>();
        }

        private void Awake()
        {
            if (slimeController == null)
                slimeController = GetComponent<SlimeController>();
        }

        private void Start()
        {
            if (!hasSavedPosition && slimeController != null)
            {
                savedPosition = slimeController.Position;
                hasSavedPosition = true;
            }
        }

        private void Update()
        {
            if (!GameOptions.SavepointEnabled) return;

            var keyboard = Keyboard.current;
            if (keyboard == null || slimeController == null) return;

            if (keyboard[saveKey].wasPressedThisFrame)
            {
                savedPosition = slimeController.Position;
                hasSavedPosition = true;
            }

            if (keyboard[recallKey].wasPressedThisFrame && hasSavedPosition)
            {
                slimeController.TeleportTo(savedPosition, Vector2.zero);
                retryCount++;
            }

            if (keyboard[resetCountKey].wasPressedThisFrame)
            {
                retryCount = 0;
            }
        }

        private void OnGUI()
        {
            if (!showRetryUI || !GameOptions.SavepointEnabled) return;

            if (labelStyle == null)
            {
                labelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.UpperCenter
                };
            }
            labelStyle.fontSize = fontSize;
            labelStyle.normal.textColor = uiColor;

            var rect = new Rect(0f, topMargin, Screen.width, fontSize + 8f);
            GUI.Label(rect, $"Retry: {retryCount}", labelStyle);
        }

        private void OnDrawGizmos()
        {
            if (!hasSavedPosition) return;

            Gizmos.color = gizmoColor;
            Vector3 pos = new Vector3(savedPosition.x, savedPosition.y, 0f);
            Gizmos.DrawWireSphere(pos, gizmoRadius);
            Gizmos.DrawLine(pos + Vector3.up * gizmoRadius, pos + Vector3.down * gizmoRadius);
            Gizmos.DrawLine(pos + Vector3.left * gizmoRadius, pos + Vector3.right * gizmoRadius);
        }
    }
}
