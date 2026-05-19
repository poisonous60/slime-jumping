# 대나무 (Bamboo)

수직 대나무 한 그루. 슬라임이 옆구리에 attach하면 측면 lateral 충격으로 가지가 휘어짐 → 스프링 복원으로 진동. SpringyBranch의 horizontal cantilever를 vertical 대나무로 재해석, **N-segment collider polyline + 단일 vertex-bend shader visual** 하이브리드로 collider 곡선과 visual 곡선이 sub-pixel 일치.

## 설계 결정 흐름 (grill-me 산출)

| 질문 | 선택 | 이유 |
|------|------|------|
| Slime ↔ bamboo 모델 | **B. attach + slide** | 슬라임 sticky 특성, PlatformCarrier 기존 인프라 재사용 |
| Bend 표현 | **X2. 단일 rb + N child collider chain + 단일 visual mesh** | 슬라임 단일 rb 보면 carrier 핸드오프 불필요, bone skinning 회피 |
| State 차원 | **D1. 단일 ODE state + shape function** | SpringyBranch 검증된 spring 식 재사용, 1자유도 안정 |
| 끝 처리 | **T1. cantilever 자유 끝** | "왔다갔다 휘어짐" 연출 적합, 1차 mode 자연 |
| Segment 개수 | **N4. SerializeField (default 12)** | 길이별 디자이너 튜닝, 대나무 마디 ~30cm 매칭 |
| Launch carrier | **L2. transfer ≈ 0.2 + per-height inheritance** | 보너스 미미하지만 timing 보상 미세 존재 |
| Visual mesh | **단일 절차 cylinder + shader bend** | per-segment cylinder 회전 시 마디 단위 끊김 → 매끄러운 곡선 위해 vertex-bend |

## 물리 모델 (SpringyBranch 재사용)

- 상태: `currentAngle` (tip deflection deg), `angularVel` (deg/s), `pivotWorldPos` (Awake 캡처, 고정), `baseRotation` (`rb.rotation` Awake 캡처).
- 각 스프링-댐퍼: `α = (−k·θ − c·ω + pendingRiderTorque) / I_total`.
- 디자이너 노브: `naturalFreqHz`, `dampingRatio` → 내부 `k = ω² · I_total`, `c = 2ζω · I_total`.
- `I_total = I_branch + m_rider · arm²`. arm = `riderWorldPos − basePos`.
- Sign convention: positive `currentAngle` = CCW around +Z (standard Unity Euler), positive `_BendAngleDeg` 일치.

## 휨 분배 — 단일 ODE + 폐형 원호

Shape function 하드코드 `h(t) = t` (linear, cantilever 1차 mode). Local tangent angle at fraction t = `currentAngle * t`. Centerline = 반지름 `L/bendRad` 원호 일부:

```
bendRad = currentAngle * Deg2Rad
centerline.x(t) = (L/bendRad) * (cos(bendRad*t) - 1)   // negative for positive bendRad (CCW)
centerline.y(t) = (L/bendRad) * sin(bendRad*t)
```

`bendRad → 0` 분기: 직선 `(0, t*L)`.

## Collider polyline

각 segment = `CapsuleCollider2D` (또는 Box) child. 모든 segment **단일 root Rigidbody2D 공유** → compound body, 슬라임은 단일 rb 본다. Carrier handoff 없음.

매 FixedUpdate `ApplySegmentTransforms()`:
- 인접 centerline 점 사이 chord
- segment localPosition = chord midpoint
- segment localRotation = `atan2(-chord.x, chord.y)` (signed angle from +Y to chord, CCW)

## Visual mesh — single shader-bent cylinder

절차적 cylinder (`BuildCylinderMesh`): positionOS.y ∈ [0, bambooLength] 그대로 world-equivalent. 인스턴스 GameObject 1개, `localScale = 1`, `localPosition = 0`.

매 FixedUpdate `PushVisualBendProperties()` 가 `MaterialPropertyBlock`으로 `_BendAngleDeg`, `_BambooLength` 푸시.

**Edit mode 보정:** OnAfterMotion이 안 돌아도 `ResolveBendMaterial`이 material 생성 시점에 `_BambooLength` 한 번 push → edit rest pose에서 정상 gradient 보장.

## BambooBend.shader (vertex bend + Quibli-style toon)

