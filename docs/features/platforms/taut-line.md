# TautLine (전깃줄 — taut elastic wire)

[Rope V3](rope-v3.md) 가 5번의 수정 시도에도 attach 진동 버그 ([RopeV3 attach 버그 기록](../../bug/ropev3-attach.md)) 를 못 잡아서, 처음부터 다시 짠 로프. RopeV3 와 병존, `IRopePlatform` 인터페이스 동일 → SlimeController 변경 0.

## 디자인 의도

전깃줄 같은 **탄성 직선 줄**. 라이더 없으면 거의 직선 (살짝 정적 sag), 라이더가 타면 **줄 전체가 부드럽게 휨** (라이더 위치에서 가장 깊음, anchor 까지 부드럽게 감소). 라이더가 떠나면 부드럽게 baseline 으로 복귀, 진동으로 다시 라이더를 잡지 않음.

**RopeV3 와의 핵심 차이:**

| 항목 | RopeV3 | TautLine |
|---|---|---|
| 모델 | Verlet polyline (다중 노드 시뮬) | Single 1D spring-damper on `dipDepth` |
| 라이더 무게 | 노드들에 분산 spring force | 줄 전체 모양에 곱해지는 단일 스칼라 |
| Collider | EdgeCollider2D (zero-thickness) | PolygonCollider2D (trigger ribbon, 두께 0.1m) |
| Slime tracking | `ConsumeRiderCarryDelta` (delta 누적, clamp) | `rb.position` 직접 절대 좌표 set |
| Underside attach | 발생 (Attempt 4 normal-flip 으로 우회) | 구조적으로 불가 (one-way) |
| Launch 후 진동 | Verlet 노드 잔여 모멘텀, slime 다시 잡음 | Overdamped 모드, baseline 으로 부드럽게 복귀 |
| 점프 boost | `timingBonusCurve(ropeVel) × chargeMultiplierCurve(charge)` | `jumpBoost × chargeRatio × upwardComponent`, `maxCap` clamp, **pre-bonus speed falloff** 로 EnergyConversion 발사일 땐 boost 0 |
| Charge 시 줄 깊이 | charge 동안 점진적으로 깊어짐 | charge 동안 baseline 유지, **발사 순간** dipVelocity kick (curve(ratio)) |
| 옆 점프 안전장치 | 없음 | `minLaunchVerticalSpeed` 로 launch 후 최소 worldUp 속도 보장 |

## 곡선 모델

라이더가 있는 경우 줄 모양:

```
shapeFactor(u)  = 4u(1 - u)                                    [가운데서 1, anchor 에서 0]
baseDepth       = baseEquilibrium + chargeExtraDepth × ChargingProgress
target          = baseDepth × shapeFactor(riderT)              [Spring 평형 깊이]

baseline(u)     = lerp(A, B, u) + worldDown × baselineSag × shapeFactor(u)   [정적 sag]
displaced(u)    = baseline(u) + worldDown × dipDepth × Bell(u, riderT)        [라이더 dip]
```

**Bell(u, riderT)** = 라이더 위치에서 정확히 1, 양 anchor 에서 0, C¹-smooth (sharp kink 없음). 두 개의 smoothstep 을 라이더 위치에서 join:

```
if u ≤ riderT:  Bell = smoothstep(u / riderT)
else:           Bell = smoothstep((1 - u) / (1 - riderT))
```

곡선 가장 깊은 지점이 정확히 라이더 위치. 라이더가 anchor 가까이 있으면 한쪽은 짧고 가파르고, 반대쪽은 길고 완만한 곡선 — tightrope 와 동일.

`shapeFactor(u)` 가 baseline 과 dip 양쪽에 들어가서, anchor 에선 둘 다 0 → endpoint 가 anchor 위치에 정확히 고정.

## Spring (1D 스칼라)

`dipDepth` 만 시뮬. 라이더 위치 (riderT) 가 변해도 dipDepth 한 변수가 모든 시간 변화 표현.

```
accel        = springStiffness × (target - dipDepth) - dampingCoeff × dipVelocity
dampingCoeff = 2ζ × √springStiffness                          [표준 1차 시스템]
dipVelocity += accel × dt
dipDepth    += dipVelocity × dt                                [no clamp — 위로 튈 수도 있음]
```

