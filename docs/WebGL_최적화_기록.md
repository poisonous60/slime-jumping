# WebGL 포팅 — 2026-08-20/21 세션 기록

**대상:** SlimeJumping_v2 (Unity 6000.3.11f1, URP 17.3.0)
**범위:** 이 문서는 **이번 세션에서 한 작업만** 다룬다. 직전 세션의 에셋 감축(텍스처 1024 상한, 메시 감축, 오디오 WebGL 오버라이드, 미사용 에셋 격리)은 제외.

세션 시작 시점: Windows 빌드 695.3 MB 까지 줄여둔 상태, **Web 빌드 실측치는 없음**.

---

## 1. Web 빌드 첫 실측

직전 세션의 Web 전용 설정(밉맵 스트리핑, 오디오 AAC)이 여기서 처음 반영됐다.

| 카테고리 | Windows | **Web** | Δ |
|---|---:|---:|---:|
| Textures | 253.2 mb | **133.6 mb** | −47% |
| Sounds | 280.3 mb | **77.9 mb** | **−72%** |
| Meshes | 37.6 mb | 40.4 mb | +7% |
| Shaders | 14.6 mb | 16.5 mb | +13% |
| Other Assets | 5.7 mb | 5.6 mb | — |
| **Total User Assets** | 599.1 mb | **274.6 mb** | −54% |
| **Complete build size** | 695.3 mb | **167.4 mb** | **−76%** |

실제 업로드 페이로드 (Brotli 압축 후):

```
SlimeJumping_web.data.br        145.3 MB
SlimeJumping_web.wasm.br          7.5 MB
framework.js.br + loader.js       0.1 MB
StreamingAssets/MenuVideo         2.2 MB
──────────────────────────────────────────
합계                            약 155 MB
```

두 Web 전용 레버가 모두 동작함을 확인:
- 밉맵 스트리핑 → `numberOfMipsStripped: 0 → 1` 이 빌드에 의해 기록됨
- 오디오 AAC q0.5 → Sounds 280 → 78 mb, 단일 최대 성과

---

## 2. 프레임레이트 — 이번 세션의 핵심

### 증상

Windows 에서 120~200 fps 나오던 게 웹에서 60 을 못 지킴. 화면을 키우면 급락하는데 줄여도 회복되지 않음. 조작감까지 달라짐.

### 측정 방법 (게임 코드 수정 불필요)

- **F12 → Ctrl+Shift+P → `rendering` → Frame Rendering Stats** — 실시간 fps 오버레이
- **Performance 탭 녹화** — Main 트랙 점유율로 CPU/GPU 병목 판정

### 트레이스 분석 (27.5초)

| 구간 | 창 크기 | 표시 fps | rAF 콜백 내 CPU(중앙값) |
|---|---|---:|---:|
| 10~16s | 보통 | 51 | 0.24 ms |
| 16~20s | 큼 | 31 | **6.30 ms** |
| 20~28s | 작음 | 52 | 0.44 ms |

메인 스레드 총 점유 **30%**. CPU 는 놀고 있었다.

**결정적 단서는 3열이다.** 창을 키웠더니 rAF 콜백 내부 CPU 시간이 26배로 뛴다. CPU 작업량이 창 크기에 비례할 이유가 없다 — CPU 가 GPU 를 기다리며 블로킹된 시간이다. WebGL 은 GPU 가 밀리면 GL 호출 스트림에서 암묵적으로 동기화된다.

→ **GPU 바운드, 해상도에 정비례.**

### 원인 1: devicePixelRatio — 최대 요인

Unity 기본 WebGL 템플릿은 캔버스 렌더 타깃을 **CSS 크기 × `window.devicePixelRatio`** 로 잡는다. `config.devicePixelRatio = 1` 줄은 주석 처리돼 있고 그나마 모바일 분기 안에만 있다.

Windows 디스플레이 배율 150% → 픽셀 **2.25배**. 보이는 크기는 그대로인데 GPU 부하만 그만큼.

**결과: 51 → 70 fps (+37%). 조작 지연도 같이 해소.**

조작감 문제는 별도 버그가 아니었다 — 입력을 `Update` 에서 읽는데 200 fps(5 ms) 대 34 fps(29 ms) 라 지연이 6배였다.

