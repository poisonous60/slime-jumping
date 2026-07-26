# Bamboo V3 세션 핸드오프 (2026-07-24)

물리 기반 점프대 대나무 `BambooPlatformV3` 신규 구현. **컴파일 통과, 계측까지 완료, 마지막 수정은 인게임 미검증.**

기능 문서는 [features/platforms/bamboo-v3.md](features/platforms/bamboo-v3.md). 이 문서는 **왜 그렇게 됐는지 / 무엇이 틀렸었는지 / 다음에 뭘 할지**만 담는다.

---

## 0. 업데이트 2026-07-25 (미검증)

유저 인게임 테스트 후 2건 수정. 둘 다 컴파일 통과, **인게임 미검증.**

### (A) 플레이어 발사 반동 제거 — 요구 6번 재해석

증상: 착지 후 가만히 두면 자동발사 잘 됨. 근데 평지처럼 **점프 버튼 누르면** 대나무 움직임이 이상해짐.

원인: `HandleSlimeLaunch`(OnLaunch 구독 = 플레이어 차지 발사 시 Newton 3법칙 반동)가 플레이어가 조준한 발사속도의 반작용을 대나무에 대입 → 자연 스윙을 걷어참. 자동발사(`ForceAirborneWithVelocity`)는 OnLaunch 안 raise → 그 경로는 멀쩡했음(유저가 본 그대로).

수정: **launch recoil 전체 제거.** 대나무는 착지 임펄스 + 무게 + 스프링 ODE로만 움직임. detach = 관성 커플링만 풀림, `I*omega` 유지 → 점프 누르든 말든 원래 반동. 딸린 기계(OnLaunch 구독/`recoil*` 스냅샷/`selfEjecting`/`EnsureLaunchSubscription`/OnEnable·OnDisable) 전부 정리. **이 접근(launch recoil)은 폐기 — 재도입 금지.**

**(A2) launch recoil 제거로 부족 — 진짜 범인은 `ApplyRiderInfluence` else 분기 (coupled 임펄스). 07-25 2차 수정.**

유저: 점프 눌렀을 때 대나무 영향받는 거 여전히 안 고쳐짐. 지시대로 계측 후 확정.

계측: `BambooPlatformV3`에 임펄스 노출 getter 추가(`LastRiderImpulseDeg/Kind`, `LastRiderCharging`, `LastWeightTorque`). carrier(-200)가 motor(-100)보다 먼저 도는 실행순서 때문에 ComputeMotion 리셋은 carrier 값을 덮음 → `Time.fixedTime` 스탬프로 staleness 처리. `BambooV3ChainDebug`를 **슬라임 부착**으로 재작성(유저 지시) + stalk 동적해석 + coupled 임펄스/상태전이 로그. 테스트 셋업도 슬라임 부착으로.

로그 확정: **coupled(kind 2) 임펄스가 charging=Y 프레임마다 대나무에 주입**(예 V3_Long `COUPLED 6.2°/s Charging=Y`, `-2.4`, `-3.3`...). else 분기 `Δω=(riderVel−prevRiderVel)·mass/ITotal`가 슬라임 per-frame velocity(carry 아티팩트 + 차징/조준 입력)를 스프링으로 되먹임 = 점프 입력이 대나무 움직임.

수정: **else 분기 전체 제거.** 대나무 = 착지 임펄스(firstAttach) + 무게 + ODE만 → 라이더 velocity 무관 → 점프 누르든 말든 **동일한** 스윙. `previousRiderVel` 필드 제거. carry 이중계산도 같이 제거(라이더 carried velocity = 대나무 자신의 ω×arm이라 대나무가 자기를 미는 항이었음). **이 접근(coupled per-frame 임펄스)도 폐기 — 재도입 금지.**

미검증(07-25). 검증법: 계측 남겨둠 — 재테스트 시 `kind 2`(coupled) 로그가 아예 안 떠야 정상. 남은 리스크: 무게 토크가 arm(위치) 의존이라 차징 중 슬라임 드리프트로 arm 변하면 2차 섭동 가능(작음). 그래도 이상하면 다음 용의자.

