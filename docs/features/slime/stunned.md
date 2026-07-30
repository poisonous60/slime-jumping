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
- **표면 접촉**: 법선 속도만 상쇄 (`SurfaceProbe.ProjectVelocityAgainstContacts`, Airborne 천장 충돌과 동일). 접선 성분은 유지 → 지면/벽을 따라 미끄러짐. Attached/Charging 전환 **차단**.
- **마찰 램프**: 접선 속도에 지수 감쇠 `v *= exp(-rate·dt)`, `rate = stunFrictionDampRate × 표면 slideResistanceMultiplier(angle) × ramp`. `ramp` 은 스턴 진행도(`1 - stunTimer/stunDuration`)를 `stunFrictionRampStart`~1 구간에서 `InverseLerp` → 진입 직후 무마찰, 만료 직전에 완전 제동. 0.2 m/s 미만은 0 으로 스냅.
  - m/s² 고정 감속(`slideResistance` = 1)은 8 m/s 넉백 대비 총 Δv 0.1~0.2 m/s (≈2%) 라 눈에 안 보임 → 속도 비례 모델 사용. 지면 차이는 mult 로 유지 (Ice 0.4 는 덜 멈추고 Mud 1.3 은 더 멈춤).
  - 스턴 중 `surfaceMod` 는 기본값에 고정되어 있으므로 `SurfaceTag` 에서 직접 읽음.
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
| stunFrictionRampStart | 0.5 | 마찰이 들어오기 시작하는 스턴 진행도 (0 = 첫 프레임부터 감속, 1 = 스턴 내내 무마찰) |
| stunFrictionDampRate | 15 | 램프 최대치에서의 접선 감쇠율 (1/s). 0.5s 스턴 후반부에 슬라이드 속도 ~80% 제거. 더 확실히 멈추려면 올림 |

## 관련
- [../mechanisms/spike-hazard.md](../mechanisms/spike-hazard.md)
- [../mechanisms/turret-hazard.md](../mechanisms/turret-hazard.md)
- [../mechanisms/saw-knife-hazard.md](../mechanisms/saw-knife-hazard.md)
