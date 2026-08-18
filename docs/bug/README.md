# 버그 기록

증상 보고 → 원인 지목 → 수정 → 검증 상태를 한 건씩 남긴다.
기능 개발 진행 기록(`docs/*-attempts.md`)이나 세션 핸드오프(`plans/handoffs/`)와는 구분한다.

| 문서 | 증상 | 상태 |
|---|---|---|
| [cut-ceiling-jitter.md](cut-ceiling-jitter.md) | 윗면 잘린 슬라임이 오버행에 붙으면 떨림. 아랫면 잘림 + 바닥은 정상 | 원인 미확정, 재현 절차 확보 |
| [cutshape-lobe-escape.md](cutshape-lobe-escape.md) | `[CutShape] lobe escapes EffRadius` — 컷 로브가 몸 반경 밖으로 나가 콜라이더에 쓰임 | 수정 적용, 플레이 검증 대기 |
| [ropev3-attach.md](ropev3-attach.md) | RopeV3 부착 문제 | 이전 세션 기록 |
| [codex-plugin-windows-broker-hang.md](codex-plugin-windows-broker-hang.md) | (툴링) `/codex:*` 스킬이 Windows 에서 결과를 못 돌려줌 | 상류 버그, 우회책 확정 |
| [gameview-overlay-top-row.md](gameview-overlay-top-row.md) | 전체화면 페이드 판이 Game 뷰 맨 윗줄 1행을 못 덮음. 뷰포트 높이가 소수(820.5)라 UI 오버레이 시저가 그 줄을 자름 | 원인 지목, 빌드 재현 여부 미확인 |
| [prefab-audio-values-destroyed.md](prefab-audio-values-destroyed.md) | (작업 사고) 프리팹에 남아 있던 옛 클립·볼륨 값을 읽지 않고 덮어써서 소실. git ignore 폴더라 복구 불가 | 값 소실 확정, 재발 방지 규칙 기록 |

## 쓰는 방식

- **원인을 값으로 지목한 뒤에 코드를 고친다.** 증상 보고에 코드 추가로 반응하지 않는다 (CLAUDE.md)
- 폐기된 수정도 반례와 함께 남긴다 — 다음 세션이 같은 걸 다시 시도하지 않도록
- 검증은 단계별로 통과/미수행을 구분해 적는다. "컴파일 통과"는 검증이 아니다
- Play 판정은 유저만 한다. 판정 기준을 문서에 미리 적어 둘 것