**(A3) 진범 = 차지 버튼 누르는 순간의 같은-플랫폼 재-Attach. 07-25 정적 audit으로 확정.**

(A2)의 freeze 분기가 안 먹은 이유: [SlimeController.Input.cs](../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Input.cs) `HandleSurfaceChargeEntry`가 점프 버튼 누르는 프레임에 `ChangeState(Charging)` 직후 **같은 대나무로 `carrier.Attach(surfaceCollider)`를 다시 호출**. `PlatformCarrier.Attach`가 무조건 `RemoveRider()`(→`riderAttached=false`) 후 `ApplyRiderInfluence` 호출 → `firstAttach=true`로 위장 → freeze 분기(`!firstAttach` 조건) 우회 → **착지 임펄스 분기 재발화**. 이때 attached 슬라임 velocity≈0이고 대나무 표면속도는 큼 → 상대속도 임펄스 `(0−surfaceVel)·mass/I_stalk` = 스윙을 즉시 꺾는 브레이크 킥 + `peakBendSinceAttach`/`returningFromPeak` 리셋(던짐 게이트 이력 소거). 매 차지 press마다 재현 = "점프 누르면 대나무가 이상해진다"의 실체.

수정(1줄, [PlatformCarrier.cs](../SlimeJumping_v2/Assets/Scripts/Platforms/PlatformCarrier.cs) `Attach`): `nextPlatform == currentPlatform`이면 early-return — 같은 플랫폼 재-Attach는 no-op. 다른 플랫폼 전이/최초 착지는 기존 그대로. audit 판정: 이전 3개 수정(recoil 제거·coupled 제거·freeze)은 전부 유지, 이 가드가 마지막 퍼즐.

검증법: Play → 대나무 위에서 차지 press → 그 순간 `kind 1`(landing) 임펄스 로그가 안 떠야 하고, `Attached -> Charging` 전이에서 angVel이 연속(급브레이크 없음), peak 이력 유지로 자동 던짐도 정상 발화.

**(A4) 약점프 재착지 버그 + 발사 보너스. 07-25 노브 5→7개.**

증상(유저): 약하게 점프하면 되돌아오는 대나무가 슬라임보다 빨라서 쫓아와 잡음 → 즉시 재착지. 기존 launch clearance(`ReattachGuard`)는 슬라임이 분리되는 순간 조기 해제라, **쫓아오는 표면**에는 무력.

수정 2건 (BambooV3Settings 노브 추가):
1. `launchBoostScale` (기본 1.5, 0~3) — `VelocityTransferScale` override. 발사 시 상속되는 ω×arm에 배수. 스윙 셀수록 보너스 큼, 죽은 perch면 0.
2. `relandLockoutTime` (기본 0.3s, 0~1) — `RemoveRider`에서 `slime.RegisterReattachGuard(segCol, t)`. **관통 금지(유저 지시)**: 처음 Passthrough(물리 무시)로 구현했다가 반려 — 어떤 경우에도 관통 없이 **밀어내야** 함. `IgnoreMode.TimedReattachGuard` 신설(IgnoredColliderList): probe만 풀 타이머로 스킵(분리 조기해제 없음) + **물리 ON** → 되돌아오는 줄기가 슬라임을 치면 물리로 밀어내고, 재부착만 차단. `RegisterSingle`에 ReattachGuard→TimedReattachGuard 업그레이드 추가(Launch가 먼저 등록한 clearance 엔트리가 분리해제를 유지하는 구멍 방지). SlimeController에 `RegisterReattachGuard` 공개 wrapper 추가.

주의: `RemoveRider`는 모든 이탈에서 발화(발사/자동던짐/hazard/다른 플랫폼 전이) — 전부 0.3s 가드. 의도된 동작. **Passthrough 재도입 금지.**

**(A5) 갇힘 루프 — 0속도 가짜 던짐. 07-25 로그 확정.**

증상: 슬라임이 점프 못하고 대나무에 갇힘. 로그: `rider left ... slime speed 0.2` 반복 — 던짐인데 0.2 m/s.

