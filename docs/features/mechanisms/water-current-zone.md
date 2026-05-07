# WaterCurrentZone (벽면 흐르는 물살)

벽면(또는 임의의 곡선)을 따라 흐르는 물살. 슬라임이 들어오면 흐름 방향으로 가속을 가하고, 동시에 물의 silhouette가 슬라임 주변에서 wake로 출렁인다. 표현은 **centerline을 따라 생성되는 5-vert 반-튜브 단면 ribbon mesh** + **vertex Z-displacement crest** + **좌/우 분리 spring 시뮬**.

이전 버전 (cube prism mesh)의 문제 — 직사각형 silhouette + 각진 외곽선 + 슬라임 진입에 시각 반응 없음 — 을 모두 해결.

쓰임:
- 벽을 "기어오를 수 없는 폭포 벽"으로 만드는 메커닉 (이전 버전과 동일한 push 의도)
- 물 자체에 visible motion이 있어야 하는 곡선 강줄기 / 흐름

## 아키텍처 개요

3개 구성 요소가 한 ribbon mesh에서 합쳐짐:

| Phase | 무엇 | 어떻게 |
|---|---|---|
| **A** Smooth ribbon | 사각 폴리곤 → centerline 따라 흐르는 매끄러운 띠 (Z 깊이 있음) | Catmull-Rom 보간 (open-polyline endpoint clamp) + 샘플마다 5-vert 반-arc 단면 (L, L', C, R', R)을 카메라 쪽으로 -Z 방향 돌출 |
| **B** Wave crests | 흐름이 빠른 곳에서 살짝 떠오르는 결 | Shader vertex stage가 노이즈 샘플 → `posOS.z -= crestN * _CrestAmplitude * centerWeight`. silhouette 가장자리는 alpha=1로 영향 안 받음 |
| **C** Slime wake | 슬라임이 들어가면 물이 헤집어졌다 회복 | 좌/우 채널 분리 spring 시뮬 (open-polyline neighbor clamp). 슬라임 위치를 centerline에 투영 → bilateral side weight 계산 → 양 채널에 비율로 impulse |

## 파일 레이아웃 (partial 패턴)

| 파일 | 역할 |
|---|---|
| [WaterCurrentZone.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterCurrentZone.cs) | 메인 — push physics, state filter, lifecycle, trigger callbacks, MaterialPropertyBlock |
| [WaterCurrentZone.Shape.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterCurrentZone.Shape.cs) | centerline 데이터, 보간된 RibbonPoint[] 빌드, 노멀 계산, 호 길이, generators, collider sync, polygon→centerline 마이그레이션 |
| [WaterCurrentZone.Mesh.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterCurrentZone.Mesh.cs) | 5-vert 단면 mesh 빌드 + 매 프레임 vertex 위치 업데이트 + 정적 Catmull-Rom 헬퍼 |
| [WaterCurrentZone.Simulation.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterCurrentZone.Simulation.cs) | 좌/우 채널 spring 시뮬 (`SimulateWaves(dt)`) |
| [WaterCurrentZone.Interaction.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterCurrentZone.Interaction.cs) | 슬라임 → ribbon impulse (`ApplySlimeImpulse(slime, dt, isEntry)`) — 가장 가까운 centerline segment 투영 + bilateral side weight + 인접 propagation |
| [WaterCurrentZoneEditor.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/WaterCurrentZoneEditor.cs) | scene-view drag handle + Shift-insert + Ctrl-delete + generator 버튼 |
| [Water_FlowingRefraction.shader](../../../SlimeJumping_v2/Assets/Art/Shader/Water_FlowingRefraction.shader) | `_CrestAmplitude` + `_CrestSpeed` + `_CrestBands` 추가, vertex stage가 Z displacement |
| [M_WaterFlowingRefraction.mat](../../../SlimeJumping_v2/Assets/Art/Materials/M_WaterFlowingRefraction.mat) | 머티리얼 |

## 콜라이더

