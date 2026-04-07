# SlimeJumping — 핵심 메커닉 설계 v6

## 컨셉

**점성 슬라임** — 모든 표면에 달라붙는 것이 자연 상태. 이동은 크롤링과 차징 발사로 수행.  
WASD + Space 키로 조작. 모든 시간 단위는 **초(seconds)** 기준.

**장르 지향**: 점프킹, Egging On, 셀레스트, 구구피자 계열의 정밀 플랫포머.  
조작 반응성과 예측 가능성이 최우선. 소프트바디 비주얼은 게임플레이에 영향을 주지 않는다.

---

## 1. 구현 아키텍처 — 2단계 분리

### Phase 1: 단일 원형 콜라이더 플랫포머 (핵심, 현재 구현 완료)

게임플레이 전체가 **원 하나**로 동작한다.

- 충돌 판정, 이동, 차징, 발사, 에어 컨트롤, 벽 부착, 드리프트 — 전부 단일 원형 콜라이더 기반.
- 소프트바디 없이도 모든 메커닉이 완전히 동작함.

### Phase 2: 소프트바디 비주얼 오버레이 (후순위, 미구현)

Phase 1이 완성된 후 비주얼로 얹는 레이어.

- 중심 1개 + 주변 6개 노드가 스프링으로 연결. 중심 → 주변 단방향.
- 게임플레이 수치에는 일절 관여하지 않음. Phase 1 코드 수정 없이 추가 가능.
- 차징 중 슬라임 변형으로 판정원 UI를 대체할 수 있음 (조준점 방향 = 압축 방향).

---

## 2. 상태 머신

3개 상태: **Attached**, **Charging**, **Airborne**.

```
Attached ──Space──▶ Charging
    ▲                    │
    │                 릴리즈/풀차지/자동발사
    │                    ▼
    └──표면접촉── Airborne ──Space홀드+표면접촉──▶ Charging (에너지 전환)
```

---

## 3. 표면 부착 & 이동 (Attached 상태)

### 부착 판정

- 원형 콜라이더가 표면에 접촉하면 Attached 진입.
- **천장 불가 (히스테리시스)**: 탈출과 진입에 다른 임계값 사용.
  - **탈출** (Attached/Charging → Airborne): `surfaceUp < ceilingThreshold`(-0.3)이면 떨어짐.
  - **진입** (Airborne → Attached/Charging): `surfaceUp >= ceilingReattachThreshold`(-0.2)이어야 부착.
  - 사이 구간(-0.3 ~ -0.2)에서는 현재 상태 유지 → 구체 표면 크롤링 시 상태 떨림 방지.
- 착지 시 `velocity = 0` (점성 슬라임 — 모든 운동량 흡수).

### 표면 감지

8방향 CircleCast로 가장 가까운 표면 검출. 복수 표면 감지 시 거리 + 입력 방향 기반 우선순위. 현재 표면을 기억하고 고수 — 접촉 끊길 때만 새 표면 탐색 (코너 떨림 방지).

### 크롤링 (WASD 이동)

입력을 표면 접선 방향으로 투영. 가속/감속 모델로 관성 표현.

```
targetSpeed = input.x * crawlSpeed
if (가속 또는 역방향)
    crawlVelocity = MoveTowards(crawlVelocity, targetSpeed, crawlAcceleration * dt)
else
    crawlVelocity = MoveTowards(crawlVelocity, targetSpeed, crawlDeceleration * dt)
```

- 바닥: A/D 좌우. 벽: W/S 위아래.
- 이동 속도는 느림 — 위치 미세 조정용. 주 이동은 차징 발사.
- 벽에서 위로 기면 중력에 의해 느려지고, 아래로 기면 빨라짐. 중력 > crawlSpeed이면 올라갈 수 없음.

### 표면 미끄러짐 (중력 분해)

중력을 접선/법선으로 분해. 접선 성분이 미끄러짐, 법선 성분은 표면이 상쇄.