원인: `peakBendSinceAttach`가 **high-water mark**. 수직 줄기 옆면 부착의 정적 sag 평형(~34.8°) > 게이트(15°)라, 스윙이 평형으로 감쇠한 뒤에도 게이트 통과 상태 유지 → 평형 근처 ω 지터(±4°/s)의 반전+turnover가 던짐 발화 → `ω×arm ≈ 0.3 m/s`로 던짐 → 언로드된 줄기가 315°/s로 채찍 → 0.3s 락아웃 뒤 근처의 느린 슬라임을 잡음(impact 6.7, 임펄스 450) → 재휨 → 무한 루프. 차징 중에도 가짜 던짐이 `ForceAirborneWithVelocity`로 차지를 지움 = "점프 못함".

수정: `MinEjectSpeed = 2f` (상수, 노브 아님) — `CheckSpringThrow`에서 `|ejectVel| < 2`면 던지지 않고 rearm(스윙 소진 처리). 정상 던짐은 ~6+ m/s라 영향 없음. 게이트가 high-water mark인 건 유지(착지 스윙 판정엔 필요), 현재 ω가 실제 운반 능력을 검증.

**(A6) 약타 감도 노브. 07-25, 노브 7→8개.**

유저: 느린 접근/아슬아슬한 접촉도 쉽게 휘고 부스트 받게. 수정: `landingImpulseGain` (기본 2, 0.5~4) — 착지 임펄스 `(v_rel × mass)`에 flat 배수. 커브/임계값 아님(V1 병 금지): **`maxTipSpeed` 클램프가 하드 히트를 천장으로 눌러주므로 flat 게인은 실질 약타 전용 감도**가 됨. 상대속도 형태 유지 = 표면 동속 착지는 여전히 임펄스 0(공짜 에너지 없음).

**(A9) 착지 임펄스 0.1s 램프 주입. 07-25.**

(A8) 압축만으론 부착 실패 미해결(유저 지적) — 진짜 원인은 임펄스가 **순간 Δω**라 착지 프레임에 표면이 0→수십 m/s로 점프, 아직 부착 중인 슬라임 밑에서 표면이 순간이동 → probe 이탈. 수정: 임펄스를 `LandingImpulseRampTime = 0.1s`(상수) 동안 일정 속도로 스프링에 주입(`rampOmegaRemainingDeg`/`rampOmegaRateDeg`, ComputeMotion에서 드레인). 총 주입량 동일, 시작만 부드러움 → 슬라임이 먼저 안착하고 줄기가 태운 채 휨. `ApplyContactImpulse` 메서드는 유일 호출처가 램프로 대체돼 제거.

**(A8) 고속 임팩트 로그 압축. 07-25, 노브 9개.**

증상(유저): 빠르게 날아와 붙으면 대나무가 아주 크게 출렁이고 슬라임이 제대로 안 붙음(채찍백이 부착 전에 털어냄). 지시: 속도 크면 반동 줄이기, 로그함수처럼.

수정: `impactSoftCap` (기본 8 m/s, 0~30, 0=off) — 착지 상대속도에 `v_eff = cap·ln(1+v/cap)` 적용, **팁 보너스 게인보다 먼저**. 기울기 1@0이라 약타 무변화, 고속만 압축: 3→2.6, 10→6.5, 22→10.6, 40→14.4. 순서 = 압축(물리 완충) → heightT² 게인(스위트스팟 보상).

**(A10) 발사 부스트 상한 — boost-falloff 게이트 이식. 07-26, 노브 9→11개.**

유저: 이미 그만큼 높게 뛸 수 있으면(pre-bonus 속도가 이미 큼) 부스트를 안 주게. SpringyBranch/TautLine과 동일한 pre-bonus speed gate를 이식.

