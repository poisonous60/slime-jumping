# TeleportTo (전체 상태 리셋)

`SlimeController.TeleportTo(Vector2 worldPos, Vector2 newVelocity)` 공개 메서드.

위치/속도 설정 + Airborne 강제 전환 + **모든 상태** 초기화. Portal, 체크포인트, 디버그 등에서 사용.

## 수행 작업
- `rb.position = worldPos`, `velocity = newVelocity`, `rb.linearVelocity = newVelocity`
- `ChangeState(Airborne, intentional: true)` — 디바운스 카운터 리셋
- **Charging 초기화**: `aimOffset`, `chargeFullAimTimer`, `coyoteTimer`, `activeChargeRadius`, `driftDirection`, `chargeVisual.Hide()`
- **Carrier**: Detach + surfaceMod 기본값
- **IgnoredColliderList**: Clear (이전 위치 무관)
- **타이머 전체 리셋**: stick, stickDeccel, slideRamp, crawlVelocity, chargeEntryCrawl, postLaunch, hop/dash 버퍼
- `externalDeltaV = Vector2.zero` (이전 위치 외부 힘 잔여 초기화)
- **에너지 추적**: `launchSpeed = |newVelocity|`, `launchHeight = worldPos.y`, `conversionCount = 0`
- **GravityField**: `gravityFieldStack.Clear()` + gravityUp/gravityRight 리셋
- **LowGravityZone**: `ResyncLowGravityZones()` — `rb.position` 대입 **뒤**에 호출. 존 쪽 추적까지 리셋하고 `SyncTransforms` 후 도착지 오버랩을 다시 잡는다 ([../mechanisms/low-gravity-zone.md](../mechanisms/low-gravity-zone.md))

## 관련 파일
- [SlimeController.Teleport.cs](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Teleport.cs)

## 관련
- [../mechanisms/portal.md](../mechanisms/portal.md)
- [../slime/state-machine.md](../slime/state-machine.md)
