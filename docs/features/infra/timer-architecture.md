# 타이머 아키텍처

## 분류
- **입력 관련 타이머** (`hopCooldownTimer`, `hopBufferTimer`, `dashCooldownTimer`, `dashBufferTimer`): `Update()`에서 wall-clock (`Time.deltaTime`) 기반 tick. 상태(Attached/Charging/Airborne)와 무관하게 경과.
- **물리 연동 타이머** (`stickTimer`, `stickDeccelTimer`, `ignoreTimers`): `FixedUpdate()`에서 `fixedDeltaTime` 기반 tick.

## IgnoredColliderList 모드 분리 (2026-04-13)
| 모드 | 용도 | 해제 조건 | Physics2D.IgnoreCollision |
|------|------|-----------|---------------------------|
| `ReattachGuard` | Launch/Hop/ForceAirborne/스턴 knockback. `RegisterOverlapping`으로 발사 표면 주변 ground 등록 | 분리 거리(`slimeRadius + launchClearanceThreshold`) 초과 시 즉시, 아니면 `maxIgnoreTime` 상한 | **호출 안 함** → 물리 충돌 유지 (낮은 점프/아래 발사에서도 바닥 관통 없음) |
| `Passthrough` | KnifeHazard body 관통 전용 | 타이머만 (관통 중 거리 흔들림에 영향 받지 않음) | **호출함** + probe 필터 |

둘 다 SurfaceProbe probe 필터 적용.

## 관련 파일
- [IgnoredColliderList.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/IgnoredColliderList.cs)
