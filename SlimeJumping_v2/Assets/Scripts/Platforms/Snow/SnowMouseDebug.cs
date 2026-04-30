using UnityEngine;
using UnityEngine.InputSystem;

namespace SlimeJumping.Platforms.Snow
{
    /// <summary>
    /// Runtime debug helper: left-click on a SnowField host (or hold the button to
    /// stamp continuously) and the snow carves at the click point as if the slime
    /// had just landed there with the configured impact speed. Re-uses the existing
    /// HandleLanded → Carve → strip → recovery pipeline — no separate write path.
    ///
    /// Designed for live tuning of the snow feel: stamp a few craters, watch them
    /// recover, tweak SnowSettings curves, repeat. Add to any scene GameObject;
    /// no slime / collider relationship required.
    /// </summary>
    [AddComponentMenu("SlimeJumping/Platforms/Snow/Snow Mouse Debug")]
    public sealed class SnowMouseDebug : MonoBehaviour
    {
        [Tooltip("Camera used to project the cursor into world space. Defaults to Camera.main on Awake.")]
        [SerializeField] private UnityEngine.Camera targetCamera;

        [Tooltip("Raw normal-impact speed pumped through SnowField.HandleLanded — mapped to depth/radius by the field's SnowSettings. 5 ≈ a brisk slime drop. Below the SnowSettings minImpactSpeed gate, nothing happens.")]
        [SerializeField] private float debugImpactSpeed = 5f;

        [Tooltip("Layers to test for a SnowField host under the cursor. Should match the host platforms' layer.")]
        [SerializeField] private LayerMask hostLayerMask = ~0;

        [Tooltip("Hold left-click to keep stamping each FixedUpdate-equivalent (Update tick) at the cursor — useful for sweeping carves. OFF = stamp only on press.")]
        [SerializeField] private bool stampWhileHeld = true;

        [Tooltip("Use right mouse button instead of left.")]
        [SerializeField] private bool useRightButton = false;

        [Tooltip("Draw a circle gizmo at the last click point (Scene view).")]
        [SerializeField] private bool drawCursorGizmo = true;

        Vector2 lastClickWorld;
        bool hasLastClick;

        void Awake()
        {
            if (targetCamera == null) targetCamera = UnityEngine.Camera.main;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;
            var cam = targetCamera != null ? targetCamera : UnityEngine.Camera.main;
            if (cam == null) return;

            var btn = useRightButton ? mouse.rightButton : mouse.leftButton;
            bool trigger = stampWhileHeld
                ? btn.isPressed
                : btn.wasPressedThisFrame;
            if (!trigger) return;

            Vector2 screenPos = mouse.position.ReadValue();
            // Slime project is 2.5D — the camera looks down -Z at z=0 platforms.
            // ScreenToWorldPoint with z = -cam.transform.position.z lands on the z=0
            // plane regardless of perspective vs orthographic.
            float z = Mathf.Abs(cam.transform.position.z);
            Vector3 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, z));
            Vector2 worldXY = new Vector2(world.x, world.y);

            var hit = Physics2D.OverlapPoint(worldXY, hostLayerMask);
            if (hit == null) return;

            if (!SnowFieldRegistry.TryGet(hit, out var field) || field == null) return;

            field.HandleLanded(worldXY, debugImpactSpeed);
            lastClickWorld = worldXY;
            hasLastClick = true;
        }

        void OnDrawGizmos()
        {
            if (!drawCursorGizmo || !hasLastClick) return;
            Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(new Vector3(lastClickWorld.x, lastClickWorld.y, 0f), 0.4f);
        }
    }
}
