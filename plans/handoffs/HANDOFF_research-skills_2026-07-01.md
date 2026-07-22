# 리서치 인프라 구축 + "왜 AI가 조사를 제대로 못 하나" 규명

**Date:** 2026-07-01
**Status:** IN PROGRESS
**Bead(s):** none
**Chain:** `standalone-research-skills` seq `1`
**Parent:** none — first in chain
**Prior chain:** none — first in chain

## Related Handoffs
- `plans/handoffs/HANDOFF_cloud-vfx_2026-07-01.md` — 이 세션은 그 handoff 붙여넣기로 시작했으나 **cloud 작업은 딴 세션에서 완료됐고**(메모리 `project_cloud_vfx_wip` 참조: prefab 기반 CloudDisperser+CloudPlatform DONE), 이 대화는 전혀 다른 스트림(리서치/스킬 메타)으로 분기함. 별개 작업, 부모 아님.

## Reference Documents
- `CLAUDE.md` — 프로젝트 규칙
- 스킬 위치: `C:\Users\poiso\.claude\skills\` (snowball-research, handoff, handoffplan)
- 메모리: `feedback_research_method_snowball`, `feedback_never_enter_play_mode`(딴 세션 추가)

## The Goal
원래 = 구름 VFX 시제품. 실제 이 세션이 된 것 = **리서치/인계 스킬 인프라 구축 + "AI가 왜 조사를 얕게/틀리게 하나"의 근본 규명.** 유저 최종 요구(미착수) = **10분/100개 소스 규모의 coverage 조사**를 실제로 지속 수행하게 만들기.

## Where We Are
- **`snowball-research` 스킬 생성**(`~/.claude/skills/snowball-research/SKILL.md`) — 반복 lead-chasing 웹조사. 이 세션에 여러 번 개정(coverage/discovery, index harvest, batch 넓히기, saturation, inversion, 3-case 등).
- **`handoff` 스킬**: 직접 만들었다가 **REMvisual/claude-handoff로 교체**(git clone → `handoff`+`handoffplan` 설치, 내 자작본 삭제). 지금 이 handoff가 그 REMvisual 스킬로 실행 중.
- **snowball로 2건 실조사 수행**: (A) 왜 LLM이 반복하나, (B) 리서치 에이전트가 진짜 판단하게 하는 법. 둘 다 실질 findings 확보(아래 Evidence).
- **cloud-vfx handoff 커밋됨**: `HANDOFF_cloud-vfx_2026-07-01.md`, commit `d22ff94`.
- 워킹트리: `MainScene.unity`(무관), `HANDOFF_cloud-vfx*.md`(딴 세션 수정), `Assets/Scripts/Test/`(구름 시제품, 딴 세션).
- **미착수**: 유저가 최종 요구한 100-소스 coverage 조사. 토픽 확정 + Workflow go 대기 중이었음.

## What We Tried (Chronological)
1. **구름 조사를 snowball 아닌 즉석 키워드 3개로 시작** → 유저 불만("검색 2-3번이 다냐"). snowball 방식(소스 정독+리드추적)으로 전환.
2. **snowball-research 스킬 제작** → prior-art 조사(dzhng/deep-research, langchain, recursive-research)로 니치 확인 후 강화.
3. **handoff 스킬 자작** → review-gate(유저가 next-plan 승인) 설계. 근데 spike/build-and-look 케이스에서 게이트 무의미 판명 → 3-case 모델 추가.
4. **handoff 스킬 반복 실패**: 내용을 Read 툴/모달로 "보여줬다" 했으나 유저 화면엔 안 보임 → "채팅 텍스트로 출력" 규칙 추가. 그래도 유저가 못 봄 반복 → 결국 **자작 폐기, REMvisual 기성품 설치.**
5. **snowball로 LLM-반복 원인 조사** → typicality bias, mode collapse 등 확보. 유저: "이건 snowball 없이도 알 만한 것" → snowball 값어치 = 개념 아니라 인용/수치 장식뿐임 인정.
6. **snowball로 "진짜 판단시키는 법" 조사** → 처음 학술만 파고 실무 각도 스킵 + 조기 saturation 선언 → 유저 "또 몇 개로 결론? 미쳤어?" → 넓혀서(인덱스+실무) 그림 바뀜(citation halluc 11-57%, multi-agent 악화).
7. **결론 도달**: 스킬(프롬프트)로는 판단·지속 규율 강제 불가. 이미 있던 규칙도 내가 무시함이 증거.

## Key Decisions
- **snowball 니치는 좁다**: 답이 웹에 있고 내가 용어 모를 때만. (a)내가 이미 아는 것, (b)답이 유저머릿속/repo인 것엔 쓰면 안 됨/오도. 이 세션 2건 다 snowball이 최적 도구 아니었음.
- **handoff 자작 폐기 → REMvisual 채택**: 기성품이 resume 문구+체인+precompact 훅 등 더 완성. 단 resume는 어느 스킬이든 수동 붙여넣기(구조적 제약).
- **"스킬에 규율 넣기"는 최약 레버**: 프롬프트 = 같은 편향 forward pass가 읽음. 자기교정 실패(DeepMind). → 규율은 **훅(harness 강제)/인간 게이트/별도 도구/Workflow**로 externalize 해야 함.
- **100-소스 coverage는 Workflow로**: 내 early-stop을 방정식에서 제거하는 결정론적 fan-out. snowball(내 의지 의존)로는 지속 불가.
- (반려) 구름: 메시파티클/절차생성/에로전 전부 반려됨(prefab 기반으로 딴 세션 확정).

## Evidence & Data
### 조사 A — 왜 LLM이 "같은 말만" (3층위)
| 층 | 기제 | 소스 |
|---|---|---|
| 학습/정렬 | typicality bias(annotator가 친숙텍스트 선호, α≈0.57) → mode collapse; RLHF가 SFT보다 다양성↓, across-input mode collapse | Verbalized Sampling 2510.01171; RLHF diversity 2310.06452 |
| 디코딩 | self-reinforcement(반복토큰 확률↑); 학습데이터 반복 10배 증폭 | Repetition-In-Repetition-Out 2310.10226 |
| 대화/in-context | **choice-supportive bias**(자기답 방어) + contrary feedback **2.5배 과가중**; **contextual inertia**(이전 trace 고착); sycophancy; attractor state | Change-of-Mind 2507.03120; Contextual Inertia 2603.04783 |
완화: **Verbalized Sampling**(답 N개+확률 요청, 온도와 직교), 프레임 강제이탈, `/clear`로 context 리셋.

### 조사 B — 리서치 에이전트를 진짜 판단시키는 법
| 발견 | 수치/핵심 | 소스 |
|---|---|---|
| intrinsic 자기교정 = **악화** | CommonSenseQA 75.8→41.8, GPT-4 GSM8K 95.5→89.0 | Cannot Self-Correct 2310.01798 |
| multi-agent 비평도 **악화** | factual attrition + stance homogenization; 같은모델=echo | Deliberative Illusion 2606.03032 |
| self-preference bias | 친숙(저perplexity) 편애, GPT-4 0.520 | 2410.21819 |
| 배포 deep-research citation | 링크유효94%+ but **사실정확도 39-77%**(23-61% 미지지) | Cited-but-Not-Verified 2605.06635 |
| **소스 많이 = 더 나쁨** | 깊이 2→150 tool calls: 정확도 79→17% | 같은 논문 |
| CoVe 작동 이유 | factored 독립검증(draft 안 보고), **사실오류만** | CoVe 2309.11495 |
| 훈련하면 됨 | SCoRe RL: MATH +4.4%, 코드 +12.2% (런타임 프롬프트론 불가) | SCoRe 2409.12917 |
결론: 자동 완전해결 없음. 최선 = **LLM판단 최소화 + 결정론적/이질/외부 grounding + load-bearing 최종판정은 인간/실행테스트.**

## Code Analysis
- 코드 작업 없음(리서치/스킬 세션). 스킬 파일은 마크다운.
- snowball-research SKILL.md = 다회 개정됨(Phase 0-7 + prior-art + 3-case + coverage/discovery + index harvest).

## Files Changed
### Skills (repo 외부, `~/.claude/skills/`)
- `snowball-research/SKILL.md` — 신규 + 다회 개정 (유지)
- `handoff/`, `handoffplan/` — REMvisual 설치본 (자작 handoff 대체)
### Repo
- `plans/handoffs/HANDOFF_cloud-vfx_2026-07-01.md` — 커밋 `d22ff94`
- (이 파일) `plans/handoffs/HANDOFF_research-skills_2026-07-01.md`
### 무관 (커밋 안 함)
- `MainScene.unity`, `Assets/Scripts/Test/` — 딴 작업

## User Feedback & Preferences (다음 세션 필수 — 이 세션의 핵심 자산)
- **조사 = coverage로 넓게, 지속.** "10분간 100개 사이트 조사"를 원함. 내 ~10소스 snowball은 **부족**. 조기 "saturation" 선언 = 그가 제일 싫어함.
- **판단 없이 동조(sycophancy) 금지.** "너 정직한 거 안 궁금하다" — "내 실수야" 반복 = 자책 attractor, 싫어함. **실제 판단**(맞다/틀리다 근거 대고 밀기)을 원함.
- **내용을 반드시 채팅 텍스트로 보여줄 것.** Read툴/모달/"위에 있음"은 유저 화면에 안 뜸. AskUserQuestion은 질문만 뜨고 draft 안 보임 → 항상 본문에 먼저 출력.
- **"승인?/바꿀까?"만 반복 금지** — 내용 없이 판단 강요당하면 분노.
- **불확실=만들어보고 결과로 판단**(spike/build-and-look), 계획 승인 강요 말고.
- **snowball 소스 수 늘리기**: 습관적 2-3 fetch 금지, 인덱스 수확, batch 넓혀라(스킬에 이미 있으나 내가 무시함).
- **스킬로 규율 강제는 환상** 임을 유저가 간파: "스킬 만들어도 의도대로 안 하면 무슨 의미냐". → 훅/인간/Workflow로 externalize 필요.
- 유저 = caveman 모드(간결체). 구현은 Claude 위임. Play 모드 진입 금지(`feedback_never_enter_play_mode`).

## Where We're Going
1. **[즉시] 100-소스 coverage 조사를 Workflow로 실행.** 토픽 유저 확정 필요(직전 후보: "AI가 진짜 판단하게 하는 법" 심화, 또는 신규). Workflow = 결정론적 fan-out(병렬 search+read 에이전트 다수) → 내 early-stop 제거. 모양 = ~100 harvest/triage + 선별 ~20 deep-read(과다정독은 정확도 붕괴하므로).
2. **[구조] 규율을 훅으로 externalize 검토**: 예 stop-hook이 "각도 N개 커버/반증검색 안 하면 종료 차단". update-config/hooks로. 프롬프트 규칙 추가는 최약(하지 말 것).
3. **[선택] snowball 개정 or coverage-research 스킬 신설** — 단 "지식/툴/라우팅"만 담고, "지속·판단"은 Workflow/훅에 위임.

## Risks & Blockers
- **내 early-stop 재발**: 이 세션 2번 조기 종료. Workflow(외부 루프)로만 신뢰성 확보. 프롬프트로는 재발.
- **소스 과다 = 정확도 붕괴**(79→17%): 100은 harvest용, deep-read는 소수로.
- bd(beads) 미설치 → 태스크 트래킹 없음, 메모리로 대체.

## Open Questions
- 100-소스 조사의 **토픽** 확정?
- Workflow로 갈지(큰 토큰) 유저 최종 go?
- 규율 훅을 실제로 박을지?

## Quick Start for Next Session
```bash
# 이 세션의 핵심 = 스킬 인프라 + "왜 조사 얕나" 규명. 코드 아님.
# 스킬: ~/.claude/skills/  (snowball-research, handoff, handoffplan)
# 메모리 확인: feedback_research_method_snowball, feedback_never_enter_play_mode

# 다음 액션 (THE 하나):
#   유저에게 100-소스 coverage 조사 토픽 확정받고 → Workflow 스크립트로
#   병렬 fan-out(search+fetch 다수 에이전트, harvest ~100 + deep-read ~20) 실행.
#   snowball(내 의지 의존)로 하지 말 것 — 조기종료 재발. Workflow = 외부 강제.

# 절대 하지 말 것:
#   - 소스 2-3개로 "saturation" 선언
#   - 내용 안 보여주고 승인만 요구
#   - 판단 없이 "내 실수야" 동조
#   - Unity Play 모드 진입
```

## Session Closed
**Closed at:** 2026-07-01
**Commit:** 32ab2f4
**Session status:** Handed off to next session
