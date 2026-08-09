---
name: unity-diagnose
description: >
  Diagnose and fix a SlimeJumping gameplay/physics bug by instrumenting with a
  read-only debug component, then driving the already-open Unity Editor with
  unity-cli to read its logs, snapshot live state, and verify the fix. Use when
  debugging slime movement/physics/corner/slide bugs, instrumenting the
  SlimeController, reading the Unity console, recompiling scripts, attaching a
  debug component, screenshotting a view, or running C# in the Editor.
---

# unity-diagnose — instrument → read-log → fix loop for SlimeJumping

This project is a Unity 6 game. You cannot "run" it headless here — the Editor
is **already open** on the user's machine. You drive that running Editor through
the **`unity-cli`** bridge, wrapped by `.claude/skills/unity-diagnose/driver.mjs`.
The user plays the game (reproduces the bug in Play mode); you instrument,
read the logs, find the root cause, and fix.

This is the disciplined loop, not a guess-and-edit loop: **instrument first,
confirm the root cause with runtime data, then change code.** See the worked
example at the bottom (corner-wedge slide-accel bug).

All paths below are relative to the repo root (`SlimeJumping_v2_project/`).

## Prerequisites

- `unity-cli` on PATH (`unity-cli v0.3.21` verified). It talks to the open
  Editor over a local HTTP bridge.
- `node` (v24 verified) — runs the driver.
- **Unity Editor open on this project.** The driver does not launch Unity.

## The loop (driver = agent path)

```bash
# 0. (optional) clear stale console before a fresh repro
node .claude/skills/unity-diagnose/driver.mjs clear

# 1. Write a read-only debug component (see "Writing the probe" below),
#    then compile it:
node .claude/skills/unity-diagnose/driver.mjs compile
#    -> "compile clean (no errors/warnings)"  (exit 1 + CS errors if not)

# 2. Attach it to the Slime GameObject and save the scene (idempotent):
node .claude/skills/unity-diagnose/driver.mjs attach SlimeJumping.Debugging.CornerWedgeAccelDebugger
#    -> "attached CornerWedgeAccelDebugger to Slime (MainScene saved)"

# 3. Ask the user to enter Play mode and reproduce the bug.
#    While it runs, snapshot live controller state:
node .claude/skills/unity-diagnose/driver.mjs state
#    -> "state=Attached contacts=2/2 |vel|=... slideVel=... tan=... nrm=..."

# 4. Read the diagnostic log lines (filter by your tag):
node .claude/skills/unity-diagnose/driver.mjs logs CornerWedge 120

# 5. Diagnose from the numbers, edit the fix, then re-verify:
node .claude/skills/unity-diagnose/driver.mjs compile
node .claude/skills/unity-diagnose/driver.mjs logs CornerWedge 120   # confirm signal gone
```

Other commands:

```bash
node .claude/skills/unity-diagnose/driver.mjs              # usage
node .claude/skills/unity-diagnose/driver.mjs shot scene   # -> SlimeJumping_v2/Screenshots/_diagnose_scene.png
```

### Raw unity-cli (what the driver wraps)

If you need something the driver doesn't cover, call `unity-cli` directly. Pipe
C# via stdin to dodge shell escaping:

```bash
echo 'return "playing=" + UnityEditor.EditorApplication.isPlaying;' | unity-cli exec
unity-cli console --type log,warning,error --lines 60
unity-cli refresh_unity
unity-cli screenshot --view scene --output_path Screenshots/x.png
```

## Writing the probe (read-only debug component)

Project rule (CLAUDE.md + memory): **debug scripts pull from the controller via
public getters and write nothing back. No debug fields in `SlimeController`.**

**NEVER self-spawn the probe.** No `[RuntimeInitializeOnLoadMethod]` bootstrap, no
auto-created GameObjects on Play — the user rejected that form outright. A probe is
ALWAYS a plain MonoBehaviour attached to the Slime GameObject via
`driver.mjs attach <FullTypeName>` (visible in the Inspector, removable by hand).
Even probes that watch other objects (bells, platforms) live on the Slime and
resolve their targets in `Start()`.

Copy the shape of an existing one — the canonical template is
[CornerWedgeAccelDebugger.cs](SlimeJumping_v2/Assets/Scripts/Debug/CornerWedgeAccelDebugger.cs):

- Namespace `SlimeJumping.Debugging`, in `SlimeJumping_v2/Assets/Scripts/Debug/`.
- `[DefaultExecutionOrder(3002)]` so it samples *after* the controller +
  `SnapToSurface` + platform writes — i.e. the final resting state each FU.
