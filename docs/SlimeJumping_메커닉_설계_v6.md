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

### Phase 1.5: 절차적 변형 비주얼 (현재 구현 완료)

방사 대칭 스프라이트 + 코드 기반 squash/stretch/관성 제어. 물리와 완전 분리된 비주얼 레이어.

- `SlimeVisual.cs` — `LateUpdate`에서 body localScale/localPosition 조작. 물리에 영향 없음.
- 표면 squash, 충격 squash, 속도 stretch, 호흡, 차징 변형을 slow/fast 2-layer SmoothDamp로 합성.
- **Visual Clearance Clamp**: 적용 직전 sag 방향 레이캐스트로 인접 표면 거리 측정 → offset과 scale을 ratio 기반 축소. 벽-바닥/천장-벽 코너에서 비주얼 침범 방지. smoothing target은 수정하지 않아 코너 이탈 시 자연 복구.
- 비대칭 셰이더(`_Asymmetry`, `_ChargeDip`, `_DipCenterU`)로 달걀 모양 + 차징 인덴트 표현.
- Controller가 노출하는 읽기 전용 API: `Velocity`, `SurfaceNormal`, `CurrentState`, `AimDirection`, `ChargingProgress`, `ChargingLaunchSpeed`.
- 상세 사양은 `슬라임_비주얼_진행현황_및_계획.md` 참조.

### Phase 2: 소프트바디 비주얼 오버레이 (후순위, 미구현)

Phase 1.5를 교체할 수 있는 장기 옵션.

- 중심 1개 + 주변 6개 노드가 스프링으로 연결. 중심 → 주변 단방향.
- 게임플레이 수치에는 일절 관여하지 않음. Phase 1 코드 수정 없이 추가 가능.
- Phase 1.5의 "Controller → Visual" 인터페이스를 그대로 재사용, 하부 구현만 교체.

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

`OverlapCircleNonAlloc`로 범위 내 콜라이더 수집 후, `ClosestPoint`로 법선·거리 계산. Active contacts (distance ≤ slimeRadius + contactSkin) 수집 후 천장 프리필터 + 5-step geometric fallback으로 primary 결정.

### 크롤링 (WASD 이동)

입력을 표면 접선 방향으로 투영. 가속/감속 모델로 관성 표현.

```
targetSpeed = input.x * crawlSpeed
overspeed = |crawlVelocity| > crawlSpeed
decel = overspeed ? crawlOverspeedDeceleration : crawlDeceleration

if (가속 또는 역방향)
    crawlVelocity = MoveTowards(crawlVelocity, targetSpeed, crawlAcceleration * dt)
else
    crawlVelocity = MoveTowards(crawlVelocity, targetSpeed, decel * dt)
```

- `crawlOverspeedDeceleration`: 최대 속도(crawlSpeed) 초과 시 적용되는 감속값. 착지 관성 등으로 crawlSpeed를 넘었을 때 별도 감속률 적용.

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
- **속도 감쇠**: 같은 방향 crawl 속도가 `dashFalloffStart`(5) 이상이면 impulse 선형 감소, `dashFalloffEnd`(13)에서 0. 반대 방향은 풀 impulse. Hop-dash chain exploit 방지.
- **입력 우선순위**: Charging > W(hop) > S(dash). Space 홀드 착지 시 둘 다 소비, hop 버퍼 활성 시 dash 버퍼 소비.

```
if (S pressed && cooldown <= 0 && |inputX| > 0)
    angleScale = 1 - InverseLerp(groundDashAngleMin, groundDashAngleMax, surfaceAngle)
    crawlVelocity += sign(inputDir) * groundDashImpulse * angleScale * surfaceMod.groundDashScale
    cooldown = groundDashCooldown
```

### 홉 (W 표면 점프)

Attached 상태에서 W를 누르면 표면 법선 방향으로 작은 점프. 기존 접선 운동량을 보존하면서 살짝 띄우는 표현용 액션.

