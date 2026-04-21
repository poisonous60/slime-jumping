# MegaDeform2DCollider

Runtime feature that keeps a `PolygonCollider2D` or `EdgeCollider2D` in sync with a
MegaFiers-deformed mesh. Built because MegaFiers only ships `MeshCollider` (3D) support,
and this project is 2.5D with 2D physics.

## Files

| Path | Role |
|------|------|
| [SlimeJumping_v2/Assets/Scripts/Platforms/MegaDeform2DCollider.cs](../SlimeJumping_v2/Assets/Scripts/Platforms/MegaDeform2DCollider.cs) | Runtime component — reads deformed mesh verts, writes collider points |
| [SlimeJumping_v2/Assets/Scripts/Editor/MegaDeform2DColliderBaker.cs](../SlimeJumping_v2/Assets/Scripts/Editor/MegaDeform2DColliderBaker.cs) | CustomEditor + baker strategies (TopEdge / ZSlice / VertexPicker) |
| [SlimeJumping_v2/Assets/Scripts/Editor/BendTestDeformingSetup.cs](../SlimeJumping_v2/Assets/Scripts/Editor/BendTestDeformingSetup.cs) | Menu `SlimeJumping/Platform/Setup BendTest Deforming Platform` — sample prefab |
| `Assets/Prebs/Platform/BendTestDeformingPlatform.prefab` | Sample prefab: bent box with `EdgeCollider2D` that follows the top surface |

## Core idea: index-mapped contour

1. **At bake time** (editor): inspect the rest-pose mesh, produce an ordered `int[] contourIndices`
   — a list of vertex indices that describe the desired 2D silhouette.
2. **At runtime** (`LateUpdate`, execution order `int.MaxValue` so it runs **after**
   `MegaModifyObject.LateUpdate`): read `source.mesh.GetVertices(...)`, pick the verts at
   `contourIndices` in order, transform them into the collider's local space, and push into
   `EdgeCollider2D.SetPoints` / `PolygonCollider2D.SetPath`.

Per-frame cost is O(contour length) — typically tens of verts. No allocations after warmup
(both `List<Vector3>` and `List<Vector2>` buffers are reused).

## Runtime component — inspector fields

Component: [`MegaDeform2DCollider`](../SlimeJumping_v2/Assets/Scripts/Platforms/MegaDeform2DCollider.cs)
(menu `Add Component → SlimeJumping/Platforms/Mega Deform 2D Collider`).

| Field | Meaning |
|-------|---------|
| **Source** | The `MegaModifyObject` whose mesh drives the shape. The component reads `source.mesh` each frame. |
| **Target** | A `PolygonCollider2D` **or** `EdgeCollider2D` (dispatched by type). Can live on the same GameObject or any other. |
| **Closed Loop** | `true` → connects the last contour point to the first. Relevant only for `PolygonCollider2D`. |
| **Path Index** | For `PolygonCollider2D.SetPath(pathIndex, ...)`. Keep at 0 unless you're layering multiple paths on one collider. |
| **Reverse On Negative Scale** | If the target's 2D lossy scale has negative product (x·y < 0), reverse the winding so Box2D doesn't treat the polygon as inverted. |
| **Contour Indices** | The baked ordered vertex indices. Normally set by a baker; can be edited manually. |

Attributes: `[ExecuteAlways]` + `[DefaultExecutionOrder(int.MaxValue)]`.

- `ExecuteAlways` → collider follows the deformation while scrubbing modifier values in the
  editor (not only at runtime).
