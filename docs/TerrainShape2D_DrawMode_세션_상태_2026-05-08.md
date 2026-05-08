# TerrainShape2D Draw Mode — 세션 상태 (2026-05-08)

## 목표

`TerrainShape2D` 의 outline 편집 워크플로우를 그림판 스타일로 확장. 사용자가 프리미티브(원/사각/삼각) 드래그, 폴리라인(각도 스냅), 프리핸드(RDP), Stamp(저장된 모양 배치) 로 빠르게 outline 을 만들고, **Boolean union** 으로 누적해서 복잡한 지형을 빠르게 짓도록 함. 부분 삭제(Eraser via Shift+drag), multi-loop, hole 도 v2 에서 추가.

설계 grilling 기록은 `docs/대화.md`, `docs/대화2.md` 참고. 이 문서는 구현 상태 + 다음 세션 이어붙이기용.

---

## 구현 완료 (v1 + v2)

### v1 (이전 세션)
- [x] Vendor Clipper2 (Boost license) → `Assets/Scripts/Editor/TerrainShape/Clipper2/`
- [x] 4번째 ActiveTool `Draw` 탭 추가
- [x] Sub-tools: Rectangle, Triangle, Circle, Polyline, Freehand, Stamp
- [x] Snap 토글 (Vertex/Edge/Grid, 우선순위 V > E > G)
- [x] Polyline 각도 스냅 (Shift+click, Inspector configurable)
- [x] Freehand RDP 단순화
- [x] Stamp click-to-place + cursor ghost preview
- [x] Stamp 회전 Q/E (15°/Shift 1°/Ctrl 0.1°)
- [x] ShapePreset (ScriptableObject) + 썸네일 + Tab popup picker
- [x] CommitDraw → Union → 모든 layer 에 복제
- [x] Outline/Interior 도구에서 Delete/Backspace 일괄 삭제
- [x] "Clear" 버튼 (Layers 섹션, Reset 옆)

### v2 (이번 세션)
- [x] **Hotkey 작동 + 충돌 해결**
  - `Shift+1..4` = Outline/Interior/Edges/Draw 탭 전환 (any tool, cross-focus)
  - `1-6` = sub-tool 전환 (Draw 모드 한정)
  - `Tab` = Stamp picker popup (popup 안에서만 1-9 = preset 픽)
  - `Q/E` = Stamp 회전 (15° / Shift 1° / Ctrl 0.1°)
  - Modifier mask 가 CapsLock/NumLock/FunctionKey 무시 (`MeaningfulMods`)
  - `EditorGUIUtility.editingTextField` 가드 — 텍스트 입력 중 hotkey 비활성
  - `OnSceneGUI` + `OnInspectorGUI` 양쪽에서 라우팅 → SceneView 안 잡혀도 Inspector focus 시 작동
  - StampPickerPopup 의 `IsOpen` static 채널 — popup 열렸을 때 외부 1-9 양보
  - LRU 1-9 시스템 통째로 제거 (사용자가 LRU 모름 → Tab popup 만 사용)
- [x] **Multi-loop 데이터 모델**
  - 새 클래스 `OutlineLoop { points: List<OutlinePoint> }`
  - `OutlineLayer.loops: List<OutlineLoop>` 추가
  - `OutlineLayer.outline` 은 `[Obsolete]` 레거시 필드 (마이그레이션 전용)
  - `OnEnable` 에서 자동 마이그레이션 (`MigrateLegacyOutlineIfNeeded`) — 레거시 `.outline` 비어있지 않고 `.loops` 비어있으면 `loops[0].points` 로 옮김
  - **v2 강화 invariant**: 모든 layer 가 동일한 `loops.Count` AND 각 loop 의 동일한 `points.Count` AND 동일한 ID 시퀀스 AND **동일한 XY**. layer 의 차이는 `z` 만.
  - 헬퍼 시그니처 변경:
    - `InsertOutlinePointInAllLayers(int loopIndex, int afterIdx, Vector2 pos)` — 모든 layer 에 동일 XY 삽입 (v1 의 interpolation 제거)
    - `RemoveOutlinePointInAllLayers(int loopIndex, int pointIdx)` — loop vertex < 3 시 그 loop 통째 제거. 마지막 outer 의 마지막 3 vertex 보호.
    - `TryFindOutlineLocByIdInLayer(layer, id, out int loopIndex, out int pointIndex)` — 새 헬퍼
  - `OutlineCount` = 모든 loop 의 점 수 합
  - `ComputeUnionBounds(layer, out min, out max)` — 모든 loop 의 union bbox (heightmap 용)
