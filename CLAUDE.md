# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.


## Before We Start
- Use only English if user asks you in Korean. Korean is 7 times expensive than english.


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


### Audio
All gameplay audio routes through `SoundService` with a `SoundCategory` — that's what hooks it to the option sliders. Rules, mixer layout, and playback recipes live in [docs/features/infra/audio-system.md](docs/features/infra/audio-system.md). Read it before adding any sound.