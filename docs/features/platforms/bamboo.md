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

각 segment = `CapsuleCollider2D` (또는 Box) child. 모든 segment **단일 root Rigidbody2D 공유** → compound body, 슬라임은 단일 rb 본다. Neighbor segment에는 `colliderSegmentOverlap`만큼 작은 길이 overlap을 둔다. 물리적으로는 한 줄기라도 capsule/box 끝 경계에서 contact normal/closest point가 튈 수 있어서, 내려올 때의 카메라 떨림을 줄이기 위한 seam 완충이다.

SlimeController 쪽 carrier/surface modifier 갱신도 raw collider가 아니라 `PlatformMotor` host 기준으로 본다. 즉 `Segment_03 → Segment_04`처럼 같은 BambooPlatform 아래 child collider 사이를 이동하는 것은 플랫폼 전환으로 취급하지 않고, 실제 다른 플랫폼으로 옮겨갈 때만 carrier attach / surface modifier refresh를 한다.

매 FixedUpdate `ApplySegmentTransforms()`:
- 인접 centerline 점 사이 chord
- segment localPosition = chord midpoint
- segment localRotation = `atan2(-chord.x, chord.y)` (signed angle from +Y to chord, CCW)

## Visual mesh — single shader-bent cylinder

절차적 cylinder (`BuildCylinderMesh`): positionOS.y ∈ [0, bambooLength] 그대로 world-equivalent. 인스턴스 GameObject 1개, `localScale = 1`, `localPosition = 0`. `visualHeightSegments`는 최소값으로 쓰고, 실제 height segment 수는 `segmentCount`의 배수이면서 segment당 최소 4 row가 되도록 올림 처리한다. vertex-stage node bulge가 모든 collider joint(`t = k/segmentCount`)에 샘플을 갖게 하기 위함.

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

실제 대나무 핵심 특징 = **bulge** (마디에서 직경 부풀음) + **sheath scar** (얇은 갈색 띠/홈). shader의 `_NodeFrequency`는 항상 `segmentCount`로 구동된다 (`VisibleNodeFrequency()`). scar는 `t = k/_NodeFrequency`, collider 경계는 `t = k/segmentCount` 이므로 눈에 보이는 마디가 `Segment_00..N` collider 분할과 정확히 같은 위치에 온다. 마디 간격은 `bambooLength`를 따라 자동으로 벌어진다 (마디 world y = k/segmentCount · L). 별도 `nodeFrequency` 필드 없음 — 마디 수를 바꾸려면 `segmentCount`를 바꾼다.

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

색 scar는 fragment에서 보간된 `tHeight`로 다시 계산한다. vertex-stage에서 scar mask를 샘플하면 `visualHeightSegments`와 `segmentCount`가 서로 나누어떨어지지 않을 때 마디가 aliasing되어 spacing이 랜덤하게 보일 수 있기 때문이다.

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
- 마디 수 = `segmentCount` (자동, 필드 없음) — scar 링이 collider 분할과 일치
- `bulgeAmount`, `bulgeWidth` — bulge 크기/폭
- `scarHalfWidth` (core), `scarSoftness` (falloff zone), `scarColorCurve` (그라데이션 곡선 exponent), `scarDepth` (안쪽 홈)

### Visual — Lighting
- `toonThreshold`, `toonSoftness` — 2-tone 경계
- `rimStrength`, `rimPower` — silhouette rim
- `lightDirWS` — shader가 사용하는 광원 방향

### Visual — Mesh
- `visualRadialSegments` (cylinder 둘레 verts)
- `visualHeightSegments` (cylinder 길이 verts 최소값 — 실제 빌드 시 `segmentCount` 배수 + segment당 최소 4 row로 올림, node bulge joint 샘플 보장)

### Carrier
- `velocityTransferScale`, `carrierMinContribution`, `carrierDistanceScale`

### Attached carry 보정

