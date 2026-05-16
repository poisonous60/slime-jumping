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

## Pre-existing 프로젝트 이슈 해결 (작업 중 발견)

`Assets/Plugins/geometry3Sharp/`에 hand-written `.csproj` 2개 포함됨. C# Dev Kit (VSCode 확장) 가 자동으로 `dotnet build` 돌려 `obj/Debug/netstandard2.0/AssemblyInfo.cs` 생성 → Unity asmdef 컴파일의 auto-AssemblyInfo와 중복 충돌(CS0579) → Assembly-CSharp 재컴파일 차단. 해결: `.csproj` 2개 + `obj/` + `bin/` 삭제 (Unity는 `geometry3Sharp.asmdef`로 빌드, `.csproj` 불필요).
