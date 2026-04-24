# ConveyorBelt (컨베이어벨트)

`IPlatformEffect` 구현. `OnSlimeStay`에서 `AddExternalVelocity` 호출. SlimeController가 Attached/Charging에서 surfaceTangent에 자동 투영.

## 파라미터
| 필드 | 설명 |
|------|------|
| `speed` | 가속도 (units/s²) |
| `localDirection` | 로컬 스페이스 방향 |
| `terminalSpeed` | 최대 속도. 선형 감쇠 |

## 외부 속도 파이프라인
가산 채널 (`AddExternalVelocity`). Attached/Charging에서 surfaceTangent에 투영 — 법선 성분 제거로 표면 스냅 지터 방지. 상세는 [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md).

## 관련 파일
- [ConveyorBelt.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/ConveyorBelt.cs)
