# 탄력 나뭇가지 (Springy Branch)

한쪽 끝이 고정된 가지. 라이더가 올라타거나 슬램 착지하면 반대 끝이 휨 → 스프링 복원으로 진동. 발사 타이밍에 따라 `AngularVelocity × arm`이 `SlimeController.Launch`에 자동 합산되어 점프 높이 증폭 (타이밍이 차지량과 별개의 스킬 축).

## 물리 모델
- 상태: `currentAngle` (쉬는 각도 기준 편차, deg), `angularVel` (deg/s), `pivotWorldPos` (Awake 캡처, 고정), `baseRotation` (Awake 에서 `rb.rotation` 캡처 → 피벗의 초기 회전이 쉬는 방향).
- 각 스프링-댐퍼: `α = (−k·θ − c·ω + pendingRiderTorque) / I_total`.
- 디자이너는 raw `k, c` 대신 `naturalFreqHz`, `dampingRatio`로 조절 → 내부에서 `k = ω² · I_total`, `c = 2ζω · I_total` 도출.
- `I_total = I_branch + m_rider · arm²` (라이더 있을 때 관성모멘트 증가 → 주파수 유지).
- 출력: `(pivotWorldPos, baseRotation + currentAngle)`. 위치는 고정, 회전만 변함 → `PlatformCarrier`가 `AngularVelocity × arm`만 정확히 전달 (이중 계산 방지).
- 가지 방향은 씬에서 BranchPivot 오브젝트를 직접 회전해 설정 (별도 `restAngleDeg` 파라미터 없음).

## 에너지 유입 — Δp 기반 통합 펌핑
슬램 착지와 몸 흔들기를 하나의 물리 경로로 처리:
- 매 틱 `dv = riderVelocity - previousRiderVel` (첫 접촉 시 `dv = riderVelocity`로 슬램 임펄스).
- `angularImpulse = (r × dv · mass) / I_total · riderPumpGain` (2D cross product, 각도 무관 정확).
- 차지 중에는 `riderPumpGain × chargePumpScale`로 별도 배율.
- 재착지 펌핑 방지 가드: `slamReattachLockout` + `slamMinDownSpeed`.
- 착지 후 `previousRiderVel = 0` 초기화 (SlimeController가 landing 시 `velocity = 0`으로 리셋하는 비연속 변화에 의한 가짜 반대 임펄스 방지).

## 에너지 배출 — 독립 감쇠
- 스프링 감쇠(`dampingRatio`)와 별개로 `angularVel *= exp(-decayRate · dt)` 추가.
- `riderDecayRate` (라이더 있을 때) vs `passiveDecayRate` (없을 때) 분리.

## 하드 캡
`maxTipSpeed` — `|angularVel| · branchLength` 상한. 런치 보너스 천장을 m/s 단위로 지정.

## 발사 시 에너지 합산
```
velocity = chargeDir × power                                   // 차지 파워
velocity += carrier.GetEffectiveVelocity() × carrierFriction   // 가지 팁 속도
```
고공 낙하 → 에너지 전환 확장된 차지 반경 + 슬램으로 휜 가지의 업스윙 피크 타이밍 = 최강 런치 벡터. 벡터 합산이라 발사 방향과 팁 속도 방향이 일치할 때만 완전 합산 (기본적으로 수직 점프 퍼즐).

## 오브젝트 계층 (중요)
```
BranchPivot   (월드 고정, 회전만. Rigidbody2D + SpringyBranchPlatform + SpringyBranchVisual)
  └─ BranchBody   (+halfLength 오프셋 자식, BoxCollider2D + 비주얼 메시)
  └─ Bone_0..N    (비주얼 휨 전용 본 체인, BranchPivot 직접 자식 — 비균일 스케일 상속 방지)
```
피벗이 원호를 그리면 `motor.Velocity + AngularVelocity × arm`이 이중 계산됨. 피벗은 제자리에서 회전만.

## 비주얼
`SpringyBranchVisual`: `Transform[] bones` 체인을 `AnimationCurve bendWeight`로 가중 분배하여 `motor.CurrentDeflectionDeg` 기반 local rotation 적용. `visualLag` 저역통과 — 비주얼이 콜라이더보다 살짝 뒤처짐.

## ApplyRiderInfluence 시그니처
`ApplyRiderInfluence(bool isCharging)` 파라미터를 받음 (PlatformMotor 시그니처 확장). PushDoor 등은 `riderVelocity` 파라미터도 받음 — [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md) 참조.

## Surface Material 오버라이드
인스펙터의 `branchSurfaceMaterial` 슬롯에 `SurfaceMaterial`을 지정하면 `PlatformCarrier`가 Attach 시 `slime.PushSurfaceMaterialOverride`, Detach 시 Pop 으로 자동 적용 (WaterCurrentZone 패턴과 동일). `SurfaceMaterial.overrideEnergyConversionCoyoteTime` 등의 표면별 튜닝(예: 튕기는 가지에서 에너지 전환 코요테 시간 연장)을 SlimeSettings 글로벌 변경 없이 표면 단위로 지정.

API: `PlatformMotor.OverrideSurfaceMaterial` virtual 프로퍼티 → `SpringyBranchPlatform`이 `branchSurfaceMaterial` 필드를 노출. `null`이면 호스트 SurfaceTag만 사용 (오버라이드 미적용).

## 한계
- 단일 라이더 가정.
- `pivotWorldPos`는 Awake에서 캡처 — 런타임 이동 불가.
- `branchLength`는 런타임 변경 시 물리는 반영되지만 콜라이더/본 위치는 setup 시점에 박힘.
- `chargePumpScale`은 실효성 낮음 — Charging 상태에서 `velocity ≈ 0` 유지해 Δp 거의 0. 차지 중 자유 이동 메커닉 추가되면 활성화됨.

## v2 변형 — Bend 구동 (SpringyBranchPlatform_v2) — **사용 안 함 (deprecated)**
MegaFiers Bend 기반 재구성 실험. 코드는 남아 있으나 플레이 느낌 기준에 못 미쳐 폐기. 신규 작업에서 참고하지 말 것 — v1 `SpringyBranchPlatform`를 사용하거나 새 구현을 고려.
