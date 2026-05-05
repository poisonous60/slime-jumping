# 로프 V3 (RopeV3 — Trampoline-coupled)

[v1 Rope](rope.md) / [v2 RopeV2](rope-v2.md) 와 동일한 Verlet 시뮬레이션 코어 + EdgeCollider/LineRenderer 파이프라인. **차이는 라이더 결합 모델**:

| 버전 | 라이더 무게 | 착지 임팩트 | 슬라임-로프 결합 |
|------|------------|-----------|----------------|
| v1 (`Rope`) | `riderForceMul × gravity` 를 `riderAccum` 에 주입 | 없음 (`impactGain=0` default) | velocity inheritance (ropeVel) only |
| v2 (`RopeV2`) | 동일하나 `riderForceMul=10` 으로 얕음 | `pos[i] += impactVelocity × dt × impactGain` 1-shot kick | velocity inheritance only |
| v3 (`RopeV3`) | **Hookean spring** — `attach 시점 형태 − riderEquilibriumDepth × falloff` 으로 끌어내림 | spring 의 추가 압축 (`activeImpactDepth`), spring 자체로 감쇠 진동 | **carry channel** — 매 FU 의 contact-point 변위를 `rb.position` 에 직접 적용 |

## 왜 v3 가 필요한가

v2 의 `ApplyRiderImpact` 는 한 FixedUpdate 안에 슬라임의 pre-attach 속도를 통째로 `pos[i] += impulse` 로 써넣음. 6 m/s 낙하 + impactGain=1 + 2 substeps + Jakobsen 전파 → 중앙 노드가 **0.3-0.5 m** 가라앉음.