**대응:** 커스텀 템플릿 `Assets/WebGLTemplates/SlimeJumping/` 신설, `webGLTemplate: PROJECT:SlimeJumping`.

```js
// 데스크톱에서도 창 전체를 채우고, 렌더 해상도를 CSS 픽셀에 고정
document.querySelector("#unity-container").className = "unity-mobile";
canvas.className = "unity-mobile";
var dprOverride = parseFloat(new URLSearchParams(location.search).get("dpr"));
config.devicePixelRatio = dprOverride > 0 ? dprOverride : 1;
```

`?dpr=0.8` 로 URL 에서 값을 바꿀 수 있게 했다 — 재빌드 없이 비교 가능. `.unity-mobile` 은 템플릿에 이미 있던 클래스(`width:100%; height:100%`, 푸터 숨김)를 재사용했다.

### 원인 2: MSAA → 뎁스 프리패스

`UniversalRenderPipelineAsset_Web.asset` 의 `m_MSAA` **2 → 1 (Off)**. PC 는 8 유지.

MSAA 가 켜져 있으면 URP 가 뎁스 버퍼를 싸게 복사할 수 없다(멀티샘플 뎁스). 이 프로젝트는 `m_RequireDepthTexture: 1` 이라 **씬 전체를 뎁스 전용 패스로 한 번 더 그린다** — 렌더러 1188개를 프레임마다 두 번 디스패치.

비용이 두 갈래라 값 하나로 양쪽을 친다.
- **고정비(CPU)** — 드로우콜 디스패치. 해상도 무관
- **변동비(GPU)** — MSAA 리졸브. 해상도 비례

대가: 폴리곤 가장자리 계단. 저폴리 플랫셰이딩이라 눈에 띄는 편.

### 원인 3: 불필요한 카메라

`Manager/StarFieldOverlay`, `Manager/NebulaOverlay` 비활성화. **활성 카메라 4개 → 2개.**

두 컴포넌트가 각각 전용 블룸 카메라와 전체화면 쿼드를 **런타임 생성**한다(`hideFlags = DontSave`). 고도 하늘 연출이라 1스테이지에서는 보이지 않는데 매 프레임 RenderTexture 를 클리어하고 패스를 돈다.

> 씬에서 쿼드를 지워도 소용없다. 컴포넌트를 꺼야 한다.

`BackgroundCamera` 는 **지울 수 없다.** 그게 Base 카메라이고 `Main Camera` 가 그 스택의 Overlay 다. 컬링 마스크도 `BackgroundBlur` 레이어 하나뿐이라 씬을 다시 그리지 않는다 — fps 주범이 아니다.

### 비용 모델 (MSAA 적용 전)

두 측정점(0.576 Mpx → 14.29 ms, 1.296 Mpx → 19.61 ms)에서:

```
프레임시간 ≈ 10.0 ms + 7.4 ms/Mpx
```

| 해상도 | 픽셀 | 예상 fps |
|---|---:|---:|
| 960×600 | 0.58 Mpx | 70 |
| 1280×720 | 0.92 Mpx | 59 |
| 1920×1080 | 2.07 Mpx | 40 |

**고정비 10 ms 가 천장을 100 fps 로 묶는다.** 저폴리 씬에서는 비싼 편이고 남은 감축 여지가 여기 있다.

### 아직 안 쓴 수단

| 수단 | 효과 |
|---|---|
| `m_RenderScale` 0.85 + `m_UpscalingFilter` FSR | 필레이트 −28%, 브라우저 확대보다 선명 |
| 오버드로우 감축 | 픽셀당 비용 직접 감소. Scene 뷰 Overdraw 모드로 Play 없이 측정 가능 |
| 렌더러 피처 정리 | 5개 중 4개가 상시 활성 |

---

## 3. 웹 전용 씬 분리

Web 은 1스테이지까지만 담고 경계에서 EndScene 으로 보낸다. 배경과 깨진 이펙트도 제거.

**MainScene 21.6 MB → MainScene_Web 5.7 MB (−74%)**, 루트 36 → 21개.

### 생성 도구

`Assets/Scripts/Editor/WebSceneBuilder.cs` — 원본에서 사본을 만든다. 원본과 `UIRoot` 프리팹은 읽기만 한다.

