# Snow surface-tag detection — attempts log

Working notes for the recurring problem of "is the slime currently standing on snow?" — i.e. when `SlimeController.RefreshSnowBlend` should swap `surfaceMod` from the host SurfaceTag to the snow SurfaceMaterial. As of writing, no approach has fully worked on the user's terrain mesh; this doc summarizes what's been tried so a fresh session can pick up without re-running the same dead ends.

## Problem statement

- Host platforms have a 3D-ish mesh + a 2D PolygonCollider2D for slime physics.
- `SnowField` extracts upward-facing top tris from the host mesh and renders snow on them. Snow does NOT cover the whole mesh — slope-rejected (steep) regions stay bare, and `excludeOverlapWithOtherPlatforms` may mask out parts.
- Gameplay: while slime is attached on the host, swap `surfaceMod` to snow tag iff the slime's **2D contact xy** is on a snow-covered region; otherwise keep the host's tag.
- Symptoms reported: slime visually on snow but tag reads as the host tag (Ice). Earlier attempt also leaked the snow tag onto the bare slope below.

## Architecture (unchanged through all attempts)

- `SnowFieldRegistry` keyed by `hostCollider`. `SnowField.OnEnable` registers, `OnDisable` unregisters.
- `SlimeController.UpdateSurfaceModifiers(col)` — at attach time, looks up the SnowField by `col`, caches `cachedSnowField` + `cachedSnowMods` (from `settings.snowSurfaceMaterial.ToModifiers()`).
- `SlimeController.RefreshSnowBlend` — every FU while attached/charging:
  - `sample = isTouchingSurface ? rb.position - surfaceNormal * slimeRadius : rb.position`
  - Calls a binary "is this point on snow" probe. Sets `surfaceMod = snow ?: host`.
  - Discrete fields (`useInertialSlide`, override flags, falloff curves) are pinned to the host so the slide model can't flip mid-attach.

The probe API is the only thing that has changed across attempts.

## Attempts

### Attempt 1 — original `SampleDepthRatio01` + threshold (pre-existing)

Probe: `cachedSnowField.SampleDepthRatio01(sample) > 0.05`.

`SampleDepthRatio01` walks each child `SnowStrip` and:
1. xy-AABB pre-filter on `verts[i].x/.y`, padded by `maxHeightRest` (≈ 0.3 m).
2. Nearest-vertex search by 2D distance in mesh-local xy.
3. Returns `heightCurrent[bestIdx] / heightRest[bestIdx]`.

**Failures observed:**
- AABB miss when host mesh's local xy ≠ world xy (or when the patch's xy footprint is narrow because most variation lives in mesh-local z).
- Bleed onto the bare slope: rim verts had `heightRest = restThickness` because `edgeTaperHops = 0`, so adjacent slope contacts read non-zero ratio.
- The `0.3 m` margin was too generous and pulled in adjacent geometry.

### Attempt 2 — PolygonCollider2D from `boundaryEdges`

Approach: at `RebuildNow`, chain `SnowStrip.boundaryEdges` (directed edges) into closed loops and emit a trigger PolygonCollider2D with one path per loop. Probe = `OverlapPoint(worldXY)` per child collider.

**Failure:** in the user's mesh, the boundary loops projected to xy collapse to nearly-coincident lines (front-face top edge and back-face top edge of a 2.5D-extruded host share the same xy), so the resulting paths are degenerate or even-odd-cancel each other → collider gizmo renders as a single line and `OverlapPoint` never hits.

This is fundamental to the boundary-projection approach for 2.5D extrudes.

### Attempt 3 — point-in-triangle in C# (mesh-local xy)

Probe: walk all patch tris, project to mesh-local xy, do sign-of-cross-product point-in-triangle. Degenerate (zero-area in xy) tris fall back to point-to-segment distance with a tolerance.

**Failure:** mesh-local xy ≠ world xy if the host transform has rotation, so the test silently misses everything.

### Attempt 4 — point-in-triangle in C# (world xy)

Probe: same as Attempt 3 but transform each tri vert to world via `sourceMeshT.TransformPoint` first, then do 2D test on world xy.

**Failure:** with the user's mesh, tris still didn't cover the contact point in world xy. SnowPlowDebug visualised the projected tris — the user reported they "didn't show well", which we assume means the projection collapsed flat onto a horizontal strip.

Cost-side concern was raised: 9600+ TransformPoint calls per FU for `subdivLevels=3`.

### Attempt 5 — current — EdgeCollider2D from z=planeZ cross-section

Approach: at `RebuildNow`, slice each patch tri against the world-z plane `z = snowTagPlaneZ` (settings asset), collect line segments, weld endpoints, chain into polylines/loops, emit one `EdgeCollider2D` per chain with `edgeRadius = snowTagEdgeRadius`. Probe = `OverlapPoint(worldXY)` per EdgeCollider2D.

