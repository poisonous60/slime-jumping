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

## 유휴 자동 연주 — 녹음 재생 + 건반 동기 (2026-07-24) — `PianoConfig.autoPlay`
**위 "자동 반주"와 다른 것이다.** 폐기된 쪽은 *생성된* 반주였고 반려 사유가 "자동 생성 음악의 퀄리티"였다. 이쪽은 **사람이 작곡·연주한 녹음을 그대로 트는 것**이라 그 반려 사유가 원천적으로 없다. 생성 요소 0.

**핵심 = 곡이 화성의 주인이다.** 슬라임이 피아노에 **올라타는 순간** 다음 곡이 뽑히고 그 곡의 조성으로 진행 전체가 전조된다 → 슬라임이 치는 화음이 이미 그 곡에 속해 있다 → 떠나서 곡이 시작되면 **이어서 들린다**. 이게 "방금 누른 음이랑 곡이 이어져야 한다"의 구현이다.

동작 순서:
1. **도착**(건반 접촉 rising edge, 직전에 away였을 때) → `SelectNextSong` → 다음 곡 선택 + 조성/장식 override 적용 + 진행 인덱스 리셋
2. 슬라임이 `minPresses`(기본 3)회 이상 착지 — 이 동안의 화음은 전부 그 곡의 조성
3. `idleSeconds`(기본 3s) 동안 건반 미접촉 → 녹음 재생 + **88건반이 그 곡의 노트대로 스스로 눌림**
4. 슬라임이 건반에 복귀 → `fadeOutSeconds` 페이드아웃 후 반환

**호출 순서가 계약이다**: `PianoKeyboard.FixedUpdate`에서 `AutoPlayStep`이 `HarmonizeStep`보다 **먼저** 돌아야 한다. 둘 다 같은 rising edge에 반응하는데 harmonize가 먼저 돌면 **매 방문의 첫 화음이 이전 곡의 조성으로 울린다**(Codex 리뷰 적발). 순서 바꾸지 말 것.

- **소리는 전적으로 녹음(mp3)에서 나온다. 샘플러 아님.** 실연 녹음이라 샘플러 재연주보다 음질이 좋고, 아래 라이선스 제약도 이 구조라야 통과한다.
- **클립의 재생 헤드가 유일한 클럭**: `TickAutoPlay`가 매 프레임 `AudioSource.time`을 읽어 그 시점까지의 노트를 소비한다. deltaTime 누적도, 자체 타이머도 없다 → 프레임 히칭·루프·일시정지 어느 경우에도 건반과 소리가 어긋날 수 없다. (폐기된 자동 반주 1차 실패가 정확히 "두 리듬이 각자 클럭을 가져서" 발생했음)
- **건반 시각은 harmonize와 완전히 같은 경로**: `ResolveKeyVisual(midi)` → `PianoKeyVisual.Press`. 시각 튜닝 파라미터도 `harmonize.visual*`를 그대로 재사용한다(같은 악기 = 한 곳에서만 조절). `autoPlay.visualDepthScale`로 깊이 배수만 따로 준다.
- 눌림 유지 시간 = **전사된 실제 음 길이**(`ScoreNote.dur`)를 `visualHoldSeconds`~`visualMaxHoldSeconds`로 clamp.

### 전사 파이프라인 (오프라인, 1회성)
곡 → 노트 이벤트는 **로컬 오프라인** 도구로 뽑는다.

