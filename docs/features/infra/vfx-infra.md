# 공용 VFX 인프라

## ImpulseEmitter
Cinemachine 3.x `CinemachineImpulseSource` 래퍼. 카메라 셰이크.

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
