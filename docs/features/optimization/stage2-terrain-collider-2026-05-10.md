# Stage2 터레인 Collider 성능 최적화 — 2026-05-10

> **문서 목적**: 이 세션(2026-05-10)에 적용한 변경, 만든 분석 도구, 그리고 **아직 답을 모르는 의문**을 다음 세션에서 이어 조사할 수 있게 정리한다. Claude Code의 새 세션이 이 문서만으로 컨텍스트를 복원할 수 있도록 자체 완결적으로 작성.

## 1. 개요

| 지표 | Before | After (3단계) | 개선 |
|---|---|---|---|
| median frame | 11.63ms (60fps) | **5.28ms (189fps)** | +215% |
| **mean frame** | **20.40ms (49fps)** | **6.47ms (154fps)** | **+214%** |
| p95 | 52.50ms (19fps) | 10.41ms (96fps) | −80% |
| p99 | 72.00ms | 18.41ms | −74% |
| max | 2,804ms | 56.49ms | −98% |
| `Physics2D.FindNewContacts` avgSelf | 5.48ms | **0.58ms** | **−89%** |

**증상**: Stage2에서 차징 점프 후 공중 체공 내내 부드럽지 않음. Editor와 Build 모두에서 발생.

**근본 원인 (3중 결합)**:
1. `TautLine.SyncShapes()`가 매 FixedUpdate `polygon.SetPath` 호출 → broadphase dirty
2. `Time.maximumDeltaTime` default 0.333s → stall 후 FixedUpdate catchup 폭주 (한 프레임 안에 20+ FixedUpdate)
3. 터레인 PolygonCollider2D가 230+개에 23,424 정점 — Box2D 내부 처리가 정점 수에 비례

## 2. 적용된 수정

### 2.1 TautLine `SetPath` unchanged-skip 캐시
**파일**: [`SlimeJumping_v2/Assets/Scripts/Platforms/TautLine.cs`](../../../SlimeJumping_v2/Assets/Scripts/Platforms/TautLine.cs)

`SyncShapes()`가 매 FixedUpdate(50Hz) 호출되며 `polygon.SetPath(0, polygonLocalPoints)`로 PolygonCollider2D shape를 재구성했음. 같은 데이터로도 매번 호출 → Box2D가 broadphase dirty 마킹 → AABB 트리 재밸런스.

**수정 내용**:
- `(dipDepth, riderT)` snapshot을 `lastSyncedDipDepth_riderT`에 캐시
- 다음 호출에서 `==` 비교로 스킵
- `Rebuild()`이 baseline을 바꿀 때 `syncedAtLeastOnce = false`로 invalidate
- Editor preview는 `Application.isPlaying` 체크로 캐시 우회 (디자이너 작업 시 즉각 갱신)
- Static transform invariant 주석 명시 (이동하는 부모 아래 두면 캐시 키 부족)

### 2.2 `Time.maximumDeltaTime` 캡
**파일**: [`SlimeJumping_v2/Assets/Scripts/Settings/GameOptions.cs`](../../../SlimeJumping_v2/Assets/Scripts/Settings/GameOptions.cs)

`BootstrapLoadAndApplyFrame()` 끝에 한 줄 추가:
```csharp
Time.maximumDeltaTime = 0.1f;
```

Default 0.333s는 stall 후 16+ FixedUpdate가 한 프레임에 누적되는 death-spiral을 허용. 0.1s로 캡하면 catchup이 ~5 FixedUpdate에서 끊겨 잃은 wall-clock은 drop. 프로젝트에 Animator/Timeline/PlayableDirector 사용 없어 슬로우모션 부작용 없음.

### 2.3 RDP collinear simplification (분석 도구 + Generator baking)
**신규 파일**:
- [`SlimeJumping_v2/Assets/Scripts/Editor/MeshTools/TerrainColliderOptimizer.cs`](../../../SlimeJumping_v2/Assets/Scripts/Editor/MeshTools/TerrainColliderOptimizer.cs) — EditorWindow

**수정 파일**:
- [`SlimeJumping_v2/Assets/Scripts/Editor/MeshTools/MeshToCollider2DGenerator.cs`](../../../SlimeJumping_v2/Assets/Scripts/Editor/MeshTools/MeshToCollider2DGenerator.cs) — Generate() 끝에 자동 simplify 호출

