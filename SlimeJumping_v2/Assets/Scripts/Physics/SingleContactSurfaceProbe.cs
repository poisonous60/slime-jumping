using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Settings;

namespace SlimeJumping.Physics
{
    /// <summary>One ClosestPoint contact per nearby collider, so multi-path polygons (SnakeSpine
    /// branches, DeformablePlatform islands) only ever yield one contact.</summary>
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
            float radius = EffRadius;   // broadphase / max reach
            float skin = settings.depenetrationSkin;
            LayerMask mask = settings.groundLayer;

            int count = Physics2D.OverlapCircleNonAlloc(pos, radius + skin, depenetrationBuffer, mask);
            bool anyPenetration = false;
            Vector2 totalPush = Vector2.zero;

            for (int i = 0; i < count; i++)
            {
                if (ignoredColliders.Contains(depenetrationBuffer[i])) continue;

                Vector2 closest = depenetrationBuffer[i].ClosestPoint(pos);
                Vector2 toSlime = pos - closest;
                float dist = toSlime.magnitude;

                // Deep penetration: skip (no reliable push direction)
                if (dist < 0.0001f) continue;

                // Directional reach: the cut shape only pushes out to its cut face on the cut side,
                // so the removed region never depenetrates the slime (no catch).
                float reach = Reach(toSlime / dist);
                if (dist < reach)
                {
                    Vector2 push = (toSlime / dist) * (reach - dist);
                    pos += push;
                    totalPush += push;
                    anyPenetration = true;
                }
            }

            if (anyPenetration)
            {
                lastDepenPushMag = totalPush.magnitude;
                lastDepenPushDir = lastDepenPushMag > 1e-8f ? totalPush / lastDepenPushMag : Vector2.zero;
            }
            else
            {
                lastDepenPushDir = Vector2.zero;
                lastDepenPushMag = 0f;
            }

            return anyPenetration;
        }
    }
}
