# 로프 V2 (RopeV2 — Timing 기반 반동)

[v1 Rope](rope.md) 와 동일한 Verlet 시뮬레이션 코어 + EdgeCollider/LineRenderer 파이프라인. **차이는 Launch 반동 메커닉 하나만**:

| 버전 | Launch 보너스 |
|------|---------------|
| v1 (`Rope`) | `sag × reboundSpeedPerMeter`, capped at `maxReboundSpeed` — 타이밍 무관, 차징 누적 sag 만 보면 됨 |
| v2 (`RopeV2`) | `timingBonusCurve(proj) × chargeMultiplierCurve(chargeRatio)`, capped — 줄의 출렁임에 맞춰 발사해야 보너스 |

`SpringyBranchPlatform` 의 angular momentum 전달과 같은 passive-physics 타이밍 감각. 명시적 phase tracker / sweet-spot multiplier 없음 — 곡선 두 개로 디자이너가 응답 모양을 직접 설계.

## Launch 반동 공식

```csharp
ropeVel  = SampleVelocityAt(slimeAttachPoint)         // (pos - prevPos) / fixedDt
proj     = max(0, dot(ropeVel, launchDir))            // 발사 방향 성분만, 음수 컷

timingBonus = timingBonusCurve.Evaluate(proj)         // x: m/s, y: 보너스 m/s
chargeMul   = chargeMultiplierCurve.Evaluate(charge)  // x: 0..1, y: scalar

bonus    = timingBonus * chargeMul
bonus    = min(bonus, maxReboundSpeed)                // 하드캡
velocity += launchDir * bonus
```

**왜 이게 타이밍 감각을 주는가**:
- 슬라임이 줄에 붙어 처지는 동안 → `ropeVel` 이 아래 방향 → `proj = 0` → 보너스 0
- sag 바닥 도달, 줄이 위로 반동 시작 → `ropeVel` 이 launch 방향 → `proj` 피크 → 곡선 출력 최대
- 너무 일찍(아직 처지는 중) / 너무 늦게(감쇠 후) 발사 → `proj` 작음 → 보너스 작음

`chargeRatio` 는 [SlimeController.Launch.cs:91](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Launch.cs#L91) 의 raw `aimOffset.magnitude / chargeRadius` (0..1) 가 그대로 들어옴. `chargeMultiplierCurve` 가 이걸 어떻게 가공할지 결정.

## 곡선 권장 세팅

| 의도 | timingBonusCurve | chargeMultiplierCurve |
|------|---------------|----------------------|
| 타이밍 무관 (테스트용) | flat 0 | — |
| 표준 타이밍 로프 | linear (0,0)→(20,20) | flat 1.0 |
| 차징도 게이팅 | linear (0,0)→(20,20) | ramp (0,0)→(1,1.5) |
| 펀시멍 타이밍 (좁은 sweet) | exponential 모양 — 작은 proj 에선 거의 0, 임계 위에서 급가속 | flat 1.0 |
| 역-차징 (charge 작을수록 좋음) | linear | falling (0,1.5)→(1,0.5) |

곡선은 인스펙터에서 핫리로드 — 플레이 중 수정 즉시 다음 발사부터 반영.

## v1 과 동일한 부분

- `IRopePlatform` 인터페이스 구현 → SlimeController 가 v1/v2 구분 없이 attach + launch
- `SampleVelocityAt(world)` — 라이더 속도 인계용 (밀착)
- `ReportRiderContact` — 라이더 무게 주입
- `OnRopeEvent` (FirstAttach, MaxTension, LaunchRebound)
- `Edge`, `MaxReboundSpeed`, `Rebuild`, `[ContextMenu] Preview Max Sag`
- 모든 시뮬 튜닝 필드 (segmentCount, restLengthMul, constraintIterations, stretchSoftness, damping, simulationSubsteps, zigzagSmoothing, elasticFactor, riderInfluenceSpacing, riderForceMul, etc.)

## v1 과 다른 부분

| v1 | v2 |
|------|------|
| `[SerializeField] float reboundSpeedPerMeter` | `[SerializeField] AnimationCurve timingBonusCurve` |
| `(없음)` | `[SerializeField] AnimationCurve chargeMultiplierCurve` |
| `GetMaxSagDistance()` 가 launch 보너스 입력 | `GetMaxSagDistance()` 는 preview gizmo 전용 (런타임 보너스에서 미사용) |

## 테스트 씬

`SlimeJumping/Mechanism/Setup Rope Test V2 (Timing)` 메뉴:
- `Rope_V2_TimingOff` — `timingBonusCurve` flat 0, baseline (보너스 없음)
- `Rope_V2_LinearTiming` — linear (0,0)→(20,20), charge flat 1.0 (타이밍만)
- `Rope_V2_ChargeGated` — linear timing × charge ramp (0,0)→(1,1.5) (차징+타이밍)

세 로프 옆에 회색 height marker 가 +4/+6/+8/+10/+12 m 에 깔림 → 발사 정점 높이로 곡선 효과 시각 비교.

## 파일

- [Scripts/Platforms/RopeV2.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/RopeV2.cs)
- [Scripts/Platforms/IRopePlatform.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/IRopePlatform.cs) — v1/v2 공유 인터페이스
- [Scripts/Editor/RopeTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/RopeTestSetup.cs) — V2 Timing 씬 빌더
