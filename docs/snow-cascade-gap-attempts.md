# Snow overlap-mask "cascade gap" — attempts log

Working notes for the recurring problem where the snow-overlap mask removes **wider regions than the foreign collider's actual footprint** when there are foreground objects on top of a SnowField host mesh. Several rounds of attempted fixes did **not** resolve the problem on the user's terrain. This doc records what was tried and why each failed, so a fresh session can pick up without re-running the same dead ends.

## Problem statement (per user observation, 2026-05-07)

> "눈이 생성되는 길이가 정해져있어. 그래서 눈 생성되는 길이 중에 일부가 침범하면 그 길이 전부 눈이 생성이 안 되서 사진처럼 비어있는 공간이 나타나는거야."
>
> "Snow generation has a fixed length. If part of that length gets intruded, the entire length becomes no-snow, so empty spaces appear like in the photo."

The user observed this **empirically** from the visual output. Previous fixes assumed the cause was "binary tri-drop with shared-vert cascade", which seemed plausible from a static read of `MaskOutOverlap` but **did not match the actual observed behavior**. The user's directive: *first investigate why this phenomenon occurs in the actual rendered output, even if the code doesn't appear to implement snow that way.*

This investigation has not been done. Every attempted fix below was based on a hypothesized cause that turned out to either not match what the user sees or to be irrelevant for the user's terrain configuration.

## Scene context

- Host platform: a `TerrainShape2D`-generated mesh with a wrapper `PolygonCollider2D` produced by `MeshToCollider2DGenerator` (slices the host mesh at world-Z = 0). One wrapper per host.
- Foreground objects: small props on top of the host. Each has its own `TerrainShape2D` mesh and `PolygonCollider2D`.
- All foreigns are `PolygonCollider2D`. Many are **multi-path** (outer + holes) because they're produced from mesh slicing. Convexity of each path is **not** enforced.
- `excludeOverlapWithOtherPlatforms = true`. The user toggles between `SampleBased` (default) and `GeometricClipping` (added this session) modes.

## Attempts

### Attempt 1 — Knob suggestions: `subdivLevels` ↑ / `edgeTaperHops` ↓

**Hypothesis**: the visible gap is caused by:
- Cascade drop via shared verts (binary tri drop where any vert in foreign drops the whole tri, then adjacent tris sharing that vert also drop).
- BFS taper at the new cut boundary fading nearby verts to height 0.

**Suggested mitigations**: bump `SnowSettings.subdivLevels` for finer tris (smaller cascade, smaller fade width); drop `edgeTaperHops` to 0 for sharp cut.

**Result**: not implemented as code, just documented. User wanted a real structural fix, not a knob workaround.

### Attempt 2 — `Manual Keep Colliders` override

**Hypothesis**: let the designer plug specific gaps with a `PolygonCollider2D`/`BoxCollider2D` listed on the SnowField. Tris overlapping the keep collider get re-included (or kept whole, in clipping mode).

**What was built**:
- `SnowField.manualKeepColliders` field.
- Initial implementation: 4-sample test (3 verts + centroid) checked against keep colliders. **Failed** — small keep polys between sample points restored nothing, partial-overlap restored either fully or not at all → blocky grid pattern. *Replaced with proper tri-vs-collider overlap test (verts in either, edges cross).*
- Box support added later via 4-corner extraction.

**Result for the cascade-gap problem**: works for *individual* gaps if the user manually places a keep collider. **Does not address the root cause.** The user explicitly does not want to hand-author gap fillers per scene.

### Attempt 3 — Z-aware mask (`maskUsesZ` / `maskZWindow`)

**Hypothesis**: a different problem the user described — Z-deep host meshes had their back-Z slice incorrectly masked by foreground colliders at front-Z.

**What was built**: per-foreign Z filter using foreign's `transform.position.z` vs the snow tri's centroid Z, gated by a window.

**Result**: works for the Z problem (orthogonal feature). Has nothing to do with the cascade gap, but mentioned here to avoid confusion when reading the diff.

### Attempt 4 — `GeometricClipping` mask mode (Sutherland-Hodgman, broken first version)