`ζ` 는 슬라임 상태 + trampoline 활성 여부에 따라 전환:

| 조건 | ζ (damping ratio) | 효과 |
|---|---|---|
| `trampolineHoldTimer > 0` (trampoline 진행 중) | `trampolineDampingRatio` (default 0.15) | 매우 underdamped — 깊은 target 주변에서 위아래로 4~5번 통통 |
| `Attached` / `Charging` (trampoline 비활성) | `attachedDampingRatio` (default 0.5) | 살짝 underdamped — 착지 시 dip 한 번 overshoot 후 정착 |
| `Airborne` (탈출 직후) | `detachedDampingRatio` (default 1.2) | Overdamped — baseline 으로 부드럽게 복귀, 위로 튀어 슬라임 못 잡음 |

`postDetach` 플래그가 detach 순간 켜지고, dipDepth + dipVelocity 가 settle 임계값 아래로 내려가면 자동 해제.

### Hard-landing trampoline (impulse-driven bouncing)

빠르게 떨어진 슬라임이 줄에 닿으면 줄이 baseline 주위로 비대칭 진동 (아래 깊게, 위 덜 깊게, 점점 작아짐). target 은 그대로 `baseEquilibrium` — 이동하지 **않음**. 충돌이 dipVelocity 에 임펄스를 주고, 스프링이 baseline 으로 끌어당기다 underdamped 라 baseline 통과해서 **위로 overshoot**.

| 곡선 | 의미 |
|---|---|
| `trampolineImpactKickCurve` | `vDown → 초기 dipVelocity 임펄스 (m/s, downward)` — 첫 dip 깊이를 결정 |
| `trampolineHoldCurve` | `vDown → 진동 지속시간 (s)` — `trampolineDampingRatio` 적용 윈도우 |

**메커니즘:**
1. `ApplyRiderImpact(impactVelocity)` (착지 순간) → 두 곡선 evaluate.
2. `dipVelocity += impactKickCurve(vDown)` (downward 임펄스), `trampolineHoldTimer = max(prev, holdCurve(vDown))`.
3. FixedUpdate target = `baseEquilibrium × shape` (변경 없음).
4. `trampolineHoldTimer > 0` 동안 ζ = `trampolineDampingRatio` (underdamped, default 0.15). 스프링이 baseline 주변에서 비대칭 진동:
   - 첫 peak (아래): 가장 깊음 (impactKick 직접 결정)
   - 두 번째 peak (위): 첫 peak 의 ~62% 만큼 baseline 위로
   - 세 번째 peak (아래): ~38%
   - decaying ringing
5. timer 만료 시 ζ = `attachedDampingRatio` (0.5) 로 복귀 → 빠르게 정착.
6. detach 시 timer 리셋.

**이전 모델과 차이:** 이전엔 target 자체를 `baseEq + extraSag` 로 깊게 옮겨서 스프링이 그 deep 점 주위에서 진동 → baseline 위로 절대 못 감. 임펄스 모델에선 target 이 baseline 이라 진동이 자연스럽게 baseline 을 가로질러 위로 올라감.

**비대칭이 자연스러운 이유:** underdamped 시스템의 step-velocity 응답에서 각 peak 는 이전 peak × `e^(-ζπ/√(1-ζ²))` 로 감쇠. ζ=0.15 → ~0.62 비율. 그래서 "내려간 만큼 위로 올라가지 않음" 이라는 사용자 직관이 그대로 구현됨.

기존 `impactCompressionFromVelocity` (dipVelocity kick) 도 그대로 동시에 fire — 작은 punch + 깊은 bowl 조합. punch 만 원하면 sagCurve / holdCurve 의 Y 값을 0 으로 깔면 되고, bowl 만 원하면 `impactCompressionFromVelocity = 0` 으로.

### Trampoline 메커니즘 (충전 → 발사)

```
state = Attached:  target = baseEquilibrium × shapeFactor(riderT)
state = Charging:  target = baseEquilibrium × shapeFactor(riderT)        [동일]
state = Airborne:  target = 0
```

**charge 중엔 줄이 깊어지지 않음.** 깊어짐은 발사 순간 한 번에 일어남 — `GetLaunchBonus` 안에서:

```csharp
dipVelocity += launchImpactKickCurve.Evaluate(chargeRatio)
```

