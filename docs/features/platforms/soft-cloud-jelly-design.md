# Soft Cloud (Jelly) Platform — Design Spec (resolved, post-Codex-review)

Status: DESIGN RESOLVED. Grilled with the user + adversarial Codex review; all 6 Codex
findings resolved (see "Codex review resolutions"). Ready for implementation planning.

## Goal

A new "soft cloud" platform, distinct from the jump-pad `CloudPlatform`. Visual = an imported
low-poly OBJ cloud mesh. When the slime contacts the cloud, the surface dents softly under the
contact, does an underdamped spring wobble ("띠용띠용" boing overshoot), and settles back.

Pure juice / material texture: NO launch, NO pass-through, NO movement drag. The slime visually
sinks in a little then pops back out to the surface.

## Architecture (resolved)

**`SoftCloudPlatform : PlatformMotor`** — a new component. Reuses the project's moving/deforming
platform infrastructure (`PlatformMotor` + `PlatformCarrier`), the same pattern SpringyBranch uses.

### Source of truth: a 2D contour field (NOT the visual mesh)
- At author time, bake a **rest 2D contour** from the imported mesh silhouette (reuse
  `MeshToCollider2DGenerator`). Validate in editor tooling: import scale/orientation
  (per mesh-import-workflow), contour ordering, and rim-point count (warn if too coarse → blocky dent).
- Each contour point owns **spring state**: a scalar/2D displacement along its inward normal +
  velocity. This contour IS the field and the single gameplay source of truth.
- **Collider**: `PolygonCollider2D.SetPath` from the displaced contour points.
- **Visual**: the imported low-poly mesh is a child (MeshFilter). Each vert samples the field
  (interpolated from nearby contour displacements by XY) and dents to follow. Visual reads the
  field, NEVER writes the collider → divide-visual-collider honored. Cosmetic-only effects must
  live on a separate child/shader, never in the shared field.

### Spring model (mirror SpringyBranchPlatform knobs)
- `naturalFreqHz` + `dampingRatio` (0..1, <1 = underdamped → 띠용띠용) + `passiveDecayRate` /
  `riderDecayRate` + `maxDeflection` clamp. Internally `stiffness = (2π·freq)²`.
- Elastic (spring returns to rest), NOT plastic. Per-contour-point spring, **weakly coupled to ring
  neighbors** via a discrete-Laplacian term (`neighborCoupling`, as a fraction of stiffness) so the
  surface deforms as one connected elastic sheet — a press drags the surroundings and the region
  moves as a coherent chunk (marshmallow), not a finely-divided per-point dimple. (Revised from the
  original pure-independent Q5=A after the local/jagged look; coupling is the Q5-B elastic-net
  behavior, done as a stable in-ODE Laplacian rather than an explicit spring mesh.)

### Drive (scripted, inspector knobs — NOT geometric penetration)
- Contact center = `rider.Position − rider.SurfaceNormal * rider.SlimeRadius`.
- Push direction = `−rider.SurfaceNormal` (into the surface, works from any contacted direction).
- `falloffRadius = (rider.SlimeRadius / rider.BodyScaleRatio) * radiusMult` — footprint conforms
  to a cut/scaled slime; `radiusMult` const reproduces a fixed radius.
- Sustained "weight" push while a rider is attached + an impulse scaled by `rider.ImpactNormalSpeed`
  injected on `OnLanded`. Held-dimple is the superset; small weight → transient behavior.

### Rider staying-on: probe on the freshly-dented collider (via the -100 ordering)
- `SoftCloudPlatform` deforms the mesh + collider in its **FixedUpdate at PlatformMotor order
  (-100)** — BEFORE `SlimeController`'s probe reads it, and `PolygonCollider2D.SetPath` applies
  synchronously (unlike `Rigidbody2D.MovePosition`), so the probe always reads the fresh dented
  collider, never a frame stale. (LateUpdate mesh-readback is dropped.) SurfaceProbe's Phase A/B
  then rides the slime with the dent — down as it deepens, up on the overshoot.
