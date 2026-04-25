# 통과 천 (Passable Cloth)

슬라임이 **통과**하는 3D 방향의 천 커튼. 부딪치지 않고 — 대신 약한 속도 비례 드래그가 걸리고, 천 질점이 슬라임 구 형태를 따라 밀려나며 감싸는 듯한 비주얼을 낸다. 가이드 §16의 "통과 천" 컨셉 구현체.

## 동작 불변식

- **통과가 본질**: 천은 Collider2D/Collider3D 없음. `SurfaceProbe`는 천의 존재 자체를 모름. 슬라임은 항상 뚫고 지나간다.
- **3축 자유 회전**: 천은 `transform.rotation`으로 월드의 임의 방향 배치. 문짝 커튼은 `Euler(0, 90, 0)`으로 로컬 +X를 월드 +Z에 맞추고, 뒷벽 커튼은 `Euler(0, 0, 0)` 그대로. 시뮬레이션은 언제나 천의 **로컬 XY 평면**에서만 돌고, 월드 축을 전혀 모른다.
- **실제 두께**: `clothThickness`로 메쉬를 로컬 ±Z/2 로 extrude. 앞/뒤 두 레이어 + 측면 스커트. 시뮬레이션 자체는 단일 레이어(로컬 Z=0).
- **Commitment 3중 안전장치** (가이드 §16 필수):
  1. 초당 최대 감속률 캡: `maxDragPerSecond` (기본 0.4) — 천 한 장이 초당 40% 이상 속도 빼앗지 못함.
  2. 누적 드래그 클램프: `maxAccumulatedDrag` (기본 0.5) — 한 번의 통과에서 진입 속도의 50% 이상 못 빼앗음.
  3. Charging/Attached 상태 드래그 비활성: 표면 접촉·차징 중 천 드래그 0 — 가장자리 접촉 시 차징·부착 거동이 안 깨지도록.
- **구면 충돌로 천 질점 + 엣지 push-out**: 슬라임은 CircleCollider2D. 천의 관점에서는 로컬 프레임의 `(slimeX, slimeY, slimeZ)` 를 중심으로 하는 반경 `SlimeRadius`의 **구(sphere)**. 매 구속 반복마다 두 단계가 돈다 — (1) 구 안에 있는 질점을 반경면으로 외향 push, (2) **모든 structural + shear 엣지**의 슬라임 중심까지 closest point 가 반경 안이면 그 세그먼트도 외향 push. 엣지 패스가 핵심: cellSize(0.25) ≪ 구 지름(~0.8) 일 때 구 안에 들어오는 질점이 2~3 개뿐이라, 엣지 패스 없으면 인접 질점 사이의 메쉬 변이 슬라임을 그대로 가로질러 — 시각적으로 "안 부딪히는" 결과. 엣지 패스가 메쉬 표면을 슬라임 원형 윤곽 따라 wrap 시킨다.
- **바닥 충돌**: `Physics2D.OverlapCircle` + `Collider2D.ClosestPoint` 로 월드 XY 쿼리. Collider2D는 월드 Z에 무한하므로 회전된 천도 올바르게 바닥 위에 얹힘.

## 스크립트 구조

### `ClothField` (`Scripts/Mechanisms/ClothField.cs`)

단일 `MonoBehaviour`. `[ExecuteAlways] + [DefaultExecutionOrder(-100)]`로 Rope와 같은 편집 모드 미리보기 패턴.

**주요 필드** (인스펙터):

| 그룹 | 필드 | 기본값 | 의미 |
|---|---|---|---|
| Topology | `gridColumns`, `gridRows` | 10, 14 | 질점 격자 크기 (로컬 +X / -Y 축) |
| Topology | `cellSize` | 0.25 | 셀당 월드 단위 |
| Topology | `clothThickness` | 0.12 | 로컬 Z 메쉬 두께 (시뮬은 2D 유지) |
| Pins | `pinPreset` | TopRow | 상단 행 고정 (v1은 커튼 전용) |
| Solver | `constraintIterations` | 10 | Jakobsen 반복 수 (5~15 권장) |
| Solver | `damping` | 0.985 | 스텝당 속도 보존 |
| Solver | `simulationSubsteps` | 2 | FixedUpdate 당 서브스텝 |
| Solver | `structuralSoftness` | 0 | 인접 (수평/수직) 스프링 |
| Solver | `shearSoftness` | 0.15 | 대각선 스프링 |
| Solver | `bendSoftness` | 0.4 | 2-step 굽힘 스프링 |
| Forces | `nodeGravityScale` | 0.6 | `Physics2D.gravity` 배율 |
| Forces | `windAccelerationWorld` | (0.5, 0, 0) | 월드 바람 (로컬로 투영됨) |
| Forces | `windTurbulence` | 0.4 | Perlin 기반 per-node 변동 |
| Forces | `windFrequency` | 0.6 | Hz |
| Slime | `clothDragCoefficient` | 0.8 | 얇은 커튼 0.5, 두꺼운 드레이프 1.8+ |
| Slime | `maxDragPerSecond` | 0.4 | 안전장치 #1 |
| Slime | `maxAccumulatedDrag` | 0.5 | 안전장치 #2 |
| Slime | `slimeRadiusHint` | 0.4 | `SlimeSettings.slimeRadius` 와 맞출 것 |
| Slime | `thicknessGateMargin` | 0.3 | 로컬 Z 게이트 여유 |
| Ground | `nodeCollisionRadius` | 0.02 | 작게 — 바닥면 근처 resting |
| Ground | `groundLayerMask` | Environment(7) | 바닥 콜라이더 레이어 |
| Render | `material` | — | `Cloth_Unlit` 머티리얼 |