Bamboo는 root Rigidbody2D가 회전하지 않고 child segment collider만 `currentAngle/angularVel`로 휘므로, 일반 `PlatformCarrier.ApplyPositionCarry`의 `DeltaRotation` 기반 carry는 0이 된다. 빠른 bamboo motion에서는 이 때문에 collider만 움직이고 슬라임이 공중에 남는 프레임이 생길 수 있다. 해결: `PlatformMotor.GetRiderCarryDelta(riderWorldPos, dt)` virtual을 추가하고, Bamboo는 `angularVel × arm * dt`를 반환한다. Launch velocity와 같은 물리 의미의 carry를 attached teleport 경로에도 적용해, 빠르게 휘는 동안에도 슬라임이 표면을 따라간다.

### Analytic snap

`SurfaceProbe.SnapToSurface`의 기본 Phase B는 active collider의 `ClosestPoint`로 center를 다시 붙인다. Bamboo처럼 여러 child capsule/box가 겹친 polyline에서는 같은 줄기 안에서도 `SelectedIndex`와 `ClosestPoint`가 segment cap/overlap 사이를 갈아타며 `dist`가 0.019 → 0.001 같은 식으로 튈 수 있다. 슬라임이 bamboo에 붙어 있을 때는 `SlimeController.SnapToSurface`가 `BambooPlatform.TryGetAnalyticSurfaceAnchor`를 우선 사용한다.

이 anchor는 collider child가 아니라 shader와 같은 `ComputeCenterlinePoint(t)` 연속 곡선에서 가장 가까운 centerline `t`를 찾고, `segmentRadius + slimeRadius`만큼 outward offset한 위치다. 즉 collision detection은 기존 child collider를 유지하지만, attached/charging frame의 최종 surface snap은 연속 bamboo 곡선을 source of truth로 삼아 segment seam jitter를 줄인다.

Multi probe의 `OutputVelocity` teleport branch도 반드시 `SlimeController.SnapToSurface()` wrapper를 호출해야 한다. `surfaceProbe.SnapToSurface(...)`를 직접 호출하면 Bamboo analytic snap을 우회한다. Bamboo analytic snap은 현재 곡선 pose에 이미 붙이므로, 같은 프레임의 `PlatformCarrier.ApplyPositionCarry`는 bamboo에 한해 생략한다. 그렇지 않으면 analytic snap + `angularVel × arm` carry가 이중 적용되어 빠르게 휘는 bamboo에서 오히려 잔류 위치 보정이 커진다.

### Re-entry Guards
- `slamMinHorizontalSpeed`, `slamReattachLockout`

## Editor convenience

- `[ContextMenu("Rebuild Segments")]` — bambooLength/segmentCount/colliderShape/colliderSegmentOverlap/visual tessellation 변경 시 수동 재생성 (mesh + collider 재생성 필요).
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

## Debugging

`BambooSurfaceJitterDebugger`는 슬라임 루트에 붙이는 대나무 하강 jitter 전용 probe다. 기존 `CurveSlideJitterProbe`/`OnScreenJitterProbe`가 일반 물리 위치와 카메라 상대 움직임을 보는데 비해, 이 컴포넌트는 bamboo segment seam과 deforming-platform carry를 직접 본다.

자동 dump 조건:
- `Segment_XX` raw collider switch / host switch
- `SurfaceProbe.SelectedIndex` 변경
- surface normal 1-frame angle jump
- slime circle ↔ active surface `Collider2D.Distance` 변화
- `observed dPos - previous ctrlVel*dt - PlatformMotor.GetRiderCarryDelta(...)` residual spike
- Phase A depenetration push spike

기본값은 `autoDumpOnlyOnBamboo = true`라서 Floor 착지, 스폰/텔레포트, 일반 발사처럼 대나무 밖에서 나는 큰 위치 변화는 자동 dump하지 않는다. 수동 `F6` dump는 이 필터를 무시하고 현재 ring buffer를 그대로 출력한다. dump 상단의 `Trigger sample` 한 줄이 실제 자동 dump를 발생시킨 프레임이다.

