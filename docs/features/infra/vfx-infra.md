# 공용 VFX 인프라

## ImpulseEmitter
Cinemachine 3.x `CinemachineImpulseSource` 래퍼. 카메라 셰이크.

## HitStopService (임팩트 프레임)
애니메이션풍 고대비 흑백 프리즈 프레임 효과. **시간 동결** (`Time.timeScale` 강제 저하) + **B&W 풀스크린 포스트 이펙트**가 한 쌍으로 구성되어 히트 임팩트를 극화. `HitStopService.Freeze(0.08f)` 한 줄로 어디서든 호출.

### 시간 동결 (HitStopService.cs)
- **API**: `Freeze(duration, frozenScale=0f)` / `Freeze(duration, ImpulseEmitter, shakeForce, frozenScale)` — 두 번째는 카메라 셰이크 동반(선택). *시간 동결만 발생, B&W 비주얼은 별도 API로 분리*.
- **오버랩**: 연속 호출 시 끝 시점을 뒤로 밀고(더 긴 것 유지), 더 강한 freeze(낮은 scale) 유지. 스택/커트 없음.
- **런너 자동 부트스트랩**: 첫 호출 시 `HideAndDontSave` GO가 `DontDestroyOnLoad`로 생성되어 코루틴 구동. 호출자는 아무 와이어링 필요 없음.
- **안전장치**: `OnDisable`/`OnApplicationQuit`/`InitializeOnEnterPlayMode`에서 `Time.timeScale` 원복, 가드 리스트 리셋, 이벤트 구독자 정리. 런어웨이 프리즈로 게임이 멈춘 채 남는 경우 없음.
- **Guard 시스템**: `AddGuard(Func<bool>)` / `RemoveGuard`. 등록된 predicate 중 하나라도 true면 `Freeze` no-op.
  - `HitStopGuards` 컴포넌트: 씬 GO에 부착, `SlimeController` + options panel GO 드래그로 두 기본 guard 자동 설치 (`IsCharging` → 차징 중 히트스톱 금지, panel `activeSelf` → 메뉴 열린 중 금지).
  - 추가 veto가 필요하면 (컷씬, 스테이지 전환 등) `HitStopService.AddGuard(() => 조건)` 직접 호출 — 컴포넌트 없이 가능.
- **정책 정합성**: 옵션 패널은 `Time.timeScale`을 건드리지 않는다는 기존 규칙(아래 "옵션 패널" 섹션)과 guard 시스템이 짝을 이룸. 차징 상태 guard는 slingshot aim 중 물리 왜곡을 방지.
- **이벤트 훅 (2채널 분리)**:
  - `OnFreezeStarted` — `Freeze()` 호출 시 fires. *시간 동결*에 딸린 피드백 전용 (오디오 피치 덕, 럼블 등).
  - `OnImpactFrameStarted` — `PlayImpactFrame()` 호출 시 fires. *B&W 비주얼*에 딸린 피드백 전용 (`ImpactFrameVisual`이 이쪽에 구독).
  - 두 채널이 분리되어 있어 "시간만 멈추고 B&W는 안 나옴" (Shift 대시) 또는 "B&W만 나오고 시간은 안 멈춤" (약한 히트 힌트) 같은 조합이 가능. 예외는 try/catch로 격리.
- **API 3종**:
  - `Freeze(duration, frozenScale=0f)` — 시간만 (Shift 대시가 사용).
  - `PlayImpactFrame(duration)` — B&W 비주얼만.
  - `ImpactHit(duration, frozenScale=0f)` — 둘 다 (큰 히트용 편의 메서드).

### 애니풍 임팩트 프레임 비주얼 (ImpactFrameGrayscale.shader + ImpactFrameVisual.cs)
**지각 모델**: 장면 *필터*가 아니라 장면 *교체*. 하드 컷 인 → 바이너리 잉크 1-N 프레임 홀드 → 하드 컷 아웃. 페이드 없음. 애니 임팩트 프레임의 핵심 규칙은 "뷰어가 의식적으로 반응하기 전에 완전히 다른 시각 어휘를 보여줬다 되돌린다".