```
gravAlongTangent = dot(gravity, surfaceTangent)

if (abs(gravAlongTangent) < slideThreshold)
    gravAlongTangent = 0                              // 완만한 경사 보호

slideSpeed = gravAlongTangent * slideGravityScale
```

- **속도 누적 없음** — 매 프레임 각도에서 직접 계산. A/D 놓으면 즉시 일정 속도 미끄러짐.
- 크롤링은 중력 접선 전체와 싸움 (slideThreshold 적용 전 값).
- 최종 속도: `finalTangent = slideSpeed + crawlVelocity`.

### 그라운드 대시 (S 순간 부스트)

Attached 상태에서 A/D 크롤 중 S를 누르면 `crawlVelocity`에 순간 impulse 추가. 표면 부착 유지.

- S pressed(rising edge)에만 발동, 홀드 시 반복 안 함.
- **입력 버퍼**: S를 먼저 누르고 `groundDashInputBuffer`(0.1초) 안에 A/D 입력 시에도 발동.
- **코요테 버퍼**: Airborne 상태에서 S 누르면 버퍼에 저장, 착지 시 A/D 홀드 중이면 발동.
- **쿨타임**: `groundDashCooldown`(0.3초) 동안 재발동 불가. 타이머는 wall-clock 기반 (Airborne/Charging 중에도 경과).
- **기울기 제한**: `groundDashAngleMin`(30°)~`groundDashAngleMax`(60°) 구간에서 1→0 보간. 평지=풀 부스트, 급경사=불가.
- 표면별 `groundDashScale` 배율로 추가 조절 가능 (기본 1.0).
- **입력 우선순위**: Charging > W(hop) > S(dash). Space 홀드 착지 시 둘 다 소비, hop 버퍼 활성 시 dash 버퍼 소비.

```
if (S pressed && cooldown <= 0 && |inputX| > 0)
    angleScale = 1 - InverseLerp(groundDashAngleMin, groundDashAngleMax, surfaceAngle)
    crawlVelocity += sign(inputDir) * groundDashImpulse * angleScale * surfaceMod.groundDashScale
    cooldown = groundDashCooldown
```

### 홉 (W 표면 점프)

Attached 상태에서 W를 누르면 표면 법선 방향으로 작은 점프. 기존 접선 운동량을 보존하면서 살짝 띄우는 표현용 액션.

- `velocity += surfaceNormal * hopPower`. 스틱 중에는 `velocity = surfaceNormal * hopPower` (충돌 잔여 속도 무시).
- `crawlVelocity = 0` (접선 운동량은 velocity 필드로 이관).
- **입력 지연 처리 (Deferred)**: 입력 콜백은 `hopRequested` 플래그만 set. 실제 `Hop()`은 `FixedUpdateAttached` 끝에서 velocity가 신선한 상태에서 호출.
- **착지 속도 보존**: `preserveLandingVelocity` 플래그로 hop 착지 시 retention을 `hopLandingRetention`(기본 1.0)으로 강제. 경사 가속 제어용 knob.
- **쿨타임**: `hopCooldown`(0.1초).

**홉 입력 버퍼 (코요테)**:
- Airborne 상태에서 W 누르면 `hopBufferTimer`에 저장.
- 착지 시 (Airborne → Attached) 버퍼가 활성이면:
  1. `preserveLandingVelocity = true` → retention 100%로 crawlVelocity 보존.
  2. `velocity = surfaceTangent * crawlVelocity`로 재구성 (착지 코드가 velocity를 0으로 만들었으므로).
  3. `stickTimer = 0` (stick 분기 우회 — 각도 무관 일관 동작).
  4. `Hop()` 즉시 호출.
- 버퍼 hop은 `hopCooldown`을 우회 (의도적 — 벽 등반 사용처).

### 하이퍼 대시 (Dash → Charge → Launch 콤보)

그라운드 대시 후 즉시 차징하면, 대시 운동량이 발사에 합산되는 콤보 기법.