수정: `BambooV3Settings`에 `boostFalloffStartSpeed`(기본 12)/`boostFalloffEndSpeed`(기본 18) 추가. `BambooPlatformV3.VelocityTransferScale` getter가 발사 순간 `slime.Velocity.magnitude`(= EnergyConversion + 하이퍼대시 상속 포함, carrier 기여 **제외**)를 읽어 `launchBoostScale × speedMul` 반환 — start 이하 풀 부스트, end 이상 0, 사이 선형 falloff (end ≤ start면 하드 컷오프). SpringyBranchPlatform.cs:102의 게이트와 같은 식. 슬라임 자신의 속도는 그대로 통과, 줄기의 carrier 상속분만 깎임.

**(A7) 던짐 발화점 |ω| turnover → 리턴 절반 지점 + Charging 중 자동던짐 금지. 07-25.**

증상 2건(유저): ① 대나무가 되돌아올 때 자동 발사가 잘 안 남 ② 차징 조준 방향이 안 먹음.

로그 원인: ① turnover(평형 통과) = 표면 최고 속도 순간 = SurfaceProbe가 채찍 줄기를 놓치는 순간 → 던짐 전에 라이더가 임의 시점 자연 셰드(어중간한 속도로 이탈). ② turnover가 차징 중에도 발화 → `ForceAirborneWithVelocity`가 조준 발사를 ω×arm 방향으로 덮어씀.

수정(`CheckSpringThrow`): ① 발화점 = **리턴 절반** — `|θ−θ_eq| ≤ 0.5·|θ_peak−θ_eq|`, θ_eq = weightTorque/stiffness(기울임 무게 평형 반영). ω² = ωₙ²(A²−x²)에서 x=A/2 → **세기 87% 유지**, 표면 아직 느려 probe 생존. `peakAngleSigned` 필드 추가. ② 상태 체크 **Attached 전용** — 차징 = 플레이어 소유, 스윙 속도는 발사 시 carrier 상속으로 전달. |ω| turnover 스테이지2는 폐기(prevAngularVel은 정점 감지에 계속 사용).

**(A6b) 07-25 후속 — flat → 높이 가중.** 유저: 하부 착지에 위쪽이 흔들리는 게 어색, 정확한 위치(팁 근처) 착지에만 보너스. 게인 적용을 `lerp(1, landingImpulseGain, heightT²)` (heightT = |arm|/length)로 변경 — 밑동 = 물리 그대로(짧은 arm이라 원래 거의 안 휨), 팁 = 풀 보너스, 2차 곡선이라 상단 1/4가 스위트스팟. 새 노브 없음. V1의 contact-height shaping과 다름: V1은 moment arm이 이미 인코딩한 높이 위에 또 얹은 이중 보정이었고, 이건 **보너스 게인만** 높이 게이트(물리 성분은 arm 그대로).

### (B) 아랫면 매달림 — surface tag stick-then-slide

증상: 슬라임이 아랫면에 붙으면 **영원히 매달림.** 유저는 surface tag 탓으로 추측했으나 → 매달림 자체는 sticky 코어("sticks to all surfaces"), tag 아님. 기존 `slideGravityMultiplier=0`이 **모든 슬라이드**를 막아서 밑으로 미끄러져 떨어지지도 못한 것.

유저 확정 모델: 착지 시 **stick으로 프리즈**(휨/발사 동안 위치 고정 = 원래 흘러내림-during-jump 문제의 진짜 해결책) → 이후 **슬라이드 재개** → 윗면=플랫폼 슬라이드, 아랫면=밑으로 미끄러져 떨어짐.

`BambooV3_Surface.asset` 3개 값 변경 (코드 무수정):

| 노브 | 이전 | 이후 | 이유 |
|---|---|---|---|
| `slideGravityMultiplier` | 0 | **1** | 슬라이드 복원 |
| `slideResistanceMultiplier` | 4 | **1** | 마찰 8→2 m/s² < 중력접선 ~6 → 실제로 미끄러짐 (4는 슬라이드 완전 차단) |
| `stickDurationMultiplier` | 1 | **1.4** | stick 창(~0.5·1.4=0.7s) ≥ 발사 창(~0.5s) 보장 → 하드히트 자동발사가 프리즈된 위치에서 나감 |