```
SlimeJumping/Web/Build Web Scenes
SlimeJumping/Web/Build Web End Scene
SlimeJumping/Web/Switch Build List to Web
SlimeJumping/Web/Switch Build List to Desktop
```

### 삭제 기준

| 종류 | 내용 |
|---|---|
| 스테이지 루트 | `stage2_*` 3개, `stage3_*` 5개, `stage4_*` 2개 |
| 배경 | `stage1_background` |
| 지형 생성기 | `TerrainMaker (6)(9)(10)` — Y 411~449 로 stage2 이상 |
| `etc` 자식 | 눈(7) · 비(6) · 우주(1) 앰비언트 |
| 깨진 이펙트 | `SpaceWarpDraw` 셰이더를 쓰는 렌더러 전량 (§4) |

깨진 이펙트는 이름 목록이 아니라 **셰이더 이름으로 판정**해 지운다 — 오브젝트 이름이 바뀌어도 조건은 유지된다.

> `etc` 는 스테이지 루트가 아닌데 stage2/3/4 물건을 담고 있다. 루트만 지우면 남는다.

### 씬 진입점

```csharp
// GameFlow.cs — 심볼 판정. Application.platform 은 에디터에서 늘 데스크톱이다.
#if UNITY_WEBGL
        public const bool IsWeb = true;
#else
        public const bool IsWeb = false;
#endif

public static string MainMenuScene => IsWeb ? "MainMenuScene_Web" : "MainMenuScene";
public static string GameplayScene => IsWeb ? "MainScene_Web"     : "MainScene";
public static string EndScene      => IsWeb ? "EndScene_Web"      : "EndScene";
```

### 엔딩 처리

`Assets/Scripts/Controller/WebStageExit.cs` **신규** — 슬라임에 붙는 컴포넌트. `exitY`(기본 375, stage1 최상단 364.5) 를 넘으면 `SceneTransitionController.Play(GameFlow.EndScene)`. 기존 `VoidRespawn` 과 같은 높이 판정 패턴이고, 페이드는 `SceneTransitionController` 가 이미 갖고 있어 새로 짜지 않았다.

`EndScene_Web` 의 아스키 발판을 **THANKS / FOR / PLAYING → DEMO / ENDING** 으로 재배치. 기존 `AsciiEndingSceneSetup` 에 진입점만 추가하고 문구·대상 씬을 인자로 뺐다. 없던 글자 아트 `letter_D` · `letter_E` · `letter_M` 을 기존 형식(6행, 후행 공백 없음, `##` 2칸 획)으로 신규 작성.

### UI 정리

웹 씬 3개 모두에서 데스크톱 전용 옵션 제거. **코드 분기 0** — `OptionsPanel` 이 모든 `[SerializeField]` 를 null 체크하므로 씬에서 끄기만 하면 된다.

| 대상 | 처리 |
|---|---|
| `ResolutionRow` `WindowModeRow` `FrameLimitRow` `VSyncRow` `BackgroundCameraRow` | 비활성 |
| `SidePanel` | 비활성 + **`sidePanel` 참조를 null 로** |
| 메인메뉴 `LoadButton` `QuitButton` | 비활성 (`UISlotType` 으로 탐색) |
| `Box` 크기 | 880 → **520** (`ContentSizeFitter` 자동), 폭 900 유지, 중앙 정렬 |

`SidePanel` 은 비활성화만으로 부족했다. `OptionsPanel.OnAfterAwake` 가 `sidePanel.SetActive(LoadSaveManager.HasCleared)` 로 되켜기 때문에 **참조 자체를 비워야** 코드가 손댈 수 없다.

옵션창 Quit 은 남겼다 — `Application.Quit` 이 아니라 `GameFlow.LoadMainMenu` 라 웹에서도 유효하다. 메인메뉴 Quit 만 진짜 종료라 숨긴다.

> **Web 빌드에서 `MainScene` 을 반드시 빼야 한다.** 포함되면 stage2/3/4 에셋이 전부 딸려와 감축이 0이 된다. Windows 는 반대로 웹 씬이 껴 있어도 stage1 에셋이 어차피 들어가므로 손해가 없다.

---

## 4. `SpaceWarpDraw` 셰이더 — Web 전량 실패

비가 마젠타로 렌더되는 증상에서 출발했다.

