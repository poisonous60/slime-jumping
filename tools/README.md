# Tools

Editor-time C# scripts run via `unity-cli exec` for profiler analysis and scene mutation,
plus standalone Python utilities. None of these are part of the Unity build — they live
outside `SlimeJumping_v2/Assets/` and execute against the live Editor session (C#) or the
repo files directly (Python).

## Standalone Python

| File | Purpose |
|---|---|
| [`extract_preview.py`](extract_preview.py) | Bakes each piano song's opening chord loop + accompaniment figure (`preview` field) into `Assets/Audio/Music/*_score.json` — HarmAn-style chord extraction, stdlib only. `--dry-run` report / `--selftest`. See `docs/features/platforms/piano-keyboard.md`. |

## Invocation

All scripts are self-contained `unity-cli exec` snippets. Pipe the file via stdin
(the `--file` flag is not supported by `unity-cli`):

```sh
cat tools/profiler/load_and_analyze.cs | unity-cli exec --usings UnityEditor.Profiling,UnityEditorInternal
```

The `--usings` flag is required for any script in `tools/profiler/` that touches
`HierarchyFrameDataView` (in `UnityEditor.Profiling`) or `ProfilerDriver` (in
`UnityEditorInternal`) — the unity-cli default `using` set doesn't include these.
`tools/scene/` scripts work without `--usings`.

Most profiler tools have a `capturePath` literal at the top — edit it before piping,
or sed-substitute on the fly:

```sh
sed 's|17-43-58|19-00-48|' tools/profiler/load_and_analyze.cs | \
  unity-cli exec --usings UnityEditor.Profiling,UnityEditorInternal
```

## `tools/profiler/` — capture analysis

Read-only against `.data` profiler captures in `SlimeJumping_v2/ProfilerCaptures/`.
None mutate the scene.

| File | Purpose |
|---|---|
| [`load_and_analyze.cs`](profiler/load_and_analyze.cs) | Loads a capture and prints frame-time distribution, top-cost methods, top-self methods, and a slow-vs-fast quartile diff. The default first stop. |
| [`compare_captures.cs`](profiler/compare_captures.cs) | Diffs two captures side-by-side: per-percentile frame-time delta, top improvements, top regressions, current hotspots. Use to verify an optimization landed. |
| [`split_fast_slow.cs`](profiler/split_fast_slow.cs) | Splits an already-loaded capture by median frame-time, prints which methods grow most in slow frames. Useful after `load_and_analyze.cs` flags a problem — drill into *what* causes the spikes. |
| [`sample_recent_frames.cs`](profiler/sample_recent_frames.cs) | Aggregates the last 30 frames of an already-loaded capture into a hierarchy listing sorted by avg self-time. Quick "what's hot right now" view. |
| [`slow_frame_deep_dive.cs`](profiler/slow_frame_deep_dive.cs) | For a loaded capture: dumps slime collider details, top-N heavy polygons by vertex count, layer collision matrix, Physics2D settings, and full call-tree breakdown of the top-5 slow frames. Use when `load_and_analyze.cs` doesn't pinpoint the cause. |

`split_fast_slow`, `sample_recent_frames`, and `slow_frame_deep_dive` all assume
`ProfilerDriver.LoadProfile` was already called (e.g. by `load_and_analyze`) and
read the currently-loaded capture.

## `tools/scene/` — scene mutation

These modify the active scene with `Undo` recorded; save (Ctrl+S) to persist.

| File | Purpose |
|---|---|
| [`collider_survey.cs`](scene/collider_survey.cs) | Read-only inventory: counts of `Collider2D` by type, top scene-root parents, top `PolygonCollider2D` clusters by vertex count. |
| [`run_collinear_pass.cs`](scene/run_collinear_pass.cs) | Runs `TerrainColliderOptimizer.SimplifyClosedPath(eps=0.001m)` on every eligible `MeshCollider2DWrapper`. Idempotent — re-running on already-simplified paths is a no-op. |
| [`add_wrappers.cs`](scene/add_wrappers.cs) | Retroactively adds `MeshCollider2DWrapper` to legacy generator-created colliders that predate the wrapper component. Detects via `_Collider2D` name suffix + child `MeshFilter`. Skips trigger / blocked-component GOs. |

## Conventions

- File names: `snake_case.cs`. Tool name should describe what runs, not what it
  reads (e.g. `compare_captures.cs` not `comparison_results.cs`).
- Top-of-file comment: one short paragraph + invocation example.
- Hardcoded paths: prefer `@"e:\..."` literals over relative paths — `unity-cli exec`
  runs in the Editor's working directory which is not the repo root.
- All scene-mutation tools must `Undo.RecordObject` / `Undo.AddComponent` so a single
  Ctrl+Z reverts the run, and `EditorUtility.SetDirty` + `MarkSceneDirty` so Unity
  prompts to save.