`carrierFriction`=1 유지(필수). 근거: `stickSlopeThreshold=0.7`, Tilt50 윗면 surfaceUp≈cos50°≈0.64<0.7 → stick 발동. base stick=CalculateStickDuration(0.3~0.6s). Charging 중 `stickTimer>0`이면 velocity=0(프리즈).

**남은 리스크 (반드시 테스트):**
- 아랫면은 gravity-tangent가 **밑동 방향**(tip-up). 밑동까지 미끄러진 뒤 실제로 **떨어지는지**(segment 0 끝 이탈→airborne) vs 밑동에 **눌러앉는지**는 씬 밑동 지오메트리 의존. 안 떨어지면 → 아랫면 전용 detach 별도 추가 필요.
- 윗면 소프트 perch가 50° 경사라 stick 만료 후 **미끄러져 내려감** = 물리적으로 정상이나, 플랫폼으로 너무 빨리 흘러내리면 `slideResistanceMultiplier` 상향.
- stick 창 < 실제 발사 타이밍이면 프리즈 풀린 뒤 발사 = 위치 드리프트. 그러면 `stickDurationMultiplier` 더 상향.

내일 첫 할 일: Play → 점프 버튼 눌러도 대나무 자연 스윙 유지되나(A), 아랫면 붙으면 미끄러져 떨어지나(B), 윗면 착지-휨 중 위치 고정되나.

아래 §1~§9는 07-24 시점 기록(launch recoil 관련 §5·§6은 위 (A)로 폐기됨).

---

## 1. 지금 상태 (07-24 시점)

### 마지막 수정 — 미검증

착지 임펄스를 **상대속도 × 줄기 관성**으로 바꿨다. 그 직전까지의 실측 로그가 근거다:

```
impact 22.1 m/s, arm 5.68  →  peak bend 12.8°     (착지)
impact  3.0 m/s            →  tip speed 17.4 m/s  (발사 반동)
impact  8.6 m/s            →  tip speed 24.8 m/s  (발사 반동)
```

착지 22 m/s가 12.8°인데 발사 반동은 tip 24.8 m/s(당시 캡 30에 근접). **비대칭 3배 이상.**

원인은 두 임펄스가 다른 관성으로 나뉘던 것:

| | 나누던 관성 | V3_Tilt50 실측 |
|---|---|---|
| 착지 (완전비탄성 포획) | `I_stalk + m·r²` | 16.33 + 5.68² = **48.6** |
| 발사 반동 | `I_stalk` | **16.33** |

라이더 관성 `m·r² = 32.3`이 줄기 관성의 2배라, 착지 운동량의 2/3가 대나무를 휘게 하는 대신 **라이더를 피벗 둘레로 돌리는 데** 쓰였다.

수정 후 양쪽이 같은 형태다:

```csharp
// 착지 (ApplyRiderInfluence, firstAttach)
surfaceVel = ω × arm
Δω = (arm × m·(v_impact − surfaceVel)) / I_stalk

// 발사 (HandleSlimeLaunch)
riderVelBefore = previousRiderVel + ω × arm
Δω = (arm × −m·(v_launch − riderVelBefore)) / I_stalk
```

**상대속도가 `I_stalk` 사용을 정당화하는 핵심이다.** 생속도를 `I_stalk`로 나누면 이미 흔들리는 대나무에 슬라임이 같은 방향으로 올라탈 때 대나무가 가속된다(공짜 에너지). 상대속도면 표면과 같은 속도로 붙으면 임펄스가 정확히 0이다.

같이 조정한 값: `maxTipSpeed` 30 → **40** (안 그러면 ~14 m/s 이상 착지가 전부 같은 각도로 포화), `ejectBendDeg` 12 → **15** (peak가 약 3배가 되므로).

### 내일 첫 할 일

`SlimeJumping → Platform → Setup BambooPlatform V3 Test` 실행 후 Play. `V3_Tilt50`에 붙은 `BambooV3ChainDebug` 패널에서 확인:

