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

## Slime Split (베임 분할 연출)
고속 히트 시 스턴 대신 **슬라임을 실제로 두 조각으로 가르는** 연출. Knife/Saw 양쪽에 옵셔널 훅.

**발동**: 해저드의 `splitSequence` 슬롯이 채워져 있고 슬라임 속도 ≥ `splitSpeedThreshold`이면 `SlimeSplitSequence.Begin()` 호출 + `return`으로 기존 스턴/넉백/passthrough 경로를 **완전히 대체**. 미할당 시 기존 동작과 byte-for-byte 동일 (회귀 0). Saw는 자체 저속 진입가드가 없어 split은 `splitSpeedThreshold`로만 게이트; Knife는 기존 `velocityThreshold`도 함께 충족해야 도달.

**파이프라인**:
- `SlimeChunkCutter` (`Scripts/Effects/`) — DynamicMeshCutter를 **메쉬 생성기로만** 사용. 매 히트 throwaway 프록시(슬라임 블롭 메쉬)를 칼날 perp 평면(+`cutNormalJitterDeg` 랜덤)으로 1회 절단 → 정확히 2조각. `MeshTarget.CreateRigidbody/CreateMeshCollider=false`로 **3D 물리 미생성**(2D 게임), 저수준 `MeshCutting.Cut` 동기 호출(씬 CutterBehaviour Update 펌프 불필요). 절단 모양이 매번 유니크. 런타임에 DMC 컴포넌트/에셋 미잔존.
- `SlimeChunk` (`Scripts/Effects/`) — 잘려나간 조각 1개. 슬라임과 **같은 중력 벡터**(gravityScale 0, 수동 적용)로 날아가 낙하 + **점성 wobble**(감쇠 sine squash를 메쉬 child 트랜스폼에 적용, 스폰·바운스마다 재충전; 부피보존 X/Y) + 셰이더 velocity-stretch(**per-renderer MPB만** — 공유 `Mat_SlimeBody3D_Quibli` 미변경). 착지 시 **지면 위에서 녹음**: flatten(Y scale→0)+spread(X scale↑) 하면서 매 프레임 **렌더러 world AABB의 바닥-중심을 착지 지점에 핀**. 지면 Y는 melt 시작 때 중력 방향 raycast(non-trigger만 → 회전 톱 무시)로 해결, 콜라이더 contact 폴백. DMC 조각 메쉬의 pivot은 조각 중심이 아니라 **슬라임 원점**이라 pivot 기준 sink는 허공 부유/지면 관통을 일으켰음 → AABB 기준 핀으로 해결. 지면 없으면(구덩이) `meltSinkFactor` in-place 폴백. 녹은 자리에 `WorldTrail` 얼룩 stamp.
- `SlimeSplitSequence : IExternalController` (`Scripts/Effects/`) — `SetOverride`로 슬라임 장악. 타임라인: **FALL**(2조각 흩어짐·낙하, 숨긴 슬라임을 생존 조각 위치로 `OverridePosition` → 프록시 기반 Cinemachine이 생존 조각 자동 추적, 카메라 코드 0 수정; 잘려나간 조각은 `severedFadeDelay` 후 shrink 소멸) → **REGEN**(생존 조각 안정 후 슬라임이 그 자리에서 reform: **SlimeVisual 트랜스폼**만 scale 0→1, 슬라임 루트 미변경 → 콜라이더 무영향) → `ReleaseOverride`로 Airborne 복귀. degenerate cut/구덩이 낙하는 `startPos` 폴백.
- `SlimeSplitSettings` (`Scripts/Settings/`, SO) — 절단/흩어짐 물리/wobble/페이드/낙하·안정/재생성 전 항목 인스펙터 노출. `SlimeVisualSettings` 패턴.

**회귀 안전**: 기존 `SlimeVisual.cs`/공유 `Mat_SlimeBody3D_Quibli`/`SlimeIcosphere` 미변경. 슬라임 측 변경(렌더러 enabled, SlimeVisual 트랜스폼 scale)은 전부 가역 복원. 콜라이더 무시는 기존 `RegisterIgnoredCollider`(bounded duration + `OnDestroy` 안전망) 재사용. `SlimeController` 코어 미수정 — 공개 API(`SetOverride`/`Override(Position|Velocity)`/`ReleaseOverride`/`RegisterIgnoredCollider`)만 사용.

**테스트**: 메뉴 `SlimeJumping/Effects/Setup Slime Split Test`(설정 SO + `Mat_SlimeChunk` 변종 생성, 씬 Knife/Saw 와이어), 플레이 후 `SlimeJumping/Effects/Trigger Split Now (Play Mode)`로 즉시 발동. `Scripts/Editor/SlimeSplitTestSetup.cs`.

## 관련
- [../slime/stunned.md](../slime/stunned.md)
- [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md)
