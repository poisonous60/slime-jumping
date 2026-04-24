# MainScene 성능 프로파일 보고서 (2026-04-24)

## 배경
프로젝트 초기 ~300fps → 현재 ~140fps 로 드롭. 에디터 상에서 원인 분포를 확인하기 위해 Unity Profiler + Frame Debugger 내부 API 를 `unity-cli exec` 경유로 샘플링.

---

## 방법론 / 제약

### Frame Debugger 덤프 시도 결과
- `UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility` (in `UnityEditor.CoreModule`) 의 내부 정적 API 를 리플렉션으로 호출 가능.
- `SetEnabled(true, 0)` → Game View repaint 강제 → `count = 162` 이벤트 캡처까지 성공.
- 단, `GetFrameEventData(i, data)` 를 **한 exec 스크립트 안에서 루프로 반복 호출**하면 응답 없이 hang. 해당 API 가 메인스레드 tick 을 요구하는데 exec 스크립트가 스레드를 점유해 데드락 발생.
- 개별 이벤트 1~2개 조회는 정상. 향후 대량 덤프가 필요하면 `Assets/Scripts/Editor/FrameDebugDumper.cs` 같은 Editor 스크립트로 구현해 메뉴 항목에서 실행(메인 루프에서 tick 사이에 호출되도록)하는 방식이 해결책.

### FPS 수치 해석 주의
- 에디터 백그라운드 상태에서는 Game View 가 throttle 되어 실측 FPS 가 왜곡됨. 본 보고서는 **per-frame CPU time(ms)** 기준으로 판단.
- 따라서 300fps / 140fps 숫자 자체도 에디터 포커스/Gizmo/EditorLoop 영향이 섞인 값임을 전제.

---

## 측정 데이터: Profiler Main Thread (30프레임 평균)

| 블록 | avgTotalMs | 빌드에서 사라지는지 | 비고 |
|---|---|---|---|
| PlayerLoop 전체 | **5.62** | - | 프레임당 실제 게임 워크 |
| EditorLoop | 3.52 | **예** | 에디터 전용 오버헤드 |
| RenderPlayModeViewCameras | 2.94 | 부분적 | 일반 Camera.Render 로 대체됨 |
| └ Inl_UniversalRenderTotal | 2.70 | 아니오 | URP 총 렌더 비용 |
| └── Inl_RenderCameraStack (calls=2) | 2.68 | 아니오 | **카메라 스택 2개** |
| └──── RenderSingleCameraInternal: Main Camera | 0.91 | 아니오 | |
| └──── RenderSingleCameraInternal: BackgroundCamera | 0.55 | 아니오 | URP Base 카메라 2개 |
| └ UpdateScene (Script & Physics) | 2.35 | 아니오 | |
| └── ScriptRunBehaviourLateUpdate | 0.95 | 아니오 | |
| └──── **WaterZone_v2.LateUpdate (calls=3)** | **0.77** | 아니오 | **유저 스크립트 1위** |
| └──── SlimeVisual.LateUpdate | 0.046 | 아니오 | |
| └──── CinemachineBrain.LateUpdate | 0.040 | 아니오 | |
| └──── WaterfallImpactVFX.LateUpdate | 0.039 | 아니오 | |
| └ ParticleSystemBeginUpdateAll | 0.42 | 아니오 | PS 28개 업데이트 |
| └ Physics2D.Simulate | 0.61 | 아니오 | FixedUpdate, 30 중 12프레임만 |
| └ Script FixedUpdate (SlimeController.FixedUpdate) | 0.055 | 아니오 | |
| GizmoManager.DrawGizmos | 0.32 | **예** | 에디터 전용 |
| GUI.Repaint + SlimeStateDebugUI.OnGUI | 0.17 | **빌드에선 제거 필요** | 디버그 UI |
| SurfaceProbeModeToggle.OnGUI | 0.08 | **빌드에선 제거 필요** | 디버그 UI |
| TranslucentImageSource.OnGUI | 0.05 | 아니오 | 외부 에셋 |

### 씬 규모
- Renderer **410개** (MeshRenderer 367, SpriteRenderer 11, LineRenderer 4, 기타 28)
- Camera **2개** (Main + Background, URP 2-카메라 스택)
- Light 1개
- ParticleSystem **28개**
- WaterZone_v2 **3개**
- Frame Debugger 이벤트 **162개** (카메라 스택 2개 합산)

---

## 회귀 원인 우선순위

### 1. 2-카메라 URP 스택 (~0.55 ms, 렌더 비용의 ~20%)
- BackgroundCamera 가 전용 URP 루프를 1회 더 통과.
- 초기 프로젝트엔 없던 구성. Quibli 기반 BG 블러 아키텍처(TranslucentImage + Base/Overlay 스택) 도입 시점부터 추가됨.
- **개선안**: 블러 옵션이 off 일 때 BackgroundCamera 를 비활성화하거나 Post-Process 가 꺼진 상태에서는 스택 자체를 1개로 축소.

