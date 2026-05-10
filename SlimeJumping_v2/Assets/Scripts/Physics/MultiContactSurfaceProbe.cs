using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Settings;

namespace SlimeJumping.Physics
{
    /// <summary>
    /// Multi-contact detector via direct polygon-edge enumeration.
    ///
    /// PolygonCollider2D / CompositeCollider2D: for each closed path, compute winding
    /// once (signed area), iterate edge segments. Each edge's outward perpendicular is
    /// the unambiguous "real" surface normal for that edge. A contact is emitted only
    /// when the slime sits on the outward side of that edge — closest-point-on-segment
    /// clamps at vertices, and a clamp-to-vertex contact whose slime-side is *inward*
    /// is just an artifact of the closest point being at a corner of an unrelated edge.
    /// Filtering those out is what stops a slime resting on the top of a block from
    /// reporting 37°/45° contacts that come from the bottom or slanted side edges.
    ///
    /// EdgeCollider2D: open polylines have no winding, so we keep the slime-radial
    /// normal `(slime − closest).normalized` and accept the artifact at endpoints.
    ///
    /// Analytical colliders (Circle / Box / Capsule): single ClosestPoint(pos) query.
    ///
    /// Replaces the legacy 8-probe ring sampling, which (a) capped distinct contacts
    /// at ~3-4 per collider regardless of facet count and (b) produced spurious
    /// ring-direction normals from probes that fell inside the polygon.
    /// </summary>
    public class MultiContactSurfaceProbe : SurfaceProbe
    {
        // Per-collider scratch for unique-face pushes during a single Phase A iter.
        private readonly Vector2[] pushNormals = new Vector2[16];
        private readonly float[] pushDepths = new float[16];

        // Path-vertex buffers reused across calls to avoid per-frame allocation.
        private readonly List<Vector2> pathBuffer = new List<Vector2>(64);
        private readonly List<Vector2> worldBuffer = new List<Vector2>(64);

        public MultiContactSurfaceProbe(Rigidbody2D rb, SlimeSettings settings, List<Collider2D> ignoredColliders)
            : base(rb, settings, ignoredColliders) { }

        protected override bool PopulateContacts(Vector2 pos, int overlapCount)
        {
            bool hasNearby = false;
            float maxDist = settings.slimeRadius + lastDetectRange;
            float maxDistSq = maxDist * maxDist;
            float dedupeDot = settings.surfaceProbeDedupeDot;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider2D col = overlapBuffer[i];
                if (ignoredColliders.Contains(col)) continue;

                hasNearby = true;
                int firstNewIdx = contactCount;

                AddContactsForCollider(col, pos, maxDistSq, dedupeDot, firstNewIdx);

                if (contactCount >= contactColliders.Length) break;
            }

            return hasNearby;
        }