`Assets/Art/Shader/BambooBend.shader` (URP Unlit, ForwardLit pass).

### Vertex bend (정확히 C# `ComputeCenterlinePoint` 와 일치)

```hlsl
t = saturate(positionOS.y / _BambooLength)
localAng = radians(_BendAngleDeg) * t
centerline.x = (L/bendRad) * (cos(localAng) - 1)
centerline.y = (L/bendRad) * sin(localAng)
```

Cross-section vertex (px, 0, pz)는 centerline 위에서 `localAng`만큼 CCW 회전 후 합산. Z (out-of-plane) 보존.

법선도 `localAng` CCW 회전 + 노드 bulge 경사 보정.

### 노드 기하 (마디)

실제 대나무 핵심 특징 = **bulge** (마디에서 직경 부풀음) + **sheath scar** (얇은 갈색 띠/홈).

```hlsl
v = positionOS.y / L            // UV-V proxy
c = frac(v * _NodeFrequency)
dist = min(c, 1 - c)            // half-cycle distance to node center

bulge = 1 - smoothstep(0, _BulgeWidth, dist)
scarRaw = 1 - smoothstep(_ScarHalfWidth, _ScarHalfWidth + _ScarSoftness, dist)

R(v) = 1 + _BulgeAmount * bulge - _ScarDepth * scarRaw
```

`pos.xz *= R(v)` 으로 radial 변형. 법선 재계산 (barrel-of-revolution):
`normal = (radialDir, -dR/dy) / sqrt(1 + (dR/dy)²)`.

`scarRaw`는 fragment로 보내 색 mask 로 사용.

### Stylized lighting

- **Height gradient**: `stalkLit = lerp(_BambooColorBase, _BambooColorTip, tHeight)`.
- **Scar color curve**: `scar = pow(scarRaw, _ScarColorCurve)` → linear / steep / gentle falloff 컨트롤 (지오메트리 pinch는 raw 그대로).
- **Color combine**: `litColor = lerp(stalkLit, _ScarColor, scar)`.
- **Toon shading**: `shade = smoothstep(_ToonThreshold - _ToonSoftness, _ToonThreshold + _ToonSoftness, dot(N, L))`. `shadowColor = litColor * _ShadowDarken`. `final = lerp(shadowColor, litColor, shade)`.
- **Rim**: `pow(1 - max(0, dot(N, V)), _RimPower) * _Rim * litColor` 추가.

## 라이더 영향 — Δp 펌핑 (vertical 변형)

SpringyBranch 와 동일 cross product `(r.x * impulseP.y - r.y * impulseP.x) / I_total * pumpGain`. 차이점:
- 수직 bamboo + 옆 hit → r.y dominant, impulseP.x dominant → 결과 정확.
- Re-entry guard: `slamMinHorizontalSpeed` (수직 bamboo는 lateral 슬램만 의미), `slamReattachLockout`.
- Gravity torque: r.x * (-g) * mass * `riderTorqueScale`. 수직 rest pose에선 0 (r.x=0), 휨 발생 후 자기-증폭/감소.

## 발사 시 carrier inheritance — per-attach-height

`VelocityTransferScale` getter가 launch 순간 슬라임 위치 t = arm.magnitude / bambooLength 계산:

```
requested = carrierMinContribution + carrierDistanceScale * t
carrierGives = max(t, 1e-3)     // avoid div-by-zero
VelocityTransferScale = (requested / carrierGives) * velocityTransferScale
```

PlatformCarrier가 `ω × arm`으로 자동 계산하는 `tipSpeed * t`에 위 비율 곱해서:
```
finalCarrierVel = tipSpeed * (carrierMin + carrierDistanceScale * t) * velocityTransferScale
```
base attach(t≈0)도 minimum 비율 inherit. tip attach 가까울수록 큰 비율.

| 파라미터 | 기본 | 의미 |
|---|---|---|
| `velocityTransferScale` | 0.2 | 전체 carrier 배율 |
| `carrierMinContribution` | 0.3 | 모든 위치 최소 비율 |
| `carrierDistanceScale` | 1.0 | t-비례 추가 비율 |

## 오브젝트 계층

```
BambooPlatform   (Rigidbody2D kinematic + BambooPlatform)
  ├─ Segment_00 ~ Segment_(N-1)   (CapsuleCollider2D 만, 매 FU 위치/회전 갱신)
  └─ Visual                       (MeshFilter + MeshRenderer + 절차 cylinder + BambooBend material)
```