charge 가 천천히 줄을 누르는 게 아니라, 슬라임이 차고 박차는 순간 줄이 punch 되는 느낌. `launchImpactKickCurve` 의 Y 값이 m/s 단위 dipVelocity kick (default linear `(0,0)→(1,4)` → full charge 시 4 m/s 하향 펄스).

## 점프 boost (Effect-mode)

```csharp
// GetLaunchBonus — launchDir 방향 스칼라 보너스 (timing + pre-bonus speed gated)
ropeUpSpeed = max(0, -dipVelocity)                          // 줄 위쪽 속도 (m/s)
timing      = jumpTimingCurve.Evaluate(ropeUpSpeed)         // multiplier
upwardComponent = max(0, dot(launchDir.normalized, worldUp))
bonus = jumpBoost × clamp01(chargeRatio) × upwardComponent × max(0, timing)
clamped = clamp(bonus, 0, maxCap)

// pre-bonus speed gate — slime.Velocity.magnitude 에는 EnergyConversion power +
// carrier inheritance + hyperdash 가 다 들어있고, 여기에 rope bonus 를 더할지 말지 결정
preBonusSpeed = slime.Velocity.magnitude
speedMul = 1 - clamp01(invLerp(boostFalloffStartSpeed, boostFalloffEndSpeed, preBonusSpeed))
return clamped × speedMul

// PostprocessLaunchVelocity — 최종 launch vector 후처리
upSpeed = dot(launchVel, worldUp)
if (upSpeed < minLaunchVerticalSpeed)
    launchVel += worldUp × (minLaunchVerticalSpeed - upSpeed)
```

**타이밍 게이트:** 줄이 위로 빠르게 움직일 때 (= release 후 baseline 통과하는 순간 부근) 점프하면 max boost. 정지 중이거나 아래로 가라앉는 중엔 boost 0. 디자이너는 `jumpTimingCurve` 의 X축 (rope 위쪽 속도, m/s) 과 Y축 (multiplier) 을 자유롭게 조정 — Linear 로 두면 시간 동기화 정확도가 그대로 결과로, EaseIn 이면 정점 근처만 큰 보너스, 평면 (Y=1) 이면 timing 무시.

**pre-bonus speed 게이트 (높이 제한):** "TautLine 으로 더 높이 올라가는 거 막기" 는 최종 magnitude clamp 가 아니라 **rope bonus 만 깎아 없애는 식**. 슬라임이 이미 빠르게 움직이고 있으면 (예: 높은 곳에서 떨어져 EnergyConversion 으로 큰 power 누적) `boostFalloffEndSpeed` 이상에선 rope 가 아무것도 안 더함 → 슬라임은 자기 EnergyConversion power 그대로 발사 (= 그 높이까지 자연스럽게 올라감). 일반 charge 발사는 pre-bonus 가 작아서 full boost 받음. `boostFalloffEndSpeed ≤ boostFalloffStartSpeed` 면 hard cutoff.

디자이너가 다루는 변수:

| 변수 | 단위 | 의미 |
|---|---|---|
| `jumpBoost` | m/s | full charge 시 launchDir 방향으로 더해지는 양 (timing × chargeRatio 가 곱해짐) |
| `maxCap` | m/s | jumpBoost (rope bonus) 의 하드 캡 — boost 항만 제한, 최종 launch 속도와 무관 |
| `jumpTimingCurve` | curve(ropeUpSpeed→mul) | 줄 상승 속도별 boost multiplier. default `Linear((0,0)→(5,1))` |
| `minLaunchVerticalSpeed` | m/s | 모든 launch 의 최소 worldUp 속도 (chargeRatio 무관 일정). 0 = disable |
| `boostFalloffStartSpeed` | m/s | pre-bonus 속도 이하에선 full boost. default 12 |
| `boostFalloffEndSpeed` | m/s | pre-bonus 속도 이상에선 boost 0. default 18. start↔end 사이 선형 falloff. EnergyConversion (= 슬라임 자체 power) 은 영향 없음 |
| `launchImpactKickCurve` | curve(ratio→m/s) | 발사 순간 줄이 받는 downward dipVelocity 펄스 (시각 효과) |

