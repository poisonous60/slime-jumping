# Snow Field

Stage 2 눈 비주얼. 플랫폼의 위쪽 면에 자동으로 눈이 깔리고, 슬라임이 착지할 때 파묻히고, 시간이 지나면 다시 채워지는 시스템 — Trail/DeformableMud 두 패턴의 조합. 현재 Phase 3 (fragment 폴리시: footprint detail / fresnel rim / glitter, 키워드 토글) 까지 구현.

## 전체 목표 (전체 vision)

1. **눈은 비주얼 전용**. 콜라이더 없음, SurfaceTag 없음. 슬라임은 항상 호스트 플랫폼의 콜라이더 위에서 attach/이동.
2. **호스트 플랫폼**의 콜라이더에 별도 `Snow.asset` SurfaceMaterial 할당 (Mud-like 접착감). SnowField와 결합 없음 — 디자이너가 per-level 셋업.
3. **자동 적층** — 슬로프 ≤ `maxSnowSlopeDeg` (기본 45°)인 윗면에만 눈이 형성. 수직 벽이나 오버행은 비어있음. SnowField 컴포넌트에서 `overrideMaxSnowSlope` 토글로 per-field 덮어쓰기 가능 — OFF 면 SnowSettings 자산 값 사용, ON 이면 `maxSnowSlopeDegOverride` 가 그 SnowField 한정으로 대체 (자산은 변경 안 됨).
4. **3D 굴곡 따라가기** — 호스트의 실제 윗면 triangulation을 그대로 복사해 vertex normal 방향으로 offset (surface-offset shell). 평지/슬로프/언덕/heightmap 변위 모두 정확히 따라감.
5. **착지 carve** — 슬라임이 떨어진 X 위치에 가우시안 dip. Trail-style 누적 (한 번 = 얕은 자국, 반복 = 깊은 자국).
6. **시간 회복** — 눈이 내리며 `heightCurrent[v]`가 `heightRest[v]`를 향해 lerp. 자국이 점진적으로 채워짐.
7. **지붕 occlusion** — 위에 천장이 있는 영역은 `heightRest`가 작아 그 이상 안 쌓임. `Physics2D.Raycast`로 한 번 계산.
8. **부드러운 가장자리** — 패치 경계에서 두께 0으로 taper, 호스트 표면으로 둥글게 내려감.
9. **저폴리 룩** — Quibli 셰이더 스택과 어울리는 폭신한 눈 비주얼: 푸른 그늘 ramp + 글리터 + Fresnel rim.

## 현재 구현 (Phase 1 + 2 + 3) ✓

> **Phase 경계 메모**: Phase 1 의 원래 설계 (mesh 에 변위를 baked, heightCurrent = heightRest 고정, 셰이더 없음)는 Phase 2 작업 때 폐기됐다. 현재 코드는 Phase 1 의 **자동 추출 파이프라인 + edge taper + lifecycle** 만 그대로 유지하고, **mesh 포맷 / 데이터 모델 / 셰이더 / routing** 은 Phase 2 가 처음부터 다시 짠 결과다. Phase 3 는 약속대로 fragment stage 만 갈아끼웠다 — `SnowMeshBuilder` 에서 uv2.y 를 heightRest 로 채우는 한 줄 변경 외에 vertex 셰이더 / 데이터 모델 / CPU 파이프라인 손 안 댔다. 그래서 세 phase 를 분리하지 않고 한 섹션으로 묶었다. Phase 4 부터는 다시 CPU 데이터 (heightRest 를 ceiling raycast 로 결정) 와 fragment 단 합쳐서 작업해야 한다.

### Tuning 자산 — `SnowSettings` ScriptableObject

ScriptableObject 한 개에 모든 snow tuning 이 모임 (deposit / mesh / render / carve / recovery). SnowField 가 `settings` 필드로 참조; 여러 SnowField 가 같은 자산을 공유하면 한 번 튜닝으로 모두 업데이트. settings 가 null 이면 SnowField 가 baked-in 기본값으로 fallback (시스템 안 깨짐). 다른 비주얼이 필요한 레벨은 별도 `SnowSettings` 자산을 만들어 그 SnowField 의 `settings` 슬롯에 끼움.

기본 자산 위치: `Assets/Settings/Snow_Default.asset` (`Tools → SlimeJumping → Snow → Create Default Settings Asset` 으로 생성).

### 자동 생성 파이프라인 (Phase 1 origin, 손 안 댐)

`SnowField` 컴포넌트를 호스트 GameObject (`MeshFilter`가 있는 플랫폼)에 추가하면 `[ExecuteAlways]` + `OnValidate→delayCall` 패턴으로 자동 rebuild:

1. `SnowTopSurfaceExtractor.Extract(mesh, maxSnowSlopeDeg)` — 호스트 메쉬의 모든 triangle 중 face normal.y ≥ cos(maxSnowSlopeDeg)인 것만 채택.
2. position-quantized vertex welding (flat-shaded 메쉬의 중복 정점 합침).
3. union-find로 연결된 component마다 별도의 `TopPatch`로 분리.
4. 각 패치별로 verts/per-vertex normals/CCW tris/directed boundary edges 출력.

### Subdivision (Phase 2 신규)

low-poly 호스트 메쉬에서도 carve 가 보이도록, `SnowPatchSubdivider.Subdivide` 가 BFS taper 전에 host triangle 들을 barycentric 으로 재귀 세분화. midpoint normal/heightRest 는 endpoint 보간으로 계산 → **3D 굴곡 보존**, 단지 vertex 만 dense 해짐. `subdivLevels` (0~3, 기본 1 = 4× verts) 은 SnowSettings 에 있음.

### Overlap mask (Phase 2 origin, Z-aware + manual keep + 모드 토글 추가됨)

`SnowField.MaskOutOverlap` 이 `excludeOverlapWithOtherPlatforms=true` 일 때 Subdivide 직후 BFS taper 직전에 실행. 한 번의 `Physics2D.OverlapArea` 로 패치 AABB 안의 candidate Collider2D 를 모은 다음 hostCollider / sister SnowField (priority 낮은 쪽) / 본인의 `manualKeepColliders` 를 제외해 `foreign` 리스트를 만든다.

**드롭 알고리즘은 `maskMode` 로 두 경로 중 선택**:

#### `SampleBased` (default — 기존 동작)

각 snow tri 의 4개 sample (3 verts + centroid) 중 하나라도 foreign 안이면 tri 를 통째로 drop. Subdivision 으로 인접 tri 가 정점을 공유하므로 단일 정점이 foreign 안에 들어가면 그 정점의 1-ring fan (~6 tris) 이 함께 drop 됨 → "캐스케이드" 로 인해 보이는 갭이 foreign 의 실제 XY footprint 보다 넓어진다. 빠르고 기존 씬 동작과 byte-identical 하지만 정확도가 떨어짐.

#### `GeometricClipping` (opt-in)

Sutherland-Hodgman tri-vs-foreign 기하학적 차집합. 각 tri 를 foreign 의 각 엣지에서 정확히 잘라내 `(tri ∩ NOT foreign)` 만 남긴다. 캐스케이드 없음, rim 이 foreign 엣지에 정확히 안착. 새로 생기는 cut 정점은 mesh-local position / vertNormal 을 tri 의 세 corner 에서 barycentric 보간으로 산출하고, position-quantized dedup (1e-4) 으로 인접 tri 가 같은 cut 점을 emit 하면 한 인덱스로 합친다 (그렇지 않으면 boundary regen 이 seam 을 두 boundary 로 인식해 BFS taper 가 패치 내부에 가짜 rim 을 seed 한다).

