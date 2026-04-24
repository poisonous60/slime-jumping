# 에너지 전환 (확장 판정원 모델)

공중에서 Space 홀드 + 표면 충돌 시, 운동량이 판정원 크기로 전환.

## 핵심
- Space 안 누르고 부딪히면: `velocity = 0`, Attached. 에너지 소멸.
- Space 누르고 부딪히면: Charging 직행, 충돌 속도에 비례해 판정원 확장. 초과 파워 가능.

## 충돌 처리 흐름
1. **에너지 보존 공식**으로 기본 중력 기준 속도 계산 (W/S 중력 조절 영향 제거):
```
heightDelta = launchHeight - currentHeight     // 떨어졌으면 양수
shadowSpeedSq = launchSpeed² + 2*|gravity|*heightDelta
impactSpeed = shadowSpeedSq > 0 ? sqrt(shadowSpeedSq) : 0
```
2. **Hop 에너지 가산**: 이번 비행이 Hop으로 시작(`launchedByHop`)이면 `shadowSpeedSq += hopConversionEnergyBonus * baseLaunchPower²` (풀 차징 점프 에너지 대비 비율).

3. **전환 rate 선택**:
```
rate = (conversionCount == 0) ? initialConversionRate : subsequentConversionRate
effectiveRate = sqrt(rate) * (chargeRadius / baseLaunchPower)
spawnDistance = impactSpeed * effectiveRate
```

4. **보너스 적용**:
- `launchSpeed == 0` (점프 없이 낙하): `spawnDistance *= notJumpConversionBonus`.
- `launchedByHop`: `spawnDistance *= hopConversionBonus`.

5. **판정원 확정**:
```
activeChargeRadius = max(spawnDistance, chargeRadius)
aimOffset = Vector2.zero                // 항상 중심에서 시작
velocity = Vector2.zero                 // 운동량 전량 전환 (포고스틱)
crawlVelocity = ComputeLandingCrawlVelocity(...)  // 표면 속성에 따라 착지 관성 보존
impactTangentSpeed = tangentSpeed       // 스틱 감속용
chargeEntryCrawl *= hyperDashRetention  // 전환 크기 비례 감쇠
conversionCount++
```
- `conversionCount`는 바닥 착지(Attached)에서만 리셋. Wall chain에서 `subsequentConversionRate` 유지가 의도된 동작.

## 강약 분기
- **약한 충돌** (`spawnDistance ≤ chargeRadius`): 일반 차징과 동일 (기본 판정원, 중심에서 시작).
- **강한 충돌** (`spawnDistance > chargeRadius`): 판정원 확장. 조준점은 중심에서 시작, 자동 드리프트로 확장 가장자리까지 이동.

## 드리프트 보상 (aimDriftRadiusScale)
확장된 판정원에서 드리프트 속도를 얼마나 보상할지:
```
activeDriftSpeed = autoDriftSpeed * lerp(1, radiusRatio, aimDriftRadiusScale)
```
| aimDriftRadiusScale | 반지름 2배일 때 | 차징 시간 |
|-------------------|----------------|-----------|
| 1.0 | 드리프트 2배 | 동일 |
| 0.7 | 드리프트 1.7배 | 약 18% 증가 |
| 0.0 | 드리프트 불변 | 2배 |

## 트레이드오프
- **안전 루트**: Space 안 누르고 착지 → Attached → 일반 차징. 파워/방향 완전 제어.
- **스피드 루트**: Space 홀드 + 빠르게 충돌 → 확장 판정원 → 초과 파워 가능하지만 차징 시간 약간 증가.
- 접근 각도는 에너지 전환량에만 영향. 조준점 초기 방향은 항상 표면 법선 기준.

## 사전 커밋 원칙
부딪히기 전에 Space를 누를지 말지 미리 결정해야 함. 예측하고 커밋한 플레이어가 보상받음. 차징 캔슬 불가 원칙과 일관.

## 코요테 창
`energyConversionCoyoteTime`(0.12초): 스페이스 없이 착지한 뒤 이 시간 이내 스페이스 누르면 Space-held 착지와 동일한 확장 판정원 부여. 0이면 비활성.
- 활성 중 시각적으로 `coyoteSquashCurve`를 따라 서서히 눌리다 유예창 종료 순간 피크, 이후 `coyoteSquashRecoveryTime` 동안 복구.

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| initialConversionRate | 2.25 | 첫 전환 rate (높을수록 확장 큼) |
| subsequentConversionRate | 0.9 | 연속 전환 rate |
| notJumpConversionBonus | 1.3 | 낙하 전환 spawnDistance 배율 |
| hopConversionBonus | 1.0 | Hop 비행 착지 spawnDistance 배율 (1=보너스 없음) |
| hopConversionEnergyBonus | 0.0 | Hop 비행 착지 shadowSpeedSq 가산 비율 |
| aimDriftRadiusScale | 0.7 | 확장 시 드리프트 보상 (1=시간 동일, 0=시간 비례) |
| energyConversionCoyoteTime | 0.12 | 착지 후 Space 유예 |

## 관련
- [charge-launch.md](charge-launch.md)
- [airborne.md](airborne.md)
- [hyper-dash.md](hyper-dash.md)