**알고리즘**: Ramer-Douglas-Peucker (RDP). 폐곡선에서는 가장 멀리 떨어진 두 정점을 anchor로 두고 두 개 open polyline으로 분할 → 각각 RDP → 머지 (seam 아티팩트 회피).

**안전 가드**:
- Whitelist: `MeshCollider2DWrapper` 컴포넌트 보유한 GameObject만 처리
- Trigger collider 제외
- 런타임 SetPath/GetPath 컴포넌트 보유 GO 제외 (`TautLine`, `DeformablePlatform`, `SnakeSpine`, `MegaDeform2DCollider`, `WaterCurrentZone`, `SnowField`)
- Undo.RecordObject + EditorUtility.SetDirty + MarkSceneDirty
- Degenerate 결과(<3 verts) 가드 — 원본 유지

**Epsilon**:
- `0.001m` (collinear-only): mesh subdivision으로 생긴 직선 위 redundant 정점 제거. 기하학적으로 zero-information loss. Slime 반경 30cm 대비 chord error 0.3% 이하 → 안전.
- `0.03m` (curve simplification): Curve 정점도 제거. Per-collider 시각 검증 필수. **이번 세션에서는 미사용** — collinear-only로 목표 달성.

**Generator baking**: `MeshToCollider2DGenerator.Generate()` 끝에 `TerrainColliderOptimizer.SimplifyClosedPath(arr, 0.001f)` 호출 추가. 향후 새 mesh import 또는 Blender re-import 후 메뉴 실행 시 자동 적용 → **durability 확보** (수동 재실행 불필요).

## 3. 분석 도구

### 3.1 EditorWindow
**메뉴**: `SlimeJumping > Terrain Collider Optimizer`

기능:
- Mode: Collinear (eps≈0.001m) / Curve (eps≈0.03m)
- "Find All Eligible In Scene" 버튼 — 자동 선택
- "Preview Selected" — SceneView에 green=원본, red=simplified 오버레이
- "Commit Preview" — Undo-가능한 적용
- 통계: 처리 가능 / 차단 / 정점 감축 비율

### 3.2 Profiler 캡처 분석 스크립트
프로젝트 루트의 `.tmp_*.cs` 파일들. `unity-cli exec`로 실행.

| 파일 | 용도 |
|---|---|
| `.tmp_load_and_analyze.cs` | Profiler `.data` 캡처 로드 후 frame distribution + top cost + slow/fast diff 종합 보고 |
| `.tmp_profiler_diff.cs` | 로드된 캡처를 median 기준 fast/slow 양분 → 차이 큰 함수 추출 (사용자 기존 도구) |
| `.tmp_profiler_sample.cs` | 마지막 30 프레임 hierarchy aggregation (사용자 기존 도구) |
| `.tmp_run_collinear_pass.cs` | EditorWindow 안 띄우고 batch로 collinear pass 실행 (자동화용) |
| `.tmp_add_wrappers.cs` | 기존 generator-created collider에 retroactive하게 `MeshCollider2DWrapper` 추가 (Stage1 같은 legacy 데이터 처리용) |
| `.tmp_deep_analysis.cs` | 개별 slow frame의 full hierarchy breakdown |
| `.tmp_collider_survey.cs` | 씬의 PolygonCollider2D 인벤토리 (사용자 기존 도구) |

### 3.3 Profiler 캡처
`SlimeJumping_v2/ProfilerCaptures/`에 보존:
- `SlimeJumping_v2_2026-05-09_15-52-41.data` — 사전 측정 (참고)
- `SlimeJumping_v2_2026-05-10_16-27-01.data` — **수정 전 baseline**
- `SlimeJumping_v2_2026-05-10_16-51-01.data` — TautLine + maxDeltaTime 적용 후
- `SlimeJumping_v2_2026-05-10_17-43-58.data` — collinear pass 추가 적용 후 (최종)

`.tmp_load_and_analyze.cs`의 `capturePath` 변수 수정해 다른 캡처 분석 가능.

## 4. 단계별 측정 데이터

### 4.1 Phase 0: Baseline (16:27 캡처)
```
median=11.63ms  mean=20.40ms  p95=52.50ms  p99=72.00ms  max=2804ms
TautLine.FixedUpdate avgTotal=0.471ms (+ DestroyShapes 0.311ms self)
Physics2D.FindNewContacts avgSelf=5.481ms
```