## Inspector 필드 (요약)

### Mode
- `backgroundOnly` (default false) — true면 collider segment 생성 스킵, Visual mesh만 빌드 (순수 배경 장식용). 토글 후 `Rebuild Segments` 호출 필요. rider physics·carrier·OverrideSurfaceMaterial 모두 무효 (충돌 없으므로 attach 자체 불가). RB는 PlatformMotor 요구상 유지되나 currentAngle=0 고정으로 직선 cylinder.

### Geometry / Spring
- `bambooLength`, `segmentCount` (2~32), `segmentRadius`, `colliderShape` (Capsule/Box)
- `naturalFreqHz` (0.3~3 Hz), `dampingRatio` (0~1), `maxDeflectionDeg`, `clampRestitution` (0 = 캡 absorb, no ringing)
- `riderPumpGain` (default 0.4), `chargePumpScale`, `riderTorqueScale`
- `passiveDecayRate` (default 1.2), `riderDecayRate`
- `maxTipSpeed` (m/s 하드 캡)

### Visual — Color
- `stalkColorBase` (base hue), `stalkColorTip` (tip hue) — gradient
- `shadowDarken` (0~1, default 0.6) — shadow side multiplier
- `scarColor` — 마디 scar 색

### Visual — Node geometry
- `nodeFrequency` (길이당 마디 수)
- `bulgeAmount`, `bulgeWidth` — bulge 크기/폭
- `scarHalfWidth` (core), `scarSoftness` (falloff zone), `scarColorCurve` (그라데이션 곡선 exponent), `scarDepth` (안쪽 홈)

### Visual — Lighting
- `toonThreshold`, `toonSoftness` — 2-tone 경계
- `rimStrength`, `rimPower` — silhouette rim
- `lightDirWS` — shader가 사용하는 광원 방향

### Visual — Mesh
- `visualRadialSegments` (cylinder 둘레 verts)
- `visualHeightSegments` (cylinder 길이 verts — bend smoothness)

### Carrier
- `velocityTransferScale`, `carrierMinContribution`, `carrierDistanceScale`

### Re-entry Guards
- `slamMinHorizontalSpeed`, `slamReattachLockout`

## Editor convenience

- `[ContextMenu("Rebuild Segments")]` — bambooLength/segmentCount/colliderShape/visual tessellation 변경 시 수동 재생성 (mesh + collider 재생성 필요).
- `[ContextMenu("Refresh Visual Material")]` — color/scar/bulge/lighting 값만 변경 시 material 즉시 푸시.
- `OnValidate` — Inspector 변경 시 `EditorApplication.delayCall`로 `RefreshVisualMaterialFromFields` 자동 호출 (대부분의 visual 필드 live 반영).
- `BambooPlatformTestSetup` 메뉴: `SlimeJumping → Platform → Setup BambooPlatform Test` — 5개 대나무 + 바닥 + 벽 + LaunchPad 자동 생성. 기존 `BambooPlatformTest` GameObject 있으면 자동 삭제.

## PlatformMotor virtual 추가

`PlatformMotor`에 신규 `OnAfterMotion(float dt)` virtual hook 추가 — 자식 transform 업데이트 + visual property push 용. 기존 platform들은 default no-op 영향 없음.

## 알려진 한계

- 단일 라이더 가정.
- `pivotWorldPos` Awake 캡처 — 런타임 이동 불가.
- bambooLength/segmentCount 런타임 변경 시 mesh/collider 재생성 안 됨 (Rebuild Segments 필요).
- 수직 정렬 가정 — 비스듬한 base rotation 미지원 (필요 시 baseRotation 응용).
- Shadow side render에서 light 받는 방향에 따라 stalk 명암 split 위치가 바뀜 — `lightDirWS` 인스펙터 조정 필요.

## Tip-Grab 모드 (그랩 / 매달림) — 설계 (미구현)

기존 측면 attach (벽 용도) 위에 **tip-grab 서브모드** 추가. 슬라임이 bamboo 위에 올라탔을 때 자동 진입, A/D 입력이 crawl 대신 bamboo tilt 토크로 라우팅. 풀면 스프링 복원 + 진동, 그 와중 charge-and-launch 하면 carrier inheritance가 lateral **Reaction Boost** 제공.

