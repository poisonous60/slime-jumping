# The definitive resource guide for building SlimeJumping

**Custom physics, squash-and-stretch animation, precision platformer feel — everything a beginner-intermediate Unity developer needs to build a 2.5D slime platformer is catalogued below, organized by topic with difficulty ratings and direct links.** This guide draws from Reddit community wikis, open-source projects, GDC talks, developer postmortems, and technical documentation. Resources span from copy-paste starter scripts to Celeste's actual production source code, so you can learn at every level of depth.

The guide is structured around SlimeJumping's core systems: custom physics, 2.5D rendering, camera, animation, and level design. Each section identifies **the single best starting resource** alongside deeper alternatives.

---

## What the Reddit community wikis recommend

The r/gamedesign and r/gamedev wiki pages serve as curated launchpads for game developers. While the pages themselves resist direct scraping, their recommended resources are well-documented across the community. The most relevant picks for a platformer project like SlimeJumping:

**Books worth reading first:** *Game Feel* by Steve Swink covers the physics-to-feedback loop central to a bouncy slime character. *Game Programming Patterns* by Robert Nystrom (free at gameprogrammingpatterns.com) teaches the state machine and update patterns you'll need for wall-slide, bounce, and launch states. *The Art of Game Design: A Book of Lenses* by Jesse Schell offers 113 frameworks for evaluating your design decisions.

**Key community guidance for your skill level:** The r/gamedev FAQ emphasizes escaping "tutorial hell" by building something original after each tutorial — SlimeJumping is exactly this kind of project. Game jams (Ludum Dare, GMTK Jam) are recommended for rapid skill-building. The FAQ also highlights **Freya Holmér's Math for Game Devs** series and **RedBlob Games** (redblobgames.com) for the vector math underlying custom physics, and points to **Game Maker's Toolkit** as the go-to channel for platformer design analysis.

---

## Custom physics and character controllers

This is the heart of SlimeJumping. You're building a character controller that handles wall sliding, momentum-preserving bounce, and power launch — likely without relying on Unity's built-in Rigidbody physics. Three architectural approaches dominate the community, and you should understand all of them before choosing.

### The three approaches compared

**Pure raycast (no Rigidbody):** You move the character via `Transform.Translate` and detect collisions with raycasts or overlap checks. This gives total control but requires building gravity, friction, and collision resolution from scratch. Sebastian Lague's playlist and Tarodev's controller use this approach.

**Hybrid (Rigidbody with manual velocity override):** You attach a Rigidbody but override its velocity directly each frame, using the physics engine only for collision detection. This is what Celeste uses (via FNA) and what Dawnosaur's controller implements. **For a 2.5D game, this is likely the best choice** — Unity's 3D physics handles environmental collisions while you control the character's movement precisely.

**Force-based with heavy tuning:** You use `AddForce` but tune gravity scales, drag, and acceleration curves aggressively. Bennett Foddy's GDC talk covers making this feel tight rather than floaty. Less recommended for precision platformers.

### Top resources, ranked by relevance

**🏆 Tarodev's Ultimate 2D Controller** — The single best starting point. Entirely custom physics with no Rigidbody. Implements **coyote time, jump buffering, variable jump height, apex modifiers, and clamped fall speed** out of the box. The extended version adds wall sliding, dash, bouncy surfaces, and moving platforms. Extremely well-commented single script. *(Beginner-Intermediate)*
- Video: https://www.youtube.com/watch?v=3sWTzMsmdx8
- GitHub: https://github.com/Matthew-J-Spencer/Ultimate-2D-Controller

**🏆 Dawnosaur's Platformer Movement** — A hybrid Rigidbody2D controller that directly recreates movement profiles from **Celeste, Super Meat Boy, and Hollow Knight** with side-by-side comparisons. Uses ScriptableObject for all tunable parameters. Already includes wall sliding, wall jumping, and squash/stretch on jump and land. *(Intermediate)*
- GitHub: https://github.com/DawnosaurDev/platformer-movement
- Demo: https://dawnosaur.itch.io/platformer-movement-demo-2

**Sebastian Lague's 2D Platformer Series** — The gold standard tutorial for raycast-based collision from scratch. Ten episodes covering slopes, through-platforms, moving platforms, wall sliding, and wall jumping. *(Beginner-Intermediate)*
- Playlist: https://www.youtube.com/playlist?list=PLFt_AvWsXl0f0hqURlhyIoAabKPgRsqjz
- GitHub: https://github.com/SebLague/2DPlatformer-Tutorial

