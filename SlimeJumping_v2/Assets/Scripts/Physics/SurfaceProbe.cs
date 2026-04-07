using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Settings;

namespace SlimeJumping.Physics
{
    public class SurfaceProbe
    {
        private readonly Rigidbody2D rb;
        private readonly SlimeSettings settings;
        private readonly List<Collider2D> ignoredColliders;

        private static readonly Vector2[] Directions = {
            Vector2.down, Vector2.up, Vector2.left, Vector2.right,
            new(-0.707f, -0.707f), new(0.707f, -0.707f),
            new(-0.707f, 0.707f), new(0.707f, 0.707f)
        };

        public SurfaceProbe(Rigidbody2D rb, SlimeSettings settings, List<Collider2D> ignoredColliders)
        {
            this.rb = rb;
            this.settings = settings;
            this.ignoredColliders = ignoredColliders;
        }

        public bool CheckSurface(out Vector2 normal, out Collider2D col, float distMultiplier, float inputX)
        {
            float radius = settings.slimeRadius;
            float dist = settings.surfaceCheckDistance * distMultiplier;
            LayerMask mask = settings.groundLayer;

            Vector2 pos = rb.position;

            normal = Vector2.up;
            col = null;
            float bestDist = float.MaxValue;
            float bestScore = -10f;
            float distTolerance = dist * 0.5f;

            foreach (var d in Directions)
            {
                RaycastHit2D[] hits = Physics2D.CircleCastAll(pos, radius * 0.9f, d, dist, mask);
                foreach (var h in hits)
                {
                    if (ignoredColliders.Contains(h.collider)) continue;

                    Vector2 toHit = h.point - pos;
                    float below = toHit.sqrMagnitude > 0.0001f
                        ? Vector2.Dot(toHit.normalized, Vector2.down)
                        : 0f;

                    float inputAlign = Mathf.Abs(inputX) > 0.01f ? -h.normal.x * inputX : 0f;

                    float score = below + inputAlign * 0.5f;

                    if (col == null || h.distance < bestDist - distTolerance)
                    {
                        bestDist = h.distance;
                        bestScore = score;
                        normal = h.normal;
                        col = h.collider;
                    }
                    else if (h.distance < bestDist + distTolerance && score > bestScore)
                    {
                        bestDist = h.distance;
                        bestScore = score;
                        normal = h.normal;
                        col = h.collider;
                    }
                }
            }

            return col != null;
        }

        public void SnapToSurface(Collider2D surfaceCollider, Vector2 surfaceNormal)
        {
            if (surfaceCollider == null) return;

            Vector2 pos = rb.position;
            Vector2 closest = surfaceCollider.ClosestPoint(pos);
            float currentDist = Vector2.Distance(pos, closest);
            float targetDist = settings.slimeRadius;

            if (Mathf.Abs(currentDist - targetDist) > 0.001f)
            {
                Vector2 pushDir = (pos - closest).normalized;
                if (pushDir.sqrMagnitude < 0.001f) pushDir = surfaceNormal;
                rb.position = closest + pushDir * targetDist;
            }
        }

        public static float GetSurfaceAngle(Vector2 surfaceNormal)
        {
            return Mathf.Acos(Mathf.Abs(Vector2.Dot(surfaceNormal, Vector2.up))) * Mathf.Rad2Deg;
        }
    }
}
