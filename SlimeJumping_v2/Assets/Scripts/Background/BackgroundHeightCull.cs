using UnityEngine;

namespace SlimeJumping.Background
{
    /// <summary>Manager-style object. Hides backdrop roots' renderers once the
    /// camera climbs past a height, so only the skybox remains behind the player.
    /// Purely visual (forceRenderingOff) — colliders and logic keep running.
    /// Hysteresis gap avoids flicker while the camera hovers near the line.
    /// Removing the object is a full rollback.</summary>
    public class BackgroundHeightCull : MonoBehaviour
    {
        [Tooltip("Camera whose height drives the cull. Empty = Camera.main.")]
        [SerializeField] private UnityEngine.Camera targetCamera;
        [Tooltip("Roots whose child renderers get hidden.")]
        [SerializeField] private GameObject[] targets;

        [Header("Cull (camera world Y)")]
        [Tooltip("Camera height where the backdrop hides.")]
        [SerializeField] private float hideAboveY = 360f;
        [Tooltip("Camera height where the backdrop shows again.")]
        [SerializeField] private float showBelowY = 355f;
        [Tooltip("Half-width of the threshold gizmo lines.")]
        [SerializeField] private float gizmoHalfWidth = 120f;

        private UnityEngine.Camera cam;
        private Renderer[] renderers;
        private bool hidden;

        private void OnEnable()
        {
            cam = targetCamera != null ? targetCamera : UnityEngine.Camera.main;
            if (cam == null || targets == null || targets.Length == 0)
            {
                enabled = false;
                return;
            }
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var t in targets)
            {
                if (t != null) list.AddRange(t.GetComponentsInChildren<Renderer>(true));
            }
            renderers = list.ToArray();
        }

        private void LateUpdate()
        {
            float y = cam.transform.position.y;
            if (!hidden && y > hideAboveY) SetHidden(true);
            else if (hidden && y < showBelowY) SetHidden(false);
        }

        private void SetHidden(bool value)
        {
            hidden = value;
            foreach (var r in renderers)
            {
                if (r != null) r.forceRenderingOff = value;
            }
        }

        private void OnDisable()
        {
            if (renderers != null && hidden) SetHidden(false);
        }

        private void OnDrawGizmosSelected()
        {
            float x = transform.position.x;
            Gizmos.color = new Color(1f, 0.4f, 0.4f, 1f);
            var a = new Vector3(x - gizmoHalfWidth, hideAboveY, 0f);
            Gizmos.DrawLine(a, a + Vector3.right * (gizmoHalfWidth * 2f));
            Gizmos.color = new Color(0.4f, 1f, 0.4f, 1f);
            var b = new Vector3(x - gizmoHalfWidth, showBelowY, 0f);
            Gizmos.DrawLine(b, b + Vector3.right * (gizmoHalfWidth * 2f));
        }
    }
}
