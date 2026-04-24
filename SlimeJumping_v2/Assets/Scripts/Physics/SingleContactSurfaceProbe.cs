using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Settings;

namespace SlimeJumping.Physics
{
    /// <summary>
    /// Original per-collider-ClosestPoint detector. One contact per nearby collider —
    /// ignores multi-path polygons (SnakeSpine branches / DeformablePlatform islands).
    /// Baseline for A/B comparison with MultiContactSurfaceProbe.
    /// </summary>
    public class SingleContactSurfaceProbe : SurfaceProbe
    {
        public SingleContactSurfaceProbe(Rigidbody2D rb, SlimeSettings settings, List<Collider2D> ignoredColliders)
            : base(rb, settings, ignoredColliders) { }

        protected override bool PopulateContacts(Vector2 pos, int overlapCount)
        {
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

            return hasNearby;
        }

        protected override bool DepenetratePass(ref Vector2 pos)
        {
            float radius = settings.slimeRadius;
            float skin = settings.depenetrationSkin;
            LayerMask mask = settings.groundLayer;

            int count = Physics2D.OverlapCircleNonAlloc(pos, radius + skin, depenetrationBuffer, mask);
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
                }
            }

            return anyPenetration;
        }
    }
}
