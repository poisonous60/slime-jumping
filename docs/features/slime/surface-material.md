# 표면 속성 시스템 (SurfaceMaterial)

표면마다 물리 배율을 커스터마이징. `SurfaceMaterial` (ScriptableObject) + `SurfaceTag` (MonoBehaviour) + `SurfaceModifiers` (struct) 패턴.

## 구조
- `SurfaceMaterial`: 에셋. "빙판", "진흙" 등을 만들어 공유.
- `SurfaceTag`: 기본값과 다른 표면 오브젝트에만 부착하는 마커.
- `SurfaceModifiers`: SlimeController가 읽는 구조체. SlimeController는 `SurfaceMaterial`을 직접 참조하지 않음 (간접 읽기).
- SurfaceTag 없는 표면 = 모든 배율 1.0 (기본 동작).

## AngleScaledValue
모든 파라미터는 `AngleScaledValue` 타입. 표면 각도에 따라 값을 보간.
```
struct AngleScaledValue {
    baseValue   // angleMin 이하에서의 값
    fullValue   // angleMax 이상에서의 값
    angleMin    // 보간 시작 각도 (0° = 바닥)
    angleMax    // 보간 끝 각도 (90° = 벽)
}
// Evaluate(surfaceAngle) = Lerp(baseValue, fullValue, InverseLerp(angleMin, angleMax, surfaceAngle))
// Constant(v) = 모든 각도 동일 값 (하위 호환)
```

## 조회 타이밍
- **착지 시** (carrier.Attach 시점): `GetComponentInParent<SurfaceTag>()` 한 번 조회 → 캐싱 (`hostSurfaceMod`).
- **Airborne 전환 시**: `settings.BuildDefaultModifiers()`로 리셋 (이전엔 하드코딩 `SurfaceModifiers.Default`).
- **표면 전환**: `FixedUpdate`에서 `surfaceCollider != prevSurfaceCollider`일 때 재호출.
- **Snow 결합 (Phase 7)**: 매 FixedUpdate, attached/charging 이고 host 가 SnowField 등록되어 있으면 `RefreshSnowBlend` 가 contact 점의 carve depth (`SnowFieldRegistry.SampleDepthRatio01`) 를 샘플 → `SnowSettings.depthToSnowBlend` curve 평가 → `SurfaceModifiers.Lerp(host, snow, t)` 로 슬라임이 읽는 `surfaceMod` 갱신. 같은 호스트 위 벽 (눈 0%) 과 평지 (눈 풀) 가 다른 마찰을 갖게 됨. discrete fields (useInertialSlide, override flags, curves) 는 host 값으로 pin — mid-attach 모델 flip 금지.

## SurfaceModifiers.Lerp / AngleScaledValue.Lerp (Phase 7)
두 surface 프로필 사이를 0..1 weight 로 component-wise lerp 하는 helper. 모든 `AngleScaledValue` 필드는 4 scalar (baseValue/fullValue/angleMin/angleMax) lerp, 일반 float 필드는 lerp, bool/curve 는 t≥0.5 snap. snow 결합이 첫 사용처 — 향후 다른 layered surface (얼음/이끼 위에 눈 등) 도 동일 패턴 재사용 가능.

## 글로벌 디폴트 (SlimeSettings)
SurfaceTag 가 없거나 material이 비어있는 표면은 `SlimeSettings`의 "Default Surface Modifiers" 섹션 값을 사용. 이전엔 `SurfaceModifiers.Default` (하드코딩 1.0/0.0)를 썼지만 이제 인스펙터에서 조정 가능. 동일한 필드 셋 (carrierFriction, crawlSpeedMultiplier, ..., hopVelocityRetention, hopNormalToTangentRatio, hopInstantFlip, useInertialSlide) + dash 관련 (`groundDashAngleFalloffCurve`, `dashSpeedFalloffCurve`, `disableDashFalloff`)을 SlimeSettings 에서 직접 설정 가능. 태그 없는 표면 = SlimeSettings 값 그대로, 태그 있고 override off = SlimeSettings dash 값 fallback (impulse/min/max/falloff/curve/disable 모두), 태그 있고 override on = SurfaceMaterial 값.

