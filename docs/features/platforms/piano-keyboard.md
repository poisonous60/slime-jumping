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

- **진행**: `List<ChordStep>` — 리서치 권장 **8스텝 백본 + 차용화음 1개 = 9코드**. `C6/9 → G(add9)/B → Am7 → Fmaj9 → C/E(add9) → Dm9 → Gsus4(add9) → C6/9(turn)` + 인덱스 8 = `Fm6`(ivm6 차용, 확률 8%로만 등장). 각 ChordStep = tonic 기준 semitone offset 보이싱(저역은 5도/8도만, 3·7·9도는 위로 = Low Interval Limit) + release. 착지 = **aggregate rising edge**(모든 건반 해제→≥1 접촉) 1회당 1전진 → seam 2건반 = 1전진, 건반간 크롤 = 전진 안 함.
- **멜로디**: 착지 건반 위치 → 현재 스케일로 스냅한 음을 보이싱 최상단 위에(`SnapMelody`). `snapMode` 3모드 = {Pentatonic(기본, 안 틀림) / ChordTones / ScaleFull}. 이음새 2건반 = **dyad**(둘 다 울림, ×`dyadVolumeScale`, 동음이면 옥타브 올려 분리).
- **velocity density**: 강한 착지 = 최상단 음 옥타브 더블링(`octaveDoubleVelocity` 임계) + 음량/시각 눌림 깊이 증가.
- **다음 코드 선택 = 3단 우선순위**: ①**zone 분기**(`branchingEnabled`, 착지 X 3분할 → `leftNext/centerNext/rightNext`, -1이면 통과) → ②**가중치 랜덤**(`randomBranching`, 스텝의 `nextOptions[]` = {stepIndex, weight}) → ③선형 i+1. `roll < 0`이면 ②를 통째로 건너뜀(토글 OFF 경로).
- **결정 노드는 2개만**(리서치 권장 위치): 스텝 3 `Fmaj9`(좌=Dm9 정통종지 / 중=C/E 기본 / 우=Am7 서정적), 스텝 7 `C6/9`(우 착지면 다음 바퀴를 Am7로 시작해 한 바퀴 단조 인상). 나머지 스텝은 zone -1이라 가중치 랜덤이 담당.
- **확률은 authored 후보 안에서만**: 무작위 생성/마르코프 아님. 리서치 결론 — *"이 게임은 '항상 좋게'가 핵심이라서, 무작위성이 만들어내는 신선함보다 작가가 미리 승인한 안전한 분기가 더 가치 있습니다"*(`docs/클로드 연구/착지로 전진하는 피아노 발판 게임을 위한 코드 진행 시스템 심층 리서치.md`). 400착지 시뮬레이션 결과 9코드 전부 도달, 범위 이탈 0, `Fm6` 등장률 ~0.8%.
- **비주얼 sink**: 코드 구성 흰 건반이 눌림 — `PianoKey.VisualPress`가 **mesh만 dip**(`OnAfterMotion`, collider/rb 불변 → 분리 규칙 준수, rider carry·소리 없음). 착지 건반 자체는 기존 contact sink로 눌림. VisualPress 미사용(OFF) 시 zero cost.
- **오디오**: `PianoVoicePool`(키보드 자식 `PianoVoices`) — N개(`voicePoolSize`) AudioSource 풀, 건반 clip에서 `midi→clip` 맵 구성해 각 음을 `2^(반음/12)` 피치로 재생(검은음·비샘플음 = 최근접 흰건반 시프트), 선형 release tail(`Tick`). harmonize ON이면 각 `PianoKey`는 `SetMuted(true)`로 자기 strike 음 소거(풀이 오디오 소유). `sfxObjectGroup` 라우팅.
- **리셋**: 마지막 전진 후 `resetIdleSeconds`(기본 1.6s) 동안 무착지(쉬거나 발판 이탈) → 진행 step 0(으뜸) 복귀 → 다음 착지는 항상 으뜸부터.
- **리버브(선택, 수동 배선)**: `reverbEnabled` + 믹서에 Reverb 이펙트 + `reverbWetParam` 노출 파라미터가 **있어야** 동작. `MainMixer.mixer` 기본엔 리버브 없음 → SFX_Object(또는 전용 Piano) 그룹에 Reverb 이펙트 추가 + wet(dB) 파라미터를 `PianoReverbWet`로 노출해야 함. 없으면 `SetFloat` 무시(graceful).
- **런타임 토글**: 매 FixedUpdate `enabled` 감시 → 인스펙터에서 플레이 중 on/off(ON = 풀 생성 + 전 건반 mute / OFF = 풀 해제 + unmute).
- **순수 함수**: `HarmonyMath`(BuildChord / SnapMelody / Zone / NextIndex / WeightedNext / ApplyVelocityDensity) — UnityEngine.Object 비의존, EditMode 테스트 `PianoHarmonyMathTests`로 검증.
- **파일**: `PianoConfig.HarmonizeSettings`/`ChordStep`(+enum), `PianoKeyboard.Harmonize.cs`(partial 상태머신), `PianoVoicePool.cs`, `HarmonyMath.cs`, `PianoKey`(`muted`+`VisualPress`+`Clip/Pitch/OutputGroup` getter). 빌더 무변경(config 필드 초기값 = 신규 피아노 `enabled=false`).

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
- 검은 건반 연주 안 함(의도). 필요 시 흰 건반과 동일 패턴 확장.