슬라임 surface-detection budget 은 `slimeRadius + surfaceCheckDistance × attachedCheckMultiplier = 0.4 + 0.05 × 3 = 0.55 m` ([SlimeSettings.cs:144-152](../../../SlimeJumping_v2/Assets/Scripts/Settings/SlimeSettings.cs#L144-L152)). 다음 프레임에 로프가 budget 을 넘어 빠지면 `isTouchingSurface=false` → [SlimeController.Attached.cs:13-30](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Attached.cs#L13-L30) 의 detach 게이트 발동 → 슬라임이 다시 `Airborne`. 또한 `velocity = Vector2.zero` 로 슬라임은 멈춰 있고 로프만 튀어나가 "트램폴린이 같이 내려갔다 같이 올라온다" 는 감각이 안 남.

v3 는 두 가지를 동시에 해결:
1. 로프 노드를 직접 밀어붙이는 1-shot 임펄스 대신 spring force 로 다프레임에 걸쳐 dip 진행 — 디텍션 budget 안에 들어옴.
2. 로프 contact-point 의 매 FU 변위를 슬라임 `rb.position` 에 carry channel 로 직접 적용 — 슬라임이 로프와 함께 내려갔다 함께 올라옴.

## Spring 모델

```
attachPos[i]   = pos[i] at attach time   (snapshot of "rest+gravity" shape)
massScale      = riderMass / referenceMass
targetDepth_i  = (riderEquilibriumDepth × massScale + activeImpactDepth) × falloff_i
currentDepth_i = dot(pos[i] − attachPos[i], gravityDownDir)
springError_i  = targetDepth_i − currentDepth_i
vDown_i        = dot(pos[i] − prevPos[i], gravityDownDir) / dt

accel_i = riderSpringStiffness × springError_i − dampCoeff × vDown_i
        where dampCoeff = 2 × riderSpringDamping × √riderSpringStiffness
pos[i] += gravityDownDir × (accel_i × dt²)
```

`activeImpactDepth` 는 `ApplyRiderImpact` 호출 시 `dot(impactVelocity, gravityDownDir) × impactCompressionFromVelocity` 만큼 누적, 매 substep 에 `√k × ζ` 비율로 지수 감쇠.

평형: `springError = 0` 즉 `currentDepth = targetDepth`. 한 노드는 attach 시점에서 `riderEquilibriumDepth × massScale × falloff` 만큼 아래로 내려간 자리에 정착. Jakobsen tension 이 인접 노드를 끌어 모양이 합쳐짐.

## Carry channel

`IRopePlatform.ConsumeRiderCarryDelta(worldContact)` — V3-only 메서드. v1/v2 는 `Vector2.zero` stub.

```csharp
// RopeV3
public Vector2 ConsumeRiderCarryDelta(Vector2 _)
{
    Vector2 d = carryDeltaAccum;
    carryDeltaAccum = Vector2.zero;
    if (maxCarryPerStep > 0f && d.sqrMagnitude > maxCarryPerStep * maxCarryPerStep)
        d = d.normalized * maxCarryPerStep;
    return d;
}
```

`carryDeltaAccum` 은 `FixedUpdate` 의 substep 직전에 `prevContactSampled = SamplePosAt(seg, t)` 로 baseline 잡고, 모든 substep 후 `newContact - prevContactSampled` 를 더해서 누적.

[SlimeController.cs:~675](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs#L675) 에서 매 FU 한 번 호출하여 `rb.position += carry`. Multi probe mode 의 teleport branch 가 `rb.linearVelocity = Vector2.zero` 처리하므로 carry 가 유일한 tracking 채널 — `ropeVel` (velocity inheritance) 와 이중 적용 없음.

## Launch 반동: v2 그대로

`timingBonusCurve(proj) × chargeMultiplierCurve(chargeRatio)` × `maxReboundSpeed` cap. [v2 doc](rope-v2.md#launch-반동-공식) 참고. V3 의 변화는 **식이 아니라 느낌** — V2 는 슬라임이 정지한 채 로프만 움직였지만, V3 는 슬라임이 로프와 함께 dip-rebound 사이클을 도므로 사용자가 자기 몸의 움직임을 직접 느끼면서 apex 에서 릴리스 → "트램폴린 위에서 발을 굴러 점프" 같은 직관적 감각.

수동 (charge 없는) rebound 자동 점프는 구현하지 않음 — `Attached` 유지하며 사이클 무한 반복. 점프하려면 charge + Space 필요 (V2 와 동일 입력 모델).

## Probe mode 가정

V3 는 **Multi probe mode** 환경에서만 사용. Single mode 에서는 `rb.linearVelocity = finalVel` 이 다음 프레임 위치에 영향을 주어 carry + ropeVel 이중 적용 위험. V3 GameObject 에는 `SurfaceProbeModeOverride` (mode=Multi) 컴포넌트가 자동 부착되어 강제됨 ([RopeTestSetup.BuildRopeV3](../../../SlimeJumping_v2/Assets/Scripts/Editor/RopeTestSetup.cs)).

## 주요 파라미터

| 필드 | 기본값 | 의미 |
|------|------|------|
| `riderEquilibriumDepth` | `0.6 m` | 1 kg 라이더의 정상 sag 깊이 (attach 시점 기준 아래로). |
| `riderSpringStiffness` | `60` | Spring k. 주기 ≈ 2π/√k ≈ 0.8 s. |
| `riderSpringDamping` | `0.3` | 임계 감쇠 비율 ζ. 0=무한 진동, 1=오버슈트 없음, 0.3=한 번 크게 튕기고 정착. |
| `impactCompressionFromVelocity` | `0.04 m/(m/s)` | 임팩트 1 m/s 당 추가 압축. 6 m/s → 0.24 m. |
| `referenceMass` | `1 kg` | equilibrium depth 정규화 기준. |
| `maxCarryPerStep` | `0.25 m` | per-FU carry 안전 캡 (벽 통과 방지). |

(`impactGain`, `riderForceMul` 은 V3 에서 제거 — equilibrium 깊이 기반 spring 으로 대체.)

## 테스트 씬

`SlimeJumping/Mechanism/Setup Rope Test V3 (Trampoline)` 메뉴:

| 로프 | 튜닝 | 의도 |
|------|------|------|
| `Rope_V3_Soft` | `equilibrium=0.3, damping=0.7, impact=0.02` | 얕은 dip + 빠른 안정화. 미니 트램폴린. |
| `Rope_V3_Trampoline` | V3 default | 캐노니컬. 깊은 dip + 활발한 rebound. |
| `Rope_V3_HeavyHammock` | `equilibrium=1.2, k=30, damping=0.5` | 깊은 정상 sag + 느린 rebound. 해먹 느낌. |

세 로프 옆에 회색 height marker 가 +4/+6/+8/+10/+12 m 에 깔림 → charge-launch 정점 높이로 비교.

## 검증 포인트

1. **착지 시 detach 없음** — `currentState` 가 `Charging`/`Attached` 로 전이된 후 dip-rebound 내내 유지.
2. **슬라임이 dip 따라감** — 압축 phase 내내 슬라임 본체가 로프 표면에서 `slimeRadius` 이내.
3. **Rebound 함께 올라감** — dip 바닥 후 슬라임이 로프와 함께 rest line 을 *지나* 올라갔다가 정착.
4. **정상 sag** — 2초 정지 시 contact 노드가 attach 시점 기준 정확히 `riderEquilibriumDepth × (slimeMass / referenceMass)` 만큼 아래.
5. **임팩트 깊이 차이** — 3 m/s vs 10 m/s 낙하: 강한 낙하가 약 0.28 m (= 7 × 0.04) 더 깊은 dip.
6. **Fling 없음** — V2 의 충격 프레임 ropeVel 스파이크 (15+ m/s) 가 V3 에서는 부드러운 ramp.

## 파일

- [Scripts/Platforms/RopeV3.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/RopeV3.cs)
- [Scripts/Platforms/IRopePlatform.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/IRopePlatform.cs) — `ConsumeRiderCarryDelta` 추가
- [Scripts/Controller/SlimeController.cs](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs) — carry block 삽입 (~ line 675)
- [Scripts/Editor/RopeTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/RopeTestSetup.cs) — V3 Trampoline 씬 빌더