해석:
- `segment switch + normal/distance jump` — collider seam/contact selection이 실제로 보이는 위치 튐을 만들 가능성이 높다.
- `residual spike` — `SnapToSurface`, depenetration, Unity solver 보정 중 하나가 slime rb.position을 밀고 있다.
- `bamboo angular velocity`가 큰데 `carry residual`도 크다 — bamboo child-collider 변형에 대한 carry 모델이 아직 실제 표면 이동과 다르다.
- `surface`가 같은 `Segment_XX`인데 `probe selected`와 `dist`만 반복적으로 바뀐다 — child collider의 `ClosestPoint` 기준이 흔들리는 케이스이며, analytic snap으로 우회한다.
- 이 probe는 물리 FixedUpdate 기준이다. 물리 데이터가 깨끗한데 화면만 떨리면 `OnScreenJitterProbe`로 camera/update timing을 이어서 본다.

## Side-Rebound V2 모드

`BambooSettings.enableSideReboundV2 = true` 인 bamboo는 기존 절차 cylinder visual, N-segment collider polyline, `BambooBend.shader`를 그대로 사용한다. 슬라임이 측면으로 부딪히면 bamboo가 더 크게 휜다. 자동 발사는 없고, Space를 눌러 기존 Charging 상태로 들어간 뒤 release할 때 플레이어 aim 방향 그대로 bamboo V2 보너스를 더한다.

> **`enableTipGrab` / `enableSideReboundV2` 는 독립 bool** (예전 `interactionMode` enum 의 상호배타 제거). 둘 다 켜면 한 대나무가 옆구리 충돌엔 V2 반동, 꼭대기 착지엔 tip-grab 으로 동시에 반응한다. 트리거 면이 다르고(side normal vs `dot(normal, gravityUp) ≥ grabEnterDot`) 스프링 계수 선택이 `grabActive > v2Active > 기본` 우선순위라 충돌 없음. `BambooV2Settings.asset` 기본값 = 둘 다 ON.

구현 위치:
- `BambooPlatform` — V2 여부, side-contact 판정, V2 spring/max deflection, bend pump gain, auto/charged launch 값을 제공.
- `SlimeController.BambooV2.cs` — landing 순간 side rebound/bonus handle을 arm, charged launch 보너스 적용.
- `SlimeController.BambooGrab.cs` — `UsesTipGrab`(=`enableTipGrab`) 이 false면 top grab을 만들지 않음. V2 여부와 독립.
- `BambooV2Response` — Additive/SpeedCurve 공통 평가 helper.

튜닝 방식은 두 가지다.

| 필드 | Additive 모드 | SpeedCurve 모드 |
|---|---|---|
| `v2BendPumpGain` / `v2BendPumpGainByImpactSpeed` | 측면 충돌 pump gain 고정 배율 | 측면 충돌 속도별 bend pump gain |
| `v2BendImpactSpeedScale` / `v2BendTipTravelByImpactSpeed` | 첫 충돌: 접선 충돌 속도 → tip 각속도 배율 | 첫 충돌: **직각 충돌 속도(m/s) → 목표 끝 이동거리(m)** 그래프, 길이 정규화(θ=2·travel/L) |
| `v2ChargedLaunchBonus` / `v2ChargedLaunchBonusByChargePower` | Charging release 발사 power에 더할 고정 보너스 | charge launch power별 추가 보너스 |

**첫 충돌 휨 (impact bend) — 모드별 2가지 (`ApplyV2ImpactKick`).** 슬라임이 처음 측면에 닿는 프레임에서 대나무 tip angular velocity를 설정한다. 빠르게 부딪힐수록 더 크게 흔들리는 점은 두 모드 공통이고, 첫 충돌에 한해 `v2BendPumpGain`/`v2BendPumpGainByImpactSpeed` impulse 펌프를 대체한다. 접촉 높이 보정(`v2BendMinHeightScale`, `v2BendHeightPower`)은 두 모드 모두 그대로 곱해진다. `v2BendPumpGain`/curve는 첫 충돌 이후의 sustained/charge 프레임에만 영향 (대개 ~0).

