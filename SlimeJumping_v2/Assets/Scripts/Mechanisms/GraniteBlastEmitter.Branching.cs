using System.Collections.Generic;
using UnityEngine;
using SlimeJumping.Audio;

namespace SlimeJumping.Mechanisms
{
    // Branching mode — N curved jagged tendrils, each a cubic Bezier path with
    // perpendicular noise, collectively chasing the slime over the recorded trail.
    // ───────────────────────────────────────────────────────────────────
    // World-space targeting. All math operates on world XY directly: no atan2,
    // no sin/cos, no muzzle-relative angles. P0/P1/P2/P3 are derived by adding
    // world axes and lerping world midpoints.
    // ───────────────────────────────────────────────────────────────────
    public partial class GraniteBlastEmitter
    {
        // Branching: only the per-tendril LAG (in trail-buffer index units) is frozen
        // at FireStart. The actual direction reference is looked up from the LIVE
        // trail at each tendril's spawn moment, so still-pending tendrils continue
        // to chase the slime through the Firing window. Once a tendril has been
        // spawned (GraniteBlastTendril GameObject created), its bolt is baked.
        private struct PendingTendril
        {
            public int lagSamples;        // trail-index offset back from newest at spawn time
        }
        private readonly List<PendingTendril> pendingTendrils = new List<PendingTendril>(16);
        private int tendrilsSpawned;
        private int tendrilsAtFireStart;

        // Reused per-spawn scratch buffer for NavGraph A* output, so each tendril
        // doesn't allocate its own list.
        private readonly List<Vector3> navPathScratch = new List<Vector3>(32);

        private void PlanBranching()
        {
            pendingTendrils.Clear();
            tendrilsSpawned = 0;

            int n = Mathf.Max(1, tendrilCount);
            tendrilsAtFireStart = Mathf.Clamp(Mathf.RoundToInt(n * branchingFireStartFraction), 1, n);

            // Snapshot the slime's world-XY trail. Tendril i (0..N-1) maps onto a
            // sample from oldest (i=0) to newest (i=N-1). All work is plain index math
            // on a list of world Vector2 — no time interpolation, no angles. After this
            // function returns, no further tracking happens; slime movement cannot
            // shift any pending tendril.
            // Convert seconds → buffer-index counts using the fixed sample interval.
            //   lagOffsetSamples = how many indices back from newest even the latest tendril references
            //   spreadSamples    = additional spread distributed across tendrils for chase
            float interval = Mathf.Max(0.001f, trailSampleInterval);
            int lagOffsetSamples = Mathf.RoundToInt(Mathf.Max(0f, trailLagOffset) / interval);
            int spreadSamples = Mathf.RoundToInt(Mathf.Max(0f, trailDuration) / interval);

            for (int i = 0; i < n; i++)
            {
                // Tendril i: lagFrac=1 (i=0, fires first) → lag = lagOffset + duration.
                //            lagFrac=0 (i=N-1, fires last) → lag = lagOffset only.
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

                // LIVE muzzle position — re-read every spawn so a moving emitter (or
                // an emitter rotated to track) keeps the bolts coming out of the
                // current barrel position, not a frozen PreFire snapshot.
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

                // LIVE trail lookup at this tendril's spawn moment — pending tendrils
                // continue to track the slime through the Firing window. Only THIS
                // tendril's direction is computed now; future tendrils will resample
                // when they spawn.
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

                // Direction-only reference. The bolt's full-length path is laid out
                // to maxRange and the Tendril itself does per-frame projectile-style
                // collision sweeps at its tip — if at the moment the tip reaches a
                // position there's a slime/terrain collider there, the bolt halts;
                // if not, it continues. So a slime that was on the line at fire time
                // but moved away by the time the tip arrives no longer halts the bolt.
                Vector2 delta = slimePos - muzzle2D;
                float dlen = delta.magnitude;
                Vector2 dir = dlen > 0.0001f ? delta / dlen : Vector2.right;
                Vector2 worldTarget = muzzle2D + dir * maxRange;

                // NavGraph A* path branch. Three-way gate (LOS-aware):
                //   (1) muzzle has direct LOS to slime (no walls between) → fall through
                //       to bezier direct fire. NavGraph only contributes when needed.
                //   (2) walls block direct shot, A* finds LOS-clear entry/exit nodes →
                //       polyline tendril follows the curved nav path.
                //   (3) walls block direct shot, no LOS-clear nodes (or graph
                //       disconnected) → fall through to bezier (will likely halt at
                //       the wall, but designer's choice; the alternative is no shot).
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
                        // Travel time honors total polyline length so tip world-speed
                        // matches `tendrilTravelSpeed` regardless of how curved the
                        // path is.
                        float pathLen = 0f;
                        for (int k = 0; k < navPathScratch.Count - 1; k++)
                            pathLen += Vector3.Distance(navPathScratch[k], navPathScratch[k + 1]);

                        // Extend the polyline past the slime's snapshot position in the
                        // last-segment direction so the bolt keeps flying when it misses
                        // — same contract as the bezier branch (path laid out to maxRange,
                        // Tendril's per-frame tip CircleCast halts on first wall/ground).
                        // Without this, a missed nav-routed bolt stops dead at the spot
                        // the slime was when it spawned.
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
                        // A* failed — no LOS-clear entry/exit nodes, or graph
                        // disconnected. Fall through to bezier (likely halts at the
                        // wall, but designer's choice).
                    }
                }

                // World-space bezier control points — pure XY arithmetic on the
                // muzzle→target straight line. No upward bias unless bowHeight > 0.
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
                // Z bow drives the midpoint OUT of the muzzle plane along world Z.
                // P0/P3 stay at muzzleZ (bolt enters and lands in plane), but the
                // midpoint can swing forward/back — the LineRenderer renders the
                // resulting curve naturally under the perspective camera.
                Vector3 p1 = new Vector3(line33.x + j1x, line33.y + bow + j1y, muzzleZ + bowDepthZ + j1z);
                Vector3 p2 = new Vector3(line67.x + j2x, line67.y + bow + j2y, muzzleZ + bowDepthZ + j2z);

                // Segment count uses the bezier control polygon length (upper bound on
                // arc length) — denser segments for longer bolts.
                float controlPolyLen = Vector3.Distance(p0, p1)
                                     + Vector3.Distance(p1, p2)
                                     + Vector3.Distance(p2, p3);
                int segments = Mathf.Clamp(Mathf.RoundToInt(controlPolyLen / segLen), 6, 48);

                // Travel time uses the straight-line chord (muzzle → maxRange endpoint)
                // so the tip's world-space speed along the line of fire matches
                // `tendrilTravelSpeed` exactly, regardless of bow/jitter on the curve.
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