### 2. 씬 규모 증가 (누적 렌더 비용)
- MeshRenderer 367 + PS 28 은 초기 프로젝트 대비 큰 증가.
- Frame Debugger 162 이벤트는 URP Base+Overlay 를 합친 숫자지만 여전히 2D 플랫포머 기준 많음.
- **개선안**: Culling 확인(카메라 밖 오브젝트가 업데이트/렌더 대상에 포함되는지). 그룹 단위 SetActive 토글 or OcclusionArea.

### 3. WaterZone_v2.LateUpdate (0.77 ms, 유저 스크립트 1위)
- 3 인스턴스 × 매 프레임 `UpdateMeshVerts()` + `UpdateOutlineLine()`.
- 위치: [WaterZone_v2.cs:251-255](../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterZone_v2.cs#L251-L255)
- **개선안**:
  - wave velocity 가 임계치 이하이고 slime이 zone 밖일 때 mesh 재생성 스킵.
  - 카메라 frustum 밖이면 LateUpdate 조기 리턴.
  - 3 × 0.26 ms → 거의 0 으로 절감 가능.

### 4. OnGUI 디버그 스크립트 (~0.25 ms, 빌드에서 제거 필요)
- `SlimeStateDebugUI.OnGUI` 0.17 ms, `SurfaceProbeModeToggle.OnGUI` 0.08 ms.
- **확인 필요**: 두 스크립트에 `#if UNITY_EDITOR` 가드 또는 ConditionalCompile 유무. 없으면 빌드에도 들어감.
- 위치: [SlimeStateDebugUI.cs](../SlimeJumping_v2/Assets/Scripts/Debug/SlimeStateDebugUI.cs), [SurfaceProbeModeToggle.cs](../SlimeJumping_v2/Assets/Scripts/Debug/SurfaceProbeModeToggle.cs)

### 5. 에디터 전용 오버헤드 (숫자 비교 자체의 착시 요인)
- EditorLoop 3.5 ms + GizmoManager 0.32 ms + OnGUI debug 0.25 ms ≈ **4 ms / frame** 이 빌드에선 사라짐.
- PlayerLoop 5.6 ms 에서 빌드 전용 비용만 남기면 **1.5~2 ms → 500+ fps 이론적 가능**.
- 즉 "140fps" 는 에디터-only 수치일 가능성이 높고, 빌드에서 재측정 시 상당히 회복될 것.

---

## 권장 액션 (큰 것부터)

1. **빌드에서 먼저 프로파일링** — 140fps 가 빌드에서도 재현되는지 확인. 재현 안 되면 회귀 체감은 에디터 한정이므로 최적화 우선순위 재평가.
2. **BackgroundCamera 조건부 비활성화** — 블러 옵션 off 일 때 스택을 1카메라로 축소.
3. **WaterZone_v2.LateUpdate 조기 리턴** — velocity/카메라 frustum 기반.
4. **Debug OnGUI 스크립트 `#if UNITY_EDITOR` 가드 확인**.
5. **Frame Debugger 자동 덤프가 상시 필요해지면** — `Assets/Scripts/Editor/FrameDebugDumper.cs` 구현(메뉴 아이템 경유). `GetFrameEventData` 를 EditorApplication.update 로 프레임당 한 건씩 iterating 하면 exec hang 문제 우회 가능.

---

## 참고: unity-cli Frame Debugger 접근 API 스니펫

```csharp
var asm = typeof(UnityEditor.EditorApplication).Assembly;
var fdUtil = asm.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility");
var evtDataType = asm.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData");

// 캡처 시작
fdUtil.GetMethod("SetEnabled").Invoke(null, new object[] { true, 0 });

// Game View repaint 강제 필요 (에디터 백그라운드 시)
var gv = System.Type.GetType("UnityEditor.GameView, UnityEditor");
var win = UnityEditor.EditorWindow.GetWindow(gv);
typeof(UnityEditor.EditorWindow)
    .GetMethod("RepaintImmediately", BindingFlags.NonPublic | BindingFlags.Instance)
    .Invoke(win, null);

int count = (int)fdUtil.GetProperty("count").GetValue(null);

// 개별 이벤트 (루프는 exec 안에서 hang — Editor 스크립트에서 호출할 것)
var data = Activator.CreateInstance(evtDataType);
fdUtil.GetMethod("GetFrameEventData").Invoke(null, new object[] { i, data });
int drawCalls = (int)evtDataType.GetField("m_DrawCallCount").GetValue(data);
string shader = (string)evtDataType.GetField("m_RealShaderName").GetValue(data);
// 기타 필드: m_VertexCount, m_IndexCount, m_InstanceCount, m_PassName,
//           m_PassLightMode, m_RenderTargetName, m_RenderTargetWidth,
//           m_RenderTargetHeight, shaderKeywords, m_BatchBreakCause, ...
```
