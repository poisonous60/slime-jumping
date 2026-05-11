# 표면 부착 & 감지

## 부착 판정
- 원형 콜라이더가 표면에 접촉하면 Attached 진입.
- 착지 시 `velocity = 0` (점성 슬라임 — 모든 운동량 흡수).
- **천장 히스테리시스**: 탈출 `surfaceUp < ceilingThreshold`(-0.3, ~107°), 진입 `surfaceUp >= ceilingReattachThreshold`(-0.2, ~101°). 사이 구간에서는 현재 상태 유지 → 구체 표면에서 상태 떨림 방지.
  - 두 임계값은 `SurfaceMaterial.overrideCeilingAngle` 로 표면 단위 오버라이드 가능. 컨택트별로 자기 표면의 임계값을 사용 → 끈끈이 표면(180°=오버행 부착) / 미끄러운 표면(예: 95°에서 천장) 등 표현. 기본 표면은 SlimeSettings 글로벌 값.

## 표면 감지 (CheckSurface)
1. `OverlapCircleNonAlloc(pos, slimeRadius + detectRange, mask)` — 주변 콜라이더 수집 (`ignoredColliders` 필터링).
2. 각 콜라이더에 `ClosestPoint(pos)` → `(pos - closest).normalized` 법선, `.magnitude` 거리.
3. 깊은 침투 시(`toSlime.sqrMagnitude < 0.0001f`) skip.
4. **Active contacts**: `distance ≤ slimeRadius + contactSkin`.

### Probe Mode (Single / Multi)
`SurfaceProbe` 추상 베이스 + `SingleContactSurfaceProbe` / `MultiContactSurfaceProbe` 두 구현. 런타임 토글 가능 (`SlimeController.SetProbeMode`, F1 키 디버그 토글).
- **Single**: 콜라이더당 `ClosestPoint(slimeCenter)` 1회 — 기본 / 가장 가벼움. multi-path polygon (SnakeSpine 가지, DeformablePlatform 섬) 한쪽 분기 누락.
- **Multi**: 콜라이더당 N개 probe (slime 중심 주위 ring, 반지름 `slimeRadius × surfaceProbeRingScale`)에서 각각 `ClosestPoint(probe_k)` 호출. 같은 면에서 나온 sample 들은 dedupeDot(0.97) 기반 머지. multi-path polygon 분기 모두 감지.

런타임 mode 스왑 시 `TransferStateFrom` 으로 `lastGoodNormal` / gravity context 인계 → 천장/벽 부착 상태 유지.

### Buried-collider Rejection (Multi 전용, opt-in)
`surfaceProbeRejectBuriedContacts=true` 시 — 두 collider 가 겹쳐있을 때 **다른 collider 내부에 묻힌 edge contact** 를 reject. 의도: 슬라임이 union 의 가장 바깥 silhouette 만 닿게.

**판정**: 각 edge 의 closest point 를 ownerCol 안쪽으로 `bias = min(setting, dist × 0.4)` 만큼 밀고, 그 testPoint 가 다른 nearby collider 의 `OverlapPoint` true 면 buried.
- bias 가 dist 비례로 자동 축소 → 슬라임이 표면에 밀착했을 때 testPoint 가 ownerCol 을 빠져나가 옆 collider 로 잘못 침범하는 걸 방지.
- 기존 outward filter (`Dot(slime − closest, outward) > 0`) 와 **함께** 동작 — outward 는 단일 collider 의 vertex-clamp artifact 차단, buried 는 다중 collider overlap 차단.
- Trigger collider (DragZone, WaterZone 등) 는 OTHER 후보에서 제외.

**Fallback**: 한 collider 의 모든 edge 가 reject 되면 (= 완전 buried) 그 collider 에서 가장 가까운 1개를 부활시켜 contact 으로 promote. 부활된 contact 는 `clamped=false` + outward normal 만 사용 → EffectiveNormal / lastGoodNormal 이 stale 데이터로 새지 않음. Airborne 박힘 회귀 (docs/faceted-terrain-jitter-attempts.md 후속 회귀 섹션) 차단.

**적용 범위**: Contact 수집 (`PopulateContacts`) + Phase A depenetration push (`DepenetratePass`) 둘 다. Push 경로는 fallback 없음 — 묻힌 collider 가 슬라임을 미는 게 의도와 반대.

## Primary Selection (5-step geometric fallback)
비천장 active 중 1개 선택:
1. 단일 active → 그것 (~99% 케이스).
1.5. **천장 프리필터**: 천장(`surfaceUp < ceilingThreshold`)과 비천장 혼재 시 천장을 후속 단계에서 제외.
1.7. **IPlatformEffect 우선권**: 비천장 active 중 정확히 1개가 effect(JumpPad 등) 가지면 그것. effect 발동 누락 방지.
2. **연속성 히스테리시스**: 이전 primary가 active 비천장에 있고, 다른 contact가 `primaryContactHysteresis`(0.005) 이상 앞서지 않으면 유지. 프레임간 flip 억제.
3. 가장 깊은 침투 (> 0.001m).
4. 입력 방향 tiebreaker (Charging에서는 inputX=0으로 비활성).
5. Gravity alignment (fallback).

