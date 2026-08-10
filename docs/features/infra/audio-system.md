# Audio System

옵션의 볼륨 슬라이더(Player SFX / World SFX / BGM / UI)가 새로 추가되는 사운드에 **자동 반영**되도록 하는 체계. 게임플레이 코드는 단일 재생 경로 `SoundService`를 거치고, 카테고리 → 믹서 버스 → 노출 파라미터 체인이 이미 깔려있어 슬라이더가 자동으로 걸림.

## 규칙 (지켜야 할 것)

1. **사운드 하나 = `SoundDef` 에셋 하나** (2026-08-10 전환). 클립·볼륨·피치·카테고리를 한 에셋이 들고, 코드는 그 에셋을 재생만 한다. 컴포넌트는 `[SerializeField] SoundDef` 만 든다.

```csharp
[SerializeField] SoundDef fireSound;
SoundService.Play(fireSound, transform.position);            // 3D 원샷
SoundService.Play2D(clickSound);                             // 2D 원샷
handle = SoundService.PlayLooping(ambientSound, transform);  // 루프
```

2. **호출부는 배율만 넘긴다.** `Play(def, pos, volumeScale, pitchScale)` 는 에셋 값을 **곱한다**(덮지 않는다). 충돌 세기·거리 감쇠처럼 그때그때 달라지는 값만 호출부 몫:

```csharp
SoundService.Play(cfg.impactSound, worldPos, DistanceFalloff(worldPos));
```

3. **믹서 그룹을 인스펙터에서 드래그하지 말 것.** 라우팅은 `SoundDef.category` → `SoundSettings` → `AudioMixerGroup` 3단으로만. 손으로 꽂는 경로가 남으면 오배선이 재발한다 — 실제로 두 건 있었다(아래 "적발된 오배선").

4. **`SoundCategory` 선택 기준**:
   - `SFX_Slime` — 슬라임 자체가 내는 소리 (착지/발사/슬라이드) → Player SFX 슬라이더
   - `SFX_Environment` — 환경 앰비언스/루프 (물, 바람, 폭포) → World SFX 슬라이더
   - `SFX_Object` — 월드 오브젝트 이벤트 (고드름, 얇은 얼음, 포탑, 폭발) → World SFX 슬라이더
   - `UI` — 버튼 클릭, 씬 전환 → UI 슬라이더
   - `BGM` — 배경음악 → BGM 슬라이더

5. **AudioSource 를 직접 드는 것은 예외뿐.** SoundService 풀이 표현 못 하는 것만: 피아노 보이스풀(88건반 동시 발음), 축음기·TV·세탁기(재생/정지/트랙 전환), `Snowball`(구르기/날기 루프를 자체 AudioSource 로 전환 — 클립·볼륨이 아직 raw). 폐기 상태인 `SwingingBell`(V1)도 타격 세기로 클립 인덱스를 고르는 구조라 이관하지 않았다. 이들도 그룹은 `SoundCategory` 에서 받아온다(`SoundService.GetGroup`). `AudioSourceMixerGroupValidator` 가 저장 시 그룹 null 을 경고.

