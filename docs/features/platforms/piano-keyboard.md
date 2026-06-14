# Piano Keyboard (플레이 가능한 피아노 플랫폼)

슬라임이 흰 건반 위에 착지하면 건반이 **속도에 비례해 내려갔다가 눌린 상태로 유지(held)**되고 그 음의 **피아노 샘플이 셈여림(강약) 적용되어 재생**된다. 슬라임이 **건반을 떠나면** 건반이 복귀하며 음이 **댐퍼처럼 페이드아웃(release)**된다(하드 컷 아님). 한 마리 슬라임이 88건반 통짜 업라이트 피아노를 연주하는 플랫폼.

## 출처 / 라이선스
- 3D 모델 + 음 샘플: [catdevpete/Unity-MIDI-Piano](https://github.com/catdevpete/Unity-MIDI-Piano) (코드 MIT). 우리는 코드는 쓰지 않고 **에셋만** 추출.
- `Piano.fbx` (업라이트 피아노, 건반 88개 분리, `PianoKey.001`~`.088`) → `Assets/_PianoImportTest/Piano.fbx`. 루트 transform 깨끗(scale 1 / rot 0). FBX mesh 자체 라이선스는 repo에 별도 명시 없음 (MIT 우산 가정) — **상용 전 확인 필요**.
- 음 샘플: Beskhu(Freesound) **CC0** 업라이트 피아노 `.aiff` 88개 → `Assets/Audio/Piano/`. 파일명 prefix = MIDI 노트번호 (예 `..__21-a0` = MIDI 21 = A0). 커버리지 MIDI 9–96.

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

## 한계 / TODO
- **머티리얼 미적용** — repo `.mat`(텍스처 없는 단색, 파이프라인 불일치 가능) 생략. 키 URP 기본(회색)로 렌더. 흰/검/우드 머티리얼 부여는 후속.
- **배치는 테스트용** — 기존 terrain 과 비주얼 겹침. 최종 위치는 `Piano` 루트 이동/스케일로 조정(감지·sensor 가 transform 따라가 robust).
- FBX mesh 라이선스 확인.
- 검은 건반 연주 안 함(의도). 필요 시 흰 건반과 동일 패턴 확장.
