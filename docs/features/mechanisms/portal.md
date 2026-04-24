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

## 관련 파일
- [Portal.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/Portal.cs)
- [PortalSwapper.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/PortalSwapper.cs)