용어/모델 = [/CONTEXT.md](../../../CONTEXT.md), 구조 선택 근거 = [/docs/adr/0001-bamboo-tip-grab-as-rope-pattern-flag.md](../../adr/0001-bamboo-tip-grab-as-rope-pattern-flag.md).

### 설계 결정 흐름 (grill-with-docs 산출)

| # | 질문 | 선택 | 이유 |
|---|------|------|------|
| Q1 | 그랩 기하 | **A. tip 끝점 잡고 매달림 (깃대-깃발)** | "매달림" 직역, swing-jump 시나리오 자연 |
| Q2 | 그랩 진입 trigger | **T2-변형. on-top 자동 감지** (`dot(surfaceNormal, gravityUp) > grabEnterDot`, bamboo 소속 collider 일 때) | 별도 키 불필요, 측면→tip 슬라이드도 자연 transition |
| Q3 | A/D 입력 매핑 | **M1. 토크 추가** (`pendingRiderTorque += -input.x * tiltTorque`) | 그네 펌핑 자연, ODE 수정 0, swing-back boost 식 자명 |
| Q4 | Tilt 중 슬라임 위치 | **P1. tip 고정 anchor** (`rb.position = tipWorldPos` 매 FU 강제) | 매달림 그림 일치, 어떤 각도에서도 그랩 유지, 코드 단순 |
| Q5 | Launch 모델 | **J1. 기존 Charging→Launch 재사용** + `grabModeCarrierScale` knob 추가 | Carrier inheritance가 그대로 boost, 신규 launch 식 0 |
| Q6 | State 모델 | **S1. Rope 패턴** (Attached 유지, `currentBambooGrab` 핸들, partial 파일) | 신규 state 도입 시 transition matrix 재검토 비용 회피 |
| Q7 | Tilt knob set | **K2. 2-knob** (`tiltTorque` + `chargingTiltScale`) | 기존 `chargePumpScale`와 짝 |
| Q8 | 위험 처리 | **R2-a / R3-a** (snap skip + intentional toggle bypass) | 1줄 가드, 본의 아닌 lockout 방지 |

### Anchor & 입력 라우팅 (Rope 패턴)

- `SlimeController.BambooGrab.cs` (신규 partial).
- 필드: `BambooPlatform currentBambooGrab`.
- **진입 감지** — `UpdateSurfaceContact` 직후 `surfaceCollider.GetComponentInParent<BambooPlatform>()` + dot 조건 검사. 충족 → `currentBambooGrab` set. 미충족 → unset.
- **Pre-probe lock** — `SlimeController.cs:533-536` rope 블록 옆에 `LockBambooGrabPosition()` 추가. 매 FU `rb.position = currentBambooGrab.GetTipWorldPos()` (BambooPlatform가 `ApplySegmentTransforms` 직후 캐시한 tip world pos getter 제공).
- **입력 게이트** — `SlimeController.cs:608` inputX 가공부에서 `currentBambooGrab != null` 이면 `inputX = 0` (crawl 차단) + `currentBambooGrab.ApplyTiltTorque(rawInputX)` 호출. `inputReader.AimInput` 은 별도 필드라 charging aim 영향 0.
- **Snap skip** — surfaceProbe.SnapToSurface 호출부 (`Attached.cs`, `Charging.cs`, `OutputVelocity` Multi-probe 분기) 에 `currentBambooGrab != null` 가드. Tip-lock이 진실의 원천.
- **Launch 해제** — `Launch()` 끝부분에서 `currentBambooGrab = null`. Carrier inheritance는 같은 프레임 `PlatformCarrier.GetEffectiveVelocity()` 호출이 BambooPlatform의 `AngularVelocity` × arm 으로 계산 (arm = tip - pivot ≈ (0, L), 최대치 자동).
- **Grab 손실 시 토글 카운트 우회** — grab 해제로 인한 Attached→Airborne 전이는 `ChangeState(Airborne, intentional: true)` (또는 동등 flag) 로 호출해서 `RegisterAttachToggle()` skip. bamboo 진동 큰 디자인에서 lockout 트립 방지.

### BambooPlatform 측 신규 API

