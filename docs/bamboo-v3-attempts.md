# Bamboo V3 세션 핸드오프 (2026-07-24)

물리 기반 점프대 대나무 `BambooPlatformV3` 신규 구현. **컴파일 통과, 계측까지 완료, 마지막 수정은 인게임 미검증.**

기능 문서는 [features/platforms/bamboo-v3.md](features/platforms/bamboo-v3.md). 이 문서는 **왜 그렇게 됐는지 / 무엇이 틀렸었는지 / 다음에 뭘 할지**만 담는다.

---

## 1. 지금 상태 (내일 여기서 시작)

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