- [x] **Hole 지원 (winding 기반)**
  - CCW (signedArea > 0) = outer fill
  - CW (signedArea < 0) = hole
  - 별도 hole 플래그 없음. Clipper2 의 NonZero fill rule 출력의 winding 만 보존
- [x] **MeshBuilder multi-loop + hole**
  - `SampleLayerLoops` — loop 별 sample, `List<List<Vector2>>` 반환
  - `Build()` 가 front loops sample → `PolygonOps.Normalize` (Clipper2 self-union) → 정규화된 multi-loop 을 모든 layer 에 적용 (v2 invariant 덕분에 front 한 번만 정규화)
  - `BuildResult.normalizedFrontLoops` 캐시 (Editor 의 hit-test 가 사용)
  - `TriangulateFaceMultiLoop` — `polygon.Add(contour, isHole)` 로 hole punch
  - Side wall mesh — loop 별 quad strip, hole loop 의 tri winding 반전 (normal 안쪽 향함)
  - `IsPointInside(IList<List<Vector2>> loops, Vector2 p)` 오버로드 — even-odd parity (도넛 자동 처리)
- [x] **PolygonOps 리팩터** (구 PolygonUnion.cs)
  - `Union(IList<List<Vector2>> existing, IList<Vector2> drawn) → List<List<Vector2>>`
  - `Subtract(IList<List<Vector2>> existing, IList<Vector2> eraser) → List<List<Vector2>>`
  - `Normalize(IList<List<Vector2>> loops) → List<List<Vector2>>`
  - 모두 NonZero fill, winding 보존. v1 의 `UnionOrReplace` 폐기 (disjoint = drawn replace 정책 → v2 에서는 **새 outer 로 추가**)
- [x] **Editor 도구 multi-loop**
  - `DrawAllLayerOutlines` — 각 loop hue 다름 (HSV golden-ratio rotate). Outer = solid, hole = dashed
  - 새 hit-test: `TryFindClickedOutlineLoc / TryFindClickedTangentLoc / TryFindClosestOutlineEdgeLoc` 모두 `(loopIdx, pointIdx)` 페어 반환
  - `HandleOutlineTool` — 모든 loop × 모든 점 순회, vertex 이동이 모든 layer × 모든 loop 에 propagate (`MoveOutlineVertexById` / `MoveSelectedOutlineByDelta`)
  - Smooth/tangent 토글이 `(loopIdx, pointIdx)` 로 모든 layer 동기화
  - `DrawTangentHandles` — `(loopIdx, pointIdx)` 시그니처
  - `BulkDeleteSelectedOutline` — 모든 loop 의 선택 점들 grouping → loop/index 내림차순 정렬 → 안전 삭제
  - `HandleEdgesTool` — 모든 loop 의 vertex picker 표시
  - `ComputeEdgeSnap` — `EdgeDragSnap.outlineLoopIndex` 추가, 모든 loop 스캔
  - `ComputeSelectionCentroid` / `ComputeAllVerticesCentroid` — 모든 loop 순회
  - `RotateVertices` — 모든 layer × 모든 loop 회전 (v2 invariant 유지)
  - `Interior` 도구의 inside-test, `EdgesTool` 의 drag fallback inside-test 가 `_cache.normalizedFrontLoops` 사용 (multi-loop overload)
  - `SnapHelper.Resolve` — vertex/edge 양쪽 모두 모든 loop scan
  - `DrawSideWallPreview` — loop 별 quad strip 표시
  - `ClearShape` — 모든 loop 비움 (이전엔 loops[0]만 비웠음)
  - "**Normalize loops**" 버튼 추가 (Layers 섹션, Reset 옆) — Clipper2 정규화 결과로 raw 데이터 덮어쓰기. 모든 ID 새로 발급, forced edges + selection wipe.
  - `DrawHeightmapSceneOverlay` — `ComputeUnionBounds` 사용해 multi-loop bbox