6. **BGM 은 `StageBgmPlayer`.** 씬에 빈 GameObject + 컴포넌트 + `SoundDef` 하나.

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
| `SoundDef` (ScriptableObject) | [SlimeJumping_v2/Assets/Scripts/Audio/SoundDef.cs](../../../SlimeJumping_v2/Assets/Scripts/Audio/SoundDef.cs) | 사운드 정의 1개 = 클립 목록 + 재생 순서(Random/Sequential) + 볼륨/피치 범위 + 카테고리 + 3D 거리. 에셋은 `Assets/Settings/Sounds/`. (`Assets/Audio/` 는 대용량 오디오 바이너리용이라 통째로 gitignore 대상 — 데이터 에셋을 거기 두면 추적이 안 된다.) |
| `SoundService` | [SlimeJumping_v2/Assets/Scripts/Audio/SoundService.cs](../../../SlimeJumping_v2/Assets/Scripts/Audio/SoundService.cs) | 싱글톤 재생 허브. 카테고리별 AudioSource 풀 + BGM 2-source. `DontDestroyOnLoad` 지속. |
| `SoundSettings` (ScriptableObject) | [SlimeJumping_v2/Assets/Settings/SoundSettings.asset](../../../SlimeJumping_v2/Assets/Settings/SoundSettings.asset) | 믹서 에셋 + 그룹 참조 + 노출 파라미터 이름 보관. |
| `StageBgmPlayer` | [SlimeJumping_v2/Assets/Scripts/Audio/StageBgmPlayer.cs](../../../SlimeJumping_v2/Assets/Scripts/Audio/StageBgmPlayer.cs) | 씬 로컬 BGM 트리거. Start에서 `PlayBgm` 호출. |
| `UIClickSoundInstaller` | [SlimeJumping_v2/Assets/Scripts/UI/UIClickSoundInstaller.cs](../../../SlimeJumping_v2/Assets/Scripts/UI/UIClickSoundInstaller.cs) | Canvas 하위 모든 Button의 onClick에 UI 버스 SFX 자동 부착. `Play2D(UI)` 경유 (2026-08-10). |
| `AmbientSound` | [SlimeJumping_v2/Assets/Scripts/Audio/AmbientSound.cs](../../../SlimeJumping_v2/Assets/Scripts/Audio/AmbientSound.cs) | 루프 환경음. `OnEnable` 에서 `PlayLooping`, `OnDisable` 에서 `StopLoop`. 자체 감쇠 거리(`minDistance`/`maxDistance`)를 들고 있어 풀 기본값(3/20)보다 멀리 들리는 앰비언스를 표현. |
| `SoundInventoryWindow` | [SlimeJumping_v2/Assets/Scripts/Editor/SoundInventoryWindow.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/SoundInventoryWindow.cs) | `Tools/Sound Inventory`. 아래 별도 섹션. |
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

1. `Tools/Sound Inventory` → **+ 새 사운드** (또는 Project 우클릭 → Create → SlimeJumping → Sound Definition)
2. 에셋에 클립·카테고리·볼륨 입력. 클립 여러 개 넣으면 Random 또는 Sequential 로 골라 재생
3. 쓰는 쪽에 `[SerializeField] SoundDef xxxSound;` + `SoundService.Play(xxxSound, pos)`

`clipOrder = Sequential` 의 커서는 **에셋에 있다** — 같은 SoundDef 를 여러 오브젝트가 물면 사이클을 공유한다(종 두 개가 번갈아 b1,b2 를 나눠 가짐). 개별 사이클이 필요하면 에셋을 복제해라.

끝. 볼륨을 호출부에 쓰지 않는다 — 크기가 상황에 따라 달라져야 하면 `volumeScale` 배율만 넘긴다.

**루프/앰비언스**: 오브젝트에 `AmbientSound` 컴포넌트 + `SoundDef` 하나. 감쇠 거리도 에셋이 들고 있다.

**여러 인스턴스에 같은 소리 일괄 주입**(`WaterZoneAudioManager` 등): 매니저가 `SoundDef` 참조만 브로드캐스트한다.

## 표면별 슬라임 사운드 (2026-04-30)

슬라임 착지 / 점프 사운드를 닿는 표면별로 다르게 재생. `SlimeLocomotionAudio` 의 4 `SoundDef`(landHard / landSoft / launch / hop)는 **fallback** 이고, 표면이 자기 것을 들고 있으면 그쪽이 이긴다.