커스텀 LightMode `SpaceWarpDraw` 를 쓰는 셰이더 5종이 WebGL 에서 SubShader 가 거부되고 FallBack 으로 떨어진다.

```
BellWave_Distortion.shader     FallBack 없음                              → 안 그려짐
Sandevistan_EnvTint.shader     Fallback Off                               → 안 그려짐
SpaceWarp_BulbShatter.shader   FallBack Off                               → 안 그려짐
SpaceWarp_Distortion.shader    FallBack Off                               → 안 그려짐
Rain_Refraction.shader         FallBack "Hidden/Shader Graph/FallbackError" → 마젠타
```

**증상이 하나만 눈에 띈 이유가 여기 있다.** 4개는 조용히 사라지고, `Rain_Refraction` 만 에러 셰이더를 FallBack 으로 선언해 두어 분홍으로 보였다. 그래서 브라우저 콘솔도 깨끗했다 — 실패가 아니라 셰이더가 선언한 대로 동작한 것.

### 배제한 가설

| 가설 | 반증 |
|---|---|
| 빌드에서 스트립됨 | 빌드 로그 `gles3 (total internal programs: 4, unique: 4)`, `skipped 0 variants` |
| `#pragma target 4.5` 미지원 | 같은 4.5 를 쓰는 셰이더 11개 중 10개가 정상 (슬라임·지형 포함) |
| 런타임 GLSL 링크 실패 | 브라우저 콘솔에 에러 없음. Unity 는 링크 실패를 반드시 로그한다 |
| Always Included Shaders 로 해결 | 그 설정은 셰이더 에셋 포함만 보장하고 variant 스트리핑과 무관 — 효과 없어 되돌림 |

**대응:** 웹 씬에서 해당 렌더러를 전부 제거. 근본 해결(패스를 GLES3 호환으로 이식)은 하지 않았다.

관련: VFX Graph 는 패키지 자체가 컴퓨트 셰이더를 포함하므로 WebGL2 에서 깨진다. 이 프로젝트의 `.vfx` 5개는 미사용 검 이펙트라 무관했다.

---

## 5. 이번 세션에서 바꾼 설정

### Player Settings

| 항목 | 키 | 이전 | 이후 |
|---|---|---|---|
| WebGL 템플릿 | `webGLTemplate` | `APPLICATION:Default` | **`PROJECT:SlimeJumping`** |

### URP — Web 전용 에셋

`Assets/Settings/UniversalRenderPipelineAsset_Web.asset`. PC 에셋 무수정.

| 항목 | PC | 이전 | **이후** |
|---|---:|---:|---:|
| `m_MSAA` | 8 | 2 | **1 (Off)** |

참고로 이 에셋의 나머지 현재값: `m_MainLightShadowmapResolution 2048` / `m_AdditionalLightShadowsSupported 0` / `m_SoftShadowQuality 1` / `m_SupportsHDR 1` / `m_RequireDepthTexture 1` / `m_RequireOpaqueTexture 1` / `m_RenderScale 1`.

### 코드

| 파일 | 내용 |
|---|---|
| `Scripts/UI/GameFlow.cs` | 씬 이름 3종 플랫폼 분기. `IsWeb` 을 `#if UNITY_WEBGL` 심볼로 판정 |
| `Scripts/Settings/GameOptions.cs` | Web 에서 `Screen.SetResolution` 차단, `targetFrameRate = -1` 고정 |
| `Scripts/Controller/WebStageExit.cs` | **신규** — 1스테이지 상단 통과 시 EndScene 전환 |
| `Scripts/Editor/WebSceneBuilder.cs` | **신규** — 웹 씬 생성 + 빌드 목록 전환 |
| `Scripts/Platforms/Ascii/Editor/AsciiEndingSceneSetup.cs` | 문구·대상 씬을 인자로 분리, Web Demo 진입점 추가 |

`GameOptions` 의 웹 가드가 필요했던 이유: 저장된 해상도(1280×720)를 부팅 시 `Screen.SetResolution` 으로 다시 걸어 **캔버스 크기를 덮어썼다.** 캔버스는 창 전체인데 Unity 는 1280×720 만 그려서 좌하단에 박히고 나머지가 검게 남았다.

### 에셋

