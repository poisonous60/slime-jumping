# Celeste식 구름 발판 VFX — 조사 완료, 시제품(spike) 대기

**Date:** 2026-07-01
**Status:** IN PROGRESS
**Bead(s):** none
**Chain:** `standalone-cloudvfx` seq `1`
**Parent:** none — first in chain
**Prior chain:** none — first in chain

> 참고: 이번 세션 초반에 OS temp(`%TEMP%\HANDOFF_cloud-vfx_seq1.md`)에 임시 handoff를 만들었으나, 그건 직접 만든 프로토 스킬 산출물이고 이 파일이 정식 대체본이다. temp 파일은 무시/삭제 가능.

## Reference Documents
- `CLAUDE.md` — 프로젝트 규칙 (rb.position, `??`/`?.` 금지, 비주얼/콜라이더 분리, 외부시스템 통합 시 `SlimeController.{Sub}.cs` partial)
- `docs/SlimeJumping_메커닉_설계_v6.md`, `docs/프로토타입_구현_현황.md` — 기능 추가/변경 시 갱신 필수
- 목표 이미지: `docs/tmp/스크린샷 2026-07-01 181528.png`, `docs/tmp/스크린샷 2026-07-01 181544.png`

## The Goal
Celeste식 "구름 발판"을 이 게임(Unity 6, URP, 2.5D 고정 카메라지만 실제 깊이 있는 3D)에 구현. 동작: (1) 슬라임 착지 시 구름이 살짝 눌림, (2) 착지 후 짧은 창 안에 점프하면 타이밍 보너스로 높이 튐 — 너무 빠르거나 늦으면 보너스 없음, (3) 떠날 때 구름이 **위로 흩어지며 부드럽게 소멸**. 핵심 난제는 게임플레이가 아니라 **"구름이 구름 모양 조각으로 부드럽게 흩어지는" 비주얼** — 이 게임에 아직 없는 표현. 목표 룩 = 위 스크린샷(둥근 구름 조각이 위로 퍼지며 반짝이와 소멸, 날카로운 파편 아님).

## Where We Are
- 기법 **조사만** 완료. cloud 관련 코드/셰이더/프리팹 **구현 0**.
- 프로젝트 내 기존 cloud 에셋 조사 완료(아래 Evidence).
- 다음 작업 = 최소 시제품(spike) 하나 만들어 "흩어짐 룩" 되는지 눈으로 확인. 착지 눌림·타이밍 게임플레이는 그 다음.
- 이번 세션은 조사 방식/handoff 도구 정비에 대부분 소비됨(아래 User Feedback). cloud 구현 자체는 미착수.
- git: 브랜치 `codex/thinice-surface-anchor`. cloud 관련 변경 없음. 워킹트리 변경은 `SlimeJumping_v2/Assets/Scenes/MainScene.unity`뿐(이번 작업과 무관한 씬 편집).
- 부수 산출물(완료): `snowball-research` 스킬 생성, `handoff`/`handoffplan` 스킬 설치(REMvisual). `~/.claude/skills/`에 있음. cloud 작업과 별개.

## What We Tried (Chronological)
조사·검토한 접근과 결과(구현이 아니라 방향 탐색):
1. **FrostCloudVFX.prefab 재사용 검토** → ❌ 기각. 텍스처가 가우시안 그라디언트 원("빛 공")이라 구름 실루엣 없음. 파티클 세팅 문제 아니라 **텍스처/실루엣 문제**. 반례로만 유용.
2. **2D 스프라이트시트/flipbook(셀레스트 원본 방식)** → ❌ 기각. 셀레스트는 Monocle 엔진 flipbook(손그림 프레임)이나, 이 게임은 실제 깊이 있는 3D라 평면 2D는 이질적.
3. **ThinIce식 날카로운 mesh shatter(조각 파편 + 속도)** → ❌ 기각. 유저는 **부드러운** 흩어짐 원함(fling-apart 아니라 erode+expand).
4. **제자리 dissolve만(위 상승/확산 없음)** → ❌ 부분기각. 목표 스샷은 위로 상승+확산 필요.
5. **Route B(mesh particle) + per-particle dissolve** → ✅ 채택 방향. 아래 결정 참조.
6. **Route A(billboard + fake-volume)** → 유력 fallback. Spellbreak가 유명 3D 스타일라이즈드 연기를 실제로 billboard로 구현(realtimevfx 분석). 2.5D 고정 카메라라 평면성 문제 작음.
7. 조사 방식: 처음 즉석 키워드 3개→합성(얕음, 유저 불만) → 소스 정독+리드 추적(snowball) 방식으로 전환. 이게 유효했음(looper/interview-skills/dev-plan 등 좁은 검색이 놓친 것 발견).

