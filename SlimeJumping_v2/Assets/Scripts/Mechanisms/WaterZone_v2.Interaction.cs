using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    public partial class WaterZone_v2
    {
        void ApplySlimeImpulse(SlimeController slime, float dt)
        {
            if (points == null || points.Length < 2 || dt <= 0f) return;
            if (impulseFactor <= 0f || contactRadius <= 0f) return;

            Vector2 sWorld = slime.Position;
            Vector2 sVel   = slime.Velocity;

            int n = points.Length;
            int best = 0;
            float bestDSq = float.MaxValue;
            float bestT = 0f;
            Vector2 bestProj = Vector2.zero;

            for (int i = 0; i < n; i++)
            {
                Vector2 a = WorldPos(i);
                Vector2 b = WorldPos((i + 1) % n);
                Vector2 ab = b - a;
                float lenSq = ab.sqrMagnitude;
                if (lenSq < 1e-6f) continue;

                float t = Mathf.Clamp01(Vector2.Dot(sWorld - a, ab) / lenSq);
                Vector2 proj = a + ab * t;
                float dsq = (sWorld - proj).sqrMagnitude;

                if (dsq < bestDSq) { bestDSq = dsq; best = i; bestT = t; bestProj = proj; }
            }

            float bestDist = Mathf.Sqrt(bestDSq);
            if (bestDist > contactRadius) return;

            int j = (best + 1) % n;

            // Blend rest normals along edge, then rotate to world.
            Vector2 nLocal = Vector2.Lerp(points[best].restNormal, points[j].restNormal, bestT).normalized;
            Vector2 nWorld = transform.TransformDirection(nLocal);

            // Which side of the surface is the slime on? +1 outside (normal side), -1 inside.
            float sideSign = Mathf.Sign(Vector2.Dot(sWorld - bestProj, nWorld));
            if (sideSign == 0f) sideSign = 1f;

            float impactSpeed = Vector2.Dot(sVel, nWorld);

            // Only push when slime is approaching the surface from its current side.
            //   Outside (sideSign +1) + moving inward (impactSpeed < 0) => approach
            //   Inside  (sideSign -1) + moving outward (impactSpeed > 0) => approach
            // sideSign * impactSpeed < 0 means approach. Moving away => let the spring restore.
            if (sideSign * impactSpeed >= 0f) return;

            float attenuation = 1f - (bestDist / contactRadius);
            float baseImpulse = impactSpeed * impulseFactor * attenuation * dt;

            points[best].velocity += baseImpulse * (1f - bestT);
            points[j].velocity    += baseImpulse * bestT;

            // Optional spread to ±spreadNeighbors.
            if (spreadNeighbors > 0 && spreadFalloff > 0f)
            {
                float w = spreadFalloff;
                for (int k = 1; k <= spreadNeighbors; k++)
                {
                    float weight = Mathf.Pow(w, k) * 0.5f;
                    points[(best - k + n) % n].velocity += baseImpulse * weight;
                    points[(j + k) % n].velocity        += baseImpulse * weight;
                }
            }
        }
    }
}