- **데이터**: `SurfaceMaterial` 에 `SoundDef` 4개 (landHardSound / landSoftSound / launchSound / hopSound). 비워두면 fallback. **2026-08-10 이전의 "볼륨 0 = 상속" 규칙은 사라졌다** — 오버라이드 여부는 에셋이 있냐 없냐로만 갈린다. 현재 채워진 것은 Snow 뿐(landHard/landSoft/hop).
- **눈 브리지**: `SnowField` 는 `SurfaceTag` 가 없지만 `SnowSettings.snowSurfaceMaterial` 에 이미 `SurfaceMaterial` 참조가 있음 (Snow.asset 사용 중). 따라서 눈 사운드는 Snow.asset 의 audio 필드만 채우면 됨 — `SnowSettings` 자체에는 audio 필드 추가하지 않음.
- **해석**: [SurfaceAudioResolver.cs](../../../SlimeJumping_v2/Assets/Scripts/Audio/SurfaceAudioResolver.cs) — 정적 헬퍼. `(slimeController.SurfaceCollider, contactPoint)` 받아서 (1) **SnowField 우선**: `SnowFieldRegistry.TryGet` 성공 + `SampleDepthRatio01(contactPoint) ≥ 0.1` 이면 `snowSurfaceMaterial` 사용, (2) 그 외에는 host 의 `GetComponentInParent<SurfaceTag>()` 폴백, (3) 둘 다 없으면 false.
- **왜 snow 우선?** SnowField 는 `[SnowField.cs:12]` 자기 설명대로 `SurfaceTag` 가 없는 visual layer. 슬라임은 항상 host 콜라이더 위에 서있고, host 가 자기 SurfaceTag 를 들고있음. 만약 SurfaceTag 를 먼저 검사하면 항상 host 의 tag 가 매칭되어 snow 분기까지 도달조차 못 함 → snow 클립 영원히 미재생. 게임플레이 측 `RefreshSnowBlend` 도 host tag + snowSurfaceMaterial 을 depth 비율로 블렌드하는데, audio 는 단발 SFX 라 임계값 분기로 단순화: depth ≥ 0.1 → snow 클립, 미만 (완전히 파인 트랙 / SnowField 미등록) → host 클립.
- **소비**: `SlimeLocomotionAudio.HandleLanded / HandleLaunch` 가 이벤트마다 `position` 인자를 contactPoint 로 전달해 resolver 호출 → 매칭 클립 있으면 사용, 없으면(개별 슬롯이 null) fallback 클립으로. 재생 호출은 여전히 이벤트당 1회 (이중 재생 없음).
- **이벤트 시점 안전성**: `surfaceCollider` 는 `Airborne` 전이 시 명시적 null 처리되지 않음 — `OnLaunch` 가 `FinalizeAirborneExit()` 이후 발화해도 직전 표면 참조가 살아있어 resolver 가 정상 동작.
- **carve 타이밍**: `OnLanded` 구독 순서에 따라 audio 가 SnowStamper 보다 먼저 / 나중일 수 있는데, 단발 착지의 carve 가 depth 를 0.1 미만으로 떨어뜨릴 만큼 깊지 않아 (단일 carve 깊이 ≪ heightRest) 임계값 0.1 은 양쪽 순서 모두에서 안정적. 깊은 트랙 launch 시에는 슬라임이 host 가까이 있어 depth 가 자연스럽게 낮음 → host 사운드로 폴백되는 게 의도된 동작.

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

**CONVERT 완료 (2026-08-10 Phase 3)** — 남아있던 손배선 AudioSource 정리:

- `SceneTransitionController.cs` — 직렬화 `audioSource` 제거, `PlayOneShot` 6곳 → `Play2D(UI)` + `sfxVolume` 노브. `Resources/SceneTransition.prefab` 의 AudioSource 제거.
- `UIClickSoundInstaller.cs` — 런타임 `AddComponent<AudioSource>` 와 드래그 `outputGroup` 필드 제거, `Play2D(UI)` 경유. 대가: SoundService 없는 씬에서 UI 소리가 안 난다(모든 씬에 서비스 인스턴스 필요).
- `WaterfallAmbient` (`stage1_floor2/wheel set/WaterfallImpactVFX_Test/`) — 스크립트 없는 생 AudioSource → `AmbientSound`(min 46.23 / max 82.93 이식).
- KEEP 3종(`ApplianceInteractable_Washing` / `MediaInteractable_Phonograph` / `MediaInteractable_TV`)은 AudioSource 유지하되 드래그 그룹 → `[SerializeField] SoundCategory` + `SoundService.GetGroup()`.

### 적발된 오배선 (2026-08-10)

인벤토리 창을 만들게 된 계기이자, 창을 만들면서 실제로 나온 것:

| 대상 | 상태 | 증상 |
|---|---|---|
| `UIRoot.prefab` 의 `UIClickSoundInstaller.outputGroup` | **BGM 그룹**에 배선 | UI 클릭음이 BGM 슬라이더를 따라 커지고 작아짐. UI 슬라이더는 무반응 |
| `SceneTransition.prefab` 의 `AudioSource.outputAudioMixerGroup` | **NULL** | Master 직결 — 씬 전환음이 UI·카테고리 슬라이더를 전부 우회 |

`SoundSettings.asset` 의 카테고리 → 그룹 매핑 자체는 정상이었다(UI → UI). 오배선은 전부 **손으로 꽂은 쪽**에서 나왔다. 규칙 2가 여기서 나온 것.

## 사운드 인벤토리 창 (2026-08-10)

`Tools/Sound Inventory`. 프로젝트가 실제로 물고 있는 사운드 전부를 한 목록에.

