# TurretHazard (CCTV 확장 사격 해저드)

`CCTVTracker`를 상속/확장한 사격 해저드.

## FSM
```
Idle → Detecting(detectionDelay) → Telegraphing(telegraphDuration) → Cooldown(cooldown) → …
```
`loseSightGrace`로 탐지 히스테리시스.

## "이전 위치 조준" 메커니즘
별도 예측/스냅샷 없이:
- `Telegraphing` 진입 시 `CCTVTracker.enabled = false`로 헤드 고정.
- 그 순간의 헤드 방향(= angularSpeed 제한으로 이미 플레이어보다 뒤처진 위치)이 총구 방향.
- 플레이어는 텔레그래프 동안 안전하게 이동 가능.
- `Cooldown`에서 재활성화되면 각속도 제한 하에 점진적으로 새 위치 따라감.

## 발사
`Physics2D.Raycast(muzzlePos, aimDir, maxShotDistance, bulletHitMask)`.
- **슬라임 명중**: `HazardEffect { knockbackForce, knockbackDirection=aimDir, stunDuration, DamageType.Generic }` 적용. `knockbackForce`는 인스펙터 노출.
- **비명중**: `impactPrefab`(ParticleSystem)을 `Instantiate + Destroy(duration + lifetime + 0.2)`로 자동 해제.

## 피드백 (모두 옵셔널, 미할당 시 조용히 스킵)
- 탐지 단계: `detectionIndicator` 활성 + `detectSound`.
- 텔레그래프: LineRenderer 레이저 사이트 + `telegraphSound`.
- 발사: `muzzleFlash.Play()` + `fireSound`.
- 오디오: `SoundService.PlayAt(clip, muzzlePos, SFX_Object, volume)`로 믹서 라우팅.

## LOS
`lineOfSightBlockers` 레이마스크 지정 시 탐지 단계에서 2D Raycast로 가림 체크 (미지정=스킵).

## CCTVTracker (베이스)
3D `Quaternion.AngleAxis` 추적. rest pose 캡처 + `rotationAxis` / `centerAngle` / `maxAngle` 콘.

## 관련 파일
- [TurretHazard.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/TurretHazard.cs)
- [CCTVTracker.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/CCTVTracker.cs)
