# 스틱 타이머 (착지 후 표면 고정)

벽/급경사에 공중에서 착지할 때 충돌 속도에 비례한 시간 동안 슬라이드 없이 고정. 점성 슬라임이 철퍼덕 붙는 느낌.

## 적용 시점
- Airborne → Attached 착지.
- Airborne → Charging 에너지 전환 착지.
- Attached → Charging (Charge Entry Grace — 아래 참조).

## 지속시간 계산 (수렴 커브)
```
if (surfaceUp >= stickSlopeThreshold)  // 바닥
    stickTimer = 0
else
    t = 1 - 1 / (1 + impactSpeed * stickSpeedScale)   // 0~1 수렴
    stickTimer = Lerp(minStickDuration, maxStickDuration, t)
```
- 수렴 커브: `impactSpeed`가 아무리 커져도 `maxStickDuration` 초과 불가.
- `stickSpeedScale`: 커브 기울기. 크면 적은 속도에서도 빨리 max.

## 3-Timer 협력
서로 직접 간섭하지 않는 독립 타이머들:

| 타이머 | 방향 | 역할 |
|--------|------|------|
| `stickTimer` | 카운트다운 | > 0이면 슬라이드 억제 |
| `stickDeccelTimer` | 카운트업 (0 → `stickDecelerationTime`) | `impactTangentSpeed` → 0 lerp 후 현재 `surfaceTangent`에 재투영 |
| `slideRampTimer` | 카운트업 (0 → `stickSlideTransitionRampTime`) | 만료 후 slideSpeed 0→전체 ramp-up |

**동작 시퀀스**:
```
착지
  stickTimer = N (충돌 속도 비례)
  stickDeccelTimer = 0 (감속 시작)
  impactTangentSpeed = 현재 접선 속도 (부호 있는 스칼라)
  wasStuck = true

[stickDeccelTimer 구간]
  velocity = surfaceTangent * Lerp(impactTangentSpeed, 0, t)  // 접선 공간 감속, 곡면에서도 표면 추종

[stickTimer 잔여 구간]
  velocity = zero  // 정지 상태, 슬라이드 억제

[stickTimer 만료 → wasStuck 감지]
  slideRampTimer = 0  // ramp 시작

[slideRampTimer 구간]
  slideSpeed *= Clamp01(slideRampTimer / rampTime)
```
`stickTimer`를 연장해도 `stickDeccelTimer`/`slideSpeed`에 직접 영향 없음 — 셋은 완전 분리.

## 상태별 동작
- **Attached**: 고정 중 크롤 속도에 `stickCrawlMultiplier`(0.2) 배율 (0=완전 차단, 1=정상).
- **Charging**: 고정 중에도 조준 가능 — 슬라이드만 멈춤.

## Charge Entry Grace
Attached에서 Space로 Charging 진입 시:
- `stickTimer += chargeEntryStickBonus` (가산, 기존 stick 위에 누적 — 플레이어 커밋 보상).
- `impactTangentSpeed = 현재 접선 속도`, `stickDeccelTimer = 0`, `slideRampTimer = 0`, `wasStuck = true`.
- 슬라이드 중이던 슬라임이 잠시 표면을 붙잡는 효과.
- 차징 중 슬라이드 중력 접선 성분에 `chargingSlideGravityScale`(0.5) 배율 적용 → 스틱 만료 후 완만한 재가속.

## 표면별 배율
- `stickDurationMultiplier`: 스틱 시간 배율 (0=빙판).
- `landingVelocityRetention`: 착지 시 접선 속도 보존 비율 (0=즉시정지, 0.8=빙판).
- `wallAttachSpeedDampen`: 착지 접선 속도 감쇠.
- `impactRedirectRatio`: 법선 충돌 속도 → 접선 전환 (빙판 벽 수평 충돌 → 위로 미끄러짐).

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| minStickDuration | 0.3 | 최소 스틱 시간 (낮은 충돌 속도) |
| maxStickDuration | 0.6 | 최대 스틱 시간 |
| stickSpeedScale | 0.1 | 수렴 커브 기울기 |
| stickDecelerationTime | 0.05 | 착지 관성 감속 시간 |
| stickSlopeThreshold | 0.7 | 이상 surfaceUp = 바닥 (스틱 없음) |
| stickCrawlMultiplier | 0.2 | 스틱 중 크롤 배율 |
| stickSlideTransitionRampTime | 0.2 | slide 0→전체 램프업 |
| chargeEntryStickBonus | 0.2 | Attached→Charging 진입 시 가산 |
| chargingSlideGravityScale | 0.5 | 차징 중 슬라이드 중력 배율 |

## 관련
- [charge-launch.md](charge-launch.md)
- [crawling.md](crawling.md)
- [surface-material.md](surface-material.md)
