# 동적 플랫폼 공통 (PlatformMotor / PlatformCarrier)

PlatformMotor + PlatformCarrier 패턴. 플랫폼은 자신의 움직임만 책임지고, 슬라임은 플랫폼 종류를 모름.

## 스크립트 구조
| 스크립트 | 역할 |
|---------|------|
| `PlatformMotor.cs` | 추상 베이스. Kinematic Rigidbody2D, MovePosition/MoveRotation. Delta/Velocity 노출. `ApplyRiderInfluence(bool isCharging, Vector2 riderVelocity)` 가상 메서드 |
| `WaypointPlatform.cs` | 웨이포인트 이동. localWaypoints, timingMode(Speed/Duration), speed 또는 segmentDurations[], easing, loop/ping-pong |
| `WaypointPlatformV2.cs` | 사각형 v2. SmoothDamp 댐핑 follow + sag 흔들림/반동 + 틸트/얼음 내장 + 탑승 트리거 왕복(범위 유지·복귀) + leader/partner 샌드위치 압출 |
| `RotatingPlatform.cs` | 연속 회전 또는 sin 기반 oscillate |
| `TiltPlatform.cs` | 스프링-댐퍼 기반 좌우 기울기. 라이더 localX → targetTilt |
| `PlatformCarrier.cs` | 슬라임에 부착. `[DefaultExecutionOrder(-200)]`로 PlatformMotor(-100)보다 먼저 실행되어 `ApplyRiderInfluence`가 `ComputeMotion`에 앞서 라이더 상태 갱신 |

## 슬라임 통합 방식
플랫폼 속도를 `rb.linearVelocity`에 합산하여 interpolation과 충돌하지 않는 velocity 기반 동기화:
```
rb.linearVelocity = velocity + platformVelocity * carrierFriction
```
- `rb.position` 직접 조작 없음 — interpolation 보장.
- `GetEffectiveVelocity()`: 선형 속도 + 회전 접선 속도 (`angularVel × arm`) 포함.
- `carrierFriction` (SurfaceModifiers): 0=얼음(안 따라감), 1=완전 전달.
- 발사 시 플랫폼 속도 상속: `velocity += carrier.GetEffectiveVelocity() * carrierFriction`.
- 착지 시 `GetSurfaceVelocity(surfaceCollider)`로 선형+회전 속도 직접 계산 (Airborne에서는 carrier가 Detach 상태이므로). 모든 착지 계산(tangentSpeed, normalSpeed, impactTangentSpeed, retention, redirect)이 이 상대속도 기준.

## 실행 순서
`PlatformCarrier` (`-200`) → `PlatformMotor` (`-100`) → `SlimeController` (default 0).

PlatformCarrier가 `ApplyRiderInfluence` 호출 → PlatformMotor가 MovePosition 큐잉 → SlimeController가 Velocity 읽어서 linearVelocity에 합산 → 물리 스텝에서 동시 이동.

## 개별 플랫폼 종류
- [waypoint-v2.md](waypoint-v2.md) — 사각형 v2 (댐핑 follow + 흔들림/반동 + 틸트/얼음 + 탑승 트리거 왕복 + 샌드위치)
- [crumbling.md](crumbling.md) — 무너지는 플랫폼
- [rolling-disk.md](rolling-disk.md) — 360° 회전 원형
- [pendulum.md](pendulum.md) — 매달린 진자
- [springy-branch.md](springy-branch.md) — 탄력 나뭇가지
- [deformable-mud.md](deformable-mud.md) — 소성 변형 / 구멍 뚫림
- [rope.md](rope.md) — Verlet 로프 + 반동 Launch
- [../mechanisms/push-door.md](../mechanisms/push-door.md) — 밀어서 여는 문 (PlatformMotor 상속)

## MegaDeform2DCollider (관련 유틸)
`MegaFiers`가 deform한 mesh vertex를 매 프레임 `EdgeCollider2D.SetPoints`로 복사.
- `contourIndices`는 에디터에서 한 번 bake (MegaDeform2DColliderBaker).
- Execution order `int.MaxValue`로 MegaModifyObject.LateUpdate 후에 실행.
- 물리와 독립적인 순수 visual→collider pass.