`minLaunchVerticalSpeed` 가 필요한 이유: 슬라임이 옆으로 점프 (수평 launchDir) 하면 vy ≈ 0. 줄은 동시에 spring back 으로 위로 튀어오름 → 슬라임과 다시 만나 re-attach. 최소 vy 를 보장하면 슬라임이 줄의 rebound envelope 위로 빠져나감.

**RopeV3 의 timing curve 모델 폐기.** 디자이너가 spring 물리 이해 안 해도 숫자로 점프 높이 튜닝 가능.

## One-way platform (위에서만 attach)

PolygonCollider2D 가 trigger 라 Unity 물리 충돌은 안 일어남. SurfaceProbe 측에서만 contact 등록. 슬라임이 아래에서 위로 올라올 땐 attach 막아야 함.

**from-below 판정은 raw `Velocity.y` 가 아니라 위치(어느 쪽 면에 있나) + 속도 방향으로 한다.** 옛날엔 `Velocity.y > 0f` 한 줄로 판정했는데, 옆 플랫폼에서 **빠르게 수평으로 미끄러져 들어오는** 슬라임은 수평 속도가 크고 vy 가 살짝만 + (faceted terrain 의 tangent 기울기 / jitter) 라서 "아래에서 올라온다"로 오판 → false Passthrough 등록 → SurfaceProbe 가 transit 내내 polygon 을 건너뛰어 **그대로 통과**하는 버그가 있었다.

```csharp
void OnTriggerEnter2D(Collider2D other) {
    // aboveDistance : 슬라임 중심이 줄 top edge 위로 떨어진 부호 있는 수직거리 (baselineNormal 기준)
    // approachUp    : 같은 normal 방향 속도 성분 (>0 = 상승)
    float aboveDistance = Vector2.Dot(slime.Position - topAtU, baselineNormal);
    float approachUp    = Vector2.Dot(slime.Velocity, baselineNormal);

    float belowGate = ribbonThickness + slime.SlimeRadius * 0.5f;
    bool risingThroughFromBelow = approachUp > 0f
        && (aboveDistance < -belowGate                        // (a) 줄 아래에 있고 상승 중, 또는
            || approachUp > 0.5f * slime.Velocity.magnitude); // (b) 가파르게 상승(줄 normal 60° 이내) — 한 FU 에 얇은 ribbon 을 뚫고 위로 지나간 fast 점프 케이스
    if (risingThroughFromBelow) {
        // 아래에서 위로 진입 → IgnoredColliderList.Passthrough 등록
        slime.RegisterIgnoredCollider(polygon, passthroughRefreshDuration);
        inPassthroughBelow = true;
    }
    else {
        // 위에서 떨어지거나 줄 높이로 수평 미끄러져 들어옴 → 아래의 anti-tunneling rescue 로 넘어가 착지
        ...
    }
}
```

수평 미끄러짐(slide-in)은 up-**비율**이 작아서 (b) 에 안 걸리고 줄 높이라 (a) 에도 안 걸림 → 정상 착지. 진짜 아래→위 점프는 up-비율이 크거나(b) 줄 아래에 있어서(a) 통과. (Springy Branch 의 `landingVelocityRetention` 미끄러짐 버그가 이 fast slide-in 을 만들어 본 버그를 드러냈지만, 둘은 독립 — slide-in 은 어느 플랫폼에서나 발생.)

`OnTriggerStay` 가 매 FU `RegisterIgnoredCollider` 로 timer 갱신. `OnTriggerExit` 에서 `UnregisterIgnoredCollider`. KnifeHazard 의 hazard-passthrough 패턴 동일.

`IgnoredColliderList.Passthrough` 는 trigger collider 에 대해선 `Physics2D.IgnoreCollision` 안 부르고 SurfaceProbe.Contains() 필터만 적용 — 우리 polygon 이 trigger 라서 여기에 정확히 맞음. SurfaceProbe 가 polygon 을 surface 후보에서 제외 → attach 안 일어남 → slime 은 자기 vy 그대로 위로 통과.

### Proactive from-below passthrough (1-FU 타이밍 레이스 수정)

**증상:** 아래에서 위로 (벽 점프 등) 올라올 때 줄에 걸려 못 올라가고 위쪽 속도 끊김. 간헐적.

