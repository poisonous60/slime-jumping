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
- `docs/` — Design documents (in Korean). The primary design reference is `SlimeJumping_메커닉_설계_v3.md`.
- `SlimeJumping_AssetVault/` — External asset packs stored outside the Unity project.


## Development Commands

Use `unity-cli` command to interact with the Unity Editor.
Available commands: `list`, `exec`, `run_tests`, `console`, `screenshot`, `refresh_unity`, `manage_editor`, `execute_menu_item`, `reserialize_assets`.
Don't use Comment when using `exec` commands. It returns error.
`unity-cli exec` requires a return statement (e.g. `return 1;`). Expressions like `Debug.Log(...)` alone will fail.


### Coding Convention
- Use `rb.position` instead of `transform.position`