using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Mechanisms
{
    // Branching mode — N curved jagged tendrils, each a cubic Bezier path with
    // perpendicular noise, collectively chasing the slime over the recorded trail.
    // All targeting math is world XY: no atan2, no sin/cos, no muzzle-relative angles.
    public partial class GraniteBlastEmitter
    {
        // Only the per-tendril LAG is frozen at FireStart; direction is read from the LIVE
        // trail at each spawn, so pending tendrils keep chasing through the Firing window.
        private struct PendingTendril
        {
            public int lagSamples;        // trail-index offset back from newest at spawn time
        }
        private readonly List<PendingTendril> pendingTendrils = new List<PendingTendril>(16);
        private int tendrilsSpawned;
        private int tendrilsAtFireStart;

        // Reused scratch for NavGraph A* output — one list instead of one per tendril.
        private readonly List<Vector3> navPathScratch = new List<Vector3>(32);

        private void PlanBranching()
        {
            pendingTendrils.Clear();
            tendrilsSpawned = 0;

            int n = Mathf.Max(1, tendrilCount);
            tendrilsAtFireStart = Mathf.Clamp(Mathf.RoundToInt(n * branchingFireStartFraction), 1, n);

            // Tendril i maps onto a trail sample from oldest (i=0) to newest (i=N-1), by
            // plain index math. Seconds → buffer-index counts via the fixed sample interval.
            float interval = Mathf.Max(0.001f, trailSampleInterval);
            int lagOffsetSamples = Mathf.RoundToInt(Mathf.Max(0f, trailLagOffset) / interval);
            int spreadSamples = Mathf.RoundToInt(Mathf.Max(0f, trailDuration) / interval);

            for (int i = 0; i < n; i++)
            {
                // i=0 fires first at the largest lag (lagOffset+duration); i=N-1 last at lagOffset.
                float lagFrac = n == 1 ? 0f : 1f - (float)i / (n - 1);
                int lagSamples = lagOffsetSamples + Mathf.RoundToInt(lagFrac * spreadSamples);
                pendingTendrils.Add(new PendingTendril { lagSamples = lagSamples });
            }
        }

        private void SpawnTendrilsUpTo(int targetCount)
        {
            int t = Mathf.Min(targetCount, pendingTendrils.Count);
            float segLen = Mathf.Max(0.1f, tendrilSegmentLength);
            float bow = Mathf.Max(0f, bowHeight);

            while (tendrilsSpawned < t)
            {
                var pt = pendingTendrils[tendrilsSpawned++];

                // Re-read every spawn so a moving/rotating emitter fires from the current
                // barrel, not a frozen PreFire snapshot.
                Vector3 muzzleOrigin = AimOrigin();
                float muzzleZ = muzzleOrigin.z;
                Vector2 muzzle2D = new Vector2(muzzleOrigin.x, muzzleOrigin.y);

                // Per-tendril fire SFX so each bolt has its own muzzle crack.
                if (fireStartSound != null)
                {
                    float pitch = 1f;
                    if (fireStartPitchJitter > 0f)
                        pitch = 1f + Random.Range(-fireStartPitchJitter, fireStartPitchJitter);
                    SoundService.PlayAt(fireStartSound, muzzleOrigin, SoundCategory.SFX_Object, fireStartVolume, pitch);
                }

                // Trail is sampled at this tendril's spawn moment; later tendrils resample.
                int bufLen = slimeWorldTrail.Count;
                Vector2 slimePos;
                if (bufLen > 0)
                {
                    int idx = Mathf.Clamp((bufLen - 1) - pt.lagSamples, 0, bufLen - 1);
                    slimePos = slimeWorldTrail[idx];
                }
                else
                {
                    slimePos = target != null
                        ? new Vector2(target.position.x, target.position.y)
                        : muzzle2D;
                }
                slimePos.x += Random.Range(-targetRadius, targetRadius);

                // Direction only — the path runs the full maxRange and the Tendril halts on
                // its own per-frame tip sweep, so a slime that moved away by the time the
                // tip arrives no longer stops the bolt.
                Vector2 delta = slimePos - muzzle2D;
                float dlen = delta.magnitude;
                Vector2 dir = dlen > 0.0001f ? delta / dlen : Vector2.right;
                Vector2 worldTarget = muzzle2D + dir * maxRange;

                // Three-way LOS gate: direct LOS → bezier; blocked but A* finds LOS-clear
                // nodes → polyline along the nav path; blocked with no usable nodes → bezier
                // anyway (halts at the wall, but the alternative is no shot at all).
                if (navGraph != null)
                {
                    Vector3 navStart = muzzleOrigin;
                    Vector3 navEnd = new Vector3(slimePos.x, slimePos.y, muzzleZ);
                    LayerMask wallMask = navWallMask;
                    bool directClear = navGraph.IsDirectPathClear(navStart, navEnd, wallMask);
                    if (directClear)
                    {
                        // (1) Direct LOS — skip graph routing, fall through to bezier.
                    }
                    else
                    {
                        navPathScratch.Clear();
                        if (navGraph.FindPath(navStart, navEnd, navPathScratch, wallMask,
                                              navPathCostJitter, navEntryJitter))
                        {
                        // Uses total polyline length so tip world-speed matches
                        // tendrilTravelSpeed however curved the path is.
                        float pathLen = 0f;
                        for (int k = 0; k < navPathScratch.Count - 1; k++)
                            pathLen += Vector3.Distance(navPathScratch[k], navPathScratch[k + 1]);

                        // Extend to maxRange along the last segment; without it a missed
                        // nav-routed bolt stops dead where the slime was at spawn.
                        if (navPathScratch.Count >= 2 && pathLen < maxRange)
                        {
                            int last = navPathScratch.Count - 1;
                            Vector3 tail = navPathScratch[last] - navPathScratch[last - 1];
                            if (tail.sqrMagnitude > 0.0001f)
                            {
                                navPathScratch.Add(navPathScratch[last] + tail.normalized * (maxRange - pathLen));
                                pathLen = maxRange;
                            }
                        }

                        float navDrawTime = tendrilTravelSpeed > 0f
                            ? Mathf.Max(0.001f, pathLen / tendrilTravelSpeed)
                            : tendrilDrawTime;

                        var go = new GameObject("GraniteBlast_Tendril_NavPath");
                        go.transform.position = navPathScratch[0];
                        var t2 = go.AddComponent<GraniteBlastTendril>();
                        t2.InitPath(this, navPathScratch,
                            segmentLength: segLen,
                            jaggedness: tendrilJaggedness,
                            coreWidth: tendrilCoreWidth,
                            drawTime: navDrawTime,
                            holdTime: tendrilHoldTime,
                            fadeTime: tendrilFadeTime,
                            canHit: true);
                        continue;
                        }
                        // A* failed — fall through to bezier.
                    }
                }

                // Bezier control points on the muzzle→target line. No upward bias unless bowHeight > 0.
                Vector3 p0 = muzzleOrigin;
                Vector3 p3 = new Vector3(worldTarget.x, worldTarget.y, muzzleZ);

                Vector3 line33 = Vector3.Lerp(p0, p3, 0.33f);
                Vector3 line67 = Vector3.Lerp(p0, p3, 0.67f);
                float j1x = Random.Range(-controlPointJitter, controlPointJitter);
                float j1y = Random.Range(-controlPointJitter, controlPointJitter);
                float j2x = Random.Range(-controlPointJitter, controlPointJitter);
                float j2y = Random.Range(-controlPointJitter, controlPointJitter);
                float j1z = Random.Range(-controlPointZJitter, controlPointZJitter);
                float j2z = Random.Range(-controlPointZJitter, controlPointZJitter);
                // Z bow swings the midpoint out of the muzzle plane while P0/P3 stay at
                // muzzleZ, so the bolt enters and lands in-plane under the perspective camera.
                Vector3 p1 = new Vector3(line33.x + j1x, line33.y + bow + j1y, muzzleZ + bowDepthZ + j1z);
                Vector3 p2 = new Vector3(line67.x + j2x, line67.y + bow + j2y, muzzleZ + bowDepthZ + j2z);

                // Control polygon length is an upper bound on arc length — denser segments for longer bolts.
                float controlPolyLen = Vector3.Distance(p0, p1)
                                     + Vector3.Distance(p1, p2)
                                     + Vector3.Distance(p2, p3);
                int segments = Mathf.Clamp(Mathf.RoundToInt(controlPolyLen / segLen), 6, 48);

                // Chord, not arc length, so tip speed along the line of fire matches
                // tendrilTravelSpeed regardless of bow/jitter.
                float chordLen = Vector3.Distance(p0, p3);
                float drawTime = tendrilTravelSpeed > 0f
                    ? Mathf.Max(0.001f, chordLen / tendrilTravelSpeed)
                    : tendrilDrawTime;

                SpawnTendril(p0, p1, p2, p3,
                    segments: segments,
                    jaggedness: tendrilJaggedness,
                    coreWidth: tendrilCoreWidth,
                    drawTime: drawTime,
                    holdTime: tendrilHoldTime,
                    fadeTime: tendrilFadeTime,
                    canHit: true);
            }
        }

        private void SpawnTendril(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                                  int segments, float jaggedness, float coreWidth,
                                  float drawTime, float holdTime, float fadeTime, bool canHit)
        {
            var go = new GameObject("GraniteBlast_Tendril");
            go.transform.position = p0;
            var t = go.AddComponent<GraniteBlastTendril>();
            t.Init(this, p0, p1, p2, p3,
                segments: segments, jaggedness: jaggedness, coreWidth: coreWidth,
                drawTime: drawTime, holdTime: holdTime, fadeTime: fadeTime, canHit: canHit);
        }
    }
}
