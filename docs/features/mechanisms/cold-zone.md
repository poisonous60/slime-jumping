# ColdZone (Stage2 동결 비주얼)

Trigger volume. 슬라임이 내부에 머물면 점진적으로 색이 변하고 표면에 성애가 끼는 **순수 비주얼** 메커닉. 게임플레이(동역학)에는 영향 없음. Stage2 "cold city" 분위기 연출용.

## 핵심 동작

```
ColdZone 내부      → freezeT 0 → 1 (default 8s)
ColdZone 외부      → freezeT 1 → 0 (default 15s, 비대칭)
freezeT == 1 시점  → 슬라임 _BaseColor가 _FreezeTint로 완전 lerp + frost 노이즈 최대 커버
```

`freezeT`는 [`SlimeFrostController`](../../../SlimeJumping_v2/Assets/Scripts/Visual/SlimeFrostController.cs)가 보유. 두 AnimationCurve로 색 전환과 성애 진행을 독립 타이밍 제어:
- `colorCurve(freezeT)` → `_FreezeColorAmount` (default linear 0→1)
- `frostCurve(freezeT)` → `_FrostStrength` (default 0~0.3 평탄, 0.3~1 ramp — 색이 먼저, 성애가 나중)

## 컴포넌트 구조

| 컴포넌트 | 위치 | 책임 |
|---|---|---|
| [`ColdZone`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/ColdZone.cs) | Stage2 추운 영역 trigger Collider2D | OnSlimeEnter→`PushColdZone()`, OnSlimeExit→`PopColdZone()` |
| [`SlimeFrostController`](../../../SlimeJumping_v2/Assets/Scripts/Visual/SlimeFrostController.cs) | Slime root | freezeT 누적, refcount push/pop, body MPB 푸시, 매니저에 freezeT 발행 |
| [`WorldTrailManager`](../../../SlimeJumping_v2/Assets/Scripts/Rendering/WorldTrailManager.cs) | 씬당 1개 | warm/cold RT 듀얼 보유, freezeT로 stamp split-write |
| [`LocalTrailReceiver`](../../../SlimeJumping_v2/Assets/Scripts/Rendering/LocalTrailReceiver.cs) | 특수 플랫폼 | 동일하게 듀얼 RT, MPB로 `_LocalTrailMaskCold` 푸시 |

## 셰이더 통합

### 슬라임 바디 ([`SlimeBody_Quibli.shader`](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeBody_Quibli.shader))
- 신규 [`SlimeFrost.hlsl`](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeFrost.hlsl) 인클루드 (`SlimeBurn.hlsl` 직후)
- ForwardLit + OccludedSilhouette 두 pass에서 `ApplyFreeze(col.rgb, osPosRest)` 호출 (burn 합성 직후)
- `ApplyFreeze` = `lerp(albedo, _FreezeTint, _FreezeColorAmount)` 후 `lerp(albedo, _FrostBaseColor, frostMask)`
- Frost noise는 OS rest-pose 기반 trilinear value noise (deform 후에도 표면에 welded)

### Trail 듀얼 합성 ([`Ground_TrailReceiverInject.hlsl`](../../../SlimeJumping_v2/Assets/Art/Shader/Ground_TrailReceiverInject.hlsl), Local 변종)
- 매 stamp는 `strength × (1−freezeT)`를 warm RT에, `strength × freezeT`를 cold RT에 BlitToTarget
- 그라운드 셰이더는 alpha-over로 합성: `final = lerp(warmTinted, coldTinted, coldMask × coldGradient.alpha)`
- Cold도 PosGradient/AngleGradient 동일 모듈레이션 적용 + luma-preserve tint (post-warm color 기준)

## Stage2 단일 슬라임 전제

`WorldTrailManager.currentFreezeT`는 단일 슬롯, `EnvironmentalZone.trackedSlime`도 단일 필드. 멀티 슬라임 도입 시 last-writer-wins이라 trail 라우팅이 한 슬라임 상태로만 결정됨. 멀티 슬라임 지원 필요 시 별도 작업 (manager dictionary화 + zone 멀티 추적).

## Cold trail 비가역성

freezeT가 0으로 돌아가도 `rtCold`에 stamp된 픽셀은 영구 유지 (warm trail 영구성과 동일). "녹은 슬라임이 과거 차가운 trail 위를 걷는" 시각적 모순은 의도된 디자인 — "얼었던 흔적이 남은 stage" 서사. 필요 시 fade decay는 추후 옵션으로 추가 가능.

## 튜닝

| 파라미터 | 위치 | 기본 | 용도 |
|---|---|---|---|
| `freezeDuration` | SlimeFrostController | 8s | T 0→1 누적 시간 |
| `thawDuration` | SlimeFrostController | 15s | T 1→0 회복 시간 (비대칭 ↑) |
| `colorCurve` | SlimeFrostController | linear | freezeT → 색 lerp 곡선 |
| `frostCurve` | SlimeFrostController | (0,0)(0.3,0)(1,1) | freezeT → 성애 강도 곡선 |
| `freezeTint` | SlimeFrostController | `#8FCFE8` | 동결 시 슬라임 base color |
| `_FrostNoiseScale` | Mat_SlimeBody3D_Quibli | 3.5 | 성애 노이즈 주파수 |
| `_FrostBaseColor` | Mat_SlimeBody3D_Quibli | `(0.92, 0.96, 1.0)` | 성애 결정 색 |
| `trailGradientCold` | WorldTrailManager | 한랭 blue | cold trail 색 그라디언트 |

## 테스트 절차

1. `SlimeJumping/Mechanism/Setup Cold Zone Test` 메뉴로 trigger box 생성 (`ColdZoneTest` root @ x=-8, y=4)
2. Play, 슬라임을 trigger 안으로 이동 → 8초간 점진 freeze 관찰
3. 외부로 이동 → 15초 thaw 관찰
4. Frozen 상태(T≈1)에서 trigger 밖 warm trail 위로 다시 지나가면 cold가 alpha-over로 덧칠되는지

## 메모리 영향

- World RT: ~16MB (pre 16MB → post 32MB at default 100×200×20ppu R8)
- Local RT: ~512KB/플랫폼 (×2). Stage2 사용 표면 수 × 0.5MB

## 회귀 안전성 (stage1)

ColdZone 미배치 시 freezeT 항상 0 → cold mask 전체 0 → cold gradient.alpha 1이어도 `coldMask × alpha = 0` → alpha-over lerp 무효. 명시적 black blit 후 미초기화 GPU 메모리 노출 없음.
