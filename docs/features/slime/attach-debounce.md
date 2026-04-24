# 부착 디바운스 (Attach Debounce)

모서리/코너에서 Attached↔Airborne 상태가 빠르게 토글되는 오실레이션 방지.

## 메커니즘
짧은 시간(`attachToggleWindow`, 0.1초) 내 상태 전환 횟수를 카운트. `attachToggleLockoutThreshold`(10) 도달 시 `attachLockoutDuration`(0.1초) 동안 착지 전환 차단.

## 카운트 대상
- 모든 비의도적 상태 전환 (Attached↔Airborne, Airborne↔Charging 포함).
- `ChangeState(newState, intentional)`에서 디바운스 훅이 자동 실행.
- `intentional=true` (Launch/Hop/Teleport): 카운터 리셋.

## Lockout 중 동작
- 중력/에어 컨트롤은 정상.
- 표면 접촉 시 법선 속도만 상쇄 (관통 방지), 접선으로 미끄러져 벗어남.
- Attached/Charging 전환 차단.

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| attachToggleWindow | 0.1 | 토글 감지 윈도우 (초) |
| attachToggleLockoutThreshold | 10 | lockout 발동 전환 횟수 |
| attachLockoutDuration | 0.1 | lockout 지속시간 (초) |

## 관련
- [state-machine.md](state-machine.md)
