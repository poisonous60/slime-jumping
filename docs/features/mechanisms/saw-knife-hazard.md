# SawBladeHazard / KnifeHazard (베임 해저드)

## 공통
두 해저드 모두 `IHazardSource` 구현. 접촉 시 `SliceEffect` 스폰 + `AddExternalVelocity` + `ApplyHazardEffect`로 Stunned.

## SawBladeHazard
Pull 방향 `HazardEffect` (radial + tangent 블렌드, `speedRetainRatio`, `hitCooldown`).
- 접촉 시 `SliceEffect` 스폰 (파티클/SFX/Impulse).
- `AddExternalVelocity(tangent * sliceKick)` — tangent 부호는 슬라임 속도 사영으로 결정 (정지 시 `rotationRPM` fallback).

## KnifeHazard
Pull + **body passthrough** (관통 가능).
- `Physics2D.IgnoreCollision` + SurfaceProbe ignore list (`IgnoredColliderList.Passthrough` 모드: 타이머 only로 해제, overlap-aware release, re-hit guard).
- 접촉 시 `SliceEffect` 스폰 + `AddExternalVelocity(edge * sliceKick)` — edge 방향은 `bladeEdgeRef.right` (null이면 `transform.right`), 부호는 슬라임 속도 사영.

## SliceEffect (공통 베임 피드백)
`Knife/Saw` 공통. `SliceEffect.Spawn(prefab, contact, signedTangent)` 정적 팩토리.
- `signedTangent`는 local +Y로 cone 회전 정렬.
- `ParticlePaletteBinder`로 `VFXType.Slice` 팔레트 색 자동 주입.
- 수명은 `max(particleLife, audioClip.length) + 0.1s`로 자체 관리 — SFX 잘림 방지.
- `DamageType.Slice`로 해저드 식별.
- 튜닝 가이드: [VFX_튜닝_가이드.md](../../VFX_튜닝_가이드.md).

## Slime Split (베임 연출)
블레이드 히트 시 **일반 스턴 위에 레이어링되는** 베임 연출. 스턴/넉백/control-loss/눈은 평소대로 `Stunned` 상태에서 오고(다른 해저드와 일관), 그 위에 (1) 떨어져나간 조각 1개 + (2) 몸통 셰이더 절단 상처 + (3) 얼굴 절단을 얹는다. **슬라임을 장악(override)하지 않는다.** Knife/Saw 양쪽 옵셔널 훅.

> ⚠️ 2026-06-06 이전의 `IExternalController` FALL/REGEN(두 실조각 분리 + 카메라가 생존조각 추적 + 슬라임 reform) 방식은 **폐기**됨. 현재는 아래 layered 방식.

**발동**: 해저드의 `splitSequence` 슬롯이 채워져 있고 `!slime.HasOverride`이면 `ApplyHazardEffect`(스턴) 직전에 `Begin(slime, slime.Velocity)` 호출. 미할당 시 기존 동작과 byte-for-byte 동일 (회귀 0). 재타격 cadence는 블레이드의 `hitCooldown`(1.2s)+passthrough가 1차 게이트, `SlimeSplitSequence` 내부 `recutMinInterval`가 동일프레임/중복해저드 가드.