**커버리지**:
- **Convex fast-path (1-piece SH)** — `BoxCollider2D`, `CircleCollider2D` (24-gon tessellate), `CapsuleCollider2D` (양 끝 반원 12분할 + 직선변, 26-gon), 그리고 `PolygonCollider2D` 단일-path 볼록 폴리곤. PolygonCollider2D 는 SH 직전에 `SimplifyCollinear` (perpendicular ε=1e-4f) 로 near-collinear 중간 verts 를 corner 로 줄임 — `MeshToCollider2DGenerator` 가 시각적으로 직선인 변에 vert 를 dense 하게 박는데, 이걸 그대로 SH 의 per-edge half-plane 으로 돌리면 얇게 어긋난 normal 들이 누적돼 rim 이 ~1 sub-tri 안쪽으로 밀린다. 면적 보존되는 simplify (collinear vert 의 shoelace 기여 = 0) 라 시각/물리적 의미 변화 없이 SH 정확도만 올라간다.
- **Decomposition path (N-piece SH)** — concave 단일-path PolygonCollider2D, multi-path PolygonCollider2D (outer/hole 자동 분류). `TryDecomposeForeignPolygon` 이 ear-clipping triangulation 으로 foreign 을 convex piece (≈ tri) 들의 union 으로 쪼갠 뒤, 각 piece 마다 fast-path 와 동일한 SH per-edge 루프를 돌려 누적 차감. `(snow tri − foreign) ≡ ((snow tri − piece₁) − piece₂) − ⋯` 가 disjoint piece 들에 대해 성립. multi-path 는 nesting depth (= "이 path 의 첫 vert 를 다른 path 가 포함하는 횟수") 로 outer (even) / hole (odd) 자동 분류 → CCW outer + CW hole 로 winding 정규화 → standard hole-bridge (rightmost-vert + closest +x ray cast) 로 단일 weakly simple polygon 으로 합쳐 ear-clip. 분해 결과는 self-check (path 자기교차 검증 + piece 면적 합 ≈ 입력 면적, 1% 허용) 를 통과해야 적용; 통과 못 하면 sample-based 폴백.
- **Per-pair sample-based fallback** — `EdgeCollider2D` (두께 0, mask 의미 정의 안 됨), `CompositeCollider2D`, 분해 self-check 가 거절한 자기교차 / degenerate polygon, 그 외 처리 못 하는 collider 종류. 그 한 쌍만 sample-based 로 fallback → 모드 전환했다고 지원 외 케이스가 더 나빠지지 않음.

Manual keep 은 clipping 을 **선점** — keep collider 와 tri 가 영역을 공유하면 clipping 을 건너뛰고 tri 를 통째 유지. Z-aware filter (`maskUsesZ`/`maskZWindow`) 는 per-pair 로 적용 (centroid Z 기준).

Default 가 `SampleBased` 인 이유: 기존 씬 (이미 튜닝된 SnowField 들) 의 동작이 토글 없이 변하면 안 되므로. 캐스케이드 갭이 신경 쓰이는 SnowField 만 인스펙터에서 `GeometricClipping` 으로 전환.

#### 두 모드 공통

- **Z-aware mask** (`maskUsesZ`, `maskZWindow`) — foreign collider 의 `transform.position.z` 를 미리 캐싱해두고, ±`maskZWindow` 안일 때만 그 foreign 으로 mask 발동. Z 깊이가 있는 큰 mesh 한 장에 SnowField 한 개를 붙여도, 앞쪽-Z 슬라이스만 foreground 와 mask 되고 뒤쪽-Z 슬라이스 (foreground 뒤 void 영역) 는 같은 SnowField 안에서 snow 그대로 유지 → 별도 backdrop mesh 필요 없음. SampleBased 는 per-sample, GeometricClipping 은 per-pair (centroid 기준) 으로 적용.
- **Manual keep override** (`manualKeepColliders`) — SampleBased 에선 drop 결정 후 tri ↔ collider 의 **실제 영역 overlap** (tri 정점이 collider 안 / collider 정점이 tri 안 / edge 끼리 교차 중 하나) 이면 tri 를 **복원**. GeometricClipping 에선 같은 영역 overlap 테스트로 clipping 을 **선점** (keep 영역과 겹치는 tri 는 통째 유지). PolygonCollider2D / BoxCollider2D 는 풀-precision, Circle / Capsule / Edge / Composite 는 cheap fallback (tri 정점이 collider 안만 봄). 출력은 tri 단위 — collider 가 tri 의 일부만 덮어도 tri 전체 복원/유지. keep collider 자체는 (a) `useTriggers=false` 로 인한 자동 제외 + (b) `IsManualKeep` 의 명시적 거름 두 단계로 자기 자신이 foreign 이 되지 않게 한다.

drop 후엔 새 boundary 를 다시 dirSet (a→b, b→a 검사) 으로 추출 → BFS taper 가 잘라낸 가장자리도 자연스럽게 동그랗게 내려감.

### Edge taper (Phase 1 origin)

graph BFS로 boundary로부터 hop count 계산. `edgeTaperHops` 설정 (기본 1):
- hop=0 (boundary): 두께 0 — 호스트 표면에 닿음
- hop≥hops (interior): 풀 `restThickness`
- 그 사이: smoothstep ramp

좁은 패치/긴 strip 패치도 일관된 결과 — vertex normal 절대값 대신 토폴로지 hop 사용. 결과는 `heightRest[v]` 배열에 저장 → SnowStrip 데이터 모델로 그대로 들어감.

### Mesh 포맷 (Phase 2 에서 전면 재설계 — 셰이더-friendly)

`SnowMeshBuilder.Build` — 호스트 patch tris 를 정점은 **un-displaced** 채로 (`mesh.verts[i] = patch.verts[i]`) emit. 변위는 셰이더가 GPU 에서 처리. **Phase 1 의 baked-displacement (`top[i] = verts + normal * heightCurrent[i]`) 는 폐기됐다.**

| 채널 | 내용 | 셰이더 사용처 |
|------|------|------|
| `position` | un-displaced patch.verts (호스트 표면 위) | `SnowVertex` 가 `_SnowHeightBuf` 로 displace |
| `normal` | per-tri face normal at REST 변위 위치 (flat shading) | Quibli 스톡 라이팅 |
| `tangent.xyz` | top 정점 = patch.vertNormals (변위 방향), base 정점 = 0 (anchored) | 셰이더 displacement axis |
| `uv (TEXCOORD0)` | (patch.verts.x, patch.verts.y) | Phase 3 footprint/glitter 샘플링 + Phase 2 procedural noise sampling |
| `uv2 (TEXCOORD1)` | `(origIdx, heightRest[origIdx])` — x = StructuredBuffer index (raw float, shader 가 uint cast), y = 휴식 두께 (Phase 3 신규) | x: `_SnowHeightBuf[(uint)staticLightmapUV.x]` / y: vertex stage 에서 carve mask `1 - h/heightRest` 계산 |

