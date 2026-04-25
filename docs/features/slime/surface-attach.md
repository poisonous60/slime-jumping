# 표면 부착 & 감지

## 부착 판정
- 원형 콜라이더가 표면에 접촉하면 Attached 진입.
- 착지 시 `velocity = 0` (점성 슬라임 — 모든 운동량 흡수).
- **천장 히스테리시스**: 탈출 `surfaceUp < ceilingThreshold`(-0.3, ~107°), 진입 `surfaceUp >= ceilingReattachThreshold`(-0.2, ~101°). 사이 구간에서는 현재 상태 유지 → 구체 표면에서 상태 떨림 방지.

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
- **Phase B — Primary Pull**: primary와 거리가 `slimeRadius + contactSkin` 초과 시에만 끌어당김. 코너에서는 미발동.
- `rb.position` 직접 설정.

## 표면 전환 감지
`FixedUpdate`에서 `surfaceCollider != prevSurfaceCollider`일 때 `carrier.Attach` + `UpdateSurfaceModifiers` 재호출.

## 곡면 떨림 (Curve Judder) 해결 히스토리
Multi-probe 모드에서 Ice / 저-decel 표면을 빠르게 슬라이드할 때 슬라임 위치가 매 프레임 1~3px 진동하던 문제. 진단 (IceMultiProbeDiagnostic) 으로 메커니즘 파악 후 4단계 누적 적용:

1. **EffectiveNormal soft band** — `effectiveNormalSoftBandMultiplier=8`. active toggle 의 step discontinuity 를 smoothstep ramp 로 변환 → contact 가 boundary 를 넘나들어도 가중치가 연속.
2. **Project same-direction filter** — `projectVelocitySameDirectionDot=0.7`. 같은 곡면의 multi-probe sample 끼리는 clip 하지 않음 → vy 가 active set 토글마다 ±5 m/s 로 튀는 패턴 제거.
3. **SurfaceNormal 항상 smoothing** — 회전 플랫폼 전용이던 IIR 필터를 정적 곡면에도 적용. surfNΔ° per frame 12~22° → 1~3°.
4. **Multi-probe Phase A net push 검증** — sum push 로 over-push 가설은 진단 결과 기각 (실제 netPushMag < 0.005).

이후 잔여 미세 떨림은 카메라 측 smoothing 으로 잡으려 시도 (`SlimeCameraTargetProxy`) 했으나 IIR catch-up 진동이 더 크게 보여 **rollback**. 카메라는 Cinemachine PositionComposer + Damping=0 직접 추적 유지. 슬라임 rb.position 자체의 잔여 떨림은 받아들임.

설정 위치: [SlimeSettings.cs](../../../SlimeJumping_v2/Assets/Scripts/Settings/SlimeSettings.cs) `Surface Probe` / `Surface Normal Smoothing` 섹션.

진단 도구: [IceMultiProbeDiagnostic.cs](../../../SlimeJumping_v2/Assets/Scripts/Debug/IceMultiProbeDiagnostic.cs) — 슬라임 GameObject 에 Add Component 후 F3 으로 ring-buffer dump. surfNΔ°, contact churn, netPushMag, per-contact pen 가시화.

## 관련 파일
- [SurfaceProbe.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceProbe.cs) — 추상 베이스, EffectiveNormal / Project / Phase B
- [SingleContactSurfaceProbe.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SingleContactSurfaceProbe.cs) — collider당 단일 ClosestPoint
- [MultiContactSurfaceProbe.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/MultiContactSurfaceProbe.cs) — N-probe ring sampling
- [SurfaceProbeMode.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceProbeMode.cs) — enum
- [IgnoredColliderList.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/IgnoredColliderList.cs) — ReattachGuard / Passthrough 모드 분리
- [SlimeSettings.cs](../../../SlimeJumping_v2/Assets/Scripts/Settings/SlimeSettings.cs) — `surfaceProbeMode`, `surfaceProbeSamples`, `surfaceProbeRingScale`, `surfaceProbeDedupeDot`, `effectiveNormalSoftBandMultiplier`, `effectiveNormalWeightRampBand`, `projectVelocitySameDirectionDot`, `surfaceNormalSmoothRate`, `surfaceNormalSmoothRotatingMultiplier`, `surfaceNormalSmoothAngularThreshold`