- **Additive 모드 — `v2BendImpactSpeedScale` (단일 노브).** tip angular velocity를 슬라임의 **접선 충돌 속도**에서 바로 비례 계산. 발사 반동(`v2LaunchReleaseVelocityScale`)과 **같은 투영식·같은 단위**라 충돌과 반동이 일관되게 읽히고 각각 숫자 하나로 튠한다. `1` = 접촉 높이의 대나무 표면이 슬라임 접선 충돌 속도를 그대로 따라가려 함, 클수록 더 휨. `0` = impact kick 끄고 impulse 펌프로 폴백.
- **SpeedCurve 모드 — `v2BendTipTravelByImpactSpeed` (그래프).** X = **직각 충돌 속도** (m/s, 대나무 축에 수직으로 들어오는 속도 성분 = `BambooV2Response.SignedPerpendicularSpeed`), Y = **목표 끝 이동거리** (m, 대나무 *끝*이 첫 스윙에 이동하는 거리). "이 속도로 부딪히면 끝이 이만큼 움직인다"를 디자이너가 그래프로 직접 그린다. **길이 정규화**: 끝 이동거리 → 휨 각도 변환을 `θ = 2 · tipTravel / bambooLength`(`PeakBendDegForTipTravel`)로 하므로, 한 그래프가 모든 길이에 일관되게 적용된다 — 긴 대나무는 같은 끝 이동을 *작은 각도*로, 짧은 대나무는 *큰 각도*로 만든다. 그 peak 각도에 V2 스프링이 실제로 도달하도록 underdamped 2차계 역산으로 초기 각속도를 부여한다: peak = `(ω₀/ω_d)·exp(−ζ/√(1−ζ²)·atan2(√(1−ζ²), ζ))` → `ω₀ = peak·ω_d/decay` (`AngularVelocityForPeakBendDeg`, freq=`v2SpringFreqHz` / ζ=`v2DampingRatio`). 끝 이동거리(m)는 소각 역산 + `v2DampingRatio` decay 때문에 근사값이라 눈으로 튠한다. 빈 곡선이면 impact kick 끄고 impulse 펌프로 폴백.

**계수 2의 근거 (2026-07-23 수정).** 대나무는 힌지에 달린 강체봉이 아니라 **원호 캔틸레버**다. `ComputeCenterlinePoint`/`BambooBend.shader`가 끝을 `x = (L/θ)(cos θ − 1)`에 두므로 끝의 가로 이동거리는 `L·(1−cos θ)/θ ≈ L·θ/2` — 강체봉 `L·θ`의 **절반**이다. 이전 변환식 `θ = travel/L`은 강체봉 가정이라 그래프에 그린 미터의 절반만 실제로 움직였다 (모든 속도·모든 길이에서 상시 2배 손실). 역산해 `θ ≈ 2·travel/L`로 고쳤고, θ→0에서 정확하며 90° 부근에서 약 15% 과대평가한다.

**끝 이동거리 천장.** 호는 포화한다 — 끝의 가로 이동은 `θ=130°` 부근에서 `~0.72·L`로 최대가 되고 그 이상 못 간다. 따라서 그래프 Y를 `0.36·L`(= 계수 2 적용 후 `0.72·L`) 넘게 그리면 죽은 구간이다. X도 실제 측면 충돌 속도 범위(대략 0–15 m/s)에 맞춰야 하며, 그보다 넓게 잡으면 그래프의 가장 완만한 앞 1/5만 쓰게 된다.

