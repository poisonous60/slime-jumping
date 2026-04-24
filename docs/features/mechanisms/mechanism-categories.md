# 메커니즘 카테고리 (6분류)

모든 환경 기믹은 아래 6개 중 하나로 분류. 공통 인터페이스와 파이프라인으로 확장.

## 분류
1. **환경 존** (지속 효과) — `EnvironmentalZone` 베이스. 물, 비, 바람, 중력장, DragZone.
2. **위험** (즉발 + 경직) — `IHazardSource` + `ApplyHazardEffect` + `Stunned` 상태. 가시, 톱날, 번개, 파이어볼, 카메라, 칼.
3. **플랫폼 효과** (착지/접촉 트리거) — `IPlatformEffect` 컴포넌트. 컨베이어, 점프대, 투석기, 탄력 나뭇가지.
4. **인터랙터블** (E키 / Interact 액션) — `IInteractable` + `InteractionDetector` + `InteractionPromptManager`(E 아이콘). 침대, TV, 퍼즐 문, 메모.
5. **오버라이드 컨트롤러** (슬라임 전체 탈취) — `IExternalController`. 삼킴, 앉기, 퍼즐 오버레이, 시네마틱.
6. **소프트 피직스 오브젝트** (자체 시뮬) — SlimeController 무접촉. Verlet 로프, 천, 적 AI.

## 초기 대략 분류 (구 설계)
- **A. Trigger volume류** (포탈, 킬존, 체크포인트, 바람, 부스터): Unity trigger collider로 표현. `OnTriggerEnter2D`는 `OnCollisionEnter2D`와 독립적 콜백. 물리적 배치(벽 표면보다 약간 앞)만으로 SlimeController 충돌 로직보다 먼저 처리 가능. Hook 불필요.
- **B. Contact interceptor류** (no-stick 영역, 바운스 패드, 부서지는 블록): 실제 collision 이벤트에 끼어들어 attach 로직을 대체/수정.

## 인터페이스 파일
- [IPlatformEffect.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/IPlatformEffect.cs)
- [IHazardSource.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/IHazardSource.cs)
- [IInteractable.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/IInteractable.cs)
- [IExternalController.cs](../../../SlimeJumping_v2/Assets/Scripts/Interfaces/IExternalController.cs)