- **캡처 시점**: 차징 진입 시 `chargeEntryCrawl = crawlVelocity` 저장. 차징 중 `crawlDeceleration`이 시각적 감속을 처리하지만, 캡처 값은 보존.
- **발사 시**: `velocity += lastSurfaceTangent * chargeEntryCrawl * tangentInheritanceFactor`.
- `tangentInheritanceFactor`(기본 0.7)로 boost 비율 조절.
- **에너지 전환 경로**: Airborne → Charging 직행 시 `chargeEntryCrawl = 0` (에너지 전환과 하이퍼 대시 스택 방지).
- **코요테 탄젠트**: `lastSurfaceTangent`를 캐싱하여 코요테 중(표면 이탈) Launch에서도 올바른 접선 방향 사용.

### 스틱 타이머 (착지 후 표면 고정)

벽/급경사에 **공중에서 착지**할 때 충돌 속도에 비례한 시간 동안 슬라이드 없이 고정. 점성 슬라임이 철퍼덕 붙는 느낌.

**적용 시점**: Airborne → Attached 착지, Airborne → Charging 에너지 전환 착지. 두 경로만 해당.
**미적용**: 이미 부착된 상태에서 Space 눌러 Charging 진입 시 — 충돌이 아니므로 스틱 불필요. 기존 stickTimer 값이 그대로 이어짐.

```
if (surfaceUp >= slopeThreshold)
    stickTimer = 0                                           // 바닥: 고정 없음
else
    t = 1 - 1 / (1 + impactSpeed * stickSpeedScale)         // 수렴 커브 (0~1)
    stickTimer = Lerp(minStickDuration, maxStickDuration, t) // 충돌 속도 비례
```

- 수렴 커브: `impactSpeed`가 아무리 커져도 `t`는 1.0에 수렴 → `maxStickDuration`을 절대 초과하지 않음.
- `stickSpeedScale`: 커브 기울기 조절. 값이 크면 적은 속도에서도 빨리 max에 도달.
- Attached: 고정 중 입력 차단.
- Charging: 고정 중에도 판정원 조준은 동작 — 슬라이드만 멈춤.

### 표면 스냅

`ClosestPoint + normal * radius`로 표면에 밀착. `transform.position` 직접 설정 (velocity 제어와의 충돌 방지).

---

## 4. 차징 & 발사 — 판정원 모델 (Charging 상태)

### 원리

스페이스를 누르면 슬라임 중심에 **판정원**이 생성. 그 안에 **조준점**이 나타남.
조준점은 자동으로 바깥으로 밀려나며, WASD로 2D 조작.
발사 시 **슬라임 중심 → 조준점 벡터의 반대 방향**으로, **거리에 비례한 파워**로 날아감.

### 판정원 구조

```
         판정원 (activeChargeRadius)
        ╱                            ╲
      ╱    조준점 ●→ 자동 드리프트      ╲
    ╱       ↑WASD로 조작                  ╲
   │      슬라임 중심 ○                     │
    ╲                                      ╱
      ╲                                  ╱
        ╲                              ╱
```

### 진입 경로 2가지

**1. 일반 차징**: Attached에서 Space → `activeChargeRadius = chargeRadius`. 조준점 중심에서 시작.

**2. 에너지 전환**: Airborne에서 Space 홀드 + 표면 충돌 → 충돌 속력에 따라 판정원 확장.

### 조준점 이동

- **자동 드리프트**: 매 프레임 현재 방향으로 바깥 이동. 초기 방향은 표면 법선 반대쪽 (표면을 향해 눌림 → 반대로 발사).
- **WASD**: 조준점에 2D 가속도. 바깥쪽 = 가속, 안쪽 = 감속(완전 상쇄 불가 — inputAcceleration < autoDriftSpeed), 좌우 = 각도.

```
// 자동 드리프트
radiusRatio = activeChargeRadius / chargeRadius
activeDriftSpeed = autoDriftSpeed * lerp(1, radiusRatio, driftCompensation)
aimOffset += aimOffset.normalized * activeDriftSpeed * dt

// WASD
aimOffset += wasdInput * inputAcceleration * dt
```

### 발사 트리거