```csharp
// 매 ApplySegmentTransforms 직후 갱신되는 tip world pos 캐시.
public Vector2 GetTipWorldPos() => cachedTipWorldPos;

// 외부 (SlimeController.BambooGrab) 가 부르는 입력 토크 라우터.
// inputX ∈ [-1, 1]. 부호 변환은 BendAngle 부호 컨벤션 (positive = CCW, tip→-X) 에 맞춰
// 토크 부호 결정: A(input.x = -1) 누르면 tip이 +X 로 이동 (BendAngle 감소, CW) →
// 토크는 negative. 즉 `pendingRiderTorque += -inputX * tiltTorque * scale`.
public void ApplyTiltTorque(float inputX, bool isCharging)
{
    float scale = isCharging ? chargingTiltScale : 1f;
    pendingRiderTorque += -inputX * tiltTorque * scale;
}
```

### 신규 Inspector 필드

| 필드 | 기본 | 의미 |
|---|---|---|
| `grabEnterDot` | 0.9 | `dot(surfaceNormal, gravityUp)` 임계. 이상이면 tip-grab 진입 |
| `tiltTorque` | (튜닝) | A/D 입력당 추가 토크 크기 (N·m 동등 단위, ODE I_total과 같은 단위계) |
| `chargingTiltScale` | 1.0 | Charging 상태일 때 tiltTorque 곱셈. 기존 `chargePumpScale`과 의미 짝 |
| `grabModeCarrierScale` | 1.0 | Tip-grab 상태에서 Launch 할 때 `VelocityTransferScale` 추가 곱셈. 0 = 그랩 boost 없음 (디자인 디버깅용), >1 = grab 모드 전용 강화 boost |

기존 carrier 식과의 합성:
```
finalCarrierVel = ω × arm * VelocityTransferScale * (currentBambooGrab == this ? grabModeCarrierScale : 1f)
```
구현 위치 = `BambooPlatform.VelocityTransferScale` getter (기존 식에 `grabModeCarrierScale` 곱 한 줄).

### 의도 동작 시나리오

1. 슬라임 측면에서 bamboo로 점프, 측면 attach → 기존 그대로 (벽-비슷)
2. 측면 slide 또는 위에서 떨어져 tip cap에 도달 → `grabEnterDot` 충족 → `currentBambooGrab` set
3. A/D 누름 → bamboo가 좌/우로 휨. 슬라임은 tip 고정이라 같이 호 그림
4. 입력 떼면 스프링 복원 + 댐핑 진동. 그네 펌핑 가능 (반대 방향 입력으로 진폭 증가)
5. Charge 입력 → Charging 진입. Grab anchor 유지. A/D는 계속 tilt, aim은 별도 입력으로 회전
6. 스윙이 BendAngle ≈ 0 (max |ω|) 지나는 순간 release → Launch. `aimDir * launchPower + ω × arm * scales` 합성 발사 → 큰 lateral 부스트
7. Airborne 진입 시 `currentBambooGrab = null`. 다음 attach까지 일반

### 알려진 한계 / 미해결

- 단일 라이더 가정 (기존 BambooPlatform 한계 그대로)
- bamboo가 비-수직(`baseRotation ≠ 0`) 일 때 `gravityUp` dot 임계가 의도와 어긋날 수 있음 — bamboo local up 기준으로 비교하도록 일반화 필요 (현재 수직 정렬 가정으로 충분)
- "tilt 가 maxDeflectionDeg 캡에 닿은 뒤 추가 입력은 무의미" — 디자인상 정상 (캡에서 추가 펌핑 차단 = 그네 펌핑 기교 강제)
- Multi-bamboo 클러스터 — 인접 bamboo로 점프하면 자연스레 그랩 핸들 갱신 (별도 처리 불필요로 평가, 첫 구현 후 검증)

## Pre-existing 프로젝트 이슈 해결 (작업 중 발견)

`Assets/Plugins/geometry3Sharp/`에 hand-written `.csproj` 2개 포함됨. C# Dev Kit (VSCode 확장) 가 자동으로 `dotnet build` 돌려 `obj/Debug/netstandard2.0/AssemblyInfo.cs` 생성 → Unity asmdef 컴파일의 auto-AssemblyInfo와 중복 충돌(CS0579) → Assembly-CSharp 재컴파일 차단. 해결: `.csproj` 2개 + `obj/` + `bin/` 삭제 (Unity는 `geometry3Sharp.asmdef`로 빌드, `.csproj` 불필요).