- [x] **Eraser via Shift+drag**
  - `DrawingSession.subtractMode` 필드 (`EndStroke()` 가 false 로 reset)
  - Rectangle/Triangle/Circle/Freehand: MouseDown 시 `e.shift` 캡처
  - Polyline: right-click 시 `e.shift` 분기 (Shift+RClick = subtract)
  - Stamp: click 시 `e.shift` 분기
  - 미리보기 색: subtract 모드 = 빨강/주황 (`DrawDrawPreview` / `DrawFreehandRawPreview`)
  - `CommitDraw` 가 `subtractMode` 기준으로 `Union` vs `Subtract` 호출
  - 결과 outer 0 개 = commit reject + Console 경고
- [x] **Audit must-fix 적용**
  - NRE 가드: empty `loops` 상태에서 DrawActiveLayerOutlineList / DrawHeightmapSceneOverlay 안전
  - Per-layer 가드: `Insert/RemoveOutlinePointInAllLayers` 가 layer 사이 invariant 검증 후 mutate
  - Multi-loop 누락 6 곳 모두 fix (위에 이미 포함)

---

## 미구현 / 후속 작업

### 우선순위 높음
- [ ] **Save & Install multi-output** — 여러 outer loop 가 있을 때 sibling GameObject N 개로 split. 각자 MeshFilter + (옵션) PolygonCollider2D. 현재는 단일 mesh 출력 (multi-loop 들이 한 mesh 의 disjoint 영역으로 들어감, 콜라이더는 단일 path).
  - 구현 힌트: `_cache.normalizedFrontLoops` 정규화된 결과를 outer-별 그룹화 (각 outer 안에 들어가는 hole 들 묶음). 그룹마다 `BuildSubMesh(group)` 헬퍼로 sub-mesh 빌드 → sibling GameObject 생성. forced edges 는 두 endpoint 가 같은 그룹 안일 때만 보존 (cross-group drop + Console 경고).
  - 위치: `TerrainShape2DEditor.SaveAndInstall` 메서드.

### 우선순위 중간
- [ ] **AutoSubdivide multi-loop** — 현재는 첫 outer loop 안에서만 sample. 여러 outer 또는 hole 영역에 대해 작동시키려면 samplers (Grid/JitteredGrid/PoissonDisk/Halton/Adaptive) 가 `IList<List<Vector2>>` 받도록 확장 + `AcceptCandidate` 가 multi-loop `IsPointInside` 사용.
- [ ] **TerrainShape2DZPainterWindow multi-loop bbox** — 헤이트맵 painter 가 여전히 `loops[0]` 만 봄. `ComputeUnionBounds` 로 교체 필요.

### 우선순위 낮음 (polish)
- [ ] DrawActiveLayerOutlineList Inspector 표시가 loop[0] 만. multi-loop 표시로 확장하면 사용자가 hole 의 vertex 수치도 인스펙터에서 편집 가능.
- [ ] `LoopXY` (Editor) 가 Repaint 마다 List 할당 → GC pressure. SignedArea 직접 계산하는 오버로드 추가.

---

## 알려진 제약 (정책)