        protected override bool DepenetratePass(ref Vector2 pos)
        {
            float radius = settings.slimeRadius;
            float skin = settings.depenetrationSkin;
            LayerMask mask = settings.groundLayer;
            float dedupeDot = settings.surfaceProbeDedupeDot;

            int count = Physics2D.OverlapCircleNonAlloc(pos, radius + skin, depenetrationBuffer, mask);
            bool anyPenetration = false;

            bool useSmoothed = settings.depenetrationUseSmoothedAggregation;
            float fullBand = settings.contactSkin * Mathf.Max(1f, settings.effectiveNormalSoftBandMultiplier);
            float rampWidth = fullBand * Mathf.Clamp01(settings.effectiveNormalWeightRampBand);

            Vector2 totalPush = Vector2.zero;

            for (int i = 0; i < count; i++)
            {
                Collider2D col = depenetrationBuffer[i];
                if (ignoredColliders.Contains(col)) continue;

                int pushCount = CollectPushesForCollider(col, pos, radius, dedupeDot);
                if (pushCount == 0) continue;

                Vector2 perColliderPush;
                if (useSmoothed)
                {
                    Vector2 weightedNormalSum = Vector2.zero;
                    float weightedDepthSum = 0f;
                    float weightSum = 0f;
                    for (int p = 0; p < pushCount; p++)
                    {
                        float contactDist = radius - pushDepths[p];
                        float wRaw = (radius + fullBand) - contactDist;
                        if (wRaw <= 0f) continue;
                        float w;
                        if (rampWidth > 1e-6f)
                        {
                            float t = Mathf.Clamp01(wRaw / rampWidth);
                            w = wRaw * t * t * (3f - 2f * t);
                        }
                        else
                        {
                            w = wRaw;
                        }
                        weightedNormalSum += pushNormals[p] * w;
                        weightedDepthSum += pushDepths[p] * w;
                        weightSum += w;
                    }
                    if (weightSum > 1e-6f && weightedNormalSum.sqrMagnitude > 1e-6f)
                    {
                        Vector2 dir = weightedNormalSum.normalized;
                        float mag = weightedDepthSum / weightSum;
                        perColliderPush = dir * mag;
                    }
                    else
                    {
                        perColliderPush = Vector2.zero;
                    }
                }
                else
                {
                    Vector2 netPush = Vector2.zero;
                    for (int p = 0; p < pushCount; p++)
                        netPush += pushNormals[p] * pushDepths[p];
                    perColliderPush = netPush;
                }

                if (perColliderPush.sqrMagnitude > 0f)
                {
                    // [A] Optional: strip surfaceTangent component from the per-collider
                    // weighted push. On polygon-vertex transitions the smoothed-aggregation
                    // bisector has a small tangent component (saw-tooth dPos source on
                    // curve slides). currentSnapNormal is the smoothed primary normal
                    // cached by SurfaceProbe.SnapToSurface. Guard on |normal|>0.5 so
                    // pre-Snap calls (currentSnapNormal still default) don't NaN.
                    if (settings.phaseAStripTangentComponent && currentSnapNormal.sqrMagnitude > 0.5f)
                    {
                        Vector2 t = new Vector2(-currentSnapNormal.y, currentSnapNormal.x);
                        float tangentDot = Vector2.Dot(perColliderPush, t);
                        perColliderPush -= t * (tangentDot * settings.phaseAStripStrength);
                    }
                    pos += perColliderPush;
                    totalPush += perColliderPush;
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

        private void AddContactsForCollider(Collider2D col, Vector2 pos,
            float maxDistSq, float dedupeDot, int firstNewIdx)
        {
            if (col is PolygonCollider2D poly)
            {
                // No OverlapPoint early-return: edge enumeration with the
                // Dot(toSlime, outward) <= 0 filter already rejects interior contacts
                // correctly. Skipping when slime center is inside the polygon would
                // emit zero contacts, and CheckSurface with prevCollider == null
                // (Airborne) would then return false → slime wedged but unattached.
                Transform t = poly.transform;
                Vector2 offset = poly.offset;
                int pathCount = poly.pathCount;
                for (int p = 0; p < pathCount; p++)
                {
                    poly.GetPath(p, pathBuffer);
                    int n = pathBuffer.Count;
                    if (n < 2) continue;
                    BuildWorldPath(t, offset, pathBuffer, worldBuffer);
                    float windingSign = ComputeWindingSign(worldBuffer);
                    for (int j = 0; j < n; j++)
                    {
                        Vector2 a = worldBuffer[j];
                        Vector2 b = worldBuffer[(j + 1) % n];
                        TryAddPolygonEdgeContact(col, pos, a, b, windingSign,
                            maxDistSq, dedupeDot, firstNewIdx);
                        if (contactCount >= contactColliders.Length) return;
                    }
                }
            }
            else if (col is EdgeCollider2D edge)
            {
                Transform t = edge.transform;
                Vector2 offset = edge.offset;
                edge.GetPoints(pathBuffer);
                int n = pathBuffer.Count;
                for (int j = 0; j + 1 < n; j++)
                {
                    Vector2 a = (Vector2)t.TransformPoint(pathBuffer[j] + offset);
                    Vector2 b = (Vector2)t.TransformPoint(pathBuffer[j + 1] + offset);
                    TryAddEdgePolylineContact(col, pos, a, b, maxDistSq, dedupeDot, firstNewIdx);
                    if (contactCount >= contactColliders.Length) return;
                }
            }
            else if (col is CompositeCollider2D composite)
            {
                Transform t = composite.transform;
                int pathCount = composite.pathCount;
                for (int p = 0; p < pathCount; p++)
                {
                    composite.GetPath(p, pathBuffer);
                    int n = pathBuffer.Count;
                    if (n < 2) continue;
                    BuildWorldPath(t, Vector2.zero, pathBuffer, worldBuffer);
                    float windingSign = ComputeWindingSign(worldBuffer);
                    for (int j = 0; j < n; j++)
                    {
                        Vector2 a = worldBuffer[j];
                        Vector2 b = worldBuffer[(j + 1) % n];
                        TryAddPolygonEdgeContact(col, pos, a, b, windingSign,
                            maxDistSq, dedupeDot, firstNewIdx);
                        if (contactCount >= contactColliders.Length) return;
                    }
                }
            }
            else
            {
                Vector2 closest = col.ClosestPoint(pos);
                Vector2 toSlime = pos - closest;
                float distSq = toSlime.sqrMagnitude;
                if (distSq < 0.0001f) return;
                if (distSq > maxDistSq) return;
                float dist = Mathf.Sqrt(distSq);
                Vector2 normal = toSlime / dist;
                MergeOrAddContact(col, normal, dist, false, dedupeDot, firstNewIdx);
            }
        }

        private void TryAddPolygonEdgeContact(Collider2D col, Vector2 pos, Vector2 a, Vector2 b,
            float windingSign, float maxDistSq, float dedupeDot, int firstNewIdx)
        {
            Vector2 ab = b - a;
            float abSq = ab.sqrMagnitude;
            if (abSq < 1e-8f) return;

            float uRaw = Vector2.Dot(pos - a, ab) / abSq;
            float u = uRaw < 0f ? 0f : (uRaw > 1f ? 1f : uRaw);
            bool clamped = (uRaw < 0f) || (uRaw > 1f);
            Vector2 closest = a + ab * u;

            Vector2 toSlime = pos - closest;
            float distSq = toSlime.sqrMagnitude;
            if (distSq < 0.0001f) return;
            if (distSq > maxDistSq) return;

            Vector2 outward = new Vector2(ab.y * windingSign, -ab.x * windingSign);
            float outwardLen = outward.magnitude;
            if (outwardLen < 1e-6f) return;
            outward /= outwardLen;

            if (Vector2.Dot(toSlime, outward) <= 0f) return;

            float dist = Mathf.Sqrt(distSq);

            // Vertex-clamped contacts get the bisector (toSlime/dist) so two adjacent
            // edges meeting at a convex corner merge into one contact (lets the slime
            // roll over a small lip without ProjectVel over-clipping). The clamped flag
            // tells EffectiveNormal to skip them — otherwise the corner-bisector would
            // contaminate the slope reading and produce phantom slide on flat tops.
            // Interior contacts use outward (the real edge perpendicular).
            Vector2 normal = clamped ? toSlime / dist : outward;
            MergeOrAddContact(col, normal, dist, clamped, dedupeDot, firstNewIdx);
        }

        private void TryAddEdgePolylineContact(Collider2D col, Vector2 pos, Vector2 a, Vector2 b,
            float maxDistSq, float dedupeDot, int firstNewIdx)
        {
            Vector2 closest = ClosestPointOnSegment(pos, a, b);
            Vector2 toSlime = pos - closest;
            float distSq = toSlime.sqrMagnitude;
            if (distSq < 0.0001f) return;
            if (distSq > maxDistSq) return;
            float dist = Mathf.Sqrt(distSq);
            // EdgeCollider2D has no inherent inside/outside. If a rotating edge swept
            // past the slime in this fixed step, slime center is now on the side
            // opposite lastGoodNormal — the radial normal would point further to the
            // wrong side, causing the next depenetration to push it through. Flip to
            // lastGoodNormal so the contact reads "still attached on the correct side".
            Vector2 normal;
            if (lastGoodNormal.sqrMagnitude > 0.5f && Vector2.Dot(toSlime, lastGoodNormal) < 0f)
                normal = lastGoodNormal;
            else
                normal = toSlime / dist;
            MergeOrAddContact(col, normal, dist, false, dedupeDot, firstNewIdx);
        }

        private void MergeOrAddContact(Collider2D col, Vector2 n, float dist, bool clamped,
            float dedupeDot, int firstNewIdx)
        {
            for (int c = firstNewIdx; c < contactCount; c++)
            {
                if (contactColliders[c] != col) continue;
                if (Vector2.Dot(n, contactNormals[c]) > dedupeDot)
                {
                    if (dist < contactDistances[c])
                    {
                        contactNormals[c] = n;
                        contactDistances[c] = dist;
                        contactClamped[c] = clamped;
                    }
                    // A non-clamped contact "wins" the slot regardless of distance —
                    // an interior edge reading is always more authoritative than a
                    // vertex-bisector at the same direction. Without this, a clamped
                    // contact emitted first by edge-iteration order locks in and the
                    // later interior contact gets dedupe-merged but keeps the clamped
                    // flag, bouncing it out of EffectiveNormal.
                    else if (!clamped && contactClamped[c])
                    {
                        contactClamped[c] = false;
                        contactNormals[c] = n;
                    }
                    return;
                }
            }
            if (contactCount >= contactColliders.Length) return;
            contactColliders[contactCount] = col;
            contactNormals[contactCount] = n;
            contactDistances[contactCount] = dist;
            contactClamped[contactCount] = clamped;
            contactCount++;
        }

        private int CollectPushesForCollider(Collider2D col, Vector2 pos, float radius, float dedupeDot)
        {
            int pushCount = 0;
            float radiusSq = radius * radius;

            if (col is PolygonCollider2D poly)
            {
                // No OverlapPoint guard — would block depenetration when slime center
                // has entered the polygon, leaving it stuck inside. Edge-enumeration
                // pushes already handle interior positions correctly.
                Transform t = poly.transform;
                Vector2 offset = poly.offset;
                int pathCount = poly.pathCount;
                for (int p = 0; p < pathCount; p++)
                {
                    poly.GetPath(p, pathBuffer);
                    int n = pathBuffer.Count;
                    if (n < 2) continue;
                    BuildWorldPath(t, offset, pathBuffer, worldBuffer);
                    float windingSign = ComputeWindingSign(worldBuffer);
                    for (int j = 0; j < n; j++)
                    {
                        Vector2 a = worldBuffer[j];
                        Vector2 b = worldBuffer[(j + 1) % n];
                        TryAddPolygonEdgePush(pos, a, b, windingSign,
                            radius, radiusSq, dedupeDot, ref pushCount);
                        if (pushCount >= pushNormals.Length) return pushCount;
                    }
                }
            }
            else if (col is EdgeCollider2D edge)
            {
                Transform t = edge.transform;
                Vector2 offset = edge.offset;
                edge.GetPoints(pathBuffer);
                int n = pathBuffer.Count;
                for (int j = 0; j + 1 < n; j++)
                {
                    Vector2 a = (Vector2)t.TransformPoint(pathBuffer[j] + offset);
                    Vector2 b = (Vector2)t.TransformPoint(pathBuffer[j + 1] + offset);
                    TryAddEdgePolylinePush(pos, a, b, radius, radiusSq, dedupeDot, ref pushCount);
                    if (pushCount >= pushNormals.Length) return pushCount;
                }
            }
            else if (col is CompositeCollider2D composite)
            {
                Transform t = composite.transform;
                int pathCount = composite.pathCount;
                for (int p = 0; p < pathCount; p++)
                {
                    composite.GetPath(p, pathBuffer);
                    int n = pathBuffer.Count;
                    if (n < 2) continue;
                    BuildWorldPath(t, Vector2.zero, pathBuffer, worldBuffer);
                    float windingSign = ComputeWindingSign(worldBuffer);
                    for (int j = 0; j < n; j++)
                    {
                        Vector2 a = worldBuffer[j];
                        Vector2 b = worldBuffer[(j + 1) % n];
                        TryAddPolygonEdgePush(pos, a, b, windingSign,
                            radius, radiusSq, dedupeDot, ref pushCount);
                        if (pushCount >= pushNormals.Length) return pushCount;
                    }
                }
            }
            else
            {
                Vector2 closest = col.ClosestPoint(pos);
                Vector2 toSlime = pos - closest;
                float distSq = toSlime.sqrMagnitude;
                if (distSq < 0.0001f) return 0;
                if (distSq >= radiusSq) return 0;
                float dist = Mathf.Sqrt(distSq);
                pushNormals[0] = toSlime / dist;
                pushDepths[0] = radius - dist;
                pushCount = 1;
            }

            return pushCount;
        }

        private void TryAddPolygonEdgePush(Vector2 pos, Vector2 a, Vector2 b,
            float windingSign, float radius, float radiusSq, float dedupeDot, ref int pushCount)
        {
            Vector2 ab = b - a;
            float abSq = ab.sqrMagnitude;
            if (abSq < 1e-8f) return;

            float uRaw = Vector2.Dot(pos - a, ab) / abSq;
            float u = uRaw < 0f ? 0f : (uRaw > 1f ? 1f : uRaw);
            bool clamped = (uRaw < 0f) || (uRaw > 1f);
            Vector2 closest = a + ab * u;

            Vector2 toSlime = pos - closest;
            float distSq = toSlime.sqrMagnitude;
            if (distSq < 0.0001f) return;
            if (distSq >= radiusSq) return;

            Vector2 outward = new Vector2(ab.y * windingSign, -ab.x * windingSign);
            float outwardLen = outward.magnitude;
            if (outwardLen < 1e-6f) return;
            outward /= outwardLen;

            if (Vector2.Dot(toSlime, outward) <= 0f) return;

            float dist = Mathf.Sqrt(distSq);
            float depth = radius - dist;

            // Same bisector-vs-outward split as the contact path: clamped vertices push
            // along the bisector (radial) so a slime caught on a small convex corner is
            // depenetrated AWAY from the corner rather than along the edge perpendicular,
            // letting it roll over the lip; interior contacts use outward (= bisector).
            Vector2 normal = clamped ? toSlime / dist : outward;

            for (int p = 0; p < pushCount; p++)
            {
                if (Vector2.Dot(normal, pushNormals[p]) > dedupeDot)
                {
                    if (depth > pushDepths[p])
                    {
                        pushNormals[p] = normal;
                        pushDepths[p] = depth;
                    }
                    return;
                }
            }
            if (pushCount >= pushNormals.Length) return;
            pushNormals[pushCount] = normal;
            pushDepths[pushCount] = depth;
            pushCount++;
        }

        private void TryAddEdgePolylinePush(Vector2 pos, Vector2 a, Vector2 b,
            float radius, float radiusSq, float dedupeDot, ref int pushCount)
        {
            Vector2 closest = ClosestPointOnSegment(pos, a, b);
            Vector2 toSlime = pos - closest;
            float distSq = toSlime.sqrMagnitude;
            if (distSq >= radiusSq) return;

            // Wrong-side detection (rotating SpringyBranch swept past slime):
            // push along lastGoodNormal so we cross back to the intended side. Depth
            // is computed along that normal — when slime is on the wrong side
            // Dot(toSlime, lastGoodNormal) is negative, so depth = radius − that dot
            // exceeds radius, restoring the slime to clearance + radius.
            Vector2 n;
            float depth;
            if (lastGoodNormal.sqrMagnitude > 0.5f && Vector2.Dot(toSlime, lastGoodNormal) < 0f)
            {
                n = lastGoodNormal;
                depth = radius - Vector2.Dot(toSlime, lastGoodNormal);
            }
            else
            {
                if (distSq < 0.0001f) return;
                float dist = Mathf.Sqrt(distSq);
                n = toSlime / dist;
                depth = radius - dist;
            }

            for (int p = 0; p < pushCount; p++)
            {
                if (Vector2.Dot(n, pushNormals[p]) > dedupeDot)
                {
                    if (depth > pushDepths[p])
                    {
                        pushNormals[p] = n;
                        pushDepths[p] = depth;
                    }
                    return;
                }
            }
            if (pushCount >= pushNormals.Length) return;
            pushNormals[pushCount] = n;
            pushDepths[pushCount] = depth;
            pushCount++;
        }

        private static void BuildWorldPath(Transform t, Vector2 offset,
            List<Vector2> localPath, List<Vector2> worldOut)
        {
            worldOut.Clear();
            int n = localPath.Count;
            for (int i = 0; i < n; i++)
                worldOut.Add((Vector2)t.TransformPoint(localPath[i] + offset));
        }

        // +1 for CCW (standard outer path), -1 for CW (hole). Computed in world
        // space so a mirrored Transform doesn't invert the outward direction.
        private static float ComputeWindingSign(List<Vector2> pts)
        {
            int n = pts.Count;
            if (n < 3) return 1f;
            float twoA = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 a = pts[i];
                Vector2 b = pts[(i + 1) % n];
                twoA += a.x * b.y - b.x * a.y;
            }
            return twoA >= 0f ? 1f : -1f;
        }

        private static Vector2 ClosestPointOnSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float abSq = ab.sqrMagnitude;
            if (abSq < 1e-8f) return a;
            float t = Vector2.Dot(p - a, ab) / abSq;
            if (t < 0f) t = 0f;
            else if (t > 1f) t = 1f;
            return a + ab * t;
        }
    }
}