**Hypothesis**: replace binary sample-based drop with proper geometric subtraction `tri ∩ NOT(foreign)`. Each tri gets clipped exactly at the foreign's edges, no cascade, tighter rim.

**What was built**: SH with "keep outside half-plane" applied sequentially per foreign edge.

**Bugs introduced** (both present in the first version):

1. **`IsOutsideEdge` inequality reversed**: for a CCW polygon, OUTSIDE means `cross > 0`, not `cross < 0`. With the reversed sign, the function returned INSIDE.
2. **Algorithm structurally wrong**: even with the inequality fixed, "intersection of OUTSIDE half-planes for every edge of a convex polygon" = empty (every point is INSIDE at least one edge of any convex polygon). The sequential SH-keep-outside loop collapses to either empty or `tri ∩ foreign`, never `tri − foreign`.

**Result**: first GeometricClipping shipped this. User saw snow appear *only inside foreigns* (the opposite of intent). Reported as: "Geometric Clipping으로 하니까 눈이 사라지는데."

### Attempt 5 — `GeometricClipping` rewrite to polygon-difference

**Hypothesis**: fix Attempt 4 by switching to a correct polygon-difference algorithm: for each foreign edge, **split** the current polygon into `inside` and `outside`, accumulate the `outside` pieces as final results, continue clipping the `inside` part with the next edge. After all edges, the remaining "inside everything" is `tri ∩ foreign` and gets discarded.

**What was built**: rewrote `ApplyClippingMask` with `SplitPolygonByEdge`. Removed `IsForeignFullyInsideTri` (the corrected algorithm handles the donut case natively). Added foreign-AABB pre-filter to skip far-away foreigns.

**Theory**: correct for convex foreigns (`BoxCollider2D`, single-path convex `PolygonCollider2D`). Falls back to per-pair sample-based for unsupported types.

**Result for the user's terrain**: **no observable improvement.** The user's foreigns are multi-path / potentially-concave `PolygonCollider2D` from mesh slicing, so they hit the per-pair sample-based fallback — which has the same behavior as `SampleBased` mode for those foreigns. User reported: "지금 Sample based로 했을 때보다 결과가 안 좋은데." Even after this fix, GeometricClipping was at best equivalent to SampleBased, not better.

### Attempt 6 — `isMaskCutAdjacent` flag + skip BFS taper at mask cuts

**Hypothesis**: tris that did get clipped by the polygon-difference path produced small pieces with most verts on the new cut boundary. BFS taper sets boundary verts to height 0, so small pieces become flat/invisible. Skipping mask-cut verts from the BFS seed list would let them keep full restThickness.

**What was built**:
- New `bool[] isMaskCutAdjacent` field on `TopPatch`.
- Populated in `ApplyClippingMask`: original tri corners (bary == 1 on one axis) → `false`; new clip verts (intermediate bary) → `true`.
- BFS taper in `RebuildNow` skips mask-cut verts from both `hopAll` and `hopSC` ramps.

**Result for the user's terrain**: **no observable change.** Same reason as Attempt 5 — for concave/multi-path PolygonCollider2D, the clipping path falls back to sample-based, which never produces clip verts, so `isMaskCutAdjacent` is empty for every patch. User confirmed: "방금 만든 코드는 아무 변화가 없어."

### Attempt 7 — `anyChanged` / drop-tracking bug fix

**What was found**: in the `GeometricClipping` path, the early-return `if (!anyClipped) return patch;` only checked whether fan-triangulation ran. If a tri was *dropped* via the per-pair sample-based fallback (and no other tri was fan-triangulated), `anyClipped` stayed `false` and we returned the *original* patch — silently undoing the drops.

**What was built**: renamed `anyClipped` → `anyChanged`, set it on the `triDropped` and `pieces.Count == 0` paths too.

**Result**: real bug, real fix, but the user reported **no observable change in their scene** ("아무 변화가 없어"). Possible explanations not investigated:
- The user's scene may not be triggering the buggy code path at all (e.g., no tri actually drops via the fallback under their config).
- The cascade gap they see is caused by something *upstream* of `MaskOutOverlap`.

