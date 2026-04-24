# Tier 1 인터랙터블 (E키 상호작용)

`InteractableBase` 상속, **E 입력** (Interact 액션, Gamepad: buttonWest) 으로 발동.

## 감지 & 프롬프트 인프라
- `InteractionDetector`: Trigger 기반 후보 감지 (Slime child, Kinematic RB2D, InteractionSensor layer — Interactable layer만 충돌). broad-phase + narrow-phase(slime body overlap) 조합.
- `CurrentTarget` / `OnTargetChanged` / `OnInteracted` 이벤트.
- `InteractionPromptManager`: 현재 타깃 상단에 반투명 "E" TMP 월드 스페이스 표시. E를 누르거나 범위 벗어나면 숨김. 같은 타깃은 범위 밖으로 나갔다 재진입해야 다시 표시.
- `ReleaseOverride.Rescan`: override 해제 후 동일 trigger 재등록.

## 개별 인터랙터블

### SitPoseInteractable (IExternalController)
침대/벤치/신사/화톳불 옆자리. 물리 동결 + anchor 텔레포트만 담당.

**앉기 애니메이션**: 슬라임에 정적 부착된 `SitPoseAnimator` 컴포넌트가 구동 (외부 튠 라이브러리 없음):
- 4단계 상태머신: `SittingDown` OutBack squash → `Idle` Spring-Damper breathing → `GettingUp` anticipation + OutElastic settle → `Released`.
- 가구별 튜닝은 `SitPoseProfile` ScriptableObject.
- 비주얼 쓰기는 `SlimeVisual.SetExternalDeform(scale, asymmetry, chargeDip)` — `IsExternallyAnimated` 게이트 준수, MPB 경유로 SRP Batcher 호환.
- 일어나기 완료는 `SitPoseAnimator.OnFullyReleased` 이벤트 → Interactable이 그 시점에 `ReleaseOverride` 호출 (get-up 중 물리 동결 유지).

**3D 메쉬 호환**:
- `SlimeVisual.ApplySitDepthShift/RestoreSitDepthShift`가 face 스프라이트와 sibling `body3d` 서브트리 Z 동시 이동.
- `SetExternalSurfaceNormal(Vector2.up)`으로 sit 중 셰이더 법선을 world-up 고정 (진입 표면 각도 무관).
- `BodyNativeHeight`는 mesh `sharedMesh.bounds × rest lossyScale` 기반 rest-only 캐시 (피드백 루프 방지) — SlimeVisual-local 단위 반환.
- `body3d.localRotation`은 sit 중 `body3dRestRotation`만 적용 (벽/경사 틸트 무시). 일반 플레이에선 `body.localRotation * body3dRestRotation` 합성.

### MediaInteractable_TV
머티리얼 / 오디오 토글 (WebView는 추후).

### MediaInteractable_Phonograph
트랙 순환, 디스크 축 선택 회전, 클립 프리로드.

### ApplianceInteractable_Washing
토글 + 시각 루트 리사주 진동 (콜라이더 분리 원칙 준수).

### FireworkInteractable
`ForceAirborneWithVelocity` 발사. `launchDelay`, `pushOriginOffset`, `launchAwayFromCenter`.

### FireworkInteractable_V2
오브젝트 자체가 폭죽 로켓. E 입력 시 `rocketVisual` 자식이 `flightDirection × flightDistance` 만큼 `flightDuration` 동안 `flightCurve` 로 비행 → 정점에서 `explosionVFX` 를 현재 위치로 이동 후 재생, `ForceAirborneWithVelocity` 로 슬라임 발사, `impulseEmitter` 카메라 쉐이크, `explosionSfx` 재생 → `rocketVisual` SetActive(false) → `respawnDelay` 초 대기 → 원위치 복귀 + `respawnScaleCurve` 팝인 (+선택적 `respawnAnimatorTrigger`). 비행/폭발/리스폰 전 구간 `ExtraCanInteract` 로 재입력 차단. 콜라이더/프롬프트는 루트에, 비주얼만 rocketVisual 에 두어 분리 원칙 준수.

### ShrineInteractable (IExternalController)
일본 신사 앞에서 E로 데모 엔딩 진입.
- `wispAnchor`에 픽업 프리팹 (기본 `Assets/Prebs/VFX/StrawberryPickup.prefab` — 딸기 스프라이트) 인스턴스화 (스폰 시 `wispScale` 필드로 루트 스케일 적용).
- `appearHold`초 홀드 → `SceneTransitionController.PlayReverse(endSceneName, wispPos)` 호출 → 인트로 아이리스의 대칭 역전(수축 → 블랙 → 다음 씬 로드 → 페이드인).
- 연출 동안 슬라임은 `SetOverride(self)`로 동결 (no-op ExternalController) — 씬 전환과 함께 자연 소멸.
- 픽업 프리팹: `SpriteRenderer` + `WispFlicker` + 3D `Point Light` 자식. `WispFlicker`가 스폰 연출(scale 0 → overshoot → settle, alpha fade-in, `spawnDuration` 0.9s)을 구동하면서 idle bob/drift/scale-wobble/alpha-flicker blend. 같은 ramp로 링크된 `Light.intensity` 펄스 (URP 2D Renderer 아닌 3D Point Light — 신사 3D 메쉬 직접 조명).

## 인터페이스
- [IInteractable.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/IInteractable.cs) — E키, Interact 액션
- [IExternalController.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/IExternalController.cs) — 외부 제어 오버라이드

## 관련
- [../slime/state-machine.md](../slime/state-machine.md) — IExternalController override
- [memo.md](memo.md) — 버튼 없는 트리거 연출 (별도 파이프라인)
