# Slime Slice Redesign — Implementation Plan v2 (post-audit)

Status: PLAN v2. Blocker + high audit deltas baked in. Ship in stages S1→S4 (each no-op-safe,
user-testable). S0 = this doc.

## 1. Problem / goal

Current cut = rigid **flat clip-plane** (`SliceClip` discards `dot(osPosRest,n) > off`) + flat
dark cross-section + `_SliceBottomGuard` (skips clipping mesh-OS-y bottom band). Faults:
1. Reads as a **solid sawn in half** (straight machined edge, flat dark slab face).
2. Guard makes an **asymmetric shelf** (protects 30% of height — too much).
3. Cut that removes ground-contact material → visual **floats** (visual decoupled from the
   fixed `CircleCollider2D`). Guard was a bad fix; and it is NOT surface-aware (mesh-OS-y is
   fixed; on a wall it protects the wrong band — latent bug).

Requirements (from design interview):
- **Axis-A (instantaneous shape):** `sealRatio∈[0,1]`, open raw tear → surface-tension sealed
  / rounder / slightly smaller. Asymmetric at all values (NOT a uniform smaller ball).
- **Axis-B (recovery, independent):** continuous over ~20–60 s, two designer AnimationCurves
  (`recoveryCurve` for shape, `launchRecoveryCurve` for power — see §3). Orthogonal to A.
