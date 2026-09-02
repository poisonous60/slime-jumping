# 옵션창 화면 구성

ESC 로 여는 판이 **어떻게 생겼는지** 적은 문서. 값은 전부
[OptionsPanelSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/UI/OptionsPanelSetup.cs) 상수와
[UISetupCommon.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/UI/UISetupCommon.cs) 팔레트 실측.
프리팹은 `Assets/Prefabs/UI/Panels/OptionsPanel.prefab`, 리빌드는
`Tools → SlimeJumping → UI → Build or Rebuild OptionsPanel Prefab`.

## 전체 배치 (1920×1080 레퍼런스)

```
┌────────────────────────── Backdrop (화면 전체, 딤) ──────────────────────────┐
│                                                                             │
│      ┌───────── Box 900×946 ─────────┐        ┌── SidePanel 450×auto ──┐     │
│      │            Options            │  gap   │  Game Speed     100%   │     │
│      │                      12:34 ●  │   20   │  ▬▬▬▬▬▬◉▬▬▬▬▬▬▬▬▬▬▬    │     │
│      │   Resolution   [1920 x 1080▾] │        │                        │     │
│      │  Window Mode  < Fullscreen >  │        │  ☑ Use a rudimentary…  │     │
│      │  Frame Limit  <     60     >  │        │  ☐ Mouse click telep…  │     │
│      │        VSync  <     On     >  │        │  ☐ Savepoint           │     │
│      │  Background…  <     On     >  │        └────────────────────────┘     │
│      │          BGM  ▬▬▬▬◉▬▬  80%    │          (1회 클리어 전엔 통째로 숨김)  │
│      │   Player SFX  ▬▬▬▬◉▬▬  80%    │                                       │
│      │    World SFX  ▬▬▬▬◉▬▬  80%    │                                       │
│      │           UI  ▬▬▬▬◉▬▬  80%    │                                       │
│      │  Aim Inverse  ☐               │                                       │
│      │                               │                                       │
│      │ (Resume)   (Quit)  (New Game) │                                       │
│      └───────────────────────────────┘                                       │
└─────────────────────────────────────────────────────────────────────────────┘
```

## 본판(Box)

- 크기 900×946, 화면 정중앙. 세로 `VerticalLayoutGroup`(padding 32, spacing 10).
- 재질은 **젖빛 유리** — LeTai `TranslucentImage`(색 `#0A1C17` alpha 0.75, foregroundOpacity 0.5,
  vibrancy 1). 인게임에선 뒤 게임 화면이 흐릿하게 비치고, **메인 메뉴 씬에서만 alpha 1** 로 올려
  뒤 메뉴 버튼이 글자에 겹치지 않게 한다.
- 뒤 `Backdrop` 은 화면 전체 딤 `#000F0A` alpha 0.6.
- 제목 `Options` — 72pt 볼드, 옅은 민트 `#D1F5E3`.
- 오른쪽 위 **플레이 타임 오버레이**(`12:34`) — `ignoreLayout` 이라 세로 스택 밖, 행을 안 먹는다.
  판이 1080 에 꽉 차서 행 예산이 없어 이렇게 뺐다.

### 행 형태 3종 + 공통 골격

한 행은 `라벨(우측정렬) | 컨트롤(좌측정렬)` 이 중앙 거터를 사이에 두는 모양(Getting Over It 풍).
라벨 컬럼 240 · 간격 24 · 컨트롤 컬럼 430, 행 높이 56.

| 형태 | 쓰는 행 | 컨트롤 칸 |
|------|---------|-----------|
| 사이클러 | Window Mode / Frame Limit / VSync / Background Camera | `<` 60 + 값 라벨 + `>` 60 |
| 슬라이더 | BGM / Player SFX / World SFX / UI | 슬라이더(높이 32) + 우측 `%` 90 |
| 드롭다운 | Resolution | TMP 드롭다운(높이 44) |
| 체크박스 | Aim Inverse | 30×30 상자 하나 |

행 순서는 위 그림 그대로 — Resolution → Window Mode → Frame Limit → VSync → Background Camera →
BGM → Player SFX → World SFX → UI → **Aim Inverse**, 그 밑에 스페이서와 액션 버튼.

### 액션 버튼

220×52 **알약**(sliced 스프라이트를 `pixelsPerUnitMultiplier` 0.35 로 눌러 둥글게), 민트 바탕
`#8FDEB8` 에 짙은 초록 글자 `#0D2E21`, 30pt 볼드. 좌·중앙·우로 벌리되 좌우 padding 을 위 행들의
컬럼 폭에서 계산해 버튼 끝과 행 끝이 맞는다.

- **Resume** — 판만 닫는다(정지 해제).
- **Quit** — 메인 메뉴로. *메뉴 씬에서 열면 숨는다*(같은 씬 재로드라 무의미).
- **New Game** — 세이브 삭제 후 새로 시작.

## 사이드 패널

본판 오른쪽에 20 띄워 붙는 450 폭 판. **위 모서리를 본판과 맞춰** 항목이 늘면 아래로만 자라고,
높이는 `ContentSizeFitter` 가 잡는다 — 체크박스를 더 넣어도 상수를 안 고친다. 폭이 좁아 라벨 컬럼이 없다 — 체크박스 옆이 곧 이름,
Game Speed 는 머리글 줄(이름 + 값)과 슬라이더 줄로 쪼갰다.

**1회 클리어 전에는 통째로 비활성** (`LoadSaveManager.HasCleared`). 그래서 여기 든 건 전부
보상·디버그 성격이고, 조작 설정(Aim Inverse)은 본판에 있다.

## 색 팔레트

| 쓰임 | 색 |
|------|-----|
| 제목 | `#D1F5E3` 옅은 민트 |
| 값 라벨 · 체크마크 · 강조 | `#8FE0BD` 민트 |
| 행 라벨 · 본문 | `#E8F7F0` 민트 섞인 오프화이트 |
| 판 바탕 | `#0A1C17` 짙은 초록 (반투명) |
| 슬라이더 홈 / 채움 | `#1A382B` / `#75D6AD` |
| 버튼 바탕 / 글자 | `#8FDEB8` / `#0D2E21` |

## 열고 닫을 때

연출 없이 **한 번에** 뜨고 사라진다(`UIPanel.instantTransitions`, 유저 지시). 게임플레이 씬에서는
여는 순간 `Time.timeScale = 0` 으로 일시정지를 겸하고, 닫으면 `GameOptions.GameSpeed` 로 되돌린다.
UI 트윈은 전부 unscaled 클럭이라 정지 중에도 버튼 호버·라벨 범프가 산다.
