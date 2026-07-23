# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.


## Project Overview

SlimeJumping_v2 is a **2.5D platformer** built in **Unity 6** (6000.3.11f1) where the player controls a viscous slime that sticks to all surfaces and moves via a charge-and-launch slingshot mechanic. The project is in early development — no gameplay scripts exist yet beyond default Unity template files.

## Repository Layout

- `SlimeJumping_v2/` — The Unity project root. Open this folder in Unity Hub.
- `SlimeJumping_v2/Assets/` — All game assets go here.
- `SlimeJumping_v2/ProjectSettings/` — Unity project configuration (do not manually edit).
- `docs/` — Design documents (in Korean). The primary design reference is `SlimeJumping_메커닉_설계_v6.md`, `프로토타입_구현_현황.md`.
- `SlimeJumping_AssetVault/` — External asset packs stored outside the Unity project.


## Development Commands

Use `unity-cli` command to interact with the Unity Editor.
Available commands: `list`, `exec`, `run_tests`, `console`, `screenshot`, `refresh_unity`, `manage_editor`, `execute_menu_item`, `reserialize_assets`.

exec : Execute C# Code
Run arbitrary C# code inside the Unity Editor at runtime. This is the most powerful command — it gives you full access to UnityEngine, UnityEditor, ECS, and every loaded assembly. No need to write a custom tool for one-off queries or mutations.

Use return to get output. Common namespaces are included by default. Add --usings only for project-specific types (e.g. Unity.Entities). The csc compiler and dotnet runtime are auto-detected; if detection fails, specify manually with --csc <path> or --dotnet <path>.

```
unity-cli exec "return Application.dataPath;"
unity-cli exec "return EditorSceneManager.GetActiveScene().name;"
unity-cli exec "return World.All.Count;" --usings Unity.Entities

# Pipe via stdin to avoid shell escaping issues
echo 'Debug.Log("hello"); return null;' | unity-cli exec
echo 'var go = new GameObject("Marker"); go.tag = "EditorOnly"; return go.name;' | unity-cli exec
```

Because exec compiles and runs real C#, it can do anything a custom tool can — inspect ECS entities, modify assets, call internal APIs, run editor utilities. For AI agents, this means zero-friction access to Unity's entire runtime without writing a single line of tool code. Piping via stdin avoids shell escaping headaches with complex code.

unity-cli 확인은 `console` 컴파일 에러 뿐. 예상 못 한 출력 나와도 조사 금지, 한 줄 보고 후 정지. Play 모드 금지.



### Coding Convention
- 요청 안 받은 산출물(테스트 파일, 헬퍼, 에디터 툴, 새 문서) 만들지 말고 물어본다.
- Use `rb.position` instead of `transform.position`
- If some feature add or change, update  `SlimeJumping_메커닉_설계_v6.md`, `프로토타입_구현_현황.md` document in /docs folder
- Never use `??` or `?.` with UnityEngine.Object references (Component, GameObject, etc). C# null-coalescing operators bypass Unity's overloaded `==` and mis-handle "fake null" (destroyed-but-not-null) references.
- Divide visual and collider. Don't let them effect each other (e.g. even if visual rotate, collider shouldn't be effected)
- SlimeController에 외부 시스템(Zone, Platform, Hazard, IExternalController 등) 통합 코드를 추가할 때는 `SlimeController.{SubsystemName}.cs` partial 파일로 분리한다. Main `SlimeController.cs`에는 라이프사이클(Awake/Update/FixedUpdate orchestration), 상태머신 dispatch, 공유 유틸/공유 상태 필드만 둔다. 상태별 처리(Attached/Charging/Airborne/Stunned)는 별도 패턴이며 기존대로 유지.





Behavioral guidelines to reduce common LLM coding mistakes. Merge with project-specific instructions as needed.

**Tradeoff:** These guidelines bias toward caution over speed. For trivial tasks, use judgment.

## 1. Think Before Coding

**Don't assume. Don't hide confusion. Surface tradeoffs.**

Before implementing:
- State your assumptions explicitly. If uncertain, ask.
- If multiple interpretations exist, present them - don't pick silently.
- If a simpler approach exists, say so. Push back when warranted.
- If something is unclear, stop. Name what's confusing. Ask.

## 2. Simplicity First

**Minimum code that solves the problem. Nothing speculative.**

- No features beyond what was asked.
- No abstractions for single-use code.
- No "flexibility" or "configurability" that wasn't requested.
- No error handling for impossible scenarios.
- If you write 200 lines and it could be 50, rewrite it.

Ask yourself: "Would a senior engineer say this is overcomplicated?" If yes, simplify.

## 3. Surgical Changes

**Touch only what you must. Clean up only your own mess.**

When editing existing code:
- Don't "improve" adjacent code, comments, or formatting.
- Don't refactor things that aren't broken.
- Match existing style, even if you'd do it differently.
- If you notice unrelated dead code, mention it - don't delete it.

When your changes create orphans:
- Remove imports/variables/functions that YOUR changes made unused.
- Don't remove pre-existing dead code unless asked.

The test: Every changed line should trace directly to the user's request.

## 4. Goal-Driven Execution

**Define success criteria. Loop until verified.**

Transform tasks into verifiable goals:
- "Add validation" → "Write tests for invalid inputs, then make them pass"
- "Fix the bug" → "Write a test that reproduces it, then make it pass"
- "Refactor X" → "Ensure tests pass before and after"

For multi-step tasks, state a brief plan:
```
1. [Step] → verify: [check]
2. [Step] → verify: [check]
3. [Step] → verify: [check]
```

Strong success criteria let you loop independently. Weak criteria ("make it work") require constant clarification.

---

**These guidelines are working if:** fewer unnecessary changes in diffs, fewer rewrites due to overcomplication, and clarifying questions come before implementation rather than after mistakes.