Charging release는 플레이어가 aim한 방향을 그대로 사용하고, `base charge power + v2ChargedLaunchBonus`만 적용한다. `v2ChargedLaunchBonus`는 “차징 점프 방향을 따라가는 추가 속도”이고, `v2CarrierScale`은 “슬라임이 bamboo에서 받는 스윙 속도 상속 배율”이다. 발사 직후 bamboo 자체도 `v2LaunchReleaseVelocityScale`만큼 반작용 kick을 받는다. `v2LaunchReleaseUseHeightScale`이 켜져 있으면 이 kick에 별도 접촉 높이 보정(`v2LaunchReleaseMinHeightScale`, `v2LaunchReleaseHeightPower`)을 곱한다. 따라서 아래쪽에서 점프하면 같은 launch velocity라도 덜 흔들리고, 위쪽/끝에 가까운 접촉일수록 접선 속도에 가까운 큰 반작용이 난다. 즉 차징을 더 세게 만들고 싶으면 `v2ChargedLaunchBonus`/`v2ChargedLaunchBonusByChargePower`, 슬라임이 스윙에서 받는 보상을 키우고 싶으면 `v2CarrierScale`, 슬라임이 떨어져나간 뒤 bamboo가 더 빠르게 튕기게 만들고 싶으면 `v2LaunchReleaseVelocityScale`을 올린다.

시간/형상 노브:
- `v2BendImpactSpeedScale` — (**Additive 모드**) 첫 측면 충돌 시 슬라임 접선 충돌 속도 → tip angular velocity 변환 배율. 빠른 충돌일수록 자동으로 더 크게 흔들리게 하는 단일 노브 (위 "첫 충돌 휨" 참조). 0이면 기존 impulse 펌프로 폴백.
- `v2BendTipTravelByImpactSpeed` — (**SpeedCurve 모드**) 직각 충돌 속도(m/s) → 목표 끝 이동거리(m) 그래프. 길이 정규화(`θ = 2 · tipTravel / bambooLength`)라 한 그래프가 모든 길이에 일관 — 긴 대나무는 작은 각도, 짧은 대나무는 큰 각도. `v2BendImpactSpeedScale`을 대체. 빈 곡선이면 impulse 펌프로 폴백 (위 "첫 충돌 휨" 참조). Y 상한 `0.36·L`, X는 실제 충돌 속도 범위(~0–15 m/s)로.
- `v2SpringFreqHz` — 낮을수록 더 오래 휘고 늦게 돌아온다.
- `v2DampingRatio` — 낮을수록 반동 에너지가 덜 죽는다.
- `v2MaxDeflectionDeg` — 측면 충돌 시 허용되는 최대 휨.
- `v2ReboundMinContactHeight01` — V2 rebound를 arm할 최소 접촉 높이. 0=바닥, 1=끝. 이보다 낮게 닿으면 큰 반동/차징 보너스를 만들지 않는다.
- `v2BendMinHeightScale` — base 접촉의 bend pump 최소 배율. 0이면 밑동 접촉은 거의 휨을 만들지 않는다.
- `v2BendHeightPower` — 접촉 높이에 따른 bend pump 곡률. 높을수록 아래쪽 접촉 영향이 빠르게 작아진다.
- `v2CarrierScale` — `ω × arm`으로 상속되는 bamboo 속도 추가 배율.
- `v2LaunchReleaseVelocityScale` — 발사 순간 slime launch velocity의 접선 성분을 bamboo angular velocity로 바꾸는 배율. 1이면 접촉 높이의 bamboo 표면 속도가 slime 발사 속도의 접선 성분과 거의 같아진다.
- `v2LaunchReleaseUseHeightScale` — release kick에 접촉 높이 보정을 적용할지. 끄면 위/아래 접촉 높이에 따른 release kick 차이를 제거한다.
- `v2LaunchReleaseMinHeightScale` — base 접촉의 release kick 최소 배율. 0이면 아래쪽 launch는 거의 대나무를 흔들지 않는다.
- `v2LaunchReleaseHeightPower` — release kick 높이 보정 곡률. 높을수록 아래쪽 launch 영향이 빠르게 작아진다.
- `v2LaunchReleaseMaxTipSpeed` — release kick 후 bamboo tip speed 상한.
- `v2SideNormalMaxUpDot` — side hit으로 인정할 normal 범위. `|dot(surfaceNormal, gravityUp)|`가 이 값 이하여야 V2 rebound가 arm된다.