## 파라미터
| 파라미터 | 기본 | 설명 |
|---------|------|------|
| carrierFriction | 1 | 플랫폼 속도 전달 (0=얼음, 1=완전) |
| crawlSpeedMultiplier | 1 | 크롤 속도 |
| crawlAccelerationMultiplier | 1 | 크롤 가속 |
| crawlDecelerationMultiplier | 1 | 크롤 감속 (낮=관성 큼) |
| crawlOverspeedDecelerationMultiplier | 1 | 초과속 감속 |
| slideResistanceMultiplier | 1 | 슬라이드 저항 (0=없음, 3=진흙). 관성 모드에서는 쿨롱 마찰 (m/s², 속도 무관) |
| slideGravityMultiplier | 1 | 슬라이드 중력 |
| slideRampUpTime | 0 | 슬라이드 램프업 (0=즉시, SlimeSettings 기본 fallback) |
| slideLinearDrag | 0 | **관성 모드 한정.** 속도 비례 드래그 (1/s). `slideVelocity *= (1 - k*dt)`. 빠를수록 더 깎임 → 하프파이프 climb < drop |
| slideDownhillGain | 1 | **관성 모드 한정.** `slideAccel`이 운동 방향과 같을 때 곱하는 게인. >1 = 내리막 가속 강화 |
| slideUphillBrake | 1 | **관성 모드 한정.** `slideAccel`이 운동 방향과 반대일 때 곱하는 게인. >1 = 오르막 감속 강화 |
| landingVelocityRetention | 0 | 착지 접선 보존 (0=정지, 0.8=빙판) |
| stickDurationMultiplier | 1 | 스틱 배율 (0=빙판) |
| impactRedirectRatio | 0 | 법선 충돌 → 접선 전환 (벽에서만 자동 활성) |
| wallAttachSpeedDampen | 1 | 착지 접선 속도 감쇠 |
| groundDashScale | 1 | 대시 impulse 배율 (0=대시 불가) |
| hopVelocityRetention | 1 | 홉 시 접선 성분 보존 비율. `Dot(velocity, surfaceTangent) × retention`. 1=ice, 0=mud |
| hopNormalToTangentRatio | 0 | 홉 시 법선 성분 → 접선 변환 비율. `\|Dot(velocity, surfaceNormal)\| × ratio`만큼 접선에 가산. 부호는 기존 접선 성분, 0이면 입력 방향 폴백. 0(기본)=법선 성분 폐기(레거시), 1=법선 속도 전체를 접선으로 슬링샷. 빙판/하프파이프에서 비행 후 표면 미끄러짐 변환에 사용 |
| hopInstantFlip | true | 홉 반대 방향 즉시 반전 (true=mud/grass, false=ice 관성) |
| **overrideHopMomentum** | false | true 시 아래 두 값으로 SlimeSettings의 `hopMomentumBonus`/`hopMomentumMaxSpeed`를 표면 단위로 덮어씀. false면 글로벌 SlimeSettings 사용 |
| hopMomentumBonus | 1.5 | (override 시) 같은 방향 입력 홉당 가산되는 접선 속도 |
| hopMomentumMaxSpeed | 20 | (override 시) 홉 모멘텀 누적 최대 접선 속도 캡 |
| **overrideGroundDash** | false | true 시 아래 5개 값으로 SlimeSettings의 ground-dash 튜닝을 덮어씀. false면 글로벌 SlimeSettings 사용. `groundDashScale` 곱셈은 override와 무관하게 항상 적용 |
| groundDashImpulse | 8 | (override 시) 대시 임펄스 |
| groundDashAngleMin | 30 | (override 시) 각도 falloff curve의 입력 도메인 시작 (deg) |
| groundDashAngleMax | 60 | (override 시) 각도 falloff curve의 입력 도메인 끝 (deg) |
| groundDashAngleFalloffCurve | Linear (0,1)→(1,0) | (override 시) `t = InverseLerp(angleMin, angleMax, surfaceAngle)` 후 이 곡선이 t∈[0,1]을 angleScale로 매핑. 기본은 선형이라 레거시(`1 - t`)와 동일. ease-out, 절벽형, 다단 cliff 등 자유 표현 가능 |
| dashFalloffStart | 5 | (override 시) 속도 falloff curve의 입력 도메인 시작 (m/s) |
| dashFalloffEnd | 13 | (override 시) 속도 falloff curve의 입력 도메인 끝 (m/s) |
| dashSpeedFalloffCurve | Linear (0,1)→(1,0) | (override 시) `t = InverseLerp(dashFalloffStart, dashFalloffEnd, sameDirSpeed)` 후 이 곡선이 t∈[0,1]을 falloff로 매핑. 기본은 선형이라 레거시(`1 - t`)와 동일. ease-out, plateau, 다단 cliff 자유 표현 |
| disableDashFalloff | false | (override 시) true면 dashSpeedFalloffCurve를 무시하고 항상 풀 임펄스 적용 — 빙판에서 속도 무한 누적용 |
| useInertialSlide | false | 관성 슬라이드 사용 (true=ice/halfpipe — 운동에너지↔높이 보존, false=기존 정상상태 슬라이드) |
| **overrideCeilingAngle** | false | true 시 아래 두 값으로 SlimeSettings 의 `ceilingAngle` / `ceilingReattachAngle` 을 표면 단위로 덮어씀. false 면 글로벌 SlimeSettings 사용. 끈끈이 표면(180°=오버행 전부 부착) / 미끄러운 표면(예: 95°에서 이미 천장) 같은 표현에 사용 |
| ceilingAngle | 107 | (override 시) 분리/필터 각도. `surfaceUp < cos(this)` 인 컨택트는 천장으로 처리되어 부착 불가 + Step 1.5 / EffectiveNormal 에서 제외 |
| ceilingReattachAngle | 101 | (override 시) Airborne 재부착 각도. `surfaceUp < cos(this)` 면 착지 시 부착 안 함. 히스테리시스 위해 ≤ ceilingAngle 권장 |

