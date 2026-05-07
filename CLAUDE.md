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

- 코드를 파악할 때는 unity-cli로 파악하지 말고 코드 구현만 파악할 것. unity-cli로 실시간 동작을 봐야 할 경우는 debug component를 작성해서 사용자에게 실행 부탁할 것.
- Before using `exec`, check if another `unity-cli` command can query/modify/add what you need.
- After changes, only check the error log. Do not run the game and take a screenshot.
- 새로 만든 `.cs` 파일이 인스펙터 Add Component 에서 안 뜨고, `Type.GetType(...)` 로도 NOT FOUND 나는 경우. Unity가 메타(guid)는 등록했지만 컴파일 목록(`Library/Bee/artifacts/*.dag/Assembly-CSharp.rsp`)에는 추가하지 않은 상태. 해결: `unity-cli exec "UnityEditor.AssetDatabase.DeleteAsset(\"<path>\"); return \"deleted\";"` → `Write` 로 동일 경로에 재생성 → `unity-cli refresh_unity`.



### Coding Convention
- Use `rb.position` instead of `transform.position`
- If some feature add or change, update  `SlimeJumping_메커닉_설계_v6.md`, `프로토타입_구현_현황.md` document in /docs folder
- Never use `??` or `?.` with UnityEngine.Object references (Component, GameObject, etc). C# null-coalescing operators bypass Unity's overloaded `==` and mis-handle "fake null" (destroyed-but-not-null) references.
- Divide visual and collider. Don't let them effect each other (e.g. even if visual rotate, collider shouldn't be effected)





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