**Catlike Coding Movement Series** — Eleven text tutorials building 3D character movement progressively. The "Reactive Environment" tutorial specifically covers **launch pads and acceleration zones** (directly relevant to SlimeJumping's power launch). The 3D-native approach makes it ideal for 2.5D. *(Intermediate-Advanced)*
- URL: https://catlikecoding.com/unity/tutorials/movement/

**Mix and Jam's Celeste Recreation** — Recreates Celeste's wall grab, wall slide, wall jump, and dash in Unity. The dash mechanic maps directly to a power launch system. *(Intermediate)*
- Video: https://www.youtube.com/watch?v=STyY26a_dPY
- GitHub: https://github.com/mixandjam/Celeste-Movement

**Roystan's 2D Character Controller** — The cleanest "from absolute scratch" tutorial. Uses `Transform.Translate` plus `Physics2D.OverlapBox` with zero Rigidbody involvement. Excellent architectural foundation. *(Beginner)*
- URL: https://roystan.net/articles/character-controller-2d/

**Board To Bits — Better Jumping in Unity** — A focused 5-minute video on making jumps feel good through separate ascending/descending gravity multipliers. Essential for a bouncy character. *(Beginner)*
- Video: https://youtu.be/7KiK0Aqtmzc

Two additional open-source controllers worth studying: **2D Platformer Hunter** (https://github.com/ta-david-yu/2D-Platformer-Hunter) extends Sebastian Lague's work into a fully modular Input-Controller-Motor architecture with swappable mechanics modules, and **akashenen's controller** (https://github.com/akashenen/2d-platformer-controller) includes configurable wall slide, slope climbing, and ladders.

---

## Setting up 2.5D in Unity with URP

A 2.5D game uses a 3D world with gameplay constrained to a 2D plane. The key challenges are movement constraint, depth sorting, and making URP's rendering pipeline work correctly for this hybrid setup.

**GameDev.net's 2.5D tutorial series** is the most targeted resource. Part 1 (https://www.gamedev.net/tutorials/programming/general-and-gameplay-programming/making-25d-game-in-unity-part-1-r5362/) covers camera setup, collider strategies, and the "card" art style approach. **Part 2** (https://www.gamedev.net/tutorials/programming/graphics/making-25d-game-in-unity-part-2-r5368/) solves a critical URP problem: modifying the URP Unlit shader (`ZWrite On`, `Cull Off`, alpha clipping) so sprites render as full 3D objects with correct depth sorting. This unlocks Depth of Field, Fog, and other URP post-processing for 2.5D. *(Beginner-Intermediate)*

For URP shader development — essential for a stylized slime character — **Cyanilux's URP shader code guide** (https://www.cyanilux.com/tutorials/urp-shader-code/) is the definitive written reference, covering HLSL syntax, SRP Batcher compatibility, and lighting passes. For a cartoony aesthetic, the **Wayline toon shader tutorial** (https://www.wayline.io/blog/unity-toon-shader-tutorial-2023) walks through building a lit toon shader in URP step-by-step, and the **NiloToonURP GitHub repo** (https://github.com/ColinLeung-NiloCat/UnityURPToonLitShaderExample) provides MIT-licensed starter code with intentionally simplified, well-commented shader source. *(Both Intermediate)*

**CG Cookie's 2.5D Side Scroller course** (https://cgcookie.com/courses/creating-a-2-5d-side-scroller-game-in-unity) covers the end-to-end process including level design with 3D prefabs, player control, traps, and UI. *(Beginner-Friendly)*

---

## Cinemachine camera for side-scrolling platformers

Cinemachine eliminates the need to write custom camera code. For a 2.5D platformer, the key components are: a Virtual Camera with Framing Transposer (body mode), dead zones and soft zones for responsive follow, look-ahead for movement anticipation, a Confiner for level boundaries, and Impulse sources for landing/bounce camera shake.

**Start here:** The official Unity blog post "Cinemachine for 2D: Tips and Tricks" (https://unity.com/blog/engine-platform/cinemachine-2d-tips-and-tricks) covers Virtual Camera creation, Framing Transposer setup, confiner configuration with CompositeCollider2D, and group cameras. *(Beginner)*

**For 2.5D specifically:** Ryan McCoach's tutorial (https://ryanjmccoach.medium.com/platformer-cinemachine-camera-follow-76ec922b0cb8) is one of the few resources that explicitly uses **perspective projection** (not orthographic) for a 2.5D platformer, with clear visual examples of how Dead Zone, Damping, and Lookahead settings affect camera behavior. Jean-Noel Seneque's series (https://jeannoelseneque.medium.com/2-5d-platformer-11-cinemachine-ac784749ec14) covers the same ground more concisely. *(Both Beginner)*

**For comprehensive understanding:** One Wheel Studio's overview (https://onewheelstudio.com/blog/2021/9/11/cinemachine) maps every Cinemachine feature including State-Driven Camera (changing behavior based on player state — useful for different camera behavior during wall slides vs. normal movement). *(Beginner-Intermediate)*

**For game feel:** The Cinemachine Impulse system (https://docs.unity3d.com/Packages/com.unity.cinemachine@2.3/manual/CinemachineImpulse.html) handles event-driven camera shake — **attach a Collision Impulse Source** to trigger shake on slime landings and bounces automatically. The Confiner2D documentation (https://docs.unity3d.com/Packages/com.unity.cinemachine@3.1/manual/CinemachineConfiner2D.html) covers level boundary setup with the latest API. *(Intermediate)*

---

## Making a slime feel alive with squash and stretch

Squash and stretch is the single most important animation principle for a slime character. There are three implementation tiers, from simplest to most sophisticated.

**Tier 1 — Scale manipulation (start here):** The simplest approach animates `transform.localScale` via coroutines. **Starpelly's SimpleSquashStretch.cs** (https://gist.github.com/Starpelly/b3588724c924310bb362c8571426a11c) is a ~35-line drop-in script that lerps between squeezed and original scale. Call `JumpSqueeze()` on land, jump, or bounce events. Dawnosaur's platformer controller already includes this built in. *(Beginner)*

**Tier 2 — Shader-based vertex deformation:** Alex Galbraith's tutorial (http://www.alexgalbraith.nz/2019/04/05/programmatic-squash-and-stretch/) uses a vertex shader that deforms sprites based on a transformation matrix driven by character acceleration and velocity. **The deformation happens automatically from the physics** — no manual triggering needed. This is the ideal approach for a slime whose shape should respond naturally to movement. *(Intermediate)*

**Tier 3 — Full soft-body simulation:** The **SoftBodySimulation repo** (https://github.com/chrismarch/SoftBodySimulation) implements spring-mass mesh deformation with collision response, volume preservation, and procedural jump compression — and uses a Dragon Quest slime as its test model. **PandaArcade's SquashAndStretch** (https://github.com/PandaArcade/SquashAndStretch) provides shader-based 3D mesh deformation with a translucent MatCap material that essentially looks like a slime shader already. *(Advanced)*

For foundational understanding, Unity's official animation tutorial (https://learn.unity.com/pathway/creative-core/unit/animation/tutorial/refine-your-animation) teaches keyframe-based squash and stretch principles.

---

## Layering on juice and game feel

Beyond squash and stretch, "juice" — the collection of effects that make interactions feel satisfying — is what separates a good platformer from a great one. Two canonical talks define the philosophy.

**Jan Willem Nijman's "The Art of Screenshake"** (https://www.youtube.com/watch?v=AJdEqssNZ-U) demonstrates ~30 incremental polish tweaks applied live to a game: screen shake, hit pause, permanence, camera lerp, knockback. Every principle transfers to a platformer. A Unity recreation of this talk exists as a playable project (https://github.com/colinbellino/screenshake). **"Juice It or Lose It"** by Martin Jonasson and Petri Purho (https://www.youtube.com/watch?v=Fy0aCDmgnxg) specifically demonstrates **squash/stretch on game objects, tweening/easing, and adding eyes that respond to the environment** — perfect for a slime character. Source code available at https://github.com/grapefrukt/juicy-breakout. *(Both Beginner)*

For practical Unity implementation, Jason Tu's "3 Basic Skills for Game Juice" (https://jasont.co/juice-techniques/) covers sound, easing curves (the **ease-out-elastic curve is perfect for slime bounce-back**), and particle systems. GameDev Academy's game feel tutorial (https://gamedevacademy.org/game-feel-tutorial/) includes Celeste-style squash/stretch and dust particles with Unity project files. *(Both Beginner)*

**For rapid iteration:** The **Feel (MMFeedbacks) asset** (https://feel.moremountains.com/) provides **150+ pre-built feedback types** including transform scale punch (one-click squash/stretch), screen shake, color flashes, Cinemachine integration, and time manipulation. Stack feedbacks into sequences triggered by jump, land, or bounce events without writing custom code. It's a paid asset but dramatically accelerates polish. *(Beginner-Intermediate)*

---

## How Celeste, Getting Over It, and others built their physics

The most valuable technical resources for SlimeJumping come directly from the creators of the games that inspired it.

### Celeste's physics architecture, from the creator's own words

**Maddy Thorson's technical article "Celeste and TowerFall Physics"** (https://maddythorson.medium.com/celeste-and-towerfall-physics-d24bd2ae0fc5) is the single most important technical document for anyone building a precision platformer. It details the **Actor/Solid architecture** — integer-locked positions with float remainders for sub-pixel movement, pixel-by-pixel collision detection via `MoveX`/`MoveY` with collision delegates, and moving platform logic that distinguishes "carrying" from "pushing." Includes C# code samples. *(Intermediate-Advanced)*

The **actual Celeste Player class source code** is publicly available (https://github.com/NoelFB/Celeste/tree/master/Source/Player) — approximately **5,400 lines of C#** covering every movement state, corner correction, hitbox management, and ~30 carefully tuned constants that create the game's signature feel. The README includes developer commentary on architectural decisions. A more navigable documentation of this code exists at https://deepwiki.com/NoelFB/Celeste/2.3-player-movement-and-physics. *(Advanced)*

### GDC talks and postmortems

Maddy Thorson's **GDC talk "Designing Celeste"** (https://www.youtube.com/watch?v=4RlpMhBKNr0) covers designing 300+ precision platformer rooms, progressive difficulty, and embracing unintentional player solutions. **Mark Brown's "Why Does Celeste Feel So Good to Play?"** (https://www.youtube.com/watch?v=yorTG9at90g) provides a frame-by-frame analysis of run curves, jump arcs, coyote time, jump buffering, and corner correction with developer quotes. His **Platformer Toolkit** (https://gmtk.itch.io/platformer-toolkit) lets you adjust 30+ variables and test them in a playable level — includes presets for Mario, Sonic, Meat Boy, and Celeste. *(Beginner-Intermediate)*

**Bennett Foddy's "Designing with Physics"** GDC talk (https://gdcvault.com/play/1021921/Designing-with-Physics-Bend-the) teaches how to make physics-engine games feel solid rather than floaty — directly relevant to SlimeJumping's hybrid physics approach. His interview on frustration design (https://www.gamedeveloper.com/design/designer-interview-the-aesthetics-of-frustration-in-i-getting-over-it-i-) explains how Getting Over It uses messy real-time physics and progress loss as deliberate design choices. *(Intermediate)*

The **Super Meat Boy "Meatmortem"** (https://www.gamedeveloper.com/audio/postmortem-team-meat-s-i-super-meat-boy-i-) reveals how Team Meat built a custom engine for precise 2D platforming and designed for **instant restarts, infinite lives, and short levels** — the three pillars of modern masocore design. The **Hollow Knight making-of** (https://gameinformer.com/2018/10/15/the-making-of-hollow-knight) describes their contrasting approach: zero acceleration/deceleration on horizontal movement, high initial jump lift, and Mega Man X as the movement model. *(Both Intermediate)*

---

## Level design principles for difficult platformers

Masocore design walks a razor's edge between challenge and frustration. The core principle from GDC's 2011 "Welcome to Masocore" talk: **"You can make a very hard game if you give the player the ability to respawn quickly"** and **"difficulty isn't bad if the player can see an escape from difficulty up ahead."**

Celeste's level design philosophy, detailed in Thorson's GDC talk and multiple analysis articles, treats each screen as a **micro-story with beginning, tension, and climax**. Mechanics are introduced chapter-by-chapter. The game teaches through obstacle placement rather than tutorials, and deliberately makes impossible things *look* impossible so players don't waste time. Detailed analyses exist at https://medium.com/@josephdiamond115/the-award-winning-level-design-in-celeste-c2acb315bf79 and https://primedpixel.co.uk/posts/lessons-ive-learnt-from-celestes-level-design/. *(Beginner-Intermediate)*

Getting Over It represents the opposite philosophy: frustration and progress loss as core mechanics. Understanding both approaches — Celeste's forgiving precision and Foddy's deliberate frustration — lets you calibrate where SlimeJumping sits on that spectrum.

---

## Recommended learning path for SlimeJumping

Rather than consuming everything at once, follow this sequence:

1. **Understand game feel theory** — Watch "Juice It or Lose It" and "Art of Screenshake" (2 hours total), read Steve Swink's game feel article
2. **Play with parameters** — Spend 30 minutes in GMTK's Platformer Toolkit adjusting physics variables
3. **Build the foundation** — Follow Roystan's tutorial to understand custom physics from scratch, then study Tarodev's controller for a production-ready architecture
4. **Study the masters** — Read Thorson's "Celeste and TowerFall Physics" article, watch Mark Brown's Celeste analysis
5. **Set up 2.5D + URP** — Follow the GameDev.net two-part series, add Cinemachine using the Unity blog guide
6. **Implement wall mechanics** — Reference Dawnosaur's controller and Mix and Jam's Celeste recreation
7. **Add squash/stretch** — Start with SimpleSquashStretch.cs, graduate to Alex Galbraith's shader approach
8. **Polish with juice** — Use Jason Tu's three techniques, consider the Feel asset for rapid iteration
9. **Design levels** — Study Celeste's screen-as-story approach through the analysis articles

**Key architectural decision:** For a 2.5D game with bounce physics and environmental interactions, the **hybrid approach** (Rigidbody for collision detection, manual velocity control for movement) offers the best balance of control and convenience. Catlike Coding's 3D movement series is the strongest reference for this pattern in a 3D/2.5D context, while Dawnosaur's controller demonstrates it in 2D.