제공 asset:
- `Assets/Settings/BambooSettings.asset` — 기존 TipGrab 모드 유지.
- `Assets/Settings/BambooV2Settings.asset` — SideReboundV2 기본 튜닝.
- 메뉴 `SlimeJumping → Platform → Setup BambooPlatform V2 Test` — 기존 visual 생성 로직으로 V2 bamboo grove 생성.

## Tip-Grab 모드 (그랩 / 매달림)

기존 측면 attach (벽 용도) 위에 **tip-grab 서브모드** 추가. 슬라임이 bamboo 위에 올라탔을 때 자동 진입, A/D 입력이 crawl 대신 bamboo tilt 토크로 라우팅. 풀면 스프링 복원 + 진동, 그 와중 charge-and-launch 하면 carrier inheritance가 lateral **Reaction Boost** 제공.

### Pump 모델 (2-channel: Lean + Anti-damping Boost)

A/D 입력은 두 채널에 동시에 들어감. 자세한 식 + 부호 컨벤션은 [BambooPlatform.ApplyTiltTorque](../../../SlimeJumping_v2/Assets/Scripts/Platforms/BambooPlatform.cs) 주석 참조.

1. **Lean** — 상시 적용되는 정적 평형 이동. 잡고만 있어도 `grabHoldBendDeg` 만큼 살짝 휘게 보임.
2. **Anti-damping Boost (C2)** — 입력 방향이 tip 운동 방향과 같을 때만 추가 토크 `|input|·ω·grabPumpBoostGain·I_total`. 그네 펌핑 패턴(앞으로 갈 때 앞으로 밀기) — 진폭이 매 swing 마다 누적 증가하며, `grabPumpBoostGain` 이 damping 계수 `2ζω`를 초과하면 effective net damping 이 음수가 되어 진폭이 지수 성장하다 soft cap 에서 saturate.

Charging 중에는 두 채널 모두 default 0 (`chargingTiltScale = 0`, `grabPumpChargingScale = 0`) — release timing(차징 끝 발사)이 휨 입력과 같은 키를 공유하므로 의도된 mute. 디자이너가 필요시 0 초과로 켤 수 있음.

### Soft Cap (grab 모드 한정)

기존 hard clamp(`ω=0 + clampRestitution`)는 side-attach 만 사용. Grab 모드에서는 ODE 안에 quartic stiffening 토크를 추가:

```
normAbs = |currentAngle| / grabMaxDeflectionDeg
softCapTorque = -sign(currentAngle) * grabCapPenaltyGain * normAbs^4 * stiffness * grabMaxDeflectionDeg(rad)
```

- normAbs < ~0.6: 거의 0, 선형 스프링이 지배.
- normAbs ≈ 0.9: penalty ≈ 0.66 × stiffness × maxAngle, 강한 추가 복원.
- normAbs → 1: penalty 폭증, 자연스럽게 멈춤. 속도 즉시 wipe 없음 → "장대가 끝까지 휘고 다시 튀어오는" 느낌.

용어/모델 = [/CONTEXT.md](../../../CONTEXT.md), 구조 선택 근거 = [/docs/adr/0001-bamboo-tip-grab-as-rope-pattern-flag.md](../../adr/0001-bamboo-tip-grab-as-rope-pattern-flag.md), pump 모델 전환 근거 = [/docs/adr/0002-bamboo-grab-anti-damping-pump.md](../../adr/0002-bamboo-grab-anti-damping-pump.md).

### 설계 결정 흐름 (grill-with-docs 산출)