**양면 emit** (top + side strip 모두 reversed-winding 페어 포함) → material 무관하게 어떤 카메라 각도에서도 보임. AABB 는 un-displaced 정점 기준이라 `bounds.Expand(2 * maxRest)` 로 padding 해서 frustum culling 클립 방지.

### 데이터 모델 — `SnowStrip` (Phase 2 에서 확장)

`SnowStrip` 컴포넌트가 각 자식 GO 에:

- `verts[]`, `vertNormals[]`, `tris[]`, `boundaryEdges[]` — 패치 토폴로지 (Phase 1)
- `heightRest[]` — taper-shaped 휴식 두께 (Phase 1 origin, 변경 없음)
- `heightCurrent[]` — per-vertex 현재 두께. **Phase 2 부터 carve/recovery 로 mutate** (Phase 1 에선 항상 = heightRest)
- `heightBuf` (`ComputeBuffer`, V × float, `StructuredBuffer<float>` GPU-side) + `MaterialPropertyBlock cachedRenderer` + `bool dirty` — heightCurrent 의 GPU 미러 (**Phase 2 신규** / Phase 2.1 에서 `Texture2D V×1 RFloat` 에서 `ComputeBuffer` 로 마이그레이션 — GPU max texture width(16384) 초과 패치도 수용)
- `aabbMin/aabbMax` (Vector2) — xy AABB pre-filter 용 (Phase 2 신규)
- `host` — 호스트 transform 참조. **Phase 2 에선 미사용**, Phase 4 ceiling raycast / Phase 5 slope diffusion 대비 보존

### Carve / recovery (Phase 2 신규 — CPU 데이터 layer)

- **Carve 이벤트**: `SnowStamper` 가 slime 에 1개 부착, `SlimeController.OnLanded` 단일 구독. `slimeController.SurfaceCollider` 를 `SnowFieldRegistry` 로 O(1) 조회 → 매치되는 SnowField 의 `HandleLanded(world, impactSpeed)` 호출 (raw impact 만 전달; depth/radius 매핑은 stamper 가 안 함).
- **세기 매핑**: `SnowField.HandleLanded` 가 자기 `SnowSettings` 에서 `minImpactSpeed`/`depthPerImpact`/`depthMax`/`radiusPerImpact`/`radiusMax` 읽어 `depth = min(depthMax, impact * depthPerImpact)`, `radius = min(radiusMax, impact * radiusPerImpact)` 산출. `impact < minImpactSpeed` 면 reject. settings null 이면 baked-in 기본값.
- **Per-vertex carve**: SnowField 가 contact 를 mesh-local 로 변환, 자식 SnowStrip 마다 `CarveLocal(localImpact, depth, radius, profile)` 호출. 각 strip 은 자기 xy AABB 로 pre-filter (radius padding) → 무관 strip 의 V 순회 0. 영향권 vertex 마다 `falloff(d/r)` 평가 후 `heightCurrent[v] = max(0, heightCurrent[v] - depth * falloff)`.
- **Carve falloff profile** (`SnowSettings.carveProfile`): `Gaussian` `exp(-4·(d/r)²)` (좁은 종모양·뾰족), `RaisedCosine` `0.5·(1+cos(π·d/r))` (매끈한 사발, 중심·가장자리 모두 zero-slope · **default**), `SphericalCap` `√(1-(d/r)²)` (구체 압흔 — 가장 둥근 바닥, d=r 에서 각짐), `Parabola` `1-(d/r)²` (둥근 바닥·선형 테이퍼).
- **회복 lerp**: `SnowField.FixedUpdate` 가 dirty/회복 중인 strip 만 `activeStrips` HashSet 에서 추적. 각 active strip `TickRecovery(dt, settings.accumulationRate)` 로 `heightCurrent[v] += accumulationRate * dt` (cap heightRest). 모든 vertex 가 heightRest 도달하면 active set 에서 제거 → idle FixedUpdate cost 0.

### GPU 업로드 + vertex displacement 셰이더 (Phase 2 신규)

- **per-strip heightBuf**: `ComputeBuffer(V, sizeof(float), Default)` → 셰이더 측 `StructuredBuffer<float> _SnowHeightBuf`. 변경 발생 (`dirty=true`) 시에만 `heightBuf.SetData(heightCurrent)` 그리고 `MaterialPropertyBlock.SetBuffer(_SnowHeightBuf, ...)` 푸시. material 자산은 read-only. **Phase 2.1**: `Texture2D V×1 RFloat` 에서 마이그레이션 — `subdivLevels=3` 등으로 패치 정점이 GPU max texture width(보통 16384) 를 초과하면 `Texture2D` alloc 이 throw 했던 문제 제거. `#pragma target 4.5` 필요 (이미 ForwardLit pass 에 설정).
- **`Ground_Quibli_Snow.shader`** — Quibli StylizedLit 클론. ForwardLit pass 만 vertex/fragment pragma 를 snow 래퍼로 교체. 다른 pass (Outline / ShadowCaster / DepthOnly / DepthNormals / GBuffer / Meta / Universal2D) 는 stock 그대로. `_SnowHeightBuf` 는 Properties 블록 대신 `.hlsl` 에서 `StructuredBuffer<float>` 로 직접 선언 (compute buffer 는 Properties 로 선언 불가).
- **`Ground_SnowVertexInject.hlsl`** — `SnowVertex` 가 `input.staticLightmapUV.x` (TEXCOORD1.x, mesh.uv2.x) 를 raw 정점 인덱스로 받아 `uint vid = (uint)(staticLightmapUV.x + 0.5)` 로 캐스트, `_SnowHeightBuf[vid]` 직접 인덱싱 (이전 `SAMPLE_TEXTURE2D_LOD` 경로 폐기 — 정점 인덱스가 곧 버퍼 인덱스). 옵션 `_SnowSurfaceNoise` multiply, `input.tangentOS.xyz * h * _SnowHeightScale` 변위. Phase 3 부터는 `staticLightmapUV.y` 의 heightRest 와 비교해서 `mask = saturate(1 - h/heightRest)` 계산 후 `SnowVaryings { v = LitPassVertex(input), snowExtras = (mask, 0) }` 반환.

### Fragment 폴리시 (Phase 3 신규 — 셰이더 단)

Phase 3 는 약속대로 fragment stage 만 갈아끼웠다 — vertex / mesh / 데이터 모델 / CPU 파이프라인 손 안 댔고, mesh.uv2.y 가 0.5 → heightRest 로 바뀐 한 줄만 mesh builder 에 추가됐다.

- **wrap 전략**: stock `StylizedPassFragment` 를 직접 갈아엎지 않고 wrapper `SnowFragment` 가 먼저 호출 → 결과에 snow 폴리시 (footprint / fresnel / glitter) 를 post-blend. 푸른 그늘 ramp 는 Quibli 가 이미 가지고 있는 `_GradientRamp` 슬롯 (NdotL toon ramp) 에 디자이너가 blue→white 그라데이션 텍스처를 할당하면 끝 — 셰이더 작업 없음.
- **`Ground_SnowFragmentInject.hlsl`** (신규) — `SnowVaryings` wrapper struct 정의 (`Varyings v` + `float2 snowExtras : TEXCOORD9`). Quibli `Varyings` 는 TEXCOORD0~8 까지 조건부로 사용, TEXCOORD9 은 비어 있어 안전. 그리고 `SnowFragment(SnowVaryings i)`:
  1. `half4 base = StylizedPassFragment(i.v);` — Quibli stock 라이팅 + 그늘 ramp 그대로
  2. `_SNOW_FOOTPRINT` (gated by `mask > 1e-3`): `_FootprintAlbedo.r` 을 host-local `i.v.uv * _FootprintTiling` 으로 샘플해서 *형태 마스크* 로 사용. 내부 눈 색은 별도 `_FootprintTint` 컬러. carve 영역 만 `lerp(base, base*tint, mask * _FootprintBlend * pattern)` — texture 는 grayscale shape, color 는 디자이너 컬러 슬롯에서.
  3. `_SNOW_FRESNEL`: `pow(1 - saturate(dot(N, V)), _SnowFresnelPower)` 의 그레이징 highlight 가산
  4. `_SNOW_GLITTER`: `_GlitterNoise` 노이즈 step + view-direction `pow(NdotV, _GlitterViewSharpness)` modulate → sparkle 가산