**시뮬레이션 순서** (FixedUpdate):

1. 상단 행 re-pin (로컬 rest 위치로 복귀)
2. `simulationSubsteps`회 Verlet 적분 + 구속 (`constraintIterations` 회)
3. 슬라임 구 충돌 (질점 push-out → structural/shear 엣지 push-out, 모두 로컬 프레임 3D)
4. 바닥 충돌 (`Physics2D.OverlapCircle` + `Collider2D.ClosestPoint`, 월드 XY)
5. 슬라임 드래그 (3중 안전장치 통과 시만 `slime.AddExternalVelocity`)
6. 메쉬 vertex 업데이트 + `RecalculateNormals/Bounds`

### `Cloth_Unlit` 셰이더 (`Art/Shader/Cloth_Unlit.shader`)

URP Unlit, `Cull Off` (양면 렌더링), `Blend SrcAlpha OneMinusSrcAlpha`. vertex color R 채널이 핀 마스크(0=pinned, 1=free)로 알파 감쇠에 사용. Gradient Noise로 작은 shimmer.

| 프로퍼티 | 기본 | 의미 |
|---|---|---|
| `_BaseColor` (HDR) | (0.75, 0.6, 0.7, 0.9) | 기본색 |
| `_PinAlphaFloor` | 0.85 | pinned row 알파 최저치 |
| `_ShimmerAmplitude` | 0.05 | 잔물결 진폭 |
| `_ShimmerScale` | 3.0 | UV 스케일 |
| `_ShimmerSpeed` | 0.35 | 스크롤 속도 |

## 외부 API / SlimeController 연동

- `slime.AddExternalVelocity(Vector2)` — 드래그 채널 (Water/Wind 와 동일)
- `slime.CurrentState` — `SlimeState.Charging / Attached` 판별
- `slime.Position`, `slime.Velocity` — 로컬 프레임 쿼리용
- **새 API 추가 없음**, 새 레이어 없음, 새 HazardEffect 없음

## 2.5D 카메라 geometry 주의사항

Cloth 는 **로컬 XY 평면** 에서 시뮬레이션됨. Wind / 슬라임 push-out 전부 이 평면 내에서 일어남.

- `Euler(0, 0, 0)` (기본, 카메라 facing): 로컬 XY = 월드 XY. 모든 변형이 카메라에 바로 보임. **2.5D 게임에서 권장하는 유일한 방향.**
- `Euler(0, 90, 0)` (문짝형): 로컬 +X = 월드 +Z (깊이 방향). 메커닉은 동작하지만 **로컬 X 변형은 월드 Z 방향으로 나타나 카메라 시점에서 안 보임**. 중력 평형(수직 rest)에서 벗어나려면 사용자가 보는 축(Y) 변형이 필요한데, 중력이 즉시 rest 로 당김 → 시각적 무반응.
- `Euler(0, 30~45, 0)` (중간 각도): 일부 변형은 카메라에 보이고 일부는 깊이로 들어감. "문 입구"/"복도" 느낌 연출에 적합.

요약: cloth 는 authoring 시 **width 가 월드 X 또는 적어도 X 성분이 있어야 보임**. 완전한 Z-axis 문짝은 위에서 내려다 보는 앵글에서만 의미 있음.

## 에디터 유틸

`SlimeJumping/Mechanism/Setup Cloth Test` — 3종 커튼 빌드:
- `Cloth_Light` — 카메라 facing, 얇고 가벼운 드래그
- `Cloth_Heavy` — 카메라 facing, 두껍고 무거운 드래그
- `Cloth_Angled30` — Y축 30° 회전, 깊이감 연출용

## 알려진 한계 / 적용 안 한 것

- **셀프 콜리전 / 찢김**: 가이드 §16 L855에서 "통과 천 컨셉에 불필요" 명시 — 설계 결정.
- **깃발/넝마 pin 프리셋**: v1은 커튼 전용. `PinPreset` enum 스캐폴딩만 되어 있어 나중에 추가 가능.
- **비사각 그리드**: 사각 그리드가 천 시뮬레이션의 표준 토폴로지 (가이드/연구 문서 공통).
- **Burst/Jobs**: 연구 문서 기준 200 질점 이하에서는 불필요. v1은 140 질점.
- **WindZone2D 방향 동기화**: ClothField가 자체 바람 보유. 여러 천을 씬 레벨에서 동기시키려면 WindZone2D에 방향 read-only 접근자 추가 필요 (향후).
- **로프 Verlet 공유**: Rope.cs는 그대로 두고 ClothField가 Verlet 패턴을 복사 — Rope-specific 로직(rebound, max-tension event) 이 많아 공유 베이스 추출 ROI 낮음.

## 관련 파일

- [ClothField.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/ClothField.cs)
- [ClothTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/ClothTestSetup.cs)
- [Cloth_Unlit.shader](../../../SlimeJumping_v2/Assets/Art/Shader/Cloth_Unlit.shader)
- [docs/기능_구현_가이드.md](../../기능_구현_가이드.md) §16
- [docs/클로드 연구/Unity 2D 플랫포머를 위한 천 시뮬레이션 완전 가이드.md](../../클로드%20연구/Unity%202D%20플랫포머를%20위한%20천%20시뮬레이션%20완전%20가이드.md)