`PolygonCollider2D` (RequireComponent, 자동 isTrigger=true). 단, **path는 designer가 직접 그리지 않음** — `centerlineLocal` + `ribbonWidth`로부터 `SyncColliderToRibbonOutline()`이 자동 생성 (좌 outline 정방향 + 우 outline 역방향 → 닫힌 폴리곤). 트리거 영역 = 시각 silhouette (XY 평면).

## 핵심 필드

### Push (이전 버전과 동일 의미)
| 필드 | 타입 | 기본 | 설명 |
|---|---|---|---|
| `flowDirection` | Vector2 | (0, -1) | 로컬 공간 흐름 방향. centerline 형태와 독립 (centerline은 곡선이지만 push는 directional) |
| `flowAccel` | float | 8 | 흐름 방향 가속도 (units/s²) |
| `flowTerminalSpeed` | float | 6 | 속도 캡 — falloff |

### State Filter
`affectAttached/Charging/Airborne/Stunned` (모두 기본 true)

### Surface Override (신규)
| 필드 | 타입 | 기본 | 설명 |
|---|---|---|---|
| `waterSurfaceMaterial` | SurfaceMaterial | null | 존 안에 있는 동안 슬라임의 `surfaceMod`를 이 material의 modifier로 swap. snow의 RefreshSnowBlend pattern을 mirror — 슬라임은 Attached로 유지되지만 (stick 0 / 마찰 0 / crawl 0 등으로 author해두면) 자연스럽게 미끄러져 떨어짐. null이면 swap 안 일어남 (host 그대로 유지). |

**기본 자산**: [Settings/SurfaceMaterials/Water.asset](../../../SlimeJumping_v2/Assets/Settings/SurfaceMaterials/Water.asset) — `stickDuration=0`, `carrierFriction=0`, `slideResistance=0`, `crawlSpeed=0`, `wallAttachDampen=0`, `hopPower=0`, `groundDashScale=0`, `landingVelocityRetention=1`. 디자이너가 새 zone 만들 때 이 asset을 슬롯에 드래그.

**Snow와 동시 적용시 우선순위**: WaterCurrent override > snow blend > host SurfaceTag. RefreshSnowBlend 끝에서 override가 active면 무조건 surfaceMod 덮어쓰기. discrete 필드 (useInertialSlide, override flags, curves)는 snow처럼 host에서 pin해 slide-model 안 바뀜.

**Stack semantics**: 여러 zone 겹칠 때 last-in-wins. `surfaceMaterialOverrideStack` (List<T> as stack) → top이 winning material. LIFO 제거로 순서 무관 push/pop 안전.

**SlimeController API** (신규):
- `PushSurfaceMaterialOverride(SurfaceMaterial mat)` — push + 즉시 RefreshSnowBlend 호출 (Attached/Charging일 때만 적용)
- `PopSurfaceMaterialOverride(SurfaceMaterial mat)` — last occurrence 제거 + RefreshSnowBlend
- `ActiveSurfaceMaterialOverride` (read-only) — top of stack, 없으면 null
- `ActiveSurfaceMaterial` 확장 — override > snow > host 순으로 resolve

**구현 노트**: 트리거 enter/exit에서 balanced push/pop. `overridePushed` flag로 중복 push 방지 + OnDisable에서 leftover pop 보장 (씬 언로드 / enable 토글 시 슬라임 surfaceMod 영구 잠김 방지). SlimeController의 SetOverride / Stunned 진입 path에서도 override stack clear (외부 override 후 stale 참조 방지).

**기존 씬 마이그레이션**: 기존 WaterCurrentZone instance는 `waterSurfaceMaterial = null`로 자동 마이그레이션됨 (이전엔 필드 없었음). 디자이너가 인스펙터에서 [Water.asset](../../../SlimeJumping_v2/Assets/Settings/SurfaceMaterials/Water.asset)을 슬롯에 드래그해야 무-stick 동작 활성. null이면 기존 host SurfaceTag 그대로 사용 (안전 fallback).