- `velocity += Vector2.up * hopPower`. 표면 법선과 무관하게 항상 위쪽으로 점프. 스틱 중에는 `velocity = Vector2.up * hopPower` (충돌 잔여 속도 무시).
- `crawlVelocity = 0` (접선 운동량은 velocity 필드로 이관).
- **홉 모멘텀**: 스틱이 아닌 상태에서 홉 시 A/D 입력 방향으로 접선 속도 `hopMomentumBonus`만큼 가산. `hopMomentumMaxSpeed`에서 캡. 입력 중립이면 보너스 미적용. 반복 홉으로 점진적 가속 가능 (버니홉).
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
- **발사 시**: 일반 crawl 속도(`crawlSpeed`)를 baseline으로 빼고 dash 초과분만 반영. `excess = Max(0, |chargeEntryCrawl| - crawlSpeed)` → `velocity += lastSurfaceTangent * sign * excess * tangentInheritanceFactor`. 일반 crawl → charge = boost 0, dash → charge = dash 기여분만 boost.
- `tangentInheritanceFactor`(기본 0.7)로 boost 비율 조절.
- **홉 경로**: Hop 시 `chargeEntryCrawl = crawlVelocity` 캡처. 홉 후 에너지 전환 착지 시 crawl boost 사용 가능.
- **에너지 전환 감쇠**: 에너지 전환 크기에 비례하여 crawl boost 감쇠. `conversionRatio = activeChargeRadius / chargeRadius`로 측정. `hyperDashConversionDampingMin`(1.2) 이하면 100% 유지, `hyperDashConversionDampingMax`(2.0) 이상이면 0%. 약한 전환(홉 등)은 full boost, 강한 전환은 판정원 확장으로 이미 충분하므로 boost 제거.
- **코요테 탄젠트**: `lastSurfaceTangent`를 캐싱하여 코요테 중(표면 이탈) Launch에서도 올바른 접선 방향 사용.

### 스틱 타이머 (착지 후 표면 고정)

벽/급경사에 **공중에서 착지**할 때 충돌 속도에 비례한 시간 동안 슬라이드 없이 고정. 점성 슬라임이 철퍼덕 붙는 느낌.

**적용 시점**: Airborne → Attached 착지, Airborne → Charging 에너지 전환 착지. 두 경로만 해당.
**Attached→Charging 진입 시 (Charge Entry Grace)**: Space를 눌러 차징 진입하면 `stickTimer += chargeEntryStickBonus` (가산). 인위적 스틱 이벤트를 발동시켜 현재 접선 속도에서 부드럽게 감속(`stickDeccelTimer` 재시작). 슬라이드 중이던 슬라임이 잠시 표면을 붙잡는 효과. 기존 stickTimer가 남아있으면 그 위에 가산되므로 플레이어의 커밋이 항상 보상받음.

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
- **스틱→슬라이드 전환**: 스틱 만료 시 `slideRampTimer`를 리셋하여 슬라이드가 0→전체로 부드럽게 ramp-up. `wasStuck` 플래그로 만료 시점 감지.

### 표면 스냅 (2-Phase Depenetration)

**Phase A — Depenetration**: `OverlapCircleNonAlloc`로 주변 모든 콜라이더를 모은 뒤, 각 콜라이더에 대해 `ClosestPoint` 기반으로 침투 여부를 판단하고 밀어냄 (push only, 최대 3회 반복). 여러 면이 만나는 코너에서 Minkowski boundary로 자동 수렴.

**Phase B — Primary Pull**: primary surface와의 거리가 `slimeRadius + contactSkin`을 초과할 때만 끌어당김 (한 방향). 코너에서는 양쪽 면과 정확히 slimeRadius 거리이므로 Phase B 미발동 → 안정.

`rb.position` 직접 설정 (velocity 제어와의 충돌 방지).

### 표면 선택 (Geometric Primary Selection)