**4 레이어 구성**:
1. **흰색 프리플래시 (1프레임)**: `_WhiteFlash=1` + `_ChromaActive=1` → 순백 + R/G/B가 방사선 방향으로 ±`_ChromaShift` 분리된 chromatic aberration.
2. **피사체 실루엣**: `_CameraDepthTexture` → eye-depth → `smoothstep(_SubjectMaxDepth ± _SubjectDepthSoftness)` → 슬라임만 단색 잉크. 루마 폴백은 `_UseDepthSilhouette=0`.
3. **잉크 윤곽선**: depth 버퍼에 Sobel 적용(Quibli 내부 셰이딩 무시) → `step(_EdgeThreshold, mag * _EdgeStrength)` → 얇은 기하 실루엣 라인.
4. **방사형 스피드 라인**: 슬라임의 스크린 UV(`cam.WorldToViewportPoint(slime.Position)`)를 중심으로 `sin(angle * _RadialLineCount)` 기반 스포크. 안쪽 반경은 페이드.

**바이너리 잉크 합성**: `ink = saturate(silhouette + edge + radial)` → `step(_InkThreshold, ink)` → 순수 흑 또는 순수 백만 출력 (회색 중간톤 제거).

**액센트 색**: `_AccentColor` (HDR, alpha=interior fill) — 실루엣 내부를 accent로 페인트 (검은 잉크 아닌 영역만) + 엣지 림에 얇은 accent 틴트. 배경 종이는 흰색 유지. 기본값 슬라임 그린 `(0.4, 1.0, 0.55, 0.85)`.

**셰이더 프로퍼티 일람** (Header 그룹):
- **Master**: `_Intensity`, `_WhiteFlash`, `_ChromaActive`, `_ChromaShift`
- **Posterize**: `_Threshold`, `_Softness`, `_Invert`, `_InkThreshold`
- **Subject Silhouette**: `_UseDepthSilhouette`, `_SubjectMaxDepth`, `_SubjectDepthSoftness`
- **Accent**: `_AccentColor` (HDR), `_AccentThreshold` (예약), `_AccentStrength`
- **Edge**: `_EdgeStrength`, `_EdgeThreshold`
- **Radial**: `_RadialStrength`, `_RadialCenter`, `_RadialLineCount`, `_RadialSharpness`, `_RadialInnerRadius`, `_RadialFadeWidth`

**의존성**: `_CameraDepthTexture` 필요 — URP Asset `m_RequireDepthTexture=1` (전역) + 렌더 피처 `requirements=Depth(1)` (방어적 중복) 둘 다 on. depth 비활성 시 `_UseDepthSilhouette=0`으로 루마 폴백.

**머티리얼** `Assets/Art/Materials/M_ImpactFrameGrayscale.mat` — 렌더 피처와 `ImpactFrameVisual` 스크립트가 동일 머티리얼 참조 공유.

**렌더 피처 와이어링**: `Tools → SlimeJumping → Setup Impact Frame Renderer Feature` 메뉴가 idempotent로 설치:
1. URP Renderer에 `FullScreenPassRendererFeature` 추가
2. Pass Material = `M_ImpactFrameGrayscale`
3. Injection Point = AfterRenderingPostProcessing (Quibli 컬러 그레이딩 뒤)
4. `requirements = Depth(1)` 세팅 (SerializedObject 경유)

셰이더 프로퍼티 변경 후에는 `Tools → SlimeJumping → Reimport Impact Frame Material`로 머티리얼 강제 재임포트.

**컨트롤러** `ImpactFrameVisual.cs` — 씬 GO 부착(HitStopGuards와 같은 GO 추천). `OnFreezeStarted` 구독 → `RunSequence(duration)` 코루틴:
- **Frame 0**: `slime`이 지정되면 `Camera.main.WorldToViewportPoint(slime.Position)`로 `_RadialCenter` 갱신 → white flash + chroma on → `_Intensity=1` → `yield return null`.
- **Frame 1..N**: white flash/chroma off → `_Intensity=1` 유지 → `while (Time.realtimeSinceStartup < endReal) yield return null`.
- **종료**: `SetIdle()` 하드 컷 (intensity/flash/chroma 모두 0). 

타이밍 소스는 `Time.realtimeSinceStartup` — `timeScale≈0`에서도 안정. `OnValidate`로 Play 중 Inspector 튜닝 즉시 반영.

**항상 켜둠**: `_Intensity=0`일 때 셰이더는 원본 반환 → 렌더 피처 토글 불필요. 비활성 비용은 풀스크린 샘플 수회 (cheap).