1. **착지 peak가 발사 후 흔들림과 비슷한가** (요구 3번의 직접 판정)
2. **자동 발사가 나는가** — peak가 게이트 15°를 넘고, 되돌아오며 `|ω|` 최대 지점에서 던져야 함
3. `stage`가 계속 `Live`로 유지되는가 (중간에 `ContactNotProbed` / `ProbedNotAttached`로 빠지면 부착 문제)

안 되면 **`ejectBendDeg` 하나만** 만진다. 그게 이 설계의 목적이다.

---

## 2. 확정 요구사항 (grill-me 산출, 유저 확정)

1. 슬라임이 오면 대나무 물성대로 휜다 — 충돌 기반, 입력 불필요
2. **스페이스 안 눌러도** 반동으로 자동으로 튕겨나간다
3. 약하게 들어오면 휘긴 하되 안 튕기고 붙어 있다 → 그냥 발판
4. 뻣뻣해야 한다
5. 원하는 시퀀스: **접촉 → 휨 → 반동으로 반대쪽으로 휨 → 슬라임 날아감 → 대나무는 반동으로 흔들림**
6. 발사 **전**도 발사 **후**만큼 흔들려야 한다
7. tip-grab은 버린다. V1과 씬 대나무 9개는 무수정
8. 슬라임이 대나무 타고 흘러내리는 것 → **Surface tag로 해결** (유저 지시)

---

## 3. 무엇을 만들었나

### 신규 파일

| 파일 | 역할 |
|---|---|
| [Platforms/BambooPlatformV3.cs](../SlimeJumping_v2/Assets/Scripts/Platforms/BambooPlatformV3.cs) | 물리 ODE + 던짐 판정 + collider/visual |
| [Platforms/BambooStalkMesh.cs](../SlimeJumping_v2/Assets/Scripts/Platforms/BambooStalkMesh.cs) | 원기둥 메시 (순수 함수) |
| [Settings/BambooV3Settings.cs](../SlimeJumping_v2/Assets/Scripts/Settings/BambooV3Settings.cs) | 노브 5개 |
| [Debug/BambooV3ChainDebug.cs](../SlimeJumping_v2/Assets/Scripts/Debug/BambooV3ChainDebug.cs) | 체인 계측 (읽기 전용) |
| [Editor/BambooPlatformV3TestSetup.cs](../SlimeJumping_v2/Assets/Scripts/Editor/BambooPlatformV3TestSetup.cs) | 테스트 씬 메뉴 |
| `Assets/Settings/BambooV3Settings.asset` | 튜닝값 |
| `Assets/Settings/SurfaceMaterials/BambooV3_Surface.asset` | 흘러내림 방지 |

### 수정한 기존 파일 (2줄)

- `Platforms/PlatformCarrier.cs` — `public PlatformMotor CurrentPlatform` getter 추가 (계측용)
- `Platforms/BambooPlatform.cs` (V1) — **이번 대나무 작업과 무관한 별건 2개.** 아래 §7

**SlimeController는 한 줄도 안 고쳤다.** 기존 훅만 사용: `OnAfterMotion`(−100) / `ForceAirborneWithVelocity` / `OnLaunch` / `ISurfaceAnchorProvider` / `GetRiderCarryDelta` / `ApplyRiderInfluence` / `RemoveRider`.

### 노브 5개 + 현재값

| 노브 | 현재 | 의미 |
|---|---|---|
| `naturalFreqHz` | 1.0 | **peak 휨 ∝ 1/ω_n.** 2.0Hz로 시작했다가 물리적으로 크게 못 휘어서 내림 |
| `dampingRatio` | 0.25 | 유일한 에너지 싱크 (decayRate 없음) |
| `maxDeflectionDeg` | 90 | 각도 하드 캡 |
| `maxTipSpeed` | 40 | `|ω|·L` 상한 = 부스트 천장 |
| `ejectBendDeg` | 15 | 던짐 게이트 (정점 휨 각도) |

---

## 4. 던짐 판정 — 2단계 상태머신

