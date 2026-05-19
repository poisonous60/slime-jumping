# Bamboo Tip-Grab is a Rope-pattern flag, not a new SlimeState

The bamboo platform gains a "tip-grab" sub-mode where the slime anchors to the tip and A/D input drives bamboo tilt instead of crawl. We considered adding a new `SlimeState.Grabbed` so the behavior would have its own `FixedUpdate<State>` handler, but instead we follow the existing **Rope pattern**: a nullable `currentBambooGrab` handle on `SlimeController`, with the slime remaining in `Attached` / `Charging` throughout. The grab participates via a pre-probe `LockBambooGrabPosition()` (mirror of `LockRopePosition`), an input gate in the Attached partial that diverts A/D into `BambooPlatform.ApplyTiltTorque`, and an implicit release on Launch.

## Why

- The existing `Attached → Charging → Launch → Airborne` transition matrix is load-bearing (energy-conversion coyote, postLaunchTimer, attachToggleCount lockout, surface-probe hysteresis). Adding a fifth state forces every transition rule to be re-audited.
- Carrier Inheritance already produces the Reaction Boost via `PlatformCarrier.ω × arm` on the existing launch path — no new launch code needed. A `grabModeCarrierScale` multiplier on `BambooPlatform.VelocityTransferScale` is the only knob added.
- Rope is the precedent for "specific platform hijacks slime position + input each FU while the SlimeState stays Attached." The hook points exist (`SlimeController.cs:533-536` pre-probe block, `SlimeController.cs:608` inputX gate). Reusing them keeps the platform-specific behavior isolated to one partial file (`SlimeController.BambooGrab.cs`) per the CLAUDE.md partial-file convention.

## Consequences

- Grab loss (slime separates from tip mid-oscillation) must not increment `attachToggleCount`, otherwise wild swings would trip the attach-lockout. Handled by routing the Airborne transition through an `intentional: true` style gate.
- `SurfaceProbe.SnapToSurface` is skipped while `currentBambooGrab != null` — the tip-lock is the source of truth for position, not the probe's nearest-segment guess.
- The grab persists across the Attached → Charging transition, so the player can charge a launch while still swinging the bamboo. This is the entire point of the feature (mid-swing launch = max Reaction Boost) but means anyone touching the Charging state must remember the grab handle can still be set.
