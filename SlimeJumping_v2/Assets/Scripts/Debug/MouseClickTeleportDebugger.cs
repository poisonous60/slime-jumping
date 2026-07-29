using UnityEngine;
using UnityEngine.InputSystem;
using SlimeJumping.Controller;
using SlimeJumping.Settings;

namespace SlimeJumping.Debugging
{
    /// <summary>Debug helper: mouse click teleports the slime to the clicked world position.</summary>
    public class MouseClickTeleportDebugger : MonoBehaviour
    {
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private UnityEngine.Camera targetCamera;
        [SerializeField] private bool useRightButton;

        private void Reset()
        {
            slimeController = GetComponent<SlimeController>();
        }

        private void Awake()
        {
            if (slimeController == null)
                slimeController = GetComponent<SlimeController>();
            if (targetCamera == null)
                targetCamera = UnityEngine.Camera.main;
        }

        private void Update()
        {
            if (!GameOptions.MouseTeleportDebugEnabled) return;

            var mouse = Mouse.current;
            if (mouse == null || slimeController == null) return;

            var cam = targetCamera != null ? targetCamera : UnityEngine.Camera.main;
            if (cam == null) return;

            bool pressed = useRightButton
                ? mouse.rightButton.wasPressedThisFrame
                : mouse.leftButton.wasPressedThisFrame;
            if (!pressed) return;

            Vector2 screenPos = mouse.position.ReadValue();
            float depth = Mathf.Abs(cam.transform.position.z);
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
            slimeController.TeleportTo(new Vector2(world.x, world.y), Vector2.zero);
        }
    }
}