`OverlapCircleNonAlloc` + `ClosestPoint`로 active contacts (distance ≤ slimeRadius + contactSkin)를 수집한 뒤, 5-step fallback으로 primary 결정:
1. 단일 contact → 그것 (99% 케이스)
1.5. **천장 프리필터**: active contact에 천장(`surfaceUp < ceilingThreshold`)과 비천장이 혼재하면, 천장을 step 2-5 후보에서 제외. 전부 천장이면 필터 미적용.
2. 침투된 contact가 있으면 가장 깊은 것 (motion-aware, weight 없음)
3. 입력 방향 tiebreaker: `-normal · inputDir` 최대값
4. continuity: prevCollider가 active set에 있으면 유지
5. gravity alignment: `normal · up` 최대값 (fresh landing)

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
activeDriftSpeed = autoDriftSpeed * lerp(1, radiusRatio, aimDriftRadiusScale)
aimOffset += aimOffset.normalized * activeDriftSpeed * dt

// WASD
aimOffset += wasdInput * inputAcceleration * dt
```

### 발사 트리거

1. **스페이스 릴리즈** — 수동 발사. 현재 조준점 위치 기준.
2. **가장자리 도달** — 자동 발사. 풀파워 (또는 확장 시 초과 파워). 단, `chargeFullAimTime`(0.1초) 동안 방향 조정 유예. 이 시간 동안 파워는 확정(가장자리 고정)되고 WASD로 방향만 변경 가능. 유예 중 스페이스 릴리즈 시 즉시 발사.

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

**차징 슬라이드 중력 감쇠**: 차징 중 슬라이드의 중력 접선 성분에 `chargingSlideGravityScale`을 곱함. Charge Entry Grace의 스틱 만료 후 슬라이드가 재개될 때 완만하게 가속. 시간 압박은 유지하되 즉각적 미끄러짐을 억제.

### 차징 규칙

- **차징 캔슬 불가.** 스페이스 누르면 무조건 발사까지 진행. 커밋.
- **발사 후 입력 무시 윈도우: 0.05초.**
- **최소 발사력**: aimOffset이 minLaunchRatio 미만이면 최소 파워로 클램프 후 발사. Attached로 돌아가지 않음 (캔슬 불가 원칙).

### 차징 표면 프로브 히스테리시스

곡면/모서리에서 SurfaceProbe가 1~2 프레임 표면 감지를 놓칠 수 있음 (접선 이동에 의한 chord-arc gap, 모서리 기하 불안정 등). 감지 실패 시 즉시 coyote로 빠지면 Charging→coyote→Airborne→재충돌 오실레이션 발생.

**해결**: `chargingSurfaceProbeGraceFrames`(기본 3) 동안 캐시된 표면 데이터(법선, 접선, 콜라이더)로 valid 분기를 유지. grace 초과 시에만 coyote 진입.

- `surfaceProbeMissFrames`: 연속 miss 카운터. 유효 프레임마다 0으로 리셋.
- `cachedSurfaceCollider`: 마지막 유효 콜라이더. `null` 체크로 파괴된 오브젝트 방어.
- grace 중 `isTouchingSurface`는 건드리지 않음 — 다른 코드의 side effect 방지.
- grace 중 `SnapToSurface()`는 캐시된 콜라이더의 `ClosestPoint`를 사용하여 복원 시도.

### 부착 디바운스 (Attach Debounce)

모서리/코너에서 Attached↔Airborne 상태가 빠르게 토글되는 오실레이션 방지.

**메커니즘**: 짧은 시간(`attachToggleWindow`, 0.1초) 내 상태 전환 횟수를 카운트. `attachToggleLockoutThreshold`(기본 10) 도달 시 `attachLockoutDuration`(0.1초) 동안 착지 전환 차단.

- Lockout 중: 중력/에어 컨트롤은 정상 작동. 표면 접촉 시 법선 속도만 상쇄하여 관통 방지, 접선으로 미끄러져 벗어남.
- Launch/Hop은 의도적 이탈이므로 카운터 리셋 (`intentional` 플래그).
- 모든 비의도적 상태 전환이 카운트 대상 (Attached↔Airborne, Airborne↔Charging 포함).

### 상태 전환 중앙화 (ChangeState)

모든 `currentState` 변경은 `ChangeState(newState, intentional)` 함수를 통해 수행. 전환 시 디바운스 훅이 자동 실행. 향후 전환 관련 로직(사운드, 이벤트 등) 추가 시 단일 지점에서 처리.

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

### 드리프트 보상 (aimDriftRadiusScale)

확장 판정원에서 드리프트 속도를 얼마나 보상할지.

```
activeDriftSpeed = autoDriftSpeed * lerp(1, radiusRatio, aimDriftRadiusScale)
```

| aimDriftRadiusScale | 반지름 2배일 때 | 차징 시간 |
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

## 7-1. 무너지는 플랫폼 (Crumbling Platform)

슬라임이 부착하면 일정 시간 뒤 충돌 판정이 사라지는 플랫폼.

### 트리거 조건

- 슬라임이 **Attached 상태**로 이 플랫폼 표면에 붙은 순간 트리거.
- 한번 트리거되면 카운트다운은 **슬라임이 떨어져도 계속 진행** (hop으로 리셋 불가).
- 선택적 각도 필터: `useAngleLimit` 활성화 시 지정 각도 범위(0°=바닥, 90°=벽, 180°=천장) 내 부착에서만 트리거.
  - 각도 계산: `Acos(Dot(surfaceNormal, up))` — `SurfaceProbe.GetSurfaceAngle()`의 `Abs` 없는 버전 (0-180° 풀 레인지).

### 상태 머신

```
Idle → Triggered → Crumbling → Gone → (Respawn 대기 →) Idle
```

| 상태 | 동작 | 시각 피드백 |
|------|------|------------|
| Idle | 대기 | 없음 |
| Triggered | `triggerDelay` 카운트다운 | 색상 경고 (→빨강) + 약한 셰이크 |
| Crumbling | `crumbleDuration` 카운트다운 | 강한 셰이크 + 알파 감소 |
| Gone | 콜라이더 비활성, 렌더러 비활성 | 안 보임 |
| Respawn 대기 | `respawnTime` 후 겹침 체크 통과 시 복귀 | — |

### 파라미터

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| triggerDelay | 0.5 | 트리거 후 무너지기 시작까지 (초) |
| crumbleDuration | 0.2 | 무너지는 연출 시간 (초) |
| respawnTime | 3.0 | 사라진 뒤 복구까지 (초). 음수면 영구 소멸 |
| useAngleLimit | false | 각도 필터 활성화 |
| minSurfaceAngle | 0 | 트리거 최소 각도 |
| maxSurfaceAngle | 180 | 트리거 최대 각도 |

### SlimeController 연동

- **폴링 방식**: CrumblingPlatform이 매 FixedUpdate에서 `slime.CurrentState`, `slime.SurfaceCollider`를 확인. SlimeController 수정 없음.
- **콜라이더 비활성화 시**: SurfaceProbe가 다음 프레임에 표면을 못 찾아 자동으로 Airborne 전환.
- **Respawn 겹침 체크**: `Collider2D.Overlap()` — 슬라임이 비켜야 복귀.

### 구조 (비주얼 분리)

```
CrumblingPlatform (부모)
├─ BoxCollider2D, CrumblingPlatform.cs, (선택) PlatformMotor
└─ Visual (자식) — MeshFilter + MeshRenderer
```

셰이크를 자식 `localPosition`에만 적용하여 PlatformMotor의 `MovePosition`과 충돌하지 않음.

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
| dashFalloffStart | 5 | 이 속도까지는 풀 dash impulse |
| dashFalloffEnd | 13 | 이 속도 이상에선 dash impulse 0 (선형 감소) |

### Hop

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| hopPower | 2.25 | 홉 impulse (hopMinAngle 이하에서 적용) |
| hopPowerMin | 1.125 | hopMaxAngle에서의 홉 impulse |
| hopMinAngle | 70 | 이 각도 이하에서 hopPower 풀 적용 (도) |
| hopMaxAngle | 90 | 이 각도에서 hopPowerMin 적용. 사이는 lerp (도) |
| hopCooldown | 0.1 | 홉 간 쿨타임 (초). 버퍼 홉은 우회 |
| hopBufferDuration | 0.1 | Airborne에서 W 입력 버퍼 시간 |
| hopLandingRetention | 1 | 홉 착지 시 접선 속도 보존 비율. 경사 가속 제어용 (0~1) |
| hopMomentumBonus | 1.5 | 홉 시 접선 속도 가산량. 반복 홉으로 가속 |
| hopMomentumMaxSpeed | 20 | 홉 모멘텀 최대 접선 속도 캡 |

### Hyper Dash

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| tangentInheritanceFactor | 0.7 | 차징 진입 시 캡처한 crawl의 발사 합산 비율 (0~1) |
| hyperDashConversionDampingMin | 1.2 | 이 전환 비율 이하에서는 crawl boost 100% 유지 |
| hyperDashConversionDampingMax | 2.0 | 이 전환 비율 이상에서는 crawl boost 0% |

### Charge Circle

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| chargeRadius | 1.5 | 기본 판정원 반지름 |
| autoDriftSpeed | 1.5 | 조준점 자동 바깥 이동 속도 |
| inputAcceleration | 1.0 | WASD 조준점 가속도 (autoDriftSpeed보다 낮아야 무한 차징 방지) |
| chargeFullAimTime | 0.1 | 풀차지 후 방향 조정 유예 시간 (초). 파워 확정, 방향만 변경 가능 |
| baseLaunchPower | 15 | 풀파워 발사 속도 |
| minLaunchRatio | 0.05 | 최소 발사 비율 (제로파워 방지) |

### Energy Conversion

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| heightRestorationRatio | 2.25 | 에너지 전환 복원 비율 (effectiveRate = sqrt(ratio) * chargeRadius / baseLaunchPower) |
| aimDriftRadiusScale | 0.7 | 확장 시 드리프트 보상 (1.0=시간 동일, 0.0=시간 비례 증가) |

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

### Charge Entry Grace

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| chargeEntryStickBonus | 0.2 | Attached→Charging 진입 시 가산되는 스틱 시간 (초) |
| chargingSlideGravityScale | 0.5 | 차징 중 슬라이드 중력 접선 성분 배율 (0~1) |
| chargingSurfaceProbeGraceFrames | 3 | 차징 중 프로브 miss 허용 프레임 수 |

### Attach Debounce

| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| attachToggleWindow | 0.1 | 토글 감지 윈도우 (초). 이 시간 내 전환만 카운트 |
| attachToggleLockoutThreshold | 10 | 이 횟수 도달 시 lockout 발동 |
| attachLockoutDuration | 0.1 | Lockout 지속시간 (초). 착지 전환 차단 |

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
| surfaceCheckDistance | 0.05 | 표면 감지 거리 |
| contactSkin | 0.02 | active contact 판정 허용 오차 |
| depenetrationSkin | 0.01 | depenetration OverlapCircle 검색 마진 |
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

## 10. 메커니즘 시스템 (Portal, Signal, Button)

### 메커니즘 카테고리

두 종류로 분류:

- **A. Trigger volume류** (포탈, 킬존, 체크포인트, 바람, 부스터): Unity trigger collider로 표현. `OnTriggerEnter2D`는 `OnCollisionEnter2D`와 독립적 콜백. 물리적 배치(벽 표면보다 약간 앞)만으로 SlimeController 충돌 로직보다 먼저 처리 가능. Hook 불필요.
- **B. Contact interceptor류** (no-stick 영역, 바운스 패드, 부서지는 블록): 실제 collision 이벤트에 끼어들어 attach 로직을 대체/수정. 2개째 메커니즘부터 `ISlimeInteractor` 인터페이스 도입 예정.

### Portal (텔레포터 쌍)

Portal-style 파란색/주황색 텔레포터. 벽에 배치.

**프레임 규약**: `transform.right` = 포탈 바깥 방향(슬라임 출구), `transform.up` = 포탈 접선.

**속도 변환**:
```
vIn      = dot(velocity, -entry.OutNormal)   // 포탈로 들어오는 성분
vTangent = dot(velocity, entry.Tangent)      // 접선 성분
newV     = exit.OutNormal * vIn + exit.Tangent * vTangent
```

법선 성분은 출구 normal로, 접선 성분은 출구 tangent로 매핑. 원작 Portal의 거동과 동일.

**재진입 방지**: 출구 포탈에 `reentryCooldown`(0.2초) 적용. 텔레포트 직후 같은 프레임에 trigger 재발화 방지.

**TeleportTo**: SlimeController의 공개 메서드. 위치/속도 설정 + Airborne 강제 전환 + 모든 상태(Charging, Carrier, Stick 등) 초기화.

### SignalSource (신호 시스템)

`UnityEvent onActivate` / `onDeactivate`를 가진 베이스 클래스. Inspector에서 연결만으로 레벨 디자인 가능.

버튼, 압력판, 타이머, 무게 플랫폼 등이 상속. 코드 수정 없이 `onActivate → 원하는 메서드` 연결.

### WeightDescendPlatform (무게 하강 플랫폼 / 버튼)

슬라임이 위에 서면 천천히 내려가는 플랫폼. 바닥 도달 시 `Activate()` 발동. 슬라임 이탈 시 복귀, 복귀 완료 시 `Deactivate()`.

- Sensor 기반 감지 (Attached 상태 무관 — charging 중, 인접 표면 접촉 중에도 동작)
- PlatformMotor 미상속 (rider 의존 → time/waypoint 패턴과 다름)
- Kinematic Rigidbody2D + MovePosition

### PortalSwapper (포탈 위치 교체)

신호 수신 시 포탈을 두 슬롯 사이에서 교체. `swapDelay` 후 실행.

**Fling puzzle**: 포탈 루프(A→B→낙하→A→B...) 중 버튼으로 포탈 B 위치 변경 → 슬라임이 옆으로 사출.

### WindZone2D (바람 구간)

Trigger volume에 들어오면 슬라임에 지속적 가속을 적용하는 환경 기믹. Pogostuck, Egging On 등에서 볼 수 있는 표준 플랫포머 기믹.

**핵심 필드**:
- `windAcceleration` (Vector2): 바람 방향 + 세기 (units/s²). 방향은 월드 기준.
- `terminalSpeed` (float): 바람 방향으로의 최대 속도. 도달 시 가속 감쇠. 0이면 무제한.
- `airborneMultiplier` (float, 기본 1.0): Airborne 상태 배율.
- `attachedMultiplier` (float, 기본 0.7): Attached 상태 배율 (크롤/슬라이드 중).
- `chargingMultiplier` (float, 기본 0.2): Charging 상태 배율.

**동작**:
```
windDir = normalize(windAcceleration)
falloff = clamp01(1 - dot(slime.Velocity, windDir) / terminalSpeed)
deltaV  = windDir * |windAcceleration| * stateMultiplier * falloff * dt
slime.AddExternalVelocity(deltaV)
```

**상태별 처리**:
- Airborne: deltaV를 velocity에 직접 가산.
- Attached/Charging: deltaV를 `surfaceTangent`에 투영 후 가산. 법선 성분 제거로 표면 스냅 지터 방지.

**외부 힘 파이프라인** (`SlimeController.AddExternalVelocity`):
- `externalDeltaV` 누적자에 velocity delta 축적.
- FixedUpdate에서 state switch 이후, `rb.linearVelocity` 대입 직전에 적용.
- 적분 순서: 중력/상태물리 → 외부힘 → 플랫폼속도 → rb 대입.
- TeleportTo 시 누적자 초기화.

**비주얼**: 자식 ParticleSystem. Stretched Billboard + Velocity over Lifetime으로 바람 방향 시각화. Noise 모듈로 물결 효과 추가 가능.

### DragZone (감속/모래 볼륨)

Trigger volume. 슬라임이 어떤 방향으로든 진입/탈출할 수 있는 환경 기믹. 점프킹 스타일 모래. **고체 콜라이더 없음** — 순수 trigger.

**핵심 필드**:

| 필드 | 시점 | 설명 |
|------|------|------|
| `entryMultiplier` (0.3) | 진입 1회 | 외부 운동량 → 내부 운동량 변환 |
| `exitMultiplier` (3.33) | 탈출 1회 | 내부 운동량 → 외부 운동량 변환 (하향 Y 제외) |
| `launchMultiplier` (0.3) | 모래 안 발사 1회 | 발사 속도 보존 비율. exit boost와 조합되어 최종 속도 결정 |
| `dragFactor` (0.3) | 매 프레임 | 체류 시간에 비례한 지수 감쇠 (`velocity *= Pow(factor, dt)`) |
| `maxFallSpeed` (5) | 매 프레임 | Y 음수 cap, 무한 중력 가속 방지 |
| `allowCharging` (false) | — | Airborne에서 볼륨 내 차징 진입 허용 |

**동작 — WindZone2D 패턴**:
- `OnTriggerEnter2D`: `slime.OnDragZoneEnter(this)` → velocity × entryMultiplier
- `OnTriggerExit2D`: `slime.OnDragZoneExit(this)` → velocity × exitMultiplier (하향 Y 보존)
- `FixedUpdate`: per-frame drag + fall cap

**볼륨 차징** (allowCharging = true):

3번째 차징 진입 경로:
```
기존: Attached + jump press → Charging (표면)
기존: Airborne + 표면 + space 홀드 → Charging (에너지 전환)
신규: Airborne + DragZone(allowCharging) 안 + jump press → Charging (볼륨)
```

- `isVolumeCharging` 플래그로 표면 차징과 구분
- 판정원 aim 로직은 동일하게 동작 (drift, WASD, angular clamp, auto-launch)
- 표면 추적/스냅/슬라이드/코요테 없음
- 중력만 적용, fall cap은 DragZone.FixedUpdate가 처리
- 볼륨 이탈 시 차징 취소 → Airborne
- 발사: 순수 aim 방향 × power × launchMultiplier. 하이퍼 대시/캐리어 없음

**발사 후 속도 복원**:
```
모래 안 charge → Launch: velocity = dir × power × launchMultiplier
모래 밖으로 나감 → Exit: velocity × exitMultiplier
최종: dir × power × launchMultiplier × exitMultiplier
```

**DragZone끼리 겹침은 미지원** (CurrentDragZone 단일 참조).

**Scene 구조**:
```
SandVolume (GameObject)
├─ BoxCollider2D (trigger, Layer 8: DragVolume)
└─ DragZone.cs (entryMultiplier, exitMultiplier, launchMultiplier, dragFactor, maxFallSpeed, allowCharging)
```

---

## 10-1. PushDoor (밀어서 여는 문)

살롱 도어 스타일의 경첩 문. 슬라임이 접촉하면 콜라이더가 X축으로 평행이동하고, 분리된 3D 비주얼이 asin 매핑으로 회전하여 문이 안쪽으로 열리는 모습을 보여줌. 충분히 밀면 콜라이더가 비활성화되어 통과 가능, 이후 스프링 복원.

### 핵심 구조
- **물리**: 접촉 기반 push + 스프링-댐퍼 return. 상태 변수 = `currentX` (0이 닫힘).
- **Push**: 슬라임이 접촉 중이면 일정한 가속도로 문을 밀어냄 (velocity 무관, 위치 기반으로 방향 결정).
- **Return**: 슬라임이 떨어지면 스프링-댐퍼가 `currentX`를 0으로 복원.
- **비주얼**: 콜라이더와 분리된 3D 메시. `theta = -asin(currentX / doorDepth)` 매핑으로 회전. `doorDepth = transform.position.z + hingeOffset.z`.
- **콜라이더/비주얼 분리**: 콜라이더는 직선 이동, 비주얼만 3D 회전.

### 물리 모델
**Push (접촉 중)**:
```
xVelocity += pushDirection * pushAccel * dt
xVelocity = clamp(xVelocity, -pushMaxSpeed, pushMaxSpeed)
```
- `pushDirection`: 슬라임 위치에서 문 반대 방향 (`-sign(dot(toRider, right))`)
- push와 return 완전 분리 — pushAccel/pushMaxSpeed는 열림 속도만 결정

**Return (접촉 없음)**:
```
force = -returnStiffness * currentX - returnDamping * xVelocity
xVelocity += force * dt
```
- returnStiffness: 닫히는 힘
- returnDamping: 진동 억제

### 통과 조건
- `|currentX| >= openThreshold` → 콜라이더 비활성 (`enabled = false`), `openTimer = minOpenTime`
- `openTimer` 동안 재활성화 시도 안 함 (슬라임 통과 시간 확보)
- `openTimer` 만료 + `|currentX| < openThreshold` → overlap 체크 후 재활성화
- overlap 발견 시 재시도 딜레이 0.1초 (프레임 단위 flicker 방지)

### 비주얼 매핑
- `doorDepth = transform.position.z + hingeOffset.z` (힌지 월드 Z)
- `openThreshold = doorDepth * sin(openAngleDeg)`
- `theta = -asin(currentX / doorDepth)` (asin: 기하학적으로 정확한 원호 투영)
- `hingeOffset`으로 VisualPivot 위치 지정, `currentX` 보정으로 힌지 월드 고정
- Gizmo: 힌지 위치에 노란색 구 표시

### 방향 모드
- 양방향: currentX 양/음 모두 가능 (살롱 도어)
- 한 방향: 한쪽만 허용, 반대는 벽처럼 작용

### Carrier 추종
PlatformMotor 상속. 콜라이더가 X 평행이동하므로 carrier에 translation 델타로 자동 전파. 슬라임이 위에 서 있으면 문 복원 시 옆으로 밀려남.

### 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| pushAccel | 15 | 접촉 시 가속도 |
| pushMaxSpeed | 3 | 밀림 최대 속도 |
| returnStiffness | 10 | 닫히는 스프링 강도 |
| returnDamping | 3 | 닫힘 진동 억제 |
| openAngleDeg | 60 | 통과 가능 각도 (°) |
| oneWay | false | 한 방향 모드 |
| hingeOffset | (0,0,2) | 힌지 위치 (로컬 오프셋) |
| rotationAxis | (0,1,0) | 비주얼 회전축 |
| minOpenTime | 1 | 콜라이더 비활성 최소 시간 |

### Scene 구조
```
PushDoor (Layer 7)
├─ Rigidbody2D (Kinematic)
├─ BoxCollider2D
├─ PushDoor.cs
└─ VisualPivot (자식)
    └─ Mesh (3D Cube, Z 방향으로 문판 표현)
