# TautLine (전깃줄 — static-baseline rope)

[Rope V3](rope-v3.md) 의 attach-after-intermediate 진동 버그 ([RopeV3_attach_bug_log.md](../../RopeV3_attach_bug_log.md)) 를 회피하기 위한 대안 로프. RopeV3 와 병존, `IRopePlatform` 인터페이스 동일 → SlimeController 변경 0.

## 왜 만들었나

RopeV3 는 verlet polyline. 라이더가 떠나서 다른 표면에 닿았다가 돌아오면, spring rebound 가 contact node 를 한 FU 안에 `slimeRadius` 보다 더 움직임. SurfaceProbe (ClosestPoint 기반) 가 contact 을 놓치고 슬라임은 Airborne→근처 rope→Attached→다시 놓침 을 반복.

검증: [RopeAttachStateProbe](../../../SlimeJumping_v2/Assets/Scripts/Platforms/RopeAttachStateProbe.cs) 로 정상 도착 vs 진동 도착 시점의 슬라임 상태 (모든 timer / cached collider / carrier 등) 을 diff — **완전히 동일**. 즉 controller 측 상태 누수가 아니라 rope-physics geometry 가 원인.

`Collider2D.ClosestPoint` 는 `EdgeCollider2D.edgeRadius` 무시 → 두께 늘려 anti-tunneling 회피도 효과 없음 (probe 는 여전히 폴리라인 중심선만 봄).

## 핵심 아이디어

> 중력으로 늘어지는 verlet 대신, anchorA→anchorB 사이의 정적 곡선 baseline 을 두고, 슬라임 위치에서만 spring-damped 로 국소 dip 을 추가.

```
baseline(u) = lerp(A, B, u) + worldDown × baselineSag × 4u(1-u)        [정적, 시간 불변]
displaced(u) = baseline(u) + worldDown × dipDepth × kernel(u - riderT) [라이더 위치 주변만]
```

- `baselineSag` 는 인스펙터 직렬화 (m). 0 = 완벽한 직선 (전깃줄). 0.15 default = 살짝 처진 모양.
- `kernel` 은 `kernelHalfWidth` 외부 0, 내부 smoothstep tent (C¹).
- baseline 은 anchor 가 움직이지 않는 한 매 FU 동일 → `ClosestPoint` 결과 안정 → probe 미스 없음.

## Spring 모델

```
target  = riderActive ? riderMass × equilibriumDepthPerKg : 0
accel   = springStiffness × (target - dipDepth) - springDamping × dipVelocity
dipVelocity += accel × dt
dipDepth    += dipVelocity × dt   (≥ 0 으로 클램프 — dip 은 위로 안 튀어나옴)
```

`ApplyRiderImpact(p, impactVel)` 은 `dipVelocity += dot(impactVel, worldDown) × impactCompressionFromVelocity` 로 spring 을 차고, 자연 감쇠 진동으로 트램폴린 바운스. 즉시 dipDepth 를 깊게 만들지 않음 — 그러면 RopeV3 의 1-FU 점프 dip 과 같은 문제 재발.

## IRopePlatform 구현

| 메서드 | 동작 |
|---|---|
| `Edge` | EdgeCollider2D 컴포넌트 그대로 |
| `MaxReboundSpeed` | 직렬화 필드 |
| `ReportRiderContact(col, p, mass, vel)` | `riderActive=true; riderMass=mass; riderT=ParamFromWorld(p)` |
| `NotifyFirstAttach(p)` | `riderT=ParamFromWorld(p)`, `OnRopeEvent(FirstAttach)` |
| `NotifyLaunchRebound(p)` | `dipDepth=0; dipVelocity=0; carryDeltaAccum=0; riderActive=false`, `OnRopeEvent(LaunchRebound)` |
| `ApplyRiderImpact(p, vel)` | `dipVelocity += max(0, dot(vel, worldDown)) × impactCompressionFromVelocity` |
| `SampleVelocityAt(p)` | `worldDown × dipVelocity × kernel(uAtP - riderT)` |
| `GetLaunchBonus(dir, p, ratio)` | RopeV2/V3 와 동식: `clamp(timingBonusCurve(proj) × chargeMultiplierCurve(ratio), 0, maxReboundSpeed)` |
| `ConsumeRiderCarryDelta(p)` | `carryDeltaAccum` 을 atomic 으로 읽고 0. 매 FU `clamp(maxCarryPerStep)`. |

`ParamFromWorld(p)` 는 precomputed `baselinePoints[]` 에 대해 segment-별 closest-point 검색. 32 샘플이면 수십 마이크로초 안에 끝남.

## 왜 이게 진동을 막는가

1. **정적 baseline.** 매 FU 같은 곡선. ClosestPoint 결과 안정.
2. **국소 dip 만 동적.** kernelHalfWidth 외부는 시간 불변. 슬라임이 dip 영역 안에 있을 때만 contact-point 가 움직이고, 그 움직임은 spring dynamics 로 bound 됨 — 디자이너가 `springStiffness/Damping` 으로 매 FU `dipDepth` 변화 < `slimeRadius` 가 되도록 튜닝하면 probe 는 절대 contact 을 놓치지 않음.
3. **Wrong-side normal 모호성 없음.** Verlet 의 gravity-driven 곡률이 사라져서 polyline 이 거의 직선. ClosestPoint 가 위/아래를 일관되게 분류.

> RopeV3 용 SlimeController 의 Attempt 4 (rope normal flip + teleport) 와 Attempt 5 (pre-probe rope position lock) 는 그대로 코드에 남음 — RopeV3 인스턴스가 씬에 있을 수 있음. TautLine 에선 normal-flip 조건과 lock 조건 모두 자연스럽게 안 발동되는 no-op.

## 사용

```
SlimeJumping/Mechanism/Setup TautLine Test
```

`TautLineSetup.cs` 가 Floor + SpawnPad_Soft + TautLine 을 생성. `RopeTestSetup.cs` 무수정.

## 한계 / out-of-scope

- 단일 라이더만. RopeV3 와 동일.
- 로프 자체가 흔들리는 swing/whip 감각 없음. 정적 baseline 이라 안 흔들림.
- 라이더가 anchor 끝에 가까이 있으면 dip kernel 이 anchor sample 까지 도달해서 line endpoint 가 살짝 처져 보일 수 있음. 의도된 동작 (anchor transform 자체는 안 움직임).
- `RopeAttachStateProbe` / `RopeV3Debug` 는 RopeV3 전용. TautLine 의 진동은 시각으로 명백 — 따로 디버그 컴포넌트 필요 없으리라 판단.

## 파일

- [Scripts/Platforms/TautLine.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/TautLine.cs)
- [Scripts/Editor/TautLineSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/TautLineSetup.cs)
- [Scripts/Platforms/IRopePlatform.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/IRopePlatform.cs) (공유 인터페이스, 무수정)