## Key Decisions
- **Route B(메시 파티클)를 먼저 시도.** 이유: 게임에 Quibli `Cloud3D.shadergraph` 이미 있음 / 각 조각이 진짜 3D 덩어리라 구름 실루엣 확실 / 발판 하나=조각 수십 개라 draw call 안전(메시 1개=draw call 1개, realtimevfx). Route A는 안 되면 쓸 더 싼 대안.
- **부드러운 소멸 = per-particle dissolve.** Custom Vertex Streams `AgePercent`(TEXCOORD0.z)를 셰이더 UV0.z로 받아 조각마다 개별 침식 + **Smoothstep**(hard Step 아님)으로 soft edge → 셀레스트식 부드러움, 날카로운 파편 아님. (torchinsky "Stylized VFX Unity Part 2")
- **구름다움의 핵심 레버 = 실루엣.** 흐릿한 원(가우시안) 금지 — "빛 공"으로 읽힘.
- **착지 눌림 = 기존 mesh-vertex-deform 재활용**, 비주얼/콜라이더 분리(CLAUDE.md 규칙).
- **흩어짐 = velocity로 튕겨내기 아님.** 위+방사 velocity는 주되, 핵심은 erode+expand(제자리 침식 + 살짝 부풀며 상승). ThinIce식 rigid fragment scatter는 명시적 반대.
- (반려 대안) 2D flipbook, 메타볼/volumetric, six-way lighting(=여전히 텍스처 시트, 배경용, 인터랙티브 발판 부적합).

## Evidence & Data
### 기법 비교 (조사 결론)
| 방식 | 핵심 | 흩어짐 성격 | 이 게임 적합 |
|---|---|---|---|
| Route B 메시 파티클 | 각 조각=진짜 3D 덩어리 + Quibli Cloud3D | erode+상승+확산 | ◎ 채택(3D 통일, 실루엣 보장) |
| Route A 빌보드 | 카메라 향한 납작 그림 + fake-volume 셰이더 | 텍스처 dissolve | ○ fallback(2.5D라 가능, Spellbreak식) |
| 2D flipbook | 손그림 프레임(셀레스트 원본) | 그려진 애니 | ✕ 3D 게임에 평면 |
| mesh shatter | 날카로운 조각+속도 | fling-apart | ✕ 유저 반대 |

### 프로젝트 내 cloud 에셋 (Assets 폴더)
- `Assets/ThirdParty/Quibli/Shaders/Cloud3D.shadergraph`, `Cloud2D.shadergraph` — 스타일라이즈드 toon 구름 셰이더 ⭐
- `Assets/ThirdParty/Quibli/Demos/[Common]/Models/_Foliage Carrier Meshes/MeshCarrier_Cloud_01~03.fbx` — 구름 메시(조각 소스 후보)
- `Assets/Prebs/ThinIceV2/FrostCloudVFX.prefab` — 기존 구름 VFX(가우시안 원, **반례**, 재사용 X)
- `Assets/ThirdParty/Sand VFX URP/VFX/Sand Storm/` — 구름 puff 텍스처(Route A용, `T_VFX_SandstormCloud*`)
- `Assets/ThirdParty/Synty/PolygonCity/` `SM_Env_Cloud_01~03` — 로우폴리 구름 메시(조각 소스 후보)
- `SlimeJumping_AssetVault/.../kenney_background-elements-remastered/` — 2D 구름 스프라이트(배경, cloud-lobe 실루엣 참고용, 미임포트)