- **스캔 대상**: 로드된 씬 전체 + `Assets/Resources`·`Assets/Settings`·`Assets/Prefabs`(창에서 편집 가능한 폴더 목록)의 프리팹/ScriptableObject. `AudioSource` 는 물론 모든 컴포넌트/에셋의 직렬화된 `AudioClip` 참조를 `SerializedObject` 순회로 수집(배열·중첩 포함). 실측 27그룹 / 198ms.
- **레지스트리 없음**: 클립이 전부 직렬화 참조라서(프로젝트 전체에 `Resources.Load<AudioClip>` 0곳) 리플렉션 스캔이면 충분하다. 새 `[SerializeField] AudioClip` 을 추가하면 창을 안 고쳐도 다음 Rescan 에 뜬다.
- **기능별 그룹**: 소유 컴포넌트 타입으로 폴드아웃. `AudioSource` 만 있는 오브젝트는 형제 `MonoBehaviour` 이름으로 라벨링(콜라이더/렌더러는 제외 — 안 그러면 피아노가 `BoxCollider2D ×52` 로 뜬다).
- **카테고리 컬럼**: `AudioSource` 는 실제 `outputAudioMixerGroup` 을 `SoundSettings` 로 역매핑. 클립 필드는 **소유 스크립트 소스 텍스트에서 `SoundCategory.X` 토큰을 스캔** — 카테고리가 직렬화 데이터가 아니라 호출부 리터럴이라 이 방법뿐이다. 파일 단위라 한 스크립트가 두 카테고리를 쓰면 둘 다 병기된다. `PlayBgm`/`StopBgm` 은 카테고리 인자가 없어 메서드명으로 BGM 매핑.
- **경고**: 클립 미배정 / Output 그룹 없음 / 생 AudioSource / 그룹↔스크립트 카테고리 불일치. "미배정" 필터가 곧 **아직 사운드 안 넣은 자리** 목록이다.
- **미사용 클립**: 프로젝트의 `AudioClip` 에셋 중 위 스캔에 안 잡힌 것(ThirdParty/Plugins 제외).
- **인라인 수정**: 클립 교체, 볼륨 슬라이더(컴포넌트의 `*volume*` float 필드 자동 노출), AudioSource 의 그룹·거리.
- **▶ 프리뷰는 창이 소유한 `AudioSource` 로 재생한다 (2026-08-10)**. `AudioUtil.PlayPreviewClip` 은 **볼륨 인자가 없어서** 못 쓴다. 대신 `OnEnable` 에서 `HideFlags.HideAndDontSave` GameObject + `AudioSource` 를 만들어 창 수명 동안 들고, `volume` 을 꽂고 `Play()`. `OnDisable` 에서 파괴. 재생 중에는 `OnGUI` 에서 `Repaint()` — **에디터 오디오는 에디터 루프가 돌아야 진행한다.** 같은 프로젝트의 `ThirdParty/IntuitiveCreative/IntuitiveAudioAdjust` 가 쓰는 방식 그대로.
  - 어느 볼륨을 쓰나: `AudioSource` 행은 자기 `volume`. 클립 필드는 **어간이 같은 볼륨 필드**(`fireClip` ↔ `fireVolume`), 없으면 볼륨 필드가 하나뿐일 때 그것, 여럿이면 원본 크기(▶ 툴팁에 표시). 고른 값은 행 옆에 `×0.10` 으로 표기.
  - 믹서 그룹 라우팅(카테고리 볼륨 슬라이더)은 반영 안 된다. 그건 Play 모드에서.
  - **막다른 길 기록** — 샘플에 볼륨을 곱한 임시 클립(`AudioClip.Create`+`SetData`)을 `PlayPreviewClip` 에 넘기면 **무음**이다. 데이터는 정상으로 들어가고(peak 0.560 → ×0.25 → 0.140) `samplePos` 도 진행하는데 소리만 없다 — `AudioUtil.HasPreview(생성클립)` 가 False. 프리뷰 플레이어는 임포트된 에셋의 프리뷰 데이터를 읽지 런타임 클립 샘플을 안 읽는다.
  - **측정 함정** — 에디트 모드 `AudioSource` 가 "안 돈다"고 오판하기 쉽다. 창 없이 `exec` 로 오브젝트만 만들어 재면 `dspTime` 이 고정(엔진이 서비스되지 않음)이라 `timeSamples` 0 · `isPlaying` false 로 보인다. 루프를 돌리는 주체(창의 `Repaint`)가 있어야 실제 동작이다.
