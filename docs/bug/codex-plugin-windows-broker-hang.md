# 툴링 — `/codex:*` 스킬이 Windows 에서 동작 안 함

**확인:** 2026-08-03 · **상태:** 상류 버그, 우회책 확정
**환경:** Windows 11 Pro · codex 플러그인 1.0.4 · codex-cli 0.137.0

---

## 증상

`codex:codex-rescue` 서브에이전트로 태스크를 보내면 "started" 는 뜨는데 결과가 안 온다.

- `status` 는 잡을 `running` 으로 계속 보고하는데 **PID 는 이미 죽어 있음**
- 심하면 **잡 레코드 자체가 안 생김** (`status --all` 이 `running: []`)
- `result` 조회하면 `No job found`

**"running" 표시를 믿으면 안 된다.** PID 생사를 따로 확인해야 한다:

```powershell
node "$env:USERPROFILE\.claude\plugins\cache\openai-codex\codex\1.0.4\scripts\codex-companion.mjs" status --all --json
Get-Process -Id <pid> -ErrorAction SilentlyContinue      # 없으면 좀비
```

## 상류 이슈

| 이슈 | 내용 |
|---|---|
| [anthropics/claude-code#48520](https://github.com/anthropics/claude-code/issues/48520) | 플러그인 Windows broker IPC 행. `waitForBrokerEndpoint()` 무한 대기, **`writeJobFile()` 미도달 → `jobs/` 미생성**, PID 죽어도 레코드는 `running`. 보고 실패율 ~20% |
| [openai/codex-plugin-cc#236](https://github.com/openai/codex-plugin-cc/issues/236) | Windows 에서 `codex app-server` 를 `shell: true` 로 spawn → stdio/인코딩 깨짐. CLI 직접 호출은 정상 |
| [openai/codex#16364](https://github.com/openai/codex/issues/16364) | 0.117.0+ app-server 가 툴콜 몇 개 뒤 멈춤 |

## 이 환경 실측

- 살아있던 broker 는 `pipe:\\.\pipe\cxc-**Ek9AEd**-codex-app-server` 를 서비스, 세션 런타임은 `cxc-**zCoMj1**` 을 찾음 → **endpoint mismatch**
- `cxc-zCoMj1\broker.pid` 안의 PID 는 이미 죽음
- companion 밑에서 뜬 `codex.js app-server --help` 가 **9분간 안 끝남**. 같은 명령을 직접 실행하면 정상 종료 → CLI 가 아니라 spawn 방식 문제
- 잔존 프로세스/디렉터리 정리 후 **재시도해도 동일 실패**. 좀비는 결과지 원인이 아님

## 우회책 (확정 — 이것만 동작)

skill dispatch 를 건너뛰고 CLI 직접 호출:

```powershell
$sp = "<scratchpad>"
Get-Content "$sp\prompt.txt" -Raw | codex exec --sandbox read-only --skip-git-repo-check - *> "$sp\out.txt"
```

- `--sandbox read-only` — Codex 가 파일을 못 고침. 리뷰용이면 반드시 붙일 것
- `-` — 프롬프트를 stdin 으로. 긴 프롬프트는 파일에 써서 파이프
- `*>` — stdout+stderr 전부 캡처
- 최종 답변은 출력의 **마지막 `codex` 마커 블록 뒤**에 있다

Codex 가 정말 아무것도 안 고쳤는지는 타임스탬프로 확인:

```powershell
Get-Item <file> | Select-Object LastWriteTime       # codex 프로세스 CreationDate 와 비교
git diff --stat
```

## Git Bash 함정

companion 을 Git Bash 에서 돌리면 MSYS 가 인자를 경로로 바꾼다:

```
taskkill /PID 26000 /T /F: exit=1: 잘못된 인수/옵션 - 'C:/Program Files/Git/PID'
```

→ cancel/kill 이 전부 실패한다. **companion 은 PowerShell 에서 돌릴 것.**

## 좀비 정리

```powershell
Remove-Item "$env:LOCALAPPDATA\Temp\cxc-*" -Recurse -Force
Get-CimInstance Win32_Process -Filter "Name='node.exe' OR Name='codex.exe'" |
  Where-Object { $_.CommandLine -match 'app-server|codex-companion' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

정리해도 dispatch 는 안 고쳐진다. 위 우회책을 쓸 것.
