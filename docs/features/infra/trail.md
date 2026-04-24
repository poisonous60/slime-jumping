# 진행 트레일 (Progress Trail)

"항아리 게임" 스타일의 진행 시각화 — 슬라임이 지나간 경로가 지면에 녹색 자국으로 누적. 수십 시간 등반 세션에서도 성능 저하 없음.

## 동작 불변식
1. **비주얼 전용**: 충돌/물리/표면 판정/상태 머신에 일절 영향 없음. 트레일 스크립트/셰이더를 **전부 제거해도 게임플레이 픽셀 동일**. `SlimeController`에서 트레일 상태를 읽는 분기 금지.
2. **공유 에셋 보호**: `TrailBrush.mat`은 Awake에 반드시 `new Material(brushMaterial)`로 복제. 에셋 직접 수정 시 세션 간 dirty 누적 → FPS 저하.
3. **글로벌 정리**: `OnDestroy`에서 `_TrailMask`/`_TrailColor.a`/`_TrailWorldSize` 중립화. Play 종료 후 stale global이 Edit 모드 렌더링 오염 방지.
4. **단일 스탬퍼**: 슬라임에 `TrailStamper` 는 하나만. 2개 있으면 스탬프 2중 누적.

## 아키텍처
두 가지 RT를 동일한 브러시/페인트 로직으로 공유하되 샘플링 좌표계만 다름.
| 대상 | RT | 좌표계 | 바인딩 |
|------|----|-------|--------|
| 정적 지면 | **월드 RT** (씬당 1개) | `positionWS.xy` | 전역 `Shader.SetGlobalTexture("_TrailMask", ...)` |
| 움직이는/회전 플랫폼 | **로컬 RT** (플랫폼당 1개) | `mul(_LocalTrailW2L, positionWS).xy` | per-renderer MPB |

**라우팅**: 슬라임이 닿은 `SurfaceCollider`를 키로 `TrailColliderRegistry` 조회. 없으면 월드 RT fallback.

## 컴포넌트
| 컴포넌트 | 역할 |
|---------|------|
| `WorldTrailManager.cs` | 월드 RT 소유 + 글로벌 바운즈/컬러/Z-fade publish. 씬당 하나. `[RuntimeInitializeOnLoadMethod]`로 `Instance` 리셋. Awake에서 TrailBrush.mat을 Material 인스턴스 복제 사용 |
| `LocalTrailReceiver.cs` | 로컬 RT 생성 + MPB 주입. `targetRenderer` 필드로 Renderer와 디커플 — 3D 기울어진 mesh는 회전 없는 부모/형제 GO에 receiver 두면 페인트 프레임이 world XY 정렬. `LateUpdate`에서 `transform.hasChanged` 감지 시 `_LocalTrailW2L` 재푸시. OnDestroy dirty면 final sync save |
| `TrailStamper.cs` | 슬라임에 부착. `OnLanded` 구독 → `ImpactNormalSpeed` → `AnimationCurve` (radius/strength) → `RouteStamp`. 연속 크롤은 `stampOnCrawl` 토글 (기본 off) |
| `TrailColliderRegistry.cs` | `Dictionary<Collider2D, LocalTrailReceiver>` static. SubsystemRegistration에 map.Clear |
| `TrailPersistence.cs` | AsyncGPUReadback + `Task.Run` + atomic rename. 라운드로빈 dirty 체크로 idle 시 디스크 I/O 0. 앱 종료/페이즈엔 `WaitAllRequests` + 동기 flush |

## 셰이더 스택
Quibli `StylizedLit`을 복제하고 **ForwardLit pass의 fragment만 래핑**. 다른 pass (outline, shadow, depth, meta 등)는 stock 그대로 — 아웃라인은 트레일 색 영향 없음.

- `Ground_Quibli_Trail.shader` + `Ground_TrailReceiverInject.hlsl` — 월드 RT, `positionWS.xy`
- `Ground_Quibli_Trail_Local.shader` + `Ground_TrailLocalReceiverInject.hlsl` — 로컬 RT, `mul(_LocalTrailW2L, positionWS).xy`. `[PerRendererData]`로 MPB override 안전
- `TrailBrush.shader` — 작은 GL.QUADS 쿼드 하나를 `Blend One One BlendOp Add`로 RT에 씀. Radial falloff

**셰이더 injection 순서 계약** (어기면 silent pink):
1. `#define REQUIRES_WORLD_SPACE_POS_INTERPOLATOR 1`을 Quibli include 전에
2. `LitForwardPass_DR.hlsl` include (StylizedPassFragment 정의)가 inject include 전에
3. `#pragma fragment TrailReceiverFragment` (stock의 StylizedPassFragment 아님)

**가드**:
- `_TrailWorldSize.xy == 0` 또는 `_TrailColor.a == 0`이면 trail lerp 스킵 (매니저 없는 씬/머터리얼 프리뷰 사고 방지).
- **Z-fade**: `|positionWS.z - center|`로 fade → 2D XY 투영이 배경 산/전경 장식 leak 방지. 기본 center=0, near=0.5, far=2.0.