1. **스페이스 릴리즈** — 수동 발사. 현재 조준점 위치 기준.
2. **가장자리 도달** — 자동 발사. 풀파워 (또는 확장 시 초과 파워).

### 발사력 공식

```
power = (aimOffset.magnitude / chargeRadius) * baseLaunchPower
direction = -aimOffset.normalized    // 압축 반대 방향
```

- **chargeRadius 기준**으로 ratio 계산. activeChargeRadius가 크면 ratio > 1.0 → 초과 파워.
- 안정도, 노드 수, 표면 종류 등 **어떤 외부 계수도 없음**.

### 조준 관성

별도 파라미터 없이 기하학적으로 발생. 조준점이 가장자리 근처일 때 같은 WASD 입력으로 각도를 바꾸려면 원호를 크게 돌아야 함. 중심 근처에서는 자유롭게 방향 전환.

### 차징 중 슬라이드

벽/급경사에서 차징 중에도 슬라이드가 동작. 슬라임이 흘러내리면서 차징 = 시간 압박. 판정원은 슬라임 기준 상대 좌표라 슬라임이 미끄러져도 같이 따라감.

### 차징 규칙

- **차징 캔슬 불가.** 스페이스 누르면 무조건 발사까지 진행. 커밋.
- **발사 후 입력 무시 윈도우: 0.05초.**
- **최소 발사력**: aimOffset이 minLaunchRatio 미만이면 최소 파워로 클램프 후 발사. Attached로 돌아가지 않음 (캔슬 불가 원칙).

### 발사 후 표면 무시

발사한 표면 콜라이더를 임시 무시. 충분히 벗어나거나(clearanceThreshold) 시간 초과(maxIgnoreTime) 시 해제. 약한 발사에서 바닥에 즉시 재부착되는 문제 방지.

---

## 5. 에너지 전환 — 확장 판정원 모델

### 핵심

공중에서 Space 홀드 + 표면 충돌 시, 운동량이 판정원 크기로 전환.

### 충돌 처리

```
// 에너지 보존 공식으로 기본 중력 기준 속도 계산 (W/S 중력 조절 영향 제거)
heightDelta = launchHeight - currentHeight              // 떨어졌으면 양수
shadowSpeedSq = launchSpeed² + 2 * |gravity| * heightDelta
impactSpeed = shadowSpeedSq > 0 ? sqrt(shadowSpeedSq) : 0

effectiveRate = sqrt(heightRestorationRatio) * (chargeRadius / baseLaunchPower)
spawnDistance = impactSpeed * effectiveRate
activeChargeRadius = max(spawnDistance, chargeRadius)    // 확장 또는 기본
aimOffset = Vector2.zero                                // 항상 중심에서 시작
velocity = Vector2.zero                                 // 운동량 전량 전환 (포고스틱 모델)
crawlVelocity = 0
```

- Space 안 누르고 부딪히면: `velocity = 0`, Attached. 에너지 소멸.
- Space 누르고 부딪히면: 위 로직 실행, Charging 직행.
- 약한 충돌 (spawnDistance ≤ chargeRadius): 일반 차징과 동일.
- 강한 충돌 (spawnDistance > chargeRadius): 판정원 확장, 초과 파워 가능.

### 드리프트 보상 (driftCompensation)

확장 판정원에서 드리프트 속도를 얼마나 보상할지.

```
activeDriftSpeed = autoDriftSpeed * lerp(1, radiusRatio, driftCompensation)
```

| driftCompensation | 반지름 2배일 때 | 차징 시간 |
|-------------------|----------------|-----------|
| 1.0 | 드리프트 2배 | 동일 |
| 0.7 | 드리프트 1.7배 | 약 18% 증가 |
| 0.0 | 드리프트 불변 | 2배 |

### 트레이드오프

- **안전 루트**: Space 안 누르고 착지 → Attached → 일반 차징. 파워/방향 완전 제어.
- **스피드 루트**: Space 홀드 + 빠르게 충돌 → 확장 판정원 → 초과 파워 가능하지만 차징 시간 약간 증가.
- 접근 각도는 에너지 전환량에만 영향 (속력). 조준점 초기 방향은 항상 표면 법선 기준.