### 4.2 Phase 1: TautLine fix + maxDeltaTime (16:51 캡처)
```
median=6.96ms  mean=11.10ms  p95=24.18ms  p99=41.30ms  max=157ms
TautLine.FixedUpdate sub-0.1ms (top 30 진입 안 함)
Physics2D.FindNewContacts avgSelf=3.72ms (−32%)
```

### 4.3 Phase 2: + Collinear RDP simplification (17:43 캡처)
```
median=5.28ms  mean=6.47ms  p95=10.41ms  p99=18.41ms  max=56.49ms
Physics2D.FindNewContacts avgSelf=0.581ms (Phase 1 대비 −84%, Phase 0 대비 −89%)
```

### 4.4 정점 감축 통계
- 처리 전 씬 전체 PolygonCollider2D 정점: 23,424
- Phase 2 적용 후: 11,086 (−53%)
- 처리된 wrapper-marked 콜라이더: 115개 (9,245 verts → 2,519 verts)
- 안전 차단: 9개 (SnowField host) + 116개 (generator-created가 아닌 sprite-derived 등)

## 5. 남은 의문 — **다음 세션에서 조사할 주제**

> 이 섹션이 다음 세션의 출발점이다.

### 5.1 핵심 질문

**"왜 멀리 있는 collider의 정점 수가 `Physics2D.FindNewContacts` 비용에 영향을 미치는가?"**

이론적으로 Box2D의 dynamic AABB tree 기반 broadphase는:
- 슬라임 AABB와 겹치지 않는 트리 노드를 일찍 컬링
- 트리 traversal은 O(log N), N=collider 수 (정점 수가 아님)
- 멀리 있는 1,386-pt 폴리곤은 슬라임이 가까이 가기 전엔 narrowphase도 호출 안 됨

**그러나 실측은 다름**: 정점 23,424 → 11,086(−53%)에서 `FindNewContacts`가 3.72ms → 0.58ms (−84%). 정점 수에 명백히 비례.

세션 마지막에 사용자가 정확히 이 의문을 제기 (`"멀리 떨어져있는 collider가 성능에 영향을 미친다는 게 이상한데"`). Claude(이전 세션)는 가설만 제시했고 실증 못 함.

### 5.2 가설 — 검증 필요

**(a) Per-step AABB 재계산 비용**
Box2D가 매 step마다 모든 collider의 AABB를 재계산/검증한다면 정점 수에 선형 비례. 1,386 정점 폴리곤은 50 정점 폴리곤보다 27배 느림. 정적 collider라도 이 작업이 일어나면 매 frame 비례 비용.

**(b) Box2D polygon shape compilation 캐시**
Polygon shape를 내부적으로 triangle fan 또는 SAT용 normal/edge로 컴파일. 어떤 trigger(예: simulation reset, 또는 일부 frame마다 검증)에 따라 재컴파일이 일어나면 정점 수 비례.

**(c) Unity Profiler 라벨 부정확성**
`Physics2D.FindNewContacts`라는 이름이지만 실제로는 broadphase + 일부 narrowphase pair-generation + shape work를 모두 합산. 라벨이 오해를 유발.

**(d) Static AABB 트리 빌드 비용**
씬 로드 시 한 번만 빌드되는 게 아니라 일부 변경(예: 다른 collider의 SetPath, transform sync)이 트리 invalidate를 유발하면 정점-비례 빌드 비용. TautLine fix 전엔 이게 매 FixedUpdate 발생했지만, fix 후에도 잔여 trigger가 있을 가능성.

**(e) Box2D contact filter 또는 layer matrix 평가**
모든 collider를 정점 수에 비례하는 양으로 layer/material 검사를 한다면. 가능성 낮지만 배제 못 함.

### 5.3 조사 방법론

**1. Box2D / LiquidFun 소스 읽기**
Unity의 2D physics는 Box2D의 fork인 LiquidFun. Unity 6.0의 Box2D 통합 코드는 비공개지만:
- 공개 Box2D 2.4.1+ 소스: https://github.com/erincatto/box2d
- 핵심 파일: `b2_broad_phase.cpp`, `b2_dynamic_tree.cpp`, `b2_polygon_shape.cpp`, `b2_world.cpp`의 `Step()` / `FindNewContacts()`
- 특히 `b2DynamicTree::ComputeHeight()`, `b2BroadPhase::UpdatePairs()` 동작 확인

