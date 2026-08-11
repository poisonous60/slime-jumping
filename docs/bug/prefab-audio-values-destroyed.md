# 프리팹에 남아 있던 옛 오디오 값을 덮어써서 소실

2026-08-11. 도구(Claude) 작업 사고 기록. 게임 코드 버그 아님.

## 증상

`SawBladeHazard` 슬라이스 효과음이 안 남. 원인은 08-10 사운드 마이그레이션이 `Slice_Hit.prefab` 을 건너뛰어 `hitSound` 가 비어 있던 것.
복구 작업 중 프리팹에 남아 있던 **이전 설정값(클립·볼륨)을 읽지 않고 덮어써서 삭제**했다.

## 왜 값이 프리팹에 남아 있었나

- 마이그레이션(`d8d2ea7`)의 `SoundInventoryWindow` 스캔 범위 = `SceneManager.GetSceneAt()` 루프 + `FindAssets("t:SoundDef")` + `FindAssets("t:AudioClip")`. **프리팹 에셋은 스캔 대상이 아니다**
- 씬 안 컴포넌트는 값까지 정확히 옮겨졌다. 예: 씬 `SlimeLocomotionAudio.landHardVolume 0.4` → `Sound_SlimeLandHard.volumeRange (0.4, 0.4)`, `pitchJitter 0.06` → `pitchRange (0.94, 1.06)`
- 씬 밖 프리팹 3개는 전환이 안 됐고, Unity 는 **에셋을 다시 저장할 때만** 스크립트에서 사라진 필드를 버리므로 옛 필드가 파일에 그대로 살아 있었다

| 프리팹 | 남아 있던 옛 필드 |
|---|---|
| `Prebs/VFX/Slice_Hit.prefab` | `hitClips`, `sfxVolume`, `pitchJitter` |
| `Prebs/WaterZoneAudioManager.prefab` | `hardEnterClip`, `softEnterClip`, `exitClip`, `underwaterLoopClip` + 볼륨들 |
| `Prebs/Stage1_Sewer/Firework_Radial.prefab` | `launchSfx` + 볼륨 |

## 실제로 무음이었던 것은 하나뿐

프리팹 에셋이 미전환이어도, 그 프리팹의 **씬 인스턴스**는 마이그레이션이 잡았다 — 씬 순회 중 컴포넌트 값을 바꾸면 프리팹 인스턴스 오버라이드로 기록된다. `MainScene` 의 `m_Modifications` 에 `hardEnterSound`/`softEnterSound`/`exitSound`/`underwaterLoopSound`/`launchSound` 오버라이드가 SoundDef 를 가리키고 있다.

| 프리팹 | 씬 인스턴스 | 게임에서 소리 | |
|---|---|---|---|
| `WaterZoneAudioManager` | 있음 (오버라이드로 전환됨) | 정상 | 내 수정 불필요 |
| `Firework_Radial` | 있음 (오버라이드로 전환됨) | 정상 | 내 수정 불필요 |
| `Slice_Hit` | **없음** — `SliceEffect.Spawn` 이 런타임 `Instantiate` | 무음 | 유일한 실제 고장 |

이전 세션 핸드오프([HANDOFF_sound-def-migration_2026-08-10.md](../../plans/handoffs/HANDOFF_sound-def-migration_2026-08-10.md))도 같은 경계를 보여준다 — "다음 세션이 확인할 것"의 재배선 대상이 **"물 존 3 / 불꽃 4 / 화강암 2 = 10개 컴포넌트"**, 전부 씬 오브젝트다. `SliceEffect` 는 코드만 이관됐고 `Sound_Slice*` 에셋은 30개 목록에 아예 없었다.

즉 씬에 인스턴스가 없는 런타임 스폰 프리팹만 마이그레이션 사각지대였다. `SawBladeHazard`·`KnifeHazard` 둘 다 이 프리팹을 쓰므로 톱·칼 슬라이스 소리가 같이 죽어 있었다.
나머지 두 프리팹은 고칠 필요가 없었고, 내 수정은 이득 없이 옛 값만 파괴했다.

## 무엇을 잘못했나

1. YAML 블록에서 **클립 guid만** 뽑고, 같은 블록의 `sfxVolume` / `pitchJitter` / `*Volume` 숫자는 읽지 않았다
2. 그 상태로 `PrefabUtility.SavePrefabAsset` 호출 → 안 읽은 줄들이 삭제됨
3. 빈자리를 근거 없는 값으로 채웠다
   - 클립: 에디터 스크립트 `SliceEffectPrefabWireup.cs` 에 하드코딩된 목록(`impactSoft_medium_000~004`)에서 가져옴. **실제 쓰던 클립은 `Fantasy_Game_Attack_Weapon_Impact`** 였고 유저가 손으로 되돌림
   - 볼륨: 기본값 1.0
4. 감사 결과를 "나머지는 깨끗함"으로 보고했다. 실제로는 프리팹 계열을 값 단위로 검증하지 않은 상태였다

## 손실

- 세 프리팹의 옛 클립·볼륨 필드 전부. 현재 파일에 0개 남음
- git 복구 불가: `.gitignore` 에 `Assets/Prebs/` 추가 = `aea7236` (2026-04-12), 세 파일명은 전체 히스토리에서 0건
- 영향 큰 쪽: `WaterZoneAudioManager` 는 자기 값을 씬의 모든 `WaterZone_v2` 에 `ApplyAudioSettings` 로 덮어쓴다. 이 프리팹의 볼륨이 실제 게임 볼륨이었다

## 재발 방지

- Unity 에셋을 쓰기 전에 **그 파일의 현재 직렬화 내용을 먼저 읽는다.** 스크립트에서 사라진 필드가 남아 있으면 그게 복원해야 할 원본이다
- 마이그레이션 복구값의 출처 우선순위: ① 대상 파일에 남은 옛 필드 → ② git 이전 리비전 → ③ 없으면 값을 넣지 말고 유저에게 묻는다. 에디터 셋업 스크립트의 하드코딩 목록은 근거가 아니다 (오래돼서 실제와 다를 수 있음)
- `Assets/Prebs/` 는 git ignore 대상 = 실수하면 되돌릴 수 없다. 이 폴더 안 에셋은 쓰기 전에 원본 값을 따로 기록해 둔다
- "나머지는 깨끗함" 류의 감사 결론은 값 단위로 확인한 범위에만 붙인다