## 표면별 상호작용
- **바닥** (surfaceUp > slopeThreshold): 슬라이드 없음, 스틱 없음. 안정적.
- **벽** (surfaceUp ≈ 0): 중력으로 아래 슬라이드. 크롤로 위아래 이동 (중력보다 느린 속도). 스틱 타이머 적용.
- **급경사** (slopeThreshold ~ ceilingThreshold): 기울기 비례 슬라이드. 차징 중에도 미끄러짐. 스틱 적용.
- **천장** (surfaceUp < ceilingThreshold): 부착 불가. 법선 속도만 상쇄, Airborne 유지.
  - 표면별 오버라이드: `SurfaceMaterial.overrideCeilingAngle` 켜면 해당 표면만 다른 천장 각도 사용. 컨택트 단위로 적용되므로 다른 두 표면이 한 코너에서 만나도 각자 자신의 임계값으로 판정.

## 예시 조합
| 표면 | carrierFriction | slideResistance | crawlSpeed | retention | stickDuration | impactRedirect | useInertialSlide | 느낌 |
|------|----|----|----|----|----|----|----|------|
| 기본 | 1.0 | 1.0 | 1.0 | 0 | 1.0 | 0 | false | 기본 |
| 빙판 | 0.2 | 0.1 | 1.0 | 0.8 | 0 | 0.5 | true | 미끄러움, 벽 타기, 하프파이프 KE 보존. `overrideHopMomentum=true` + bonus/maxSpeed ↑, `overrideGroundDash=true` + falloffEnd ↑로 가속감 강화 |
| 진흙 | 1.0 | 3.0 | 0.5 | 0 | 1.0 | 0 | false | 느리고 안 미끄러움 |

