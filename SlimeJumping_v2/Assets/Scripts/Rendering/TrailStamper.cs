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
        [SerializeField] private CircleCollider2D slimeCollider;
        // The trail manager is resolved at runtime via WorldTrailManager.Instance — no serialized
        // reference so cross-scene stampers (e.g. on a persistent slime prefab) can't hold a dead
        // reference to a manager from an unloaded scene.

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
            if (slimeController != null) slimeController.OnLanded += HandleLanded;
        }

        private void OnDisable()
        {
            if (slimeController != null) slimeController.OnLanded -= HandleLanded;
        }

        private void HandleLanded(Vector2 position, Vector2 surfaceNormal)
        {
            // Normal (perpendicular-to-surface) impact speed — how hard the slime slammed into
            // the surface, which drives the "splat" feel better than the tangent (slide) speed.
            // Controller already captures this at landing for the energy-conversion path.
            float impactSpeed = slimeController != null
                ? slimeController.ImpactNormalSpeed
                : 0f;

            float denom = Mathf.Max(0.01f, landingSpeedMax - landingSpeedMin);
            float t = (impactSpeed - landingSpeedMin) / denom;   // unclamped — curve's wrap mode decides extrapolation
            float radius   = Mathf.Max(0f, landingRadiusCurve.Evaluate(t));
            float strength = Mathf.Clamp01(landingStrengthCurve.Evaluate(t));

            if (logLandings)
            {
                Debug.Log($"[Trail] Land: normalSpeed={impactSpeed:0.00} t={t:0.00} → radius={radius:0.00} strength={strength:0.00}");
            }

            if (radius < 0.001f || strength < 0.001f) return;     // nothing visible to draw

            RouteStamp(position, radius, strength);
            lastStampPos = position;
            lastSurface = slimeController != null ? slimeController.SurfaceCollider : null;
            hasLastStamp = true;
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
            // Primary collider's closest point matches where the stamp routes to, and
            // stays correct at concave corners where SurfaceNormal is the aggregate
            // bisector (which would place `center - n*r` in mid-air between surfaces).
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
            // No local receiver on the contact surface → fall through to the world RT, if any.
            // Scene may legitimately have no WorldTrailManager (trail disabled in that scene);
            // silently skip instead of throwing.
            var mgr = WorldTrailManager.Instance;
            if (mgr != null) mgr.Stamp(worldPos, radius, strength);
        }
    }
}