- **The deterministic, non-stale follow the design wanted comes from that -100 ordering guarantee**
  (which is *why* this is a PlatformMotor) — NOT from PlatformCarrier position-carry. A carry
  override was tried and removed: for a non-translating deformer the base carry delta is zero, and
  adding an explicit carry ON TOP of the probe double-applies the dent motion whenever a tick's dent
  change exceeds `contactSkin` (0.02) — a jitter that peaks exactly at the wobble crests.
  (Caught by the post-implementation Codex/audit review.) The blob contour is unambiguous for
  `ClosestPoint`, so — unlike the bending `BambooPlatform` — no `ISurfaceAnchorProvider` is needed.
- `maxSurfaceSpeed` caps per-tick dent motion so a fast wobble can never outrun the probe's detect
  range and drop the rider (the numeric detach invariant).
- `VelocityTransferScale = 0` — the wobble must never bake into a launch (pure juice, no boost).

### Multi-rider (superposition)
- Track the SET of `SlimeController`s whose `SurfaceCollider == this` collider (keyed by instance),
  via `IPlatformEffect` OnSlimeAttach/Stay/Detach. Each rider injects into the field with its own
  position + `BodyScaleRatio`; displacements superpose additively (two riders close → deeper dent;
  apart → two dimples). Each rider's own `PlatformCarrier` samples `GetRiderCarryDelta` at its
  position → multi-rider carry is automatic. Clean removal on detach.

### Charging (no special suppression)
- No charging-specific overshoot suppression. It was tried (Q12=C: clamp outward overshoot while
  charging) but abruptly froze the visible wobble the instant charging began — bad feel — and was
  never what protected the charge anyway. The surface keeps wobbling naturally through a charge.
- Charge-cancel safety comes entirely from `maxSurfaceSpeed`: the per-tick dent motion can never
  outrun the surface probe's detect range, so the probe never misses → charging never cancels
  from a moving surface. (This is the real guard; the overshoot clamp was a redundant, harmful add.)

### Safety / perf (applied silently, sensible defaults)
- Numeric clamps tie max dent depth + per-step surface motion to `SlimeRadius`, `surfaceCheckDistance`,
  `contactSkin`, `fixedDeltaTime` so the carried rider can never be moved out of probe range in one step.
- Perf gating: skip `SetPath` when the contour delta is below an epsilon; sleep the whole component
  when the field is at rest AND no riders; only occupied/recently-hit clouds tick; cap contour vertex
  count. (Mitigates the known `PolygonCollider2D.SetPath` → Box2D contact-cache invalidation spike.)

## Codex review resolutions

1. [high] Collider ordered after the probe → **deform + SetPath in FixedUpdate at order -100**
   (before SlimeController's probe); LateUpdate readback dropped.
2. [high] Clamp doesn't prove no-detach/chatter → SurfaceProbe Phase A/B rides the slime on the
   **fresh -100 collider** (absolute re-projection each tick, drift-free), and `maxSurfaceSpeed`
   caps per-tick dent motion below the probe's detect range so it can never outrun contact.
   (Post-impl review: the earlier carry-glue idea double-applied with the probe — removed.)
3. [high] Charging cancel → handled by `maxSurfaceSpeed` (probe never misses a bounded-speed surface,
   so charging never cancels). The earlier Q12=C outward-overshoot suppression was removed: it froze
   the wobble abruptly on charge (bad feel) and was redundant with the speed cap.
4. [medium] Single-rider field → **Q14=B** per-rider superposition, keyed by SlimeController instance.
5. [medium] Visual-readback = gameplay source → **Q11=A** 2D contour field is authoritative; visual
   mesh follows; editor validates the baked contour/import.
6. [medium] Per-FixedUpdate SetPath perf → epsilon-gated SetPath, sleep-when-idle, occupied-only ticking,
   capped contour count (silent).

## Remaining implementation-verification items (not design forks)
- Enforce/verify execution order: SoftCloud deform (-100) → PlatformCarrier carry → SlimeController
  probe, so carry consumes THIS frame's deform delta (confirm where `ApplyPositionCarry` is invoked).
- Confirm `SlimeController` auto-wires `PlatformCarrier.Attach` on attach for a `PlatformMotor` parent.
- Editor bake tool for the rest contour + import validation.