**권장 값**: `duration 0.04~0.15s`, `frozenScale 0`(클래식) / `0.05f`(슬로모 느낌). `_SubjectMaxDepth`는 슬라임 Z 대비 +~1.

**현재 상태**: 구현/와이어링 완료. 호출 지점은 미결정 — 쓸 곳 정해지면 (해저드 히트, 보스 KO, 폭죽 폭발 등) `HitStopService.Freeze(...)` 추가.

## FullscreenEffectCanvas / FullscreenOverlayCanvas
풀스크린 이펙트 공용 베이스 추상 클래스.

## SliceEffect
Knife/Saw 공통 베임 피드백. `SliceEffect.Spawn(prefab, contactPoint, signedTangent)` 정적 팩토리.
- `signedTangent`는 local +Y로 cone 회전 정렬.
- `ParticlePaletteBinder`로 `VFXType.Slice` 팔레트 색 자동 주입.
- 수명은 `max(particleLife, audioClip.length) + 0.1s`로 자체 관리 — SFX 잘림 방지.
- `DamageType.Slice`로 해저드 식별.

## VFX 팔레트 시스템
| 컴포넌트 | 역할 |
|---------|------|
| `VFXType.cs` (enum) | Wind/Fire/Water/Dust/Lightning/Pipe/Slice. **Append-only 규칙** |
| `VFXColorPalette.cs` (ScriptableObject) | VFXType별 Primary/Secondary 색상 정의 |
| `ParticlePaletteBinder.cs` | ParticleSystem에 부착, VFXColorPalette에서 색상 자동 적용 |

## LandingDustSpawner
`OnLanded` 이벤트 구독, 착지 시 Dust 파티클 생성.

## 배경 상시 Blur (2026-04-23)
원경 레이어(gradient quad + 파럴랙스)를 상시 흐리게 → DOF 느낌. Le Tai's TranslucentImage 에셋 기반.

**카메라 스택**:
- `BackgroundCamera` (Base, Layer 12 "BackgroundBlur"만 컬링, Solid Color clear) + `Main Camera` (Overlay, Depth-Only clear, Layer 12 제외).
- `BackgroundCamera`에 `TranslucentImageSource` 컴포넌트, `BackgroundBlurConfig.asset` (ScalableBlurConfig, strength 튜닝) 참조, `Preview=true`로 블러 결과를 카메라 출력 타겟에 직접 blit (1프레임 지연 없음).
- `UniversalRenderer.asset`에 `TranslucentImageBlurSource` Renderer Feature 등록.

**사용법**: 흐릿하게 보이고 싶은 배경 오브젝트의 Unity Layer를 12 (BackgroundBlur)로 설정 → 자동 blur 편입. 슬라임/지형/전경/UI는 그대로 선명.

**향후 Pause/Menu UI blur**: 별도 TranslucentImageSource를 Main Camera에 추가 (배경용 소스 재사용 금지 — Preview 동작 범위가 다름).

## Two-Ramp Y Blend 셰이더 (2026-04-22)
스테이지 경계 메시 한 장이 위·아래 두 스테이지 Quibli 머티리얼의 **`_GradientRamp` 텍스처 자체**를 world-Y 기준으로 보간.

Quibli에서 머티리얼의 "색"은 `_BaseColor`가 아니라 `_GradientRamp` 그 자체 (Lighting_DR.hlsl에서 NdotL로 샘플링). 단순 albedo override는 `_TextureImpact=0` (Quibli 기본값) 상태에서 무시 → Quibli `StylizedLit`을 4파일 포크해 `LightingPhysicallyBased_DSTRM`의 ramp 샘플링 지점에 ramp 블렌드 주입.

- `StylizedLit_TwoColor.shader` — `_GradientRampTop` 2D 프로퍼티 (위 스테이지의 Quibli Gradient).
- `Lighting_DR_TwoColor.hlsl` — ForwardLit Pass만 포크.
- 핵심: `DR_TWO_COLOR_Y_ON` 시
```
c = lerp(SAMPLE(_GradientRamp, uv), SAMPLE(_GradientRampTop, uv),
         saturate((posWS.y - _TwoColorBottomY) / (_TwoColorTopY - _TwoColorBottomY)))
```
- 나머지 Pass (ShadowCaster/DepthOnly/GBuffer/Meta/Outline/Universal2D)는 원본 Quibli.
- SRP Batcher cbuffer 레이아웃은 `StylizedInput_TwoColor.hlsl`에서 공유.
- **Trail World RT 호환**: ForwardLit fragment를 `TrailReceiverFragment`로 연결. `WorldTrailManager`가 없는 씬에서는 globals=0 가드로 no-op.
- 원본 `Assets/ThirdParty/Quibli/Shaders/*`는 절대 수정하지 않음.