## State of the code after revert (2026-05-07)

The user requested all the recent (Attempts 6 and 7) changes be reverted as "meaningless code that only adds complexity". Reverted:
- `isMaskCutAdjacent` field on `TopPatch`.
- Canon's `maskCut` parameter, the `outMaskCut` list, and `EmitClipVert`'s bary-based mask-cut detection.
- BFS taper's mask-cut skip in `RebuildNow`.
- `anyChanged` rename + drop-tracking fix.

Still in place from earlier this session:
- `OverlapMaskMode` enum + `maskMode` field. Default `SampleBased` (byte-identical to pre-session behavior).
- The corrected polygon-difference algorithm in `ApplyClippingMask` (Attempt 5). The `IsOutsideEdge` inequality is correct (`cross > 0`).
- `manualKeepColliders` (Attempt 2).
- `maskUsesZ` / `maskZWindow` (Attempt 3).
- `debugLogMaskColliders` toggle (independent debug tool).

So GeometricClipping mode currently:
- Is correct for `BoxCollider2D` and convex single-path `PolygonCollider2D` foreigns (compute `tri − foreign`).
- Falls back to sample-based per pair for everything else (concave, multi-path, Circle, Capsule, Edge, Composite).
- Has the silent-drop bug (Attempt 7's fix was reverted) — drops via the per-pair sample fallback may not propagate to the output patch in some cases. Acceptable for now since the user's scene didn't trigger an observable difference either way.

## What was never investigated (the user's actual ask)

The user's directive was:

> "내 관찰상 이런 현상이 나타나고 있다고. 실제로 코드가 이런 식으로 눈을 구현하는지 먼저 파악하고, 코드상에서 이렇게 눈이 구현이 안 되어있더라도 이런 식의 현상이 왜 나타나는지를 먼저 조사해."

Translated: "I observe this phenomenon. First understand whether the code actually implements snow this way, and **even if the code doesn't implement it this way, investigate FIRST why this phenomenon occurs**."

What "this phenomenon" means concretely (per user description):
- Snow appears to be generated in **fixed-length segments**.
- If **any part** of one of those segments gets intruded by a foreign object, the **entire segment** turns into no-snow — a visibly empty band, not just the part that overlaps.

Possible places to look (none confirmed; explicitly leaving as pointers for next session):

1. **`SnowTopSurfaceExtractor` connected-component split** — patches are union-find groups of upward-facing tris. Does the host mesh have a topology that produces narrow band-shaped patches? If so, masking a small part of one band could drop verts that disconnect the band into pieces below `MinPatchTris`.
2. **`MinPatchTris` filter** — applied to the *original* host triangulation before subdivision. Does the user's mesh have patches just barely above the threshold, such that any drop pushes them below it? But this filter is checked **before** `MaskOutOverlap`, not after — so masking shouldn't cause a patch to fall below the threshold post-hoc unless somewhere re-applies it.
3. **`SnowMeshBuilder.Build`** — emits per-tri front/back faces and a side strip from `boundaryEdges`. If the **side strip** is what's visible (rendered at full restThickness because the base is at host surface and the top is at vertNormal × restThickness), and the top face has been tapered to 0 across most of its extent, the user might be seeing only the side strip at the original outer boundary — which would look like "snow at the top edge of each step but missing across the middle".
4. **The host mesh's actual topology** vs. how the user *thinks* the mesh is laid out. The fixed-length-segment observation might be the patch boundaries themselves, not anything mask-related — i.e. the bands are how `Extract` always partitioned this mesh, and the user is *now* noticing them because the mask is making them more visible. The right fix in that case is in the mesh authoring or the extractor's connectivity rules, not the mask.
5. **`SnowField` re-emits one `SnowStrip` GameObject per patch.** If a patch is large and gets dropped entirely (e.g., its kept-tri count hits zero after mask), the whole strip GO is skipped — a single foreign somewhere in the band wipes out the whole band.

The fastest path to confirm any of (1)–(5) is **not more code edits** but **reproducing the user's scene with `debugLogMaskColliders` on, then dumping the patch list before and after `MaskOutOverlap`** (vert count, tri count, AABB) for a single rebuild. That tells you whether the empty bands are: (a) patches entirely dropped, (b) patches reduced to all-boundary verts, (c) patches that never existed because of the extractor's topology, or (d) something else entirely.

## Resolution (2026-05-07)

Resolved. Two corrections to the diagnoses above made it work:

### What the actual symptom turned out to be

The "fixed-length segment turned entirely off" hypothesis (Layer 2 in the planning doc — `kept == 0` wholesale patch wipe) was **overgeneralised**. After re-checking the user's scene with mask off → on toggle, the visible gap was **patch + ~1 sub-tri-edge ring**, not a whole patch erased. Patches stayed alive; only the rim around foreign objects was over-cut.

The cause of that 1-sub-tri ring is exactly the cascade noted in possibility (5)'s mechanism but at sub-tri scale, not patch scale: in the SampleBased path (and in GeometricClipping's per-pair fallback), a foreign vertex hit causes every sub-tri sharing that vertex to drop, so the dropped region extends 1 vertex-ring beyond the foreign's actual XY footprint.