## 스탬프 규칙
- **착지 전용 (기본)**: `OnLanded` 이벤트에서만 트리거. 수직 임팩트 속도(`ImpactNormalSpeed`, 플랫폼 상대)를 `landingSpeedMin/Max`로 정규화 → 두 `AnimationCurve`(radius, strength) 샘플. WrapMode `ClampForever`.
- **연속 크롤 (선택)**: `stampOnCrawl = true` 시 Attached/Charging에서 `moveStep` 간격 interpolation 포함 스탬프. `teleportCutoff`로 warp 스트릭 방지.
- **Additive 누적 + cap**: 같은 지점 반복 → 마스크값 additive, R8 UNORM 1.0 포화. `_TrailMaxMask` (0.75) 전역으로 시각 상한 별도.

## 영속성
- **파일**: `Application.persistentDataPath/trail/{saveKey}.dat` (raw R8) + `.meta` (JSON: version/resolutionX,Y/originX,Y/sizeX,Y/format).
- **SaveKey**: `world_{sceneBuildIndex or sceneKeyOverride}` / `local_{sceneBuildIndex}_{hierarchyPath or overrideId}`.
- **메타 불일치**: bounds/resolution 변경 시 로드 **거부** + 새로 시작 (침묵 재스케일링 금지). Clear Save / Revert Bounds 버튼 제공.
- **Tick 비용**: idle (dirty 없음) 시 디스크 I/O 0. Dirty면 `autoSaveInterval` (15s) 마다 `AsyncGPUReadback` → `Task.Run`으로 atomic rename. `readbackBuffers` byte[] 풀링.
- **Flush**: `OnApplicationPause(true)`/`OnApplicationQuit()` 에서 `WaitAllRequests` + 동기 `ReadPixels` + write. 종료 시 1회 hitch 허용.
- **Cancel**: `OnDestroy` → `persistence.Cancel()`. 이후 콜백은 no-op.

## 에디터 지원 (TrailEditor.cs)
- **Tools > Trail 메뉴**: Migrate Selected Materials → World / Local / Revert (Undo 지원). Validate Current Scene (stock StylizedLit 남은 Environment 렌더러, 로컬 셰이더인데 receiver 없는 렌더러, overrideId 충돌 진단). Open Save Folder, Clear Saves for Active Scene.
- **WorldTrailManagerEditor**: RT 크기/메모리 라이브 라벨, bounds/save 불일치 경고 + Clear Save / Revert Bounds (0.5s TTL 캐시). Fit Bounds to Scene Ground, Clear World / Clear All / Delete Save File.
- **LocalTrailReceiverEditor**: 머터리얼 변종 검증, Clear Trail / Delete Saved File.
- **Scene Gizmo**: 바운즈 직사각형 (월드=녹색, 로컬=시안) + RT 크기/메모리 라벨. 캐시로 per-repaint 할당 0.

## 핵심 파라미터

### WorldTrailManager
- **Bounds**: `worldMin` (-50, -50), `worldSize` (100, 200), `pixelsPerUnit` 20.
- **Appearance**:
  - **(A) Trail Gradient**: `trailGradient` (HDR, mask 0→1 매핑), `trailMaxMask` 0.95 (saturation cap).
  - **(B) Position Gradient** (optional): `positionGradient`, `positionGradientCenter`, `positionGradientSize` 20, `positionGradientAngle` 0° (세로).
  - **(C) Angle Gradient** (optional): `angleGradient` — t=0 수평 바닥, 0.5 수직 벽, 1 천장/오버행. 매핑 `t = (1 - normalWS.y) * 0.5`.
  - 세 개는 곱셈 — (A)×(B)×(C).
- **Z-plane fade**: `trailZCenter` 0, `trailZNear` 0.5, `trailZFar` 2.0.
- **Persistence**: `autoSaveInterval` 15s, `sceneKeyOverride` "".

### LocalTrailReceiver
- `localMin` (-1.5, -0.5), `localSize` (3.0, 1.0), `pixelsPerUnit` 20. RT 해상도 = `localSize × lossyScale × PPU` (512² clamp).
- `contactCollider`, `targetRenderer` (auto), `overrideId` (GUID 권장 — rename/reparent 안전).

### TrailStamper
- **Wiring**: `slimeController`, `slimeCollider`.
- **Landing**: `landingSpeedMin` 0, `landingSpeedMax` 15, `landingRadiusCurve`, `landingStrengthCurve`.
- **Continuous** (기본 off): `stampOnCrawl`, `moveStep` 0.12, `moveRadius` 0.25, `moveStrength` 0.18.
- **Safety**: `teleportCutoff` 3, `logLandings`.

## 금지 사항
- **Gameplay → Trail 결합 금지**: "트레일 있는 곳은 더 미끄럽다" 같은 반영은 설계 경계 위반.
- **RT를 gameplay 상태로 읽기 금지**: 트레일 RT는 write-only (gameplay 관점). 읽는 건 오로지 지면 셰이더.
- **공유 브러시 material 에셋 수정 금지**: Material은 Awake에 인스턴스 복제 필수.