### 핵심 소스(조사 시 정독)
- torchinsky "Stylized VFX Unity" Part 1(dissolve: Noise→Step→Alpha, Smoothstep soft edge) + Part 2(per-particle: Custom Vertex Streams `AgePercent` + Custom Data 커브)
- Gabriel Aguiar "Stylized Smoke Poof in Unity VFX Graph" — 스타일 연기 poof 표준 튜토
- Mike Young(Medium) — mesh particle 스타일라이즈드 구름(Renderer=Mesh + vertex-noise 셰이더)
- Spellbreak 스타일 연기 breakdown(realtimevfx, ArtStation) — 3D 게임도 billboard + tangent-space fake-volume 라이팅 사용
- 함정(realtimevfx): 메시 1개=draw call 1개(남발 금지); baked FBX 애니=랜덤화 불가→반복 티남; six-way lighting=텍스처 시트, 배경용

## Code Analysis
- 아직 cloud 코드 없음. 신규 예정.
- 통합 위치 미정: `SlimeController.Cloud.cs` partial(CLAUDE.md: 외부시스템 통합 시 partial 규칙) vs 독립 `CloudPlatform` 컴포넌트. 상태별 처리는 기존 패턴(Attached/Charging/Airborne/Stunned) 유지.
- 착지 눌림은 기존 deformable/vertex-deform 방식과 동일 계열. 비주얼 변형이 콜라이더에 영향 주면 안 됨.

## Files Changed
### Source code
- (없음 — cloud 구현 미착수)
### 세션 산출물 (repo 외부)
- `~/.claude/skills/snowball-research/SKILL.md` — 신규 조사 스킬(유지)
- `~/.claude/skills/handoff/`, `~/.claude/skills/handoffplan/` — REMvisual 설치본(내 프로토 스킬 대체)
### 무관 변경
- `SlimeJumping_v2/Assets/Scenes/MainScene.unity` — 이번 작업과 무관한 씬 편집(커밋 판단 유저 몫)

## User Feedback & Preferences (중요 — 다음 세션 반드시 반영)
- **조사 방식**: 즉석 키워드 fan-out 싫어함. "소스를 실제로 읽고 쓸모 판단 + 거기서 나온 리드로 다음 검색" 반복(snowball) 원함. `deep-research`/Claude 웹 같은 한방 fan-out 금지. → `/snowball-research` 사용.
- **검색 깊이**: 라운드당 2~3개 fetch는 적음. rich vein이면 넓게, 인덱스(awesome-list/topic) 수확해서 커버리지 확보.
- **구름 표현**: 부드럽게 흩어지는 것(셀레스트식). ThinIce식 날카로운 조각 파편 명시적 반대. 가우시안 블러 원("빛 공") 반대 — 구름 실루엣 필수.
- **"대충"의 의미**: 품질 낮추지 말고 범위를 좁히라는 뜻([[feedback_scope_not_quality]]). 값싼 근사/전신 치트 금지([[feedback_no_cheap_approximations]]).
- **소통**: 내용 안 보여주고 "승인?/바꿀까?"만 반복하는 것에 크게 분노함. **내용을 채팅 텍스트로 먼저 다 보여주고** 판단하게 할 것. 툴 결과/모달/"위에 있음"은 유저 화면에 안 보임.
- **불확실한 작업**: "만들어봐야 아는" 건 계획 승인 시키지 말고 **일단 만들어서 결과를 보여줘라**(spike/build-and-look). 결과가 첫 검토 대상.
- 유저는 구현을 Claude에 위임함([[user_workflow_delegation]]).
- 캐릭터: 유저는 caveman 모드 사용 중(간결체). 코드/커밋은 정상 문체.