Primary는 `surfaceCollider` / `surfaceMod` / `carrier` / `IPlatformEffect` / `OnLanded` collider 인자에만 사용.

## Effective Normal (continuous physics, soft band)
slide/crawl/aim/impact 는 비천장 contact 의 침투-가중 평균 법선. **soft band** 안에 들어오는 모든 contact 가 `smoothstep` 비례 가중치로 기여 (active/inactive binary 게이트 제거):
```
fullBand    = contactSkin × effectiveNormalSoftBandMultiplier   (default 8 → 0.16 unit)
rampWidth   = fullBand × effectiveNormalWeightRampBand          (default 1.0)
w_i         = (radius + fullBand) − dist_i             (≤ 0 이면 skip)
weight_i    = smoothstep(0, rampWidth, w_i) × w_i
effectiveNormal = Σ(n_i × weight_i) / |Σ|
```
band 안 contact 0개 → `lastGoodNormal` fallback.

**왜 soft band 인가**: hard active 게이트 (`dist ≤ radius + contactSkin`) 는 step function 이라, 곡면 슬라이드 시 multi-probe sample 들이 매 프레임 boundary 를 넘나들면서 EffectiveNormal 이 ±10° 씩 떨림. 결과적으로 `velocity = surfaceTangent × speed` 가 매 프레임 다른 방향 → 시각적 진동. soft band 는 contact 가 멀어질수록 weight 가 0 으로 부드럽게 감소하므로 active toggle 의 step discontinuity 를 제거.

## Multi-constraint Velocity Clipping (Same-Direction Filter)
`ProjectVelocityAgainstContacts(ref v, fallbackNormal)`:
1. **항상 실행**: `fallbackNormal` (= smoothed `surfaceNormal`) 에 대해 full clip — primary axis 의 안정적인 "표면으로 안 들어감" 제약.
2. **multi-clip**: contactCount 안 contact 중 `dot(n_i, fallbackNormal) > projectVelocitySameDirectionDot` (default 0.7, ~45°) 인 것은 **skip**. 같은 곡면의 multi-probe sample 은 noise 일 뿐 — 이들에 clip 하면 active set 토글에 따라 vy 가 매 프레임 다른 값으로 깎임. dot < threshold 인 진짜 corner / opposing wall 만 soft-banded weighted clip 으로 처리.

2-pass Gauss-Seidel. 코너 러버밴딩 보존하면서 곡면 multi-sample 떨림 제거.

## SurfaceNormal Low-Pass Filter
`SlimeController.FixedUpdate` 에서 `EffectiveNormal` 을 `surfaceNormal` 에 쓰기 전 1차 IIR 필터:
```
k = 1 − exp(−smoothRate × dt)
surfaceNormal = lerp(prevSmoothedNormal, raw, k)
```
- `surfaceNormalSmoothRate` (default 20) — 정적 표면 포함 항상 적용 (50Hz 물리 기준 frame 당 ~33% 갱신).
- `surfaceNormalSmoothRotatingMultiplier` (default 1.0) — 회전 플랫폼 (`PlatformMotor.AngularVelocity > surfaceNormalSmoothAngularThreshold`) 시 rate 에 곱해짐. 더 강한 smoothing 원하면 2~3.
- 착지 프레임 (`prev !isTouching`) 에는 raw 사용 → 첫 프레임 lag 없음.

## 표면 스냅 (2-Phase Depenetration)
- **Phase A — Depenetration**: OverlapCircle로 콜라이더 모은 뒤 각 콜라이더 ClosestPoint 기반 침투 판단 push (최대 3회). Minkowski boundary로 코너 수렴.
  - **콜라이더당 통합**: Multi-probe 모드는 unique-face contact 들을 dedupe 한 뒤, `depenetrationUseSmoothedAggregation=true` (default) 시 `EffectiveNormal` 과 동일한 soft-band smoothstep weight 로 **단일 push** 산출 — `dir = (Σ n_p × w_p).normalized`, `mag = Σ depth_p × w_p / Σ w_p`. chord junction 에서 push 방향이 매 프레임 ±20° 튀던 패턴 제거. `false` 로 끄면 legacy `Σ n_p × depth_p` (합 벡터).
  - Single-probe 모드는 콜라이더당 contact 1 개 → 통합 무관.
- **Phase B — Primary Pull**: primary 콜라이더의 `ClosestPoint(pos)` + 법선 × radius 로 매 프레임 anchor (`dist > radius + contactSkin` 인 경우에만 적용 → skin tolerance 안에서는 no-op). PolygonCollider2D 의 ClosestPoint 자체는 edge 사이를 부드럽게 미끄러지므로 (vertex 에서 edge normal 만 flip, closest point 는 연속) chord-junction kick 없음. Phase A 만 쓰면 "막 침투 → push" / "boundary 위 → no-op" 가 매 프레임 번갈아 나오면서 50Hz magnitude 진동이 생김 → 매 프레임 anchor 로 제거.
- `rb.position` 직접 설정.

