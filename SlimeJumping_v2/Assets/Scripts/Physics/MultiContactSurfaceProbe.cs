using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Settings;

namespace SlimeJumping.Physics
{
    /// <summary>
    /// Multi-probe detector. For each nearby collider, samples ClosestPoint from N
    /// probe points arranged around the slime; deduplicates samples whose normals are
    /// nearly identical (same face) so flat surfaces still report a single contact.
    /// Distinct faces on a multi-path polygon (SnakeSpine branches, DeformablePlatform
    /// islands) survive dedupe and feed the existing multi-constraint solver.
    /// </summary>
    public class MultiContactSurfaceProbe : SurfaceProbe
    {
        // Phase A scratch: per-collider unique push vectors within a single iter.
        private readonly Vector2[] pushNormals;
        private readonly float[] pushDepths;

        public MultiContactSurfaceProbe(Rigidbody2D rb, SlimeSettings settings, List<Collider2D> ignoredColliders)
            : base(rb, settings, ignoredColliders)
        {
            pushNormals = new Vector2[16];
            pushDepths = new float[16];
        }

        protected override bool PopulateContacts(Vector2 pos, int overlapCount)
        {
            bool hasNearby = false;

            int samples = Mathf.Max(1, settings.surfaceProbeSamples);
            float probeR = settings.slimeRadius * settings.surfaceProbeRingScale;
            float dedupeDot = settings.surfaceProbeDedupeDot;
            float twoPi = Mathf.PI * 2f;

            for (int i = 0; i < overlapCount; i++)
            {
                Collider2D col = overlapBuffer[i];
                if (ignoredColliders.Contains(col)) continue;

                hasNearby = true;

                int firstNewIdx = contactCount;

                for (int k = 0; k < samples; k++)
                {
                    float theta = twoPi * (k / (float)samples);
                    Vector2 probe = new Vector2(
                        pos.x + probeR * Mathf.Cos(theta),
                        pos.y + probeR * Mathf.Sin(theta));

                    Vector2 closest = col.ClosestPoint(probe);
                    Vector2 toSlime = pos - closest;

                    if (toSlime.sqrMagnitude < 0.0001f) continue; // deep penetration

                    float dist = toSlime.magnitude;
                    Vector2 n = toSlime / dist;

                    // Dedupe within this collider's samples
                    bool merged = false;
                    for (int c = firstNewIdx; c < contactCount; c++)
                    {
                        if (contactColliders[c] != col) continue;
                        if (Vector2.Dot(n, contactNormals[c]) > dedupeDot)
                        {
                            // Same face — keep the nearer (deeper) sample
                            if (dist < contactDistances[c])
                            {
                                contactNormals[c] = n;
                                contactDistances[c] = dist;
                            }
                            merged = true;
                            break;
                        }
                    }
                    if (merged) continue;

                    if (contactCount >= contactColliders.Length) break;

                    contactColliders[contactCount] = col;
                    contactNormals[contactCount] = n;
                    contactDistances[contactCount] = dist;
                    contactCount++;
                }

                if (contactCount >= contactColliders.Length) break;
            }

            return hasNearby;
        }

        protected override bool DepenetratePass(ref Vector2 pos)
        {
            float radius = settings.slimeRadius;
            float skin = settings.depenetrationSkin;
            LayerMask mask = settings.groundLayer;

            int samples = Mathf.Max(1, settings.surfaceProbeSamples);
            float probeR = radius * settings.surfaceProbeRingScale;
            float dedupeDot = settings.surfaceProbeDedupeDot;
            float twoPi = Mathf.PI * 2f;

            int count = Physics2D.OverlapCircleNonAlloc(pos, radius + skin, depenetrationBuffer, mask);
            bool anyPenetration = false;

            for (int i = 0; i < count; i++)
            {
                Collider2D col = depenetrationBuffer[i];
                if (ignoredColliders.Contains(col)) continue;

                // Collect unique pushes from N samples against this collider
                int pushCount = 0;
                for (int k = 0; k < samples; k++)
                {
                    float theta = twoPi * (k / (float)samples);
                    Vector2 probe = new Vector2(
                        pos.x + probeR * Mathf.Cos(theta),
                        pos.y + probeR * Mathf.Sin(theta));

                    Vector2 closest = col.ClosestPoint(probe);
                    Vector2 toSlime = pos - closest;
                    float dist = toSlime.magnitude;

                    if (dist < 0.0001f) continue;      // deep penetration: skip
                    if (dist >= radius) continue;      // not penetrating

                    Vector2 n = toSlime / dist;
                    float depth = radius - dist;

                    bool merged = false;
                    for (int p = 0; p < pushCount; p++)
                    {
                        if (Vector2.Dot(n, pushNormals[p]) > dedupeDot)
                        {
                            if (depth > pushDepths[p])
                            {
                                pushNormals[p] = n;
                                pushDepths[p] = depth;
                            }
                            merged = true;
                            break;
                        }
                    }
                    if (merged) continue;
                    if (pushCount >= pushNormals.Length) break;
                    pushNormals[pushCount] = n;
                    pushDepths[pushCount] = depth;
                    pushCount++;
                }

                // Apply pushes as a single net vector. Sequential application
                // would over-correct in concave corners: later pushes were
                // measured against the pre-push pos, so applying them from an
                // already-moved pos double-counts. The outer 3-iter loop still
                // refines against any residual penetration in the next pass.
                if (pushCount > 0)
                {
                    Vector2 netPush = Vector2.zero;
                    for (int p = 0; p < pushCount; p++)
                        netPush += pushNormals[p] * pushDepths[p];
                    pos += netPush;
                    anyPenetration = true;
                }
            }

            return anyPenetration;
        }
    }
}
