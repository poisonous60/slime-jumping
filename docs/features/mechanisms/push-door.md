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

---

# PushDoorV2 (충격량-기반 문)

v1의 "접촉 시 지속 가속" 모델이 어색해서 (살짝 닿아도 훅 밀림) 만든 개선판. **슬라임이 문에 부딪힌 순간의 운동량**을 한 번만 주입하고, 이후에는 관성 + drag + 스프링으로 자연스럽게 스윙. 살롱 도어 레이아웃 (-Z 방향 패널, asin 매핑 + collider X평행이동) 전용.

> 표준 FBX 도어 mesh (leaf 의 Y rotation 만으로 swing) 에는 [v3](#pushdoorv3-각도-기반-도어) 사용.

## 핵심 차이 (v1 → v2)
| 항목 | v1 (PushDoor) | v2 (PushDoorV2) |
|------|---------------|-----------------|
| 트리거 | 접촉 중 매 FixedUpdate 가속 | 접촉 시작 순간 1회 충격량 주입 (이탈 후 재접촉시에만 재발화) |
| 강도 결정 | `pushAccel` 고정 | `impulseResponseCurve` (AnimationCurve) 로 충격량 → 전달량 매핑 |
| 약한 접촉 | 동일하게 끝까지 밀림 | `openThresholdImpact` 미만은 **흔들림(jiggle)만**, 문 안 열림 |
| Return | 스프링-댐퍼 | 스프링 + drag (공기저항 모델) |
| 비주얼 | `VisualPivot` 수동 할당 | `Leaf` / `DoorLeaf` / `VisualPivot` 중 자동 탐색, `Hinge` 자식에서 오프셋 자동 추출 |

## 물리 모델
**Contact (edge-triggered)**:
```
vRel        = dot(riderVelocity - doorVelocity, right)
impactMag   = |vRel| * riderMass
dir         = sign(vRel)              // door moves with the slime's momentum (Newton 3rd law)
normalized  = impactMag / impactReference
transferred = impulseResponseCurve.Evaluate(normalized)

if impactMag < openThresholdImpact:
    xVelocity += dir * impactMag * jiggleScale    // 흔들기만
else:
    xVelocity += dir * transferred * impulseScale  // 실제 열기
```

**FixedUpdate (관성 + drag + 스프링)**:
```
xVelocity += (-returnStiffness * currentX) * dt
xVelocity *= (1 - angularDrag * dt)
currentX  += xVelocity * dt
```
- `pushAccel` / `pushMaxSpeed` 없음. 한 번의 충격이 관성으로 유지 → drag 로 감속 → 스프링이 복원.

## AnimationCurve 편집 (편차 축소)
`impulseResponseCurve` 는 디자이너가 **충격량 편차를 실제 전달량에 어떻게 반영할지** 직접 곡선으로 편집. X=정규화된 충격량, Y=전달 계수.

기본값:
- `(0, 0)` → 정지 접촉은 전달 0
- `(0.2, 0.08)` → 약한 충격은 살짝만
- `(1, 1)` → `impactReference` 와 같은 충격이면 계수 1 (= `impulseScale` 만큼 주입)
- `(2, 1.3)` → 강한 충격은 saturate (편차 과다 방지)

## 트리거 로직
- **Edge-triggered**: `wasInContact` 플래그로 접촉 **시작 프레임에만** 충격량 주입. Carrier 가 매 FixedUpdate 호출해도 중복 주입 없음.
- **재발화 조건**: `RemoveRider()` (= 슬라임 이탈) 시에만 플래그 리셋. **한 번의 부착당 1회 충격**. 슬라임이 launch → 다시 부딪히면 그 때 새 충격.
- 시간 기반 자동 재장전(cooldown) 없음 — 이전 버전에 있었지만 매 0.25초마다 재발화하여 v1의 지속 push처럼 느껴지는 버그가 있어 제거.

## Scene 구조 (자동 감지)
```
Door_XX                 (Rigidbody2D, BoxCollider2D, PushDoorV2)
├─ Frame                (MeshRenderer, 물리 없음 — 장식용 문틀)
├─ Hinge                (empty, localPosition 만 사용)
└─ Leaf                 (visualTransform 자동 할당 대상)
    └─ Mesh             (실제 문짝 MeshRenderer)
```
- `Reset()` 에서 `Leaf`/`DoorLeaf`/`VisualPivot` 순으로 탐색, `Hinge` 자식이 있으면 `hingeOffset` 으로 복사.
- 기존 FBX (문틀 + 문짝 분리) 의 자식 이름을 규약에 맞추기만 하면 됨.

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| impactReference | 8 | 정규화 기준 (kg·m/s) |
| impulseResponseCurve | 0→0, 0.2→0.08, 1→1, 2→1.3 | 충격량 → 전달 계수 매핑 |
| impulseScale | 2.5 | Y=1 일 때 주입할 xVelocity |
| openThresholdImpact | 1.5 | 이 미만은 흔들림만 (열리지 않음) |
| jiggleScale | 0.15 | 임계 미달 충격의 흔들림 계수 |
| returnStiffness | 10 | 닫힘 스프링 강도 |
| angularDrag | 2 | 속도 감쇠 (1/s) |
| openAngleDeg | 60 | 통과 각도 |
| hingeOffset | (0,0,2) | 힌지 오프셋 (Hinge 자식 있으면 자동) |
| doorDepthOverride | 0 (=auto) | 패널 길이. 0이면 `transform.position.z + hingeOffset.z` (살롱 도어 레이아웃) |
| rotationAxis | (0,1,0) | 비주얼 회전축 |
| leafForwardLocal | (0,0,-1) | 닫힘 상태 leaf 방향 (gizmo 전용). rotationAxis 와 평행이면 호 collapse |
| minOpenTime | 1 | 콜라이더 비활성 최소 시간 |

## 에디터 도구
- `SlimeJumping/Mechanism/Setup PushDoorV2 Test` — 테스트 씬 자동 구성 (Frame/Hinge/Leaf 포함)
- `SlimeJumping/Mechanism/Convert Selection to PushDoorV2` — 선택한 루트의 `Leaf`/`Hinge` 자식을 자동 와이어링 (기존 v1 컴포넌트가 있으면 제거)
- `SlimeJumping/Mechanism/Wrap Selection in Axis-Aligned Parent` — 씬에서 회전된 도어를 axis-aligned 부모 안으로 래핑. **BoxCollider2D 는 Z축 회전만 2D 물리에 반영**되므로, Y/X 회전이 걸린 도어는 콜라이더가 X-Y 평면에서 좁아지거나 collapse 함. 이 메뉴가 회전을 부모에게 위임하고 도어 자체를 (0,0,0) 회전으로 리셋
- `SlimeJumping/Mechanism/Convert 3D Door Prefab to PushDoorV2` — 3D 도어 mesh prefab (LowPolyInterior 등) 자동 변환:
  - 자식 MeshRenderer 중 위치 offset 이 가장 큰 것을 leaf 로 자동 감지
  - leaf pivot 을 hingeOffset 으로 자동 복사 (FBX 가 hinge edge 에 origin 을 두는 경우)
  - leaf bounds 의 X/Z 중 큰 값을 `doorDepthOverride` 로 자동 설정
  - BoxCollider2D 를 leaf 풋프린트에 맞춰 size/offset 자동 계산
  - 기존 3D MeshCollider 들은 자동 비활성화 (2.5D 충돌 중복 방지)
  - Layer = 7

## Scene Gizmo
- 항상 표시: 힌지 위치 (시안색 구)
- 선택 시 표시 (`OnDrawGizmosSelected`):
  - 시안색 짧은 선 — 회전축
  - 노란색 선 — 닫힘 상태의 leaf 방향 (힌지 → 끝)
  - 초록색 호 + 양 끝 광선 — `openAngleDeg` 만큼의 swing 범위. `oneWay=true` 면 허용 방향 한쪽만 표시.

## 관련 파일
- [PushDoorV2.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/PushDoorV2.cs)
- [PushDoorTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/PushDoorTestSetup.cs)

---

# PushDoorV3 (각도 기반 도어)

표준 FBX 도어 mesh — leaf 의 pivot 이 hinge edge 에 있고 leaf 의 `localRotation` (보통 Y) 만 바꾸면 자동으로 swing — 와 직접 호환되는 모델. v2 의 collider X평행이동 + asin 매핑은 살롱 도어 전용이라, 표준 FBX 도어는 잘 안 맞아서 따로 만든 버전.

## 핵심 차이 (v2 → v3)
| 항목 | v2 (PushDoorV2) | v3 (PushDoorV3) |
|------|-----------------|-----------------|
| 모델 | Collider X평행이동 + asin 매핑 (살롱 도어) | **Leaf의 localRotation 직접 회전 (각도 state)** |
| 콜라이더 | 슬라임에 맞춰 X로 이동 | **위치 고정**, |angle| ≥ openAngle 일 때 enabled toggle 만 |
| State 변수 | `currentX` (m) + `xVelocity` (m/s) | `currentAngleDeg` (deg) + `angularVelocityDeg` (deg/s) |
| 힌지 위치 | `hingeOffset` (수동) | leaf 의 `transform.position` (자동 — leaf의 pivot 그 자체) |
| 패널 길이 | `doorDepthOverride` (수동) | 불필요 (각도 state라 길이 무관) |
| 메시 호환 | 살롱 도어 (-Z 방향 패널 전용) | **표준 FBX 도어 (어떤 mesh 든 leaf 의 Y rotation 만 바꾸면 OK)** |

## 물리 모델 (각도 기반)
**Contact (edge-triggered)**:
```
vRel        = dot(riderVelocity - doorVelocity, right)
impactMag   = |vRel| * riderMass
dir         = sign(vRel)
normalized  = impactMag / impactReference
transferred = impulseResponseCurve.Evaluate(normalized)

if impactMag < openThresholdImpact:
    angularVelocityDeg += dir * impactMag * jiggleScale
else:
    angularVelocityDeg += dir * transferred * impulseScale
```

**FixedUpdate (각 관성 + 각 drag + 각 스프링)**:
```
angularVelocityDeg += (-returnStiffness * currentAngleDeg) * dt
angularVelocityDeg *= (1 - angularDrag * dt)
currentAngleDeg    += angularVelocityDeg * dt
```

**LateUpdate (시각화)**:
```
visualTransform.localRotation = baseLeafRotation * AngleAxis(currentAngleDeg, rotationAxis)
```
- 콜라이더 위치/회전 변경 없음. Leaf 의 `localRotation` 만 변경 → mesh가 자체 pivot에서 회전.
- `currentAngleDeg ≥ openAngleDeg` → 콜라이더 enabled = false (슬라임 통과).

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| impactReference | 8 | 정규화 기준 (kg·m/s) |
| impulseResponseCurve | 0→0, 0.2→0.08, 1→1, 2→1.3 | 충격량 → 전달 계수 매핑 |
| impulseScale | 200 | Y=1 일 때 주입할 angular velocity (deg/s) |
| openThresholdImpact | 1.5 | 이 미만은 흔들림만 (열리지 않음) |
| jiggleScale | 12 | 임계 미달 충격의 흔들림 계수 (deg/s per impactMag) |
| returnStiffness | 5 | 닫힘 각 스프링 강도 |
| angularDrag | 2 | 각속도 감쇠 (1/s) |
| openAngleDeg | 60 | 통과 각도 (이상이면 collider disable) |
| maxAngleDeg | 90 | hard clamp — 물리적 최대 회전 |
| rotationAxis | (0,1,0) | leaf의 local 회전축. 표준 FBX 도어=Y |
| leafForwardLocal | (0,0,0)=auto | (gizmo 전용) 닫힘 상태 leaf 방향. (0,0,0)=mesh bounds.center 로 자동 |
| minOpenTime | 1 | 콜라이더 비활성 최소 시간 |

## 에디터 도구
- `SlimeJumping/Mechanism/Convert 3D Door Prefab to PushDoorV3` — 표준 FBX 도어 prefab 자동 변환:
  - 자식 MeshRenderer 중 가장 큰 mesh 를 leaf 로 자동 감지
  - leaf bounds.center 로 leafForwardLocal 자동 (gizmo only)
  - BoxCollider2D 를 leaf footprint 에 맞춰 size/offset 자동 (콜라이더는 이동 안 하므로 닫힘 위치에 그대로)
  - 기존 V1/V2 컴포넌트 자동 제거 → V3 부착
  - rotationAxis = (0,1,0) 기본 (Y rotation)

## Scene 구조
```
Door_v2 (래퍼, 시각 회전)
└─ Door2_C1 (Rigidbody2D + BoxCollider2D + PushDoorV3, 회전 (0,0,0))
    ├─ <문틀 mesh>
    └─ <leaf mesh>     ← visualTransform 자동 할당. 이 mesh 의 localRotation.y 만 변경하여 swing
        └─ <handle mesh>
```

## 관련 파일
- [PushDoorV3.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/PushDoorV3.cs)
- [PushDoorTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/PushDoorTestSetup.cs)
