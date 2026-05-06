# RopeV3 attach bug — investigation log

## Bug

After ANY non-rope contact between leaving a rope and touching it again, slime cannot reattach to the rope normally. Multiple visible symptoms surfaced across the attempts below.

## Repro (deterministic)

**Triggers the bug:**
- Charge-launch from rope → mid-air touch any other surface → land back on rope
- Slide off another surface → land on rope
- Any case where slime had a non-rope contact between leaving rope and re-contacting rope

**Does NOT trigger:**
- Charge-launch from rope → no intermediate contact → land back on rope
- Charge-launch from non-rope platform → land directly on rope (rope is first contact since launch)

## Smoking-gun signals (from `RopeV3Debug` dumps)

| Field | Observed |
|---|---|
| `state` | `Airborne` (stuck) |
| `touch` | `True` |
| `surfMine` | `True` (`surfaceCollider == rope.Edge`) |
| `surfAng` | `±180°` (`norm = (0, -1)`) — slime registered on rope's UNDERSIDE |
| `slimeY` | frozen across hundreds of FUs |
| `velocity.y` | grows unboundedly (>200 m/s observed) — gravity accumulates into velocity but Unity's contact solver pins position |
| `rA` / `aV` | `False` / `False` — controller doesn't call `ReportRiderContact` while Airborne |
| `pendCarry` / `consC` | `(0,0)` — carry channel never activates because state isn't Attached |
| `seg` | `-1` — `riderSegment` reset on launch, never re-set because attach never happens |

## Investigation tools added (still in code)

- **`Assets/Scripts/Platforms/RopeV3Debug.cs`** — multi-event recorder. Triggers on Detach / Attach / HighSpeed / OppositeKick / Manual. Dumps last N FUs around each event with cooldown. HUD overlay + console dump + gizmos.
- **`RopeV3.GetDebugSnapshot()`** — read-only introspection (riderActive, segment lock, contactPos, pendingCarry, lastConsumedCarry, maxTensionRatio, etc.).
- **`RopeV3.ConsumeRiderCarryDelta`** records last raw/clamped carry for diagnostic readback.
- **`RopeV3.NotifyLaunchRebound`** clears `riderSegment` to `-1` (prevents stale segment data in dumps after launch).

## Attempts (chronological)

### Attempt 1 — Filter rope colliders out of `IgnoredColliderList.RegisterOverlapping`

- **Hypothesis (first audit):** an unrelated rope collider gets captured into the post-launch `ReattachGuard` list, blocking probe re-attach.
- **Change:** `RegisterOverlapping` skipped any collider whose hierarchy contains an `IRopePlatform`.
- **Result:** Bug unchanged.
- **Verdict:** Hypothesis wrong — `surfMine=True` proves probe DID find the rope, so `IgnoredColliderList.Contains()` wasn't filtering it.
- **Status:** Reverted.

### Attempt 2 — Source-body filter in `ReattachGuard` (Option B)

- **Hypothesis:** Same as Attempt 1 but more general — only register colliders sharing rigidbody/transform.root with the launch source.
- **Change:** Added `Collider2D sourceCollider` parameter to `RegisterOverlapping`. Filtered overlaps via `BelongsToSameBody`. Updated 4 call sites.
- **Result:** Bug unchanged.
- **Verdict:** Same root reason as Attempt 1.
- **Status:** Reverted.

### Attempt 3 — Flip contact normal when rope ceiling-rejected

- **Hypothesis (second audit):** `EdgeCollider2D` is a 1-D polyline with no inherent inside/outside. Fast vertical motion lands the slime on the underside; contact normal points into gravity; `FixedUpdateAirborne`'s ceiling-reject gate at line 78 (`surfaceUp < landingReattachThreshold`) fires every frame and traps the slime. Unity's contact solver pins position, gravity accumulates into velocity unbounded.
- **Change:** In `FixedUpdateAirborne` before the ceiling check, if `surfaceCollider == currentRope.Edge && Dot(surfaceNormal, gravityUp) < 0`, flip `surfaceNormal` and `surfaceTangent`.
- **Result:** Stuck-Airborne resolved. But slime now attaches to the BOTTOM of the rope and falls — flipping normal alone doesn't reposition the slime, which is still physically below the rope.
- **Verdict:** Necessary but not sufficient.
- **Status:** Replaced by Attempt 4.

### Attempt 4 — Teleport above rope + flip (currently in code)