- **키워드 토글**: `_SNOW_FOOTPRINT` / `_SNOW_FRESNEL` / `_SNOW_GLITTER` 모두 `shader_feature_local_fragment`. `SnowSettings` 의 토글 bool 3개 + 텍스처 슬롯 3개 (shadowRamp / footprintAlbedo / glitterNoise) 가 **runtime material clone** 에 push 되어 디자이너는 SnowSettings 한 곳만 만지면 됨. SnowField 가 rebuild 마다 `new Material(snowMaterial)` 클론을 만들고 (HideFlags.DontSave) Phase 3 텍스처/키워드 적용 후 자식 SnowStrip 들에 sharedMaterial 로 할당, OnDestroy 에서 dispose. 원본 `Ground_Quibli_Snow.mat` 자산은 read-only 유지 — heightTex MPB 와 동일한 invariant. 기본값은 모두 off → Phase 2 와 시각적으로 동일 (안전한 기본값).
- **Quibli 파일 미수정**: 모든 추가는 클론 셰이더 / 신규 inject HLSL 에서만. `Assets/ThirdParty/Quibli/` 트리는 read-only 유지.
- **노멀 양자화**: 별도 fragment 작업 없음 — `SnowMeshBuilder` 가 per-tri face normal 을 굽기 때문에 자연스럽게 flat 셰이딩 = patch 느낌. 변위 후 normal 재계산은 안 함 (cost 무시 가능 변위라 Phase 2 부터 채택).

### Routing — registry + stamper (Phase 2 신규)

`SnowField.OnEnable` 에서 `hostCollider → this` 를 `SnowFieldRegistry` 정적 dict 에 등록. 슬라임 한 개의 `SnowStamper` 가 OnLanded 단일 구독 → registry 조회 → 해당 SnowField 만 dispatch. SnowField 들이 모두 OnLanded 구독하는 N-callback 폭증 회피 (TrailColliderRegistry 패턴).

### 컴포넌트 lifecycle (Phase 1 + 2 합본)

- `Reset` / `OnValidate` / `Awake` → ScheduleRebuild → delayCall 로 `RebuildNow` (Phase 1)
- `OnEnable` (play): `SnowFieldRegistry.Register` (Phase 2)
- `OnDisable`: dirty strips flush + activeStrips clear + registry unregister (Phase 2)
- `FixedUpdate` (play, idle 일 땐 no-op): activeStrips 에 있는 strip 만 TickRecovery + FlushIfDirty (Phase 2)
- `OnDestroy` → 자식 `SnowStrip_*` GO 자동 destroy (각 SnowStrip 도 자기 heightTex destroy) (Phase 1 + 2)
- 호스트 mesh 해시 watcher — 인스펙터 repaint 마다 mesh 변경 감지, `Rebuild Now` 안내 HelpBox (Phase 1)
- 작은 noise patch 필터 — `minPatchTris` 이하 connected component drop (Phase 1)

### 에디터 도구

- `[CustomEditor]` SnowFieldEditor — Rebuild Now 버튼, mesh 변경 알림 (Phase 1)
- `Tools → SlimeJumping → Snow → Rebuild Selected / Rebuild All In Scene` (Phase 1)
- `Tools → SlimeJumping → Snow → Create Default Settings Asset` — `Snow_Default.asset` 생성 + 머티리얼 자동 seed (Phase 2, idempotent)
- `Tools → SlimeJumping → Snow → Wire MainScene + SampleScene` — slime 에 SnowStamper 추가, SnowField 의 settings/hostCollider 자동 wire (Phase 2, idempotent)
- 디버그 기즈모: 패치 외곽선 (초록 line), per-vertex 노멀 (파란 line)

### Snow `SurfaceMaterial` 자산 (Phase 7 부터 SnowField 와 결합)

`Assets/Settings/SurfaceMaterials/Snow.asset` — Mud-derived. `slideResistance` 1.0 (vs Mud 1.3, 살짝 더 미끄럼), 그 외는 Mud 값 그대로. **Phase 7 부터** 호스트 SurfaceTag 에 수동 할당하는 대신 `SnowSettings.snowSurfaceMaterial` 슬롯에 들어감. SnowField 가 attach 된 host 위에서만, 슬라임 contact 위치의 carve 깊이에 따라 host 의 SurfaceTag 와 동적으로 blend (벽처럼 눈이 안 쌓이는 가파른 면 = 0% blend = host 그대로 / 평지 풀-thickness = curve(1) 만큼 blend).

### 의도적으로 안 한 것

- **Disk persistence**: 메모리 only, scene 재로드 시 fresh. snow carve 는 일시 비주얼이라 Trail 처럼 디스크 저장 불필요.
- **Sliding carve** (Phase 6 옵션 A): doc 의 leaning B 채택. 게임필 잡힌 후 결정.
- **Fragment-stage 폴리시** (Phase 3): 푸른 ramp / glitter / Fresnel rim / 발자국 detail texture.
- **Spatial hash / KD-tree**: V < 2000 면 brute force 충분.
- **다른 pass vertex displacement**: ForwardLit 만. 그림자/depth 는 un-displaced rest 모양으로 캐스트 — 얇은 carve (≤ 0.2m) 면 시각 차이 미미.

## Phase 7 — Gameplay 결합 (read-only sampler) ✓

Phase 1~3 의 mesh / vertex / fragment 파이프는 손대지 않음. SnowField 가 이미 들고 있는 `heightCurrent` 데이터를 *read-only* 채널로 노출해 슬라임 측이 surface 마찰 / 점프 파워에 반영하도록 한 single 진입점 추가. 기존 invariants (snow 가 collider/event push 안 함, 비주얼 vs 게임플레이 결합 금지) 중 *write* 측은 그대로, *read* 측만 허용하는 형태로 완화.

### 새 read API

- `SnowStrip.SampleDepthRatio01(Vector3 localXY) → float`: AABB pre-filter + nearest-vertex 로 `clamp01(heightCurrent / heightRest)` 반환. 0 = 눈 없음 / 풀로 carve 됨, 1 = 풀 두께. Carve 가 사용하는 동일한 mesh-local 좌표계.
- `SnowField.SampleDepthRatio01(Vector2 worldXY) → float`: world→local 변환 후 자식 strip 들에 위임, 겹치는 patch 는 max.
- `SnowFieldRegistry.SampleDepthRatio01(Collider2D host, Vector2 worldXY) → float`: gameplay 가 호출하는 단 하나의 entry point. 미등록 host 는 0.

