using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Rendering
{
    /// Drops trail stamps from slime state. One-way subscriber, never pushes back to the controller.
    [AddComponentMenu("Rendering/Trail/Trail Stamper")]
    public class TrailStamper : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private SlimeController slimeController;
        [SerializeField] private CircleCollider2D slimeCollider;
        // Manager resolved at runtime via WorldTrailManager.Instance — a serialized ref would go
        // dead on a persistent prefab once the owning scene unloads.

        [Header("Continuous (while Attached/Charging)")]
        [Tooltip("Off by default: trail only marks landings. Enable for a crawl streak between landings.")]
        [SerializeField] private bool stampOnCrawl = false;
        [SerializeField] private float moveStep = 0.12f;
        [SerializeField] private float moveRadius = 0.25f;
        [Range(0f, 1f)] [SerializeField] private float moveStrength = 0.18f;

        [Header("Landing — radius/strength driven by normal impact speed")]
        [Tooltip("Below this normal (perpendicular-to-surface) speed the curves sample x=0.")]
        [SerializeField] private float landingSpeedMin = 0f;
        [Tooltip("At this normal speed the curves sample x=1. Higher speeds clamp to the curves' last key.")]
        [SerializeField] private float landingSpeedMax = 15f;
        [Tooltip("X: normalized normal impact speed (0 at speedMin, 1 at speedMax).\nY: landing stamp radius in world units.")]
        [SerializeField] private AnimationCurve landingRadiusCurve = new AnimationCurve(
            new Keyframe(0f, 0.35f, 0f, 0.25f),
            new Keyframe(1f, 0.7f,  0.25f, 0f));
        [Tooltip("X: normalized normal impact speed.\nY: landing stamp mask strength (0..1).")]
        [SerializeField] private AnimationCurve landingStrengthCurve = new AnimationCurve(
            new Keyframe(0f, 0.3f, 0f, 0.6f),
            new Keyframe(1f, 1.0f, 0.3f, 0f));

        [Header("Safety")]
        [Tooltip("If the slime moves more than this distance in one FixedUpdate, skip interpolation (respawn / warp).")]
        [SerializeField] private float teleportCutoff = 3f;

        [Header("Debug")]
        [Tooltip("Log the tangent speed + curve outputs on every landing so you can tune the curves against real numbers.")]
        [SerializeField] private bool logLandings = false;

        private Vector2 lastStampPos;
        private Collider2D lastSurface;
        private bool hasLastStamp;

        private void Reset()
        {
            slimeController = GetComponentInParent<SlimeController>();
            slimeCollider = GetComponent<CircleCollider2D>();
            if (slimeCollider == null) slimeCollider = GetComponentInParent<CircleCollider2D>();
        }

        private void OnEnable()
        {
            if (slimeController == null) return;
            slimeController.OnLanded += HandleLanded;
            slimeController.OnCeilingBump += HandleCeilingBump;
        }

        private void OnDisable()
        {
            if (slimeController == null) return;
            slimeController.OnLanded -= HandleLanded;
            slimeController.OnCeilingBump -= HandleCeilingBump;
        }

        private void HandleLanded(Vector2 position, Vector2 surfaceNormal)
        {
            // Normal (perpendicular) impact speed, not tangent speed — tangent reads as a slide,
            // not a splat.
            float impactSpeed = slimeController != null
                ? slimeController.ImpactNormalSpeed
                : 0f;

            if (!StampImpact(position, surfaceNormal, impactSpeed, "Land")) return;

            lastStampPos = position;
            lastSurface = slimeController != null ? slimeController.SurfaceCollider : null;
            hasLastStamp = true;
        }

        // Head hit on a ceiling: no attach, so the crawl fields stay untouched — mark only.
        private void HandleCeilingBump(Vector2 position, Vector2 surfaceNormal, float normalSpeed)
        {
            StampImpact(position, surfaceNormal, normalSpeed, "Ceiling");
        }

        private bool StampImpact(Vector2 position, Vector2 surfaceNormal, float impactSpeed, string kind)
        {
            float denom = Mathf.Max(0.01f, landingSpeedMax - landingSpeedMin);
            float t = (impactSpeed - landingSpeedMin) / denom;   // unclamped — curve's wrap mode decides extrapolation
            float radius   = Mathf.Max(0f, landingRadiusCurve.Evaluate(t));
            float strength = Mathf.Clamp01(landingStrengthCurve.Evaluate(t));

            if (logLandings)
            {
                Collider2D surf = slimeController != null ? slimeController.SurfaceCollider : null;
                bool routedLocal = surf != null && TrailColliderRegistry.TryGet(surf, out var lr) && lr != null;
                Debug.Log($"[Trail] {kind}: pos={position} n={surfaceNormal} up={surfaceNormal.y:0.00}"
                    + $" surf={(surf != null ? surf.name : "null")} route={(routedLocal ? "local" : "world")}"
                    + $" normalSpeed={impactSpeed:0.00} t={t:0.00} → radius={radius:0.00} strength={strength:0.00}");
            }

            if (radius < 0.001f || strength < 0.001f) return false;   // nothing visible to draw

            RouteStamp(position, radius, strength);
            return true;
        }

        private void FixedUpdate()
        {
            if (!stampOnCrawl) return;
            if (slimeController == null) return;

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

            if (dist < moveStep * 0.5f) return;

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
            // ClosestPoint, not `center - normal*r`: at concave corners SurfaceNormal is the
            // aggregate bisector and that formula lands in mid-air between the two surfaces.
            Collider2D primary = slimeController.SurfaceCollider;
            if (primary != null) return primary.ClosestPoint(center);
            if (slimeCollider == null) return center;
            float r = slimeCollider.radius * slimeCollider.transform.lossyScale.x;
            return center - slimeController.SurfaceNormal * r;
        }

        private void RouteStamp(Vector2 worldPos, float radius, float strength)
        {
            if (slimeController == null) return;
            Collider2D surface = slimeController.SurfaceCollider;
            if (surface != null && TrailColliderRegistry.TryGet(surface, out var local) && local != null)
            {
                local.StampAt(worldPos, radius, strength);
                return;
            }
            // Fall through to the world RT. A scene with no WorldTrailManager is legitimate
            // (trail disabled there), so skip silently.
            var mgr = WorldTrailManager.Instance;
            if (mgr != null) mgr.Stamp(worldPos, radius, strength);
        }
    }
}