- `DefaultExecutionOrder(int.MaxValue)` → guarantees our `LateUpdate` runs after MegaFiers'.
  MegaFiers `.Complete()`s all its job handles inside `SetMesh()` ([MegaModifyObject.cs:857, 870, 883, 894, 1215](../SlimeJumping_v2/Assets/ThirdParty/MegaFiers/Scripts/MegaFiers/Modifiers/MegaModifyObject.cs#L857)),
  so `mesh.GetVertices` sees finalized positions.

## Baker strategies

Baker inspector section lives below the component's fields. Pick a **Strategy** from the
dropdown, then press **Bake Contour Indices** (except for VertexPicker, which has its own
Start/Stop workflow).

### TopEdge

Picks the top surface of a mesh as an open polyline. Suited for `EdgeCollider2D` on things
like platforms where the slime walks on top.

**Algorithm**: filter verts with `y ≈ bounds.max.y` (within `Y Epsilon`) AND `z ≈ z_target`
(within `Z Epsilon`), sort by X ascending, dedupe near-duplicate X values. Result is an open
polyline, `closedLoop` is forced to `false`.

**Params**:
- `Z Side` — which side of the mesh to pick: `MaxZ`, `MinZ`, or a `Custom Z` value.
- `Y / Z / X Dedupe Epsilon` — tolerances. Defaults (1e-3, 1e-3, 1e-4) work for the
  BendTestBox (22 top verts).

### ZSlice

Picks the 2D silhouette at a chosen Z plane, as a closed polygon. Suited for
`PolygonCollider2D` on closed 3D meshes (e.g. the BendTestBox).

**Algorithm** — see [MegaDeform2DColliderBaker.cs `BakeZSlice`](../SlimeJumping_v2/Assets/Scripts/Editor/MegaDeform2DColliderBaker.cs):

1. Mark verts with `|v.z - zTarget| < zEps` as "in-slice".
2. Canonicalize by world position — collapse duplicate-position verts onto their lowest-
   index occurrence (a closed box has multiple vert indices at the same world point, one
   per face/normal).
3. Find **slice triangles** — triangles where ALL 3 verts are in the slice.
4. For each slice triangle, count its 3 edges (undirected, min/max-ordered key).
5. **Boundary edges** are those appearing in exactly one slice triangle. Interior edges
   appear in two.
6. Walk the boundary adjacency starting from the leftmost (min-X) vert; each boundary vert
   has degree 2 in a clean cycle, so the walk is unambiguous.
7. Compute shoelace signed area; if `Force CCW Winding` is on and the area is negative,
   reverse the cycle.

Result has `closedLoop` forced to `true`.

**Params**:
- `Z Side` / `Custom Z` — which Z plane to slice.
- `Z Epsilon` — slice-plane tolerance.
- `Position Dedupe Epsilon` — tolerance for merging same-position duplicates.
- `Force CCW Winding` — flip to counterclockwise if the walk produced clockwise.

**Failure modes** it reports:
- `no slice triangles` — no triangle has all 3 verts on the Z plane. Widen `Z Epsilon` or
  pick a different `Z Side`.
- `boundary is not a simple cycle` — the slice region branches or has multiple loops.
  Adjust the slice plane.

For the BendTestBox at `z = max Z`: 44 verts, 44 boundary edges, 42 slice tris — a closed
rectangle matching the box's front silhouette.

### VertexPicker

Interactive scene-view picking — click verts in order to build the contour by hand. Use
when neither TopEdge nor ZSlice fits the shape.

**Workflow**:
1. Set Strategy to `VertexPicker`.
2. Press **Start Picking**. Scene-view overlay appears:
   - White semi-transparent dots: un-picked verts.
   - Green dots with number labels: picked verts, labeled with their order (0, 1, 2, ...).
   - Orange dot: the most recently picked vert.
   - Cyan preview polyline connects picks in order (including the wrap-around edge if
     `Closed Loop` is on).
3. Click verts in outline order.
4. To remove a pick: **click a picked vert again** OR **Alt+click**.
5. **Clear Indices** wipes the array. **Reverse Order** flips the sequence (useful for
   fixing winding).
6. Press **Stop Picking** when done.

**Params**:
- `Dedupe By Position` (default on) — if you click at a world position that's already in
  the list (different vert index but same spatial point), the click is silently ignored.
  Turn off if you actually want duplicates.
- `Dedupe Epsilon` — tolerance for the position match.

## Typical usage recipes

### Case 1: `EdgeCollider2D` for a platform's walkable top surface

1. Add `EdgeCollider2D` to the platform GameObject (no points needed yet).
2. Add `MegaDeform2DCollider`. Assign `Source` = the `MegaModifyObject`, `Target` = the
   `EdgeCollider2D`. Leave `Closed Loop` off.
3. Strategy = `TopEdge`. Z Side = whichever face you care about (typically `MaxZ` for
   front-facing). Press **Bake Contour Indices**.
4. Done — play or scrub the modifier and the edge collider follows.

(This is exactly what `BendTestDeformingSetup.cs` automates for the sample prefab.)

### Case 2: `PolygonCollider2D` for a closed 3D mesh's silhouette

1. Add `PolygonCollider2D` to the GameObject.
2. Add `MegaDeform2DCollider`. Wire `Source`/`Target`, tick `Closed Loop`.
3. Strategy = `ZSlice`. Z Side = whichever depth slice is the 2D silhouette (e.g. `MaxZ`
   for the box's front face rectangle). Press **Bake Contour Indices**.
4. If the resulting winding looks wrong, toggle `Force CCW Winding` off/on or press
   `Reverse Order` (only visible in VertexPicker mode — alternately, re-bake).

### Case 3: Arbitrary silhouette, click-to-build

1. Add the collider and component as above.
2. Strategy = `VertexPicker`, **Start Picking**, click along the outline, **Stop Picking**.

## Gotchas

- **UpdateMode warning**: If `source.UpdateMode == MegaUpdateMode.OnRender`, MegaFiers
  writes verts in `OnRenderObject` — our `LateUpdate` runs earlier, so the collider lags
  by one frame. The component logs this once and falls back to stale data. Prefer
  `MegaUpdateMode.LateUpdate` (default).
- **Source/Target transform mismatch**: If the collider lives on a different GameObject
  than the mesh, the component handles the transform conversion via
  `target.transform.InverseTransformPoint(source.transform.TransformPoint(v))`. But scale
  mismatches can still surprise you — keep both transforms at uniform scale when possible.
- **Negative scale**: Flipping X or Y scale inverts polygon winding. `Reverse On Negative
  Scale = true` (default) detects `lossyScale.x * lossyScale.y < 0` and reverses the
  points. Disable if you're doing something exotic with nested mirrors.
- **Mesh vert count changing at runtime**: The baked `contourIndices` refer to indices in
  the rest-pose mesh. If you swap `source.originalMesh` to a different topology, re-bake.
- **`source.mesh` null before Start**: `MegaModifyObject.mesh` is bound in `Start` /
  `MeshChanged`. The component falls back to `originalMesh` so the collider still tracks
  rest pose even in the very first editor frame.
- **Assembly boundaries**: The baker lives in `Assets/Scripts/Editor/` (not
  `Editor/MeshTools/`), because the `MeshTools.asmdef` is isolated and doesn't reference
  Assembly-CSharp — so it can't see the runtime component. If you reorganize, mind the
  asmdef references.

## Extending

### Adding a new baker strategy

1. Add an enum entry to `BakeStrategy` in [MegaDeform2DColliderBaker.cs](../SlimeJumping_v2/Assets/Scripts/Editor/MegaDeform2DColliderBaker.cs).
2. Add a `DrawXxxParams()` method for the inspector section.
3. Add a `case BakeStrategy.Xxx` in `OnInspectorGUI`'s switch.
4. Add a `case BakeStrategy.Xxx` in `Bake()`'s switch that calls a static helper method.
5. Write the static helper, matching the pattern of `BakeTopEdge` / `BakeZSlice`:
   takes a `Mesh`, returns `int[]` (or `null` on failure with a `diag` string).
6. Include it in the "strategies with a Bake button" list at the bottom of
   `OnInspectorGUI`.

### Adding a new target collider type

Edit the `switch (target)` in [`MegaDeform2DCollider.LateUpdate`](../SlimeJumping_v2/Assets/Scripts/Platforms/MegaDeform2DCollider.cs).
For example, to support `CompositeCollider2D` you'd either unwrap it or issue
`compositeTarget.GenerateGeometry()` after writing to the child sources.

### Calling the baker programmatically

Both `BakeTopEdge` and `BakeZSlice` are `public static` and can be called from other
editor scripts. See [`BendTestDeformingSetup.cs`](../SlimeJumping_v2/Assets/Scripts/Editor/BendTestDeformingSetup.cs)
for an example (it calls `BakeTopEdge` during prefab construction).

## Why not modify MegaFiers directly?

Originally considered. Rejected because:
- MegaFiers lives in `Assets/ThirdParty/` → modifications break on reimport.
- `CLAUDE.md` has an explicit "divide visual and collider" rule.
- `MeshToCollider2DGeneratorLargestOnly.cs` already sets the project's precedent of
  wrapping rather than editing vendor code.

Instead, we run externally and read MegaFiers' public `mesh` field after its own
LateUpdate finishes — clean boundary, zero upgrade risk.
