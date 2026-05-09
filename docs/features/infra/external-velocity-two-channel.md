# 외부 속도 채널 모델

환경 기믹이 슬라임 속도에 기여하는 채널들.

## 가산 채널 — `AddExternalVelocity(Vector2 deltaV)`
- `externalDeltaV` (Vector2): 매 FixedUpdate 소비/초기화. 호출측이 `acceleration * dt`를 넘김.
- 적용 시점: state switch 이후, `rb.linearVelocity` 대입 직전.
- **Airborne/Stunned** → velocity에 직접 가산. 매 프레임 누적.
- **Attached/Charging** → `surfaceTangent`에 투영 후 `velocity`에 가산 (법선 성분 제거, 스냅 지터 방지). **누적 안 됨** — 다음 frame state handler가 `velocity = (slideSpeed + crawlVelocity) * tangent` 로 덮어쓰므로 push는 한 프레임만 효과적. 같은 가속도라도 Attached가 Airborne 보다 ~50× 약하게 체감.
- 사용처: WindZone2D (v1), ConveyorBelt, WaterZone(부력).

## 적분 가산 채널 — `AddSustainedFlowImpulse(Vector2 deltaV)`
- 가산 채널의 attached-누적-소실 문제를 우회하는 변형.
- **Airborne/Stunned** → `externalDeltaV += deltaV` (가산 채널과 동일 경로).
- **Attached/Charging** → `tangentDelta = Vector2.Dot(deltaV, surfaceTangent)` 후 `slideVelocity += tangentDelta` (inertial) 또는 `crawlVelocity += tangentDelta` (legacy)에 직접 적분. 영구 채널에 누적되므로 frame 간 보존되어 Airborne 와 동일한 가속 거동.
- 사용처: WaterCurrentZone, WindZone2D_v2.

## Hazard 가산 채널 — `ApplyHazardPush(Vector2 deltaV, Vector2 hitPoint)`
- `externalDeltaV` 동일 경로 — 가산 + `OnHazardPushed` 이벤트 발화 (SlimeBurnVisual 등 반응).
- 사용처: GraniteBlast Branching/Beam(legacy)/Burst.

## Hazard Overwrite 채널 — `ApplyHazardPushOverwriteSlide(Vector2 deltaV, Vector2 hitPoint)`
- Attached/Charging 상태에서: `crawlVelocity` / `slideVelocity` 영구 채널을 wipe 후 `Vector2.Dot(deltaV, surfaceTangent)` 로 `crawlVelocity` 를 set. 다음 FixedUpdate 의 `velocity = surfaceTangent * (slideSpeed + crawlVelocity)` 가 새 값으로 재구성됨.
- Airborne/Stunned: `externalDeltaV += deltaV` 로 fallback (slide 채널 비활성).
- 가산 채널과 달리 기존 슬라이드 momentum 을 **덮어씀** — 이미 슬라이딩 중인 슬라임도 새 push 속도로 즉시 핀(pin) 됨.
- 중력 슬로프 슬라이드(`slideSpeed`)는 매 프레임 재계산되므로 hazard 가 호출을 멈추면 자연스럽게 복귀.
- `OnHazardPushed` 동시 발화.
- 사용처: GraniteBlast Beam (`beamOverwriteSlide=true`).

## 곱셈 채널 — `MultiplyVelocity(float factor)` / `ApplyDragFactor(float factor)`
- `velocity *= factor` 직접 적용. 프레임당 호출.
- `ApplyDragFactor`는 DragZone 호환용 별칭. `MultiplyVelocity`가 정식 이름.
- 사용처: DragZone(감속), WaterZone(드래그).

## 부가 — `ClampFallSpeed(float max)`
- 곱셈 채널의 특수 케이스. 하향 속도를 max로 제한.
- 사용처: DragZone, WaterZone.
- **중력축 분해 패턴** (GravityField 대응):
```csharp
float gravComponent = Vector2.Dot(velocity, gravityUp);
float hComponent = Vector2.Dot(velocity, gravityRight);
velocity = gravityUp * gravComponent + gravityRight * hComponent;
```

## 적분 순서
```
중력/상태물리 → 외부힘(AddExternalVelocity) → 플랫폼속도 → rb.linearVelocity 대입
```

## TeleportTo 시
`externalDeltaV = Vector2.zero` 초기화 (이전 위치의 외부 힘 잔여분 방지).

## IPlatformEffect 라이프사이클
- `CheckPlatformEffect(surfaceCollider)`: 착지 시 `GetComponentInParent<IPlatformEffect>()` 조회. 변경 시 `OnSlimeDetach`/`OnSlimeAttach` 호출.
- `OnSlimeStay(slime, dt)`: `FixedUpdateAttached`와 `FixedUpdateCharging`(표면 위)에서 매 프레임 호출.
- `ClearPlatformEffect()`: 분리 시 호출 (표면 이탈, Launch, Hop, Teleport, 기절, 오버라이드).
- `CheckPlatformEffect`는 착지 시퀀스(velocity/state 설정) 완료 후 호출 → JumpPad처럼 즉시 `ForceAirborneWithVelocity` 호출해 상태 되돌릴 수 있음.

## ForceAirborneWithVelocity
외부에서 호출 가능한 공개 launch 메서드. IPlatformEffect 구현체(JumpPad, Catapult 등)가 사용.
- `FinalizeAirborneExit()` 내부 헬퍼를 Launch/Hop과 공유하여 코드 중복 방지.
- 차징 상태 정리, 캐리어 분리, 플랫폼 효과 해제, `RegisterOverlapping`(ReattachGuard 모드 — 물리 충돌은 유지, probe 재접착만 차단), 에너지 추적 리셋 포함.

## 관련
- [../mechanisms/wind-zone.md](../mechanisms/wind-zone.md)
- [../mechanisms/drag-zone.md](../mechanisms/drag-zone.md)
- [../mechanisms/water-zone.md](../mechanisms/water-zone.md)
- [../mechanisms/conveyor-belt.md](../mechanisms/conveyor-belt.md)
- [../mechanisms/jump-pad.md](../mechanisms/jump-pad.md)
