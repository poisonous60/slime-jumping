# PushDoor (밀어서 여는 문)

살롱 도어 스타일의 경첩 문. 슬라임이 접촉하면 콜라이더가 X축으로 평행이동하고, 분리된 3D 비주얼이 asin 매핑으로 회전하여 문이 안쪽으로 열리는 모습. 충분히 밀면 콜라이더 비활성화 → 통과 → 스프링 복원.

## 핵심 구조
- **물리**: 접촉 기반 push + 스프링-댐퍼 return. 상태 변수 = `currentX` (0이 닫힘).
- **Push**: 접촉 중이면 일정 가속도로 밀어냄 (velocity 무관, 위치 기반으로 방향 결정).
- **Return**: 슬라임 이탈 시 스프링-댐퍼가 `currentX`를 0으로 복원.
- **비주얼**: 콜라이더와 분리된 3D 메시. `theta = -asin(currentX / doorDepth)` 매핑. `doorDepth = transform.position.z + hingeOffset.z`.
- **콜라이더/비주얼 분리**: 콜라이더는 직선 이동, 비주얼만 3D 회전.

## 물리 모델
**Push (접촉 중)**:
```
xVelocity += pushDirection * pushAccel * dt
xVelocity = clamp(xVelocity, -pushMaxSpeed, pushMaxSpeed)
```
- `pushDirection`: 슬라임 위치에서 문 반대 방향 (`-sign(dot(toRider, right))`).
- push와 return 완전 분리 — pushAccel/pushMaxSpeed는 열림 속도만 결정.

**Return (접촉 없음)**:
```
force = -returnStiffness * currentX - returnDamping * xVelocity
xVelocity += force * dt
```

## 통과 조건
- `|currentX| >= openThreshold` (= `doorDepth * sin(openAngleDeg)`) → 콜라이더 비활성 (`enabled = false`), `openTimer = minOpenTime`.
- `openTimer` 동안 재활성화 시도 안 함 (슬라임 통과 시간 확보).
- `openTimer` 만료 + `|currentX| < openThreshold` → overlap 체크 후 재활성화.
- overlap 발견 시 재시도 딜레이 0.1초 (프레임 단위 flicker 방지).

## 비주얼 매핑
- `doorDepth = transform.position.z + hingeOffset.z` (힌지 월드 Z).
- `openThreshold = doorDepth * sin(openAngleDeg)`.
- `theta = -asin(currentX / doorDepth)` (asin: 기하학적으로 정확한 원호 투영).
- `hingeOffset`으로 VisualPivot 위치 지정, `currentX` 보정으로 힌지 월드 고정.
- Gizmo: 힌지 위치에 노란색 구.

## 방향 모드
- **양방향**: currentX 양/음 모두 가능 (살롱 도어).
- **한 방향** (`oneWay=true`): 한쪽만 허용, 반대는 벽처럼 작용.

## Carrier 추종
PlatformMotor 상속. 콜라이더가 X 평행이동하므로 carrier에 translation 델타로 자동 전파. 슬라임이 위에 서 있으면 문 복원 시 옆으로 밀려남.

## 시그니처 확장
`PlatformMotor.ApplyRiderInfluence`에 `Vector2 riderVelocity` 파라미터 추가. PlatformCarrier가 `SlimeController.Velocity` 전달. PushDoor는 riderVelocity 미사용 (접촉 기반).

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| pushAccel | 15 | 접촉 시 가속도 |
| pushMaxSpeed | 3 | 밀림 최대 속도 |
| returnStiffness | 10 | 닫히는 스프링 강도 |
| returnDamping | 3 | 닫힘 진동 억제 |
| openAngleDeg | 60 | 통과 가능 각도 (°) |
| oneWay | false | 한 방향 모드 |
| hingeOffset | (0,0,2) | 힌지 위치 (로컬 오프셋) |
| rotationAxis | (0,1,0) | 비주얼 회전축 |
| minOpenTime | 1 | 콜라이더 비활성 최소 시간 |

## Scene 구조
```
PushDoor (Layer 7)
├─ Rigidbody2D (Kinematic)
├─ BoxCollider2D
├─ PushDoor.cs
└─ VisualPivot (자식)
    └─ Mesh (3D Cube, Z 방향으로 문판 표현)
```

## 관련 파일
- [PushDoor.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/PushDoor.cs)