**파이프라인**:
- `SlimeChunk` (`Scripts/Effects/`) — 잘려나간 조각 1개. **DMC 절단 안 씀** — 조각은 슬라임 자신의 블롭 메쉬(`origMesh`)를 작게(`pieceFinalScale` × 슬라임 **콜라이더(비변형) 반경**, 균일 스케일 → 진짜 구) 스폰. 슬라임과 **같은 중력**(gravityScale 0 수동)으로 날아가며, 잘린 직후 `pieceSharpBeat`(~0.15s)는 **절단 노멀 따라 납작한 sliver**(메쉬 child를 cut normal에 정렬 후 그 축으로 `pieceFlatten`까지 flatten, 부피보존) 유지 → 공중에서 `pieceRoundUpDuration`(~0.35s) 동안 **표면장력 un-squash**(flat→1 lerp)로 **진짜 둥근 droplet**으로 펴짐(깨끗한 블롭 topology, 정점 reproject 아님). round-up 끝나면 mesh 프레임 upright 리셋. 그 위에 점성 wobble(부피보존 X/Y) + 셰이더 velocity-stretch(per-renderer MPB). 착지 시 **지면 위에서 녹음**(flatten+spread, 매 프레임 world AABB 바닥-중심을 착지 지점에 핀; 구덩이는 `meltSinkFactor` 폴백) + `WorldTrail` 얼룩 stamp. 조각 콜라이더는 **슬라임과 절대 충돌 안 함**(spawn 시 `RegisterIgnoredCollider` Passthrough) — 해제는 **조각 수명**(ignoreDur 타이머 / GameObject 파괴 시 null self-clean)으로만, **상처 회복 타임라인과 무관**(상처는 ~4.4s에 낫지만 조각은 ~5.2s까지 떠 있어, wound에 묶으면 회복 후 조각이 슬라임과 재충돌함).
- `SlimeSlice.hlsl` + `SlimeBody_Quibli.shader` — 몸통은 full 메쉬 유지, 절단은 **rest-pose OS clip plane 배열**(최대 `SLICE_MAX=3`)로 렌더. `_SliceCount`/`_SlicePlaneOS[3]`(xyz=normal, w=offset)/`_SliceHeal[3]`/`_SliceCapColor`/`_SliceHealColor`/`_SliceHealWidth` MPB. 어느 plane이든 removed side면 fragment discard, cut opening은 back-face(Cull Off)에 **어두운** 단면색. **재생 연출** = `SliceHealFill` (fragment): 절단 edge(closing front) kept-side 밴드를 **밝은 점액색(`sliceHealColor`)으로 lerp** → 그 뒤는 body 색으로 정착. clip edge가 fill 동안 (느리게, `regenFillDuration`) 후퇴하므로 fresh-goo 밴드가 상처를 **쓸며 차오름** = 점액이 차올라 메우는 느낌. 발광/HDR 없음(초기 emissive 발광=반전술식은 토온 슬라임에 안 어울려 폐기). **지오메트리 변위 없음**(초기 `SliceDome` vertex bulge는 저폴리 흰 spike로 폐기). ForwardLit fragment 적용, clip은 ForwardLit+OccludedSilhouette. `_SliceCount=0` → 완전 no-op(회귀 0). **코어 보존 불변식**: offset>0이라 중심(dot=0)은 어느 plane도 제거 못 함 → 아무리 겹쳐도 블롭은 안 사라짐(비치명). **하단 가드(`_SliceBottomGuard`)**: rest-OS y < guardY(아래 `bottomProtectFraction`=30%)는 어느 wound도 안 자름 — 밑면(접지측 -localY) 보존해 잘린 부분만큼 슬라임이 **떠 보이는 현상 방지**(윗부분만 잘림). 얼굴도 동일 가드를 월드평면(`_SliceBottomGuardWS`)으로 적용.
- `SlimeFaceSlice.shader` (신규) — 눈/입 SpriteRenderer를 **월드공간** clip plane 배열(`_SliceCountWS`/`_SlicePlaneWS[3]`)로 동일 절단. 얼굴 스프라이트는 몸통 deform을 안 타므로 SSS가 각 상처의 rest-OS plane을 body 현재 transform으로 월드 변환해 매 프레임 push. 잘린 쪽 눈/입 사라지고 상처 회복하면 복귀. 스톡 URP Default Sprite 머티리얼 → `Mat_SlimeFaceSlice`로 교체(에디터 와이어).
- `SlimeSplitSequence` (`Scripts/Effects/`) — 상처 리스트(최대 3). 각 상처 **back-loaded 타임라인**: HOLD(어두운 깨끗한 단면, `regenHoldDuration`) → GATHER(**재생 발광 seam** 0→1 ramp, `regenGatherDuration`) → FILL(clip이 `regenFillCurve`로 mesh 밖 후퇴, 발광 seam이 상처 쓸며 닫음, `regenFillDuration`). **전신 squash/pop 없음** — 전부 상처 국소 셰이더 발광. 재절단 = 새 상처 추가, 3개 초과 시 가장-많이-회복된 상처 evict. 점프파워(`SplitLaunchMultiplier`)는 **가장 안 나은(freshest) 상처** 기준 — HOLD/GATHER 동안 `woundedJumpMultiplierMin`, FILL에 full snap, 재절단 시 min 리셋. body+face MPB push는 SSS.LateUpdate(SlimeVisual/Burn 이후), 사전할당 길이-3 배열 재사용.
- `SlimeSplitSettings` (`Scripts/Settings/`, SO) — 절단/조각(`pieceFinalScale`/`pieceFlatten`/round-up)/wobble/melt/regen 타임라인/재생 발광(`sliceHealColor`/`sliceHealIntensity`/`sliceHealWidth`, `sliceCapColor`=어두운 단면)/stacking 전 항목 인스펙터 노출.

**회귀 안전**: 공유 `Mat_SlimeBody3D_Quibli`/`SlimeIcosphere`/`SlimeVisual` 핵심 미변경. 모든 변경(body+face `_Slice*` MPB→count 0, `SplitLaunchMultiplier`→1, `RegisterIgnoredCollider` bounded+`OnDestroy` 안전망)은 가역/default-off. `SlimeController` 코어 미수정(공개 API만). `SlimeVisual`은 `FaceTransform` 노출만 추가(전신 squash API는 redesign에서 제거). DMC(`SlimeChunkCutter`)는 미사용으로 삭제.

**테스트**: 메뉴 `SlimeJumping/Effects/Setup Slime Split Test`(설정 SO + `Mat_SlimeChunk` + `Mat_SlimeFaceSlice` 생성·와이어, 씬 Knife/Saw 와이어), 플레이 후 `Trigger Split Now (Play Mode)`로 즉시 발동(연속 호출 시 stacking 확인). 얼굴 머티리얼만 다시 와이어: `SlimeJumping/Effects/Wire Face Slice Material`. `Scripts/Editor/SlimeSplitTestSetup.cs`, `SlimeFaceSliceSetup.cs`.

## 관련
- [../slime/stunned.md](../slime/stunned.md)
- [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md)