```
접촉 → 임펄스로 휨
  ↓  ω 부호 반전 = 정점 도달        → returningFromPeak = true
반동으로 되돌아옴
  ↓  |ω| 최대 = 평형 통과            → 여기서 던짐
슬라임 날아감 (ω × arm)
  ↓  대나무는 반대쪽까지 휘고 계속 흔들림
```

게이트: `peakBendSinceAttach >= ejectBendDeg`. 미달이면 리셋하고 계속 흔들리는 발판으로 남는다.

**stage 2가 각도 zero-crossing이 아니라 `|ω|` turnover인 이유:** 기울인 대나무에 라이더가 있으면 평형이 0이 아니라 무게로 shift된 각도라서, 작은 스윙은 0을 아예 안 지날 수 있다.

**stage 1이 있어서 착지 프레임에 발화가 구조적으로 불가능하다.** 이게 중요하다 — 초기 구현이 정확히 그 버그였다.

---

## 5. 틀렸던 접근들 (다시 하지 말 것)

### ❌ 표면 가속도 임계 (`adhesionBreakAccel`)

`a = r·√(α² + ω⁴)`가 `adhesionBreakAccel`을 넘으면 던지는 방식. **폐기.**

이유: `a`가 스윙 전체에서 **거의 평평하다**(착지 직후 ≈ 정점). 실측 대입 시 착지 직후 81, 정점 82. 그래서 임계를 넘으면 **닿는 프레임에 즉발**하고 못 넘으면 **영원히 안 난다.** 중간이 없다. "rest 통과 직후 감속이 최대라 자연히 튕긴다"는 전제 자체가 이 파라미터 영역에서 성립하지 않았다.

증상: 유저가 "닿자마자 튕기는 걸 요구한 게 아니다" / "절대 안 튕긴다" 둘 다 겪음.

### ❌ 완전비탄성 포획으로 착지 처리

물리적으로는 맞지만 라이더 관성 `m·r²`가 줄기 관성의 2배라 착지 휨이 3배 약해졌다. §1 참조.

### ❌ `naturalFreqHz = 2.0`

"뻣뻣 = 빠른 복원"으로 정리하고 2Hz를 잡았는데, `peak θ = ω₀/ω_n`이라 휨을 같이 죽였다. **임펄스 하나로 큰 진폭과 빠른 복원을 동시에 얻을 수 없다.** 1.0Hz로 절충.

### ❌ 발사 반작용을 `RemoveRider()`에서 처리

detach 시점 속도로는 **발사/기어감/hazard/teleport를 구분할 수 없다.** 붙어있는 슬라임의 `velocity`는 0이 아니고([Attached.cs:95](../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Attached.cs)) carrier의 위치 운반분 `ω×arm`을 빠뜨린다. hazard는 넉백 대입 **전에** Detach한다. 크기로 추정하면 발사마다 스윙 75% 삭제 또는 기어나갈 때마다 공짜 증폭.

→ `SlimeController.OnLaunch` 이벤트로 이전. 이 이벤트는 `Launch()`에서만 발생하고 `ForceAirborneWithVelocity`(JumpPad·자동던짐·넉백)에선 안 난다. 스냅샷은 `Time.frameCount`로 잠가 묵은 반동 차단.

### ❌ 다각도 fan-out 조사

유저가 중단시킴. "왜 안 되는지는 디버그 컴포넌트 만들어서 하라"는 지시. **추측 대신 계측.** 실제로 계측 로그 한 번에 원인이 나왔다.

---

## 6. Codex 리뷰에서 잡힌 것 (반영 완료)

4회 왕복, 최종 PASS.

1. **착지가 각운동량 보존이 아니었다** — 각속도에 임펄스만 더해서 이미 흔들리는 대나무에 올라타면 가속됐다(공짜 에너지). → 완전비탄성 포획으로 수정 → 이후 §1에서 상대속도 방식으로 재수정(공짜 에너지 차단은 유지)
2. **분리 반동이 `I_total`을 썼다** — 임펄스 직후엔 라이더가 분리되므로 `I_stalk`. 반동이 정답의 57%였음
3. **표면속도 상속분 이중 반영** — `slime.Velocity`에 carrier 상속 `ω×arm`이 이미 들어있어 발사마다 스윙 75% 삭제
4. **구독 수명주기** — disable→enable 재구독 지연