### 새 generic Carve API

- `SnowField.Carve(Vector2 worldCenter, float depth, float radius)`: 슬라임 착지 외 (blast / 발사체 / 디버그 도구) 도 직접 carve 할 수 있는 generic entry. impact-speed 매핑 없이 raw depth/radius. `HandleLanded` 는 settings → depth/radius 매핑만 거친 뒤 `Carve` 로 위임 (외부 동작 회귀 0).

### 슬라임 측 blend (`SlimeController`)

- 새 필드: `hostSurfaceMod` (host SurfaceTag → modifiers, attach 시 캐시), `cachedSnowField` + `cachedSnowMods` (snow 측 pre-resolved, attach 시 캐시), `snowDepthRatio01` (per-FU sampler 결과).
- `UpdateSurfaceModifiers(col)`: 기존 host SurfaceTag 빌드 후 SnowFieldRegistry 조회 → 캐시 업데이트 → `RefreshSnowBlend()` 호출.
- `RefreshSnowBlend()`: contact 점에서 depth 샘플 → `SnowSettings.depthToSnowBlend` curve 평가 → `SurfaceModifiers.Lerp(host, snow, t)`. `useInertialSlide` / `overrideHopMomentum` / `overrideGroundDash` / `overrideCeilingAngle` / `disableDashFalloff` 등 discrete 필드는 host 값으로 pin (mid-attach 모델 flip 방지).
- 매 FixedUpdate, 같은 host 상에서 attached/charging 이고 snow 캐시가 있으면 `RefreshSnowBlend()` 재호출 → carve / recovery 진행이 slide gravity 에 즉시 반영.

### 점프 파워 nerf

`SnowSettings.depthToLaunchScale` (AnimationCurve, default flat 1.0). `SlimeController.Launch.cs` 의 `power = ...` 다음 줄에서 `power *= depthToLaunchScale.Evaluate(snowDepthRatio01)`. flat 1.0 = 효과 0 (회귀 없음); 디자이너가 `(1, 0.6)` 등으로 끝점을 내리면 깊이 묻힌 곳에서 점프 약해짐.

### `SurfaceModifiers.Lerp` / `AngleScaledValue.Lerp`

snow 결합의 부산물로 두 helper static 추가. `SurfaceModifiers.Lerp` 는 모든 `AngleScaledValue` 필드를 component-wise lerp (`AngleScaledValue.Lerp` 가 baseValue/fullValue/angleMin/angleMax 4 scalar lerp), float 필드 lerp, bool/curve 는 t≥0.5 snap. 사용처는 현재 슬라임의 snow blend 한 곳뿐이지만 향후 다른 layered surface (얼음/이끼 위에 눈) 도 동일 패턴 재사용 가능.

### Plow drag (슬라임이 눈을 밀고 들어감)

벽 타고 내려와 눈에 박혔을 때 "눈이 밀리며 멈춰야" 하는 동작 — surface tag blend 만으론 부족 (slide friction 의 선형 감속이라 고속 진입 시 즉시 stop 안 됨). 별도 메커닉.

매 FixedUpdate, 슬라임 위치의 `SnowDepthRatio01` 이 `plowMinDepthRatio` 이상이면:

1. **지수 감쇠**: `decay = exp(-plowDragK * depth * dt)` 곱이 `slideVelocity`, `crawlVelocity`, `velocity` (Attached / Charging) 또는 `velocity` (Airborne) 에 적용. 깊이 × 계수가 클수록 빨리 정지.
2. **연속 carve**: `field.Carve(slimePos, plowCarveDepthPerSecond * dt, plowCarveRadius)` — 슬라임이 지나는 자리에 실시간 자국. 반경은 SnowSettings 의 별도 필드 (슬라임 콜라이더 반지름과 무관, 디자이너가 트렌치 폭을 자유롭게 조정). 시각적으로 "눈을 밀고 가는" 효과 + **자기-제한**: 자기가 판 자리는 depth 떨어져 drag 도 약해지므로 영원히 멈춰 있지 않음 (recovery 가 다시 묻고 → drag 다시 작동).
3. **Stop snap**: `|v| < plowStopSpeed` 면 0 으로 클램프 → 박힘. asymptote twitch 방지. **Snap 자체는 `1 - decay >= 0.05` (= K·depth·dt 가 의미 있는 크기) 일 때만 fire** — slope-fade 로 K 가 거의 0 인 경우 (수직 벽) gravity 가 `slideVelocity` 를 계속 펌프하는데도 snap 이 매 프레임 0 으로 죽이는 freeze 버그 방지 (특히 inertial slide 모드에서).

State 별 hook:
- **Attached / Charging**: `cachedSnowField` 재사용, contact 점 (`rb.position - surfaceNormal * slimeRadius`) 에서 sample.
- **Airborne**: surfaceCollider 없으므로 `Physics2D.OverlapCircleNonAlloc(rb.position, slimeRadius, ..., groundLayer)` 로 host 검출 → registry 조회. 99% 의 airborne frame 은 host 없어 즉시 return.
- **Stunned**: skip (이미 lockout).

새 SnowSettings 필드:

| 필드 | 기본값 | 설명 |
|------|--------|------|
| Plow Drag K | 4 | 풀-burial 에서 1/s 단위 지수 drag rate. 0 = plow 비활성. 4 ≈ 0.75s 안에 95% 감속. 실제 K = `plowDragK * slopeToDragScale.Evaluate(surfaceAngleDeg)` (아래 참조). |
| Slope To Drag Scale | (0,1)→(60,1)→(90,0) | 컨택트 노멀의 수평 대비 각도(0° = 평지, 90° = 수직 벽)에 따른 plowDragK 배율. `slopeToHeightScale` 와 동일 패턴. 기본값은 60° 까지 풀 drag, 90° 에서 0 — 거의 수직인 면에서는 눈이 비주얼로만 남고 슬라임을 잡지 않음. |
| Plow Carve Depth Per Second | 0.3 | 이동 중 자동 carve 깊이 (m/s). 0 = drag 만, 시각 자국 없음. |
| Plow Carve Radius | 0.25 | 연속 carve 반경 (m). 슬라임 콜라이더와 독립 — 트렌치 폭을 디자이너가 직접 조정. |
| Plow Min Depth Ratio | 0.1 | 이 미만 depth 는 drag 0 — 자연 bare 영역 (carve 자국 / patch edge) 에서 자유. |
| Plow Stop Speed | 0.5 | 이하 속도는 0 으로 snap. m/s. |

### 디버그: `SnowMouseDebug`

런타임 컴포넌트 (별도 GO 에 부착). 좌클릭 (또는 hold) 시 cursor 아래 host collider 를 OverlapPoint 로 찾고 SnowFieldRegistry 조회 → `field.HandleLanded(world, debugImpactSpeed)` 호출. 별도 carve 시스템이 아니라 슬라임 착지와 동일한 path 재사용 → recovery 도 자동. 인스펙터: `targetCamera`, `debugImpactSpeed` (default 5), `hostLayerMask`, `stampWhileHeld`, `useRightButton`, `drawCursorGizmo`.

### 동작 불변식 (Phase 7 갱신)

