# WaypointPlatformV2 샌드위치 압착 — 작업 히스토리 (사실 기록)

> 이 문서는 시도별 **구현 메커니즘**과 **관찰된 동작**만 기록한다. 원인 분석·수정 방향은 적지 않는다(다음 세션이 백지에서 조사).

## 목표
두 `WaypointPlatformV2` 플랫폼이 위/아래로 서로 마주보며 다가와 슬라임을 압착한다. 원하는 동작:
1. 슬라임이 압착 정도에 맞춰 **눈에 보이게 눌린다**(찌부러지는 변형).
2. 슬라임이 **천천히 옆으로 압출되어** 빠져나간다.
3. 누르는 플랫폼은 슬라임에 **걸려서 멈추고 기울었다가**, 슬라임이 빠져나가면 **정상 이동을 재개**한다.

페어링: 두 데크는 `partner` 참조 + `isLeader`로 연결. leader 탑승 시 `partner.TriggerFromLeader()`로 동시 출발. 위/아래 반대 이동은 각 데크의 waypoints가 결정.

## 시도별 기록

### 시도 1 — 플랫폼이 velocity nudge (`WaypointPlatformV2.ApplyCrushEscape`)
- 메커니즘: leader가 slime이 두 plate 사이 gap에 있으면 매 FixedUpdate `slime.AddExternalVelocity(perp * sign * strength * dt)`로 압착축 수직 방향으로 밀었다. 이후 overlap 기반 감지 + `crushDetectRange` + ice 데크로 보강.
- 관찰: 슬라임이 안 밀려나고 내려오는 플랫폼이 **그대로 통과/파묻힘**.

### 시도 2 — core 충돌에 pinch-resolver (`SurfaceProbe.SnapToSurface` → `ResolvePinch`)
- 메커니즘: Phase A depenetration 직후 `ResolvePinch`(Phase A.5)를 넣어, 겹친 collider별 dir/depth를 모아 **반대(opposing) 한 쌍**이 확인되면 pinch축 수직 tangent로 슬라임을 옆으로 position push. + sandwich 데크에 `SurfaceProbeModeOverride(Multi)`를 자동 부착(SnapToSurface는 Multi 프로브 모드에서만 돌기 때문).
- 관찰: 슬라임이 **옆으로 밀려나옴(동작함)**. 단 **너무 빨리** 빠지고, 눌리는 표현은 없음.

### 시도 3 — 시간 게이트 + localScale squash
- 메커니즘: `ResolvePinch`에 `pinchDwellTimer` 시간 게이트(Press/Slide 2-phase)로 탈출을 지연. `SlimeVisual.ComputePinchSquash`는 `PinchCompression01 * pinchSquashIntensity`로 body localScale을 anisotropic 압축(intensity 노브 기반). 노브 `pinchMaxStep/pinchPressTime/pinchSlideRampTime/pinchSquashIntensity` 등.
- 관찰: 여전히 빨리 빠짐. `pinchMaxStep`/`pinchPressTime`을 어떻게 바꿔도 탈출 속도가 변하지 않고 플랫폼 속도에 맞춰짐. 눌림 표현은 `pinchSquashIntensity`를 0.0001~50 어떻게 줘도 안 보임.

### 시도 4 (현재 코드) — 명시적 platform↔slime 커플링
시도 2·3의 코드(ResolvePinch, 옛 pinch 노브 전부, 강제 Multi)를 **모두 제거**하고 재설계.
- `WaypointPlatformV2.UpdateSandwich()` (leader 전용): 두 plate face의 gap을 계산. `gap < 2R` 이고 슬라임이 axis 사이 + |lateral| ≤ halfSpan이면 catch → `slime.SetPinch(axisN, tangent, gapCenter, gap, halfSpan)`를 매 프레임 보고. `gap ≤ 2R − pinchPressDepth`면 `pinchStall`(자신+partner)로 `AdvanceRigidTarget`의 `pathParam`을 freeze(더 안 밀고 멈춤). 슬라임 쪽으로 scissor tilt.
- `SlimeController.Pinch.cs` (신규 partial): `SetPinch`가 보고를 받고, `UpdatePinch`(FixedUpdate 맨 끝, `OutputVelocity` 후)가 슬라임을 두 plate 중앙 + 제어된 lateral로 `rb.position`을 직접 소유, `velocity`/`linearVelocity`=0. Press → Hold(`pinchHoldTime`) → Extrude(`pinchExtrudeSpeed`로 옆으로) → |lateral| ≥ halfSpan이면 release.
- `SlimeVisual.ComputePinchSquash`: intensity 제거, gap-driven. 축 scale = `max(pinchMinThickness, 1−comp01)`, 수직 = `1/sqrt(along)`.
- 관찰: 슬라임이 안 밀려나고 **그대로 통과**.

## 현재 코드 위치 (시도 4)
- `SlimeJumping_v2/Assets/Scripts/Platforms/WaypointPlatformV2.cs` — `UpdateSandwich()`, `pinchStall`, `pinchPressDepth`, `AdvanceRigidTarget` freeze.
- `SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Pinch.cs` — `SetPinch` / `UpdatePinch` / `ReleasePinch`, `PinchActive`/`PinchCompression01`/`PinchAxis`.
- `SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs` — `UpdatePinch(dt)` 호출(FixedUpdate 끝), 공개 프로퍼티 `PinchActive`/`PinchCompression01`/`PinchAxis`.
- `SlimeJumping_v2/Assets/Scripts/Visual/SlimeVisual.cs` — `ComputePinchSquash`.
- 노브: `SlimeSettings.pinch{PressTargetComp, PressMaxTime, HoldTime, ExtrudeSpeed}`, `SlimeVisualSettings.pinch{MinThickness, SquashSmoothTime}`, `WaypointPlatformV2.pinchPressDepth`.
- 테스트 씬 생성: 메뉴 `SlimeJumping/Platform/Setup WaypointPlatformV2 Test` (C 클러스터가 샌드위치). 코드: `SlimeJumping_v2/Assets/Scripts/Editor/WaypointPlatformV2TestSetup.cs`.
- 전체 재설계 플랜: `C:\Users\poiso\.claude\plans\adaptive-finding-pike.md`.

## 현재 상태
시도 4 = 슬라임이 압착 시 옆으로 안 밀리고 그대로 통과. 시도 2에서는 (느낌은 별로였어도) 슬라임이 옆으로 빠져나오긴 했음.
