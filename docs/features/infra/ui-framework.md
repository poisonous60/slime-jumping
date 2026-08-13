# UI Framework

Screen-space UI 는 전부 `UIRoot` 프리팹 위에서 돌아간다. 월드 스페이스 UI (Memo, InteractionPrompt, KeyPromptIcon) 는 별개.

## 구성 요소

### Runtime (`Assets/Scripts/UI/Framework/`)
- `UILayer` — enum `{ Hud, Menu, Modal, Fullscreen }`. 패널이 속할 Canvas 결정.
- `UISlotType` + `UISlot` — MemoSlot 패턴 일반화. 프리팹 자식에 타입 태그만 붙이면 컨트롤러가 경로 없이 바인딩.
- `UIPanel` (abstract) — CanvasGroup 페이드 in/out, `isModal/openOnStart/openOnEscape` 플래그, `OnAfterAwake/OnAfterOpen/OnAfterClose` 훅. `[RequireComponent(CanvasGroup, RectTransform)]`.
- `UIManager` — 씬마다 하나. UIRoot 에 붙음. 4 개 레이어 Canvas 소유, LIFO 모달 스택, ESC 전역 처리, Slime 입력 게이트.

### Panels (`Assets/Scripts/UI/Panels/`)
- `MainMenuPanel` — Menu 레이어, `Start New Game/Load Game/Options/Quit` 버튼. UISlot 기반. Load Game 은 `LoadSaveManager.HasSave` 일 때만 켜지고, Start New Game 은 누를 때 세이브를 지운다. 앱 종료는 이 패널의 Quit 뿐. `instantTransitions=true` 로 연출 없이 즉시 표시.
- `OptionsPanel` — Modal 레이어, `openOnEscape=true`. GameOptions 로 모든 설정 영속화 (PlayerSfx/WorldSfx/Bgm/Ui 볼륨, FrameLimit, WindowMode, Resolution, BackgroundCamera). 기존 `OptionsPanelController` 로직 그대로 이식. 레이아웃(2026-08-13)은 탭 없는 단일 리스트 — 행마다 라벨(우측정렬) | 컨트롤(좌측정렬)이 중앙 거터를 공유. Box = `TranslucentImage` frosted 판(alpha 1 = 뒤 UI 차단, `foregroundOpacity` 0.5 로 BG 카메라 블러가 비침, 소스는 Start 자동 획득), Backdrop 딤 0.75. Debug 설정(Charge Visual/Mouse Teleport/Clear Trail)은 UI 미노출. `instantTransitions=true` 로 오픈/클로즈 연출 없이 즉시 표시. Actions 행은 `Close` + `Return to Menu`(`menuButton`, 자동저장 후 `GameFlow.LoadMainMenu`) — 메인 메뉴 씬에서 열렸을 땐 같은 씬 재로드라 숨긴다.
- `EndPanel` — Menu 레이어, 데모 종료 후 아무 키 → `GameFlow.LoadMainMenu`.

### Editor setup (`Assets/Scripts/Editor/UI/`)
메뉴: `Tools → SlimeJumping → UI → ...`
- `Build or Rebuild UIRoot Prefab` — `Assets/Prefabs/UI/UIRoot.prefab` 생성/재생성.
- `Build or Rebuild MainMenuPanel Prefab` / `OptionsPanel Prefab` / `EndPanel Prefab` — 각 프리팹을 `Assets/Prefabs/UI/Panels/` 에 생성/재생성.

재실행 시 기존 프리팹을 덮어씀 — Inspector 에서 튜닝한 값은 재빌드 시 초기화됨에 주의.

`UISceneWirer` — 원샷 마이그레이션 유틸. `WireMainMenuScene/WireMainScene/WireEndScene` 정적 메서드. 기존 Canvas 를 삭제하고 UIRoot + 필요한 패널 프리팹을 인스턴스화. `unity-cli exec` 로 호출.

## UIRoot 프리팹 구조

```
UIRoot                  UIManager, UIClickSoundInstaller
├── EventSystem         EventSystem + InputSystemUIInputModule
├── HudLayer            Canvas Overlay, sortingOrder 100,  1920×1080 Match 0.5
├── MenuLayer           Canvas Overlay, sortingOrder 500
├── ModalLayer          Canvas Overlay, sortingOrder 800
└── FullscreenLayer     Canvas Overlay, sortingOrder 9000
    ├── FlashCanvas     CanvasGroup + Image + FlashCanvasController
    └── JumpscareCanvas CanvasGroup + Image + JumpscareCanvasController
```

Canvas 는 모두 `ScaleWithScreenSize 1920×1080 Match=0.5` 단일 프리셋.

## 패널 생명주기