- **Goo read:** (1) **curved** boundary, (2) **outward surface-tension lip** (the #1 cue),
  (3) **wet translucent cross-section**. Boundary noise OPTIONAL, default low/off.
- **Grounding (any cut dir × any slope):** body stays planted on the current contact surface.
  Pure rigid sink → single-point contact only → tilted/slope still float. Solution = a
  surface-aware **core-foot clip invariant** (foot geometry always survives the clip) +
  vertex **conform/sink** (flatten/translate that foot flush). Conform alone is insufficient —
  a vertex stage cannot un-discard fragment-clipped foot.
- **Feel (Tier 2, controllable 20–60 s):** reduced launch power + launch **direction bias**.
  **CoM offset DROPPED** (inert — see §4.6). Collider stays a circle.
- **Severed droplet** (`SlimeChunk`/`SpawnSeveredPiece`) kept wholesale.

## 2. Architecture (locked)
Keep shipped fragment-clip + rest-OS-key + vertex-deform. REJECTED: SDF raymarch, soft-body,
metaball/marching-cubes, mesh-split. Body shader is hand-written `SlimeBody_Quibli.shader`
(no Shader Graph) → new vertex stages added in HLSL includes. Mesh 2670 v / 5120 tri (lip
viable with ANALYTIC normals + adequate band width). Core-preservation kept (`off>0`).

## 3. Two orthogonal axes
- `sealRatio` (A): boundary curvature/noise amp + lip outward magnitude + cap character.
  Instantaneous. Per-wound MPB `_SliceSeal[i]`.
- `healVisual = recoveryCurve.Evaluate(t/recoveryDuration)`: drives dispOffset recede + sink +
  lip unwind.
- `healLaunch = launchRecoveryCurve.Evaluate(t/recoveryDuration)`: drives `SplitLaunchMultiplier`
  + launch-dir bias decay. **Separate curve from the start** (audit: fusing them couples visible
  size and launch power). Both default to the same shape; designer can diverge.
- A ⊥ B: different uniforms; a wound can be sealed (A=1) at heal 0, or open (A=0) at heal 0.5.

## 4. File-by-file (with audit deltas)

### 4.1 New `SlimeSliceData.hlsl` — SOLE owner of slice data [BLOCKER fix]
`#ifndef SLIME_SLICE_DATA_INCLUDED` guard. Declares: `SLICE_MAX`, `_SliceCount`,
`_SlicePlaneOS[]`, `_SliceHeal[]`, `_SliceSeal[]`, `_SliceSinkAmount`, `_ContactPointWS`,
`_ContactNormalOS`, `_CutRoundness`, `_TearAmp`, `_TearFreq`, `_LipBand`, `_LipMag`, cap colors,
+ helper `SliceSignedDist(osPosRest, i)` (the ONE signed-distance the clip AND lip share).
- `#include "SlimeSliceData.hlsl"` becomes the **FIRST line of `SlimeBody_QuibliDeformInject.hlsl`,
  ABOVE the `SlimeDeform.hlsl` include**, so the vertex stages resolve the symbols.
- `SlimeSlice.hlsl` includes it (guarded no-op) and **deletes its own L31-39 declarations**.
- ShadowCaster (includes DeformInject only) now resolves symbols. **ShadowCaster slice policy:
  gate stages 6/7 OFF in ShadowCaster** (cheapest, no shadow artifact) via a compile path that
  does not run lip/conform/clip in the shadow pass — i.e. the new stages live behind a
  `#ifdef SLIME_FORWARD_SLICE` set only in ForwardLit/Occluded, OR add `SliceClip` to the
  shadow fragment. Decision: **gate OFF in shadow** (shadow stays the convex blob; a missing
  wedge in a stylized drop-shadow is invisible at this scale). Document in the shader.

### 4.2 `SlimeSlice.hlsl` (fragment)
- **Curved boundary:** clip against a sphere of curvature `_CutRoundness` (limit 0 == today's
  plane) for a smooth rounded cut face. Optional FROZEN low-freq noise `_TearAmp*noise(osPosRest*
  _TearFreq)`, default `_TearAmp=0`, faded near the foot. Boundary uses `SliceSignedDist`.
- **Core-foot clip invariant [BLOCKER fix]:** replace the mesh-OS-y `_SliceBottomGuard` with a
  **surface-aware** keep-band: never clip where `dot(osPosRest, _ContactNormalOS) < footY`,
  where `footY = -extentAlongContact + footPatch` (footPatch = small contact-patch fraction,
  default ~0.12·R, NOT 0.3·height). Always active while `_SliceCount>0`. `_ContactNormalOS` =
  C# `InverseTransformDirection(SurfaceNormalWS)` (tracks walls/ceilings/slopes). When Airborne
  there is no surface — C# sets `_ContactNormalOS` to the GravityUp-OS so the cut still shows
  with a minimal foot until landing (or footPatch→0 in air; pick in S1 test).
- **Wet cap (`SliceCap`):** depth-shaded interior `lerp(capDeepColor, capRimColor,
  saturate(distIntoWound/_CapDepth))` + fresnel wet rim `pow(1-saturate(dot(V,N)),
  _CapFresnelPower)`, cel-quantized.
- `SliceHealFill`: keep, driven by `_SliceHeal` (= healVisual band). Drop guard ref.
- `_SliceCount==0` ⇒ whole file no-op.

### 4.3 `SlimeDeform.hlsl` (vertex) — stages 6/7, gated `_SliceCount>0.5` FIRST [audit]
- **Stage 6 — lip:** per wound, kept-side verts in `d∈[-_LipBand,0]` (d = `SliceSignedDist`)
  push OUTWARD along normalOS by `_LipMag*smoothstep(-_LipBand,0,d)*_SliceSeal[i]`. **ANALYTIC
  normal** (blend profile-gradient normal to base sphere normal by the SAME smoothstep weight;
  ==base at the band ends) — NOT finite-difference. Compute **BEFORE stage 5b/5c velocity
  stretch** so the push axis stays surface-glued. `_LipBand >= ~0.15·extentOS` (≈4 edge lengths),
  `_LipMag` capped small.
- **Stage 7 — conform/sink:** translate by `-_ContactNormalOS·_SliceSinkAmount` (C# closed-form,
  §4.5) + per-vertex soft-clamp verts penetrating below the contact plane up onto it (foot
  patch). Disjoint band from the lip. **Double-grounding reconciliation [audit]:** while a wound
  is live, **gate stage-3's `maxCompress` sink OFF** (let stage-7 own body3d grounding) and
  **suppress SlimeVisual.cs:290-297 ground-compensation for body3d** while wounded; stage-7 is
  then the single grounding op. Verify foot not double-buried.
- Stages 6/7 run only in ForwardLit/Occluded (gated off in shadow per §4.1). MUST only WRITE
  positionOS/normalOS strictly AFTER osPosRest capture (preserve burn/frost/clip weld); may READ
  osPosRest.

### 4.4 `SlimeFaceSlice.shader` + face handling [audit]
- Match curved boundary by passing the cut sphere **projected into the face plane** (center+radius
  WS), not the raw 3D sphere (a flat sprite vs raw 3D sphere = wrong arc).
- **Keep a face foot-guard** (face has no conform; a low cut must not vanish an eye the body
  still shows). Surface-aware like the body.
- Face **sink = additive local delta** onto what `UpdateFaceOffset` wrote (SlimeVisual.cs:1037
  overwrites X/Y each LateUpdate): `face.localPosition += parentInv(-ContactNormalWS*sink)`,
  **skipped when `IsExternallyAnimated`/external face offset active**. Lip mismatch bound ≈
  `_LipMag` only where an eye sits at the rim — document, drop the stale "sub-pixel" claim.

### 4.5 `SlimeSplitSequence.cs`
- Timeline: `regenHold/Gather/Fill` → `recoveryDuration` + `recoveryCurve` (healVisual) +
  `launchRecoveryCurve` (healLaunch). `dispOffset = lerp(cutOffset, pastMesh, healVisual)`.
- Push per frame in LateUpdate: `_SliceSeal[]`, `_SlicePlaneOS[]`, `_SliceHeal[]`, `_CutRoundness`,
  cap colors, `_LipBand/_LipMag`, `_ContactNormalOS`, `_ContactPointWS`, `_SliceSinkAmount`.
  **MUST NOT write `_SurfaceNormalWS`** (SlimeVisual owns it) — use the dedicated `_ContactNormalOS`.
- **Closed-form sink [audit formula]:** u = SurfaceNormal; per wound c = dot(n_cut, u);
  lowest kept point candidate A = -R (valid iff -R·c <= off), candidate B (rim) = off·c -
  R·sqrt(max(0,1-c²)); `sink = max(0, contactHeight - min(valid candidates))`. **R from VISUAL
  mesh half-extent × body3d lossyScale** (not collider radius). Multi-wound: lowest point over
  the INTERSECTION of all kept half-spaces, single sink. Active only when a contact surface
  exists (Attached OR grounded-Stunned); 0 + lerp-out when Airborne. Push every frame; lerp
  on attach.
- Launch: `SplitLaunchMultiplier = lerp(min,1,healLaunch)` (min across wounds). **Launch-dir
  bias** = a clamped SMALL-ANGLE rotation (≤~5–10°) toward kept side, magnitude scaled by
  `(1-healLaunch)`, **combined multi-wound angle clamped to the single cap**.
- `RestoreAll` also zeroes `_SliceSinkAmount`/`_ContactPointWS`/`_ContactNormalOS`/flag.
- Retire wound when `healVisual>=~1 AND healLaunch>=~1` (not timer alone).
- Orphan cleanup: remove `SliceBottomGuardID`/`SliceBottomGuardWSID`, guardY block (cs:246-247),
  guardWS block (cs:259-262), the guardWS param through `PushFace`.

### 4.6 `SlimeController.Split.cs` (partial)
- Keep `SplitLaunchMultiplier`.
- ADD `SplitLaunchDirBias` (Vector2 world, kept-side) — applied in `Launch()` as a clamped
  small-angle rotation of `dir`, **on the aim/power term BEFORE** `TryApplyBambooV2ChargedLaunch`
  and the carrier/rope/bamboo `velocity +=` (Launch.cs:108-141); do NOT re-derive the rope
  `launchDir` (L127). **Disabled when `currentRope`/`currentBambooGrab`/`carrier` active.**
- **CoM DROPPED entirely** [BLOCKER]: `rb` is `FreezeRotation`, motion fully analytic, zero
  torque/AddForceAtPosition anywhere → `centerOfMass` is inert. No `SplitCenterOfMassOffset`.
  Optional later: a small kept-side tangent skew in the analytic slide/crawl for an attached
  "lean" (NOT in S1–S4 unless requested).

### 4.7 `SlimeSplitSettings.cs` + migration [audit]
- ADD: `sealRatio` (~0.25), optional `sealOnsetCurve`, `cutRoundness`, `tearAmp`(0)/`tearFreq`,
  `lipBand`/`lipMag`, `capDeepColor`/`capRimColor`/`capFresnelPower`/`capDepth`, `footPatch`,
  `recoveryDuration` (~30), `recoveryCurve`, `launchRecoveryCurve`, `launchDirBiasDeg`,
  `sinkLerpSpeed`.
- REMOVE: `regenHoldDuration/regenGatherDuration/regenFillDuration/regenFillCurve`,
  `bottomProtectFraction`. KEEP piece/melt/wobble/stacking/sliceHealColor block.
- **Migration:** Unity drops orphan keys, but the asset has `woundedJumpMultiplierMin: 1`
  (user deliberately disabled the penalty) and an authored regen timeline — both silently lost.
  Tier-2 now WANTS a launch penalty, so hand-author `woundedJumpMultiplierMin` (~0.5) +
  `recoveryDuration`/curves directly in the `.asset` so first Play is intentional (TestSetup
  only creates-if-missing, won't repair).

### 4.8 Docs
Update `saw-knife-hazard.md`, `프로토타입_구현_현황.md`, `SlimeJumping_메커닉_설계_v6.md`.

## 5. Regression safety
`_SliceCount==0` ⇒ all new fragment + vertex stages no-op (verified byte-for-byte for fragment;
vertex gated on `_SliceCount>0.5` first). Sink/conform off + lerp-out in air. Stage-3 sink only
suppressed WHILE wounded. CoM removed. Launch-bias default 0 + disabled under rope/bamboo/carrier.
Droplet shares `sharedMaterials[0]` → every new property MUST default to a strict no-op.
SlimeController core untouched except the Split partial.

## 6. Staged ship order (each independently no-op-safe + user-testable)
- **S1 fragment:** SlimeSliceData.hlsl + include hoist + ShadowCaster gate; curved boundary +
  wet cap + frozen-noise(0) + surface-aware core-foot clip replacing the guard; C# axis-A
  sealRatio + axis-B healVisual/healLaunch curves; settings migration w/ preserved tuning.
  VERIFY: uncut blob byte-identical; cut reads gooey; no float on slab-cut/flat-floor; burn/frost
  still welded; compiles (unity-cli console clean).
- **S2 lip (stage 6):** analytic lip + analytic normal, before 5b, lipBand≥~4 edges, ForwardLit-only.
  VERIFY: no silhouette spikes; no crawling cel band on the rim; shadow/x-ray consistent.
- **S3 conform+sink (stage 7 + C# closed-form):** reconcile vs stage-3 (gate one off);
  intersection sink; R from visual extent; soft-clamp; face additive sink w/ external gate;
  per-frame push + RestoreAll zeroing. VERIFY: planted foot on tilted-cut × slope × multi-wound;
  no double-bury; face tracks body.
- **S4 feel:** launch-dir small-angle rotation, aim/power only, disabled under rope/bamboo/carrier,
  multi-wound clamp. VERIFY over 20–60 s w/ 1.2 s recut cadence that aim authority is preserved.

## 7. Residual risks (watch during impl)
1. Curved fragment clip (rest-OS) vs lip/conform vertex push (deformed OS) can slide under
   squash/charge-dip — mitigate: lip band uses the SAME `SliceSignedDist(osPosRest)` the clip uses.
2. CPU sink must mirror GPU stages; add one play-mode assertion sampling a few GPU verts vs the
   C# formula; keep formula in one shared place.
3. `surfaceNormal` is smoothed/lags on rotating platforms (SlimeController.cs:654-664); feed the
   conform an unsmoothed contact normal + slerp the contact-plane orientation on attach.
4. Droplet shares the body material — verify every new prop default is a no-op pre-first-cut.
5. `recoveryDuration ~30s` ≫ droplet life ~6.2s — wound outlives droplet; confirm droplet
   self-cleans its ignore before retire; no conform/sink reads a dead chunk.
6. MeshDepthFlatten non-uniform Z foreshortens any lip Z-component — document as approximation.
7. Removing the guard lets burn/frost on a removed lower wedge vanish with the wedge (only while
   a wound is live, not a `_SliceCount==0` regression) — **confirm desired with user** before S1 merge.