**원인:** OnTrigger 통과 등록이 **1 FixedUpdate 늦음**. OnTriggerEnter/Stay 는 `Physics2D.Simulate` 중 = SlimeController.FixedUpdate(order 0)의 `CheckSurface`+`ProjectVelocityAgainstContacts` **이후** 발동. 슬라임 중심이 줄 아래 → 줄 contact normal 아래(천장) → 위쪽 속도가 클립됨, passthrough 가 켜지기 한 FU 전에. (broadphase 도달거리 > 트리거 겹침거리라 OnTriggerEnter 전에 이미 클립 contact 로 잡힘.)

**수정:** `UpdateFromBelowPassthrough()` 를 `FixedUpdate`(order 200)에서 매 FU 호출 — OnTriggerEnter 와 동일 술어(`IsRisingThroughFromBelow`, slide-in 가드 공유)를 **기하학적으로** 평가. 슬라임이 줄 한참 아래(상승 중)일 때 미리 passthrough 등록 → 다음 FU CheckSurface 에 이미 활성 → 클립 범위 도달 전에 통과. cheap gate(approachUp≤0 / broadphase bounds)로 idle 비용 ~0. OnTrigger 의 from-above anti-tunnel snap 은 유지.

## Anti-tunneling rescue

**문제 ([SingleContactSurfaceProbe.cs:30-31](../../../SlimeJumping_v2/Assets/Scripts/Physics/SingleContactSurfaceProbe.cs#L30-L31)):**

```csharp
if (toSlime.sqrMagnitude < 0.0001f) continue;  // Deep penetration: skip
```

`PolygonCollider2D.ClosestPoint(p)` 는 `p` 가 polygon 내부면 `p` 자기 자신 반환 (Unity 동작). 슬라임이 빠르게 떨어져서 한 FU 안에 polygon 두께보다 더 깊이 박히면 ClosestPoint 가 query point 반환 → SurfaceProbe 가 deep-penetration 으로 판정해서 contact skip → attach 안 됨 → 슬라임이 polygon 통과해서 계속 떨어짐.

(슬라임 `rb.collisionDetectionMode = Continuous` 라 OnTriggerEnter 자체는 fast 한 슬라임도 잡음. 단지 SurfaceProbe 의 ClosestPoint 검사가 deep penetration 에서 무력화.)

**해결 (TautLine OnTriggerEnter2D, vy<0 분기):**

```csharp
float u = ProjectToBaselineParam(slime.Position);
Vector2 topAtU = SampleCurveAt(u);
Vector2 toSlime = (Vector2)slime.Position - topAtU;
float aboveDistance = Vector2.Dot(toSlime, baselineNormal);
if (aboveDistance < slime.SlimeRadius) {
    // 슬라임이 polygon 안쪽으로 박혔거나 너무 가까이 있음 → 곡선 top + slimeRadius 로 snap
    slimeRb.position = topAtU + baselineNormal * slime.SlimeRadius;
}
```

다음 FU 에 SurfaceProbe 가 정상 거리에서 contact 잡아서 attach. OnTriggerEnter 는 event-driven 이라 평소 cost 0.

## Slime tracking (carry 채널 폐기)

RopeV3 의 `ConsumeRiderCarryDelta` 는 매 FU contact-point 변위를 누적해서 SlimeController 가 `rb.position += carry` 적용. 빠른 spring 진동에서 delta 가 `maxCarryPerStep` clamp 에 걸리면 슬라임이 못 따라잡고 detach 발생 → attach 진동 버그.

**TautLine 은 carry 채널 사용 안 함.** `ConsumeRiderCarryDelta` 는 `Vector2.zero` 반환 (no-op). 대신:

```csharp
[DefaultExecutionOrder(200)]   // SlimeController 후 실행
public class TautLine : ... {
    void FixedUpdate() {
        // ... dipDepth 적분, polygon 모양 갱신 ...

        // 슬라임이 이 줄에 attach 돼있으면 절대 위치로 직접 set
        if (riderActive && slimeRb != null) {
            float u = ProjectToBaselineParam(slime.Position);
            Vector2 desired = SampleCurveAt(u) + baselineNormal * slime.SlimeRadius;
            slimeRb.position = desired;     // delta 누적/clamp 없음
        }
    }
}
```

매 FU 새로 desired 계산 → 누적 오차 없음. clamp 없음. 슬라임은 항상 정확히 곡선 위.

## Launch 후 처리

`SlimeController.Launch.cs:90` 가 발사 시점에 `ignoredColliderList.RegisterOverlapping(...)` 으로 주변 collider (TautLine polygon 포함) 를 **ReattachGuard** 모드로 등록. 슬라임이 polygon 영역 (`slimeRadius + launchClearanceThreshold`) 벗어날 때까지 OR `postLaunchTimer` 만료까지 SurfaceProbe 가 polygon 을 surface 로 안 봄.

이게 launch 직후 즉시 re-attach 막아주는 기존 메커니즘. **TautLine 측에 추가 launch-settle 코루틴 같은 거 안 둠** — 그러면 다시 떨어져 와서 정상 attach 해야 할 때도 polygon 이 가려져서 통과 버그 재발 (이전 attempt 에서 발생).

ReattachGuard release 후 슬라임 다시 떨어져 오면, `postDetach=true` 로 spring 이 overdamped 상태 → 줄이 baseline 으로 부드럽게 복귀 중 → 슬라임 잡으려고 위로 튀지 않음 → 정상 착지.

## IRopePlatform 구현

| 메서드 | TautLine 동작 |
|---|---|
| `Edge` | `null` 반환. SlimeController 의 Attempt 4/5 hack (RopeV3 용) 의 `currentRope.Edge` 검사가 자연스럽게 false 로 평가되어 무발동. |
| `MaxReboundSpeed` | `maxCap` 직렬화 필드. |
| `ReportRiderContact(...)` | No-op. `riderActive` 는 `slime.SurfaceCollider == polygon` 검사로 매 FU 도출. |
| `NotifyFirstAttach(p)` | `postDetach = false`. `OnRopeEvent(FirstAttach)`. |
| `NotifyLaunchRebound(p)` | `OnRopeEvent(LaunchRebound)`. detach 자체는 FixedUpdate 의 state edge 검출에서 처리 — 이 hook 은 외부 listener 용. |
| `ApplyRiderImpact(p, vel)` | `vDown = max(0, dot(vel, worldDown))`. ① `dipVelocity += vDown × impactCompressionFromVelocity` (punch). ② trampoline 상태 set (`extraSag = max(prev, sagCurve(vDown))`, `holdTimer = max(prev, holdCurve(vDown))`) — 깊은 bowl. |
| `SampleVelocityAt(p)` | `worldDown × dipVelocity × Bell(uAtP, riderT)`. SlimeController 의 ropeVel inheritance 에 사용. |
| `GetLaunchBonus(dir, p, ratio)` | side-effect: `dipVelocity += launchImpactKickCurve.Evaluate(ratio)` (downward punch). 반환: `clamp(jumpBoost × ratio × upwardComponent, 0, maxCap) × speedFalloffMul(slime.Velocity.magnitude)`. EnergyConversion 으로 pre-bonus 속도가 이미 클 땐 falloff 가 boost 를 깎아 `높이 제한` 역할. |
| `PostprocessLaunchVelocity(vel)` | `dot(vel, worldUp) < minLaunchVerticalSpeed` 이면 worldUp 성분만 끌어올림. 수평 성분 보존. (이전 `maxLaunchSpeed` 매그니튜드 캡은 폐기 — EnergyConversion 까지 같이 깎이는 부작용 때문, `boostFalloff*Speed` 로 이관.) |
| `ConsumeRiderCarryDelta(p)` | `Vector2.zero` (no-op). TautLine 이 절대 좌표로 슬라임 직접 위치시킴. |

## 인스펙터 직렬화 필드

```
[Anchors]
  anchorA, anchorB          : Transform

[Targets]
  polygon                   : PolygonCollider2D (trigger)
  line                      : LineRenderer

[Slime ref]
  slime                     : SlimeController (auto-find if null)

[Shape]
  sampleCount               : int   (default 32)
  baselineSag               : m     (default 0.15)  — 라이더 없을 때 정적 처짐
  ribbonThickness           : m     (default 0.1)   — polygon 두께

[Spring (rider sag)]
  baseEquilibrium           : m     (default 0.3)   — 정지 슬라임 sag 평형
  springStiffness           : 1/s²  (default 60)    — k
  attachedDampingRatio      : ζ     (default 0.5)   — 부착 중 underdamped
  detachedDampingRatio      : ζ     (default 1.2)   — 탈출 후 overdamped
  impactCompressionFromVelocity : (default 0.05)   — 착지 임팩트 → dipVelocity kick (작은 punch)

[Trampoline (impulse-driven bounce)]
  trampolineImpactKickCurve : curve(impactSpeed→m/s, default Linear(0,0)→(30,15))  — 충돌 시 dipVelocity 다운 임펄스 (첫 dip 깊이 결정)
  trampolineHoldCurve       : curve(impactSpeed→s,   default Linear(0,0)→(30,1.5)) — 진동(underdamped) 윈도우 길이

[Launch boost (effect-mode)]
  jumpBoost                 : m/s   (default 3)     — full charge 시 launch boost (launchDir 방향)
  maxCap                    : m/s   (default 6)     — jumpBoost (rope bonus) 의 하드 캡 (boost 항만)
  launchImpactKickCurve     : curve  (default Linear(0,0)→(1,4))  — chargeRatio → 발사 순간 dipVelocity 다운펄스
  minLaunchVerticalSpeed    : m/s   (default 3)     — launch 후 최소 worldUp 속도 (일정, charge 무관). 0 = disable
  boostFalloffStartSpeed    : m/s   (default 12)    — pre-bonus 슬라임 launch 속도 (= EnergyConversion + 관성 포함, rope bonus 미포함) 이 이하 → full jumpBoost
  boostFalloffEndSpeed      : m/s   (default 18)    — pre-bonus 슬라임 launch 속도가 이상 → jumpBoost = 0. start↔end 사이 선형 감쇠. end ≤ start 면 hard cutoff. **이전 `maxLaunchSpeed` (최종 magnitude 캡) 대체** — magnitude 캡은 EnergyConversion 까지 같이 깎아 "높은 곳서 떨어져 줄에 착지 → EnergyConversion 보존" 시나리오를 망가뜨림. boost 만 깎으면 슬라임 자체 power 는 그대로 통과
  jumpTimingCurve           : curve(ropeUpSpeed→mul, default Linear(0,0)→(5,1))  — 줄 상승 속도별 boost multiplier (timing window)

[Pass-through]
  passthroughRefreshDuration : s   (default 0.2)    — IgnoredCollider timer 갱신 주기
  settleVelocityThreshold   : m/s  (default 0.05)   — postDetach 해제 임계

[Surface Override]
  ropeSurfaceMaterial       : SurfaceMaterial (null = no override) — attach 중 슬라임 surfaceMod 덮어쓰기
```

## Surface override

WaterCurrentZone / SpringyBranchPlatform 동일 패턴. `ropeSurfaceMaterial` 가 설정되어 있으면:

- attach edge (FixedUpdate 에서 `riderActive` false→true) → `slime.PushSurfaceMaterialOverride(ropeSurfaceMaterial)`.
- detach edge (true→false) → `slime.PopSurfaceMaterialOverride(ropeSurfaceMaterial)`.
- `OnDisable` 안전망 — push 된 상태로 컴포넌트 비활성화되면 pop.

`overridePushed` 플래그가 push 상태를 추적해서 중복 push 막음. 비워두면 (`null`) 슬라임은 host SurfaceTag 그대로 사용.

## 사용

```
Tools / SlimeJumping/Mechanism/Setup TautLine Test
```

`TautLineSetup.cs` 가 Floor + SpawnPad_Soft + TautLine_Default 생성. `RopeTestSetup.cs` 무수정.

## 검증 시나리오

| # | 시나리오 | 기대 동작 |
|---|---|---|
| 1 | TautLine 위에 떨어져서 착지 | 진동 없이 한 번에 attach |
| 2 | 빠른 속도 (vy ≥ 25 m/s) 로 떨어짐 | 통과 안 하고 정상 attach (anti-tunneling rescue) |
| 3 | 줄 위에서 charge → launch | charge 중 줄 깊이 일정 (baseline), 발사 순간 줄이 펀치되듯 한 번 깊게 가라앉음 → 부드럽게 baseline 복귀 |
| 3b | 줄 위에서 거의 수평으로 launch | 슬라임이 `minLaunchVerticalSpeed` 이상 위로 떠서 줄 rebound 에 안 잡힘 |
| 4 | Launch → 정점 → 다시 줄로 떨어짐 | 진동 없이 정상 attach (postDetach overdamped) |
| 5 | 아래에서 위로 점프 (vy>0) → 줄 위치로 | 통과, attach 안 됨, 슬라임 vy 유지 |
| 6 | SUSPECT repro: launch → SpawnPad 튕김 → 줄 재착지 | 깔끔히 attach (RopeV3 진동 버그 케이스) |
| 7 | 슬라임이 vy>0 로 줄 안 들어왔다가 못 넘어서 다시 떨어짐 | 통과 모드 유지, 줄 바닥으로 빠져나가 그냥 떨어짐 |
| 8 | 옆 플랫폼에서 빠르게 수평으로 미끄러져 줄에 도달 (vy 살짝 +) | **통과 안 하고 정상 attach** (position-based from-below 판정 — up-비율 작아서 passthrough 안 함) |

## 한계 / out-of-scope

- 단일 라이더만. 다중 슬라임 시 첫 번째 만 추적.
- 줄 자체가 흔들리는 swing/whip 감각 없음. 정적 baseline + local spring 만.
- TautLine 이 `slimeRb.position` 직접 덮어쓰기 → SlimeController 의 tangential 입력 처리와 1-FU lag (실용상 invisible).
- `RopeAttachStateProbe` / `RopeV3Debug` 는 RopeV3 전용. TautLine 진동/통과 버그는 시각적으로 명백하므로 별도 디버그 component 만들지 않음.

## 결정 트리 (이 디자인이 어떻게 나왔나)

설계 단계의 큰 결정들 — 나중에 비슷한 결정 재방문할 때 참고:

1. **줄 모양**: V (sharp kink) vs smooth → smooth (quadratic Bezier-like).
2. **Bezier 의 deepest point**: 근사 vs 정확히 슬라임 아래 → 정확히 (piecewise smoothstep).
3. **빈 줄 형태**: 완벽 직선 vs 정적 sag → 정적 sag (`baselineSag` 직렬화).
4. **Sag depth가 라이더 위치에 의존?**: 위치 비례 vs 일정 → 위치 비례 (`shapeFactor = 4u(1-u)`).
5. **One-way platform 시멘틱**: from-above only → 슬라임이 아래에서 위로 올라가면 통과.
6. **Spring 동역학**: 즉시 / spring-damper / 다른 → spring-damper (no clamp), state-dependent damping.
7. **Collider 종류**: EdgeCollider2D vs PolygonCollider2D → PolygonCollider2D (EdgeCollider 가 항상 통과 버그).
8. **From-below 통과 구현**: 매 FU 체크 vs event-driven → event-driven (KnifeHazard 패턴, OnTriggerEnter/Stay/Exit + IgnoredColliderList).
9. **Trampoline 메커니즘**: 슬라임 jump 시 줄이 먼저 위로 vs 더 깊어졌다가 → 더 깊어졌다가 (`chargeExtraDepth × ChargingProgress` 로 charge 시 깊이 증가).
10. **Launch boost 튜닝 모드**: physics params vs effect mode → effect mode (`jumpBoost` + `maxCap` 두 개만).
11. **Charge → boost 관계**: 일정 vs 비례 → charge 비례 (`bonus = jumpBoost × chargeRatio`).
12. **Slime tracking**: ConsumeRiderCarryDelta 재사용 vs 새 메커니즘 → 새 메커니즘 (절대 좌표 직접 set, executionOrder=+200).
13. **Launch 후 settle 보호**: 추가 Passthrough 코루틴 vs 기존 ReattachGuard 만 → 기존 ReattachGuard 만 (코루틴 추가 시 다시 떨어져서 통과 버그 재발).
14. **Anti-tunneling**: SurfaceProbe 수정 vs TautLine 자체 처리 → TautLine 자체 처리 (OnTriggerEnter 에서 deep-penetration 검출 + snap).

## 파일

- [Scripts/Platforms/TautLine.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/TautLine.cs)
- [Scripts/Editor/TautLineSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/TautLineSetup.cs)
- [Scripts/Platforms/IRopePlatform.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/IRopePlatform.cs) (공유 인터페이스, 무수정)