### Why the GeometricClipping path didn't help on its own

`TryGetConvexForeignWorldVerts` accepted only `BoxCollider2D` and **convex** single-path `PolygonCollider2D`. The user's foreigns are 17–71-vert PolygonCollider2D from `MeshToCollider2DGenerator` — almost always concave or near-collinear-noisy. They were rejected, fell back to sample-based per pair, and the cascade returned. This is the gap Attempts 5–7 all worked around without addressing.

### What fixed it

1. **Concave / multi-path / Circle / Capsule decomposition** in `ApplyClippingMask`. New `TryDecomposeForeignPolygon` runs ear-clipping triangulation (with hole-bridging for multi-path) and then subtracts each resulting triangle as a convex piece via the existing per-edge SH loop. `(snow tri − foreign) ≡ ((snow tri − piece₁) − piece₂) − ⋯`. Circle/Capsule tessellated to N-gons inside `TryGetConvexForeignWorldVerts` so they take the convex fast-path directly.

2. **Collinear-vertex simplify pre-pass** for the convex fast-path. `MeshToCollider2DGenerator` emits many evenly-sampled verts along visually-straight edges. The original near-rectangular foreign had ~71 verts where 4 corners would be enough. Feeding all 71 into the SH per-edge loop accumulated float drift that pulled the kept boundary ~1 sub-tri inward at the rim — same ~1 sub-tri overshoot, opposite direction, but visually similar. `SimplifyCollinear` (perpendicular-distance ε=1e-4f) collapses collinear runs to true corners before SH; total polygon area is preserved exactly (collinear verts contribute zero to the shoelace integral).

3. **A compile error in the new code went unnoticed** because the unity-cli console returned `[]` for several rebuild attempts — Unity's compile cache hadn't refreshed after the asset import. The CS1628 (`ref` parameter captured in nested local function) blocked the new code from running in the user's session for ~30 minutes, making the changes appear ineffective. After force-recompile the fix took effect immediately. Lesson: when a code change "has no observable effect", verify the binary actually shipped (force recompile + check console errors) before assuming the algorithm is wrong.

### What stayed the same (no longer relevant)

- `OverlapMaskMode` enum + `maskMode` field — still defaults to `SampleBased` for byte-identical legacy behavior. SnowField instances that want the cascade-free path must opt in via inspector.
- `manualKeepColliders` (Attempt 2) — still useful as a designer-side override but no longer needed for the cascade gap.
- `maskUsesZ` / `maskZWindow` (Attempt 3) — orthogonal feature, unchanged.
- The corrected polygon-difference algorithm (Attempt 5) — still the foundation; the new decomposition just feeds more inputs into it.

The "wholesale patch wipe" failure mode (5) above remains theoretically possible for very small patches (host mesh with 3–5 host tris per bump and a foreign covering most of one bump's footprint), but didn't manifest in the user's scene. If it surfaces later, the same decomposition + collinear simplify combo applied in GeometricClipping mode would tighten the cascade to 0 and prevent it.