Rationale: the slime moves on the gameplay plane (world z = 0). The cross-section of the patch at that z is exactly the 1D outline of the snow that the slime can physically touch. No xy-projection collapse, no AABB heuristic, native physics OverlapPoint.

**Status: NOT WORKING in user's scene.** Even with `snowTagEdgeRadius = 10`, no EdgeCollider2D is created.

This means `BuildSnowEdgeColliders` returned early, most likely because `rawSegs.Count == 0` — no patch tri intersected `planeZ`. Either:
- The host mesh's world z range doesn't bracket `snowTagPlaneZ = 0` (mesh is fully in front of or behind the gameplay plane), or
- All tris are exactly co-planar and the slice algorithm's coplanar handling missed them.

**Diagnostics added but not yet validated by user:**
- `SnowStrip.debugPatchZMin / debugPatchZMax / debugSegmentCount / debugChainCount` populated each build.
- `SnowPlowDebug.sliceDiagnostics` shows per-field: `planeZ`, patch z range, strip count, segs, edges, ⚠ if planeZ outside patch z range.
- Also added lay-on-tri handling (`aOn && bOn && cOn` → emit all 3 edges as segments) and bumped `kPlaneEps` from 1e-5 to 1e-3.

User has NOT yet reported the diagnostic readout. Next session should start there.

## Settings asset fields (live in `Snow_Default.asset`)

Added to `SnowSettings`:
- `snowTagPlaneZ` — world-z plane to slice at. Default 0.
- `snowTagEdgeRadius` — EdgeCollider2D thickness band. Default 0.05.

Accessors on `SnowField`: `SnowTagPlaneZ`, `SnowTagEdgeRadius` (settings-or-default fallback pattern matching the rest of the field).

## Carve drag / audio paths

Both still consume `SampleDepthRatio01` (height-based ratio, not the EdgeCollider2D probe). They do NOT need fixing for the surface-tag problem. `SnowSurfaceTagThreshold = 0.05f` in `SlimeController` is now only used by the plow-drag depth gate, not the tag swap.

## Files touched (current state)

- `SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowStrip.cs` — has `BuildSnowEdgeColliders`, `EdgeCross`, `DestroySnowEdgeColliders`, `snowEdges` list, debug counters. `IsPointInPatch` (Attempts 3-4) was removed.
- `SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowField.cs` — `IsPointOnSnow` now iterates child strips' `snowEdges` and calls `OverlapPoint`. `RebuildNow` calls `ss.BuildSnowEdgeColliders(sourceMeshFilter.transform, SnowTagPlaneZ, SnowTagEdgeRadius)` after `ss.Init(mr)`. Inspector fields removed (now in settings asset).
- `SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowSettings.cs` — `snowTagPlaneZ`, `snowTagEdgeRadius` fields added under "Gameplay coupling".
- `SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowPlowDebug.cs` — rewritten to pull from `SlimeController` (Position / SurfaceNormal / SlimeRadius / SurfaceCollider / ActiveSurfaceMaterial). Visualises EdgeCollider2D polylines + acceptance band. Has `sliceDiagnostics` field showing patch z range vs planeZ.
- `SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs` — `RefreshSnowBlend` calls `cachedSnowField.IsPointOnSnow(sample)` (binary). `SnowSurfaceTagThreshold` const repurposed for plow drag gate only.

## Suggested next steps for fresh session

1. **Read the diagnostic first.** Drop a SnowPlowDebug on a scene GO, wire its `slimeController` ref, look at the `Slice diagnostics` field. The patchZ range vs planeZ tells you if the problem is "planeZ outside the mesh" or "slice produced segments but the chain walk dropped them" or "everything looks fine but `OverlapPoint` still misses".
2. **If patchZ range is something like `[5, 6]` and planeZ is 0**, that's the answer — set `snowTagPlaneZ` to something inside the range (e.g. midpoint) in `Snow_Default.asset`.
3. **If patchZ brackets planeZ but `segs = 0`**, check the lay-on-tri path and `kPlaneEps`. Maybe vertex z values happen to fall inside the eps band on the wrong side (all reading "above" or all "below" with epsilon misclassifying).
4. **If `segs > 0` but `edges = 0`**, the weld/chain-walk logic dropped the segments. Inspect the chain-walk in `BuildSnowEdgeColliders`.
5. **If edges > 0 but `OverlapPoint` still misses**, check the EdgeCollider2D's transform — the points are stored in strip-local 2D space and the strip GO is parented under SnowField with `worldPositionStays: false`, so its local space matches SnowField's. Verify by drawing `ec.transform.TransformPoint(points[i])` vs the gizmo overlay.

Pragmatic fallback if Attempt 5 stays unworkable: drop the geometric approach entirely and **filter by 2D contact-normal angle** (`SurfaceNormal.y > cos(maxSnowSlopeDeg)`) once we know the host has a SnowField registered. Loses per-region masking (overlap-mask, slope-cut) but is bulletproof for the 2.5D platformer case. Discussed but not implemented.