### Centerline (Phase A 신규)
| 필드 | 타입 | 기본 | 설명 |
|---|---|---|---|
| `centerlineLocal` | List<Vector2> | 빈 배열 | 디자이너가 scene view drag handle로 편집 (혹은 generator 버튼) |
| `ribbonWidth` | float | 0.8 | centerline에 수직 폭 |
| `tubeDepth` | float | 0.2 | 단면 arc의 카메라 쪽 돌출 깊이. 0 = flat plane (sticker처럼 보임) |
| `curveSubdivisions` | int | 5 | Catmull-Rom 샘플 수. 1 = polyline, 4-6 = 부드러운 곡선 |

### Wave Simulation (Phase C 신규)
| 필드 | 타입 | 기본 | 설명 |
|---|---|---|---|
| `springK` | float | 1.25 | Hooke 복원력 |
| `damping` | float | 2.0 | 감쇠 |
| `spread` | float | 2.5 | 인접 propagation 강도 |

### Slime Interaction (Phase C 신규)
| 필드 | 타입 | 기본 | 설명 |
|---|---|---|---|
| `impulseFactor` | float | 25 | 슬라임 속도 → 정점 속도 변환 |
| `contactRadius` | float | 1.0 | 이 거리보다 더 멀면 impulse 무시 (continuous만; entry는 항상 발화) |
| `spreadNeighbors` | int | 2 | impulse 받는 양옆 정점 수 |
| `spreadFalloff` | float | 0.5 | 인접 단계당 falloff |

### Visual
`flowMaterial`, `centerColor`, `edgeColor`, `visualFlowSpeed` (uv/sec), `noiseScale`. `visualFlowSpeed`/`noiseScale`은 `MaterialPropertyBlock`으로 인스턴스별 푸시.

## 5-vert 반-튜브 단면

각 centerline 샘플 `i`마다 5개 정점이 카메라 쪽으로 bulging하는 반-arc를 그림. `u ∈ {-0.5, -0.25, 0, +0.25, +0.5}`:

```
        C   (u=0,    z=-tubeDepth)
      L'    R'        (u=±0.25, z=-tubeDepth*0.707)
       \    /
        \  /
   L         R     (u=±0.5,   z=0)
```

Z 프로파일: `z(u) = -tubeDepth * cos(u * π)` → 반원 단면.

**왜 평면이 아닌 3D 단면이 필요한가**: 2.5D 카메라가 약간만 off-axis로 기울어도 평면 ribbon은 sticker처럼 들킴. tubeDepth = 0.2면 silhouette 가장자리(L, R)는 GameObject Z 평면(=0)에 있고, 중심(C)이 카메라 쪽으로 0.2만큼 튀어나와 visible volume이 생김. Phase B의 vertex crest가 추가로 결을 만들지만, **rest 상태 자체가 이미 3D**.

**시뮬 displacement 적용**:
- L (u=-0.5): full `dispL`, 외측 방향 (-normalLR)
- L' (u=-0.25): `dispL * 0.5`
- C (u=0): displacement 안 받음 — apex는 안 움직임
- R' (u=+0.25): `dispR * 0.5`
- R (u=+0.5): full `dispR`, 외측 방향 (+normalLR)

**vertex 색상 알파** (셰이더 edge fade 마스크):
- L, R: α=1 (silhouette — fully fade)
- L', R': α=0.5
- C: α=0 (solid center)

**메쉬 카운트** (M = (N-1) * curveSubdivisions + 1):
- 정점: 5*M
- 삼각형: 8*(M-1) (cross-section 사이 4개 quad × 2 tri)

## Catmull-Rom (open polyline)

