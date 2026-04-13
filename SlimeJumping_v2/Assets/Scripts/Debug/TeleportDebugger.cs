using UnityEngine;
using UnityEngine.InputSystem;
using SlimeJumping.Controller;

namespace SlimeJumping.Debugging
{
    /// <summary>
    /// Debug helper: E saves slime position, R teleports slime back with cleared physics state.
    /// </summary>
    public class TeleportDebugger : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private Key saveKey = Key.E;
        [SerializeField] private Key recallKey = Key.R;

        [Header("Gizmo")]
        [SerializeField] private Color gizmoColor = new Color(0.2f, 1f, 0.4f, 0.9f);
        [SerializeField] private float gizmoRadius = 0.4f;

        private Vector2 savedPosition;
        private bool hasSavedPosition;

        private void Reset()
        {
            slimeController = GetComponent<SlimeController>();
        }

        private void Awake()
        {
            if (slimeController == null)
                slimeController = GetComponent<SlimeController>();
        }

        private void Update()
        {
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
            }
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