- **Change:** In `FixedUpdateAirborne` when normal points into gravity on a rope, set `rb.position = ropeContact + gravityUp * slimeRadius` and `surfaceNormal = gravityUp`. (First tried offset = `slimeRadius + contactSkin` but the gap caused next-FU detach; tightened to exactly `slimeRadius`.)
- **Result:** Bottom-attach resolved. New symptom: slime oscillates Attached ↔ Airborne while sitting on top of the rope.
- **Hypothesized cause of oscillation:** rope's spring rebound moves contact node by more than the slime's overlap range each FU → probe loses rope → state→Airborne (carry doesn't fire because gated on Attached/Charging) → gravity → slime falls back toward rope, possibly tunnels through → my fix teleports above → repeat.
- **Status:** Currently in code at `SlimeController.Airborne.cs:71-86`.

### Attempt 5 — Pre-probe rope position lock (currently in code)

- **Hypothesis:** Order-of-operations issue. Probe decides `isTouchingSurface` BEFORE carry can apply, but carry is gated on the very state the probe is about to drop. Need to snap slime to rope's current closest point BEFORE the probe runs so probe still finds contact.
- **Change:** In `SlimeController.FixedUpdate` (just before `CheckSurface`), if `currentRope != null && state ∈ {Attached, Charging}` and slime is within `1.5 × slimeRadius` of rope, set `rb.position = ropeContact + slimeRadius * (rb.position - ropeContact).normalized`.
- **Result:** User reports "this isn't the problem right now" — symptom not addressed by this change. Investigation pivoted.
- **Status:** Currently in code at `SlimeController.cs:614-636`.

## Currently active code changes (decide: keep or revert)

| File | Change | Attempt |
|---|---|---|
| `SlimeController.Airborne.cs:71-86` | Rope normal-flip + teleport above when contact normal points into gravity | 4 |
| `SlimeController.cs:614-636` | Pre-probe rope position lock | 5 |
| `RopeV3.cs` | `GetDebugSnapshot()`, `dbgLast*` fields in `ConsumeRiderCarryDelta`, `riderSegment=-1` reset in `NotifyLaunchRebound` | (debug only) |
| `RopeV3Debug.cs` | New file — debug recorder | (debug only) |
| `IgnoredColliderList.cs` | Reverted to original | — |
| `SlimeController.Launch.cs` | Reverted to original | — |

## Open hypotheses for next investigation

1. **Upstream tunneling prevention.** Set `EdgeCollider2D.edgeRadius > 0` (e.g. `0.05`) on the rope. Gives the polyline thickness so fast vertical motion can't trivially cross to the other side; normal-flip ambiguity reduced. Tuning change, no controller code touched. Cheapest test.

2. **State leak from intermediate touch.** The deterministic difference between working and broken repros is "did slime have a non-rope contact between rope-leave and rope-rearrive." Diff slime state field-by-field at the moment of rope re-contact in both cases. Candidates: `attachLockoutTimer`, `blockAttachCount`, `postLaunchTimer`, `coyoteTimer`, `carrier` reference, `surfaceMod` (cached snow/etc), `cachedSurfaceCollider`, `lastSurfaceNormal`, `prevSurfaceCollider`.

3. **`carrier.Attach()` leaking from intermediate platform.** When slime briefly attaches to non-rope, `carrier.Attach(intermediateSurface)` fires. Verify `carrier.Detach()` actually runs on every exit path. If carrier reference persists across the rope re-attach, downstream logic (carrier velocity inheritance, friction lookup) may be confused.

4. **Physics2D collision filter side-effect.** Verify nothing along the intermediate-touch path is calling `Physics2D.IgnoreCollision(slime, ropeEdge, true)` and failing to unset it.

5. **Carrier system equivalent for ropes.** `PlatformCarrier` + `IPlatformMotor` provide rigid follow-platform semantics. Ropes can't use it directly (each node has its own velocity), so `RopeV3` substitutes `SampleVelocityAt` (velocity-side) and `ConsumeRiderCarryDelta` (position-side). The pre-probe lock in Attempt 5 is the closest equivalent for "rigid follow" applied at position level. If we keep going down this path, the same approach should generalize to `SpringyBranch` once it implements `IRopePlatform.Edge`.

## Next-step recommendations (priority order)

1. **Inspector tweak — set `edgeRadius=0.05` on rope's `EdgeCollider2D`.** No code change; may eliminate the tunneling that creates wrong-side contacts in the first place. If this resolves the bug, reconsider whether Attempts 4 and 5 are still needed.

2. **State-diff probe.** Add a one-shot `Debug.Log` of `{attachLockoutTimer, blockAttachCount, postLaunchTimer, cachedSurfaceCollider, currentRope, carrier?.IsOnPlatform, prevSurfaceCollider}` at the FU when slime first touches rope after intermediate contact, in both working and broken repros. Diff names the variable that's actually different.

3. **Decide attempts 4 & 5.** If (1) or (2) reveals the true cause and renders 4/5 unnecessary, revert them. If they're still load-bearing, keep with explanatory comments tied to the validated cause.