```

---

## 11. 설계 원칙

1. **원 하나가 게임 전부**: 단일 원형 콜라이더로 모든 게임플레이. 소프트바디는 비주얼 오버레이.
2. **점성 슬라임**: 표면에 닿으면 붙는다. 바운스 없음. 운동량 전부 흡수. (관성은 있을 수 있음)
3. **판정원이 파워와 방향을 동시 결정**: 조준점 위치 하나로 발사 벡터가 결정됨. 별도 계수 없음. (관성은 있을 수 있음)
4. **불안정함은 시간이 만든다**: 벽/경사 슬라이드로 차징 시간 제한. 안정도 시스템 없음.
5. **차징은 커밋**: 캔슬 불가. WASD로 발사까지 능동적 조절.
6. **사전 커밋 보상**: 에너지 전환은 부딪히기 전에 Space를 미리 누른 플레이어가 보상받음.
7. **조준 관성은 기하학적**: 가장자리에서 방향 전환이 어려움. 파라미터 불필요.
8. **플레이어 조절 중력**: W/S로 중력 스케일 조절. 에너지 전환은 에너지 보존 공식으로 정규화.
9. **마찰 해체**: 이동 속도, 관성, 미끄러짐을 독립 파라미터로 분리.
10. **시스템 분리**: 중력 → 접선 분해. 에어 컨트롤 → 방향 회전. 차징 → 판정원 내부. 드리프트 → 표면 법선. 각각 독립.
