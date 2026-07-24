# 대나무 V3 (Bamboo V3) — 물리 기반 점프대

> **진행 상태 (2026-07-24): 인게임 미완.** 착지 임펄스를 상대속도×줄기관성으로 바꾼 마지막 수정이 미검증이다. 세션 핸드오프 · 폐기된 접근 · 실측 로그는 [/docs/bamboo-v3-attempts.md](../../bamboo-v3-attempts.md).

기울여 배치하는 **점프대**. 슬라임이 부딪히면 대나무 물성대로 휘고, 복원되면서 **입력 없이** 슬라임을 튕겨낸다. 약하게 닿으면 안 튕기고 그냥 발판이 된다. 둘을 가르는 건 노브 **하나**다.

> [Bamboo v1](bamboo.md)은 그대로 유지된다. 씬의 기존 대나무 9개, tip-grab, Side-Rebound V2 전부 무수정. V3는 **새 게임플레이 대나무용**이고, v1을 교체하지 않는다.

## 왜 새로 만들었나

기울인 대나무를 아래에서 위로 타는 플레이를 만들다 네 가지가 동시에 깨졌다. 전부 v1의 **정직한 캔틸레버 ODE 위에 얹힌 7겹의 비물리 보정** 때문이었다.

| 증상 | v1의 원인 |
|---|---|
| 밑에서 부딪혀도 안 휨 | `ApplyV2ImpactKick`이 충돌 속도의 **줄기 직각 성분만** 사용 (`SignedPerpendicularSpeed`). 줄기와 나란히 들어가면 0 |
| 점프해도 반동 출렁 없음 | 발사 순간 `riderAttached=false` → **스프링 세트가 통째로 교체**. `v2*`(0.7Hz/ζ0.12/cap 130°) → 기본(1.2Hz/ζ0.45/**cap 30°**) + `passiveDecayRate 1.2`. 휨이 30°를 넘어 있었으면 그 프레임에 30°로 순간이동하고, `clampRestitution: 0`이라 각속도가 0으로 지워진다. **반동에 쓸 에너지를 슬라임이 떠나는 그 프레임에 삭제한다** |
| 약한 충돌에 휨 ≈ 0 | 커브가 원점 시작 직선 + `v2BendMinHeightScale 0.45` + 길이 정규화(`θ=2·travel/L`). 커브 Y는 **미터**인데 화면에 보이는 건 **각도**라, 씬 대나무가 4~20m로 섞여 있으면 같은 커브가 각도로 4배 차이 |
| 씬 대나무 2개는 V2가 아예 꺼짐 | 길이 20/길이 5짜리가 `enableSideReboundV2: 0`인 `BambooSettings.asset`을 참조 |

노브 26개 중 상당수가 서로를 상쇄했다. 대표적으로 **모멘트 암 `r`이 이미 접촉 높이 보정인데** `v2BendHeightPower`/`v2BendMinHeightScale`가 그 위에 한 번 더 걸렸다.

V3의 원칙: **정직한 ODE 하나. 물리 아닌 규칙은 딱 하나(접착 파단)만 두고, 그 하나는 명시적으로 표시한다.**

## 물리 모델

### 스프링 — 1세트, 모드 분기 없음

```
I  = m_stalk·L²/3 + (riderAttached ? m_rider·r² : 0)
ω  = 2π·naturalFreqHz
k  = ω²·I,   c = 2ζω·I
θ̈  = (−kθ − cθ̇ + τ_weight) / I
```

`riderAttached` 여부로 **스프링 상수도, 캡도, 감쇠도 바뀌지 않는다.** v1에서 반동을 죽이던 주범이 이것이었다.

`θ`(=`currentAngle`)는 원호 끝단의 **접선 각도**다. 강체봉 회전각이 아니다 — 90°는 1/4 원호.

### 대나무를 휘게 하는 것 — 전부 진짜 물리, 배율 없음

| | 식 | 훅 |
|---|---|---|
| 라이더 무게 | `τ = r × (m·g)` | `ApplyRiderInfluence` 매 FU |
| 착지 (완전비탄성 포획) | `ω' = (I_stalk·ω + r × m·v_impact) / (I_stalk + m·r²)` | `ApplyRiderInfluence` firstAttach |
| 라이더 운동량 변화 | `Δω = (r × m·Δv) / I_total` | `ApplyRiderInfluence` 이후 프레임 |
| 발사 반작용 (뉴턴 3법칙) | `Δω = (r × −m·(v_launch − v_rider_before)) / I_stalk` | `SlimeController.OnLaunch` |

**투영이 없다.** `r × J` 외적이 입사각을 알아서 처리한다 — 줄기와 나란히 들어오면 작은 게 맞고, 비스듬히 들어오면 자동으로 커진다. `|r|`이 접촉 높이를 처리하므로 높이 보정 노브도 필요 없다.

**무게 토크는 수직 대나무에서 0이다** (`r.x = 0`). 기울인 대나무에서만 정적 처짐이 생긴다 — "얹으면 살짝 처진다"가 공짜로 나온다.

### 관성 선택이 경우마다 다르다

세 가지 임펄스가 서로 다른 관성으로 응답한다. 헷갈리기 쉬운 지점이라 명시한다.

- **착지** — 라이더가 임펄스만 주는 게 아니라 회전계에 **합류**한다. 그래서 넘어가는 건 각속도가 아니라 **각운동량**이다. 각속도에 임펄스를 그냥 더하면(임펄스만 새 관성으로 나누면) 이미 흔들리는 대나무에 올라탄 슬라임이 대나무를 **가속시킨다** — 공짜 에너지. 정지 상태에서 출발하면 두 식은 일치하므로, 차이는 대나무가 이미 움직일 때만 드러난다.
- **결합 중 프레임** — 둘이 같이 돈다. `I_total`(라이더 포함).
- **발사** — 임펄스가 둘을 **분리**시키므로 직후 대나무는 혼자 움직인다. `I_stalk`만. `I_total`을 쓰면 반동이 `I_stalk/I_total`배로 과소평가된다 (L=5, arm 2.5 기준 57%).

### 발사 반작용을 왜 `OnLaunch` 이벤트로 잡나

`RemoveRider()`(= `carrier.Detach()`)에서 처리하면 **안 된다.** 이유:

붙어 있는 슬라임의 `velocity`는 (a) 0이 아니고 — [Attached.cs:95](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Attached.cs)가 슬라이드 접선 속도를 넣는다 — (b) carrier가 **위치로** 운반하는 표면 속도(`ω × arm`)를 포함하지 않는다 (`ApplyPositionCarry`/`GetRiderCarryDelta`). 즉 detach 시점의 속도 표현이 **손실적**이다.

그래서 detach 시점 속도만으로는 이 넷을 구분할 수 없다:

| 경로 | detach 시 slime.Velocity |
|---|---|
| 플레이어 차지 발사 | 발사 속도 (carrier 상속분 포함) |
| 옆 표면으로 기어감 | 직전 슬라이드 속도 (0 아님) |
| Hazard 넉백 | **넉백 전** 값 — [Hazard.cs:83](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Hazard.cs)이 `Detach()`를 속도 대입보다 **먼저** 부른다 |
| Teleport | 직전 값 |

크기로 추정하면 양쪽으로 다 틀린다 — 발사마다 스윙을 크게 깎거나(5m 대나무에서 약 75%), 기어나갈 때마다 스윙을 공짜로 키운다.

해결: `RemoveRider()`는 **스냅샷만 arm**하고, 실제 임펄스는 [`SlimeController.OnLaunch`](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs)가 왔을 때만 적용한다. 이 이벤트는 [Launch.cs:147](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Launch.cs) **플레이어 차지 발사에서만** 발생하고 `ForceAirborneWithVelocity`(JumpPad·접착 파단·넉백)에서는 발생하지 않는다. 일반 decouple은 이벤트가 안 오니 대나무는 `ω`를 그대로 유지한다 — 라이더 관성이 결합에서 빠지는 것만으로는 대나무 자신의 `I_stalk·ω`가 변하지 않으므로 이게 옳다.

스냅샷은 `Time.frameCount`로 잠근다. `Launch()`는 같은 호출 안에서 `carrier.Detach()`(→`RemoveRider`) 후 `OnLaunch`를 쏘므로 같은 프레임이고, 나중에 **다른 발판**에서 발사할 때 묵은 스냅샷이 쓰이는 일이 없다.

라이더의 진짜 발사 전 속도 = `previousRiderVel + ω × arm`. 뒤 항을 빼주지 않으면 `Launch`가 carrier 상속으로 돌려준 대나무 자신의 운동량을 "플레이어가 민 것"으로 다시 청구하게 되고, 그게 발사마다 스윙을 깎는다.

### 자동 튕김 = 접착 파단 (V3의 유일한 비물리 규칙)

슬라임은 모든 것에 붙는다. 그냥 두면 스윙 내내 매달려 있고 절대 안 날아간다. 실제 접착에는 한계가 있다 — 붙잡고 있는 표면이 결합이 따라갈 수 있는 것보다 세게 가속하면 떨어진다.

접촉점은 피벗 중심 반지름 `|arm|` 원 위를 돈다. 접선 가속 `α·r`과 구심 가속 `ω²·r`의 합성:

```
a = |arm| · √(α² + ω⁴)          α = θ̈ [rad/s²], ω = θ̇ [rad/s]

a > adhesionBreakAccel  →  slime.ForceAirborneWithVelocity(ω × arm)
```

- 세게 박음 → 크게 휨 → `a` 큼 → **자동 튕김**, 스페이스 불필요
- 약하게 박음 → `a` 작음 → **안 떨어짐, 발판**
- 정적 평형에 매달림 → `α = ω = 0` → `a = 0` → **영원히 안 떨어짐** (오작동 없음)

**튕기는 타이밍이 물리에서 저절로 나온다.** 질량-스프링은 rest를 지날 때 가장 빠르고 직후에 가장 세게 감속한다 — 트램펄린이 사람을 던지는 그 지점이다. 별도 타이밍 규칙이 없다.

`ForceAirborneWithVelocity`는 JumpPad / HammerPlatform / JumpBall / Snowball / CloudPlatform이 이미 쓰는 공개 경로다. 내부에서 carrier 해제 + `RegisterLaunchClearance()`(재부착 방지 collider ignore) + Airborne 전이까지 처리한다.

**검산** (L=5, arm 2.5, m=1, 15 m/s 발사 반작용): peak 휨 ≈ 34°, rest 통과 시 `a` ≈ 50 m/s²(5G), peak에서 ≈ 85 m/s²(8.7G). 기본값 40(4G)이면 세게=튕김 / 약하게=붙음이 갈린다.

## 노브 5개

| 노브 | 기본 | 의미 |
|---|---|---|
| `naturalFreqHz` | 2.0 | 줄기 스프링 주파수. **"뻣뻣하다"는 작은 진폭이 아니라 빠른 복원이다.** 2Hz = 0.5초 주기. v1이 쓰던 0.7Hz(1.43초)는 어떻게 튜닝해도 고무줄로 읽힌다 |
| `dampingRatio` | 0.3 | 몇 번 흔들리고 멎나. 0.3 = 2~3회, 0.1 = 젤리, 0.7 = 첫 복귀에 거의 정지. **유일한 에너지 싱크** — 별도 decay rate가 없어서 설정값이 곧 보이는 것 |
| `maxDeflectionDeg` | 60 | `|θ|` 하드 캡. 초과 시 각도 clamp + 바깥 방향 각속도 흡수. **라이더 유무와 무관하게 항상 동일** |
| `maxTipSpeed` | 18 | `|θ̇|·L` 상한. carrier 상속이 배율 없이 1.0이라 **이게 곧 부스트 천장** |
| `adhesionBreakAccel` | 40 | 접착 파단 임계 (m/s²) |

**튜닝 규칙:** 약해도 튕기면 `adhesionBreakAccel`을 **올리고**, 세게 박아도 안 튕기면 **내린다.** 다른 노브는 안 건드려도 된다 — 그게 이 설계의 목적이다.

carrier 상속은 `VelocityTransferScale = 1`(`PlatformMotor` 기본값). 전송 배율도, falloff 게이트도, 높이별 분율도 없다. 부스트 천장은 `maxTipSpeed` 하나.

per-stalk 인스펙터 필드(길이·segment 수·반지름·색·마디·툰·테셀레이션·`backgroundOnly`)는 컴포넌트에 남아 있어서, 같은 settings 에셋을 공유해도 대나무마다 다르게 보일 수 있다.

## v1에서 걷어낸 것

| 걷어낸 것 | 이유 |
|---|---|
| `v2ResponseMode` + 커브 4개 | `r × J`가 커브 없이 입사각·속도를 다 처리 |
| 접촉 높이 보정 4개 (`v2Bend*HeightScale/Power`, `v2LaunchRelease*Height*`) | **모멘트 암 `r`이 이미 높이 보정.** 이중 적용이었음 |
| `v2LaunchRelease*` 5개 | 뉴턴 3법칙이 대체 |
| 스프링 2세트 | 1세트. detach 프레임 교체가 반동을 죽이던 주범 |
| 캡 2세트 (130° / 30°) | 1개. 캡 교체가 휨을 순간이동시켰음 |
| `passiveDecayRate` / `riderDecayRate` | damping ratio 밖의 유령 에너지 싱크 |
| 직각/접선 투영 | 축 방향으로 들어오는 충돌을 0으로 만들던 원인 |
| tip-grab 12개 + `enableTipGrab`/`enableSideReboundV2` | V3는 순수 점프대. 매달림/그네 펌핑 없음 |

**26 → 5.**

## 오브젝트 계층

```
BambooPlatformV3   (Rigidbody2D kinematic + BambooPlatformV3)
  ├─ Segment_00 ~ Segment_(N-1)   (CapsuleCollider2D 또는 Box, 매 FU 위치/회전 갱신)
  └─ Visual                       (MeshFilter + MeshRenderer + 절차 cylinder + BambooBend material)
```

기하·비주얼은 v1과 동일하다. 원호 centerline `(L/θ)(cos−1), (L/θ)sin` — 셰이더 `BambooBend.shader`와 **정확히 같은 식**이라 collider polyline과 visual이 sub-pixel 일치한다. 마디 링은 `segmentCount`로 구동되어 collider 분할 지점과 겹친다.

원기둥 메시 생성은 `BambooStalkMesh.Build()`로 분리했다. v1은 자기 private 복사본을 그대로 쓴다 — v1 무수정 결정에 따른 의도적 중복이다.

## 기울여 배치

기울기 = **루트 Transform Z 회전.** 전용 필드 없음.

`ComputeMotion`이 Awake에서 캡처한 `baseRotation`을 매 FU 그대로 돌려주므로 루트는 기울기를 유지한 채 회전하지 않는다. 휨은 child segment의 localRotation에만 산다. collider와 Visual 모두 루트 child라 같이 기운다.

V3에는 **world up과 비교하는 판정이 아예 없다** (tip-grab 진입 dot도, side-contact 판정도 삭제됨). 그래서 v1이 필요로 했던 `StalkUp` 보정 대상이 남아 있지 않다 — `StalkUp`은 gizmo와 외부 조회용으로만 노출된다.

## SlimeController 수정 0

V3는 기존 공개/가상 훅만 쓴다.

| 필요한 것 | 사용한 기존 훅 |
|---|---|
| 접착 파단 발화 | `PlatformMotor.OnAfterMotion(dt)` (실행순서 −100, SlimeController 0보다 먼저) → `slime.ForceAirborneWithVelocity()` |
| 발사 반작용 | `SlimeController.OnLaunch` 이벤트 구독 |
| 표면 snap | `ISurfaceAnchorProvider.TryGetSurfaceAnchor` — [SlimeController.cs `GetSurfaceAnchorProvider`](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs)가 일반화되어 있음 |
| 표면 추종 | `PlatformMotor.GetRiderCarryDelta` 오버라이드 (`ω × arm × dt`) |
| 라이더 물리 | `PlatformMotor.ApplyRiderInfluence` / `RemoveRider` 오버라이드 |

`IPlatformEffect`는 **쓰지 않는다.** `CheckPlatformEffect`가 착지 분기 2곳에서만 불리고 표면 전환 시(`carrier.Attach`)엔 안 불려서, 기어 들어온 슬라임에 훅이 안 걸린다.

### 실행순서 의존

```
−200  PlatformCarrier.FixedUpdate  → ApplyRiderInfluence (라이더 임펄스/무게 토크 적립)
−100  PlatformMotor.FixedUpdate    → ComputeMotion (ODE 적분) → OnAfterMotion (segment/visual/접착 파단)
   0  SlimeController.FixedUpdate  → 상태 처리
```

접착 파단이 −100에서 발화하므로 슬라임은 **그 프레임에 이미 Airborne**으로 처리된다. 한 프레임 늦지 않는다.

### 재진입 안전성

`CheckAdhesionBreak` → `ForceAirborneWithVelocity` → `carrier.Detach()` → 우리 `RemoveRider()`로 되돌아온다. `selfEjecting` 플래그가 이 재진입 경로에서 반작용 스냅샷 arm을 막는다 (접착 파단은 표면 속도로 놓아주는 것이지 미는 게 아니다). `try/finally`로 잠근 이유는 `ForceAirborneWithVelocity`가 `IPlatformEffect.OnSlimeDetach` 리스너로 퍼지기 때문 — 그 안에서 예외가 나면 플래그가 true로 굳어 이후 모든 발사 반동이 조용히 죽는다.

`ejectLockoutTimer`(0.15초 하드코드, 노브 아님)는 튕겨낸 직후 아직 세게 흔들리는 대나무가 슬라임을 즉시 다시 떼어내는 것을 막는다.

> 알려진 한계: `ForceAirborneWithVelocity` 자체에는 재진입 가드가 없고 `ClearPlatformEffect`가 임의의 `OnSlimeDetach` 콜백을 실행한다. 현재 구현들 중 재진입하는 것은 없고 V3는 `IPlatformEffect`를 구현하지 않는다. SlimeController 무수정이 이번 작업의 스코프라 의도적으로 손대지 않았다.

## 에디터

- `[ContextMenu("Rebuild Segments")]` — 길이/segment 수/반지름/collider 모양/테셀레이션 변경 시 mesh + collider 재생성
- `[ContextMenu("Refresh Visual Material")]` — 색/마디/툰 값만 바꿨을 때 즉시 반영
- `OnValidate` → `EditorApplication.delayCall` 지연 갱신 (대부분의 visual 필드 live 반영)

제공 에셋: `Assets/Settings/BambooV3Settings.asset`

## 배치 방법

1. 빈 GameObject에 `BambooPlatformV3` 추가
2. `settings`에 `BambooV3Settings.asset` 연결
3. 루트 Transform Z 회전으로 기울임
4. 컴포넌트 헤더 우클릭 → `Rebuild Segments`

## 테스트 씬

메뉴 `SlimeJumping → Platform → Setup BambooPlatform V3 Test` (`Editor/BambooPlatformV3TestSetup.cs`). 기존 `BambooPlatformTest` 셋업과 같은 패턴 — 같은 이름의 루트가 있으면 지우고 새로 만든다.

바닥 + 좌우 벽 + LaunchPad + **대나무 5개**:

| 이름 | 기울기(Z) | 길이 | 목적 |
|---|---|---|---|
| `V3_Vertical` | 0° | 5 | **대조군.** 수직에선 무게 토크가 0(`r.x=0`)이라 충돌로만 움직인다. 여기서 처짐이 보이면 다른 게 작용하는 것 |
| `V3_Tilt30` | −30° | 6 | 완만한 기울기 |
| `V3_Tilt50` | −50° | 7 | |
| `V3_Tilt70` | −70° | 6 | 거의 수평 — 밑면 정면 타격이 가장 강하고 무게 처짐도 최대 |
| `V3_Long` | −40° | 13 | **길이 무관성 확인.** 같은 settings 에셋을 쓰므로 짧은 것들과 일관돼야 한다. 4~20m 혼재 씬에서 이게 안 맞던 게 v1 튜닝이 깨진 이유 |

기울인 4개는 **받침대 위에 올려서 줄기 밑이 열린 공간**이 되게 배치했다. 바닥에 직접 세운 대나무는 밑동 근처 밑면을 칠 수가 없어서, v1의 "밑에서 치면 안 휨" 버그가 잘 안 보이던 배치였다.

## 알려진 한계

- 단일 라이더 가정
- `pivotWorldPos`는 Awake 캡처 — 런타임 이동 불가
- 길이/segment 수 런타임 변경 시 mesh/collider 재생성 안 됨 (Rebuild Segments 필요)
- 슬라임이 표면을 타고 흐르는 것(붙어서 미끄러짐)은 V3 범위 밖 — SlimeController의 슬라이드 시스템 문제
- 발사 반작용의 `ω × arm` 차감은 `surfaceMod.carrierFriction`이 1 미만인 표면 태그에서 약간 과다 차감된다. 물리적으로 실제 탑승 속도는 `ω × arm`이 맞으므로 그대로 쓴다
