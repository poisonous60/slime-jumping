# Portal (텔레포터 쌍)

Portal-style 파란색/주황색 텔레포터. 벽에 배치.

## 프레임 규약
- `transform.right` = 포탈 바깥 방향 (슬라임 출구 법선).
- `transform.up` = 포탈 접선.
- 벽 배치 시 right가 벽에서 바깥으로 향하도록 Z축 회전.

## 속도 변환
```
vIn      = dot(velocity, -entry.OutNormal)   // 포탈로 들어오는 성분
vTangent = dot(velocity, entry.Tangent)      // 접선 성분
newV     = exit.OutNormal * vIn + exit.Tangent * vTangent
exitPos  = exit.position + exit.OutNormal * exitOffset
```
법선 성분은 출구 normal로, 접선 성분은 출구 tangent로 매핑. 원작 Portal의 거동과 동일.

## 재진입 방지
출구 포탈에 `reentryCooldown`(0.2초) 적용. 텔레포트 직후 같은 프레임 trigger 재발화 방지.

## TeleportTo
포탈이 호출하는 SlimeController 공개 메서드. 상세는 [../infra/teleport.md](../infra/teleport.md).

## PortalSwapper (신호 수신 시 위치 교체)
- 신호 수신 시 포탈을 두 슬롯 사이에서 교체. `swapDelay` 후 실행.
- **Fling puzzle**: 포탈 루프(A→B→낙하→A→B...) 중 버튼으로 포탈 B 위치 변경 → 슬라임이 옆으로 사출.

## Portal_v2 (소비/재생성 라이프사이클)
v1 의 텔레포트 산식은 그대로 유지하되, 한 번 통과하면 두 포탈이 동시에 사라졌다가 일정 시간 뒤에 재생성된다.

### 사이클
`Idle → Consuming → Gone → Respawning → Idle`. 슬라임이 입구에 진입하면:
1. 입구·출구 양쪽 콜라이더가 즉시 비활성화되어 출구 트리거 재발화 차단.
2. 슬라임 텔레포트 (v1 와 동일한 산식).
3. 양쪽 포탈이 `Consuming` 으로 전환 (스케일 축소 애니).
4. `consumeDuration` 후 `Gone` (visual 비활성, `respawnDelay` 동안 대기).
5. `respawnDuration` 동안 `Respawning` (스케일 확대), 끝나면 `Idle` 로 복귀.

스케일 커브는 `consumeScaleCurve` / `respawnScaleCurve` 로 노출. JumpBall 의 소비/재생성 패턴과 동일한 구조.

### 배치 편의
- **자동 페어링**: `pair` 한쪽만 연결하면 `Awake` 에서 반대편의 `pair` 가 자동으로 채워진다.
- **Create Paired Portal** 컨텍스트 메뉴: 현재 포탈을 비주얼까지 통째로 복제해 접선 방향으로 3 단위 떨어진 곳에 회전 180° 거울 배치하고, 양쪽 `pair` 를 즉시 연결한다. 보색을 자동 적용.
- `visualRenderer` 미할당 시 `OnValidate` 에서 `visual` 자식의 `Renderer` 를 자동 탐지.

### 색 지정
`portalColor` 필드. 에디터에서 변경 즉시:
- `SpriteRenderer` → `color` 직접 적용.
- 그 외 `Renderer` (Quad + URP 머티리얼 등) → `MaterialPropertyBlock` 으로 `_BaseColor` / `_Color` 동시 세팅 (URP/레거시 양쪽 호환, 머티리얼 인스턴스 누수 없음).

기즈모도 `portalColor` 로 그려져 씬 뷰에서 한눈에 짝을 식별할 수 있다.

### 차이 정리 (v1 vs v2)
| 항목 | v1 (Portal) | v2 (Portal_v2) |
|------|-------------|----------------|
| 페어 참조 | `exit` (단방향, 양쪽 모두 수동 와이어링) | `pair` (양방향 자동 미러링) |
| 라이프사이클 | 항상 활성, `reentryCooldown` 으로 중복 방지 | Idle/Consuming/Gone/Respawning 상태 머신 |
| 시각 효과 | 없음 | 스케일 애니 + 등장/소멸 SFX |
| 색상 | 머티리얼 직접 편집 | `portalColor` 필드 (라이브 미리보기) |
| 배치 | `PortalTestSetup` 같은 에디터 스크립트 의존 | `Create Paired Portal` 컨텍스트 메뉴 한 번 |

### 관련 파일
- [Portal_v2.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/Portal_v2.cs)
- 참고 패턴: [JumpBall.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/JumpBall.cs) 의 소비/재생성 상태 머신.

## 관련 파일
- [Portal.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/Portal.cs)
- [Portal_v2.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/Portal_v2.cs)
- [PortalSwapper.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/PortalSwapper.cs)
