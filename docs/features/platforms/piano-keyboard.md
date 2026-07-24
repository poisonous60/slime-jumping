# Piano Keyboard (플레이 가능한 피아노 플랫폼)

슬라임이 흰 건반 위에 착지하면 건반이 **속도에 비례해 내려갔다가 눌린 상태로 유지(held)**되고 그 음의 **피아노 샘플이 셈여림(강약) 적용되어 재생**된다. 슬라임이 **건반을 떠나면** 건반이 복귀하며 음이 **댐퍼처럼 페이드아웃(release)**된다(하드 컷 아님). 한 마리 슬라임이 88건반 통짜 업라이트 피아노를 연주하는 플랫폼.

## 출처 / 라이선스
- 3D 모델 + 음 샘플: [catdevpete/Unity-MIDI-Piano](https://github.com/catdevpete/Unity-MIDI-Piano) (코드 MIT). 우리는 코드는 쓰지 않고 **에셋만** 추출.
- `Piano.fbx` (업라이트 피아노, 건반 88개 분리, `PianoKey.001`~`.088`) → `Assets/_PianoImportTest/Piano.fbx`. 루트 transform 깨끗(scale 1 / rot 0). FBX mesh 자체 라이선스는 repo에 별도 명시 없음 (MIT 우산 가정) — **상용 전 확인 필요**.
- 음 샘플: Beskhu(Freesound) **CC0** 업라이트 피아노 `.aiff` 88개 → `Assets/Audio/Piano/`. 파일명 prefix = MIDI 노트번호 (예 `..__21-a0` = MIDI 21 = A0). 커버리지 MIDI 9–96.

## 오디오 임포트 (타격 히치 방지)
88개 건반 샘플 + `piano_pedal.wav` 임포트 설정: **Compression Format = PCM**, **Preload Audio Data = ON**, **Load Type = Decompress On Load**. 이전 설정(Vorbis + Preload OFF)은 각 건반 **첫 타격** 때 디스크 동기 로드 + Vorbis 디코드를 메인 스레드에서 수행해 "잠깐 렉"이 발생했음 → PCM(디코드 0) + Preload(씬 로드 시 선적재)로 게임플레이 중 히치 제거. 짧은 원샷 SFX라 메모리 부담 작음.

## 건반 매핑
- `PianoKey.00N` 은 x 정렬 = 음 오름차순. **MIDI = 20 + N** (N=1 → A0/21 … N=88 → C8/108).
- **흰/검 분류:** 흰 건반 `localPos.y ≈ 5.639`, 검은 건반 `≈ 5.584`. 52 흰 / 36 검. **흰 건반만** collider/스크립트 부여 (검은 건반은 비주얼만, 충돌 없음 — 요구사항).
- 샘플 매핑: 흰 건반 MIDI → 동일 prefix 샘플. MIDI 97–108(상단 흰 7개)은 샘플 없어서 `MIDI−12` 샘플 + `AudioSource.pitch = 2` 옥타브 시프트.

## 스크립트 (`Assets/Scripts/Platforms/Piano/`)
- **`PianoKeyboard.cs`** (피아노 루트, `DefaultExecutionOrder(-150)` → 모터보다 먼저) — 매 FixedUpdate 각 흰 건반과 슬라임의 **수평 overlap** 판정(`overlapThreshold` 넘으면 + 슬라임 밑면이 건반 top 근처 `verticalTolerance` 이내) → `PianoKey.Drive(contacting, impactSpeed, settings)`. overlap 방식이라 **슬라임이 두 건반 seam 에 걸치면 둘 다 눌림**(slime 폭 ≈ 건반 폭이라 정확 straddle 필요했던 Distance 방식 대체).
- **`PianoKey.cs`** (흰 건반당, **`PlatformMotor` 상속**) — 밟으면 건반(collider+mesh, mesh 가 모터 자식)이 **아래로 sink**(damped spring) → 표준 `PlatformCarrier` 로 **슬라임을 같이 태워 내림**(페달·SpringyBranch 와 동일), 떠나면 복귀. rising edge=**Strike**(velocity→sink 깊이 `holdDepth`~`strikeDepth` + `source.Play()`, 볼륨=velocity 셈여림), falling edge=**Release**(같은 클립 선형 페이드 `releaseFade`→`Stop`, 댐퍼). sink 깊이 = **world 단위**(carry 양 = 이 깊이). 전용 `AudioSource`, `sfxObjectGroup`. *주의: carry 위해 collider+visual 함께 이동(키는 "분리" 규칙을 의도적으로 오버라이드).*
- **`PianoSettings.cs`** (인스펙터 인라인) — 튜닝.
- **`PianoPedal.cs`** (페달당, **`PlatformMotor` 상속**) — 밑 페달 3개(`Pedal_Left/Centre/Right`). 슬라임이 밟으면 페달+`PolygonCollider2D`가 **아래로 sink**(damped spring) → 표준 `PlatformCarrier` 경로로 **슬라임을 같이 태워 내림**(SpringyBranch 와 동일 carry), 떠나면 복귀. **소리·음 영향 없음**. 위에 **visual mesh만 X축 cosmetic tilt**(화면 안쪽으로, 2D collider 안 건드림). `ApplyRiderInfluence`/`RemoveRider` 로 rider 감지, `ComputeMotion` 이 collider 를 Y 로 내려 carry, `OnAfterMotion` 이 visual pivot tilt. collider 는 `PolygonCollider2D` (페달 모양으로 reshape 가능). per-pedal 튜닝(`maxDip` 내려가는 깊이=carry, `maxAngle` 시각 tilt, `springStiffness`/`springDamping`).
- **`Editor/PianoKeyboardBuilder.cs`** — 씬 빌더. 메뉴 **`Tools/SlimeJumping/Build Test Piano`**(전체, 페달 포함) / **`Tools/SlimeJumping/Add or Rebuild Piano Pedals`**(기존 피아노에 페달만 추가, 건반·배치 안 건드림, idempotent).

## 비주얼/콜라이더 분리 (프로젝트 규칙 준수)
sensor(`BoxCollider2D`)는 정적 자식 GO, dip 애니는 건반 mesh transform 의 localPosition.y 만 이동 → collider 불변, surface probe 안정.

## 튜닝 — **단일 자산 `Assets/Settings/PianoConfig.asset`** (`PianoConfig` ScriptableObject)
건반·키보드 감지·페달 전부 이 자산 하나로 조절(다른 플랫폼 `BambooV2Settings`/`SoundSettings` 패턴). `PianoKeyboard` 와 3개 `PianoPedal` 모두 이 자산 참조. 빌더가 없으면 생성, `RebuildKeys`/`AddPedals` 가 참조 연결. 섹션: **Keys**(`keys.*` 아래 — 셈여림/눌림 커브·sink 깊이·spring·release·masterVolume), **Keyboard**(`overlapThreshold`/`verticalTolerance`), **Pedals**(`pedalMaxDip`/`pedalMaxAngle`/`pedalSpring*`/`pedalPressSound`/`pedalSoundVolume` + **`pedalVelocityToVolume`** AnimationCurve[밟는 속도 0..1→볼륨 0..1] + `pedalMin/MaxImpactSpeed` 정규화 범위). 페달 소리는 첫 contact 의 슬라임 impact 속도(landing zero 전 캡처)를 커브에 매핑 × `pedalSoundVolume`.

깊이는 **world 단위**(건반 sink = carry 양).
- `velocityToVolume` (AnimationCurve) — 속도→음량, **셈여림**.
- `velocityToDepth` (AnimationCurve) — 속도→**타격 순간** dip 비율(0..1 × `strikeDepth`).
- `minImpactSpeed` / `maxImpactSpeed` — 커브 입력 정규화 범위(world u/s).
- `strikeDepth` — 풀스피드 타격 순간 transient dip 피크(world).
- `holdDepth` — **슬라임이 올라가 있을 때 유지되는 눌림 깊이**(world). 낮으면 얕게 보임 → 이 값 키워라.
- `springStiffness` / `springDamping` — **피아노 뻑뻑함 / 출렁임**. damping 낮으면 더 출렁, stiffness 높으면 빨리 복귀.
- `releaseFade` (초) — 슬라임이 떠날 때 음이 사라지는 댐퍼 페이드 시간. 짧으면 빨리 끊김, repo 는 ~1s.
- `masterVolume`.

기본값: `strikeDepth 0.45`, `holdDepth 0.28`, `releaseFade 0.35`, `springStiffness 120`, `springDamping 12`.

## 빌더 동작
1. 이전 빌드(`PianoKeyboard` 보유 오브젝트) 삭제 → 재실행 가능(idempotent).
2. FBX instantiate + unpack, 흰 건반 median pitch 로 **흰 건반 폭 ≈ 0.7 world** 되게 스케일(현재 ≈3.18×).
3. 슬라임 X 중앙 + 키보드 top = 슬라임 −`DropBelowSlime`(1.5) 에 배치, sensor 레이어 = 슬라임 `groundLayer`(현 `Environment`).
4. 흰 건반당 정적 sensor(폭 = 이웃 흰 건반 pitch ×1.02, 약간 겹쳐 seam 빠짐 방지) + `AudioSource` + `PianoKey` 생성, 클립/pitch 할당.

## 현재 씬 상태 (MainScene)
- 빌드 완료·저장됨. 52 흰 건반, 52 sensor, 52 클립. 슬라임(공중, 21.6/204.5)이 키보드(top y≈203, x[5.4~37.8]) 의 건반 44(E4)/45(F4) seam 바로 위 → Play 시 낙하·**2건반 동시 타격** 데모.

## 하모나이즈 모드 (opt-in 진행형 화음) — `PianoConfig.harmonize`
`harmonize.enabled` 켜면 밟은 건반의 **원음 대신 착지마다 코드 진행이 1칸 전진**하며 화음을 낸다(끄면 위 literal-note 동작 그대로, 무변경). 리서치 4편(`docs/클로드 연구/`) 수렴 설계.

- **진행 = authored 스키마 7종 × 4코드 = 28스텝**. 루프 **안**은 항상 순서대로(기능화성 T→S→D→T 유지 = "곡처럼" 들리는 이유), 확률은 **각 루프의 4번째 코드에서만** 굴려 다음 스키마를 고른다. 스키마 전부 코퍼스에 이름이 붙은 실제 진행:

  | | 진행 | 출처 |
  |---|---|---|
  | S1 | C – G – Am – F | 싱어송라이터 회전형(U2 "With or Without You" 형) |
  | S2 | C – Am – F – G | 두왑(1960s pop) |
  | S3 | Am – F – C – G | 싱어송라이터 원형 vi–IV–I–V |
  | S4 | C – Am – Dm – G | 프레이즈 모델 확장형 "I VI II V I" |
  | S5 | F – G – A♭ – B♭ | 차용화음(병행단조), 루프당 6%로만 진입 |
  | M1 | **Am – F – G – Am** | A 에올리안 i–VI–VII–i |
  | M2 | **Am – Em – F – G** | A 에올리안 i–v–VI–VII |

  M1·M2는 **나란한단조**라 전부 흰건반(임시표 없음) — C장조 펜타토닉 = A단조 펜타토닉이라 멜로디 스냅도 그대로 안전. 각 ChordStep = tonic 기준 semitone offset 보이싱(**최저음 F2(41) 이상** — 그 아래는 피치시프트 샘플에서 탁해짐) + release. 착지 = **aggregate rising edge**(모든 건반 해제→≥1 접촉) 1회당 1전진 → seam 2건반 = 1전진, 건반간 크롤 = 전진 안 함.
- **장/단 교대**: 장조 루프 끝 = 자기 반복 0.24 / 다른 장조 0.08×3 / 단조 0.23×2 / 차용 0.06 → **루프마다 약 50% 확률로 장↔단 전환**(10000루프 시뮬레이션 실측 50%). 단조 루프 끝은 장조 복귀 0.13×4 / 단조 0.21×2 / 차용 0.06.
- **멜로디**: 착지 건반 위치 → 현재 스케일로 스냅한 음을 보이싱 최상단 위에(`SnapMelody`). `snapMode` 3모드 = {Pentatonic(기본, 안 틀림) / ChordTones / ScaleFull}. **스텝별 `overrideSnapMode`** — 조성 밖 차용화음(♭VI/♭VII)은 ChordTones로 강제해야 펜타토닉 멜로디(라♮·미♮)와 반음으로 부딪치지 않음(검증: 충돌 0). 이음새 2건반 = **dyad**(둘 다 울림, ×`dyadVolumeScale`, 동음이면 옥타브 올려 분리).
- **velocity density**: 강한 착지 = 최상단 음 옥타브 더블링(`octaveDoubleVelocity` 임계) + 음량 증가.
- **다음 코드 선택 = 3단 우선순위**: ①**zone 분기**(`branchingEnabled`, 착지 X 3분할 → `leftNext/centerNext/rightNext`, -1이면 통과) → ②**가중치 랜덤**(`randomBranching`, 스텝의 `nextOptions[]` = {stepIndex, weight}) → ③선형 i+1. `roll < 0`이면 ②를 통째로 건너뜀(토글 OFF 경로). 기본 진행은 zone 전부 -1이라 **가중치가 담당**.
- **확률은 authored 후보 안에서만**: 무작위 생성/마르코프 아님. 리서치 결론 — *"이 게임은 '항상 좋게'가 핵심이라서, 무작위성이 만들어내는 신선함보다 작가가 미리 승인한 안전한 분기가 더 가치 있습니다"*(`docs/클로드 연구/착지로 전진하는 피아노 발판 게임을 위한 코드 진행 시스템 심층 리서치.md`).
  **실패 이력**: 처음엔 거의 모든 스텝에 확률을 달았더니 기능화성 순환이 매번 끊겨 "막 치는 것처럼" 들렸음(유저 반려). 확률을 **루프 경계로만** 격리한 뒤 해결.
- **연주 장식 11종** (`harmonize.ornaments`, 전부 개별 on/off + 기본 OFF). 규칙: **모든 장식의 첫 음은 착지 순간에 즉시 울린다** — 뒤 음만 지연되므로 클럭이 없고 시각-청각 동기가 안 깨짐. 지연 음은 `PianoVoicePool`의 pending 큐가 `Tick`에서 소화.
  - ① **롤드 코드**(아르페지아토) — 화음을 낮은음→높은음 20~40ms 간격으로. 근거: "arpeggios may be called rolled chords … read as to be played from the lowest to highest note"(Wikipedia), 바로크 건반의 관행적 스프레드
  - ② **보이스리딩** — 직전 화음에서 가장 적게 움직이는 옥타브로 윗성부 재배치(`HarmonyMath.VoiceLead`, 아래 `voiceLeadingKeepLow`음은 고정 = 저역 간격 보존, 멜로디 아래로 ceiling 제한). S1 한 바퀴 총 이동량 83 → 67 실측
  - ③ **페달 포인트** — 스키마 첫 코드에서 저역 으뜸음 1개를 긴 테일로
  - ④ **알베르티**(강착지 한정) — low-high-middle-high = 코드톤 `{0,2,1,2}`. 근거: Wikipedia/Open Music Theory, 게임 선례 SMB2 엔딩 테마
  - ⑤ **칩튠 아르페지오 버스트** — 코드톤 고속 순환. 근거: "In early video game music, arpeggios were often the only way to play a chord since sound hardware usually had a very limited number of oscillators"
  - ⑥ **아포자투라** — 착지 순간엔 한 음 위, 80~150ms 뒤 본음으로 해결(`NeighborAbove`)
  - ⑦ **서스펜션 4-3** — 직전 화음의 음을 붙들었다가 한 음 아래로 해결. 근거: "approached by the same note and resolves down by step"(Open Music Theory). 칩튠·알베르티와는 동시 사용 안 함(그 음형들이 모든 코드톤을 다시 쳐서 중복됨)
  - ⑨ **Drop-2 오픈 보이싱** — 위에서 두 번째 음을 옥타브 내림. 근거: "Closed-position voicings are dense … Open voicings produce a fuller, more resonant sound"
  - ⑩ **서스테인 페달 연동** — 슬라임이 `PianoPedal`을 밟는 동안 모든 릴리즈 테일 ×`pedalReleaseMultiplier`. 페달은 그 전까지 소리에 아무 영향이 없는 순수 소품이었음
  - ⑪ **심퍼세틱 레조넌스** — 최저음의 옥타브·12도 위를 아주 작게 동시 발음
  - ⑬ **글리산도 런** — 직전 착지에서 `glissMinSemitones` 이상 건너뛰었으면 진행 방향으로 짧은 런(점프 거리 → 음악)
  - **⑧ 앤티시페이션은 구조상 불가**: "occur immediately before a change of harmony"인데 다음 착지 시점을 알 수 없음(물리 입력). 같은 이유로 트릴·트레몰로 등 지속 음형도 제외
  - 화음 음형 우선순위(동시 ON 시): **칩튠 > 알베르티 > 롤드 > 블록**. 보이스리딩·페달포인트·레조넌스는 독립
- **건반 시각 = 울린 음마다 1:1** (2026-07-23 재설계): 발음의 유일한 관문인 `PianoVoicePool`이 `NotePlayed(midi, volume, release)`를 **음이 실제로 울리는 순간**에 발사 → `PianoKeyboard.OnNoteSounded`가 해당 MIDI 건반을 누름. 덕분에 롤드 코드의 3번째 음, 서스펜션의 180ms 뒤 해결, 칩튠 버스트가 **전부 제 타이밍에** 건반으로 보임. 장식 코드는 시각을 전혀 신경 쓰지 않음(새 장식 추가 시 배선 불필요).
  - **88건반 전부 대상**: 흰 52 + **검은 36**. MIDI는 메시 이름에서 산출(`PianoKeyVisual.MidiFromName`, 빌더와 같은 규칙 `20 + index` → `PianoKey.001`=21 … `.088`=108). 씬 검증: 88개 전부 고유 MIDI, 흰 건반 52개는 컴포넌트 `midiNote`와 불일치 0. 차용화음 A♭·B♭가 **실제 검은 건반**을 누름(콜라이더는 안 붙임 = 여전히 밟을 수 없음)
  - **`PianoKeyVisual`**(신규, 런타임 `AddComponent` — 빌더·씬 무변경): 자기 `localPosition`만 조작(collider/rb 불변 = 분리 규칙), `LateUpdate`에서 애니메이션(모터의 FixedUpdate 보간 뒤에 합성 + 프레임레이트). **누적 규칙**: 같은 건반에 겹치면 시간·깊이 모두 `max`(짧은 장식음이 긴 음을 끊지 않음). 슬라임이 물리로 밟고 있는 건반은 코스메틱 dip 생략(이중 침하 방지)
  - **유지 시간 = 소리 길이**: `clamp(release × visualHoldScale, visualHoldSeconds, visualMaxHoldSeconds)`. 단 **페달을 밟는 중이면 `visualPedaledHoldSeconds`(0.25s)로 짧게** — 실제 피아노처럼 댐퍼가 떨어져 건반을 떼도 소리가 남고, 페달 효과가 눈으로도 구분됨
  - **깊이 = 음량**: `visualSinkDepth × lerp(visualMinDepthScale, visualDepthVelocityScale, 음량)`. `visualMinVolume` 미만(레조넌스 같은 미세음)은 건반 안 움직임
- **오디오**: `PianoVoicePool`(키보드 자식 `PianoVoices`) — N개(`voicePoolSize`) AudioSource 풀, 건반 clip에서 `midi→clip` 맵 구성해 각 음을 `2^(반음/12)` 피치로 재생(검은음·비샘플음 = 최근접 흰건반 시프트), 선형 release tail + 지연 발음 pending 큐(`Tick`). harmonize ON이면 각 `PianoKey`는 `SetMuted(true)`로 자기 strike 음 소거(풀이 오디오 소유). `sfxObjectGroup` 라우팅.
- **리셋**: 마지막 전진 후 `resetIdleSeconds`(**기본 10s**) 동안 무착지 → **현재 스키마의 첫 코드**로 복귀(전역 0번이 아님). 전역 0으로 보내면 플랫포머의 정상적인 공백마다 S1으로 되돌아가 갈아탄 스키마가 증발함(실제로 그 버그가 있었음).
- **리버브(선택, 수동 배선)**: `reverbEnabled` + 믹서에 Reverb 이펙트 + `reverbWetParam` 노출 파라미터가 **있어야** 동작. `MainMixer.mixer` 기본엔 리버브 없음 → SFX_Object(또는 전용 Piano) 그룹에 Reverb 이펙트 추가 + wet(dB) 파라미터를 `PianoReverbWet`로 노출해야 함. 없으면 `SetFloat` 무시(graceful).
- **런타임 토글**: 매 FixedUpdate `enabled` 감시 → 인스펙터에서 플레이 중 on/off(ON = 풀 생성 + 전 건반 mute + 건반 시각 맵 구축 / OFF = 풀 해제 + unmute + 시각 복귀).
- **순수 함수**: `HarmonyMath`(BuildChord / SnapMelody / Zone / NextIndex / WeightedNext / VoiceLead / DropTwo / Neighbor / ApplyVelocityDensity), `PianoKeyVisual`(MidiFromName / DipFactor) — UnityEngine.Object 비의존, EditMode 테스트 `PianoHarmonyMathTests` + `PianoKeyVisualTests`로 검증.
- **파일**: `PianoConfig.HarmonizeSettings`/`ChordStep`/`NextOption`/`OrnamentSettings`(+enum), `PianoKeyboard.Harmonize.cs`(partial 상태머신), `PianoVoicePool.cs`, `PianoKeyVisual.cs`, `HarmonyMath.cs`, `PianoKey`(`muted`/`IsPhysicallyPressed`/`Mesh`/`Clip`/`Pitch`/`OutputGroup`), `PianoPedal.IsPressed`. 빌더 무변경.

## 자동 반주 — 시도했다가 **전량 폐기** (2026-07-23)
`dspTime` 고정 BPM 클럭 위에 베이스+아르페지오+퍼크를 깔았고(arranger-lite, 리서치 2편 권장), 그 다음 프레스-구동 재설계(스트릭 언락 + 플레이어 착지 간격에서 BPM 추정 + 알베르티 베이스 + A/B 섹션)까지 만들었으나 **둘 다 플레이 테스트에서 반려**됨.

- 1차 실패 원인: 코드는 착지마다 전진하는데 반주는 자기 박에 그 인덱스를 읽음 → 두 리듬이 어긋남. 유저 평: *"내가 건반 누르는거랑 하나도 안 맞고 지맘대로 실행돼"*
- 2차(주도권을 클럭으로 넘겨 어긋남을 구조적으로 제거한 버전)도 반려. 유저 평: *"LLM한테 노래 만들어달라면 만들어주는 그런 퀄리티"* — 어긋남이 아니라 **자동 생성 반주의 음악적 퀄리티 자체**가 문제였음.
- 결론: **자동 반주는 이 게임에서 하지 않는다.** 곡을 흐르게 하려면 사람이 쓴 곡이 필요하고, 그건 이 메커닉(불규칙 착지)과 별개 문제. 코드/에셋 전량 삭제(`AccompanimentMath`, `PianoKeyboard.Accompaniment.cs`, `SongMath`, `PianoKeyboard.Song.cs`, `AccompanimentSettings`/`SongSettings`/`GrooveLayer`/`PercTrack`). `Assets/Audio/Perc/vcsl_*.wav` 3개는 미사용으로 남아 있음.
- 리서치 자체는 유효하게 남음 — 아래 확률 분기가 그 결론(`"작가가 미리 승인한 안전한 분기 > 무작위 생성"`)의 직접 구현.

## 한계 / TODO
- **머티리얼 미적용** — repo `.mat`(텍스처 없는 단색, 파이프라인 불일치 가능) 생략. 키 URP 기본(회색)로 렌더. 흰/검/우드 머티리얼 부여는 후속.
- **배치는 테스트용** — 기존 terrain 과 비주얼 겹침. 최종 위치는 `Piano` 루트 이동/스케일로 조정(감지·sensor 가 transform 따라가 robust).
- FBX mesh 라이선스 확인.
- 검은 건반은 **밟을 수 없음**(콜라이더 없음, 의도) — 단 2026-07-23부터 소리에 맞춰 **시각적으로는 눌림**(차용화음 A♭/B♭ 등).
- `Assets/Audio/Perc/vcsl_*.wav` 3개는 폐기된 자동 반주용으로 받아둔 것 — 현재 미사용.