### 사전 커밋 원칙

부딪히기 전에 Space를 누를지 말지 미리 결정해야 함. 예측하고 커밋한 플레이어가 보상받는 구조. 차징 캔슬 불가 원칙과 일관.

---

## 6. 공중 제어 (Airborne 상태)

### 중력

키네마틱 방식. 매 프레임 `velocity.y += gravity * dt`.

상승/하강 비대칭 중력: 상승 시 `risingGravityScale`, 하강 시 `fallingGravityScale` 곱셈 적용. 기본값 둘 다 1.0 (대칭).

### 에어 컨트롤 활성 조건

**`velocity.y <= apexThreshold`**(기본 0)일 때만 에어 컨트롤 활성. 상승 중에는 W/S 중력 조절과 A/D 수평 조작이 모두 비활성.

### 에어 컨트롤 — A/D 수평 가감속

A/D로 수평 속도를 가감속. 발사 속도가 상한보다 빠르면 감속(반대 방향)만 허용.

```
targetVx = inputX * airMaxHorizontalSpeed
if (이미 상한 초과) → 반대 방향 입력만 적용
else → MoveTowards(velocity.x, targetVx, accel/decel * dt)
```

### 에어 컨트롤 — W/S 중력 조절

W/S로 비행 중 체감 중력을 조절하여 궤적 형태를 변경.

```
W 홀드: gravityScale = floatGravityScale (기본 0.5)  → 넓고 평평한 궤적
S 홀드: gravityScale = diveGravityScale (기본 2.0)  → 빠르게 꽂히는 궤적
미입력:  gravityScale = 1.0                          → 기본 포물선

실제 적용 중력: gravity * gravityScale
```

- 플레이어 본인이 조작하므로 예측 가능성 유지.
- S로 빠르게 꽂으면 에어 컨트롤 시간 감소, W로 길게 뜨면 착지점 세밀 조절 가능. 속도 vs 정밀도 트레이드오프.

### 에너지 전환 보정 (에너지 보존 공식)

W/S 중력 조절이 에너지 전환을 왜곡하지 않도록, 착지 시 에너지 보존 공식으로 "기본 중력이었으면 이 속도" 계산:

```
heightDelta = launchHeight - currentHeight        // 떨어졌으면 양수
shadowSpeedSq = launchSpeed² + 2 * |gravity| * heightDelta
impactSpeed = shadowSpeedSq > 0 ? sqrt(shadowSpeedSq) : 0
```

- 에어 컨트롤은 속력을 보존하므로 에너지에 영향 없음. 유일한 변수는 높이 차이.
- W로 정상 중력에서 도달 불가능한 높이에 도착 → shadowSpeedSq < 0 → 에너지 전환 0.

### 착지 분기

| 조건 | 결과 |
|------|------|
| 천장 (surfaceUp < ceilingReattachThreshold) | 부착 안 함. 법선 속도 상쇄, Airborne 유지 |
| Space 홀드 + 실제 비행 (체공 > 0.1초) | 에너지 전환 → Charging 직행 |
| Space 미홀드 + 실제 비행 | velocity = 0, Attached 전환, 스틱 타이머 적용 |
| 코너 튕김 (체공 < 0.1초) | 법선 속도만 상쇄, Attached 전환 |

---

## 7. 표면별 상호작용

### 바닥 (surfaceUp > slopeThreshold)

- 슬라이드 없음. 안정적. 여유롭게 차징 가능.
- 스틱 타이머 없음 (surfaceUp >= slopeThreshold).

### 벽 (surfaceUp ≈ 0)

- 중력으로 아래로 슬라이드. 빨리 쏘면 높은 위치, 늦으면 낮아짐.
- 크롤링으로 위아래 이동 가능 (중력보다 느린 속도).
- 스틱 타이머: 충돌 속도 비례 (minStickDuration ~ maxStickDuration).

### 급경사 (slopeThreshold 이하, ceilingThreshold 이상)

