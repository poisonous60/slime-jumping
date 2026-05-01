using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    // Target detection, aim resolution, raycast/circlecast helpers, and the slime
    // position trail used by Branching mode for chase-spread tendril targeting.
    public partial class GraniteBlastEmitter
    {
        // Slime XY trail — purely world-space positions, oldest at index 0. Sampled
        // every Update at trailSampleInterval, trimmed to trailDuration. PlanBranching
        // takes a one-shot snapshot at FireStart.
        private readonly List<Vector2> slimeWorldTrail = new List<Vector2>(64);
        private float lastTrailSampleTime = -999f;

        // --- fire direction / origin ---

        public Vector3 AimOrigin() => muzzle != null ? muzzle.position : transform.position;

        public Vector2 AimDirection2D()
        {
            if (aimAtTarget && target != null)
            {
                Vector3 origin = AimOrigin();
                Vector2 d = new Vector2(target.position.x - origin.x, target.position.y - origin.y);
                if (d.sqrMagnitude > 0.0001f) return d.normalized;
            }
            Transform t = muzzle != null ? muzzle : transform;
            Vector3 r = t.right;
            Vector2 dr = new Vector2(r.x, r.y);
            return dr.sqrMagnitude < 0.0001f ? Vector2.right : dr.normalized;
        }

        // --- detection ---

        private bool IsTargetVisible()
        {
            if (target == null) return false;
            Vector3 origin = AimOrigin();
            Vector3 delta = target.position - origin;
            float distSqr = delta.sqrMagnitude;
            if (distSqr > detectionRange * detectionRange) return false;
            if (lineOfSightBlockers == 0) return true;

            float dist = Mathf.Sqrt(distSqr);
            Vector2 dir = new Vector2(delta.x, delta.y);
            if (dir.sqrMagnitude < 0.0001f) return true;
            dir.Normalize();

            var hit = RaycastExcludingSelf(origin, dir, dist, lineOfSightBlockers);
            if (hit.collider == null) return true;
            var slime = hit.collider.GetComponentInParent<SlimeController>();
            return slime != null && slime.transform == target;
        }

        public RaycastHit2D RaycastExcludingSelf(Vector2 origin, Vector2 dir, float maxDist, LayerMask mask)
        {
            // Sort by distance so we always return the *closest* valid solid hit —
            // Physics2D.RaycastAll's array order is not guaranteed.
            var hits = Physics2D.RaycastAll(origin, dir, maxDist, mask);
            RaycastHit2D best = default;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                // Skip triggers (InteractionSensor / Water / etc. on the bullet hit mask
                // would otherwise block the beam at the muzzle).
                if (h.collider.isTrigger) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                }
            }
            return best;
        }

        // Same exclusion logic as the line raycast above, but sweeps a circle of
        // `radius`, giving the swept tube some forgiveness — used for tendril
        // endpoint resolution so a slightly stale direction (slime moved a hair off
        // the line) still terminates on slime/terrain instead of overshooting to
        // maxRange in mid-air.
        // Triggers are skipped EXCEPT those belonging to a SlimeController: the bolt
        // must stop at the slime's body even when that body collider is configured
        // as a trigger (otherwise the line passes through and ends in mid-air past
        // the slime). Foreign triggers (water zones, interaction sensors, etc.)
        // remain pass-through.
        public RaycastHit2D CircleCastExcludingSelf(Vector2 origin, float radius, Vector2 dir, float maxDist, LayerMask mask)
        {
            var hits = Physics2D.CircleCastAll(origin, radius, dir, maxDist, mask);
            RaycastHit2D best = default;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.isTrigger
                    && h.collider.GetComponentInParent<SlimeController>() == null) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                }
            }
            return best;
        }

        // CircleCast variant that skips slime colliders entirely — used by Beam
        // pass-through to find the ground BEHIND the slime so the beam reaches it
        // visually and carves SnowField even when a slime sits in the path. Same
        // self/trigger exclusion rules as CircleCastExcludingSelf, but slime hits
        // (anything with a SlimeController in its parent chain) are filtered out.
        public RaycastHit2D CircleCastIgnoringSlime(Vector2 origin, float radius, Vector2 dir, float maxDist, LayerMask mask)
        {
            var hits = Physics2D.CircleCastAll(origin, radius, dir, maxDist, mask);
            RaycastHit2D best = default;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (h.collider.isTrigger) continue;
                if (h.collider.GetComponentInParent<SlimeController>() != null) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h;
                }
            }
            return best;
        }

        // Trail-lagged slime position lookup. Returns the slime's recorded XY from
        // ~lagSeconds ago (interpolated to the nearest sample bucket). Used by Beam
        // mode tracking so the beam aims at where the slime was, not where it is.
        // Falls back to live target position when the trail is empty or lag<=0.
        public Vector2 GetLaggedTargetPosition(float lagSeconds)
        {
            if (target == null) return AimOrigin();
            if (lagSeconds <= 0f || slimeWorldTrail.Count == 0)
                return new Vector2(target.position.x, target.position.y);
            float interval = Mathf.Max(0.001f, trailSampleInterval);
            int lagSamples = Mathf.RoundToInt(lagSeconds / interval);
            int idx = Mathf.Clamp((slimeWorldTrail.Count - 1) - lagSamples, 0, slimeWorldTrail.Count - 1);
            return slimeWorldTrail[idx];
        }

        private void SampleTrail()
        {
            if (target == null) return;
            float now = Time.time;
            float interval = Mathf.Max(0.001f, trailSampleInterval);
            if (now - lastTrailSampleTime < interval) return;
            lastTrailSampleTime = now;

            slimeWorldTrail.Add(new Vector2(target.position.x, target.position.y));

            // Cap to (trailDuration + trailLagOffset) so even the oldest tendril
            // (lagFrac=1) at the largest lagOffset still has a sample available.
            float maxBackSec = Mathf.Max(0f, trailDuration) + Mathf.Max(0f, trailLagOffset);
            int maxSamples = Mathf.CeilToInt(maxBackSec / interval) + 2;
            while (slimeWorldTrail.Count > maxSamples) slimeWorldTrail.RemoveAt(0);
        }
    }
}
