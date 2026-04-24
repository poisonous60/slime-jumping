# Tier 1 Fullscreen Hazard / 인테리어

## JumpscareTrigger + JumpscareCanvasController
풀스크린 페이드 + `ImpulseEmitter` (카메라 셰이크).

## CameraFlashHazard + FlashCanvasController
Self-contained 트리거, 뷰파인더 UI 오버레이.

## 공용 VFX 베이스
- `FullscreenEffectCanvas` / `FullscreenOverlayCanvas` — 풀스크린 이펙트 공용 베이스 추상 클래스.
- `ImpulseEmitter` — Cinemachine 3.x `CinemachineImpulseSource` 래퍼.

## 관련
- [../infra/vfx-infra.md](../infra/vfx-infra.md) — 공용 VFX 인프라
- [saw-knife-hazard.md](saw-knife-hazard.md) — SliceEffect 등 접촉 기반 해저드
