# SpikeHazard (가시)

`IHazardSource` 구현. 트리거 콜라이더. `OnTriggerEnter2D`에서 `slime.ApplyHazardEffect(effect)` 호출 → Stunned 진입.

## 방향 모드
| 모드 | 동작 |
|------|------|
| `useLocalUp = true` | 가시의 `transform.up` 방향으로 넉백 |
| `useLocalUp = false` + `redirectTarget` 없음 | 가시 중심 → 슬라임 방향으로 자동 계산 |
| `useLocalUp = false` + `redirectTarget` 지정 | 슬라임 → 타겟 좌표 방향으로 넉백 |

## HazardEffect 필드
- `knockbackForce`: 넉백 속도 (units/s).
- `stunDuration`: 기절 지속시간 (초).
- `damageType`: Generic / Slice / etc.

Stunned 상태 동작은 [../slime/stunned.md](../slime/stunned.md) 참조.

## 관련 파일
- [SpikeHazard.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/SpikeHazard.cs)
- [HazardEffect.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/HazardEffect.cs)
- [IHazardSource.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/IHazardSource.cs)