**2. ProfilerMarker 주입으로 sub-phase 분리**
`Physics2D.FindNewContacts` 라벨이 합쳐서 보여주니, `Physics2D.Simulate` 직전/직후, `Physics2D.Step` 안에 자체 마커 넣어 어느 phase에서 시간이 소모되는지 격리. Unity 자체 라벨 신뢰 말고 직접 측정.

**3. 합성 실험**
- 빈 씬에 1×1,386-pt 폴리곤 + 슬라임 → 측정
- 같은 씬에 50×30-pt 폴리곤(동일 총 정점) → 측정
- 같은 씬에 1×30-pt 폴리곤 → 측정

이렇게 변수 격리하면 (a) "정점 수 자체" vs (d) "collider 수" 비용 분리 가능. 슬라임 위치를 고정해 broadphase 쿼리 결과는 동일하게 둔 상태에서 비교.

**4. CompositeCollider2D 비교 실험**
240 polygon → 1 composite로 교체 후 측정. Composite는 broadphase 노드 1개 + 내부 SAT는 polygon 그대로 → 만약 비용이 broadphase에 있다면 큰 개선, narrowphase에 있다면 변화 없음. 가설 (a) vs (d) 분리에 결정적.

**5. Unity Physics2D.bodyCount / shapeCount 카운터 활용**
Profiler의 Physics2D 카운터에 body/shape count 등이 노출됨. 정점-비례 카운터(예: 'Total Polygon Edges' 같은 게 있으면)를 시간과 상관관계 분석.

**6. Native profiler (Superluminal/PIX/Tracy)**
Unity Profiler 라벨 신뢰 못 한다면 native CPU sampler로 Box2D 함수 실측. Unity 빌드에 symbol 포함하고 sampling profiler로 어느 함수가 실제 시간 먹는지 확인.

### 5.4 시작점 추천 (다음 세션 첫 액션)

1. `docs/features/optimization/stage2-terrain-collider-2026-05-10.md` (이 문서) 읽기
2. 메모리에서 [Stage2 terrain collider load](../../../../../C:/Users/poiso/.claude/projects/e--260301-project-SlimeJumping-v2-project/memory/project_stage2_terrain_collider_load.md) 확인
3. `.tmp_load_and_analyze.cs`로 Phase 0/1/2 캡처 비교 — 자기 눈으로 데이터 확인
4. 가설 (a) "Per-step AABB 재계산" 먼저 검증 — Box2D 소스에서 `b2World::Step()` → `Solve()` → `ContactManager::FindNewContacts` 호출 경로 따라가며 정점-비례 작업이 있는지 찾기. WebFetch로 Box2D 코드 직접 읽기.
5. 가설 검증 안 되면 합성 실험 씬 만들어 실측

### 5.5 비-목표

이 조사는 **이론적 이해**가 목적. 추가 perf 최적화는 현 시점에서 불필요 (mean 6.5ms / 154fps로 120fps 목표 초과 달성). 사용자가 이미 명시: "추가 작업은 불필요, 왜 그런지 알고 싶을 뿐".

따라서 다음 세션에서는:
- 코드 변경 X
- 새 도구 작성 X
- **조사 보고서만 작성** — 가설을 실증으로 결론 지어 이 문서 업데이트

## 6. 메타 정보

- **세션 일자**: 2026-05-10
- **Unity 버전**: 6000.3.11f1
- **참여**: Claude Code (Sonnet 4.6 추정 — 도구 결과 기반), 사용자 (poisonous60@gmail.com)
- **관련 문서**:
  - [`docs/MainScene_성능_프로파일_2026-04-24.md`](../../MainScene_성능_프로파일_2026-04-24.md) — 이전 baseline 측정
  - [`docs/SurfaceProbe_곡선_성능_분석_2026-05-09.md`](../../SurfaceProbe_곡선_성능_분석_2026-05-09.md) — SurfaceProbe 별건 분석
  - [`docs/곡선_슬라이드_stutter_튜닝_가이드.md`](../../곡선_슬라이드_stutter_튜닝_가이드.md) — faceting 관련 (이번 작업이 이 문서의 우려를 다시 떠올리게 함)
- **관련 audit 보고서**: 본 세션 안에서 두 차례 `code-audit-reviewer` agent 실행 (TautLine fix 사전 audit + 콜라이더 최적화 사전 audit). 결과는 본 문서 §2.1, §2.3에 반영.