| 단계 | 도구 | 비고 |
|---|---|---|
| audio → MIDI | **[transkun](https://github.com/yujia-yan/transkun) 2.0.1** (MIT, ISMIR 2024) | MAESTRO V3 note+offset+velocity **F1 0.9505**, 벨로시티·서스테인 페달 출력 |
| MIDI → score JSON | `midi_to_score.py` (scratchpad) | pretty_midi 파싱 + librosa로 키/코드 추정 |
| score JSON → `preview` 필드 | **`tools/extract_preview.py`** (repo, stdlib only) | 도입부를 **타건 단위**로 추출(아래 "곡별 미리보기" 참조). `--dry-run` 리포트만 / `--selftest` 자체 검증(블록·브로큰·셈여림·페달 4종 합성곡) |

- **Windows 설치 함정**: `transkun` 의존성 `ncls`는 최신(0.0.70)에 win_amd64 wheel이 없어 MSVC 빌드로 넘어가 실패한다. Python **3.11** venv + **`ncls==0.0.39` 핀**을 같은 resolve에 넣어야 함 (`uv pip install transkun "ncls==0.0.39"`). 3.13에서는 어느 조합으로도 안 됨.
- **자작 DSP는 폐기했다.** onset + CQT 하모닉 억제 방식을 직접 구현했으나 객관 지표에서 실패:

  | | 자작 DSP | transkun |
  |---|---|---|
  | chord-tone agreement (우연=0.25) | 0.303 | **0.409** |
  | semitone clash rate (버그 지표, 낮을수록 좋음) | **0.597** | **0.000** |
  | diatonic fit | 0.716 | **0.821** |
  | 키 추정 | D# major r=0.62 (코드열과 모순) | **G minor r=0.74** (pc 히스토그램과 일치) |

  실패 원인: CQT 빈이 이웃 반음으로 새서 **동시 음정의 43%가 "반음"** — 실제 피아노곡에 있을 수 없는 분포. 이미 풀린 문제를 더 나쁘게 다시 푼 것이라 중단.
- **온라인 전사 서비스(Klangio/Songscription 등)는 쓰지 않는다** — 품질 이점이 검증되지 않은 데다, 아래 라이선스가 제3자 업로드를 금지한다.

### 라이선스 — 이 구조를 강제한 이유
현재 곡 `Assets/Audio/Music/OpenAndAiryPiano.mp3` = *"Open and Airy Piano"* (album "Solo Piano", 작곡 Doug Frantz(BMI), 퍼블리셔 Sounds Incredible Publishing(SOCAN), 라이선싱 **AudioHero**, SourceAudio ID 16916260, 2017). ID3 태그에 전부 박혀 있음.

[AudioHero EULA](https://www.audiohero.com/end-user-license-agreement) 기준:
- ✅ **게임에서 이 녹음을 트는 것** — `"You may create copies of the Production for distribution without limitations"`, 허용 용도에 `computer games` 명시. 길이/피치/포맷 변형도 허용.
- ✅ **녹음에 맞춰 화면 요소(건반)를 움직이는 것** — 권리 범위가 `"synchronization rights only"`이고 이게 정확히 싱크다.
- ❌ **전사해서 샘플러로 재연주** — 새 녹음을 만드는 것이라 mechanical right 영역. 싱크권에 없다. 추가로 `"Add new instruments, melody or lyrics to an original music composition ... without the prior consent of the owner"` 금지.
- ⚠️ **AI/ML 조항**: `"Use AudioHero royalty free producto in an AI (Artificial Intelligence), Machine Learning or Neural Network system, app or program for any purpose"` 금지 — LIMITATIONS 섹션, 제품 구분 없음. transkun은 뉴럴넷이므로 **문자 그대로는 걸린다**. 조항 취지는 학습 데이터/생성 클로닝 방지이고 오프라인 1회 전사에 대한 현실 적발 가능성은 사실상 0이지만, **계약서에 있는 건 사실**. 상용 출시 전 `support@audiohero.com`에 사용 형태를 밝히고 confirm 받는 것을 권장(금지 문구가 `without the prior consent of the owner`라 허락 경로가 열려 있음).

→ **결론: 소리는 mp3, 건반은 전사 데이터.** 샘플러 재연주 경로를 택하지 않은 것은 취향이 아니라 라이선스 제약이다. PD 곡(사티·쇼팽·바흐 등)으로 바꾸면 그 제약이 전부 사라진다.

### 곡별 미리보기 — 실측 타건 재현 (2026-07-27 재작성)
**한 스텝 = 코드가 아니라 곡이 실제로 친 타건 하나.** 착지를 여러 번 하면 곡의 도입부가 타건 단위로 지나간다.

앞선 두 판(조성·장식 배정 → 도입부 코드열 + texture 스칼라 5개)이 모두 같은 이유로 실패했다: **곡 간 차이는 코드 심볼 *위*에 있는데 그 층을 평균내 버렸다.** 코드 라벨과 전역 스칼라만 남기고, 화음은 템플릿에서 재구성하고, 보이스리딩이 옥타브를 다시 배치하고, 그 위에 펜타토닉 스냅 멜로디를 얹으니 — 어떤 음을 함께 치는지, 얼마나 세게, 얼마나 길게, 무엇이 위에서 노래하는지가 전부 사라져 **모든 곡이 같은 블록 화음**으로 나왔다. 유저 판정: *"화성이 똑같아도 곡을 들어보면 다른데, 지금은 곡이랑 상관없이 들린다"*.

- **스텝 = 제스처 예산(`GESTURE_WINDOW` 0.35s) 안에 들어오는 곡의 타건들.** 도입부에서 `MAX_STEPS`(16) 스텝을 뽑고 루프. 블록 곡은 한 번에 여러 음, 느린 곡(ADarkBlue: 타건 간격 1s)은 한 타건, **빠른 곡(APositiveMedium2: 간격 0.21s)은 곡의 실제 간격 그대로 굴러가는 짧은 음형** — 음형을 고른 게 아니라 곡의 타건 간격이 그렇다.
  - 처음엔 `1스텝 = 1타건`이었는데 유저 판정에서 걸렸다: *"APositiveMediumPiano2는 곡이 빠른데 하나씩 치는 걸론 이 속도가 표현이 안 돼."* 착지가 ~1초 간격이라 어택 간격 0.21초짜리 곡을 5배 느리게 재생하는 셈이었다. 예산 안에서만 묶으므로 figure는 항상 다음 착지 전에 끝나고(겹침 없음), 예산 밖 타건은 다음 착지 몫이다. 예산을 0.08로 낮추면 착지당 1타건(이전 동작)으로 돌아간다.
- **`support`** = 그 구간 음 전부, **노래하는 음 포함**(그 음에 `sings` 플래그). 음마다 `midi` / `dt` / `vel` / `hold` / `fall`.
  - 처음엔 노래하는 음을 빼고 그 자리에 밟은 건반을 꽂았다가 반려됨(*"멜로디를 직접 치는 게 오히려 안 어울린다"*) — 곡의 선율선이 사라지고 임의 음이 제일 앞에 나와 겉돌았다. 지금은 곡이 친 건 다 울리고 **밟은 건반은 `snapPcs`(그 순간 곡이 쓰는 피치클래스)로 스냅되어 그 화음의 평균 음량·길이로 섞인다**. 곡이 이미 그 음을 치고 있으면 겹쳐 치지 않는다(그럼 그 성부만 커짐). `previewPlayerAsLead`로 예전 방식 복구.
- **화음 뭉침**(`previewChordBlur`, 기본 켬) — 곡의 음을 **순서대로 따라 치지 않는다.** 그 구간 음들을 한 화음으로 모아 `blurRollSeconds`(0.03)만큼만 굴려서 낸다.
  - 유저 판정: *"곡이 친 음을 그대로 따라하는 게 오히려 어색하다. 화음 위주로 두루뭉실하게."* 전사를 문자 그대로 재생하면 곡을 어설프게 흉내내는 것처럼 들린다 — 화성과 연주 방식만으로도 인상은 전달된다.
  - **버리는 건 선율 순서뿐**. 세기·눌림·잔향·음역은 전부 실측값 그대로다. 곡이 촘촘할수록 뭉치는 음이 많아져 자연히 두꺼워지므로, figure 묶음이 만든 밀도 차(빠른 곡 = 두꺼움)는 그대로 남는다.
  - 끄면 곡의 실제 간격·순서대로 굴러간다(비교용).
- **`vel`은 전사값 그대로, 정규화 없음.** 타건별로 정규화하면 "모든 착지가 똑같이 크다"가 되고, **곡별로 정규화하면 "모든 곡이 똑같이 크다"가 된다** — 곡마다 어딘가에서 반드시 1.0을 찍기 때문. 유저 판정에서 걸림(*"ASpringGreen 미리보기가 좀 크게 들린다"*): 그 곡 평균 세기 0.54가 0.77로 ×1.45 올라간 반면 센 곡 ADarkBlue는 0.72→0.86(×1.19)이라, 여리게 친 곡이 센 곡 옆에 붙어버렸다. 절대 레벨은 `masterVolume` 하나가 담당한다.
- **길이는 `hold` + `fall` 두 값** — 한 값으로 뭉개면 안 된다. `hold` = 곡의 실제 키다운 시간, `fall` = 그때 페달이 내려가 있었으면 페달이 올라갈 때까지(없으면 0 → 런타임 `damperSeconds`).
  - 처음엔 "울린 시간" 하나로 뭉쳤다가 유저 판정에서 걸렸다: *"ASpringGreen은 통통 튀는 느낌인데 한 번 눌렀을 때 지속 시간이 너무 길다."* 그 곡의 음 길이는 **0.215초**인데 페달이 3.37초 내려가 있어서 모든 음이 3.4초로 늘어났고, 런타임은 그동안 게인을 1.0으로 붙잡고 있었다. **페달이 내려가 있다 = 그 음이 풀볼륨으로 3.4초 난다가 아니라, 댐퍼가 안 내려와서 자연 감쇠한다**는 뜻이다. 0.2초 바운스 + 3.2초 꼬리가 맞다.
  - `fall`이 0보다 크면 곡이 이미 페달을 밟은 것이므로 **슬라임이 페달 오브젝트를 밟아도 더 안 늘린다**(이중 적용 방지). 페달을 안 밟은 음만 `damperSeconds × sustain`.
- **음량·음색은 곡이 친 세기로만**(`previewUseLandingVelocity` 기본 off). 착지 속도는 안 쓴다 — 세게 착지했다고 곡보다 크게 치면 재현이 깨진다. 리버브 send도 같은 규칙.
- **끊기는 시점 = 실측 길이와 밟은 시간의 max**(`sustainWhileStanding`, 기본 켬, 2026-07-27) — 실측 `hold`가 수십 ms거나 오탐인 구간에서 한 번 밟은 음이 즉시 끊겼다(유저 판정 *"한 번 눌렀을 때 너무 음이 짧게 지속되는 구간이 있다"*). **둘 중 하나를 고르지 않는다**: 밟고 있는 동안에도 실측 `hold` 카운트다운은 계속 돌고(누르는 게 음을 재시작하지 않는다) 눌림이 막는 건 댐퍼뿐이므로, 짧은 실측은 점프까지 유지되고 긴 실측은 떠난 뒤에도 남은 길이만큼 더 울린다. 키업은 접촉 falling edge → `PianoVoicePool.ReleaseSustained()`, 그 뒤 댐퍼는 그 음의 `fall`(실측 페달 잔향, 없으면 `damperSeconds`)로 내려온다. 지연 발음은 **실제 발음 시점에** 플래그를 읽으므로 건반이 이미 올라온 뒤 울리는 장식음은 안 붙들린다. 스키마 폴백 경로에도 같이 걸린다(플래그를 두 경로 공통 지점에서 세팅).
- **밟은 건반 → 화음 음으로 강제**(`snapLandedKeyToChord`, 기본 끔, 2026-07-27) — 스냅 후보를 `snapPcs`가 아니라 **그 figure가 실제로 친 음들의 피치클래스**로 좁힌다. `snapPcs`에는 곡이 노래하는 선의 경과음까지 들어 있어서 화성 밖 반음이 남을 수 있었고, 좁히면 밟은 건반은 **울리는 화음의 음일 수밖에** 없다(이동량은 여전히 최대 온음 = 밟은 자리 근처).
  - **건반 소리를 죽이는 방식은 반려됐다**(*"밟은 근처의 소리는 나야해. 화성에 맞춰서 나야하지만, 나긴 해야한다고"*) — 첫 구현은 밟은 건반을 아예 넣지 않았고 유저 판정 *"아예 무시하는 느낌"*.
  - **화음으로 스냅하면 곡이 이미 그 음을 치고 있는 경우가 기본**이 된다. 똑같은 피치를 겹쳐 치면 샘플·온셋이 동일해서 **별개의 발음으로 안 들리고 그 성부만 커진다** — 그래서 겹칠 때 옥타브를 옮긴다(88건반 범위 안에서 위 우선). 예전처럼 건너뛰면 다시 "무시하는 느낌"이 된다.
  - 스키마 폴백 경로에선 스냅 모드를 `ChordTones`로 강제(장식용 `melSnap`까지 동일). lead 모드와도 공존한다 — 노래하는 음을 대신하되 화음 음으로만.
- **`melodyPcs`** = 그 코드 구간에서 곡이 노래하는 음들의 피치 클래스. 밟은 건반을 여기로 스냅하므로 **상성부는 곡의 음재료이면서 플레이어 입력에 답한다**. `melodyVel`/`melodyRing`은 그 타건이 실제로 노래했으면 그 음의 값, 아니면 구간 중앙값.
- **런타임은 재구성하지 않는다.** preview 스텝이 있으면 `PlayPreviewGesture`가 보이스리딩·velocity density·장식 전부를 건너뛴다 — 그것들은 전부 *화음 모양을 지어내는* 장치이고, 지어내는 것이 곡의 실측 보이싱을 덮어써서 모든 곡을 같게 만든 원인이다.
- **밟은 건반이 곡 바깥일 때**: `snapPcs` 안으로 최대 온음만큼만 굽힌다. `melodyClashSemitones`(1) 이내 지지음 제거는 **lead 모드 전용** — 거기선 밟은 건반이 멜로디를 대신하니 옆 음이 뭉갠다. 섞는 모드에선 곡이 친 걸 빼지 않는다.
- **제스처 예산**: 런타임 `gestureMaxSpreadSeconds`(0.35, 추출 `GESTURE_WINDOW`와 같은 값)를 넘으면 **압축**하되 잘라내지 않는다(꼬리 음을 버리면 figure가 해체됨). `gestureMaxRingSeconds`(6)로 울림 상한.
- 코드 라벨(HarmAn)은 남지만 이제 **멜로디 스냅 집합과 리포트용**이다. 화음을 만드는 데는 안 쓴다.

### 연주 표현 — 세기·길이·음색 (2026-07-27)
전사에 노트별 세기·길이가 있는데도 전부 같은 음량·같은 길이·선형 페이드로 나가던 층. *"강약이나 셈여림, 음 길이 표현이 안 되고 있어서 그런 것 같다"* 는 유저 진단이 맞았고, 리서치 2편도 같은 5요인을 지목했다(`docs/클로드 연구/피아노 발판 …`).

- **눌림 유지 → 지수 감쇠**. 클립은 실제 피아노 녹음이라 **이미 스스로 감쇠한다**. 릴리즈 내내 선형으로 줄이던 이전 방식은 그 자연 감쇠와 싸워서 "샘플이 재생된다"는 인상의 최대 원인이었다. 지금은 실측 `hold` 동안 게인을 건드리지 않고, 그 뒤 `fall`(곡의 페달 잔향, 없으면 `damperSeconds` 0.18) 동안 −60dB까지 지수로 떨군다.
- **세기 → 음색**(`brightnessEnabled`). 실제 해머는 경화 스프링이라 세게 칠수록 밝다. 건반당 샘플이 1개라 **밝게는 못 만들고 어둡게만 가능** — 약한 타건일수록 로우패스 컷오프를 기음 쪽으로 내린다(`brightnessSoftHz`~`brightnessHardHz`, 하한 = 기음 × `brightnessHarmonicFloor`). 보이스마다 `AudioLowPassFilter`.
  - **스윕 구간은 0~1이 아니라 `brightnessVelocityLow`~`High`(0.35~0.85)**. 전사 세기는 0~1을 다 안 쓰고 대략 0.4~0.8에만 몰려 있어서, 0~1에 매핑하면 그 구간이 컷오프 6.8k~9.4kHz밖에 안 움직여 **여리게 쳐도 안 어두워진다**. 유저 판정 *"실제 곡은 가볍게 여러 번 치는데 미리보기 치는 느낌이랑 다르다"* 의 음색 쪽 원인.
- **음량·길이가 음마다 다름**: `support[i].vel`이 그대로 음량 배수, `hold`/`fall`이 그대로 길이. 멜로디는 `melodyVel`/`melodyHold`/`melodyFall`.

### 런타임 배선
- **`HarmonizeSettings`의 `[NonSerialized]` override** — key/ornament와 같은 패턴에 `Progression`(ChordStep 리스트) + `PreviewSteps`(`ScorePreviewStep[]`) 두 개. `SetProgressionOverride`/`SetPreviewStepsOverride`, `ClearOverrides()`가 함께 해제.
- **두 배열은 인덱스 1:1**. `BuildPreviewProgression`이 JSON 스텝마다 ChordStep을 **하나씩, 건너뛰지 않고** 만든다(빈 offsets면 `{0}`) — 착지가 `harmonizeIndex`로 두 배열을 동시에 인덱싱하기 때문. 하나라도 스킵하면 화음과 타건이 어긋난다.
- **선형 루프는 데이터로 성립** — `new ChordStep()`의 leftNext=-1 / nextOptions 빈 배열이 `NextIndex`를 자동으로 `(i+1)%count`로 떨어뜨리고, `wasLoopEnd`가 절대 참이 안 돼 `harmonizeLoopStart`=0 고정(리셋 = 도입부 첫 타건 복귀). branching 끄는 코드 없음.
- **`PlayPreviewGesture`** (`PianoKeyboard.Harmonize.cs`)가 착지 경로에서 화음 재구성 블록 **전체를 대신한다**. `AdvanceHarmonizeStep`(리버브 + 인덱스 전진)은 두 경로가 공유. `PianoVoicePool.PlayExpressive(midi, vol, ring, delay, velocity)`로 나가고, 첫 음(멜로디)은 delay 0 — 착지 즉시. 건반 시각은 `NotePlayed` 경로로 공짜.
- **`PianoVoicePool`**: 보이스마다 `AudioLowPassFilter`. `Play(…)` 4인자는 예전 그대로 delay 의미(장식 경로 무수정), 세기까지 주는 건 `PlayExpressive` — 같은 이름에 velocity 오버로드를 얹으면 4인자 호출이 delay/velocity 사이에서 모호해진다.
- **유효성 판정은 내용 기반**(`PreviewUsable`): steps ≥ 2 + tonicMidi ∈ [60,71]. JsonUtility는 JSON에 `preview`가 없어도 기본 인스턴스를 만들 수 있어 null 체크만으론 안 된다. 같은 이유로 JSON을 ChordStep으로 **직접 역직렬화하지 않는다**(leftNext가 -1 대신 0이 되면 전 코드가 스텝 0으로 분기).
- **폴백**: preview 없음/부실, 또는 `previewGesture` off → 기존 스키마 + 장식 + 나란한장조 규칙 경로 그대로(변화 0).
- **수정 워크플로**(유저 확정): 스크립트 리포트(`--dry-run` — 곡별 진행 + 타건 수/성부 수 문자열 + 펼침·울림·셈여림 범위) 보고 파라미터 조정 후 재실행. 인스펙터 편집 필드 없음. 콘솔 확인은 `logSongChange` 의 `preview N strikes [1212121212121212]` 표기(숫자열 = 착지마다 눌리는 건반 수).
- **라이선스**: 진행·리듬 패턴은 보호 대상이 아니고 보이싱·샘플은 자작. 상성부는 곡의 피치 클래스로 스냅될 뿐 곡의 멜로디를 그대로 재생하지 않는다.

이전 판(2026-07-25/26)의 기록 — 무엇을 왜 버렸는지:

- 초기 판은 `preview`에 ①도입부 4~8코드 루프 ②**texture = 전역 스칼라 5개**를 구웠다. 그 둘은 2026-07-27에 위의 타건 스트림으로 대체됐다(`ScoreTexture` 타입·`SetTextureOverride`·`PlayChordFigure`의 texture 분기 전부 삭제).
  - 코드 추출 = **HarmAn** (Pardo & Birmingham, *Algorithms for Chordal Analysis*, CMJ 26:2, 2002) 자작 구현: partition point(음 시작/끝에서만 화성 변화 — **비트 그리드 불필요**라 rubato 연주 전사에 그대로 적용) + 템플릿 스코어 `S = P − (M + N)`(가중치 = 노트가 걸치는 minimal segment 수) + DAG 최적 세그멘테이션. 템플릿은 원저 6종 + **min7/maj7 추가**(원저는 "It is simple to add new templates" — pop 계열 m7가 min7 템플릿 없이는 hdim7로 오인됨, 실측으로 확인). 연주 온셋 흩어짐(~40ms)은 60ms ε-클러스터로 흡수.
  - **texture는 토글이 아니라 전부 측정값** (기성 장식 카탈로그를 곡에 배정하는 방식은 반려되어 폐기): `spreadSeconds`/`rollUp` = 화음을 펼치는 폭·방향(다중 온셋 attack의 중앙값 — 24ms 롤이면 24ms 그대로, 블록 곡은 ~3ms로 측정되어 자연히 동시타. 임계값·그리드 뭉갬 없음) · `releaseSeconds` = 잔향(반주 음길이 p75) · `bassReleaseSeconds`/`bassVolumeScale` = 저역 드론(MIDI<55에서 2s+ 지속음의 중앙값 — OpenAndAiry 5.5s가 그대로 나옴) · `chordVolumeScale` = 반주/탑보이스 음량비.
  - **착지 1회 = 발음 제스처 1회 (유저 계약, 2026-07-25)**: 착지가 시간차로 재타격하면 안 된다 — 곡의 오스티나토를 충실히 베끼더라도 금지. 이 계약으로 attack 간 `pattern` 재생(한 착지가 0.3~0.9s 뒤 3~4회 재타격)은 **도입 당일 폐기**했다. 펼침(수십 ms)·링잉·드론은 한 번의 발음으로 침.
  - **성부 수 = 스텝별 타건 제스처** (2026-07-26 교체): 각 코드 세그먼트 안에서 **가장 큰 온셋 클러스터 크기**(`gesture_size`) = 곡이 그 코드를 선언할 때 실제로 한 번에 치는 음 수가 그 스텝의 보이싱 상한. 이전의 전역 동시발음 밀도(`measure_density` — attack 시점에 *울리고 있는* 노트 수 중앙값)는 잔잔한 브로큰코드 곡(AFlintGrey: 타건 1~2음, 서스테인 겹침으로 4~5음 울림)을 5음 블록코드로 과밀 재생시켜 폐기. sounding 기반 수직 측정은 화성 라벨용(chordify/jSymbolic 관행), 제스처는 온셋 동기 기반(Huron 1989 텍스처 분류 축) — 조사 근거로 분리. 보이싱은 **그 구간에서 곡이 실제 친 음만**(안 친 템플릿 톤을 지어내지 않음 — 그게 "곡보다 건반이 많이 눌리는" 원인이었음), 곡이 실제 친 옥타브에 배치(dur×vel 가중 최빈, 근음 더블링도 곡이 하면 따라함), 제스처 초과분은 베이스 제외 최저 가중치부터 탈락. 유일한 보정 = F2(41) 하한·상한 76(멜로디 레인). 결과: 브로큰코드 곡은 착지당 화음 2~3성(+멜로디 1), 진짜 블록코드 곡(ADarkBlue 타건 5~7)만 4~5성, DramaticSensitive는 오프닝 강타 스텝(5성)만 두껍고 이후 얇음.
- **곡별 장식 프리셋 폐기**: `PianoSong.ornaments`의 카탈로그 장식(rolled/alberti/chiptune/pedalPoint/suspension/dropTwo/resonance/**appoggiatura/glissando**)은 전곡 off — "있는 장식을 곡에 적용"이 반려된 방식이라서. 2026-07-27부터는 preview 곡에서 **voiceLeading도 안 탄다**(실측 보이싱을 덮어쓰는 게 문제의 일부였음). 장식 코드 자체는 존치(로테이션 밖 수동 harmonize용).

### 조성 일치 — 자동, 그리고 **나란한장조 규칙**
곡 선택 시 전사 결과의 `score.key`에서 조성을 읽어 **자동 적용**한다. 단 그냥 tonic을 넣으면 틀린다:

> **(예외) preview 진행을 쓰는 곡은 이 규칙을 타지 않는다.** 진행이 곡 자체 조성으로 voiced되어 있어 tonic = `preview.tonicMidi`(단조면 실제 으뜸음 + Minor). 아래 +3 우회는 **스키마 폴백 경로 전용**이다 — 스키마가 tonic 상대라서 생긴 우회이므로. 멜로디 스냅 안전성은 동일(단조 펜타 = 나란한장조 펜타, 같은 음집합).

> **단조 곡은 tonic을 자기 으뜸음이 아니라 나란한장조(pitch class +3)에 놓는다.**

이유: authored 단조 스키마 M1/M2는 `ChordAmTonic = {-15,-8,-3,0}` 으로 **tonic의 나란한단조**를 만든다(C장조 tonic=60 → Am). 그래서 G단조 곡에 tonic 67(G)을 주면 M1/M2가 **E단조**에 떨어져 곡과 3도 어긋난다. tonic 70(B♭)을 주면 M1/M2 = G단조 ✓, 장조 스키마 = B♭장조 ✓ — 스키마가 원래 설계한 장↔단 교대가 그대로 곡의 조성 안에서 돈다.

`scale`은 그 경로에서 **Major로 강제**한다. tonic이 이미 나란한장조라 그 위의 장조 펜타토닉 = 곡 으뜸음의 단조 펜타토닉(같은 음집합, 위 하모나이즈 항목의 "C장조 펜타 = A단조 펜타"와 같은 근거). Minor로 두면 B♭단조 펜타가 되어 곡에 없는 음이 나온다.

실측(현재 로테이션 42곡, 19개 조성):
```
C  /Am  -> 60 (C)     C# /B♭m -> 61 (C#)    D  /Bm  -> 62 (D)     E♭ /Cm  -> 63 (E♭)
E  /C#m -> 64 (E)     F  /Dm  -> 65 (F)     F# /E♭m -> 66 (F#)    G       -> 67 (G)
Fm      -> 68 (A♭)    B♭ /Gm  -> 70 (B♭)    A♭m     -> 71 (B)
```
tonic은 항상 `[60,71]`에 놓는다 — authored 보이싱 최저 오프셋이 `-19`라 최저음이 `41..52`가 되어 "최저음 F2(41) 이상" 규칙을 정확히 만족한다(Codex가 전 조성 × 전 스키마로 검산).

`PianoSong.tonicMidi`에 0 이상을 넣으면 자동 추정 대신 그 값 + `PianoSong.scale`을 쓴다(-1 = 자동, 기본값).

### 런타임 override — 에셋을 절대 안 건드린다
`HarmonizeSettings`에 `[System.NonSerialized]` 필드 4개(`hasKeyOverride`/`overrideTonicMidi`/`overrideScale`/`overrideOrnaments`)와 읽기용 프로퍼티 `TonicMidi`/`Scale`/`Ornaments`를 뒀다. `tonicMidi`/`scale`/`ornaments`를 직접 쓰면 **에디터에서 그 즉시 `.asset`에 영구 저장되어 디자이너 튜닝이 소리 없이 파괴**되기 때문. `[NonSerialized]`가 디스크로 새는 걸 막고, 인스펙터 라이브 튜닝은 원래 필드로 그대로 살아있다.

`HarmonyMath`와 `PianoKeyboard.Harmonize.cs`의 **모든** 읽기가 프로퍼티로 전환됐다 — 한 곳이라도 raw 필드로 남으면 멜로디와 화음이 서로 다른 조성으로 갈린다(Codex가 잔여 raw read 0건 확인).

### 곡 추가 방법
`sound/music/` 에 솔로 피아노 오디오를 넣고 배치 스크립트를 돌리면 `Assets/Audio/Music/` 에 `<Name>.mp3` + `<Name>_score.json` 쌍이 생긴다. 그 다음 Unity에서 `PianoConfig.autoPlay.songs` 에 등록.

판정 기준(스크립트가 표로 출력): **key confidence ≥ 0.6** + 노트 수가 귀로 들은 것과 맞을 것. n/s가 비정상적으로 높은데 confidence가 낮으면 솔로 피아노가 아니었다는 뜻.

곡별 임포트 설정은 자동으로 잡아준다 — 60초 이하 `CompressedInMemory`, 초과 `Streaming`, 둘 다 `preloadAudioData`+`loadInBackground` ON. 기본값(`DecompressOnLoad`+preload OFF)은 30초 클립을 재생 프레임에 통째로 디코드해 히치를 만든다.

### 파일 / 설정
- **`PianoKeyboard.AutoPlay.cs`** (신규 partial) — 상태머신 전부. `AutoPlayStep(anyContacting)`은 FixedUpdate에서 도착·착지·복귀를 처리, `AutoPlayUpdate()`는 Update에서 트리거 판정 + 재생 tick.
- **`PianoScoreData.cs`** — `PianoScoreData`/`ScoreNote`/`ScorePedalSpan`/`ScoreKey` + **`ScorePreview`/`ScorePreviewStep`/`ScoreSupportNote`**(곡별 미리보기). JsonUtility DTO. JSON의 `chords` 필드는 런타임 미사용이라 무시됨(오디오 크로마 추정이라 신뢰도 낮음 — chordToneAgreement 0.41; 심볼릭 재추출인 `preview.steps`가 그 자리를 대체).
- **`PianoVoicePool.cs`** — `Envelope` 구조체(damper/brightness)를 착지마다 config에서 받아 세팅. `Play`(delay)와 `PlayExpressive`(delay+velocity) 분리. `sustainNewNotes` + `ReleaseSustained()`가 "밟고 있는 동안 풀게인 유지 → 키업에 댐퍼" 담당.
- **`HarmonizeSettings`의 `Performance expression` 블록** — `previewGesture`/`gestureMaxSpreadSeconds`/`gestureMaxRingSeconds`/`melodyClashSemitones`/`melodyAboveSupport`/`previewPlayerAsLead`/`previewChordBlur`/`blurRollSeconds`/`previewUseLandingVelocity`/`sustainWhileStanding`/`snapLandedKeyToChord`/`damperSeconds`/`brightnessEnabled`/`brightnessSoftHz`/`brightnessHardHz`/`brightnessHarmonicFloor`/`brightnessVelocityLow`/`brightnessVelocityHigh`.
- **`PianoVoicePool.PlayExpressive(midi, vol, hold, fall, delay, velocity)`** — `Play`(4인자)는 예전대로 delay 의미라 장식 경로 무수정.
- **`PianoConfig.PianoSong`** (신규) — `label`/`clip`/`scoreJson`/`tonicMidi`(-1=자동)/`scale`/`ornaments`.
- **`PianoConfig.AutoPlaySettings`** — `enabled`/`songs`/`minPresses`/`idleSeconds`/`cooldownSeconds`/`volume`/`loop`/`fadeInSeconds`/`fadeOutSeconds`/`visualDepthScale`.
- **에셋**: `Assets/Audio/Music/` 에 42곡 (30~39초 41곡 + 167초 1곡), 각각 mp3 + `_score.json`. `Assets/Audio/`는 gitignore이므로 리포에 안 들어간다 — 원본은 `sound/music/`.

### 하드 게인 — 두 번의 적대적 리뷰에서 나온 것
Claude 워크플로우(5렌즈) + Codex 리뷰가 잡은 실제 결함들. 재발 방지용으로 기록:
1. **호출 순서** — `AutoPlayStep`이 `HarmonizeStep`보다 먼저여야 함(위 참조). 이 기능의 존재 이유를 정면으로 깨던 버그.
2. **페이드 중 스코어 교체** — 슬라임 복귀 시 다음 곡이 선택되며 스코어 캐시가 갈리는데 이전 곡은 아직 페이드 중 → A의 재생 헤드로 B의 악보를 읽음. **재생 중인 스코어는 `autoPlayingScore`로 별도 고정**(캐시 `autoPlayScore`와 분리).
3. **`JsonUtility.FromJson`은 손상된 JSON에서 null이 아니라 throw** → Update 전체가 죽음. try/catch 필수.
4. **클립 디코드 히치** — 위 임포트 설정 항목.
5. **루프 감지 오작동** — 페이드아웃 중 non-loop 클립이 자연 종료하면 `AudioSource.time`이 0으로 읽혀 "루프됨"으로 오인, 커서가 되감겨 무음인데 건반이 눌림. 종료 판정을 페이드 분기 **앞**으로 올리고 "실제 재생된 적 있음"(`autoPlayHeard`) 게이트를 둠.
6. **검은 건반 2배 이동** — `PianoKeyVisual`이 localPosition을 쓰는데 흰 건반 메시는 `KeyMotor`(scale 1), 검은 건반 메시는 `Piano` 루트(scale 2) 자식이라 같은 depth가 2배로 나갔음. `ToLocal(world)`로 통일, `visualSinkDepth`는 이제 **world 단위**.
7. **보이스 스틸이 풀볼륨 음을 자름**(2026-07-27, 실측 타건 판) — 선형 릴리즈 시절엔 "끝나는 데 가까운 보이스" = "이미 조용한 보이스"였는데, 울림 유지 방식에선 아니다. 스틸 우선순위를 *빈 보이스 → 댐퍼 중 → 울리는 중 가장 조용한 것* 으로 바꿨다.
8. **페달 이중 적용**(2026-07-27) — 실측 `ring`에 이미 곡의 페달이 들어가 있는데 런타임 sustain 배수를 또 곱했다. `pedalled` 플래그로 분리.
9. **preview 경로가 legacy 상태를 안 갱신**(2026-07-27) — `prevChordScratch`/`prevLandingMidi`를 안 쓰고 return해서, 도중에 `previewGesture`를 끄면 보이스리딩·서스펜션·글리산도가 preview 이전의 낡은 화음을 읽었다. 제스처 경로가 두 값을 갱신하도록 수정.
- **라이프사이클 이동**: `Update()`/`OnDisable()`이 `PianoKeyboard.cs`(메인)로 올라가 `HarmonizeUpdate()`+`AutoPlayUpdate()` / `HarmonizeDisable()`+`AutoPlayDisable()`을 dispatch. CLAUDE.md의 "메인 파일엔 라이프사이클 orchestration만" 규약.
- **`keyVisuals` 공유화**: `BuildKeyVisuals()` → **`EnsureKeyVisuals()`**(멱등, 이미 있으면 early-return) + 신규 `ReleaseKeyVisuals()`. harmonize가 꺼져 있어도 자동 연주가 88건반 시각을 써야 하므로, `TeardownHarmonize`는 이제 맵을 지우지 않고 `ResetNow()`만 하고 실제 해제는 `HarmonizeDisable`에서 한다.
- 자동 연주 `AudioSource`는 런타임 생성(`PianoAutoPlay` 자식)이며 **건반과 같은 믹서 그룹**으로 라우팅한다(같은 악기가 소리내는 것이므로 피아노 볼륨/FX가 그대로 적용).

## 한계 / TODO
- **머티리얼 미적용** — repo `.mat`(텍스처 없는 단색, 파이프라인 불일치 가능) 생략. 키 URP 기본(회색)로 렌더. 흰/검/우드 머티리얼 부여는 후속.
- **배치는 테스트용** — 기존 terrain 과 비주얼 겹침. 최종 위치는 `Piano` 루트 이동/스케일로 조정(감지·sensor 가 transform 따라가 robust).
- FBX mesh 라이선스 확인.
- 검은 건반은 **밟을 수 없음**(콜라이더 없음, 의도) — 단 2026-07-23부터 소리에 맞춰 **시각적으로는 눌림**(차용화음 A♭/B♭ 등).
- `Assets/Audio/Perc/vcsl_*.wav` 3개는 폐기된 자동 반주용으로 받아둔 것 — 현재 미사용.
- **자동 연주 곡의 라이선스** — 현재 곡은 AudioHero 싱크권이라 "mp3 재생 + 건반 싱크"까지만 가능하고 샘플러 재연주는 불가. AI 전사 조항도 문자상 걸림(위 참조). 상용 전 confirm 또는 PD 곡 교체 필요.
- **전사 스크립트가 repo에 없음** — `add_songs.py`(배치) / `midi_to_score.py`(MIDI→score JSON) / `build_artifact.py`(score JSON→테스트 HTML)는 전부 scratchpad에만 있다. transkun venv(`amt311`, Python 3.11 + `ncls==0.0.39`)와 librosa venv(`amt`)도 같은 자리. 이 셋 중 하나라도 지워지면 곡을 더 못 넣는다 — repo로 옮기려면 별도 요청 필요. `extract_preview.py`(score JSON→preview)는 2026-07-25부터 `tools/`에 있음.
- 자동 연주 중 `harmonize.enabled`가 켜져 있고 슬라임이 착지하면 두 시스템이 같은 건반 시각을 겹쳐 누를 수 있다 — `PianoKeyVisual.Press`가 깊이·시간 모두 `max` 누적이라 깨지진 않지만, 복귀 직후 ~fadeOut 동안 자동 연주가 누른 건반이 남아 있다(의도: 급정지로 지우면 슬라임이 방금 친 건반의 dip까지 지워짐).