- **Loop 별 다른 material 불가** — 한 mesh 한 material. 다른 material 필요하면 Save & Install 후 GameObject 수동 분리.
- **Smooth tangent 가 Draw commit 후 사라짐** — commit 시 모든 새 vertex 가 corner. commit 후 Outline 도구로 다시 토글 (v1 정책 유지).
- **Hole vertex 가 outer 밖으로 빠지는 weird 상태** — raw 데이터 자유, mesh 는 Clipper2 정규화 결과로 그려짐 (live preview). 사용자가 명시적 정리 원하면 "Normalize loops" 버튼.
- **Forced edge cross-loop** — 편집 중 허용 (TriangleNet 가 처리). Save & Install split 시점에는 cross-group drop 정책이지만 현재 multi-output 미구현이라 검증 안 됨.

---

## 파일 맵

### 수정/추가된 파일
```
SlimeJumping_v2/Assets/Scripts/
├── Platforms/TerrainShape/
│   └── TerrainShape2D.cs                            (수정: OutlineLoop, OutlineLayer.loops, multi-loop 헬퍼들, ComputeUnionBounds, MigrateLegacyOutlineIfNeeded)
└── Editor/TerrainShape/
    ├── TerrainShape2DEditor.cs                      (대량 수정: Draw 탭 + 멀티-루프 도구 + 단축키 + Eraser)
    ├── TerrainShape2DMeshBuilder.cs                 (수정: Build 가 multi-loop 정규화, TriangulateFaceMultiLoop, side wall 윈딩 swap, IsPointInside 오버로드)
    ├── TerrainShape2DZPainterWindow.cs              (수정: 단일-loop 으로 임시 갱신, multi-loop bbox 미구현)
    ├── ShapePreset.cs                               (v1 신규: ScriptableObject + 썸네일 생성기)
    ├── Drawing/
    │   ├── PolygonOps.cs                            (v2 신규, 구 PolygonUnion.cs 폐기: Union/Subtract/Normalize multi-loop)
    │   ├── DrawingShapes.cs                         (v1 신규: 프리미티브 polygon 생성 + RDP)
    │   ├── DrawingSession.cs                        (v1 신규 + v2 subtractMode 추가)
    │   ├── SnapHelper.cs                            (v1 신규 + v2 multi-loop scan)
    │   └── StampPickerPopup.cs                      (v1 신규 + v2 IsOpen + Alpha1-9)
    └── Clipper2/
        ├── Clipper.cs                               (v1 신규: Cinemachine 사본 vendor)
        ├── Clipper.Core.cs                          (v1 신규)
        └── Clipper.Engine.cs                        (v1 신규)
```

### 신규 데이터
- `Assets/ShapePresets/*.asset` — 사용자가 저장한 ShapePreset (각 .asset 안에 썸네일 sub-asset 포함)
- `Assets/HeightmapPresets/*.asset` — v1 의 HeightmapPreset (변경 없음)

---

## v2 핵심 invariant (꼭 알 것)

1. **layer XY 동기 invariant**: 모든 layer 가 동일한 `(loops.Count, 각 loop 의 points.Count, 각 point 의 id, 각 point 의 pos)`. layer 별 차이는 `z` 만. 어떤 mutation 도 이걸 깨면 안 됨. 새 코드 작성 시 vertex 이동/추가/삭제는 반드시 모든 layer × 모든 loop 의 같은 위치에 propagate.
2. **Outer/hole 구분은 winding 으로만**: CCW=outer, CW=hole. 별도 플래그 안 씀. Clipper2 NonZero fill 의 출력 winding 을 그대로 보존.
3. **Mesh = 정규화 결과, raw stored = 자유**: `BuildResult.normalizedFrontLoops` 가 사용자에게 보이는 것. Outline 핸들은 raw 위치에 그려짐 — 두 representation 이 다르면 weird 상태 시각 단서.
4. **Vertex ID 는 unique across all loops in TerrainShape2D**: Marquee selection / forced edge / undo 가 ID 기반.

