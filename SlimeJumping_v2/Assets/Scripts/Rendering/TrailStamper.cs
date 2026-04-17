using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Rendering
{
    /// Drops trail stamps based on slime state. One-way subscriber to SlimeController
    /// events — never pushes back. Trail is purely visual.
    [AddComponentMenu("Rendering/Trail/Trail Stamper")]
    public class TrailStamper : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private WorldTrailManager trailManager;
        [SerializeField] private CircleCollider2D slimeCollider;

        [Header("Continuous (while Attached/Charging) — additive: 4-5 passes saturate to 1.0")]
        [SerializeField] private float moveStep = 0.12f;
        [SerializeField] private float moveRadius = 0.25f;
        [Range(0f, 1f)] [SerializeField] private float moveStrength = 0.18f;

        [Header("Landing")]
        [SerializeField] private float landingRadius = 0.55f;
        [Range(0f, 1f)] [SerializeField] private float landingStrength = 1f;

        [Header("Safety")]
        [Tooltip("If the slime moves more than this distance in one FixedUpdate, skip interpolation (respawn / warp).")]
        [SerializeField] private float teleportCutoff = 3f;

        private Vector2 lastStampPos;
        private Collider2D lastSurface;
        private bool hasLastStamp;

        private void Reset()
        {
            slimeController = GetComponentInParent<SlimeController>();
            trailManager = FindFirstObjectByType<WorldTrailManager>();
            slimeCollider = GetComponent<CircleCollider2D>();
            if (slimeCollider == null) slimeCollider = GetComponentInParent<CircleCollider2D>();
        }

        private void OnEnable()
        {
            if (slimeController != null) slimeController.OnLanded += HandleLanded;
        }

        private void OnDisable()
        {
            if (slimeController != null) slimeController.OnLanded -= HandleLanded;
        }

        private void HandleLanded(Vector2 position, Vector2 surfaceNormal)
        {
            RouteStamp(position, landingRadius, landingStrength);
            lastStampPos = position;
            lastSurface = slimeController != null ? slimeController.SurfaceCollider : null;
            hasLastStamp = true;
        }

        private void FixedUpdate()
        {
            if (slimeController == null || trailManager == null) return;

            var state = slimeController.CurrentState;
            if (state != SlimeController.SlimeState.Attached && state != SlimeController.SlimeState.Charging)
            {
                hasLastStamp = false;
                lastSurface = null;
                return;
            }

            Vector2 pos = ComputeContactPoint();
            Collider2D surface = slimeController.SurfaceCollider;

            if (!hasLastStamp || surface != lastSurface)
            {
                RouteStamp(pos, moveRadius, moveStrength);
                lastStampPos = pos;
                lastSurface = surface;
                hasLastStamp = true;
                return;
            }

            float dist = Vector2.Distance(pos, lastStampPos);
            if (dist > teleportCutoff)
            {
                RouteStamp(pos, moveRadius, moveStrength);
                lastStampPos = pos;
                return;
            }

            if (dist < moveStep * 0.5f) return;  // too close to last stamp, skip

            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / moveStep));
            for (int i = 1; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(lastStampPos, pos, (float)i / steps);
                RouteStamp(p, moveRadius, moveStrength);
            }
            lastStampPos = pos;
        }

        private Vector2 ComputeContactPoint()
        {
            Vector2 center = slimeController.Position;
            if (slimeCollider == null) return center;
            float r = slimeCollider.radius * slimeCollider.transform.lossyScale.x;
            return center - slimeController.SurfaceNormal * r;
        }

        private void RouteStamp(Vector2 worldPos, float radius, float strength)
        {
            Collider2D surface = slimeController.SurfaceCollider;
            if (surface != null && TrailColliderRegistry.TryGet(surface, out var local) && local != null)
            {
                local.StampAt(worldPos, radius, strength);
                return;
            }
            trailManager.Stamp(worldPos, radius, strength);
        }
    }
}
