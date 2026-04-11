# Scalable architecture for SlimeJumping: 30 features without the god object

**Your existing partial-class FSM + event-driven visuals + ScriptableObject materials form a solid foundation that needs extension, not replacement.** The architecture should layer three key patterns on top: (1) an `IExternalController` override system to handle forced states without bloating the SlimeController, (2) composition-based platform/hazard/interactable systems using interfaces and ScriptableObject configs, and (3) ScriptableObject event channels for cross-system communication. This recommendation draws from Celeste's open-source player architecture, Unity's Open Project 1 SO event system, Ryan Hipple's Unite 2017 patterns, and Robert Nystrom's *Game Programming Patterns*. Every pattern below was chosen because it solves a specific scaling problem in your codebase — nothing is included for architectural purity alone.

---

## The right cuts: a taxonomy of 30 features into 8 subsystems

Your rough categories are close but need sharper boundaries. The natural subsystems emerge from *how each feature interacts with the SlimeController*, not from thematic similarity. Here is the proposed taxonomy with the governing interface or base class for each:

**1. Surface effects** (`IPlatformEffect` components composed on platform GameObjects)
Features: Conveyor belt (#13), Jump pad (#21), Ski jump (#24), Springy tree branch (#18), Catapult (#17), Rubber band rope (#4), Curved platforms (#8), Rotating falling platform (#6), Swinging platform (#7)

**2. Hazards** (`IHazardSource` carrying a `HazardEffect` data struct)
Features: Spikes (#2), Saw blade (#3), Knife (#12), Fireball/explosion (#9), Lightning (#20)

**3. Environmental zones** (`EnvironmentalZone` abstract MonoBehaviour + `ZoneConfig` ScriptableObject)
Features: Water (#1), Rain zone (#30), Pass-through cloth (#16) — cloth is a zone because it modifies trajectory without attachment

**4. Interactables** (`IInteractable` interface + `InteractableBase` MonoBehaviour)
Features: Elevator (#11), Pushable box (#14), Bed/bench/washing machine/etc (#15), Puzzle door (#22), Memo/note (#23)

**5. AI agents** (FSM-driven MonoBehaviours implementing `IExternalController` for swallow sequences)
Features: Big slime enemy (#5), Snake (#27)

**6. Pickups** (`ICollectible` interface)
Features: Auto-jump/dash collectible (#10)

**7. Camera regions** (Cinemachine VCam activation via trigger volumes)
Features: Rain World camera transitions (#28), Photo camera (#25), CCTV (#26), Jump-scare trigger (#19)

**8. Cinematics** (Timeline + `IExternalController` for player override)
Features: Knockdown/wake-up intro (#29)

The key insight is that features #4 (rubber band rope), #6 (rotating falling platform), #7 (swinging platform), #8 (curved platforms), #17 (catapult), #18 (springy branch) are all *surface effects* — they modify what happens when the slime attaches or launches, and they compose naturally with your existing `SurfaceMaterial` system. They are **not** separate subsystems; they're `IPlatformEffect` components on platform GameObjects that carry `SurfaceMaterial` references. Meanwhile, water (#1) belongs in environmental zones rather than surfaces because buoyancy is a volume effect, not a contact-surface effect, and the slime's interaction model inside water differs fundamentally from surface attachment.

---

## Folder and namespace structure

```
Assets/
├── _Game/
│   ├── Scripts/
│   │   ├── Core/
│   │   │   ├── SlimeController.cs                 // State machine, ChangeState(), override logic
│   │   │   ├── SlimeController.Attached.cs        // Partial: attached state
│   │   │   ├── SlimeController.Charging.cs        // Partial: charging state
│   │   │   ├── SlimeController.Airborne.cs        // Partial: airborne state
│   │   │   ├── SlimeController.Stunned.cs         // Partial: parameterized transient stun
│   │   │   ├── SlimeInput.cs                      // Input reading, exposes events
│   │   │   ├── SlimePhysics.cs                    // Rigidbody2D wrapper, knockback, velocity
│   │   │   ├── SlimeHealth.cs                     // IDamageable implementation
│   │   │   ├── SlimeVisuals.cs                    // Sprite, particles, event-driven reactions
│   │   │   ├── SlimeAudio.cs                      // SFX, event-driven
│   │   │   └── SurfaceProbe.cs                    // Existing depenetration system
│   │   │
│   │   ├── Interfaces/
│   │   │   ├── IDamageable.cs
│   │   │   ├── IHazardSource.cs
│   │   │   ├── IPlatformEffect.cs
│   │   │   ├── IInteractable.cs
│   │   │   ├── ICollectible.cs
│   │   │   └── IExternalController.cs
│   │   │
│   │   ├── Surfaces/
│   │   │   ├── SurfaceMaterial.cs                 // Existing SO
│   │   │   ├── PlatformBase.cs                    // IPlatformEffect dispatcher
│   │   │   ├── Effects/
│   │   │   │   ├── ConveyorEffect.cs
│   │   │   │   ├── JumpPadEffect.cs
│   │   │   │   ├── CatapultEffect.cs
│   │   │   │   ├── SpringyBranchEffect.cs
│   │   │   │   └── SkiJumpEffect.cs
│   │   │   └── Motion/
│   │   │       ├── PlatformMotor.cs               // Existing
│   │   │       ├── WaypointPlatform.cs            // Existing
│   │   │       ├── RotatingPlatform.cs            // Existing
│   │   │       ├── TiltPlatform.cs                // Existing
│   │   │       ├── CrumblingPlatform.cs           // Existing
│   │   │       ├── SwingingPlatform.cs            // New: pendulum via HingeJoint2D
│   │   │       └── RotatingFallingPlatform.cs     // New: Pogostuck tilt variant
│   │   │
│   │   ├── Hazards/
│   │   │   ├── HazardEffect.cs                    // Data struct
│   │   │   ├── Hazard.cs                          // Base MonoBehaviour, trigger detection
│   │   │   ├── SpikesHazard.cs                    // Config: bounce-away + redirect
│   │   │   ├── SawBladeHazard.cs                  // Config: rotating, pass-through stun
│   │   │   ├── KnifeHazard.cs                     // Config: attachable sides, split logic
│   │   │   ├── ProjectileHazard.cs                // Fireball, moves + applies effect
│   │   │   └── LightningHazard.cs                 // Telegraph + instant strike
│   │   │
│   │   ├── Zones/
│   │   │   ├── ZoneConfig.cs                      // ScriptableObject
│   │   │   ├── EnvironmentalZone.cs               // Abstract base
│   │   │   ├── ZoneEffectAggregator.cs            // Player-side, handles overlap
│   │   │   ├── WindZone2D.cs                      // Existing, refactored
│   │   │   ├── DragZone2D.cs                      // Existing, refactored
│   │   │   ├── GravityZone2D.cs                   // Existing, refactored
│   │   │   ├── WaterZone.cs                       // Buoyancy + surface deformation
│   │   │   ├── RainZone.cs                        // Jump/movement modifier + visual
│   │   │   └── Portal.cs                          // Existing
│   │   │
│   │   ├── Interactables/
│   │   │   ├── InteractableBase.cs                // Abstract MonoBehaviour
│   │   │   ├── InteractionDetector.cs             // Player-side proximity scanner
│   │   │   ├── ElevatorInteractable.cs
│   │   │   ├── PushableBox.cs
│   │   │   ├── SeatInteractable.cs                // Bench, bed (uses IExternalController)
│   │   │   ├── MediaInteractable.cs               // TV, gramophone, speaker
│   │   │   ├── PuzzleDoorInteractable.cs          // Opens overlay (uses IExternalController)
│   │   │   ├── MemoInteractable.cs                // Proximity-triggered text
│   │   │   ├── ShrineInteractable.cs
│   │   │   └── FireworkInteractable.cs
│   │   │
│   │   ├── AI/
│   │   │   ├── SurfaceFollower.cs                 // Reusable component
│   │   │   ├── BigSlimeAI.cs                      // FSM: Patrol, Chase, Attack, Stunned
│   │   │   ├── BigSlimeSwallowController.cs       // IExternalController
│   │   │   ├── SnakeAI.cs                         // FSM: Lurk, Detect, Lunge, Swallow
│   │   │   └── SnakeSwallowController.cs          // IExternalController
│   │   │
│   │   ├── Camera/
│   │   │   ├── CameraRegionTrigger.cs             // Activates/deactivates VCams
│   │   │   ├── PhotoCameraEffect.cs               // Flash + stun + screenshot
│   │   │   ├── CCTVTracker.cs                     // Visual tracking, ceiling-mounted
│   │   │   ├── JumpScareTrigger.cs                // One-shot animation region
│   │   │   └── CinematicIntroController.cs        // IExternalController for knockdown/wake
│   │   │
│   │   ├── Pickups/
│   │   │   └── AirDashPickup.cs                   // ICollectible, grants air action
│   │   │
│   │   ├── Physics/
│   │   │   ├── VerletRope.cs                      // Rubber band, power line
│   │   │   ├── Water2DSurface.cs                  // Spring column mesh deformation
│   │   │   ├── Cloth2D.cs                         // Pass-through cloth simulation
│   │   │   └── SpringBranch.cs                    // HingeJoint2D-based bending
│   │   │
│   │   ├── Events/
│   │   │   ├── VoidEventChannel.cs                // SO event channel (no payload)
│   │   │   ├── FloatEventChannel.cs
│   │   │   ├── IntEventChannel.cs
│   │   │   ├── Vector2EventChannel.cs
│   │   │   ├── HazardEventChannel.cs              // Payload: HazardEffect
│   │   │   └── StateChangeEventChannel.cs         // Payload: old state, new state
│   │   │
│   │   └── Data/
│   │       ├── FloatVariable.cs                   // Shared runtime variable SO
│   │       ├── IntVariable.cs
│   │       └── BoolVariable.cs
│   │
│   ├── ScriptableObjects/
│   │   ├── SurfaceMaterials/                      // Existing: per-surface configs
│   │   ├── ZoneConfigs/                           // Wind, Drag, Gravity, Water, Rain
│   │   ├── HazardConfigs/                         // Per-hazard-type HazardEffect presets
│   │   ├── PlatformEffectConfigs/                 // JumpPad, Conveyor, Catapult tuning
│   │   ├── AIConfigs/                             // BigSlime stats, Snake timings
│   │   ├── CameraConfigs/                         // Per-region blend settings
│   │   └── EventChannels/                         // All SO event channel assets
│   │
│   └── Prefabs/
│       ├── Platforms/
│       ├── Hazards/
│       ├── Zones/
│       ├── Interactables/
│       ├── AI/
│       └── VFX/
```

---

## Interface contracts with method signatures

These interfaces define the boundaries between subsystems. Each is deliberately minimal — one responsibility, no bloat.

```csharp
// === CORE OVERRIDE SYSTEM ===
public interface IExternalController
{
    void OnTakeControl(SlimeController player);
    void UpdateControl(SlimeController player);
    void FixedUpdateControl(SlimeController player);
    void OnReleaseControl(SlimeController player);
}

// === HAZARD SYSTEM ===
[System.Serializable]
public struct HazardEffect
{
    public float damage;
    public DamageType damageType;
    public Vector2 knockbackDirection;   // (0,0) = computed from relative position
    public float knockbackForce;
    public float stunDuration;           // 0 = no stun, just knockback
    public bool killsInstantly;
}

public enum DamageType { Physical, Fire, Electric, Sharp }

public interface IDamageable
{
    bool IsInvulnerable { get; }
    void ApplyHazardEffect(HazardEffect effect, Vector2 sourcePosition);
}

public interface IHazardSource
{
    HazardEffect GetEffect();
    bool IsActive { get; }
}

// === PLATFORM SYSTEM ===
public interface IPlatformEffect
{
    void OnSlimeLand(SlimeController slime, ContactPoint2D contact);
    void OnSlimeStay(SlimeController slime);
    void OnSlimeLeave(SlimeController slime);
}

// === INTERACTION SYSTEM ===
public interface IInteractable
{
    string Prompt { get; }
    int Priority { get; }
    bool CanInteract(GameObject interactor);
    void Interact(GameObject interactor);
    void OnFocusEnter();
    void OnFocusExit();
}

// === COLLECTIBLE SYSTEM ===
public interface ICollectible
{
    void Collect(SlimeController collector);
}

// === ENVIRONMENTAL ZONE ===
// Not an interface — abstract MonoBehaviour base because zones share
// significant implementation (trigger tracking, FixedUpdate loop)
public abstract class EnvironmentalZone : MonoBehaviour
{
    [SerializeField] protected ZoneConfig config;
    protected HashSet<Rigidbody2D> affectedBodies = new();

    protected virtual void OnTriggerEnter2D(Collider2D col) { ... }
    protected virtual void OnTriggerExit2D(Collider2D col) { ... }
    protected virtual void FixedUpdate() { foreach body: ApplyEffect(rb); }
    protected abstract void ApplyEffect(Rigidbody2D rb);
    protected virtual void OnBodyEntered(Rigidbody2D rb) { }
    protected virtual void OnBodyExited(Rigidbody2D rb) { }
}
```

**Why interfaces for some, abstract classes for others:** `IPlatformEffect`, `IInteractable`, and `IDamageable` are interfaces because a single MonoBehaviour might implement multiple contracts (a knife is both `IHazardSource` and `IPlatformEffect` — attachable sides but damaging blade). `EnvironmentalZone` is an abstract class because all zones share substantial implementation (trigger tracking, the FixedUpdate loop, body set management) and no zone needs to simultaneously be something else.

---

## Event and messaging architecture

Use a **two-tier system**: C# `Action` delegates for intra-system communication within the player stack, and ScriptableObject event channels for cross-system broadcasts.

**Tier 1 — C# events (intra-system, high-frequency, zero GC):**
```csharp
// Inside SlimeController — consumed by SlimeVisuals, SlimeAudio on the same GameObject
public event Action<SlimeState, SlimeState> OnStateChanged;        // old, new
public event Action<HazardEffect> OnDamageReceived;
public event Action<float> OnChargeProgressChanged;                 // 0..1
public event Action<Vector2> OnLaunched;                            // launch velocity
public event Action OnDeath;
```

These events are subscribed to in `Awake()`/`OnEnable()` by sibling components. They never cross scene boundaries or system boundaries. **Performance**: C# delegate invocation produces zero GC allocation and is roughly 10× faster than UnityEvent invocation.

**Tier 2 — ScriptableObject event channels (cross-system, decoupled):**
```csharp
[CreateAssetMenu(menuName = "Events/Void Channel")]
public class VoidEventChannel : ScriptableObject
{
    private readonly HashSet<Action> _listeners = new();
    public void Raise() { foreach (var l in _listeners) l?.Invoke(); }
    public void Subscribe(Action listener) => _listeners.Add(listener);
    public void Unsubscribe(Action listener) => _listeners.Remove(listener);
}

[CreateAssetMenu(menuName = "Events/Typed Channel")]
public abstract class EventChannel<T> : ScriptableObject
{
    private readonly HashSet<Action<T>> _listeners = new();
    public void Raise(T value) { foreach (var l in _listeners) l?.Invoke(value); }
    public void Subscribe(Action<T> listener) => _listeners.Add(listener);
    public void Unsubscribe(Action<T> listener) => _listeners.Remove(listener);
}
```

Create concrete channels: `IntEventChannel`, `FloatEventChannel`, `Vector2EventChannel`, `HazardEventChannel`. These live as assets in `ScriptableObjects/EventChannels/`. Example usage: `OnCheckpointReached` channel — the checkpoint trigger raises it, the camera system listens to it, the save system listens to it, the UI listens to it. None of these systems reference each other.

**Why not a singleton event bus or static EventManager:** A static event bus creates a single coupling point that everything depends on. SO event channels are *granular* — each channel is a named, inspectable asset. You can select a channel in the editor and see exactly who listens. When you delete a feature, you delete its channels. The Unity Open Project 1 team (Unity's own reference architecture) chose this pattern explicitly to avoid singletons entirely.

**Why not UnityEvents everywhere:** UnityEvents allocate 136 bytes on first invocation and are ~10× slower than C# delegates on dispatch. They're valuable only where you need Inspector wiring for non-programmer designers. Your visual/audio subscription system already uses C# events — keep it.

**Trade-off acknowledged:** SO event channels add asset management overhead. For a solo/small-team project, the overhead is modest (one asset per channel, maybe 15–20 channels total). The decoupling benefit outweighs this cost once you exceed ~10 cross-system communication paths, which you will with 30 features.

---

## How to handle forced player states without bloating the controller

This is the most architecturally critical decision. The answer: **add exactly one transient state (Stunned) to the FSM, and handle everything else via `IExternalController` override.**

**The override controller pattern:**
```csharp
public partial class SlimeController
{
    private IExternalController _override;

    public bool IsOverridden => _override != null;

    public void SetOverride(IExternalController controller)
    {
        if (_override != null) return; // Already overridden — reject
        _currentState?.Exit();
        _override = controller;
        _override.OnTakeControl(this);
    }

    public void ReleaseOverride(IExternalController controller)
    {
        if (_override != controller) return; // Not the current owner
        _override.OnReleaseControl(this);
        _override = null;
        ChangeState(SlimeState.Airborne); // Safe default
    }

    void Update()
    {
        if (_override != null) { _override.UpdateControl(this); return; }
        _currentState?.Update();
    }
}
```

**Decision matrix for each forced-state scenario:**

| Scenario | Mechanism | Why |
|----------|-----------|-----|
| Stunned by hazard | `SlimeState.Stunned` (parameterized, timed, returns to Airborne) | Short-lived, physics-driven, needs the FSM's velocity/gravity handling |
| Swallowed by snake/slime | `IExternalController` on the enemy | Enemy owns the player for an extended sequence with custom animation |
| Sitting on bench/bed | `IExternalController` on the interactable | Player surrenders control, custom pose, W to release |
| In puzzle overlay | `IExternalController` on the puzzle system | Input redirected to puzzle, player frozen |
| Cinematic intro | `IExternalController` on the cinematic controller | Timeline-driven sequence, player has no agency |
| Riding elastic rope | NOT an override — regular `Attached` state with rope's `SurfaceMaterial` | The slime is attached to a surface; the rope's physics properties handle the rest |
| On jump pad | NOT an override — `Airborne` state after force applied | Jump pad applies impulse in `OnSlimeLand`, slime is immediately airborne |

**Why Stunned belongs in the FSM but Swallowed doesn't:** Stunned is a *brief interruption* (0.2–1.0s) where the slime still exists in physics space, still has gravity and velocity, and returns automatically to its prior locomotion context. It shares 80% of its logic with Airborne (apply gravity, check for surface contact). Making it an override controller would require duplicating all that physics code. Swallowed, by contrast, teleports the slime inside an enemy, plays a custom animation sequence, and has completely different rules — no gravity, no input, no collision. The enemy should own that behavior entirely.

**Celeste validates this split.** The Celeste developers explicitly noted that their `StDummy` and `StIntroWalk` states "should be an entirely different entity" rather than player states. Your `IExternalController` pattern is exactly that principle — external systems spawn a controller that puppets the player, rather than the player knowing how to be puppeted by every possible system.

---

## Environmental zone architecture: unify wind, drag, gravity, water, and rain

Refactor your existing `DragZone`, wind, and gravity fields into the `EnvironmentalZone` hierarchy. The unifying abstraction is a **trigger volume that modifies physics properties of bodies inside it**, configured via a `ZoneConfig` ScriptableObject.

```csharp
[CreateAssetMenu(menuName = "Zones/Zone Config")]
public class ZoneConfig : ScriptableObject
{
    public Vector2 forceDirection = Vector2.up;
    public float forceMagnitude = 10f;
    public float dragMultiplier = 1f;
    public float gravityScaleOverride = -1f;  // -1 = don't override
    public bool isAdditive = true;             // false = override mode
    public int priority = 0;                   // higher wins in override conflicts
}
```

**Zone overlap is handled by a `ZoneEffectAggregator`** on the player. When the slime enters a zone, the zone registers with the aggregator. The aggregator processes all active zones each FixedUpdate: **additive** effects (wind force, drag multipliers) stack; **override** effects (gravity scale) use priority — highest wins. This prevents the "last zone entered wins" bug that plagues naive implementations.

Water deserves special attention. `WaterZone` extends `EnvironmentalZone` with additional responsibilities: a spring-column surface mesh for visual deformation (Hooke's law per vertex column, wave propagation to neighbors), splash particle emission on entry, and a state signal to `SlimeController` enabling water-specific charging behavior. The buoyancy force itself is just an upward force proportional to submersion depth — standard `EnvironmentalZone` behavior. You can optionally use Unity's built-in `BuoyancyEffector2D` for the physics and layer your custom spring-mesh renderer on top.

Rain zones (#30) fit cleanly here: a `RainZone` applies a downward force and drag modifier (affecting jump height and movement speed), while a separate `RainVisualController` handles particle effects and checks for overhang occlusion via raycasts. The visual controller subscribes to the zone's enter/exit events. This separation means the rain visual system can run independently — rain particles render everywhere, but the *gameplay effect* only applies inside zone bounds.

---

## Camera system: Cinemachine Brain with priority-driven virtual cameras

Your camera needs are: default follow-cam, Rain World–style fixed regions, photo camera flash/stun, CCTV tracking, cinematic intro, and jump-scare framing. **All of these map to Cinemachine's priority system without custom camera code.**

**Architecture:**

- **One `CinemachineBrain`** on the main camera. Default blend: EaseInOut 0.5s.
- **Follow VCam** (Priority 10): Framing Transposer following the slime. This is your default.
- **Region VCams** (Priority 15 each): One per fixed-camera region. Each has a `CinemachineConfiner2D` extension with a `PolygonCollider2D` matching the room bounds. Activated/deactivated by `CameraRegionTrigger` MonoBehaviours on trigger volumes.
- **Cinematic VCam** (Priority 20): Used for intro sequences, activated by the `CinematicIntroController`.
- **Photo/JumpScare VCam** (Priority 25): Brief, high-priority override for flash or scare effects.

**Region transitions (Rain World style):**
```csharp
[RequireComponent(typeof(Collider2D))]
public class CameraRegionTrigger : MonoBehaviour
{
    [SerializeField] private CinemachineVirtualCamera regionCam;

    void OnTriggerEnter2D(Collider2D col)
    {
        if (col.CompareTag("Player"))
            regionCam.gameObject.SetActive(true);
    }

    void OnTriggerExit2D(Collider2D col)
    {
        if (col.CompareTag("Player"))
            regionCam.gameObject.SetActive(false);
    }
}
```

When the slime enters a region trigger, that region's VCam activates (Priority 15 > Follow's 10), and the Brain blends to it. When the slime exits, the VCam deactivates, and the Brain blends back to the follow cam. **No custom blending code needed** — configure per-transition blends via a `CinemachineBlenderSettings` asset assigned to the Brain's `CustomBlends` field.

**Key technical note from Cinemachine docs:** "It is much more efficient to have more Cinemachine Cameras with different input bounding shapes and blend between them instead of changing one Confiner2D's input bounding shape." This means: one VCam per room, not one VCam with a swapping confiner. The Confiner2D's internal polygon computation is expensive and should not be repeated at runtime.

The photo camera (#25) is a special case: it's not a camera *system* change but a hazard-zone hybrid. When the slime enters the photo region, a flash VFX plays, the slime receives a `HazardEffect` (stun, zero damage), and a `RenderTexture` snapshot is captured. The snapshot can be stored and displayed on the map screen later. The brief high-priority VCam creates the "flash freeze" framing effect.

---

## Platform composition: effects as components, not inheritance

New platform types should be **`IPlatformEffect` components composed on platform GameObjects**, not subclasses of a `PlatformBase`. The `PlatformBase` itself is a thin dispatcher:

```csharp
[RequireComponent(typeof(Collider2D))]
public class PlatformBase : MonoBehaviour
{
    private IPlatformEffect[] _effects;

    void Awake() => _effects = GetComponents<IPlatformEffect>();

    void OnCollisionEnter2D(Collision2D col)
    {
        if (col.gameObject.TryGetComponent<SlimeController>(out var slime))
            foreach (var e in _effects)
                e.OnSlimeLand(slime, col.GetContact(0));
    }
    // OnCollisionStay2D, OnCollisionExit2D follow the same pattern
}
```

Each effect is a single-responsibility MonoBehaviour. A conveyor belt has `PlatformBase` + `ConveyorEffect` + a `SurfaceMaterial` SO reference. A crumbling conveyor adds `CrumblingEffect`. No `CrumblingConveyorPlatform` subclass needed. Level designers compose behaviors by dragging components in the Inspector.

**How this integrates with `SurfaceMaterial`:** Your existing `SurfaceMaterial` ScriptableObject handles per-surface physics properties (friction, bounce, stick behavior). `IPlatformEffect` components handle *active behaviors* (applying forces, triggering timers, launching). The distinction: `SurfaceMaterial` is passive data consumed by `SurfaceProbe` during attachment; `IPlatformEffect` is active logic triggered by contact events. A jump pad has both: a `SurfaceMaterial` with `canAttach = false` and a `JumpPadEffect` that applies upward impulse.

For the rope/rubber band (#4), the platform is a `VerletRope` component (custom Verlet integration, **not** Unity joints — Spring/Hinge joints are unreliable when chained) with an `IPlatformEffect` implementation that reports the local segment's velocity to the slime on launch, providing elastic recoil. **Verlet integration is strongly recommended over Unity joints** for ropes based on broad community consensus: joints are buggy when chained, offer poor control, and have performance issues since each joint is a separate physics solver constraint. A 40-node rope with 80 constraint iterations costs ~3,200 operations per FixedUpdate — acceptable for 3–5 ropes; use Unity Burst/Jobs if you need more.

For springy tree branches (#18), a `HingeJoint2D` with angular limits and a spring motor is the right tool. The branch is a separate `Rigidbody2D` connected to the trunk. When the slime lands, weight naturally bends it via physics; when the slime launches, the branch springs back and optionally adds a boost via `SpringyBranchEffect.OnSlimeLeave()`. This is simpler than Verlet and integrates natively with Unity's solver.

---

## Hazard system: one data struct, one response pathway

Every hazard produces a `HazardEffect` struct. The slime has one method: `ApplyHazardEffect(HazardEffect, Vector2)`. No new player states per hazard type.

**How spikes with redirect (#2) work:** The `SpikesHazard` subclass overrides `GetEffect()` to set `knockbackDirection` to a preconfigured redirect vector (pointing toward specific coordinates). The slime's `ApplyHazardEffect` applies this as a velocity impulse. From the slime's perspective, it's identical to any other knockback — the spike decided the direction, not the player.

**How the knife (#12) "splits slime" on high-energy impact:** The `KnifeHazard` checks `collision.relativeVelocity.magnitude` against a threshold. Below the threshold, it behaves as an attachable surface (blade sides implement `IPlatformEffect`). Above it, it emits a `HazardEffect` with a split visual trigger. The "split" is purely visual (particle effect, temporary sprite deformation) unless you want gameplay consequences — in which case, `HazardEffect` carries a `splitForce` field that `SlimeVisuals` reads to play the deformation.

**Lightning (#20)** works differently from contact hazards: it's a **projectile hazard** with a telegraph phase. `LightningHazard` shows a warning indicator (line renderer, ground marker), waits a configurable delay, then instantiates a trigger collider at the strike position for one physics frame. Anything `IDamageable` inside that trigger receives the `HazardEffect`. This pattern — telegraph → brief trigger → cleanup — handles all delayed/area hazards.

---

## Interaction pipeline: proximity scan, priority, overlay control

All "press W to interact" objects implement `IInteractable`. An `InteractionDetector` component on the slime scans for nearby interactables each frame using `Physics2D.OverlapCircleAll`, selects the highest-priority candidate (scoring: `priority × 100 − distance`), shows its prompt, and routes W-press to `Interact()`.

**Interactions that take control of the player** (bench sitting, puzzle overlays) do so through the `IExternalController` override system. The interactable spawns a controller, calls `slime.SetOverride(controller)`, and the controller handles the interaction sequence. When the player presses W again to stand up (or completes the puzzle), the controller calls `slime.ReleaseOverride(this)`. This keeps all interaction logic *in the interactable*, not in the player.

**Puzzle door (#22)** overlays are the most complex interactable. The `PuzzleDoorInteractable.Interact()` instantiates a puzzle prefab (Sokoban grid, block puzzle, code lock — each is a self-contained UI prefab with its own logic), takes control of the player via override, and redirects input to the puzzle's own input handler. On puzzle completion, the overlay fires a `VoidEventChannel` that the door listens to (opening the door), and releases the player override. The puzzle prefabs are fully decoupled from the interaction system — they just expose an `OnComplete` event.

**Memo/note (#23)** does not use an override. It's an `InteractionType.Instant` interactable: `Interact()` calls a UI manager to display text, and the player retains control. Alternatively, proximity-triggered notes skip the W-press entirely — use `OnFocusEnter`/`OnFocusExit` to show/hide the text panel.

---

## AI agents: finite state machines, not behavior trees

Both the Big Slime (#5) and Snake (#27) have **fewer than 5 distinct behaviors**. Behavior trees are overkill and add unnecessary complexity. Use simple FSMs with enum states and switch-based transitions.

The Big Slime's architecture: `BigSlimeAI` (MonoBehaviour with FSM: `Patrol → Chase → Attack → Stunned`) plus a `SurfaceFollower` component (reusable — raycast along current surface normal, project movement onto surface tangent, handle edge wrapping). The swallow behavior uses `IExternalController`: when the Big Slime's attack succeeds, it calls `player.SetOverride(swallowController)`, plays the swallow/eject animation sequence, then releases.

The Snake has a similar FSM: `Lurk → Detect → Lunge → Swallow → Digest → Release`. The `SnakeSwallowController` implements `IExternalController` and handles the body-traversal animation — moving the player transform along a spline inside the snake mesh.

**The `SurfaceFollower` component is shared between Big Slime AI and potentially any future surface-walking enemy.** It raycasts downward relative to current orientation, snaps to the hit surface, and moves along the tangent. This is extracted as a component rather than inherited because composition lets you attach it to any AI without an inheritance chain.

**When to upgrade to behavior trees:** If you later add bosses with 8+ interruptible behaviors, priority-based decision making, and complex multi-step sequences with fallback alternatives. Until then, FSMs with the `IExternalController` pattern handle your needs with far less overhead. If you do upgrade, the **UnityHFSM** open-source library (hierarchical FSM) or **NodeCanvas** (paid, visual BT editor) are the recommended options.

---

## ScriptableObject organization for data-driven tuning

With 30 features, every tunable parameter must be exposed without code changes. Extend the `SurfaceMaterial` pattern to all subsystems:

```
ScriptableObjects/
├── SurfaceMaterials/
│   ├── SM_Default.asset          // Existing
│   ├── SM_Ice.asset
│   ├── SM_Rubber.asset
│   └── SM_Rope.asset
├── ZoneConfigs/
│   ├── ZC_Wind_Light.asset       // forceDir=(1,0), magnitude=5
│   ├── ZC_Wind_Strong.asset      // forceDir=(1,0), magnitude=15
│   ├── ZC_Water_Shallow.asset    // buoyancy=8, drag=3
│   ├── ZC_Water_Deep.asset       // buoyancy=12, drag=5
│   ├── ZC_GravityReverse.asset   // gravityScale=-1
│   └── ZC_Rain_Heavy.asset       // downForce=3, drag=1.5
├── HazardConfigs/
│   ├── HC_Spikes_Normal.asset    // knockback=800, stun=0.3
│   ├── HC_Spikes_Redirect.asset  // knockback=1200, dir=(0.5,0.8)
│   ├── HC_Saw_Fast.asset         // damage=1, stun=0.5, passThrough=true
│   ├── HC_Lightning.asset        // damage=2, stun=1.0, telegraph=1.5s
│   └── HC_Fireball.asset         // damage=1, knockback=600, fire
├── PlatformEffectConfigs/
│   ├── PE_JumpPad_Low.asset      // force=800, overrideVelocity=true
│   ├── PE_JumpPad_High.asset     // force=1500
│   ├── PE_Conveyor_Slow.asset    // speed=3, dir=(1,0)
│   ├── PE_Conveyor_Fast.asset    // speed=8, dir=(1,0)
│   └── PE_Catapult_45deg.asset   // force=2000, angle=45, chargeTime=0.5
├── AIConfigs/
│   ├── AI_BigSlime.asset         // speed=2, detectionRadius=8, attackRadius=2
│   └── AI_Snake.asset            // lurkTime=3, lungeSpeed=15, digestTime=5
├── CameraConfigs/
│   └── (per-region blend settings stored in CinemachineBlenderSettings assets)
└── EventChannels/
    ├── EC_OnPlayerDeath.asset
    ├── EC_OnCheckpointReached.asset
    ├── EC_OnPuzzleCompleted.asset
    └── EC_OnCameraRegionChanged.asset
```

**The `FloatVariable` pattern** (from Ryan Hipple's talk) should be used for shared runtime state that multiple systems read. Create `FloatVariable` assets for player health, charge progress, current speed — the UI reads these directly without any reference to `SlimeController`. The `FloatReference` wrapper (toggle between constant and SO variable in the Inspector) lets you hardcode values during prototyping and switch to shared variables when you need cross-system reads.

---

## Soft-body physics: choose the right tool per object

| Object | Technique | Why |
|--------|-----------|-----|
| Rope / rubber band / power line | Custom Verlet integration | Stable, performant, full control. Unity joints are buggy when chained. |
| Water surface | Spring column model (Hooke's law per vertex, wave propagation) | Standard 2D water technique. Layer on BuoyancyEffector2D for physics. |
| Pass-through cloth | 2D mass-spring grid (structural + shear + bend springs) | Same principle as rope, extended to 2D grid. Keep grids small (<15×15). |
| Springy tree branch | HingeJoint2D with spring motor + angle limits | Simple, integrates natively with Unity physics. No custom solver needed. |
| Big Slime soft-body | Sprite deformation via shader (vertex displacement in Shader Graph) | Gameplay-irrelevant cosmetic effect. Don't simulate physics for this. |
| Swinging platform | HingeJoint2D as pivot point | Single-joint pendulum is stable and integrates with Unity's solver. |

**Do not build a unified spring-mass solver.** The systems have different dimensionalities (1D rope, 2D cloth, 0D pivot), different performance profiles, and different collision needs. A shared solver would be over-engineered. Each system uses the simplest technique that achieves the visual/gameplay goal.

---

## Extending the existing event-based visual pipeline

Your existing pattern — visuals/audio subscribe to controller events — is correct and should be extended, not replaced. The SO event channel layer adds cross-system communication on top without changing how `SlimeVisuals` works.

**Concrete extension path:**
1. `SlimeVisuals` and `SlimeAudio` continue subscribing to `SlimeController`'s C# events (same GameObject, direct reference, zero overhead).
2. `SlimeController` *also* raises SO event channels for cross-system needs: `OnPlayerDeath` channel notifies the camera to shake, the UI to show death screen, and the checkpoint system to record. These systems subscribe to the SO channel, not to the controller directly.
3. New features add their own C# events internally (e.g., `WaterZone.OnSplash` → `WaterVisuals` subscribes) and raise SO channels when cross-system notification is needed (e.g., `OnPuzzleCompleted` channel → door opens + score updates).

**Don't migrate existing C# events to SO channels.** The intra-object events (controller → visuals on the same GameObject) are faster, simpler, and don't benefit from the decoupling that SO channels provide. Only use SO channels when the sender and receiver are in different scene hierarchies or different subsystems.

---

## Trade-offs and the decisions behind them

**Partial classes vs. separate state classes:** Celeste's developers deliberately kept their 5,400-line Player class monolithic because "the player behavior code needs to be very tightly ordered and tuned" and splitting into separate classes "would turn into a giant messy web of references." Your partial-class split is a pragmatic middle ground — file-level organization without inter-class communication overhead. The downside is no encapsulation between states (any state can read/write any field). For a solo/small-team project, this is an acceptable trade-off. If you later add developers, consider extracting state-specific fields into state objects.

**Component composition vs. inheritance for platforms:** Inheritance creates a combinatorial explosion (`ConveyorCrumblingPlatform`, `JumpPadConveyorPlatform`, etc.). Composition lets designers mix behaviors freely. The cost is more GameObjects with more components, slightly more complex prefabs, and `GetComponents<IPlatformEffect>()` calls instead of virtual method dispatch. At 30 features, composition wins decisively.

**ScriptableObject event channels vs. a static EventManager singleton:** The singleton is simpler to implement (one static class, `EventManager.Fire<T>()`). But it creates a hidden global dependency, can't be inspected in the editor, and doesn't support scene isolation. SO channels are inspectable assets with explicit wiring. The overhead is one `.asset` file per channel. For a project adding 30 features, the debuggability of SO channels justifies the extra files.

**Verlet rope vs. Unity joints:** Unity's Spring/Hinge joints are seductive because they're built in. But community consensus from shipped projects is clear: chained joints are unstable, hard to tune, and perform poorly. Verlet integration gives you full control over iteration count, constraint behavior, and collision response. The cost is ~100 lines of custom physics code per deformable type.

**FSM vs. behavior tree for enemies:** Behavior trees shine when enemies need complex priority-based decision-making with interruptible multi-step sequences. Your enemies have 4–5 states with linear transitions. An FSM is 50 lines of code; a behavior tree for the same behavior is a tree of 15+ nodes with a runtime evaluator. Use FSMs now, upgrade if you add complex bosses later.

---

## Conclusion

The architecture hinges on three core patterns that solve your three biggest scaling risks. First, `IExternalController` prevents the SlimeController from absorbing every forced-state scenario — enemies, interactables, and cinematics own their player-override behavior, and the controller just surrenders and resumes. Second, `IPlatformEffect` composition eliminates the inheritance explosion that would otherwise accompany 14 new surface/platform types — each behavior is a single MonoBehaviour, mixed and matched on prefabs. Third, the two-tier event system (C# delegates intra-system, SO channels cross-system) keeps communication decoupled without the performance overhead of broadcasting everything through a global bus.

The less obvious but equally important decision is what *not* to unify. Don't build a shared spring-mass solver — ropes, water, cloth, and branches have different dimensionalities and collision needs. Don't build a behavior tree framework for two simple enemies. Don't migrate working C# events to SO channels when they don't cross system boundaries. Architectural elegance that doesn't solve a real problem is just code to maintain. Every pattern recommended here exists because a specific feature in your 30-item list would be painful without it.