---

## 시각 검증 체크리스트 (다음 세션 시작 시 권장 테스트)

### 기본 동작
- [ ] 새 GameObject 에 TerrainShape2D 추가 → 기본 사각형 mesh 가 보이는지
- [ ] Inspector 의 Draw 탭에서 1-6 키로 sub-tool 전환되는지
- [ ] Shift+1..4 로 Outline/Interior/Edges/Draw 탭 전환되는지 (Inspector focus 상태에서도)
- [ ] Tab 키 → Stamp picker popup 열리는지. Popup 안 1-9 = N번째 항목 선택

### Multi-loop & Hole
- [ ] Outer 사각형 그린 후 그 안에 Shift+drag Rectangle → 도넛 mesh (가운데 빈 영역) 생성
- [ ] Outline 도구에서 hole vertex 도 잡고 끌 수 있는지
- [ ] Hole 색이 outer 와 다르게 + dashed line 으로 표시되는지
- [ ] 떨어진 두 곳에 Rectangle 두 번 그리면 2 outer loop 으로 표시 (각자 다른 hue)
- [ ] Eraser 로 Outer 가 둘로 split → 2 outer loop 으로 표시 + mesh 가 두 영역
- [ ] Eraser 로 모든 영역 지우려 하면 → Console "Subtract would erase entire shape — rejected" + 데이터 그대로

### v2 invariant
- [ ] 기존 v1 .asset 열어서 정상 동작 (마이그레이션 자동, 사용자 인지 없음)
- [ ] Layer 추가 후 vertex 드래그 → 모든 layer 의 같은 vertex 가 같이 움직임 (side wall 어긋나지 않음)
- [ ] Layers 섹션의 "Normalize" 버튼 → weird 상태 정리, raw 데이터가 정규화 결과로 변함
- [ ] Layers 섹션의 "Clear" 버튼 → 모든 loop 비움, mesh 사라짐 → Draw 한 번 하면 다시 정상

### Hotkey 충돌
- [ ] Inspector 의 Layer name 텍스트 필드 입력 중 1-6 → 일반 텍스트로 입력됨 (hotkey 가로채지 않음)
- [ ] CapsLock 켠 상태에서도 1-6 작동
- [ ] StampPickerPopup 떠 있는 동안 외부 1-6 비활성

### 회귀
- [ ] Heightmap painter, Auto Subdivide (단일 outer 케이스), Save & Install (단일 outer 케이스), Layers 추가/삭제 모두 정상
- [ ] Ctrl+Z 가 Draw commit 단위로 되돌아가는지

---

## 다음 세션 진입점

1. 이 문서 (`docs/TerrainShape2D_DrawMode_세션_상태_2026-05-08.md`) 읽기
2. 검증 체크리스트 돌려서 현 상태 확인
3. 미구현 작업 중 우선순위 높은 것부터 (Save & Install multi-output 가 가장 큼)
4. 디자인 grilling 기록 필요 시 `docs/대화.md`, `docs/대화2.md` 참고

핵심 코드 진입점:
- `TerrainShape2DEditor.HandleEditorHotkeys` (line ~2900) — hotkey 라우팅
- `TerrainShape2DEditor.HandleOutlineTool` (line ~896) — multi-loop hit-test 흐름
- `TerrainShape2DEditor.CommitDraw` (line ~3130) — Union/Subtract + multi-loop 복제
- `TerrainShape2DMeshBuilder.Build` (line ~23) — 정규화 + multi-loop triangulation
- `Drawing/PolygonOps.cs` — Clipper2 wrapper API

핵심 데이터:
- `TerrainShape2D.layers[i].loops[k].points` — 새 multi-loop 데이터
- `TerrainShape2D.layers[i].outline` — `[Obsolete]` 레거시. 절대 새 코드에서 읽지 말 것.
