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

        // Side buffer for buried-rejected contacts. PopulateContacts promotes one per
        // collider when contactCount==0 (every contact got rejected), so a slime fully
        // wedged inside a single buried collider still has a primary contact and stays
        // Attached. Without this fallback CheckSurface would return false on Airborne
        // entry frames (prevCollider==null), reproducing the
        // "후속 회귀 — 폴리곤에 박혀서 Airborne으로 멈춤" failure mode in
        // docs/faceted-terrain-jitter-attempts.md.
        // Stored normals are always the edge's outward perpendicular (never bisector)
        // so promoted contacts carry a real surface direction into lastGoodNormal /
        // EffectiveNormal. The clamped flag is implicitly false for promoted entries.
        private readonly Collider2D[] rejectedColliders = new Collider2D[16];
        private readonly Vector2[] rejectedNormals = new Vector2[16];
        private readonly float[] rejectedDistances = new float[16];
        private int rejectedCount;

        public override int RejectedBuriedCount => rejectedCount;

        public MultiContactSurfaceProbe(Rigidbody2D rb, SlimeSettings settings, List<Collider2D> ignoredColliders)
            : base(rb, settings, ignoredColliders) { }

        protected override bool PopulateContacts(Vector2 pos, int overlapCount)
        {
            rejectedCount = 0;

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

                AddContactsForCollider(col, pos, maxDistSq, dedupeDot, firstNewIdx,
                    overlapBuffer, overlapCount);

                if (contactCount >= contactColliders.Length) break;
            }

            // Every emitted contact got rejected as buried but at least one collider was
            // nearby — promote one rejected contact per collider so SelectPrimary has a
            // primary candidate. See comment on rejectedColliders[] field.
            if (contactCount == 0 && rejectedCount > 0)
                PromotePerCollider();

            return hasNearby;
        }

        protected override bool DepenetratePass(ref Vector2 pos)
        {
            float radius = settings.slimeRadius;   // broadphase / max reach
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

                int pushCount = CollectPushesForCollider(col, pos, radius, dedupeDot,
                    depenetrationBuffer, count);
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
            float maxDistSq, float dedupeDot, int firstNewIdx,
            Collider2D[] otherBuffer, int otherCount)
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
                            maxDistSq, dedupeDot, firstNewIdx, otherBuffer, otherCount);
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
                    TryAddEdgePolylineContact(col, pos, a, b, maxDistSq, dedupeDot, firstNewIdx,
                        otherBuffer, otherCount);
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
                            maxDistSq, dedupeDot, firstNewIdx, otherBuffer, otherCount);
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
                if (IsContactBuried(col, closest, normal, dist, otherBuffer, otherCount))
                {
                    StashRejected(col, normal, dist);
                    return;
                }
                MergeOrAddContact(col, normal, dist, false, false, dedupeDot, firstNewIdx);
            }
        }

        private void TryAddPolygonEdgeContact(Collider2D col, Vector2 pos, Vector2 a, Vector2 b,
            float windingSign, float maxDistSq, float dedupeDot, int firstNewIdx,
            Collider2D[] otherBuffer, int otherCount)
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

            // Buried check: bias the test point INTO ownerCol along -outward; if that
            // interior point is also inside another nearby collider, this edge is in the
            // union's interior. Stash with outward (not bisector) so promotion carries a
            // real edge perpendicular into lastGoodNormal / EffectiveNormal.
            if (IsContactBuried(col, closest, outward, dist, otherBuffer, otherCount))
            {
                StashRejected(col, outward, dist);
                return;
            }

            // Vertex-clamped contacts get the bisector (toSlime/dist) so two adjacent
            // edges meeting at a convex corner merge into one contact (lets the slime
            // roll over a small lip without ProjectVel over-clipping). The clamped flag
            // tells EffectiveNormal to skip them — otherwise the corner-bisector would
            // contaminate the slope reading and produce phantom slide on flat tops.
            // Interior contacts use outward (the real edge perpendicular).
            Vector2 normal = clamped ? toSlime / dist : outward;
            // Classify a clamped vertex as a small-ledge lip (low + open above) so
            // ProjectVelocityAgainstContacts skips its clip and the controller exempts
            // this collider from the teleport CCD. closest == the clamped vertex.
            bool lowLedge = clamped && settings.surfaceProbeSmoothSmallLedges
                && IsSmallLedgeVertex(col, closest, settings.slimeRadius);
            MergeOrAddContact(col, normal, dist, clamped, lowLedge, dedupeDot, firstNewIdx);
        }

        // A vertex-clamped contact/push is a "small ledge" the slime should climb iff the
        // vertex sits within smallLedgeStepHeight above the slime's foot (an upward step,
        // not a tall wall or a downward edge) AND a point a short distance above the vertex
        // lies outside ownerCol — i.e. the obstruction has a low top. The "open above"
        // test (a single OverlapPoint, no raycast — so unaffected by QueriesStartInColliders)
        // is what rules out tessellated/solid walls whose lowest segment's top vertex falls
        // within stepHeight but more collider continues above it. Caller has already
        // established the contact clamped to vertex.
        private bool IsSmallLedgeVertex(Collider2D ownerCol, Vector2 vertex, float radius)
        {
            Vector2 foot = rb.position - lastGravityUp * radius;
            float heightAboveFoot = Vector2.Dot(vertex - foot, lastGravityUp);
            if (heightAboveFoot < -settings.contactSkin || heightAboveFoot > settings.smallLedgeStepHeight)
                return false;
            float aboveEps = Mathf.Max(2f * settings.contactSkin, 0.5f * settings.smallLedgeStepHeight);
            return !ownerCol.OverlapPoint(vertex + lastGravityUp * aboveEps);
        }

        private void TryAddEdgePolylineContact(Collider2D col, Vector2 pos, Vector2 a, Vector2 b,
            float maxDistSq, float dedupeDot, int firstNewIdx,
            Collider2D[] otherBuffer, int otherCount)
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
            if (IsContactBuried(col, closest, normal, dist, otherBuffer, otherCount))
            {
                StashRejected(col, normal, dist);
                return;
            }
            MergeOrAddContact(col, normal, dist, false, false, dedupeDot, firstNewIdx);
        }

        private void MergeOrAddContact(Collider2D col, Vector2 n, float dist, bool clamped, bool lowLedge,
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
                        contactLowLedge[c] = lowLedge;
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
                        contactLowLedge[c] = false; // an interior edge isn't a vertex lip
                    }
                    return;
                }
            }
            if (contactCount >= contactColliders.Length) return;
            contactColliders[contactCount] = col;
            contactNormals[contactCount] = n;
            contactDistances[contactCount] = dist;
            contactClamped[contactCount] = clamped;
            contactLowLedge[contactCount] = lowLedge;
            contactCount++;
        }

        private int CollectPushesForCollider(Collider2D col, Vector2 pos, float radius, float dedupeDot,
            Collider2D[] otherBuffer, int otherCount)
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
                        TryAddPolygonEdgePush(col, pos, a, b, windingSign,
                            radius, radiusSq, dedupeDot, ref pushCount,
                            otherBuffer, otherCount);
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
                    TryAddEdgePolylinePush(col, pos, a, b, radius, radiusSq, dedupeDot, ref pushCount,
                        otherBuffer, otherCount);
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
                        TryAddPolygonEdgePush(col, pos, a, b, windingSign,
                            radius, radiusSq, dedupeDot, ref pushCount,
                            otherBuffer, otherCount);
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
                Vector2 normal = toSlime / dist;
                if (IsContactBuried(col, closest, normal, dist, otherBuffer, otherCount))
                    return 0;
                float reach = Reach(normal) - dist;
                if (reach <= 0f) return 0;
                pushNormals[0] = normal;
                pushDepths[0] = reach;
                pushCount = 1;
            }

            return pushCount;
        }

        private void TryAddPolygonEdgePush(Collider2D col, Vector2 pos, Vector2 a, Vector2 b,
            float windingSign, float radius, float radiusSq, float dedupeDot, ref int pushCount,
            Collider2D[] otherBuffer, int otherCount)
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

            // Buried check: drop pushes from edges that are buried inside another collider.
            // No fallback on push path — buried colliders shouldn't push the slime; the
            // outermost surface's pushes do the work.
            if (IsContactBuried(col, closest, outward, dist, otherBuffer, otherCount)) return;

            // Directional reach: only push out to the cut shape's extent toward this surface, so
            // the removed region never depenetrates the slime (no catch / no phantom push).
            float depth = Reach(toSlime / dist) - dist;
            if (depth <= 0f) return;

            // Same bisector-vs-outward split as the contact path: clamped vertices push
            // along the bisector (radial) so a slime caught on a small convex corner is
            // depenetrated AWAY from the corner rather than along the edge perpendicular,
            // letting it roll over the lip; interior contacts use outward (= bisector).
            Vector2 normal = clamped ? toSlime / dist : outward;

            // Small-ledge lift: a clamped-vertex push whose vertex is a low lip (within
            // stepHeight of the slime's foot, open space above — same classification as the
            // contact path's contactLowLedge). The plain bisector push points up-AND-BACK
            // while the slime approaches (vertex behind the slime center horizontally), and
            // that "back" component cancels the forward slide → slime stalls at the lip.
            // Strip the along-surface (tangent) component so the push becomes purely "up the
            // base surface" (lastGoodNormal = the surface the slime is on): the slime then
            // rises past the lip while the preserved forward slide (clip skipped in
            // ProjectVelocityAgainstContacts) carries it over.
            if (clamped && settings.surfaceProbeSmoothSmallLedges
                && IsSmallLedgeVertex(col, closest, radius)) // closest == the vertex
            {
                float along = Vector2.Dot(normal * depth, lastGoodNormal);
                if (along > 1e-5f)
                {
                    normal = lastGoodNormal;
                    depth = along;
                }
                else
                {
                    return; // push had no useful up-component (vertex above slime) — skip
                }
            }

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

        private void TryAddEdgePolylinePush(Collider2D col, Vector2 pos, Vector2 a, Vector2 b,
            float radius, float radiusSq, float dedupeDot, ref int pushCount,
            Collider2D[] otherBuffer, int otherCount)
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
            float distForBuried;
            if (lastGoodNormal.sqrMagnitude > 0.5f && Vector2.Dot(toSlime, lastGoodNormal) < 0f)
            {
                n = lastGoodNormal;
                depth = Reach(lastGoodNormal) - Vector2.Dot(toSlime, lastGoodNormal);
                distForBuried = Mathf.Max(0.0001f, distSq > 0.0001f ? Mathf.Sqrt(distSq) : 0.0001f);
            }
            else
            {
                if (distSq < 0.0001f) return;
                float dist = Mathf.Sqrt(distSq);
                n = toSlime / dist;
                depth = Reach(toSlime / dist) - dist;
                if (depth <= 0f) return;
                distForBuried = dist;
            }

            // Buried check: drop pushes from polyline edges buried inside another collider.
            if (IsContactBuried(col, closest, n, distForBuried, otherBuffer, otherCount)) return;

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

        // Buried-collider check. Bias the closest point slightly INTO ownerCol's interior
        // (along -normal) and ask whether that interior point also lies inside any other
        // nearby collider. If yes, this edge is in the union's interior, not on the outer
        // silhouette — reject. The bias disambiguates "shared boundary" (Unity OverlapPoint
        // is boundary-inclusive) from "actually buried inside another collider's body".
        // bias = min(setting, dist*0.4) keeps the test point inside ownerCol when the slime
        // is very close to the surface (dist small).
        private bool IsContactBuried(Collider2D ownerCol, Vector2 closest, Vector2 normal,
            float dist, Collider2D[] otherBuffer, int otherCount)
        {
            if (!settings.surfaceProbeRejectBuriedContacts) return false;
            if (otherCount <= 1) return false;

            float bias = Mathf.Min(settings.surfaceProbeBuriedTestBias, dist * 0.4f);
            Vector2 testPoint = closest - normal * bias;

            for (int oi = 0; oi < otherCount; oi++)
            {
                var other = otherBuffer[oi];
                if (other == ownerCol || other == null) continue;
                if (other.isTrigger) continue;
                if (ignoredColliders.Contains(other)) continue;

                // Raw min/max compare instead of bounds.Contains(Vector3) — the latter
                // boxes a temp Vector3, this is allocation-free and 1 branch cheaper.
                var b = other.bounds;
                if (testPoint.x < b.min.x || testPoint.x > b.max.x ||
                    testPoint.y < b.min.y || testPoint.y > b.max.y) continue;

                if (other.OverlapPoint(testPoint)) return true;
            }
            return false;
        }

        // Stash a rejected (buried) contact for possible promotion later. Always store
        // the edge's outward perpendicular (not a vertex bisector) so promoted contacts
        // carry a real surface direction.
        private void StashRejected(Collider2D col, Vector2 outwardNormal, float dist)
        {
            if (rejectedCount >= rejectedColliders.Length) return;
            rejectedColliders[rejectedCount] = col;
            rejectedNormals[rejectedCount] = outwardNormal;
            rejectedDistances[rejectedCount] = dist;
            rejectedCount++;
        }

        // Fallback: when contactCount==0 because every contact was rejected as buried,
        // promote one rejected contact per collider (the closest distance per collider)
        // into the contact arrays. Promoted contacts always have clamped=false so
        // EffectiveNormal includes them rather than skipping (clamped contacts are
        // skipped → would return stale lastGoodNormal). Runs inside PopulateContacts so
        // the SurfaceProbe.CheckSurface ceiling-threshold resolution loop covers
        // promoted indices naturally.
        private void PromotePerCollider()
        {
            for (int i = 0; i < rejectedCount; i++)
            {
                Collider2D rc = rejectedColliders[i];
                float rd = rejectedDistances[i];

                bool found = false;
                for (int j = 0; j < contactCount; j++)
                {
                    if (contactColliders[j] == rc)
                    {
                        if (rd < contactDistances[j])
                        {
                            contactNormals[j] = rejectedNormals[i];
                            contactDistances[j] = rd;
                        }
                        found = true;
                        break;
                    }
                }
                if (found) continue;

                if (contactCount >= contactColliders.Length) return;
                contactColliders[contactCount] = rc;
                contactNormals[contactCount] = rejectedNormals[i];
                contactDistances[contactCount] = rd;
                contactClamped[contactCount] = false;
                contactLowLedge[contactCount] = false;
                contactCount++;
            }
        }
    }
}
