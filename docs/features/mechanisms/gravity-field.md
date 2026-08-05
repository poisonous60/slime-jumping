# GravityField (중력 방향 치환 구간)

**Force field이 아님**. 구간 내부에서 "아래" 방향만 바꿈. 중력 크기(`|settings.gravity|`)는 동일. Mario Galaxy의 gravity area와 같은 모델 — 시각적 끌어당김은 down 방향 변화의 결과지 force 때문이 아님.

## 모드
- **Fixed**: Inspector에서 `localDownDirection` 벡터 1개. zone 안 어디서든 같은 방향. "뒤집힌 방", "벽이 바닥인 구역".
- **Radial**: `localPivot` 점(로컬 좌표) 기준으로 down = 슬라임→pivot 방향. `invertDirection`으로 안/밖 전환. 원형 플랫폼 빙 둘러 붙기.

## Effective Gravity 아키텍처
- `SlimeController`에 `gravityUp`/`gravityRight`/`GravityVector` 필드. 매 `FixedUpdate` 첫머리 1회 계산.
- `gravityFieldStack` (LIFO): GravityField가 `OnTriggerEnter2D`/`Exit2D`로 push/pop. 스택 최상위의 `GetDownDirection` 사용.
- 스택 비면 `gravityUp = Vector2.up` (world gravity).
- 모든 물리/비주얼 코드가 `Vector2.up` 대신 `gravityUp`을, `new Vector2(0, gravity)` 대신 `GravityVector`를 참조.
- `SurfaceProbe.CheckSurface`와 `GetSurfaceAngle`에 `gravityUp`/`gravityRight` 파라미터. 천장 필터, 입력 타이브레이커, 중력 정렬 모두 effective gravity 기준.

## Gravity-axis 분해 패턴 (air control, ClampFallSpeed, DragZoneExit)
```csharp
float gravComponent = Vector2.Dot(velocity, gravityUp);
float hComponent = Vector2.Dot(velocity, gravityRight);
velocity = gravityUp * gravComponent + gravityRight * hComponent;
```

## 진입/탈출
velocity 보존, 중력 방향만 즉시 전환. 부착 중 전환은 자연 동작에 맡김 (별도 처리 없음).

## 겹침 정책
- 디자인상 금지. 안전망으로 LIFO 스택 (마지막 진입 zone이 이김).
- **갇힘 방지**: 시스템에 탈출 메커니즘 없음. 디자이너가 레벨로 보장.

## TeleportTo 연동
`TeleportTo`에서 `gravityFieldStack.Clear()` + gravityUp/gravityRight 리셋.
`launchHeight`는 `Vector2.Dot(rb.position, gravityUp)`으로 중력축 투영.

## 파라미터
| 파라미터 | 타입 | 비고 |
|---------|------|------|
| `gravityMode` | enum {Fixed, Radial} | |
| `localDownDirection` | Vector2 | Fixed 모드에서만 사용 |
| `localPivot` | Vector2 | Radial 모드. transform 기준 로컬 오프셋 |
| `invertDirection` | bool | Radial일 때 false=중심으로(inward), true=밖으로(outward) |
| (collider) | Collider2D, `isTrigger=true` | 아무 모양 OK. Layer 9: GravityField |

## Scene 구조
```
GravityZone (GameObject, Layer 9)
├─ BoxCollider2D / CircleCollider2D / PolygonCollider2D (trigger)
└─ GravityField.cs
```

## 스코프 외
- 카메라 회전
- force field falloff
- 다중 zone 가중 평균

중력 **강도**는 별도 메커니즘이다 — [low-gravity-zone.md](low-gravity-zone.md). 스택이 따로라 겹쳐 놓으면 방향은 이쪽, 크기는 저쪽이 정한다.

## 관련 파일
- [GravityField.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GravityField.cs)
