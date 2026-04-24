# 매달린 플랫폼 (Pendulum with Landing Inertia)

줄 기반 진자 위에 얹힌 판자 플랫폼. 슬라임이 착지하는 순간의 수평 속도와 서 있는 위치가 진자 스윙에 영향.

## 물리 모델
- 상태: `angle` (rad), `angularVel` (rad/s), `worldPivot` (고정).
- 진자 방정식: `α = −(g/L)·sin(θ) − c·ω + pendingRiderAngAccel`.
- 출력:
  - `pos = worldPivot + (sin(θ)·L, −cos(θ)·L)`
  - `rot = θ · Rad2Deg · swayRatio + currentTilt`

## 라이더 영향 — 세 층

**(a) 착지 임펄스 (한 번, 줄 스윙에 작용)**
- Attach 순간(`ApplyRiderInfluence` 첫 호출)에만 `angularVel += riderVelocity.x / L · landingImpulseScale`.
- `riderAttached` 플래그로 제어, `RemoveRider`에서 리셋.
- 전제: `SlimeController`가 같은 `surfaceCollider`에 대해 `Attach` 중복 방지.

**(b) 위치 기반 연속 토크 (줄 스윙에 작용)**
- 매 프레임 `pendingRiderAngAccel += −gravity · mass · localX · riderPosAngAccelScale`.
- `ComputeMotion`에서 진자 방정식에 합산 후 0으로 리셋.
- (a)와 (b)는 방향이 반대 — (a)는 라이더 진행방향, (b)는 중력 토크 반대방향.

**(c) 판자 flex (스프링-댐퍼, 줄 스윙과 독립)**
- 매 프레임 `targetTilt = −clamp(localX/halfWidth) · maxTiltAngle · mass`.
- 스프링-댐퍼(`tiltStiffness`, `tiltDamping`)로 `currentTilt`가 `targetTilt`를 따라감.
- 출력 회전에 **가산** (`+ currentTilt`) — 줄은 그대로 스윙하되 판자만 라이더 쪽 즉각 기울임.

## swayRatio — 판자/줄 커플링
`rotDeg = angle·Rad2Deg · swayRatio + currentTilt`
- `1.0` (기본): 판자가 줄과 같은 각도 (실제 진자).
- `0.3`: 줄 45° 스윙해도 판자는 13.5° — 그네 좌석처럼 상대적 수평.
- `0.0`: 판자는 줄 스윙과 무관 (단 `currentTilt`는 별개로 계속 작동).
- `currentTilt`는 swayRatio 영향 받지 않음 — 두 효과 독립.

## 구조
빈 루트 → Board(PendulumPlatform + Rigidbody2D) + Rope(LineRenderer) 자식.
- `ropes` (LineRenderer[]) 배열 — 매 LateUpdate에 world-space 갱신.
- 각 로프의 Z는 자신의 transform Z를 따라 서로 다른 depth 배치 가능.

## 한계
- 단일 라이더 가정 (동시 착지 시 첫 번째만 임펄스).
- World-down gravity 가정 — `GravityField` 구역 내 배치 비추.
- 부호는 플레이테스트에서 확정 — 의도와 반대면 `landingImpulseScale` 또는 `riderPosAngAccelScale` 부호 반전.
