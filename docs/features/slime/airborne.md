# 공중 제어 (Airborne)

## 중력
키네마틱. 매 프레임 `velocity += GravityVector * dt`. (GravityField 내부에서는 방향이 치환됨 — [../mechanisms/gravity-field.md](../mechanisms/gravity-field.md))

**상승/하강 비대칭**: `risingGravityScale`, `fallingGravityScale` 곱셈. 기본값 둘 다 1.0.

## 에어 컨트롤 활성 조건
- **Apex 게이트**: `velocity.y <= apexThreshold`(0)일 때만 A/D 수평 제어 활성. 상승 중에는 A/D 비활성.
- **Post-launch 잠금**: 차징 발사 직후 `postLaunchAirControlLockTime`(0.15초) 동안 에어 컨트롤 비활성. Hop/JumpPad 등 `ForceAirborneWithVelocity` 경로는 예외.
- **유체(`InFluid`) 중**: Apex 게이트와 Post-launch 잠금 모두 우회.

W/S 중력 조절은 apex 게이트와 **무관** — 항상 적용.

## A/D 수평 가감속
```
targetVx = inputX * airMaxHorizontalSpeed
if (이미 상한 초과)  → 반대 방향 입력만 적용 (브레이킹)
else                 → MoveTowards(velocity.x, targetVx, accel/decel * dt)
```

## W/S 중력 조절
수직 속도 부호(`isRising = dot(velocity, gravityUp) > 0`)로 상승/하강 분기:
```
W 홀드: gravityScale = isRising ? floatGravityScaleRising(1.0) : floatGravityScaleFalling(0.5)   // 체공
S 홀드: gravityScale = isRising ? diveGravityScaleRising(1.0) : diveGravityScaleFalling(2.0)     // 급강하
미입력: gravityScale = 1.0
실제 중력: gravity * gravityScale * risingGravityScale or fallingGravityScale
```
Rising 기본값 1.0이면 "상승 중엔 W/S 효과 없음" 유지. 값 바꾸면 상승 페이즈에서도 조절 가능.

**유체(`InFluid`) 중**: 수직 속도 부호 무관 `floatGravityScaleInFluid`(0.5) / `diveGravityScaleInFluid`(2.0)만 사용. 부력 기반 이동에는 뚜렷한 apex가 없어 단일화.

## 에너지 전환 보정
W/S 중력 조절이 에너지 전환을 왜곡하지 않도록, 착지 시 **에너지 보존 공식**으로 "기본 중력이었으면 이 속도" 계산:
```
shadowSpeedSq = launchSpeed² + 2 * |gravity| * heightDelta
```
- 에어 컨트롤은 속력 보존 → 에너지에 영향 없음. 유일한 변수는 높이 차이.
- W로 정상 중력 도달 불가 높이에 도착 → shadowSpeedSq < 0 → 에너지 전환 0.

## 착지 분기
| 조건 | 결과 |
|------|------|
| 천장 (`surfaceUp < ceilingReattachThreshold`) | 부착 안 함. 법선 속도 상쇄, Airborne 유지 |
| Space 홀드 + 실제 비행 (체공 > 0.1초) | 에너지 전환 → Charging 직행 |
| Space 미홀드 + 실제 비행 | velocity = 0, Attached 전환, 스틱 타이머 적용 |
| 코너 튕김 (체공 < 0.1초) | 법선 속도만 상쇄, Attached 전환 |

## 착지 시 상대속도 계산
`relativeVel = velocity - landingPlatformVel`을 기준으로 tangentSpeed, normalSpeed, impactVelocity, retention, redirect를 모두 계산. 이동 플랫폼에서 속도 이중 적용 방지.
- `landingPlatformVel`은 선형 + 회전 접선 속도 포함 (`GetSurfaceVelocity` 헬퍼).
- `ComputeLandingCrawlVelocity` 헬퍼가 두 착지 경로(Attached/Charging)에서 공유.

## 외부 힘 (바람/부력)
`AddExternalVelocity(deltaV)` 가산 채널, `MultiplyVelocity(factor)` 곱셈 채널은 Airborne에서 velocity에 직접 적용. 상세는 [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md).

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| gravity | -20 | 키네마틱 중력 |
| airMaxHorizontalSpeed | 5 | 수평 최대 속도 |
| airAcceleration | 20 | 수평 가속 |
| airDeceleration | 15 | 수평 감속 |
| floatGravityScaleRising | 1.0 | W 홀드 상승 중 |
| floatGravityScaleFalling | 0.5 | W 홀드 하강 중 |
| diveGravityScaleRising | 1.0 | S 홀드 상승 중 |
| diveGravityScaleFalling | 2.0 | S 홀드 하강 중 |
| floatGravityScaleInFluid | 0.5 | 수중 W 홀드 |
| diveGravityScaleInFluid | 2.0 | 수중 S 홀드 |
| risingGravityScale | 1 | 상승 중 배율 |
| fallingGravityScale | 1 | 하강 중 배율 |
| apexThreshold | 0 | 이 이하 velocity.y에서 에어 컨트롤 활성 |
| postLaunchAirControlLockTime | 0.15 | 발사 후 에어컨트롤 잠금 |

## 관련
- [energy-conversion.md](energy-conversion.md)
- [stick-timer.md](stick-timer.md)
- [../mechanisms/gravity-field.md](../mechanisms/gravity-field.md)