[`WaterZone_v2.Mesh.cs`의 `CatmullRom`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterZone_v2.Mesh.cs#L177-L187) 공식 그대로 차용. 단 endpoint indexing은 closed-loop modulo wrap (`% n`) 대신 **clamp** (`Mathf.Max(i-1, 0)` / `Mathf.Min(i+2, n-1)`) — 양 끝점이 정확히 사용자가 author한 centerline 위에 sit (overshoot 없음).

## Spring Sim — 좌/우 채널 분리

`WaterZone_v2.Simulation.cs`는 단일 채널 (`displacement`/`velocity`). 우리는 **L/R 두 채널** (`dispL/velL`, `dispR/velR`)을 독립 시뮬:

```csharp
for (int i = 0; i < n; i++) {
    int l = i > 0 ? i - 1 : i;          // open clamp
    int r = i < n-1 ? i + 1 : i;        // open clamp
    
    deltaVL[i] = (-spread*(diffLL+diffRL) - springK*dispL[i] - damping*velL[i]) * dt;
    deltaVR[i] = (-spread*(diffLR+diffRR) - springK*dispR[i] - damping*velR[i]) * dt;
}
```

**왜 분리**: 슬라임이 좌측에서 들어왔을 때 좌측 silhouette만 변형되어야 자연스러움. 단일 채널은 이걸 표현 못 함.

## Slime Impulse

`OnTriggerEnter2D` (entry) + `FixedUpdate` 매 프레임 (continuous):

1. 슬라임 위치를 모든 centerline segment에 투영 → 가장 가까운 segment + 보간 t 찾기
2. **Bilateral side weight**: `sideNorm = clamp(Dot(slime - proj, normalLR_world) / halfWidth, -1, +1)` → `wR = (1+sideNorm)/2`, `wL = 1 - wR`. 슬라임이 정확히 centerline에 있으면 양 채널 각 0.5; 우측 silhouette에 있으면 `wR=1`.
3. `baseImpulse = |slimeVelocity| * impulseFactor * attenuation * dt`
4. `points[best/best+1].velL += baseImpulse * wL * (boundary blend)`, 우측 동등
5. ±`spreadNeighbors`까지 `pow(spreadFalloff, k) * 0.5` 가중치로 propagation (open-clamp)

## Phase B Vertex Crest (Shader)

`Water_FlowingRefraction.shader` vertex stage에 추가:

```hlsl
float2 crestUV = IN.uv * _NoiseScale.xy * 0.7
               + float2(0, -_CrestSpeed * _Time.y);
float crestN = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, crestUV, 0).r - 0.5;

if (_CrestBands > 0.5)  // BotW-style banding option
    crestN = round(crestN * _CrestBands) / _CrestBands;

float centerWeight = saturate(1.0 - IN.color.a);
posOS.z -= crestN * _CrestAmplitude * centerWeight;
```

`_CrestAmplitude` 0.08 (기본). **안전 budget**: `_CrestAmplitude < tubeDepth`면 crest peak도 GameObject Z 평면 앞에 머무름 → 벽 뒤로 클리핑 안 됨.

`_CrestBands = 0` (기본): 매끄러운 노이즈. `_CrestBands > 0`: round-to-N-levels로 banded look (Zelda BotW 폭포 스타일). low-poly 환경 매칭 옵션.

## 마이그레이션 (기존 씬)

기존 cube 메쉬 버전의 `WaterCurrentZone`은 사각 `PolygonCollider2D` path로 author되어 있음. `OnEnable` / `OnValidate`에서 `TryMigrateFromPolygon()`이 한 번 fire:
- polygon path AABB 추출 → `centerlineLocal = [(midX, +halfY), (midX, midY), (midX, -halfY)]` 3점 vertical
- `ribbonWidth = AABB.x_span`
- `migrated = true` (idempotent flag)

`flowAccel` 등 다른 필드는 보존. 디자이너가 원하면 추가로 centerline을 곡선으로 다듬을 수 있음.

## 동작 (Push 부분)

```
slimeState in [Attached, Charging] → dir = surfaceTangent의 gravity-down 성분
slimeState in [Airborne, Stunned]  → dir = Vector2.down
falloff = clamp01(1 - dot(slime.Velocity, dir) / flowTerminalSpeed)
slime.AddSustainedFlowImpulse(dir * (flowAccel * falloff * dt))
```

**`AddSustainedFlowImpulse` vs `AddExternalVelocity`**: 일반 `AddExternalVelocity`는 Attached state에서 `velocity += projected_push` 후 다음 frame 의 `FixedUpdateAttached`가 `velocity = (slideSpeed + crawlVelocity) * tangent`로 덮어써서 push가 한 frame만 효과적임. → 같은 flowAccel로 Attached가 Airborne보다 ~50× 약함 (사용자가 관찰한 "10× 차이" 버그). `AddSustainedFlowImpulse`는 Attached/Charging일 때 push를 `slideVelocity` (inertial) 또는 `crawlVelocity` (legacy)에 직접 적분 → frame 간 누적되어 Airborne와 동일한 가속 거동. Airborne/Stunned는 기존대로 `externalDeltaV` 경로.

## 셰이더: Water_FlowingRefraction

Refraction 기반 (`_CameraOpaqueTexture` 샘플 + 노이즈 distortion + tint). Phase B 추가 외에는 변경 없음. 이미 있는 `_Turbulence`/`_LayerSpeedRatio`/`_EdgeNoise`/`_EdgeFadePower`는 모두 활용:
- `_FlowSpeed` → V축 noise scroll (downstream 시각 모션)
- `_LayerSpeedRatio` → 두 노이즈 레이어 비대칭 속도 (한 방향 균일 슬라이드 깸)
- `_Turbulence` → 도메인 워프 (eddy-like swirls)
- `_EdgeFadePower` → vertex.color.a 기반 silhouette 페이드
- `_EdgeNoise` → silhouette 가장자리 wobble

`Cull Off` (양면). off-axis 카메라에서 backface도 보여 3D 볼륨감 유지.

## 메쉬 재생성

- `OnEnable` (런타임 + 에디터): `TryMigrateFromPolygon` → `BuildShape` → `SyncColliderToRibbonOutline` → `BuildRibbonMesh`.
- `OnValidate`: 정규화 후 `EditorApplication.delayCall`로 재빌드.
- `[ContextMenu("Rebuild Ribbon")]` (`Rebuild()`): 명시 트리거.
- 매 frame: `LateUpdate` → `UpdateMeshVerts()` (정점 위치만 푸시; UV/색/tris는 정적).

## 설치 워크플로

1. 빈 GameObject → `Add Component → Water Current Zone`
2. 인스펙터에서 "Generate Vertical Line" (또는 "Slope (45°)") 버튼 → 5점 centerline 생성
3. Scene view에서 노란 핸들로 점 이동, Shift-click으로 점 추가, Ctrl-click으로 점 삭제
4. `ribbonWidth`, `tubeDepth` 슬라이더로 두께 조정
5. `flowDirection` 화살표(파란색 gizmo)로 push 방향 확인 → state filter 토글
6. 우클릭 "Rebuild Ribbon" 또는 자동 (OnValidate)

테스트 씬 자동 생성: `SlimeJumping/Mechanism/Setup WaterCurrent Zone Test`. 5점 vertical centerline + ribbonWidth 2.4의 ribbon이 벽 위에 만들어지고, 슬라임 prefab을 떨어뜨려 wake 테스트.

## 관련 파일

- [WaterCurrentZone.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterCurrentZone.cs)
- [WaterCurrentZone.Shape.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterCurrentZone.Shape.cs)
- [WaterCurrentZone.Mesh.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterCurrentZone.Mesh.cs)
- [WaterCurrentZone.Simulation.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterCurrentZone.Simulation.cs)
- [WaterCurrentZone.Interaction.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterCurrentZone.Interaction.cs)
- [WaterCurrentZoneEditor.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/WaterCurrentZoneEditor.cs)
- [Water_FlowingRefraction.shader](../../../SlimeJumping_v2/Assets/Art/Shader/Water_FlowingRefraction.shader)
- [M_WaterFlowingRefraction.mat](../../../SlimeJumping_v2/Assets/Art/Materials/M_WaterFlowingRefraction.mat)
- [WaterCurrentZoneTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/WaterCurrentZoneTestSetup.cs)
- 외부 힘 파이프라인: [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md)
- 패턴 차용원: [WaterZone_v2](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterZone_v2.cs) (closed-loop 폐쇄형 물 시뮬)