| # | 질문 | 선택 | 이유 |
|---|------|------|------|
| Q1 | 그랩 기하 | **A. tip 끝점 잡고 매달림 (깃대-깃발)** | "매달림" 직역, swing-jump 시나리오 자연 |
| Q2 | 그랩 진입 trigger | **T2-변형. on-top 자동 감지** (`dot(surfaceNormal, gravityUp) > grabEnterDot`, bamboo 소속 collider 일 때) | 별도 키 불필요, 측면→tip 슬라이드도 자연 transition |
| Q3 | A/D 입력 매핑 | **M1. 토크 추가** (`pendingRiderTorque += -input.x * tiltTorque`) | 그네 펌핑 자연, ODE 수정 0, swing-back boost 식 자명 |
| Q4 | Tilt 중 슬라임 위치 | **P1. tip 고정 anchor** (`rb.position = tipWorldPos` 매 FU 강제) | 매달림 그림 일치, 어떤 각도에서도 그랩 유지, 코드 단순 |
| Q5 | Launch 모델 | **J1. 기존 Charging→Launch 재사용** + `grabModeCarrierScale` knob 추가 | Carrier inheritance가 그대로 boost, 신규 launch 식 0 |
| Q6 | State 모델 | **S1. Rope 패턴** (Attached 유지, `currentBambooGrab` 핸들, partial 파일) | 신규 state 도입 시 transition matrix 재검토 비용 회피 |
| Q7 | Tilt knob set | **K2. 2-knob (initial) → 4-knob (after pump rework)** | 초기 `tiltTorque` + `chargingTiltScale`. 후속 ADR-0002 에서 Lean (`grabHoldBendDeg` + `chargingTiltScale`) + Anti-damping Boost (`grabPumpBoostGain` + `grabPumpChargingScale`) 로 분리 |
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

### BambooPlatform 측 API

```csharp
// 매 ApplySegmentTransforms 직후 갱신되는 tip world pos 캐시.
public Vector2 GetTipWorldPos() => cachedTipWorldPos;

// 외부 (SlimeController.BambooGrab) 가 부르는 입력 토크 라우터 (2-channel).
public void ApplyTiltTorque(float inputX, bool isCharging);
```

### Settings 필드 (Tip-Grab 전용 — BambooSettings asset)

| 필드 | 기본 | 의미 |
|---|---|---|
| `grabEnterDot` | 0.9 | `dot(surfaceNormal, gravityUp)` 임계. 이상이면 tip-grab 진입 |
| `grabHoldBendDeg` | 30 | Lean 채널이 노리는 정적 평형 휨 각도. 잡고만 있어도 이만큼 휨 |
| `grabSpringFreqHz` | 1.0 | Grab 모드 스프링 주파수 (rest 복원 속도) |
| `grabDampingRatio` | 0.335 | Grab 모드 damping. Boost gain 이 충분히 크면 사실상 무효화됨 |
| `grabMaxDeflectionDeg` | 180 | Soft cap. `grabCapPenaltyGain` 와 함께 비선형 stiffening 으로 enforce |
| `chargingTiltScale` | 0 | Charging 중 lean 채널 multiplier. 기본 0 = 발사 입력과 키 겹침 방지 |
| `grabPumpBoostGain` | 4 | Anti-damping boost gain (1/s). `2ζω` 초과 시 진폭 지수 성장 |
| `grabPumpChargingScale` | 0 | Charging 중 boost multiplier. 기본 0 |
| `grabCapPenaltyGain` | 5 | Soft cap stiffening 강도 (quartic) |
| `grabModeCarrierScale` | 1.0 | Tip-grab Launch 시 carrier inheritance(`ω × arm`) 추가 곱셈 |
| `grabLaunchPowerScale` | 1.0 | Tip-grab Launch 시 charge-power 점프 성분(`aim * power`) 곱셈. 발사 세기. carrier(swing momentum)와 독립 |
| `grabChargeFillScale` | 2.0 | grab 중 charge auto-drift 속도 배율. A/D가 tilt로 가서 aim 입력 없이 drift로만 충전되므로, 평지보다 빠르게 차게 함. >1 = 길게 안 눌러도 빨리 충전 |
| `grabChargeFullAimTime` | -1 | grab 중 `chargeFullAimTime`(full charge edge 도달 후 auto-launch 까지 grace/유지시간) override. -1 = slime 기본값. 0 = full 되는 즉시 발사. 크게 = swing timing 맞출 hold 시간 ↑ (surface tag 방식) |

