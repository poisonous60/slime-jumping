# 차징 & 발사 (판정원 모델)

스페이스 홀드로 슬라임 중심에 **판정원** 생성 → 조준점이 원 안에서 이동 → 슬라임 중심에서 조준점으로 가는 벡터의 **반대 방향**으로 발사.

## 원리
- 조준점은 자동으로 바깥으로 밀려나며, WASD로 2D 조작.
- 발사력은 중심에서 조준점까지의 거리에 비례.
- 안정도, 노드 수, 표면 종류 등 외부 계수 없음.

## 진입 경로
1. **일반 차징**: Attached에서 Space → `activeChargeRadius = chargeRadius`. 조준점 중심에서 시작.
2. **에너지 전환**: Airborne에서 Space 홀드 + 표면 충돌 → 판정원 확장. 상세는 [energy-conversion.md](energy-conversion.md).

## 조준점 이동 (aimOffset)
**자동 드리프트**:
```
radiusRatio = activeChargeRadius / chargeRadius
activeDriftSpeed = autoDriftSpeed * lerp(1, radiusRatio, aimDriftRadiusScale)
aimOffset += aimOffset.normalized * activeDriftSpeed * dt
```
- 초기 방향: 표면 법선 반대쪽 (표면을 향해 눌림 → 반대로 발사).

**WASD**: 2D 가속도. 바깥=가속, 안=감속(완전 상쇄 불가 — `inputAcceleration < autoDriftSpeed`), 좌우=각도.

**aimOffset은 슬라임 기준 상대 좌표** — 슬라임이 미끄러져도 판정원은 따라감.

**곡면 법선 추적**: `chargeNormalFollowSpeed`(rad/s)로 aim/driftDirection 회전 속도 제한 → 평지에서는 영향 없음.

**차징 중 표면 선택**: `CheckSurface`에 `inputX=0` 전달 → 입력 기반 tiebreaker 비활성 → 곡면에서 법선 안 튐.

## 발사 트리거
1. **스페이스 릴리즈** → 수동 발사. 현재 aimOffset 기준.
2. **가장자리 도달** → 자동 발사. `chargeFullAimTime`(0.1초) 유예 — 파워 확정(가장자리 고정), WASD로 방향만 변경. 유예 중 릴리즈 시 즉시 발사.

## 발사력 공식
```
ratio = aimOffset.magnitude / chargeRadius    // chargeRadius 기준 (activeChargeRadius 아님)
curvedRatio = ratio ^ chargeCurveExponent
# 일반 차징:
power = minLaunchPower + curvedRatio * (baseLaunchPower - minLaunchPower)
# 에너지 전환 (확장 판정원):
power = curvedRatio * baseLaunchPower         // minLaunchPower 미적용
direction = -aimOffset.normalized              // 압축 반대 방향
```
- activeChargeRadius가 크면 ratio > 1.0 → 초과 파워.

## 조준 관성
별도 파라미터 없이 기하학적으로 발생. 조준점이 가장자리 근처일 때 같은 WASD 입력으로 각도를 바꾸려면 원호를 크게 돌아야 함.

## 차징 규칙
- **캔슬 불가**: 스페이스 누르면 무조건 발사까지 진행. 커밋.
- **최소 발사력**: aimOffset이 minLaunchRatio 미만이면 최소 파워 클램프 후 발사.
- **발사 후 입력 무시**: 0.05초.
- **발사 후 에어 컨트롤 잠금**: `postLaunchAirControlLockTime`(0.15초). Hop/ForceAirborneWithVelocity 경로 제외.

## 차징 표면 프로브 히스테리시스
곡면/모서리에서 SurfaceProbe가 1~2 프레임 표면 감지를 놓칠 수 있음. 감지 실패 시 즉시 coyote로 빠지면 오실레이션 발생.

- `chargingSurfaceProbeGraceFrames`(3) 동안 캐시된 표면 데이터(법선, 접선, 콜라이더)로 valid 분기 유지.
- `surfaceProbeMissFrames` 카운터: 연속 miss, 유효 프레임마다 0 리셋.
- `cachedSurfaceCollider`: 마지막 유효 콜라이더. null 체크로 파괴된 오브젝트 방어.
- grace 중 `SnapToSurface()`는 캐시 콜라이더 `ClosestPoint` 사용.
- `isTouchingSurface`는 건드리지 않음 — 다른 코드 side effect 방지.

## 발사 후 표면 무시
발사 표면 콜라이더를 임시 무시 (`IgnoredColliderList.ReattachGuard` 모드).
- SurfaceProbe probe 필터만 적용, `Physics2D.IgnoreCollision` 호출 안 함 → 물리 충돌 유지 (낮은 점프/아래 발사에서도 바닥 관통 없음).
- 분리 거리(`slimeRadius + launchClearanceThreshold`) 초과 시 즉시 해제.
- 아니면 `maxIgnoreTime`(0.15초) 상한까지.

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| chargeRadius | 1.5 | 기본 판정원 반지름 |
| autoDriftSpeed | 1.5 | 자동 바깥 이동 속도 |
| inputAcceleration | 3.0 | WASD 가속도 |
| chargeFullAimTime | 0.1 | 풀차지 후 방향 조정 유예 (초) |
| baseLaunchPower | 15 | 풀파워 발사 속도 |
| minLaunchPower | 3 | 최소 발사 속도 |
| chargeCurveExponent | 1 | 차징 비율 → 파워 커브 지수 |
| maxAimAngularSpeed | 180 | 조준점 최대 각속도 (°/s) |
| chargeNormalFollowSpeed | 5 | 곡면 법선 추적 회전 속도 (rad/s) |
| chargingCoyoteTime | 0.2 | 차징 중 표면 이탈 유예 |
| coyoteGraceTime | 0.1 | 코요테 진입 직후 정지 유지 |
| chargingSurfaceProbeGraceFrames | 3 | 프로브 miss 허용 프레임 |
| postLaunchIgnoreTime | 0.05 | 발사 후 입력 무시 |
| launchClearanceThreshold | 0.05 | 발사 표면 무시 해제 거리 |
| maxIgnoreTime | 0.15 | 발사 표면 무시 최대 시간 |

## Launch 시 플랫폼 속도 상속
```
velocity = chargeDir * power                              // 차지 파워
velocity += carrier.GetEffectiveVelocity() * carrierFriction  // 플랫폼/탄력가지 팁 속도
velocity += lastSurfaceTangent * ... * tangentInheritanceFactor  // 하이퍼 대시
```
- Rope의 `GetMaxSagDistance() * reboundSpeedPerMeter`가 `launchDir`에 가산 (고무줄 반동).
- DragZone의 `launchMultiplier` × `exitMultiplier` 마지막에 적용.

## 관련
- [energy-conversion.md](energy-conversion.md)
- [hyper-dash.md](hyper-dash.md)
- [airborne.md](airborne.md)
- [stick-timer.md](stick-timer.md) — Charge Entry Grace
