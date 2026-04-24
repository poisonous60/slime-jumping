# 회전 원형 플랫폼 (Rolling Disk Platform)

슬라임의 무게(위치)로 자유 회전하는 원형 플랫폼. 360° 회전 허용 — 라이더가 한쪽에 머물면 결국 굴러 떨어짐.

`TiltPlatform`과 구조는 같지만 스프링 복원과 각도 제한이 없음.

## 물리 모델
- 상태: `currentAngle` (deg, 자유 wrap), `angularVel` (deg/s).
- 각가속도 누적:
```
pendingAngAccel += -effectiveArm * mass * riderAngAccelScale
```
  - **기준축은 world-horizontal** (`riderWorldPos.x − rb.position.x`) — 디스크 자기 회전과 무관. 중력이 world-down이라 불균형 축도 world-X.
  - 부호: 라이더가 +X(world)면 디스크 CW(부호 -) 회전.
  - `riderAngAccelScale`이 중력·관성모멘트 상수를 모두 흡수.
- **Dead zone** (`deadZoneAngleDeg`, 기본 15°): 디스크 정점(world-up 기준) 근처에서는 토크 0. 경계 밖에서 0부터 선형으로 증가(하드 컷 아님). `stickSlopeThreshold`와 유사한 정지 영역.
- `ComputeMotion`: `angAccel = pendingAngAccel − angularDamping * angularVel`, clamp `maxAngularSpeed`.
- **복원 항 없음** — dead zone 안에서만 토크 0, 밖에서는 복원 없이 계속 회전.

## 점프 이탈 시 탄젠셜 속도
`PlatformCarrier.GetEffectiveVelocity`의 기존 크로스 곱 로직(선형속도 + `angularVel × arm`)으로 자동 전달. 캐리어·베이스 클래스 수정 없음.

## 프리팹 구성
```
RollingDiskPlatform
├─ RollingDiskPlatform.cs (→ Rigidbody2D Kinematic 자동 추가)
├─ CircleCollider2D
└─ Visual (자식, 콜라이더와 함께 회전)
```
