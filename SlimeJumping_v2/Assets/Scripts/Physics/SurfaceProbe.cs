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

        private readonly Collider2D[] overlapBuffer = new Collider2D[16];
        private readonly Collider2D[] depenetrationBuffer = new Collider2D[16];

        // Per-frame contact data (reused to avoid GC)
        private readonly Collider2D[] contactColliders = new Collider2D[16];
        private readonly Vector2[] contactNormals = new Vector2[16];
        private readonly float[] contactDistances = new float[16];
        private int contactCount;

        // Finding 1: preserve last valid normal for deep penetration fallback
        private Vector2 lastGoodNormal = Vector2.up;

        public SurfaceProbe(Rigidbody2D rb, SlimeSettings settings, List<Collider2D> ignoredColliders)
        {
            this.rb = rb;
            this.settings = settings;
            this.ignoredColliders = ignoredColliders;
        }

        public bool CheckSurface(out Vector2 normal, out Collider2D col,
            float distMultiplier, float inputX, Collider2D prevCollider)
        {
            float radius = settings.slimeRadius;
            float detectRange = settings.surfaceCheckDistance * distMultiplier;
            float contactSkin = settings.contactSkin;
            LayerMask mask = settings.groundLayer;
            Vector2 pos = rb.position;

            normal = Vector2.up;
            col = null;
            contactCount = 0;

            // Find all nearby colliders
            int overlapCount = Physics2D.OverlapCircleNonAlloc(pos, radius + detectRange, overlapBuffer, mask);

            bool hasNearby = false;

            for (int i = 0; i < overlapCount; i++)
            {
                if (ignoredColliders.Contains(overlapBuffer[i])) continue;

                hasNearby = true;

                Vector2 closest = overlapBuffer[i].ClosestPoint(pos);
                Vector2 toSlime = pos - closest;

                // Deep penetration: ClosestPoint returned query point itself — skip
                if (toSlime.sqrMagnitude < 0.0001f) continue;

                float dist = toSlime.magnitude;
                Vector2 n = toSlime / dist;

                contactColliders[contactCount] = overlapBuffer[i];
                contactNormals[contactCount] = n;
                contactDistances[contactCount] = dist;
                contactCount++;

                if (contactCount >= contactColliders.Length) break;
            }

            if (contactCount == 0)
            {
                // Edge case: all nearby colliders deeply penetrated
                // Use prevCollider with last known normal; otherwise treat as no surface
                if (hasNearby && prevCollider != null)
                {
                    col = prevCollider;
                    normal = lastGoodNormal;
                    return true;
                }
                return false;
            }

            // Identify active contacts (within slimeRadius + contactSkin)
            // and select primary using 5-step fallback
            int selected = SelectPrimary(radius, contactSkin, inputX, prevCollider);
            normal = contactNormals[selected];
            col = contactColliders[selected];
            lastGoodNormal = normal;
            return true;
        }

        private int SelectPrimary(float radius, float contactSkin, float inputX, Collider2D prevCollider)
        {
            // Count active contacts and find closest overall
            int activeCount = 0;
            int firstActive = -1;
            int closestIdx = 0;
            float closestDist = contactDistances[0];

            for (int i = 0; i < contactCount; i++)
            {
                if (contactDistances[i] < closestDist)
                {
                    closestDist = contactDistances[i];
                    closestIdx = i;
                }
                if (contactDistances[i] <= radius + contactSkin)
                {
                    activeCount++;
                    if (firstActive < 0) firstActive = i;
                }
            }

            // No active contacts — select closest (landing detection)
            if (activeCount == 0) return closestIdx;

            // Step 1: Single active contact — no choice needed
            if (activeCount == 1) return firstActive;

            // Step 2: Penetration wins — deepest penetrating contact
            int deepestIdx = -1;
            float deepestPen = 0f;
            for (int i = 0; i < contactCount; i++)
            {
                if (contactDistances[i] > radius + contactSkin) continue;
                float pen = radius - contactDistances[i];
                if (pen > deepestPen)
                {
                    deepestPen = pen;
                    deepestIdx = i;
                }
            }
            if (deepestIdx >= 0 && deepestPen > 0.001f) return deepestIdx;

            // Step 3: Input tiebreaker — surface slime is pressing into
            if (Mathf.Abs(inputX) > 0.01f)
            {
                Vector2 inputDir = new Vector2(inputX, 0f).normalized;
                int bestIdx = -1;
                float bestDot = float.MinValue;
                float secondDot = float.MinValue;

                for (int i = 0; i < contactCount; i++)
                {
                    if (contactDistances[i] > radius + contactSkin) continue;
                    float d = -Vector2.Dot(contactNormals[i], inputDir);
                    if (d > bestDot)
                    {
                        secondDot = bestDot;
                        bestDot = d;
                        bestIdx = i;
                    }
                    else if (d > secondDot)
                    {
                        secondDot = d;
                    }
                }

                if (bestIdx >= 0 && bestDot - secondDot > 0.01f) return bestIdx;
            }

            // Step 4: Continuity — prevCollider if in active set
            if (prevCollider != null)
            {
                for (int i = 0; i < contactCount; i++)
                {
                    if (contactDistances[i] > radius + contactSkin) continue;
                    if (contactColliders[i] == prevCollider) return i;
                }
            }

            // Step 5: Gravity alignment — most floor-like
            {
                int bestIdx = firstActive;
                float bestGrav = float.MinValue;
                for (int i = 0; i < contactCount; i++)
                {
                    if (contactDistances[i] > radius + contactSkin) continue;
                    float g = Vector2.Dot(contactNormals[i], Vector2.up);
                    if (g > bestGrav)
                    {
                        bestGrav = g;
                        bestIdx = i;
                    }
                }
                return bestIdx;
            }
        }

        public void SnapToSurface(Collider2D surfaceCollider, Vector2 surfaceNormal)
        {
            if (surfaceCollider == null) return;

            float radius = settings.slimeRadius;
            float skin = settings.depenetrationSkin;
            float contactSkin = settings.contactSkin;
            LayerMask mask = settings.groundLayer;
            Vector2 pos = rb.position;

            // Phase A: Depenetrate against all nearby colliders (push only)
            bool anyPush = false;
            int count = Physics2D.OverlapCircleNonAlloc(pos, radius + skin, depenetrationBuffer, mask);

            for (int iter = 0; iter < 3; iter++)
            {
                // Re-query after position changed from previous iteration
                if (iter > 0 && anyPush)
                    count = Physics2D.OverlapCircleNonAlloc(pos, radius + skin, depenetrationBuffer, mask);

                bool anyPenetration = false;
                for (int i = 0; i < count; i++)
                {
                    if (ignoredColliders.Contains(depenetrationBuffer[i])) continue;

                    Vector2 closest = depenetrationBuffer[i].ClosestPoint(pos);
                    Vector2 toSlime = pos - closest;
                    float dist = toSlime.magnitude;

                    // Deep penetration: skip (no reliable push direction)
                    if (dist < 0.0001f) continue;

                    if (dist < radius)
                    {
                        pos += (toSlime / dist) * (radius - dist);
                        anyPenetration = true;
                        anyPush = true;
                    }
                }
                if (!anyPenetration) break;
            }

            // Phase B: Primary pull — only when Phase A found nothing to fix (genuine drift).
            // If Phase A pushed, slime is on Minkowski boundary — pulling back risks oscillation.
            if (!anyPush)
            {
                Vector2 closest = surfaceCollider.ClosestPoint(pos);
                Vector2 toSlime = pos - closest;
                float dist = toSlime.magnitude;

                if (dist > radius + contactSkin && dist > 0.0001f)
                {
                    pos = closest + (toSlime / dist) * radius;
                }
            }

            rb.position = pos;
        }

        public static float GetSurfaceAngle(Vector2 surfaceNormal)
        {
            return Mathf.Acos(Mathf.Abs(Vector2.Dot(surfaceNormal, Vector2.up))) * Mathf.Rad2Deg;
        }
    }
}
