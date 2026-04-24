# Audio System

옵션의 볼륨 슬라이더(Player SFX / World SFX / BGM / UI)가 새로 추가되는 사운드에 **자동 반영**되도록 하는 체계. 게임플레이 코드는 단일 재생 경로 `SoundService`를 거치고, 카테고리 → 믹서 버스 → 노출 파라미터 체인이 이미 깔려있어 슬라이더가 자동으로 걸림.

## 규칙 (지켜야 할 것)

1. **모든 재생은 `SoundService.PlayAt / Play2D / PlayLooping / PlayBgm / StopBgm` 경유.** `AudioSource.Play()` / `PlayOneShot()` / `PlayClipAtPoint()` 직접 호출 금지. 서비스가 `SoundCategory` → `AudioMixerGroup` 매핑을 소유하고 있어 올바른 슬라이더에 자동 반응.

2. **씬/프리팹에 직접 꽂은 `AudioSource`는 반드시 `Output AudioMixerGroup` 지정.** 비워두면 Master 직결 → 슬라이더 우회. `AudioSourceMixerGroupValidator`가 저장 시 콘솔 경고 (저장 차단은 안 함).

3. **`SoundCategory` 선택 기준**:
   - `SFX_Slime` — 슬라임 자체가 내는 소리 (착지/발사/슬라이드) → Player SFX 슬라이더
   - `SFX_Environment` — 환경 앰비언스/루프 (물, 바람, 흐르는 진흙) → World SFX 슬라이더
   - `SFX_Object` — 월드 오브젝트 이벤트 (고드름, 점프볼, 얇은 얼음, 포탑, 폭발) → World SFX 슬라이더
   - `UI` — 버튼 클릭, 메뉴 피드백 → UI 슬라이더
   - `BGM` — 배경음악 → BGM 슬라이더. `PlayBgm` 전용, `PlayLooping`로 쓰지 말 것
   - 역할 고정 메커니즘 → 클래스 안에 하드코딩. 범용 에미터일 때만 `[SerializeField] SoundCategory`.

4. **BGM은 `StageBgmPlayer` 컴포넌트로.** 스테이지 씬에 빈 GameObject 하나 + 컴포넌트 + 클립 드래그. 씬 전환 크로스페이드 자동. 묵음 스테이지는 `stopIfClipNull=true + bgmClip=null`.

## 믹서 계층

```
MainMixer
 └── Master  (param: MasterVolume)
     ├── BGM  (param: BGMVolume)
     ├── SFX  (param: SFXVolume — 현재 코드에서 미사용)
     │   ├── SFX_Slime        (param: SFXSlimeVolume)        ← Player SFX slider
     │   ├── SFX_Environment  (param: SFXEnvironmentVolume)  ← World SFX slider
     │   └── SFX_Object       (param: SFXObjectVolume)       ← World SFX slider
     └── UI   (param: UIVolume)                              ← UI slider
```

`GameOptions.cs`는 선형 0..1 슬라이더 값을 `Mathf.Log10(v) * 20` 로 dB 변환해 `SoundService.SetCategoryVolumeDb(category, db)` → `AudioMixer.SetFloat(param, db)` 체인으로 적용. 인체 청감이 로그 스케일이라 이 공식이 표준.

## 컴포넌트 역할

| 컴포넌트 | 경로 | 역할 |
|---|---|---|
| `SoundService` | [SlimeJumping_v2/Assets/Scripts/Audio/SoundService.cs](../../../SlimeJumping_v2/Assets/Scripts/Audio/SoundService.cs) | 싱글톤 재생 허브. 카테고리별 AudioSource 풀 + BGM 2-source. `DontDestroyOnLoad` 지속. |
| `SoundSettings` (ScriptableObject) | [SlimeJumping_v2/Assets/Settings/SoundSettings.asset](../../../SlimeJumping_v2/Assets/Settings/SoundSettings.asset) | 믹서 에셋 + 그룹 참조 + 노출 파라미터 이름 보관. |
| `StageBgmPlayer` | [SlimeJumping_v2/Assets/Scripts/Audio/StageBgmPlayer.cs](../../../SlimeJumping_v2/Assets/Scripts/Audio/StageBgmPlayer.cs) | 씬 로컬 BGM 트리거. Start에서 `PlayBgm` 호출. |
| `UIClickSoundInstaller` | [SlimeJumping_v2/Assets/Scripts/UI/UIClickSoundInstaller.cs](../../../SlimeJumping_v2/Assets/Scripts/UI/UIClickSoundInstaller.cs) | Canvas 하위 모든 Button의 onClick에 UI 버스 SFX 자동 부착. SoundService 독립. |
| `AudioSourceMixerGroupValidator` | [SlimeJumping_v2/Assets/Scripts/Editor/AudioSourceMixerGroupValidator.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/AudioSourceMixerGroupValidator.cs) | 에디터 저장 시 `outputAudioMixerGroup == null` AudioSource 경고. |
| `GameOptions` | [SlimeJumping_v2/Assets/Scripts/Settings/GameOptions.cs](../../../SlimeJumping_v2/Assets/Scripts/Settings/GameOptions.cs) | PlayerPrefs 영속화 + dB 변환 + 부팅 시 적용. |

## BGM 재생 세부

- `SoundService.Awake`에서 자식 GameObject `BGM_A`, `BGM_B` 생성. 둘 다 `settings.bgmGroup` 라우팅, `spatialBlend=0`, `loop=true`, `playOnAwake=false`.
- `PlayBgm(clip, crossfadeSeconds, volume)`:
  - `clip == bgmActive.clip` 이면 no-op (동일 BGM 재호출 안전)
  - 이전 페이드 중이면 `StopCoroutine` 후 새로 시작
  - A/B 교대 — 이전 active가 outgoing, 반대쪽이 incoming
  - 페이드는 `AudioSource.volume` **선형 lerp**, `Time.unscaledDeltaTime` 기반 (시간스케일 무관)
  - 완료 시 outgoing.Stop() + clip=null, bgmActive = incoming
