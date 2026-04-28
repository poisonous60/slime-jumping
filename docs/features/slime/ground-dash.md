# 그라운드 대시 (A/D 더블탭 순간 부스트)

Attached 상태에서 A 또는 D를 짧은 간격으로 두 번(`horizontalDoubleTapWindow` 0.25초 이내) 누르면 `crawlVelocity`에 순간 impulse 추가. **표면 부착 유지** — 실제 점프 아님.

## 동작
- A 또는 D 키의 두 번째 rising edge에서 발동(같은 키여야 함, A→D 같은 교차 입력은 발동 안 함).
- 두 번째 키가 눌린 시점에 해당 키가 곧 inputX 부호를 결정 → 그 방향으로 dash.

```
if (doubleTap(A or D) && cooldown <= 0 && |inputX| > 0)
    angleScale = 1 - InverseLerp(groundDashAngleMin, groundDashAngleMax, surfaceAngle)
    crawlVelocity += sign(inputDir) * groundDashImpulse * angleScale * surfaceMod.groundDashScale
    cooldown = groundDashCooldown
```

## 버퍼
- **입력 버퍼**: 더블탭이 먼저 발화되고 `groundDashInputBuffer`(0.1초) 안에 A/D가 다시 눌리거나 유지되면 발동(double-tap 두 번째 누름이 곧 방향 입력이므로 거의 항상 즉시 발동됨).
- **코요테 버퍼**: Airborne 중 A/D 더블탭 시 저장, 착지 시 A/D 홀드 중이면 발동.

## 속도 감쇠 (Hop-dash chain 방지)
같은 방향 crawl 속도가 `dashFalloffStart`(5) 이상이면 impulse 선형 감소, `dashFalloffEnd`(13)에서 0. 반대 방향 dash는 제약 없음.

## 쿨타임 & 기울기
- `groundDashCooldown`(0.3초) 재발동 불가. Wall-clock 기반 — Airborne/Charging 중에도 경과.
- `groundDashAngleMin`(30°)~`groundDashAngleMax`(60°) 사이 1→0 보간. 평지=풀 부스트, 급경사=불가.

## 표면별
- `SurfaceMaterial.groundDashScale` 배율 (기본 1.0, 0=대시 불가). 항상 곱해짐.
- `SurfaceMaterial.overrideGroundDash = true` 시 `groundDashImpulse`, `groundDashAngleMin`, `groundDashAngleMax`, `dashFalloffStart`, `dashFalloffEnd`가 SlimeSettings 글로벌 대신 SurfaceMaterial 값으로 치환됨. 추가로 두 AnimationCurve가 비율 매핑을 결정:
  - `groundDashAngleFalloffCurve`: 표면 각도 → angleScale. 도메인은 [angleMin, angleMax] 정규화.
  - `dashSpeedFalloffCurve`: 동방향 접선 속도 → falloff. 도메인은 [dashFalloffStart, dashFalloffEnd] 정규화.
  - 둘 다 기본은 (0,1)→(1,0) 선형이라 레거시 동작 유지.
- `SurfaceMaterial.disableDashFalloff = true` (override 필요) 시 속도 falloff 자체를 건너뜀 — 동방향 속도와 무관하게 매 대시 풀 임펄스. 빙판처럼 속도가 무한 누적되어야 하는 표면용.

## 입력 우선순위
Charging > W(hop) > A/D 더블탭(dash). Space 홀드 착지 시 둘 다 소비, hop 버퍼 활성 시 dash 버퍼 소비.

## 헬퍼
`ApplyGroundDash(float inputSign)` — FixedUpdateAttached 대시 로직과 착지 버퍼 경로에서 공유.

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| groundDashImpulse | 8 | crawlVelocity에 더하는 순간 속도 |
| groundDashCooldown | 0.3 | 쿨타임 (초) |
| groundDashInputBuffer | 0.1 | 입력 버퍼 시간 |
| groundDashAngleMin | 30 | 이 각도 이하에서 풀 대시 |
| groundDashAngleMax | 60 | 이 각도 이상에서 대시 불가 |
| dashFalloffStart | 5 | 이 속도까지는 풀 impulse |
| dashFalloffEnd | 13 | 이 속도 이상에선 impulse 0 |

## 관련
- [hyper-dash.md](hyper-dash.md) — dash → charge 콤보
