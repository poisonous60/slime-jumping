# JumpPad (점프대)

`IPlatformEffect` 구현. 착지 시 `ForceAirborneWithVelocity` 호출로 즉시 발사.

## 파라미터
| 필드 | 설명 |
|------|------|
| `launchForce` | 발사 속도 (units/s) |
| `launchDirection` | 로컬 스페이스 발사 방향 (자동 정규화) |
| `cooldown` | 재발동 방지 쿨다운 (초) |

## 동작
`CheckPlatformEffect`는 착지 시퀀스 완료 후 호출 → `ForceAirborneWithVelocity`가 상태를 Airborne으로 되돌림.

**Primary 우선권**: `SurfaceProbe.SelectPrimary` Step 1.7에서 비천장 active 중 `IPlatformEffect` 가진 콜라이더 우선 선택 → JumpPad + 바닥 동시 착지 시 effect 발동 누락 방지. [../slime/surface-attach.md](../slime/surface-attach.md) 참조.

## 관련 파일
- [JumpPad.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/JumpPad.cs)
- [IPlatformEffect.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/IPlatformEffect.cs)