미해결(의도적): `ForceAirborneWithVelocity` 자체에 재진입 가드가 없고 `ClearPlatformEffect`가 임의의 `OnSlimeDetach` 콜백을 실행한다. 현재 재진입하는 구현은 없고 V3는 `IPlatformEffect`를 구현하지 않는다. SlimeController 무수정이 스코프라 손대지 않았다.

---

## 7. 같은 세션의 별건 2개 (V1 `BambooPlatform`)

V3와 무관하게 먼저 처리한 것들. 이미 문서 반영됨.

1. **기울인 V1 대나무가 겉모습만 기울던 문제** — tip-grab 진입 dot / V2 side-contact / slam guard / bend pump 입력속도가 world up 기준이었다. `StalkUp`(= `transform.up`) + `PerpendicularSpeed()` 기준으로 전환. 수직에서는 이전과 수치 동일
2. **V1 발사 부스트에서 raw charge-power 보너스 그래프 삭제** — 대나무 휨 상태와 무관해 예측 불가였음. SpringyBranch/TautLine의 boost-falloff 게이트 이식

---

## 8. 계측 도구 사용법

`BambooV3ChainDebug`를 대나무에 붙이면 화면에 **끊긴 첫 링크를 이름으로** 찍는다:

| stage | 뜻 |
|---|---|
| `NoContact` | 슬라임이 근처에 없음 (최근접 segment + 거리) |
| `ContactNotProbed` | 겹쳐 있는데 SurfaceProbe가 표면으로 안 잡음 → layer / 부착 각도 상한 |
| `ProbedNotAttached` | 표면은 잡혔는데 Attached 아님 → 착지 거부/디바운스 (toggles·lockout 표시) |
| `AttachedNoCarrier` | Attached인데 carrier가 이 대나무가 아님 |
| `CarrierNoImpulse` | carrier는 붙었는데 `ApplyRiderInfluence` 미도달 |
| `Live` | 임펄스 도달. **`approach` 속도와 결과 `peak` 각도를 나란히 표시** |

`F7` = 최근 90 FixedUpdate 덤프. 라이더가 떠날 때마다 `impact / peak / gate / tip speed` 한 줄 로그.

읽기 전용이다 — SlimeController에도 대나무에도 push하지 않는다. `BambooPlatformV3`에 추가한 getter들(`RiderAttached`, `PeakBendSinceAttach`, `ReturningFromPeak` 등)은 전부 이미 있던 필드의 read-only view다.

---

## 9. 남은 리스크 / 미확인

- **§1 수정 인게임 미검증.** 계산상 peak가 약 3배지만 `maxTipSpeed 40 / L 7 = 5.71 rad/s` 클램프에 걸릴 수 있다. 걸리면 하드 착지들이 전부 같은 각도로 포화한다 — 그때는 `maxTipSpeed`를 올리되 부스트 천장도 같이 오른다는 점 유의
- **흘러내림 방지 Surface tag 미검증.** `BambooV3_Surface.asset`은 `slideGravityMultiplier 0` / `slideResistance 4` / `landingVelocityRetention 0` / `impactRedirectRatio 0`. `carrierFriction`은 **반드시 1 유지** — 낮추면 발사 시 `ω×arm` 상속이 깎여 부스트가 죽는다
- 밑동까지 흘러내리면 `arm → 0`이라 **던짐 속도와 차지 발사 보너스가 둘 다 0이 된다.** "차징하면 더 안 휜다"의 유력 원인
- 로그에 `ContactNotProbed`(gap 0.002인데 probe가 '-')와 `ProbedNotAttached`(Airborne)가 착지/이탈 전후로 등장한다. 과도기 프레임으로 보이지만 **지속적으로 나오면 부착 문제이므로 별도로 파야 한다**
- 단일 라이더 가정, `pivotWorldPos` Awake 캡처(런타임 이동 불가), 길이/segment 런타임 변경 시 Rebuild 필요
