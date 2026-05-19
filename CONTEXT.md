# SlimeJumping

2.5D platformer where a sticky **Slime** moves between **Platforms** via a charge-and-launch slingshot. The slime adheres to any surface and is launched by aiming a charged shot, not by classical run/jump controls.

## Language

### Core entity

**Slime**:
Player-controlled circular rigidbody that adheres to surfaces. Always in one of four **SlimeStates**.
_Avoid_: player, character, ball

**SlimeState**:
One of `Attached | Charging | Airborne | Stunned`. Single source of truth for which `FixedUpdate<State>` handler runs.
_Avoid_: mode, status

**Surface**:
Any collider the slime can stick to (floor, wall, ceiling, platform body). Reported by the **SurfaceProbe** each FixedUpdate.
_Avoid_: ground, floor (those are slang for one specific surface orientation)

### Launch model

**Charge**:
The aim phase entered by pressing the charge button while **Attached**. Drags an **AimOffset** around the slime; release launches in the aim direction. Charge never reverts to Attached — it always ends in **Launch**.
_Avoid_: aim, draw, pull

**Launch**:
The instantaneous transition Charging → Airborne with velocity = `LaunchPower(aim) · aimDir + carrierInheritance`.
_Avoid_: jump, shoot, fire

**Hop**:
A minimum-power launch used as a short jump from Attached without full charge. Same code path as Launch.
_Avoid_: jump

### Platform integration

**Platform**:
Any collider authored as a level element the slime interacts with. May implement **PlatformMotor** if it moves, **IPlatformEffect** if it reacts to slime presence, **IRopePlatform** if it sags, or none if static.

**PlatformMotor**:
Base class for moving platforms. Provides `Velocity`, `AngularVelocity`, `VelocityTransferScale`, and an `ApplyRiderInfluence` hook the slime calls each FixedUpdate while **Attached**.

**Carrier Inheritance**:
The launch-time velocity bonus the slime receives from the platform it was attached to. Computed by **PlatformCarrier** as `(platform.Velocity + ω × arm) · VelocityTransferScale` at the Launch instant.
_Avoid_: platform boost, kick, transfer

### Bamboo platform

**BambooPlatform**:
A vertical cantilever **Platform** with a single base **Rigidbody2D** driving N collider segments that approximate a circular-arc bend. One **BendAngle** ODE state drives both segment placement and the **BambooBend.shader** visual.

**BendAngle** (`currentAngle`, degrees):
Tip deflection from rest. Positive = counter-clockwise around +Z (tip moves to −X). Drives the **BambooBend.shader** `_BendAngleDeg` uniform and the segment polyline.
_Avoid_: rotation, tilt (tilt is the *input action*, BendAngle is its state)

**Tip-Grab** (그랩 / 매달림):
A bamboo-specific interaction sub-mode in which the slime is anchored to the tip and player A/D input is rerouted from crawl into bamboo **Tilt** torque. Slime remains in `SlimeState.Attached` or `Charging` throughout — Tip-Grab is *not* a new SlimeState, it is a flag carried by the `currentBambooGrab` reference (Rope-pattern). Entered automatically when **Attached** to a bamboo segment AND `Vector2.Dot(surfaceNormal, gravityUp) > grabEnterDot`. Released on Launch.
_Avoid_: hanging, mount, climb

**Tilt** (기울임):
Player A/D input while in **Tip-Grab**, applied as additive torque on the bamboo's `pendingRiderTorque` channel. Independent of and additive to the existing impact-driven `ApplyRiderInfluence` pump.
_Avoid_: lean, sway

**Reaction Boost** (반동 부스트):
The lateral component of **Carrier Inheritance** that the slime receives when launched mid-swing from **Tip-Grab**. Maximum when `BendAngle ≈ 0` (peak `AngularVelocity` of the bamboo); zero at maximum deflection. Scaled by `grabModeCarrierScale` on top of the standard carrier formula.
_Avoid_: jump boost, swing launch

## Relationships

- A **Slime** is **Attached** to exactly one **Surface** at a time; the **Surface**'s parent **Platform** drives **Carrier Inheritance** at the next **Launch**
- A **BambooPlatform** owns one continuous bend curve; its segments share one **Rigidbody2D**, so the **Slime** sees one **Platform**, not N
- **Tip-Grab** is a flag on the **Slime**, not a **SlimeState** — orthogonal to Attached/Charging
- **Tilt** input and `ApplyRiderInfluence` both feed the same `pendingRiderTorque` channel on **BambooPlatform**

## Flagged ambiguities

- "Tilt" was originally ambiguous between *input action* and *current bend state* — resolved: **Tilt** is the *input*, **BendAngle** is the *state*.
- "Hanging" / "그랩" / "매달림" / "mount" were used interchangeably for the new bamboo top behavior — resolved: canonical term is **Tip-Grab**; aliases avoided.
- "Jump" is not a term in this project — the slime never has a classical jump button. Use **Launch** (charge-and-release) or **Hop** (minimum-power launch).
