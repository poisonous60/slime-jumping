# 홉 (W 표면 점프)

Attached 상태에서 W를 누르면 작은 점프. 접선 운동량을 보존하면서 살짝 띄우는 표현용 액션.

## 마스터 토글
`SlimeSettings.enableHop` (기본 true) 가 false 면 W 입력이 입력 레이어 (`HandleVerticalUpPressed`) 에서 무시됨. Attached 즉시 홉 / Airborne 코요테 버퍼 양쪽 모두 차단. air-control용 W 홀드 (gravity scale) 는 영향 없음.

## 기본 동작
- `velocity += Vector2.up * hopPower`. 표면 법선과 무관하게 항상 위쪽. 스틱 중에는 `velocity = Vector2.up * hopPower` (충돌 잔여 속도 무시).
- `crawlVelocity = 0` (접선 운동량은 velocity 필드로 이관).
- **각도별 파워**: `hopMinAngle`(70°) 이하 = `hopPower`(2.25), `hopMaxAngle`(90°) = `hopPowerMin`(1.125), 사이 lerp.
- **표면별 배율**: 결과 power는 `SurfaceMaterial.hopPowerMultiplier`(기본 1, AngleScaledValue) 로 곱해짐. 0=홉 불가, >1=강화. 빙판 강한 점프, 진흙 약한 점프 등 표현용.
- **쿨타임**: `hopCooldown`(0.1초). Wall-clock.

## 표면별 접선 보존 (hopVelocityRetention + hopNormalToTangentRatio)
접선 성분과 법선 성분을 독립 노브로 변환:
```
tangentSpeed     = Dot(velocity, surfaceTangent)
normalSpeed      = Dot(velocity, surfaceNormal)
sign             = (tangentSpeed != 0) ? sign(tangentSpeed) : -sign(inputX)
preservedTangent = tangentSpeed * hopVelocityRetention
                 + sign * |normalSpeed| * hopNormalToTangentRatio   // ratio=0이면 추가 항 없음
velocity         = surfaceTangent * preservedTangent + hopDir * power
```
- **hopVelocityRetention** (접선): 1=Ice 유지, 0=Mud 제거.
- **hopNormalToTangentRatio** (법선→접선): 0(기본)=법선 폐기(레거시), 1=법선 속도 전체를 접선으로 슬링샷. 빙판/하프파이프 비행 운동량 변환용.
- stick/non-stick 경로 모두 동일 적용.

## 홉 모멘텀
입력 방향과 현재 속도 방향 비교:
- **같은 방향(또는 정지)**: `boosted = min(currentInDir + hopMomentumBonus, hopMomentumMaxSpeed)` — 가속.
- **반대 방향**: 표면별 분기 (`SurfaceMaterial.hopInstantFlip`):
  - `true` (기본, 아케이드, Grass/Mud): `newTangent = inputSign * min(hopMomentumBonus, hopMomentumMaxSpeed)` — 기존 속도 제거 후 입력 방향으로 즉시 스타트.
  - `false` (관성, Ice): `newTangent = MoveTowards(preservedTangent, 0, hopMomentumBonus)` — 감속만. 0 도달 이후 반대 방향 빌드업.
- 입력 중립이면 보너스 미적용.
- 스틱 중에는 모멘텀 블록 미적용.

**표면별 오버라이드**: `SurfaceMaterial.overrideHopMomentum = true` 시 위 식의 `hopMomentumBonus`/`hopMomentumMaxSpeed`는 SlimeSettings 글로벌이 아닌 **해당 SurfaceMaterial의 값**으로 치환됨. 빙판처럼 가속감을 강화하려면 override on + 두 값 ↑.

## 입력 지연 처리 (Deferred flag)
입력 콜백은 `hopRequested = true`만 set. 실제 `Hop()`은 `FixedUpdateAttached` 끝에서 호출 — velocity, surfaceTangent, surfaceNormal 신선한 상태.

## 홉 버퍼 (코요테)
- Airborne에서 W 누르면 `hopBufferTimer`에 저장.
- Airborne → Attached 착지 시 버퍼 활성이면:
  1. `preserveLandingVelocity = true` → retention `hopLandingRetention`(1.0)으로 강제.
  2. `velocity = surfaceTangent * crawlVelocity` 재구성 (착지 코드가 velocity=0 만들었으므로).
  3. `stickTimer = 0` (stick 우회 — 각도 무관 일관 동작).
  4. `Hop()` 즉시 호출.
- 버퍼 hop은 `hopCooldown` 우회 (의도적 — 벽 등반 사용처).

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| enableHop | true | 마스터 토글. false 면 W 홉 차단 |
| hopPower | 2.25 | hopMinAngle 이하 impulse |
| hopPowerMin | 1.125 | hopMaxAngle에서 impulse |
| hopMinAngle | 70 | 풀 파워 적용 상한 각도 |
| hopMaxAngle | 90 | hopPowerMin 적용 각도 |
| hopCooldown | 0.1 | 쿨타임 (버퍼 홉 우회) |
| hopBufferDuration | 0.1 | 코요테 버퍼 시간 |
| hopLandingRetention | 1 | 홉 착지 접선 속도 보존 비율 |
| hopMomentumBonus | 1.5 | 같은 방향 가산량 |
| hopMomentumMaxSpeed | 20 | 같은 방향 최대 접선 캡 |

## Hop → Energy Conversion 보너스
Hop으로 시작된 비행이 에너지 전환으로 착지 시 `launchedByHop=true` 플래그로 `hopConversionBonus`(1.0, spawnDistance 배율) + `hopConversionEnergyBonus`(0, shadowSpeedSq 가산) 적용.

## 관련
- [surface-material.md](surface-material.md)
- [energy-conversion.md](energy-conversion.md)