- 기울기에 비례한 슬라이드.
- 차징 중에도 미끄러짐.
- 스틱 타이머: 충돌 속도 비례 (수렴 커브).

### 천장 (surfaceUp < ceilingThreshold)

- 부착 불가. 닿으면 법선 속도만 상쇄, Airborne 유지.

### 좁은 플랫폼

- 크롤링으로 위치 미세 조정이 중요.
- 가장자리에서 떨어지기 쉬움.

---

## 8. 튜닝 파라미터

### Surface Movement

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| crawlSpeed | 5 | A/D 최대 이동 속도 |
| crawlAcceleration | 50 | 입력 시 가속 |
| crawlDeceleration | 30 | 입력 해제 시 감속 (관성) |
| slideGravityScale | 1 | 중력 접선 → 미끄러짐 속도 배율 |
| slideThreshold | 1 | 이 이하 접선 중력은 무시 (완만한 경사 보호) |

### Ground Dash

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| groundDashImpulse | 8 | S 누를 때 crawlVelocity에 더하는 순간 속도 |
| groundDashCooldown | 0.3 | 대시 간 쿨타임 (초). Wall-clock 기반 |
| groundDashInputBuffer | 0.1 | S 입력 버퍼 시간 — Attached/Airborne에서 유효 |
| groundDashAngleMin | 30 | 이 각도 이하에서 풀 대시 |
| groundDashAngleMax | 60 | 이 각도 이상에서 대시 불가, 사이 보간 |

### Hop

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| hopPower | 2.25 | W 누를 때 surfaceNormal 방향 impulse |
| hopCooldown | 0.1 | 홉 간 쿨타임 (초). 버퍼 홉은 우회 |
| hopBufferDuration | 0.1 | Airborne에서 W 입력 버퍼 시간 |
| hopLandingRetention | 1 | 홉 착지 시 접선 속도 보존 비율. 경사 가속 제어용 (0~1) |

### Hyper Dash

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| tangentInheritanceFactor | 0.7 | 차징 진입 시 캡처한 crawl의 발사 합산 비율 (0~1) |

### Charge Circle

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| chargeRadius | 1.5 | 기본 판정원 반지름 |
| autoDriftSpeed | 1.5 | 조준점 자동 바깥 이동 속도 |
| inputAcceleration | 1.0 | WASD 조준점 가속도 (autoDriftSpeed보다 낮아야 무한 차징 방지) |
| baseLaunchPower | 15 | 풀파워 발사 속도 |
| minLaunchRatio | 0.05 | 최소 발사 비율 (제로파워 방지) |

### Energy Conversion

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| heightRestorationRatio | 2.25 | 에너지 전환 복원 비율 (effectiveRate = sqrt(ratio) * chargeRadius / baseLaunchPower) |
| driftCompensation | 0.7 | 확장 시 드리프트 보상 (1.0=시간 동일, 0.0=시간 비례 증가) |

### Stick

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| minStickDuration | 0.3 | 최소 스틱 시간 — 낮은 충돌 속도 (초) |
| maxStickDuration | 0.6 | 최대 스틱 시간 — 높은 충돌 속도 (초) |
| stickSpeedScale | 0.1 | 수렴 커브 기울기. 클수록 적은 속도에서 max 도달 |
| stickDecelerationTime | 0.05 | 스틱 시작 시 착지 관성 감속 시간 (초) |
| slopeThreshold | 0.7 | 이 이상 surfaceUp = 바닥 (스틱/슬라이드 없음). 약 45° |
| ceilingThreshold | -0.3 | 이 미만 surfaceUp = 천장 (부착 불가). 약 107° |
| ceilingReattachThreshold | -0.2 | Airborne에서 이 이상이어야 재부착. 히스테리시스 |

### Air Control

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| airMaxHorizontalSpeed | 5 | A/D 수평 최대 속도 |
| airAcceleration | 20 | 수평 가속 |
| airDeceleration | 15 | 수평 감속 |
| floatGravityScale | 0.5 | W 홀드 시 중력 배율 |
| diveGravityScale | 2.0 | S 홀드 시 중력 배율 |
| risingGravityScale | 1 | 상승 중 중력 배율 |
| fallingGravityScale | 1 | 하강 중 중력 배율 |
| apexThreshold | 0 | 이 이하 velocity.y에서만 에어 컨트롤 활성 |