1. 씬 로드 시 패널 프리팹 인스턴스가 `Start()` 에서 `UIManager.Instance.Register(this)` 호출 → UIManager 가 `layer` 에 맞는 Canvas 아래로 reparent + 앵커 fill.
2. `openOnStart=true` 인 패널은 그 자리에서 열림 (MainMenuPanel, EndPanel).
3. `openOnEscape=true` 인 패널은 UIManager 가 ESC 키 감지 시 연다 (OptionsPanel).
4. Modal 패널은 열릴 때 `modalStack.Push` → `UpdateInputGate` 로 `SlimeInputReader.enabled = false`. 닫히면 Pop → enabled = true.
5. ESC: 스택이 비어 있으면 `openOnEscape` 패널을 찾아 열고, 비어 있지 않으면 최상위 모달을 닫는다.

## 슬라임 입력 게이트

`UIManager.slimeInputReader` (인스펙터 할당) 가 null 아니면, modalStack.Count 0↔1 전환 시 `SlimeInputReader.enabled` 토글. `OnDisable` 이 `InputActionAsset.FindActionMap("Player").Disable()` 호출 → Move/Jump/Aim 등 전부 정지. 슬라임 물리는 계속 돌고, 속도는 freeze 된 시점 값 유지. 게임을 pause 하지는 않는다.

Phonograph 같은 이후 패널이 "슬라임 물리까지 멈춰야 함" 라면 `IExternalController.SetOverride` 와 UI gate 를 병행한다. UI gate 는 **입력만** 막는다.

## 자동 레이아웃 규칙

프리팹은 전부 Unity LayoutGroup 조합으로 구성 — 텍스트 겹침/크기 이상 방지.

- **수직 스택**: `VerticalLayoutGroup { childControlWidth=childControlHeight=true, childForceExpandWidth=true, padding/spacing 명시 }`.
- **행**: `HorizontalLayoutGroup` + `LayoutElement.minHeight/preferredWidth`.
- **텍스트**: TMP `enableAutoSizing=false`, 고정 `fontSize`. Title 48 / Section 28 / RowLabel 22 / RowValue 22 / Button 26 / Body 20.
- **공통 폰트**: `SpoqaHanSansNeo-Bold` (Memo 프리팹과 동일, GUID `8f586378b4e144a9851e7b34d9b748ee`).

## 신규 패널 추가 절차

1. `Assets/Scripts/UI/Panels/{Name}Panel.cs` — `UIPanel` 상속, `OnAfterAwake` 에서 `FindSlot<T>(UISlotType)` 또는 `[SerializeField]` 직접 참조로 바인딩.
2. (필요 시) `UISlotType` 에 새 enum 값 추가.
3. `Assets/Scripts/Editor/UI/{Name}PanelSetup.cs` — `UISetupCommon` 헬퍼로 프리팹 계층 구성, `SerializedObject` 로 `UIPanel.layer/isModal/openOnStart/openOnEscape` 및 참조 필드 설정, `SavePanelPrefab` 저장. `[MenuItem("Tools/SlimeJumping/UI/Build or Rebuild {Name}Panel Prefab")]` 부착.
4. 메뉴 실행 → 프리팹 생성 → 필요한 씬에 드래그 (또는 `UISceneWirer` 에 추가).
5. 튜닝은 Inspector 에서.

## 범위 밖

- **World-space UI**: Memo, InteractionPromptManager, KeyPromptIcon 는 이 프레임워크 외부. 각자 기존 설계 유지.
- **SceneTransitionController**: `Resources/SceneTransition.prefab` + `DontDestroyOnLoad`. UIRoot 와 독립.
- **Debug UI**: `ChargeVisualCanvas`, `SlimeStateDebugUI(IMGUI)` 는 의도적으로 포팅하지 않음.
- **HUD**: HudLayer 는 존재하나 아직 사용 안 함.
- **Gamepad focus / Selection**: 아직 대응 안 함.

## 마이그레이션 기록 (2026-04-25)

- 기존 구조: `MainMenuCanvas/OptionsCanvas/EndCanvas/FullscreenEffectCanvas` 가 씬 루트에 직접 배치, `OptionsCanvas` 는 MainMenuScene + MainScene 에 중복 (MainMenuScene 쪽은 Resolution + BackgroundCamera 행 누락 drift).
- 포팅 후: 세 씬 모두 `UIRoot` 프리팹 인스턴스 + 필요한 패널 프리팹 인스턴스. OptionsPanel 단일 프리팹을 두 씬이 공유.
- `CameraFlashHazard`, `JumpscareTrigger` 는 `Awake` 에서 `FindFirstObjectByType` 폴백을 추가하여 새 UIRoot 의 Flash/Jumpscare Controller 를 자동으로 잡는다. 기존 인스펙터 참조가 끊겨도 런타임 작동.
- 삭제된 스크립트: `MainMenuController`, `OptionsPanelController`, `DemoEndController`, `FullscreenCanvasSetupUtility` (사용처 없음으로 유지). 삭제된 씬 오브젝트: 위 네 Canvas + 중복 EventSystem + 레거시 `MainMenuController`/`DemoEndController` 호스트 GameObject.