- Pull only public getters: `slime.CurrentState`, `Velocity`, `Position`,
  `SurfaceTangent`, `SurfaceNormal`, `GravityVector`, `TangentSpeed`,
  `CrawlVelocity`, `Probe.ActiveContactCount`, `Probe.GetContactNormal(i)`, …
  (the inertial slide store is `TangentSpeed - CrawlVelocity`).
- Emit **one tagged line per FU** (`[YourTag] #frame key=val …`) so it greps
  cleanly. Promote the bug fingerprint to `Debug.LogWarning` with a marker
  (e.g. `<<< WEDGE-ACCEL`) so it stands out in the log dump.
- Gate logging to the situation (e.g. `ActiveContactCount >= 2` for corners) to
  avoid spam; sample position/velocity deltas every FU so they stay valid.

## Gotchas (battle scars — all hit this session)

- **The default `console` reads only error + warning.** Your `Debug.Log` lines
  are type `log` and are invisible unless you pass `--type log,warning,error`.
  The driver's `logs` command already does this. This is the #1 "my logs are
  empty" trap.
- **Runtime getters are null in edit mode.** `driver.mjs state` raised
  `NullReferenceException` until guarded — `Probe`/`rb`-backed getters only
  exist after `Awake`. The driver now returns "enter Play mode first"; run
  `state` only during Play.
- **`unity-cli` resolves a *relative* `--output_path` against the Unity project
  dir (`SlimeJumping_v2/`), not the repo root.** Passing `SlimeJumping_v2/…`
  doubles it to `SlimeJumping_v2/SlimeJumping_v2/…`. Use a path relative to the
  Unity project (`Screenshots/x.png`) or an absolute path. The driver's default
  is already correct.
- **Game-view screenshots are black when not in Play mode.** Use `shot scene`
  to see level geometry while stopped; `shot game` only after entering Play.
- **`refresh_unity` is async.** It returns immediately; the recompile happens
  after. Wait (~5s) before reading the console for compile errors — the driver's
  `compile` does this. After a script edit, **compile before `attach`** or
  `System.Type.GetType` returns null ("TYPE NOT FOUND").
- **Recompile is blocked in Play mode.** `refresh_unity` no-ops while playing, so
  a naive compile would falsely report "clean". The driver's `compile` checks
  `isPlaying` first and bails — exit Play mode before recompiling.
- **`unity-cli list` prints command help, not running instances**, and `console`
  occasionally returns "no Unity instances running" transiently even though the
  Editor is up — just retry; `exec`/`console` reconnect.
- **Pipe C# via stdin** (`echo '…' | unity-cli exec`), never as a quoted arg —
  shell escaping of `"`, `<`, `;` mangles real code.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `logs` prints nothing | You used raw `unity-cli console` without `--type log,...`; use the driver's `logs`, or add `--type log,warning,error`. |
| `attach` → "TYPE NOT FOUND" | Script not compiled yet. Run `compile` first, then `attach`. |
| `state` → NullReference / "enter Play mode" | Not in Play mode. Ask user to press Play, then re-run. |
| screenshot file is all black | Stopped + game view. Use `shot scene`, or enter Play for `shot game`. |
| `compile` reports CS errors | Read them; they're the real compiler output. Fix and re-run. |
| "no Unity instances running" | Transient bridge hiccup; re-run the command. Confirm with `echo 'return 1;' | unity-cli exec`. |

## Worked example (this session)

Bug: slime sliding down a wall into a concave corner (wall meets floor) wedged
and kept accelerating, then phantom-launched on break-free.

1. Built [CornerWedgeAccelDebugger.cs](SlimeJumping_v2/Assets/Scripts/Debug/CornerWedgeAccelDebugger.cs)
   logging `slideVel`, `dSlideVel`, `gravTan`, `velCmd`, `actualSpd`, `clipLoss`,
   `drainArmed`, per-contact angles.
2. `compile` → `attach` → user reproduced → `logs CornerWedge`.
3. Log showed the fingerprint: `actualSpd ≈ 0.14` (wedged) while `slideVel`
   climbed `-11 → -12 → … → -46` and `drainArmed=False` every frame — the
   inertial store banking gravity that produced no motion, released as a launch.
4. Root cause: [ComputeSlideSpeed (SlimeController.cs)](SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs)
   integrates `slideVelocity += gravAlongTangent·dt` unconditionally; the drain
   guard checked `velocity.sqrMagnitude < 0.01` (commanded velocity), which never
   fires at a corner where the store keeps velocity large.
5. Fix in [SlimeController.Attached.cs](SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Attached.cs):
   detect the wedge by the magnitude `ProjectVelocityAgainstContacts` *removed*
   (>15% drop) and resync `slideVelocity` to the surviving tangent speed.
6. `compile` clean; logs confirmed `slideVel` bounded, no `<<< WEDGE-ACCEL`.