- `StopBgm(fadeSeconds)`: 현재 소스만 0으로 페이드 후 Stop
- **크로스페이드 품질 한계**: 선형은 짧은 전환(≤2s)에서 중앙 볼륨 딥 미미. 긴 드라마틱 전환에서 딥이 거슬리면 `Mathf.Pow(10, dB/20)` 로그 보정으로 업그레이드 가능. 업계 레퍼런스: [John Leonard French - How to fade audio in Unity](https://johnleonardfrench.music/how-to-fade-audio-in-unity-i-tested-every-method-this-ones-the-best/)

## 씬 지속성 (DontDestroyOnLoad)

- `SoundService.Awake`에서 싱글톤 가드 통과 시 `DontDestroyOnLoad(gameObject)`. 먼저 로드된 씬의 prefab 인스턴스 하나가 살고, 뒤에 로드되는 씬이 같은 prefab을 또 들고 있으면 `Destroy(gameObject)`로 파괴 → 항상 1개.
- MainScene / MainMenuScene **양쪽에 prefab 인스턴스 배치**. 어느 씬으로 시작하든 서비스 확보.
- `ResetStatics()` (`SubsystemRegistration` 타이밍)에서 `Instance = null` 로 play-mode 재진입 시 stale 참조 방지.

## 옵션 패널 볼륨 슬라이더 (2026-04-24)

[vfx-infra.md 옵션 패널 섹션](vfx-infra.md#옵션-패널-2026-04-23-데모용) 참고. 패널 UI/레이아웃은 거기서 다루고, 여기서는 **카테고리 → 파라미터 매핑만** 기록:

| 슬라이더 | `GameOptions` 필드 | 적용 대상 (`SoundService.SetCategoryVolumeDb`) |
|---|---|---|
| Player SFX | `PlayerSfxVolume` | `SFX_Slime` |
| World SFX | `WorldSfxVolume` | `SFX_Environment` + `SFX_Object` (둘 다 동일 dB) |
| BGM | `BgmVolume` | `BGM` |
| UI | `UiVolume` | `UI` |

PlayerPrefs 키는 전부 `opt.*` 접두사. 레거시 단일 `opt.sfxVolume`은 최초 로드 시 Player/World 양쪽으로 마이그레이션됨.

## 새 사운드 추가하는 법 (치트시트)

**단발성 효과음** (충돌/트리거 등):
```csharp
// 메커니즘 클래스 내부
SoundService.PlayAt(clip, transform.position, SoundCategory.SFX_Object, volume);
```

**루프/앰비언스**:
```csharp
var handle = SoundService.PlayLooping(loopClip, followTransform,
                                       SoundCategory.SFX_Environment, volume, spatial3D: true);
// 나중에
SoundService.StopLoop(handle);
```

**ZoneAudioManager 프록시 패턴** (여러 존에 같은 클립 일괄 주입): `WaterZoneAudioManager.cs` 참고 — 프록시는 클립/볼륨만 전달, 카테고리는 재생 주체(Zone 클래스) 안에 하드코딩.

**BGM**: 씬에 빈 GameObject → `StageBgmPlayer` 컴포넌트 → `bgmClip` 드래그 → `fadeSeconds` 조정. 끝.

**UI 사운드**: 기존 `UIClickSoundInstaller`가 Canvas 하위 모든 버튼 자동 처리. 다른 UI 사운드 필요 시 `SoundService.Play2D(clip, SoundCategory.UI)`.

## 기존 코드 리팩터 현황

**CONVERT 완료 (2026-04-24 Phase 2)** — 직접 `AudioSource.Play*` → `SoundService` 경유로 이관, 프리팹/씬 orphan AudioSource 제거:

- `SliceEffect.cs` → `SoundService.PlayAt(SFX_Object)` (`Slice_Hit.prefab` AudioSource 제거)
- `FireworkInteractable.cs` → `PlayAt(SFX_Object)` (`Firework_Radial.prefab` 포함 인스턴스 AudioSource 전부 제거)
- `ShrineInteractable.cs` → `PlayAt(SFX_Object)`
- `CameraFlashHazard.cs` → `PlayAt(SFX_Object)` (`shutterAudio` 필드 → `shutterClip`+`shutterVolume`만)
- `JumpscareCanvasController.cs` → `Play2D(UI)`. `FullscreenOverlayCanvas` base class의 `audioSource` 필드도 같이 제거 (유일 사용자였음)

**KEEP — embedded AudioSource 유지 (상태/루프 필요)** — prefab/씬 인스턴스의 `Output AudioMixerGroup`만 검증/설정:

- `ApplianceInteractable_Washing.cs` → `MainMixer/SFX/SFX_Environment` (기계 루프 on/off 토글)
- `MediaInteractable_Phonograph.cs` → `MainMixer/BGM` (디스크 회전 sync with `audioSource.time`)
- `MediaInteractable_TV.cs` → `MainMixer/SFX/SFX_Object` (toggle on/off clip)

**DEFER** — 이번 범위 밖, 별도 PR에서 진행:

- `SceneTransitionController.cs` (6개 `PlayOneShot`) — 비동기 씬 로드 async 로직과 얽힘. 현재 prefab의 AudioSource `Output` 필드만 지정하면 슬라이더 반영은 됨 (validator로 관리).
- `UIClickSoundInstaller.cs` — 자체 AudioSource + `settings.uiGroup` 경로로 이미 규칙 충족. 변경 불필요.
