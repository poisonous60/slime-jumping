# 그라운드 대시 (S 순간 부스트)

Attached 상태에서 A/D 크롤 중 S를 누르면 `crawlVelocity`에 순간 impulse 추가. **표면 부착 유지** — 실제 점프 아님.

## 동작
- S pressed(rising edge)에만 발동, 홀드 시 반복 안 함.
- 입력 방향과 기존 crawl 방향 모두 고려.

```
if (S pressed && cooldown <= 0 && |inputX| > 0)
    angleScale = 1 - InverseLerp(groundDashAngleMin, groundDashAngleMax, surfaceAngle)
    crawlVelocity += sign(inputDir) * groundDashImpulse * angleScale * surfaceMod.groundDashScale
    cooldown = groundDashCooldown
```

## 버퍼
- **입력 버퍼**: S를 먼저 누르고 `groundDashInputBuffer`(0.1초) 안에 A/D 입력 시에도 발동.
- **코요테 버퍼**: Airborne에서 S 누르면 저장, 착지 시 A/D 홀드 중이면 발동.

## 속도 감쇠 (Hop-dash chain 방지)
같은 방향 crawl 속도가 `dashFalloffStart`(5) 이상이면 impulse 선형 감소, `dashFalloffEnd`(13)에서 0. 반대 방향 dash는 제약 없음.

## 쿨타임 & 기울기
- `groundDashCooldown`(0.3초) 재발동 불가. Wall-clock 기반 — Airborne/Charging 중에도 경과.
- `groundDashAngleMin`(30°)~`groundDashAngleMax`(60°) 사이 1→0 보간. 평지=풀 부스트, 급경사=불가.

## 표면별
`SurfaceMaterial.groundDashScale` 배율 (기본 1.0, 0=대시 불가).

## 입력 우선순위
Charging > W(hop) > S(dash). Space 홀드 착지 시 둘 다 소비, hop 버퍼 활성 시 dash 버퍼 소비.

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
