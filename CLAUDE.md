# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.


## Before We Start
- Use only English if user asks you in Korean. Korean is 7 times expensive than english.
- Don't use agent if you can do it yourself ability. Agent is expensive. 


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
Don't use Comment when using `exec` commands. It returns error.
`unity-cli exec` requires a return statement (e.g. `return 1;`). Expressions like `Debug.Log(...)` alone will fail.

If a newly-written `.cs` file fails to compile with "type could not be found" AND `CompilationPipeline.GetAssemblies()` doesn't list it under Assembly-CSharp, Unity's AssetDatabase has a stuck import entry — `refresh_unity` / `AssetDatabase.Refresh` / `ImportAsset(ForceUpdate)` won't fix it. Delete both `.cs` and `.cs.meta`, refresh, then rewrite the file to force a fresh import.


### Coding Convention
- Use `rb.position` instead of `transform.position`
- If some feature add or change, update  `SlimeJumping_메커닉_설계_v6.md`, `프로토타입_구현_현황.md` document in /docs folder
- Never use `??` or `?.` with UnityEngine.Object references (Component, GameObject, etc). C# null-coalescing operators bypass Unity's overloaded `==` and mis-handle "fake null" (destroyed-but-not-null) references.
- Divide visual and collider. Don't let them effect each other (e.g. even if visual rotate, collider shouldn't be effected)