> 빙판 행의 `slideResistance`(0.1)는 관성 모드에서 **친선 마찰 (m/s²)** 단위로 해석됨 — 아래 [관성 슬라이드 모드](#관성-슬라이드-모드-useinertialslide) 참조.

## 관성 슬라이드 모드 (useInertialSlide)

기본(false): 슬라이드는 매 프레임 `gravAlongTangent`에서 새로 계산 — 정상 상태 / drag-dominant. 접선 방향 KE 누적 없음. 하프파이프에서 내려갔다 올라가면 운동 에너지가 보존되지 않아 같은 높이까지 못 올라감.

켜면(true): 슬라이드 채널이 관성을 가짐.
```
slideAccel     = gravAlongTangent * slideGravityScale * slideGravityMultiplier   // [m/s²]
slopeGain      = (sign(slideAccel) == sign(slideVelocity)) ? slideDownhillGain
                                                            : slideUphillBrake    // 1=중립
slideAccel    *= slopeGain                                                        // C: 비대칭 게인
slideVelocity += slideAccel * dt                                                  // 누적
slideVelocity *= max(0, 1 - slideLinearDrag * dt)                                 // B: 속도 비례 드래그
frictionDecel  = slideResistance * slideResistanceMultiplier                      // [m/s²] 쿨롱 마찰
slideVelocity  = MoveTowards(slideVelocity, 0, frictionDecel * dt)
```
- **`slideResistanceMultiplier`의 단위가 바뀜**: 정상상태 모드에서는 슬라이드 속도에서 빼는 값이지만, 관성 모드에서는 마찰 가속도 (m/s²). Ice는 0.1 → 약 0.2 m/s² 마찰.
- **`slideLinearDrag`(B)**: 속도 비례 감속. 0 = 비활성. 0.5–1.5 정도면 마일드 — 하프파이프 climb 이 drop 보다 짧아져 "에너지 빠지는 빙판" 느낌. KE↔PE 보존을 깨고 싶을 때 사용.
- **`slideDownhillGain` / `slideUphillBrake`(C)**: `slideAccel`이 현재 운동 방향과 같으면 downhillGain, 반대면 uphillBrake가 곱해짐. 둘 다 1이면 비활성. 내리막 가속과 오르막 감속을 독립 튜닝 — 비물리적이지만 강한 게임필 표현 가능. B 와 직교(B는 운동에 비례, C는 중력 접선에 비례)이므로 함께 사용 가능.
- **`slideThreshold` 데드존 미적용**: 하프파이프 바닥(접선 중력≈0) 근처에서도 KE 보존되도록.
- **slideRampUp 미적용**: 중력 가속이 자연스럽게 누적되므로 별도 램프 불필요.
- 착지 시 접선 속도는 `slideVelocity`로 들어감 (`crawlVelocity`로 가는 기본 모드와 다름) — 마찰 외에 감쇠가 없어 운동량을 유지.
- `crawlVelocity`는 그대로 입력 가속/감속만 담당. 최종 접선 속도 = `crawlVelocity + slideVelocity`.
- 두 모드 표면 사이를 기어다닐 때 `UpdateSurfaceModifiers`가 채널 간 접선 속도를 자동 이전 — 운동량 손실 없음.
- Hop / Hyper-dash / Ground-dash 모두 합산 접선 속도를 읽어 자연스럽게 호환.

## 값 검증
- `SurfaceMaterial.OnValidate()`: 음수 방지, friction/retention은 0~1로 클램핑.
- `SlimeSettings`: `chargeRadius`, `baseLaunchPower`에 `[Min(0.01f)]`, `initialConversionRate`에 `[Min(0f)]`.

## 관련 파일
- [SurfaceMaterial.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceMaterial.cs)
- [SurfaceTag.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceTag.cs)
- [SurfaceModifiers.cs](../../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceModifiers.cs)
