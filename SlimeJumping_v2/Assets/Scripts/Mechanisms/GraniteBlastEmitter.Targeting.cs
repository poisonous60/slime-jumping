using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    // Target detection, aim resolution, cast helpers, and the slime position trail.
    public partial class GraniteBlastEmitter
    {
        // World-space slime trail, oldest at index 0. PlanBranching snapshots it at FireStart.
        private readonly List<Vector2> slimeWorldTrail = new List<Vector2>(64);
        private float lastTrailSampleTime = -999f;

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
            // Physics2D.RaycastAll order is not guaranteed — pick the closest by distance.
            var hits = Physics2D.RaycastAll(origin, dir, maxDist, mask);
            RaycastHit2D best = default;
            float bestDist = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                // Triggers on the hit mask (InteractionSensor / Water) would stop the beam at the muzzle.
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

        // Swept radius forgives a stale aim direction so tendrils still end on slime/terrain
        // instead of maxRange. Slime triggers hit — its body collider may be a trigger.
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

        // Skips slime so Beam pass-through finds the ground BEHIND it and still carves SnowField.
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

        // Slime position from ~lagSeconds ago, so Beam tracking aims at where the slime was.
        // Falls back to the live position when the trail is empty or lag<=0.
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

            // Include trailLagOffset in the cap, else the oldest tendril has no sample left.
            float maxBackSec = Mathf.Max(0f, trailDuration) + Mathf.Max(0f, trailLagOffset);
            int maxSamples = Mathf.CeilToInt(maxBackSec / interval) + 2;
            while (slimeWorldTrail.Count > maxSamples) slimeWorldTrail.RemoveAt(0);
        }
    }
}
