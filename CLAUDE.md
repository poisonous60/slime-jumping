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
- `docs/` — Design documents (in Korean). The primary design reference is `SlimeJumping_메커닉_설계_v3.md`.
- `SlimeJumping_AssetVault/` — External asset packs stored outside the Unity project.

## Unity Project Configuration

- **Unity version**: 6000.3.11f1
- **Render pipeline**: URP 17.3.0 with separate PC and Mobile renderer/quality assets in `Assets/Settings/`
- **Input**: New Input System 1.19.0 (action map at `Assets/InputSystem_Actions.inputactions`)
- **Key packages**: AI Navigation 2.0.11, Timeline 1.8.11, Visual Scripting 1.9.10
- **CLI connector**: `com.youngwoocho02.unity-cli-connector` — an editor HTTP server that lets external tools (like Claude Code) execute C# in the editor, run tests, take screenshots, and manage the editor. Registered via git URL in `Packages/manifest.json`.


## Development Commands

Use `unity-cli` command to interact with the Unity Editor.
Available commands: `list`, `exec`, `run_tests`, `console`, `screenshot`, `refresh_unity`, `manage_editor`, `execute_menu_item`, `reserialize_assets`.