1. **비주얼 측 write 는 여전히 격리**: snow 가 collider 를 만들거나 event 를 push 하지 않음. 슬라임 충돌 / 상태 머신은 snow 쪽으로 흘러가는 데이터 없음.
2. **Gameplay → Snow read 만 허용**: `SnowFieldRegistry.SampleDepthRatio01` (또는 `SnowField.SampleDepthRatio01` 직접 호출) 단 한 채널. 다른 곳에서 snow 내부 상태 (heightCurrent[], strips, settings) 를 직접 만지지 않음.
3. 나머지 invariants (호스트 비파괴, idempotent rebuild, 자식 GO 만 spawn/destroy) 그대로.

### 새 SnowSettings 필드

| 필드 | 기본값 | 설명 |
|------|--------|------|
| Snow Surface Material | None | 슬라임이 눈 위에 있을 때 host SurfaceTag 와 blend 될 SurfaceMaterial. 보통 `Assets/Settings/SurfaceMaterials/Snow.asset`. null = 결합 비활성. |
| Depth To Snow Blend | (0,0)→(1,1) linear | X = 남은 두께 비율 (0 bare, 1 풀), Y = snow material blend 가중치. 좌측 평탄 = 일정 깊이 이상부터만 효과 (step). |
| Depth To Launch Scale | flat 1.0 | X = depth, Y = 점프 파워 multiplier. flat = 효과 없음. 우측 0.6 등으로 깎으면 깊이 묻힌 곳에서 점프 약해짐. |

## heightMax / Splash / RaiseLocal (Phase 9)