기존 carrier 식과의 합성:
```
finalCarrierVel = ω × arm * VelocityTransferScale * (currentBambooGrab == this ? grabModeCarrierScale : 1f)
```
구현 위치 = `BambooPlatform.VelocityTransferScale` getter (기존 식에 `grabModeCarrierScale` 곱 한 줄).

### 의도 동작 시나리오

1. 슬라임 측면에서 bamboo로 점프, 측면 attach → 기존 그대로 (벽-비슷)
2. 측면 slide 또는 위에서 떨어져 tip cap에 도달 → `grabEnterDot` 충족 → `currentBambooGrab` set
3. A/D 누름 → Lean 채널이 `grabHoldBendDeg` 만큼 정적 휨 만듦. 슬라임은 tip 고정이라 같이 호 그림
4. 운동 시작되면 Anti-damping Boost 채널이 잠금 — tip 운동 방향대로 입력 유지 시 매 swing 진폭 누적 증가. 입력 떼면 스프링 복원 + damping 으로 자연 감쇠
5. Charge 입력 → Charging 진입. Grab anchor 유지. Lean/Boost 두 채널 모두 default 0 으로 mute (입력 키 겹침 회피)
6. release → Launch. **Aim-controlled Launch**: grab 중에도 평지 charge 와 동일하게 WASD(AimInput)로 발사 방향 조준. `aimOffset` 이 charging 중 AimInput 을 적분하므로 발사 방향 = aim 벡터, 세기 = `power * grabLaunchPowerScale`. 무입력이면 drift(시드 `-surfaceNormal ≈ tip-up`)로 기본 위쪽 발사. 거기에 swing momentum(`ω × arm` carrier, `grabModeCarrierScale`)을 가산 → 느린 swing 은 조준 방향 거의 그대로, 빠른 swing 은 조준 방향에서 이동 방향으로 비스듬히 skew
7. Airborne 진입 시 `currentBambooGrab = null`. 다음 attach까지 일반

### 알려진 한계 / 미해결

- 단일 라이더 가정 (기존 BambooPlatform 한계 그대로)
- bamboo가 비-수직(`baseRotation ≠ 0`) 일 때 `gravityUp` dot 임계가 의도와 어긋날 수 있음 — bamboo local up 기준으로 비교하도록 일반화 필요 (현재 수직 정렬 가정으로 충분)
- "tilt 가 maxDeflectionDeg 캡에 닿은 뒤 추가 입력은 무의미" — 디자인상 정상 (캡에서 추가 펌핑 차단 = 그네 펌핑 기교 강제)
- Multi-bamboo 클러스터 — 인접 bamboo로 점프하면 자연스레 그랩 핸들 갱신 (별도 처리 불필요로 평가, 첫 구현 후 검증)

## Pre-existing 프로젝트 이슈 해결 (작업 중 발견)

`Assets/Plugins/geometry3Sharp/`에 hand-written `.csproj` 2개 포함됨. C# Dev Kit (VSCode 확장) 가 자동으로 `dotnet build` 돌려 `obj/Debug/netstandard2.0/AssemblyInfo.cs` 생성 → Unity asmdef 컴파일의 auto-AssemblyInfo와 중복 충돌(CS0579) → Assembly-CSharp 재컴파일 차단. 해결: `.csproj` 2개 + `obj/` + `bin/` 삭제 (Unity는 `geometry3Sharp.asmdef`로 빌드, `.csproj` 불필요).
