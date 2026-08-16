using UnityEngine;
using SlimeJumping.Controller;

namespace SlimeJumping.Mechanisms
{
    public partial class WaterCurrentZone
    {
        [Header("Slime Interaction")]
        [Tooltip("Slime velocity → point velocity per second. Higher = stronger wake. " +
                 "Default 25 matches WaterZone_v2 (0.5/frame at 50Hz).")]
        [Min(0f)] [SerializeField] float impulseFactor = 25f;
        [Tooltip("Max slime-center-to-centerline distance for the per-FixedUpdate impulse. " +
                 "0 disables the gate (always apply). Acts as a soft outer boundary so slimes " +
                 "near the silhouette edge contribute less.")]
        [Min(0f)] [SerializeField] float contactRadius = 1.0f;
        [Tooltip("Number of neighbor points on each side of the impacted segment that also " +
                 "receive a weakened impulse. Spreads the wake over a wider arc length.")]
        [Range(0, 5)] [SerializeField] int spreadNeighbors = 2;
        [Tooltip("Per-step falloff for neighbor spread. 0.5 = each step halves the impulse weight.")]
        [Range(0f, 0.99f)] [SerializeField] float spreadFalloff = 0.5f;

        [Header("Audio")]
        [Tooltip("Played once when the slime enters the current. Empty = silent.")]
        [SerializeField] SlimeJumping.Audio.SoundDef enterSound;

        /// <summary>
        /// Slime → ribbon impulse: project onto the centerline, split L/R by lateral offset, kick
        /// both channels. Open polyline — neighbors clamp at array bounds, no modulo wrap like v2.
        /// </summary>
        void ApplySlimeImpulse(SlimeController slime, float dt, bool isEntry = false)
        {
            if (points == null || points.Length < 2 || dt <= 0f) return;
            if (impulseFactor <= 0f) return;

            Vector2 sWorld = slime.Position;
            Vector2 sVel   = slime.Velocity;

            int n = points.Length;
            int best = 0;
            float bestDSq = float.MaxValue;
            float bestT = 0f;
            Vector2 bestProj = Vector2.zero;

            for (int i = 0; i < n - 1; i++)
            {
                Vector2 a = transform.TransformPoint(points[i].restCenter);
                Vector2 b = transform.TransformPoint(points[i + 1].restCenter);
                Vector2 ab = b - a;
                float lenSq = ab.sqrMagnitude;
                if (lenSq < 1e-6f) continue;

                float t = Mathf.Clamp01(Vector2.Dot(sWorld - a, ab) / lenSq);
                Vector2 proj = a + ab * t;
                float dsq = (sWorld - proj).sqrMagnitude;
                if (dsq < bestDSq) { bestDSq = dsq; best = i; bestT = t; bestProj = proj; }
            }

            int j = best + 1;
            float bestDist = Mathf.Sqrt(bestDSq);

            // Bilateral weight: wR = 0.5 on the centerline, 1 at the right silhouette — the wake
            // favors the slime's side but still nudges the far side near the middle.
            Vector2 nLocal = Vector2.Lerp(points[best].normalLR, points[j].normalLR, bestT).normalized;
            Vector2 nWorld = transform.TransformDirection(nLocal);
            float halfW = ribbonWidth * 0.5f;
            float sideRaw = Vector2.Dot(sWorld - bestProj, nWorld);
            float sideNorm = Mathf.Clamp(sideRaw / Mathf.Max(halfW, 0.01f), -1f, +1f);
            float wR = (1f + sideNorm) * 0.5f;
            float wL = 1f - wR;

            // |velocity|, not the normal component — a slime moving parallel to the ribbon
            // must still leave a wake.
            float impactSpeed = sVel.magnitude;
            if (impactSpeed < 0.001f) return;

            // Gate stops a slime hovering just outside the silhouette from injecting energy
            // every step. Entry impulse is exempt — the splash sets the initial wave shape.
            float attenuation = 1f;
            if (!isEntry && contactRadius > 0f)
            {
                if (bestDist > contactRadius) return;
                attenuation = 1f - bestDist / contactRadius;
            }

            float baseImpulse = impactSpeed * impulseFactor * attenuation * dt;
            float impulseL = baseImpulse * wL;
            float impulseR = baseImpulse * wR;

            points[best].velL += impulseL * (1f - bestT);
            points[j].velL    += impulseL * bestT;
            points[best].velR += impulseR * (1f - bestT);
            points[j].velR    += impulseR * bestT;

            // Open-clamp at array bounds — an impulse near sample 0 stops there, no wrap-around.
            if (spreadNeighbors > 0 && spreadFalloff > 0f)
            {
                float falloff = spreadFalloff;
                for (int k = 1; k <= spreadNeighbors; k++)
                {
                    float weight = Mathf.Pow(falloff, k) * 0.5f;
                    int li = Mathf.Max(best - k, 0);
                    int ri = Mathf.Min(j + k, n - 1);
                    points[li].velL += impulseL * weight;
                    points[ri].velL += impulseL * weight;
                    points[li].velR += impulseR * weight;
                    points[ri].velR += impulseR * weight;
                }
            }
        }
    }
}
