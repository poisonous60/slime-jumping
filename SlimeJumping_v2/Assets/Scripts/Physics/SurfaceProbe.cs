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
        private readonly bool[] contactActive = new bool[16];
        private int contactCount;

        // Diagnostic state
        private int activeCount;
        private int selectedIdx = -1;
        private int phaseAIterCount;
        private float phaseARemainPen;

        // Diagnostic accessors
        public int ContactCount => contactCount;
        public int ActiveContactCount => activeCount;
        public int SelectedIndex => selectedIdx;
        public int PhaseAIterations => phaseAIterCount;
        public float PhaseARemainingPenetration => phaseARemainPen;
        public Collider2D GetContactCollider(int i) => contactColliders[i];
        public Vector2 GetContactNormal(int i) => contactNormals[i];
        public float GetContactDistance(int i) => contactDistances[i];
        public bool IsContactActive(int i) => contactActive[i];

        // Finding 1: preserve last valid normal for deep penetration fallback
        private Vector2 lastGoodNormal = Vector2.up;

        public SurfaceProbe(Rigidbody2D rb, SlimeSettings settings, List<Collider2D> ignoredColliders)
        {
            this.rb = rb;
            this.settings = settings;
            this.ignoredColliders = ignoredColliders;
        }

        public bool CheckSurface(out Vector2 normal, out Collider2D col,
            float distMultiplier, float inputX, Collider2D prevCollider,
            Vector2 gravityUp, Vector2 gravityRight)
        {
            float radius = settings.slimeRadius;
            float detectRange = settings.surfaceCheckDistance * distMultiplier;
            float contactSkin = settings.contactSkin;
            LayerMask mask = settings.groundLayer;
            Vector2 pos = rb.position;

            normal = gravityUp;
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
                activeCount = 0;
                selectedIdx = -1;
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
            int selected = SelectPrimary(radius, contactSkin, inputX, prevCollider, gravityUp, gravityRight);
            selectedIdx = selected;
            normal = contactNormals[selected];
            col = contactColliders[selected];
            lastGoodNormal = normal;
            return true;
        }

        private int SelectPrimary(float radius, float contactSkin, float inputX, Collider2D prevCollider,
            Vector2 gravityUp, Vector2 gravityRight)
        {
            // Count active contacts and find closest overall
            int localActiveCount = 0;
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
                bool isActive = contactDistances[i] <= radius + contactSkin;
                contactActive[i] = isActive;
                if (isActive)
                {
                    localActiveCount++;
                    if (firstActive < 0) firstActive = i;
                }
            }
            activeCount = localActiveCount;

            // No active contacts — select closest (landing detection)
            if (localActiveCount == 0) return closestIdx;

            // Step 1: Single active contact — no choice needed
            if (localActiveCount == 1) return firstActive;

            // Step 1.5: Ceiling filter — when active contacts mix ceiling and
            // non-ceiling, exclude ceiling contacts from steps 2-5.
            // Prevents a marginally-deeper ceiling from winning over a valid floor.
            bool filterCeiling = false;
            float ceilThresh = settings.ceilingThreshold;
            {
                int attachableCount = 0;
                int firstAttachable = -1;
                for (int i = 0; i < contactCount; i++)
                {
                    if (!contactActive[i]) continue;
                    if (Vector2.Dot(contactNormals[i], gravityUp) >= ceilThresh)
                    {
                        attachableCount++;
                        if (firstAttachable < 0) firstAttachable = i;
                    }
                }
                if (attachableCount > 0 && attachableCount < localActiveCount)
                {
                    filterCeiling = true;
                    firstActive = firstAttachable;
                }
            }

            // Step 2: Penetration wins — deepest penetrating contact
            int deepestIdx = -1;
            float deepestPen = 0f;
            for (int i = 0; i < contactCount; i++)
            {
                if (!contactActive[i]) continue;
                if (filterCeiling && Vector2.Dot(contactNormals[i], gravityUp) < ceilThresh) continue;
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
                Vector2 inputDir = (gravityRight * inputX).normalized;
                int bestIdx = -1;
                float bestDot = float.MinValue;
                float secondDot = float.MinValue;

                for (int i = 0; i < contactCount; i++)
                {
                    if (!contactActive[i]) continue;
                    if (filterCeiling && Vector2.Dot(contactNormals[i], gravityUp) < ceilThresh) continue;
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
                    if (!contactActive[i]) continue;
                    if (filterCeiling && Vector2.Dot(contactNormals[i], gravityUp) < ceilThresh) continue;
                    if (contactColliders[i] == prevCollider) return i;
                }
            }

            // Step 5: Gravity alignment — most floor-like
            {
                int bestIdx = firstActive;
                float bestGrav = float.MinValue;
                for (int i = 0; i < contactCount; i++)
                {
                    if (!contactActive[i]) continue;
                    if (filterCeiling && Vector2.Dot(contactNormals[i], gravityUp) < ceilThresh) continue;
                    float g = Vector2.Dot(contactNormals[i], gravityUp);
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
            int iterationsRun = 0;

            for (int iter = 0; iter < 3; iter++)
            {
                iterationsRun++;

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

            // Diagnostic: measure remaining penetration after Phase A
            phaseAIterCount = iterationsRun;
            phaseARemainPen = 0f;
            if (anyPush)
            {
                int postCount = Physics2D.OverlapCircleNonAlloc(pos, radius + skin, depenetrationBuffer, mask);
                for (int i = 0; i < postCount; i++)
                {
                    if (ignoredColliders.Contains(depenetrationBuffer[i])) continue;
                    Vector2 c = depenetrationBuffer[i].ClosestPoint(pos);
                    Vector2 d = pos - c;
                    float dist = d.magnitude;
                    if (dist < 0.0001f) continue;
                    if (dist < radius)
                        phaseARemainPen += radius - dist;
                }
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

        public static float GetSurfaceAngle(Vector2 surfaceNormal, Vector2 gravityUp)
        {
            return Mathf.Acos(Mathf.Abs(Vector2.Dot(surfaceNormal, gravityUp))) * Mathf.Rad2Deg;
        }
    }
}
