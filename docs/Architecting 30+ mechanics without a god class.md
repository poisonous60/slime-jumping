# Architecting 30+ mechanics without a god class

**The single most important pattern for SlimeJumping's expansion is responsibility inversion: world objects act on the player through narrow interfaces, not the other way around.** Every major indie platformer — Celeste, Hollow Knight, Rain World — converges on this principle despite radically different engines and architectures. Celeste's Solids define `OnDashCollide` callbacks. Hollow Knight attaches `DamageHero` components to hazards rather than making HeroController detect them. Rain World distributes behavior across per-creature AI classes. The lesson is consistent: when you need 30+ interactive object types, the player controller must become a *service provider* exposing thin interfaces, not a dispatcher that knows about every object type.

This report synthesizes architecture research from five major platformers, Unity-specific patterns, and technical implementation details for each of SlimeJumping's planned features — all mapped against the existing codebase (partial-class SlimeController, SurfaceProbe, `AddExternalVelocity()`, GravityField stack, SignalSource/UnityEvent system).

---

## How Celeste, Hollow Knight, and Rain World solve this exact problem

All three games face the same challenge SlimeJumping faces: a single player character interacting with dozens of object types. Their solutions differ in implementation but share core principles.

**Celeste** (C#/MonoGame, Monocle Engine) splits all physics entities into exactly two base classes: **`Solid`** (level geometry, platforms, moving blocks) and **`Actor`** (player, projectiles, anything affected by physics). Solids cannot collide with other Solids. When a Solid moves, it automatically pushes or carries any Actor riding it — the platform logic lives *on the platform*, not in the Player class. For specific interactions like dash collisions, Solids expose an `OnDashCollide` delegate that the Player invokes without knowing the Solid's type. Dream blocks, zip movers, bumpers, and springs all work through this single delegate pattern. The Player class uses `CollideCheck<T>()` for type-specific checks (springs, water) and a `StateMachine` component with integer-indexed states (`StNormal`, `StSwim`, `StBoost`, `StStarFly`). Notably, **Celeste's developers deliberately kept the Player as a large monolithic file**, arguing that theoretical architectural purity was less important than precise control over frame-sensitive mechanics — but they offloaded interaction *responses* to world objects.

**Hollow Knight** (Unity + PlayMaker) takes the world-object-ownership pattern further. Every hazard in the game has a **`DamageHero`** component with a `hazardType` integer. When the player touches a hazard collider, `DamageHero` calls `HeroController.TakeDamage(hazardType, damage)` — the player only needs to handle a few damage *categories*, not every individual hazard type. Enemy behavior is entirely driven by **PlayMaker FSMs** attached to each enemy GameObject, meaning artists could create complex enemy behaviors without modifying the player code. HeroController is honestly a god-class singleton, but the architecture survives because behavior logic is distributed across the FSMs on world objects.

**Rain World** (Unity, custom C#) uses the most sophisticated architecture: a deep class hierarchy rooted in `UpdatableAndDeletable → PhysicalObject → Creature → Player`. Every creature has three separated concerns: a **core class** (physics, state), a **graphics class** (procedural animation), and an **AI class** (behavior decisions). Rain World also implements a dual-layer **Abstract/Realized** system where rooms far from the player downgrade to lightweight `AbstractRoom` representations, reducing hundreds of creatures across 1600+ rooms to manageable simulation cost. The player is just another `Creature` in the hierarchy — **no special treatment, no god class**.

| Aspect | Celeste | Hollow Knight | Rain World |
|--------|---------|---------------|------------|
| Who owns interaction logic? | Hybrid: Solid callbacks + Player type-checks | World object (DamageHero, PlayMaker FSMs) | Distributed (per-creature AI classes) |
| Player class structure | Large monolith + StateMachine component | God-class singleton (HeroController) | Just another Creature subclass |
| How hazards work | Entity with kill zone; Player checks `CollideCheck<T>` | `DamageHero` component on hazard calls `TakeDamage()` | `HitByWeapon()` on PhysicalObject |
| State management | Integer-indexed StateMachine | Boolean flags (`cState`) + PlayMaker | Creature state hierarchy |
| Key mitigation | Delegates on Solids, Solid push/carry system | Behavior in FSMs on objects, not in player | Deep inheritance, AI/Graphics/Core split |

The critical insight across all three: **the player exposes capabilities; the world uses them**.

---

## Five interfaces that will carry the entire expansion

SlimeJumping's existing architecture — partial-class state machine, `AddExternalVelocity()` pipeline, GravityField stack, SignalSource events — maps naturally onto an interface-driven design. Define these five core interfaces and nearly every one of the 30+ features slots cleanly into one or more of them.

**On the world-object side** (what objects *are*):

- **`IHazard`** — anything that damages, stuns, or negatively affects the player. Exposes `DamageType`, damage amount, and `OnPlayerContact()`. Spikes, saw blades, fireballs, lightning, blades, cameras-with-stun, snakes, and jump-scare squares all implement this.
- **`IPlatform`** — any surface the player rides. Exposes `GetSurfaceVelocity()` for conveyor-style effects and `IsActive` for toggling. Rotating platforms, pendulums, vines, elevators, conveyor belts, and springy branches implement this.
- **`IInteractable`** — objects activated by player proximity or input. Exposes `Interact()` and `InteractionPrompt`. Notes/signs, puzzle doors, decorative objects (bed, washing machine, gramophone, TV, bonfire, speakers, fireworks), and umbrellas implement this.
- **`IPlayerStateModifier`** — zone-based effects that change player behavior while inside. Exposes `ApplyEffect()` and `RemoveEffect()`. Water, auto-jump zones, ski-jump sections, and passable cloth implement this.
- **`IVelocitySource`** — anything contributing to the external velocity pipeline. Exposes `GetVelocityContribution()`. Wind zones, conveyor belts, water currents, and moving platforms implement this. This directly extends the existing `AddExternalVelocity()` architecture.

**On the player side** (what the player *offers*):

- **`IPlayerDamageable`** — `TakeDamage(float, DamageType)` 
- **`IPlayerVelocityTarget`** — `AddExternalVelocity(Vector2)` (already exists)
- **`IPlayerTeleportable`** — `TeleportTo(Vector2)` (already exists)
- **`IPlayerState`** — `ForceState(StateType)` for objects that need to push the player into specific states (stunned, swimming, launched)

The collision dispatch pattern replaces type-checking with interface queries:

```csharp
// On any collision handler (SurfaceProbe or a dedicated CollisionRouter)
private void OnTriggerEnter2D(Collider2D other) {
    if (other.TryGetComponent<IHazard>(out var hazard))
        hazard.OnPlayerContact(this);
    if (other.TryGetComponent<IPlayerStateModifier>(out var modifier))
        activeModifiers.Add(modifier);
    if (other.TryGetComponent<IVelocitySource>(out var source))
        velocityPipeline.Register(source);
}
```

**Objects can implement multiple interfaces.** An electrified conveyor belt implements both `IHazard` and `IPlatform`. A catapult implements both `IInteractable` and `IVelocitySource`. This combinatorial flexibility is exactly why interfaces outperform inheritance for 30+ diverse types. Adding a new feature means creating a new MonoBehaviour that implements existing interfaces — **zero changes to SlimeController**.

Configure each object with **ScriptableObject data assets** (`HazardData`, `PlatformData`, `InteractableData`) so designers can create variants (fire spike, ice spike, electric spike) by creating new `.asset` files without touching code. The existing GravityField stack pattern generalizes beautifully: formalize it into a `VelocityPipeline` that registers and unregisters `IVelocitySource` components on trigger enter/exit, computing aggregate velocity each `FixedUpdate`.

---

## How all 30 features map to seven architectural categories

Grouping the features reveals that most share underlying systems. Features within the same group reuse identical base components, reducing the actual number of *new systems* needed from 30+ to roughly **seven**.

**Group 1 — Hazards (IHazard implementations, ~8 features).** Spikes-that-bounce, rotating saw blades, fireballs, blades, lightning, cameras-with-stun, snakes, and jump-scare squares. All share `DamageType` discrimination and contact response logic. Saw blades and fireballs add movement patterns (circular path, projectile trajectory). Cameras-with-stun and jump-scare squares add a "stun" `DamageType` that triggers input lockout rather than health damage. Lightning adds periodic activation via coroutine timer. **New system needed: Stun state** in the player FSM (a single new state that temporarily locks input, analogous to Celeste's `StReflectionFall` or Hollow Knight's hazardType routing).

**Group 2 — Platforms and surfaces (IPlatform + IVelocitySource, ~7 features).** Rotating circular platforms, pendulum platforms, vine/curved platforms, elevators, conveyor belts, springy branches, and ski-jump sections. All need to carry the player using one shared mechanism: track platform delta position/velocity each frame, apply it to the player via `AddExternalVelocity()` or direct position offset. Rotating platforms use `Rigidbody2D.MoveRotation()` on a kinematic body. Pendulums use scripted sine motion or `HingeJoint2D`. Conveyor belts add horizontal velocity via `IVelocitySource`. Elevators use `MovePosition()` along waypoints. Springy branches use `SpringJoint2D`. **No new player system needed** — all feed into the existing external velocity pipeline.

**Group 3 — Player state zones (IPlayerStateModifier, ~4 features).** Water/buoyancy, auto-jump points, passable cloth, and rain-with-umbrellas. Water creates a `Swimming` state: trigger zone modifies gravity scale, increases drag, replaces jump with swim-up impulse, tracking submergence depth (0–1) via downward raycast. Auto-jump zones force an immediate state transition to Airborne on contact. Passable cloth is a trigger that allows passage while applying light drag. Rain-with-umbrella is an environmental zone that checks whether the player holds an umbrella item. **New system needed: Swimming state** and a generalized zone-effect stack (extending GravityField stack to handle multiple simultaneous modifiers).

**Group 4 — Physics objects (~4 features).** Pushable boxes, rubber bands/wires, catapults, and rope-like objects. Pushable boxes need a `Rigidbody2D` (Dynamic, rotation-frozen) with manual force transfer since SlimeController uses a custom physics controller. For ropes and rubber bands, **Verlet integration** is strongly recommended over `HingeJoint2D` chains — Verlet is more stable when both ends move, more predictable for gameplay, and easier to tune. The core algorithm stores position and previous-position per node, iterates distance constraints 40–80 times per frame, and renders via `LineRenderer`. Catapults use impulse force (`AddForce` with `ForceMode2D.Impulse`) after resetting velocity for consistent launches. **New system needed: Verlet rope solver** (reusable across rubber bands, wires, vines, and springy branches).

**Group 5 — AI entities (~2 features).** Enemy slime and snakes. Both need a behavior system. Following Rain World's pattern of separating core/graphics/AI into three classes, create: `EnemyBase` (physics, health), `EnemyGraphics` (procedural visual deformation — reuse SlimeJumping's existing deformation system for enemy slimes), and `EnemyAI` (FSM with patrol/chase/attack states). Snakes can reuse the same `EnemyBase` with a different `EnemyAI` subclass. **New system needed: Enemy AI framework** (lightweight FSM, not PlayMaker — use the same IState pattern as the player FSM).

**Group 6 — Interactive objects (IInteractable, ~10 features).** Decorative objects (bed, washing machine, gramophone, TV, bonfire, fireworks, speakers), notes/signs, puzzle-locked doors, and CCTVs. Most of these are simple trigger-animation-sound sequences — player approaches, presses interact, object plays feedback. Notes/signs add a UI overlay (pause input, show text panel, dismiss on input). Puzzle doors add a condition-check system (inventory keys, switch states, or multi-condition AND gates wired via the existing SignalSource/UnityEvent system). CCTVs trigger a camera viewpoint change. **New system needed: InteractionPrompt UI** and a lightweight **puzzle condition evaluator** (which can use existing SignalSource for wiring).

**Group 7 — Presentation systems (~3 features).** Rain World-style camera zones, cutscene transitions, and rain weather. Camera zones use **Cinemachine**: one `CinemachineVirtualCamera` per room zone with `CinemachineConfiner2D` bound to room-bounds colliders. Trigger zones swap VCam priority; `CinemachineBrain` handles blending automatically. For hard Rain World-style cuts, set the blend to "Cut" with an optional screen fade overlay. Cutscene transitions use **Unity Timeline** + `PlayableDirector` with Signal Tracks to disable/enable input at cutscene boundaries. Rain uses a particle system plus an environmental zone modifier. **New system needed: Room camera manager** (thin wrapper around Cinemachine VCam priority switching).

---

## The seven new systems and their build order

Dependencies determine the optimal build sequence. Foundation interfaces must exist before any feature can use them, and some features depend on systems that other features also need.

**Phase 0 — Foundation (1–2 days).** Define the five world-object interfaces and four player interfaces. Create the `CollisionRouter` component that queries interfaces on trigger contacts. Formalize `VelocityPipeline` from the existing `AddExternalVelocity()` pattern. Create base `ScriptableObject` data types (`HazardData`, `PlatformData`, `InteractableData`, `StateModifierData`). This phase touches SlimeController minimally — it just exposes existing capabilities through new interfaces.

**Phase 1 — Hazard system + Stun state (2–3 days).** Build `HazardBase` MonoBehaviour implementing `IHazard`, configured via `HazardData` ScriptableObjects. Add a `Stunned` state to the player FSM (input lockout timer + optional knockback/bounce). Wire DamageType discrimination so bouncing spikes, damaging saws, and stun cameras all use the same `IHazard` pipeline with different response types. This unlocks: spikes, saw blades, fireballs, blades, lightning, cameras-with-stun, jump-scare squares.

**Phase 2 — Platform carrier system (2–3 days).** Build a `PlatformCarrier` component that tracks kinematic Rigidbody2D delta motion and feeds it to riding players via `AddExternalVelocity()`. Implement movement patterns as modular components: `WaypointMover` (elevators), `RotatingMover` (`MoveRotation` in FixedUpdate), `PendulumMover` (sine-wave angle), `ConveyorSurface` (`IVelocitySource` with horizontal velocity). This unlocks: rotating platforms, pendulums, elevators, conveyor belts. Vine/curved platforms are static colliders with `EdgeCollider2D` — no movement system needed.

**Phase 3 — Swimming state + zone modifiers (2–3 days).** Add `Swimming` state to the player FSM. Build `WaterZone` as `IPlayerStateModifier` that pushes the player into Swimming on enter, pops on exit, with configurable buoyancy force, drag, and submergence tracking. Generalize the GravityField stack into a `ModifierStack` that handles multiple simultaneous zone effects. This unlocks: water/buoyancy, auto-jump zones, passable cloth, ski-jump sections.

**Phase 4 — Verlet rope solver (3–4 days).** Build a reusable `VerletRope` component: configurable node count, constraint iterations, gravity, collision against `Physics2D.OverlapCircle`, and `LineRenderer` visualization. Create attachment/grab interface for the player (on grab, constrain player position to nearest rope node; on release, transfer rope velocity to player). This unlocks: rubber bands, telegraph wires, springy branches (shorter ropes with high stiffness), and vine-grab mechanics.

**Phase 5 — Interactable system + puzzle UI (2–3 days).** Build `InteractableBase` implementing `IInteractable` with proximity detection, interaction prompt UI, and UnityEvent hooks for designers. Create `PuzzleCondition` evaluator that checks multiple SignalSource states (AND/OR gates). Build a simple text overlay panel for notes/signs. Wire decorative objects as InteractableBase instances with animation triggers. This unlocks: all decorative objects, notes/signs, puzzle doors, and serves as foundation for CCTV interaction.

**Phase 6 — Enemy AI framework (3–4 days).** Build `EnemyBase`/`EnemyGraphics`/`EnemyAI` separation following Rain World's pattern. Create patrol/chase/attack FSM states. Enemy slime reuses the existing procedural deformation system for visuals. Snake uses a different AI subclass (ground-bound patrol, lunge attack). Both use `IHazard` for player-contact damage. This unlocks: enemy slime, snakes.

**Phase 7 — Presentation systems (2–3 days).** Set up Cinemachine room camera system: one VirtualCamera per room zone, Confiner2D bounds, trigger-based priority switching. Build cutscene trigger using Timeline + PlayableDirector with Signal Track for input locking. Add rain particle system as environmental zone. This unlocks: camera zones, cutscene transitions, rain/weather, CCTV viewpoints.

---

## Specific technical recommendations for the trickiest mechanics

**Water buoyancy** works best as a trigger zone that transitions the player into a `Swimming` state rather than just modifying physics values. Calculate **submergence** (0.0–1.0) by raycasting downward from the player's top against the water surface, then blend between normal and water physics proportionally. This prevents the jarring oscillation at the water surface that plagues simpler implementations. Track water zones with an **integer counter** (increment on enter, decrement on exit) rather than a boolean, to handle overlapping water volumes correctly. Key pitfall: if SlimeController sets velocity directly each frame, `BuoyancyEffector2D` forces get overwritten — use manual buoyancy force addition through the velocity pipeline instead.

**Rope physics via Verlet integration** stores each node's current and previous position; velocity is implicit in the difference. Each FixedUpdate: (1) apply gravity to each node, (2) iterate distance constraints 40–80 times to maintain segment lengths, (3) check each node against colliders via `Physics2D.OverlapCircle`. More iterations = stiffer rope. A **40-node rope with 80 iterations** produces **3,200 collision checks per tick** — use Unity's Burst compiler for ropes that need collision. For rubber bands and springy branches, shorter ropes (8–15 nodes) with very high iteration counts create the right feel. Avoid `HingeJoint2D` chains for anything gameplay-critical — they become unstable and unpredictable when both ends are mobile.

**Moving platform carry mechanics** require that platforms use **`Rigidbody2D.MovePosition()`/`MoveRotation()`** on kinematic bodies (never `transform.position` directly, which bypasses physics interactions). Track the platform's position delta each `FixedUpdate` and feed it into the player's `AddExternalVelocity()` pipeline. For rotating platforms, convert the angular velocity to tangential velocity at the player's contact point. The existing GravityField stack pattern is the right model — platforms register as velocity sources on contact and unregister on separation.

**Room-based camera zones** use Cinemachine's built-in infrastructure. Create one `CinemachineVirtualCamera` per room zone, each with a `CinemachineConfiner2D` extension bound to a `PolygonCollider2D` defining room bounds. Place trigger colliders at room boundaries. When the player enters a new zone trigger, raise that zone's VCam priority and lower the previous one — `CinemachineBrain` handles blending automatically. For Rain World's hard-cut style, set the default blend to "Cut" with an optional full-screen fade overlay. For smoother transitions, use "EaseInOut" with **0.5–1 second** duration. This requires no custom camera code.

**Stun mechanics** map cleanly onto the existing state machine. Add a `Stunned` state that locks input for a configurable duration, applies optional knockback velocity, decays velocity via damping (multiply by **0.95** each frame), and transitions back to the appropriate grounded/airborne state when the timer expires. For screen flash effects, overlay a full-screen `UI.Image` (white) with `CanvasGroup` alpha lerped from 1 to 0 over 0.2–0.3 seconds. For screen shake, use `CinemachineImpulseSource.GenerateImpulse()` which integrates cleanly with the room camera system. Hitstop (brief freeze) uses `Time.timeScale = 0f` for 2–3 frames, resumed via `WaitForSecondsRealtime`.

---

## Conclusion

SlimeJumping's existing architecture — partial-class state machine, external velocity pipeline, gravity field stack, signal/event system — provides a surprisingly strong foundation for the expansion. The codebase already embodies two of the three core patterns that successful indie platformers use (velocity aggregation pipeline and event-driven object communication). What it's missing is the third: **responsibility inversion via interfaces**.

The highest-leverage single change is defining the five world-object interfaces and four player interfaces, then building a `CollisionRouter` that dispatches via `TryGetComponent<IInterface>()`. This one structural change makes every subsequent feature additive rather than invasive — each new object type is a new MonoBehaviour implementing existing interfaces, with zero modifications to SlimeController. The 30+ features reduce to seven actual systems, buildable in roughly seven phases over 3–4 weeks, with each phase unlocking multiple features simultaneously.

The key architectural insight from studying Celeste, Hollow Knight, and Rain World is pragmatic: **a somewhat large player class is acceptable** (both Celeste and Hollow Knight ship with monolithic player classes) **as long as interaction response logic lives on the world objects, not on the player**. Don't split SlimeController into 15 tiny components for theoretical purity. Instead, keep it as the authoritative source for player state and capabilities, expose those capabilities through narrow interfaces, and let the growing ecosystem of world objects use those interfaces autonomously.