[Snowball](../mechanisms/snowball.md#snowfield-상호작용-phase-9) 의 착지 splash 연출 (분화구 + 옆 둔덕) 을 위해 도입. `heightCurrent` 가 `heightRest` 위로 일시적으로 솟을 수 있도록 시스템 확장. 기존 `Carve` / `HandleLanded` 호출 사이트 (슬라임 착지, 그라나잇 블래스트 임팩트) 는 영향받지 않음 — `Splash` 는 신규 API.

### 데이터 모델 변화

- **`heightMax = heightRest × SnowSettings.heightMaxMultiplier`** (per-vertex, 메모리 저장 안 함 — 매번 계산). raise 의 절대 천장. `multiplier == 1` 이면 raise 사실상 비활성 (기존 동작과 동일).
- **`heightCurrent` 의 가능 범위**: 기존 `[0, heightRest]` → 확장 `[0, heightMax]`. 셰이더는 `heightCurrent` 를 raw 값으로 받아 vertex displacement 만 하므로 `> heightRest` 도 자동 렌더 (정규화 비율 사용 안 함). carve mask (`1 - h/heightRest`) 는 ratio > 1 케이스에서 음수가 되어 footprint 효과가 안 보일 수 있는데, raise 영역은 footprint 의도가 없으므로 OK.

### 신규 API

- **`SnowStrip.RaiseLocal(localPos, raise, radius, profile, heightMaxMultiplier) → bool`**: `CarveLocal` 거울. 동일 falloff profile 분기 (Gaussian / RaisedCosine / SphericalCap / Parabola), 동일 AABB pre-filter. 차이는 `heightCurrent[v] = min(heightRest[v] × heightMaxMultiplier, heightCurrent[v] + raise * falloff)`. `heightRest[v] == 0` (taper 가장자리, 바깥쪽) 인 vertex 는 cap=0 이므로 raise 적용 안 됨 — 둔덕은 눈이 있는 곳에만 솟음.
- **`SnowField.Splash(world, dipDepth, dipRadius, ringRaise, ringRadius)`**: `Carve` 와 동일 명시 인자 패턴. 각 strip 에 대해 `CarveLocal`(중앙 분화구) + `RaiseLocal`(외곽 둔덕) 둘 다 호출. 둘 중 하나라도 변경되면 `activeStrips.Add` 로 recovery 추적. dipRadius < ringRadius 일 때 분화구 + 가장자리 둔덕 형태 — RaiseLocal 의 falloff 가 중심에서 0 이 아니라 dent 를 일부 채우지만 dipDepth 가 ringRaise 보다 크게 잡혀 있으면 분화구가 살아남음.

### 양방향 TickRecovery

`SnowStrip.TickRecovery(dt, accumulationRate, settleRate)` — 시그니처에 `settleRate` 매개변수 추가 (Phase 9). 동작:

| `heightCurrent[v]` 와 `heightRest[v]` 관계 | 동작 |
|---|---|
| `cur < rest` (도랑 — 카브됨) | `cur += accumulationRate × dt`, cap rest. 기존 동작. |
| `cur > rest` (둔덕 — raise 됨) | `cur -= settleRate × dt`, floor rest. 신규. |
| `cur == rest` | no-op. |

리턴값: 어떤 vertex라도 `heightRest` 와 다르면 true (strip 이 다음 프레임에도 tick 받아야 함). `accumulationRate == 0 && settleRate == 0` 이면 즉시 false 반환 (전 vertex 정지). `settleRate == 0` 이면 둔덕은 영구 유지 — designer 가 stage 위에서 raise 가 누적되어 푹신해지는 emergent 텍스처를 의도할 때 유용.

### Phase 9 SnowSettings 필드

| 필드 | 기본값 | 설명 |
|------|--------|------|
| Height Max Multiplier | 1.5 | `heightMax = heightRest × this`. raise 의 절대 천장. 1.0 = raise 비활성, 1.5 = 50% 마진. |
| Settle Rate | 0 | m/s. `cur > rest` 일 때 rest 까지 가라앉는 속도. 0 = raise 영구 유지, >0 = 천천히 가라앉음. |

### 호출자 (Phase 9 시점)

- **Snowball 착지** ([`ResumeRollingFromLanding`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/Snowball.cs)) — 절벽 낙하 후 SnowField 에 다시 착지하는 순간 `field.Splash(...)` 한 번 호출.
- 그 외 호출자 없음 (현 시점). 향후 슬라임 hard-landing 이나 그라나잇 블래스트가 `Splash` 로 마이그레이션할 수 있지만, 현재는 기존 `Carve` 채널 그대로 사용 — 비주얼 회귀 회피.

## 미구현 (Phase 4 이후)

> Phase 4+ 는 Phase 2/3 의 mesh / vertex / fragment 셰이더 contract 를 그대로 두고 heightRest 결정 로직 (Phase 4) · neighbour 시뮬 (Phase 5) 만 갈아끼우는 추가 작업이다. Phase 1 ↔ Phase 2 사이 같은 mesh-format 재설계는 더 일어나지 않을 예정.

### Phase 4: 지붕 occlusion

- 시작 시 (또는 N프레임마다) 각 vertex에서 위로 `Physics2D.Raycast` → `ceilingMask` 충돌 거리에 따라 `heightRest[v]` 결정
- 기존 `SnowCameraEmitter.emitterArea` (PolygonCollider2D) 와 occlusion 영역 공유 가능

### Phase 5 — 슬로프-기반 heightRest modulation (rebuild-time)

디자이너 의도 — *경사 가파른 부분은 눈이 얇게* — 를 **rebuild 시점의 곡선 기반 modulation** 으로 처리. 런타임 diffusion (cellular automata) 은 cliff 부풀림 / 변화 속도 트레이드오프가 어렵고 디자이너 의도와 동떨어진 결과를 내서 코드 자체에서 제거.

- `SnowSettings.slopeToHeightScale` AnimationCurve. X = 호스트 표면 각도 (deg, 0=평지, 90=수직 벽), Y = `heightRest` multiplier.
- `SnowField.RebuildNow` 가 edge taper 직후 각 vertex 의 averaged normal 로 `angleDeg = acos(normal.y)` 계산해 curve 평가, `heights[i] *= curve.Evaluate(angleDeg)`.
- Default keyframes: `(0,1) (18,1) (45,0) (90,0)` — 18° 까지는 풀, 18~45° 부드럽게 감소, 45° 이상 0. 기존 hard-coded smoothstep 과 거의 동일한 동작 (호환).
- 디자이너가 곡선 변경 시 OnValidate → ScheduleRebuild → 자동 재빌드.
- **알려진 한계**: 평지 메쉬여도 vertex normal 평균값이 약간 비스듬하게 나올 수 있음 (jagged TerrainShape2D). 그런 곳에서 그래프 좌측 (낮은 각도) 을 평탄화하면 (예: `(0,1) (40,1) (45,0)`) 평지 손실 최소화 가능.

### Phase 6 (open): Differential deformation

- "스쳐 지나가는" 슬라이딩 carve를 landing carve와 다르게 처리할지 미결정
- 옵션 A: stampOnCrawl 패턴 (가벼운 carve)
- 옵션 B: 슬라이딩은 carve 안 함 (현재 leaning)
- Phase 2 carve가 게임필 잡힌 후 결정

## 파라미터 (현재)

### SnowSettings (`Assets/Settings/Snow_Default.asset`)

**모든 snow tuning 이 한 자산에 모임** — 여러 SnowField 가 같은 자산을 참조해 동시 튜닝 가능. `Tools → SlimeJumping → Snow → Create Default Settings Asset` 메뉴로 생성.

| 그룹 | 필드 | 기본값 | 설명 |
|---|---|---|---|
| Deposit | Max Snow Slope Deg | 45 | face normal y ≥ cos(this) 인 face 만 패치 채택 |
| | Min Patch Tris | 3 | 이 미만 connected component 는 노이즈로 drop |
| Mesh | Rest Thickness | 0.25 | vertex normal 방향 perpendicular 두께 (m) |
| | Edge Taper Hops | 1 | boundary 로부터 풀 두께까지의 graph hop 수 |
| | Subdiv Levels | 1 | barycentric 세분화 단계 (0 = host 그대로, 3 = 64× verts) |
| Render | Snow Material | None | 자식 SnowStrip MeshRenderer 에 할당될 머티리얼 템플릿 (`Ground_Quibli_Snow.mat`). 실제 할당은 SnowField 가 rebuild 시 만든 **runtime clone** — 아래 텍스처/키워드 SnowSettings 필드들은 그 clone 에만 push 되어 원본 .mat 자산은 read-only 유지. |
| | Shadow Ramp | None | Quibli 의 NdotL toon ramp (`_GradientRamp`) 슬롯에 push. blue→white 그라데이션 텍스처를 할당하면 그늘 부분에 푸른 톤. null 이면 머티리얼 자산의 기본값 유지. |
| | Footprint Albedo | None | Carve 영역 detail 블렌드용 (`_FootprintAlbedo`). **Grayscale shape** — `.r` 채널만 사용 (흰=내부 눈 노출, 검=원래 표면). 색은 아래 Footprint Tint 에서. `_SNOW_FOOTPRINT` 키워드 켜야 효과 있음. |
| | Footprint Tint | (0.65, 0.78, 0.95) | 내부 눈 색 (`_FootprintTint`). base lighting 과 곱해져 toon ramp 음영 보존. 청백색 권장. |
| | Glitter Noise | None | Glitter sparkle 노이즈 (`_GlitterNoise`). Perlin 등 고대비 노이즈 텍스처 할당. `_SNOW_GLITTER` 키워드 켜야 효과 있음. |
| Phase 3 effects | Footprint Enabled | false | `_SNOW_FOOTPRINT` 키워드 토글 (runtime material clone 에 EnableKeyword/DisableKeyword) |
| | Fresnel Enabled | false | `_SNOW_FRESNEL` 키워드 토글 |
| | Glitter Enabled | false | `_SNOW_GLITTER` 키워드 토글 |
| Carve | Min Impact Speed | 1.5 | 이 미만 임팩트는 carve 안 함 (m/s) |
| | Depth Per Impact | 0.012 | dip 깊이 계수 (m per m/s) |
| | Depth Max | 0.20 | dip 깊이 상한 (m) |
| | Radius Per Impact | 0.05 | dip 반경 계수 (m per m/s) |
| | Radius Max | 0.8 | dip 반경 상한 (m) |
| Recovery | Accumulation Rate | 0.05 | 회복 속도 (m/s) — heightCurrent → heightRest |

### SnowField (호스트 GO 에 부착)

per-instance reference 만 — tuning 은 settings asset 으로.

| 인스펙터 | 기본값 | 설명 |
|---------|--------|------|
| Settings | None | `SnowSettings` 자산 참조. null 이면 baked-in 기본값 |
| Source Mesh Filter | auto | 호스트 메쉬. 같은 GO 또는 자식에서 자동 resolve |
| Host Collider | None | 슬라임 SurfaceCollider 와 매칭될 호스트의 Collider2D — registry 키 |
| Exclude Overlap With Other Platforms | true | rebuild 시점에 각 snow tri 가 (hostCollider 가 아닌) 다른 Collider2D 와 겹치면 그 tri 를 drop. 다른 플랫폼 mesh 와 겹쳐서 z-fighting 나는 영역이 사라지고, 외곽 rim 만 남음. boundary 는 잘라낸 모양에 맞게 재생성되어 BFS taper 가 새 가장자리에서도 둥글게 내려감. drop 알고리즘은 `Mask Mode` 로 선택 |
| Mask Mode | SampleBased | drop 알고리즘 선택. **SampleBased**: 4-sample (3 verts + centroid) 중 하나라도 foreign 안이면 tri 통째 drop. 빠르고 기존 동작 유지하지만 정점 공유로 인한 캐스케이드로 갭이 foreign 실제 footprint 보다 넓어짐. **GeometricClipping**: Sutherland-Hodgman 으로 tri 를 foreign 엣지에서 정확히 잘라 `(tri ∩ NOT foreign)` 만 남김. 캐스케이드 없음, rim 이 foreign 엣지에 정확히 안착. 지원: Box / 단일-path 볼록 Polygon. Fallback: Circle/Capsule/Edge/Composite, 다중 path / concave Polygon, "foreign 이 단일 tri 안에 완전히 포함" 케이스. 기존 씬 byte-identical 동작 유지를 위해 default 는 SampleBased — 캐스케이드 갭이 신경 쓰이는 SnowField 만 인스펙터에서 전환 |
| Overlap Layer Mask | Everything | 위 toggle 이 켜져 있을 때 검사할 레이어. hostCollider 는 항상 제외 |
| Overlap Priority | 0 | 두 SnowField 가 XY 로 겹칠 때 tiebreak. 높은 priority 가 겹친 영역의 snow 를 가져감 |
| Mask Uses Z | false | overlap mask 가 Z 깊이를 고려하는지. true 면 foreign collider 의 world Z 가 snow tri 의 world Z 와 ±`maskZWindow` 안에 있을 때만 mask 발동. **Z 깊이가 있는 큰 mesh 에서 앞쪽-Z 슬라이스는 foreground 에 의해 mask 되고, 뒤쪽-Z 슬라이스 (foreground 뒤 void 영역) 는 같은 SnowField 안에서 그대로 snow 유지**. false (기본) 면 XY 만 보고 모두 mask — 기존 동작 |
| Mask Z Window | 0.5 | `maskUsesZ` 의 Z 허용 범위 (m). 작을수록 엄격 (정확히 같은 Z 의 foreground 만 mask), 클수록 느슨. `maskUsesZ=false` 면 무시 |
| Manual Keep Colliders | empty | overlap mask 가 너무 적극적으로 잘라낸 seam 갭을 메우는 override 영역 리스트. 각 Collider2D 와 snow tri 가 영역을 공유하면 그 tri 가 *복원* 됨. **풀-precision 지원**: PolygonCollider2D, BoxCollider2D (rotated 박스도 4 corner 추출). **Cheap-fallback**: Circle/Capsule/Edge/Composite 는 'tri 정점이 collider 안' 만 봄 → 부분 overlap (collider 가 tri 한가운데를 가로지르지만 정점은 안 들어가는 케이스) 은 복원 못 함. **출력은 tri 단위** — collider 가 tri 의 일부만 덮어도 tri 전체 복원, 더 정밀한 외곽 필요하면 `subdivLevels` 를 올림. 사용법: 빈 GO 에 원하는 collider 그려 갭 모양 잡고 → `Is Trigger = true` 로 (안 그러면 자기 자신이 foreign mask 가 됨; 본 컴포넌트가 belt-and-braces 로도 한번 더 거르긴 함) → 인스펙터 list 에 드래그. `Exclude Overlap With Other Platforms` 가 off 면 어차피 모든 tri 유지되므로 효과 없음. (이전 이름 `manualKeepPolygons` — `[FormerlySerializedAs]` 로 자동 마이그레이션) |
| Draw Patch Outlines | true | scene 기즈모: 패치 외곽 초록 line |
| Draw Vert Normals | false | per-vertex 노멀 파란 line |

### SnowStamper (슬라임 GO 에 1개 부착, TrailStamper 옆)

| 인스펙터 | 기본값 | 설명 |
|---------|--------|------|
| Slime Controller | auto | OnLanded 발신자 |
| Log Landings | false | 디버그: 매 carve 의 impact/SnowField 로그 |

depth/radius 매핑은 SnowField 가 settings 에서 읽어 처리. stamper 는 raw `ImpactNormalSpeed` 만 전달하는 thin router.

## 관련 파일

- [SnowField.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowField.cs) — 호스트 컴포넌트 + rebuild + carve dispatch + recovery
- [SnowTopSurfaceExtractor.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowTopSurfaceExtractor.cs) — face slope 필터 + connected component 분리
- [SnowPatchSubdivider.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowPatchSubdivider.cs) — barycentric 재귀 세분화
- [SnowMeshBuilder.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowMeshBuilder.cs) — un-displaced + tangent + uv2 mesh emit
- [SnowStrip.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowStrip.cs) — 자식 GO 데이터 + heightTex/MPB + Carve/Tick/Flush
- [SnowFieldRegistry.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowFieldRegistry.cs) — Collider2D → SnowField static dict
- [SnowStamper.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowStamper.cs) — 슬라임 측 OnLanded 구독자 (raw impact 만 전달)
- [SnowSettings.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowSettings.cs) — 모든 tuning 의 ScriptableObject. 여러 SnowField 가 공유
- [SnowFieldEditor.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/Snow/SnowFieldEditor.cs) — 인스펙터 + 메뉴
- [SnowSceneWirer.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/Snow/SnowSceneWirer.cs) — MainScene / SampleScene 자동 wire 메뉴
- [SnowDefaultSettingsBootstrap.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/Snow/SnowDefaultSettingsBootstrap.cs) — 기본 settings 자산 생성 메뉴
- [Ground_Quibli_Snow.shader](../../../SlimeJumping_v2/Assets/Art/Shader/Ground_Quibli_Snow.shader) — Quibli StylizedLit 클론, ForwardLit pass 의 vertex displacement + fragment polish inject
- [Ground_SnowVertexInject.hlsl](../../../SlimeJumping_v2/Assets/Art/Shader/Ground_SnowVertexInject.hlsl) — `SnowVertex` 래퍼: heightTex 변위 + carve mask 계산, `SnowVaryings` 반환
- [Ground_SnowFragmentInject.hlsl](../../../SlimeJumping_v2/Assets/Art/Shader/Ground_SnowFragmentInject.hlsl) — `SnowVaryings` struct + `SnowFragment` (footprint / fresnel / glitter post-blend), Phase 3 신규
- [Ground_Quibli_Snow.mat](../../../SlimeJumping_v2/Assets/Art/Materials/Ground_Quibli_Snow.mat) — Phase 2/3 default material; Phase 3 키워드 (`_SNOW_FOOTPRINT` / `_SNOW_FRESNEL` / `_SNOW_GLITTER`) 모두 default off
- [Snow.asset](../../../SlimeJumping_v2/Assets/Settings/SurfaceMaterials/Snow.asset) — Mud 변형 SurfaceMaterial. Phase 7 부터 `SnowSettings.snowSurfaceMaterial` 슬롯을 통해 자동 wire
- [SnowMouseDebug.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/Snow/SnowMouseDebug.cs) — Phase 7 런타임 마우스 carve 디버그 컴포넌트

## 알려진 한계 / 트레이드오프

- **Slope 컷오프의 이중 효과** — `maxSnowSlopeDeg`가 face filter (어떤 face가 패치에 속하나) + 자연스러운 boundary 위치를 동시에 결정. 너무 strict면 (예: 30°) 작은 jagged jitter face가 빠지면서 host 노출. 너무 관대하면 (예: 80°) 패치가 옆면까지 늘어나 거의 모든 vertex가 boundary로 분류 → taper로 두께 거의 0. **45° 내외가 sweet spot**, 호스트 jitter 정도에 따라 튜닝.
- **`frontInteriorJitter` 강한 host 메쉬에서 작은 노출 가능** — 아주 가파른 jagged tri는 패치 가장자리에 안 들어가 host 그대로 보임. 호스트 mesh 측에서 jitter 줄이거나 maxSnowSlopeDeg 키우면 완화.
- **두께 0 face의 z-fighting** — 현재 두께 0인 vertex는 정확히 호스트 위에 겹침. 카메라에 따라 z-fighting 가능. 미세 baseOffset 추가는 미구현.
- **다중 `SnowField` per host 미지원** — `[DisallowMultipleComponent]`. 한 호스트에 여러 눈 영역이 필요하면 호스트를 분리.

## 동작 불변식 (Trail 시스템과 동일 원칙)

1. **비주얼 전용**: 충돌/물리/표면 판정/상태 머신에 일절 영향 없음. SnowField/SnowStrip 전부 제거해도 게임플레이 픽셀 동일.
2. **호스트 비파괴**: 호스트 mesh, 호스트 transform, 호스트 collider 절대 안 건드림. SnowField가 자식 GO만 spawn/destroy.
3. **idempotent rebuild**: RebuildNow N번 호출해도 자식 개수 일정. 메쉬 자산 누수 없음.
4. **`Gameplay → Snow` 결합 금지**: 슬라임이 눈 위에 있는지 여부로 분기 금지.