## 표면 전환 감지
`FixedUpdate`에서 `surfaceCollider != prevSurfaceCollider`일 때 `carrier.Attach` + `UpdateSurfaceModifiers` 재호출.

## 곡면 떨림 (Curve Judder) 해결 히스토리
Multi-probe 모드에서 Ice / 저-decel 표면을 빠르게 슬라이드할 때 슬라임 위치가 매 프레임 1~3px 진동하던 문제. 진단 (IceMultiProbeDiagnostic) 으로 메커니즘 파악 후 5단계 누적 적용:

1. **EffectiveNormal soft band** — `effectiveNormalSoftBandMultiplier=8`. active toggle 의 step discontinuity 를 smoothstep ramp 로 변환 → contact 가 boundary 를 넘나들어도 가중치가 연속.
2. **Project same-direction filter** — `projectVelocitySameDirectionDot=0.7`. 같은 곡면의 multi-probe sample 끼리는 clip 하지 않음 → vy 가 active set 토글마다 ±5 m/s 로 튀는 패턴 제거.
3. **SurfaceNormal 항상 smoothing** — 회전 플랫폼 전용이던 IIR 필터를 정적 곡면에도 적용. surfNΔ° per frame 12~22° → 1~3°.
4. **Multi-probe Phase A net push 검증** — moderate-speed 영역에서는 sum push over-push 가설은 진단 결과 기각 (netPushMag < 0.005). 그러나 v ≈ 14 m/s × R ≈ 2.5 m 의 U-bottom 에서는 chord junction 마다 sum push 가 0.005~0.02 로 spike 하고 방향이 (0.50, 0.87) → (0.69, 0.72) → (0.57, 0.82) 로 매 프레임 횡진동 → 시각적 "덜컹".
5. **Multi-probe Phase A smoothed aggregation** (`depenetrationUseSmoothedAggregation=true`, default ON) — Phase A 가 같은 콜라이더의 unique-face contact 들을 EffectiveNormal 과 동일한 soft-band smoothstep weight 로 통합. push 방향 = 가중 평균 법선 (smooth bisector), magnitude = 가중 평균 침투 깊이. chord 사이를 슬라이드해도 두 입력이 모두 slime 위치의 연속함수라 push 가 매 프레임 일관 → 횡진동 제거. SurfaceProbe.cs 에 `LastDepenPushDir` / `LastDepenPushMag` 진단 필드도 추가 (last-iter push 표시), IceMultiProbeDiagnostic 가 컬럼으로 노출.
6. **Phase B unconditional anchor** — Phase A push 여부에 무관하게 매 프레임 primary `ClosestPoint(pos) + n × radius` 로 anchor (skin tolerance 적용). 5번 단독으로는 "penetrating → push" / "on boundary → no push" 패턴이 50Hz magnitude oscillation 을 만듦. PolygonCollider2D 의 ClosestPoint 가 edge 사이에서 연속이므로 anchor 자체는 부드러움.

설정 위치: [SlimeSettings.cs](../../../SlimeJumping_v2/Assets/Scripts/Settings/SlimeSettings.cs) `Surface Probe` / `Surface Normal Smoothing` 섹션.

진단 도구: [IceMultiProbeDiagnostic.cs](../../../SlimeJumping_v2/Assets/Scripts/Debug/IceMultiProbeDiagnostic.cs) — 슬라임 GameObject 에 Add Component 후 F3 으로 ring-buffer dump. surfNΔ°, contact churn, netPushMag, per-contact pen 가시화.

## 관련 파일
- [SurfaceProbe.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceProbe.cs) — 추상 베이스, EffectiveNormal / Project / Phase B
- [SingleContactSurfaceProbe.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SingleContactSurfaceProbe.cs) — collider당 단일 ClosestPoint
- [MultiContactSurfaceProbe.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/MultiContactSurfaceProbe.cs) — N-probe ring sampling
- [SurfaceProbeMode.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceProbeMode.cs) — enum
- [IgnoredColliderList.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/IgnoredColliderList.cs) — ReattachGuard / Passthrough 모드 분리
- [SlimeSettings.cs](../../../SlimeJumping_v2/Assets/Scripts/Settings/SlimeSettings.cs) — `surfaceProbeMode`, `surfaceProbeSamples`, `surfaceProbeRingScale`, `surfaceProbeDedupeDot`, `effectiveNormalSoftBandMultiplier`, `effectiveNormalWeightRampBand`, `depenetrationUseSmoothedAggregation`, `projectVelocitySameDirectionDot`, `surfaceNormalSmoothRate`, `surfaceNormalSmoothRotatingMultiplier`, `surfaceNormalSmoothAngularThreshold`