## Where We're Going
**다음 세션 = SPIKE(최소 시제품 만들고 눈으로 보기). 계획 승인 게이트 없음 — 결과가 판단 대상.**
1. 구름 조각 메시 몇 개 확보 — Quibli `MeshCarrier_Cloud` 슬라이스 또는 임시 구체(Open Question).
2. Particle System: Renderer=**Mesh** + 조각 메시 + Quibli `Cloud3D` 머티리얼 → 작은 구름 클러스터 1개(정적 발판).
3. per-particle dissolve 셰이더: Custom Vertex Streams `AgePercent`(TEXCOORD0.z) + Smoothstep soft edge. Quibli Cloud3D에 dissolve 통합 또는 파생.
4. 점프 트리거 시: 각 조각에 위+방사 velocity + Size/Rotation over Lifetime + dissolve 진행 → 흩어짐.
5. **관찰(유저가 눈으로)**: (a) 구름 조각으로 읽히나(블러 아님?) (b) 부드럽게 위로 흩어지며 소멸하나(파편 아님?) → 목표 스샷과 비교.
6. **판정**: 잘 나옴 → Route B 확정, 착지 눌림(mesh-vertex-deform Y squash)·타이밍(sine 바운스 상단 window 보너스)·다듬기 진행. 실패 → Route A(billboard fake-volume) 또는 dissolve 파라미터·메시 실루엣 조정.
7. 완료 후 `docs/SlimeJumping_메커닉_설계_v6.md` + `docs/프로토타입_구현_현황.md` 갱신.

## Risks & Blockers
- Unity Editor 조작 필요(unity-cli exec/screenshot). 파티클/셰이더는 에디터 GUI 작업이 많아 코드만으론 한계 — 유저 협업 or unity-cli 병행.
- 유저가 시각 검증 담당([[feedback_user_tests_visuals]]) — Claude가 직접 플레이/판정 말고, 뭘 볼지 설명하고 유저가 평가.
- draw call: 조각 메시 남발 금지(수십 개 선).

## Open Questions
- 구름 조각 메시 소스: Quibli 슬라이스 / Synty / 신규 로우폴리 — 어느 것? (실험서 결정)
- Route B 확정 vs A로 pivot — 시제품 결과가 정함.
- 타이밍 보너스 window 파라미터(폭/관대함).
- CloudPlatform 코드 위치: `SlimeController.Cloud.cs` partial vs 독립 컴포넌트.

## Quick Start for Next Session
```bash
# 참고 문서
#   CLAUDE.md (프로젝트 규칙)
#   docs/SlimeJumping_메커닉_설계_v6.md, docs/프로토타입_구현_현황.md

# 목표 이미지 먼저 볼 것
#   docs/tmp/스크린샷 2026-07-01 181528.png
#   docs/tmp/스크린샷 2026-07-01 181544.png

# 먼저 읽을 에셋 (구현 재료)
#   Assets/ThirdParty/Quibli/Shaders/Cloud3D.shadergraph
#   Assets/ThirdParty/Quibli/Demos/[Common]/Models/_Foliage Carrier Meshes/MeshCarrier_Cloud_01~03.fbx
#   Assets/Prebs/ThinIceV2/FrostCloudVFX.prefab  (반례 — 왜 안 되는지 확인용)

# Unity 조작
#   unity-cli exec / screenshot / refresh_unity

# 다음 액션 (THE 단 하나)
#   Route B 최소 시제품 만들기: 파티클 Renderer=Mesh + 구름 조각 메시 + Quibli Cloud3D
#   + per-particle dissolve(AgePercent+Smoothstep) + 점프 시 위/방사 velocity.
#   → "구름 조각이 부드럽게 위로 흩어지며 소멸"하는지 스크린샷으로 유저에게 보여주고 keep/pivot 판정.
```

## Session Closed
**Closed at:** 2026-07-01
**Commit:** d22ff94
**Session status:** Handed off to next session