| 대상 | 내용 |
|---|---|
| `Assets/WebGLTemplates/SlimeJumping/` | **신규** — Unity `Base/Default` 템플릿 복사 후 수정 |
| `Assets/AsciiArt/Ending/letter_{D,E,M}.txt` | **신규** |
| `Assets/Scenes/{MainScene,MainMenuScene,EndScene}_Web.unity` | **신규** |
| `Manager/StarFieldOverlay`, `Manager/NebulaOverlay` | 웹 씬에서 비활성 |

---

## 6. 이번 세션에서 밟은 함정

- **`Application.platform` 은 에디터에서 항상 `WindowsEditor`** 다. 빌드 타깃이 WebGL 이어도 그렇다. 플랫폼 분기는 `#if UNITY_WEBGL` 심볼로 해야 에디터 Play 가 활성 빌드 타깃을 따라간다. 이걸 `Application.platform` 으로 짰다가 에디터 Play 에서 "씬을 찾을 수 없다" 로 터졌다.
- **Web 빌드 리포트의 `Levels` 는 0.0 kb 로 나온다.** 씬이 빠진 게 아니라 회계 방식이 다르다. WebGL 은 별도로 `***Player size statistics***` 블록에 레벨별 크기를 찍는다.

```
Level 0 'MainMenuScene.unity'  5.1 MB compressed /  13.5 MB uncompressed
Level 1 'MainScene.unity'     74.6 MB compressed / 186.0 MB uncompressed
Level 2 'EndScene.unity'       1.5 MB compressed /   2.8 MB uncompressed
```

- **프리팹 인스턴스의 자식은 지울 수 없다.** 비활성화가 씬 로컬 수단이다. 그런데 **비활성화만으로 부족한 경우가 있다** — 코드가 `Awake` 에서 되켜면 참조 자체를 null 로 비워야 한다.
- **`ContentSizeFitter` 로 높이를 줄일 때 폭은 건드리지 말 것.** 900 → 700 으로 좁혔더니 `Actions` 행의 버튼 3개가 안 들어가 `New Game` 이 두 줄로 접혔다.
- **Always Included Shaders 는 variant 스트리핑을 막지 않는다.** 셰이더 에셋이 빌드에 들어가는 것만 보장한다.
- **런타임 생성 오브젝트는 씬에서 지워도 다시 생긴다.** `hideFlags = DontSave` 로 만들어지는 것들은 생성 컴포넌트를 꺼야 한다.
- **`AsciiEndingSceneSetup` 같은 셋업 스크립트는 대상 씬 이름을 하드코딩한다.** 사본 씬에 쓰려면 인자로 빼야 한다.

---

## 7. 남은 것

| 항목 | 상태 |
|---|---|
| **itch.io 의 Brotli 서빙** | 미확인. `webGLCompressionFormat: 0`(Brotli) 인데 `webGLDecompressionFallback: 0` 이라 안전망이 없다. 서버가 `Content-Encoding: br` 을 안 주면 로딩 실패 |
| **MSAA off 후 fps 재측정** | 미측정. `?dpr=1` 최대화 상태와 1280×720 뷰포트에서 확인 필요 |
| **가장자리 자글거림 룩 판정** | 미판정. 못 봐주겠으면 카메라 FXAA 로 대체(플랫폼별 분기 필요) |
| **밉맵 절반 해상도 룩 판정** | 미판정. 무너지면 `Web` 레벨 `globalTextureMipmapLimit` 0 |
| **Windows 오디오** | 미적용. 695 MB 중 사운드가 280.3 MB(46.8%). Web 과 같은 기법을 Vorbis 로 적용하면 큰 감축 여지. **단 포맷 변경은 손실 압축이라 소리가 바뀐다** — Load Type 변경만은 무손실 |
| **`SpaceWarpDraw` 근본 해결** | 미해결. 웹에서는 제거로 대응 |
| **웹 데모의 클리어 기록** | 의도적 미구현. `ShrineInteractable.MarkCleared()` 가 웹 씬에 없어 클리어 보상이 열리지 않는다 |

### 주의

- **Windows 로 돌아갈 때는 두 가지를 같이** 해야 한다 — 빌드 타깃을 Windows 로 변경 + `SlimeJumping/Web/Switch Build List to Desktop`.
- **`MainScene_Web` 은 생성 후 손으로 편집됐다.** `Build Web Scenes` 를 다시 돌리면 그 편집이 사라진다.