### Physics

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| gravity | -20 | 키네마틱 중력 |
| slimeRadius | 0.4 | CircleCollider2D 반지름 |
| surfaceCheckDistance | 0.05 | 표면 감지 캐스트 거리 |
| attachedCheckMultiplier | 3 | Attached/Charging 표면 감지 거리 배율 |
| airborneCheckMultiplier | 1 | Airborne 표면 감지 거리 배율 |
| postLaunchIgnoreTime | 0.05 | 발사 후 입력 무시 |
| launchClearanceThreshold | 0.05 | 발사 표면 무시 해제 거리 |
| maxIgnoreTime | 0.15 | 발사 표면 무시 최대 시간 |

---

## 9. 입력 요약

### 표면 (Attached)

| 입력 | 동작 |
|------|------|
| A / D | 표면 위 크롤링 (접선 투영) |
| W | 홉 (표면 법선 방향 작은 점프, 접선 운동량 보존) |
| S | 그라운드 대시 (크롤 방향 순간 부스트, A/D 입력 필요) |
| Space 홀드 | 차징 시작 (판정원 생성, 조준점 중심). Dash → Space = 하이퍼 대시 |

### 차징 (Charging)

| 입력 | 동작 |
|------|------|
| WASD | 조준점 2D 조작 (바깥=가속, 안=감속, 좌우=각도) |
| Space 릴리즈 | 수동 발사 |
| (자동) | 가장자리 도달 시 자동 발사 |

### 공중 (Airborne)

| 입력 | 동작 |
|------|------|
| A / D | 수평 가감속 (하강 시에만) |
| W | 홉 버퍼 (착지 시 즉시 홉 발동) / 중력 조절 float (하강 시) |
| S | 대시 버퍼 (착지 시 A/D 방향으로 대시 발동) / 중력 조절 dive (하강 시) |
| Space 홀드 | 표면 충돌 시 에너지 전환 준비 |

**착지 시 입력 우선순위**: Space(Charging) > W(Hop) > S(Dash). 상위 입력이 하위 버퍼를 소비.

### 표면 충돌

| 충돌 시 상태 | 결과 |
|-------------|------|
| Space 안 누름 | velocity = 0, Attached (에너지 소멸) |
| Space 누르고 있음 | velocity = 0, Charging 직행 (판정원 확장 가능) |
| 천장 | 부착 안 함. 법선 속도 상쇄, Airborne 유지 |

---

## 10. 설계 원칙

1. **원 하나가 게임 전부**: 단일 원형 콜라이더로 모든 게임플레이. 소프트바디는 비주얼 오버레이.
2. **점성 슬라임**: 표면에 닿으면 붙는다. 바운스 없음. 운동량 전부 흡수.
3. **판정원이 파워와 방향을 동시 결정**: 조준점 위치 하나로 발사 벡터가 결정됨. 별도 계수 없음.
4. **불안정함은 시간이 만든다**: 벽/경사 슬라이드로 차징 시간 제한. 안정도 시스템 없음.
5. **차징은 커밋**: 캔슬 불가. WASD로 발사까지 능동적 조절.
6. **사전 커밋 보상**: 에너지 전환은 부딪히기 전에 Space를 미리 누른 플레이어가 보상받음.
7. **조준 관성은 기하학적**: 가장자리에서 방향 전환이 어려움. 파라미터 불필요.
8. **플레이어 조절 중력**: W/S로 중력 스케일 조절. 에너지 전환은 에너지 보존 공식으로 정규화.
9. **마찰 해체**: 이동 속도, 관성, 미끄러짐을 독립 파라미터로 분리.
10. **시스템 분리**: 중력 → 접선 분해. 에어 컨트롤 → 방향 회전. 차징 → 판정원 내부. 드리프트 → 표면 법선. 각각 독립.
