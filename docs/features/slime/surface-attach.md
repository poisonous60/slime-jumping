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

## Effective Normal (continuous physics)
slide/crawl/aim/impact는 모든 active 비천장의 침투-가중 평균 법선:
```
effectiveNormal = Σ(n_i × max(0, radius + contactSkin − dist_i))  / |Σ|
```
분모≈0 또는 비천장 active 0개면 primary normal fallback. 단일 contact에서는 primary == effective → 기존 동작 bit-identical.

## Multi-constraint Velocity Clipping
`ProjectVelocityAgainstContacts(ref v, fallback)`: 모든 active contact(천장 포함)에 대해 `v -= n · max(0, -v·n)` 2-pass Gauss-Seidel. 코너 러버밴딩 해결.

## 표면 스냅 (2-Phase Depenetration)
- **Phase A — Depenetration**: OverlapCircle로 콜라이더 모은 뒤 각 콜라이더 ClosestPoint 기반 침투 판단 push (최대 3회). Minkowski boundary로 코너 수렴.
- **Phase B — Primary Pull**: primary와 거리가 `slimeRadius + contactSkin` 초과 시에만 끌어당김. 코너에서는 미발동.
- `rb.position` 직접 설정.

## 표면 전환 감지
`FixedUpdate`에서 `surfaceCollider != prevSurfaceCollider`일 때 `carrier.Attach` + `UpdateSurfaceModifiers` 재호출.

## 관련 파일
- [SurfaceProbe.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceProbe.cs)
- [IgnoredColliderList.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/IgnoredColliderList.cs) — ReattachGuard / Passthrough 모드 분리
