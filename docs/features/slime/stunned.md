# Stunned (기절 상태)

위험 오브젝트에 의해 진입. `ApplyHazardEffect(HazardEffect)` 호출로 진입.

## HazardEffect struct
| 필드 | 설명 |
|------|------|
| `knockbackForce` | 넉백 속도 (units/s) |
| `knockbackDirection` | 방향 (Vector2.zero = 자동 계산) |
| `stunDuration` | 기절 지속시간 (초) |
| `damageType` | Generic / Slice / etc. (i-frame/타깃 식별용) |

## 동작
- **입력 잠금**: 모든 입력 핸들러 early-return.
- **공중**: 중력 1x (W/S 조절 없음, 상승/하강 비대칭 없음).
- **표면 접촉**: 법선 속도만 상쇄 (Airborne 천장 충돌과 동일). Attached/Charging 전환 **차단** — 미끄러지지 않고 정지 유지도 아님, 벽에 달라붙어 슬라이드 방지.
- **타이머**: `stunTimer` 카운트다운. 만료 시 Airborne 전환 (`intentional: true`).
- **i-frame**: 기절 중 추가 `ApplyHazardEffect` 무시.
- **외부 힘**: `AddExternalVelocity` (바람 등) 정상 적용 (Airborne과 동일 분기).

## 진입 시 정리
- 차징 비주얼 숨김
- 캐리어 분리
- 플랫폼 효과 해제
- 표면 modifier 기본값 리셋
- 모든 타이머/크롤/에임 초기화
- `RegisterOverlapping` 호출 (ReattachGuard)

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| defaultStunDuration | 0.5 | 기본 기절 시간 (폴백) |
| defaultKnockbackForce | 8 | 기본 넉백 속도 (폴백) |

## 관련
- [../mechanisms/spike-hazard.md](../mechanisms/spike-hazard.md)
- [../mechanisms/turret-hazard.md](../mechanisms/turret-hazard.md)
- [../mechanisms/saw-knife-hazard.md](../mechanisms/saw-knife-hazard.md)