## 옵션 패널 (2026-04-23, 데모용)
ESC로 열고 닫되 `Time.timeScale`은 건드리지 않아 게임 진행 유지.

**항목**:
- Frame Limit (30/60/120/Unlimited — `<`/`>` 사이클러).
- Player SFX (0..1 linear → dB 변환해 `SoundService.SetCategoryVolumeDb(SFX_Slime, db)`로 슬라임 액션 효과음 전용 버스 적용 → 믹서 노출 파라미터 `SFXSlimeVolume`).
- World SFX (0..1 linear → dB 변환해 `SFX_Environment` + `SFX_Object` 두 버스에 동일 dB 적용 → 노출 파라미터 `SFXEnvironmentVolume` / `SFXObjectVolume`). 환경 루프(WaterZone, ThinIce, JumpBall)와 오브젝트 이벤트(Icicle 등) 묶음.
- BGM (`BGMVolume` 파라미터 → BGM 버스). `StageBgmPlayer` 컴포넌트를 통해 재생.
- UI (`UIVolume` 파라미터 → UI 버스). `UIClickSoundInstaller`가 Canvas 하위 버튼 클릭음에 적용.
- Window Mode (Windowed/Borderless/Fullscreen — `Screen.fullScreenMode`에 `Windowed` / `FullScreenWindow` / `ExclusiveFullScreen`).
- Background Camera (On/Off — `BackgroundCameraEnabledChanged` 이벤트로 `BackgroundCameraOption`이 BG 카메라 + TranslucentImageSource 비활성화 + Main Camera Overlay→Base 전환. Off 시 블러/BG 스택이 꺼지며 `MainScene_성능_프로파일_2026-04-24.md`의 ~0.55 ms 렌더 비용 제거).
- CLOSE + QUIT 버튼.

**영속화**: `GameOptions.cs`가 PlayerPrefs에 저장.
- `BeforeSceneLoad`: `Application.targetFrameRate` + `Screen.fullScreenMode` + `BackgroundCameraEnabled` 로드 (카메라 토글은 씬의 `BackgroundCameraOption.OnEnable`에서 현재 값 적용).
- `AfterSceneLoad`: 오디오 적용 (SoundService Awake 이후).

**배치**: MainMenuScene / MainScene 양쪽에 `OptionsCanvas` (sortingOrder=1000) 루트 + `OptionsPanelController.cs`. 메인메뉴에는 `OPTIONS` 버튼 추가 (`onClick → OptionsPanelController.Open()`). 게임 중 ESC는 `Input.GetKeyDown` 직접 폴링.

**UI 클릭 SFX**: `UIClickSoundInstaller.cs`가 Canvas 하위 모든 Button의 onClick에 UI 버스 SFX 후킹 (`Fantasy_Game_UI_Material_Liquid_Bubble_Pop_1.wav`). 자체 AudioSource를 만들어 `AudioMixerGroup` (SoundSettings.uiGroup)으로 라우팅 → SoundService 없는 씬에서도 동작. UI 슬라이더는 `UIVolume` 파라미터로 이 그룹 제어.

> **오디오 시스템 전체 아키텍처/규칙**: [audio-system.md](audio-system.md) 참고. SoundService, 믹서 계층, BGM, validator, 새 사운드 추가 가이드.

## 해상도 독립 슬라임 크기 (2026-04-23)
`ResolutionScaleController.cs`:
```
newFov = 2 * atan(tan(refFov/2) * Screen.height / refHeight)
```
- `Start` + 매 프레임 `Screen` 변화 감지 시 적용.
- `CinemachineCamera.Lens.FieldOfView` (SlimeVCam, Brain이 Main Camera에 전파) + 비Cinemachine 카메라들(BackgroundCamera 스택)에 직접 적용.
- Ref = 1080p / FOV 40°.
- 창모드 토글로 `Screen.height` 변하면 자동 재적용.
- **F5 projection toggle 주의**: `CameraProjectionToggle.cs`는 `perspectiveFov`를 상수 40 하드코딩 → F5 전환 종료 시 스케일 리셋. 데모 한정 디버그 전용이므로 무시.
