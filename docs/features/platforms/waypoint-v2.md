# WaypointPlatform v2 (`WaypointPlatformV2`)

`PlatformMotor` 상속. 사각형(BoxCollider2D) 이동 플랫폼. 기존 `WaypointPlatform`의 단순 lerp 루프 위에 **탑승 피드백 + 2가지 신규 거동**을 얹은 버전. 모든 연출은 `ComputeMotion` 의 단일 `(pos, rot)` 반환에 합쳐지므로 `PlatformMotor` 의 `DeltaPosition`/`DeltaRotation` → `PlatformCarrier` 라이더 캐리가 그대로 정확하게 동작. 샌드위치 슬라임 사출은 **코어 depenetration 수정**(아래 Pinch escape)이 담당, 플랫폼은 열림 연출만.

스크립트: `Assets/Scripts/Platforms/WaypointPlatformV2.cs`

## 라이더 감지 (신규 센서 없음)
`PlatformCarrier`(order -200)가 부착 중 매 FixedUpdate `ApplyRiderInfluence(pos,mass,vel,charging)`, 이탈 시 `RemoveRider()` 호출. v2는 `ApplyRiderInfluence`에서 `riderInfluencedThisStep` 플래그 + `riderPos/riderMass/riderVel` 캐시 → 같은 프레임 `ComputeMotion`(order -100)에서 소비 후 클리어. `prevRiderPresent` 로 **탑승 rising-edge** 산출.

## 연출 (매 FixedUpdate `ComputeMotion` 합성)
1. **Rigid path target** — `localWaypoints` 폴리라인을 `travelSpeed`로 진행, `easeCurve` per-leg 이징. Phase가 방향 결정. waypoint <2 개 또는 `mountTriggered` off → 정지(틸트/얼음 전용).
2. **Damped follow (UI 댐핑)** — `Vector2.SmoothDamp(smoothPos, rigidTarget, …, riderPresent ? mountedSmoothTime : followSmoothTime)`. 탑승 시 smoothTime↑ → 묵직한 UI-스프링 지연감.
3. **Vertical sag 스프링** — under-damped. 목표 `riderPresent ? -sagDepth*riderMass : 0`. 탑승 → 살짝 가라앉으며 흔들림, 이탈 → overshoot 반동. rb를 실제로 움직이므로 슬라임이 출렁임을 같이 탐.
4. **Tilt 스프링** (`enableTilt`) — `TiltPlatform` 수식 재사용: 라이더 localX/halfWidth → targetTilt, 스프링-댐퍼 → currentTilt. **콜라이더가 기울어** 슬라임이 미끄러짐.
5. 반환 `(smoothPos + (0, sagY), baseRot + currentTilt)`.

## 얼음 → 계속 점프 강제
- `iceMaterial` 필드(= `Settings/SurfaceMaterials/Ice.asset`)를 `OverrideSurfaceMaterial`로 노출 → 부착 시 carrier가 자동 push, 낮은 `slideResistanceMultiplier`로 미끄러짐.
- 얼음 + 가장자리 틸트 = **플랫폼 중심이 불안정 평형**: 미세 drift → 틸트 → 얼음 슬라이드 → 틸트↑ → 떨어짐. 정중앙에서도 떨어지도록 `idleTipBias`(deg): 라이더 속도 < `idleSpeedThreshold` 일 때 라이더 쪽으로 틸트를 약간 seed (창발적, 꼼수 아님).

## Feature 1 — 탑승 트리거 + 범위 유지 (상태기계)
`Phase { Parked, Departing, Holding, Returning }`, `mountTriggered` 토글.
- **Parked**(target=wp0): 탑승 rising-edge(또는 `TriggerFromLeader()`) → Departing.
- **Departing**: wp[last]까지 진행, 도착(`pathParam≈last` && smoothPos 근접) → Holding.
- **Holding**(target=wp[last]): 매 프레임 `Physics2D.OverlapCircle(wp[last], holdRange, riderLayers)`. 범위 내 슬라임 없으면 → Returning. (부착이 아닌 **근접** — 슬라임이 공중이어도 근처면 유지. `ThinIceRespawnZone` exit 패턴과 동일.)
- **Returning**: wp0 복귀 → Parked(재무장).

## Feature 2 — 샌드위치 페어 + Pinch (명시적 platform↔slime 커플링)
> 재설계 이유: 이전 3개 시도는 pinch를 **generic collision depenetration**(Phase A)에 의존 → off-center 슬라임의 대각선 normal 합이 1~2프레임에 옆으로 사출(시간/step 게이트 우회), kinematic 플랫폼이 dynamic 슬라임을 그냥 통과, localScale·intensity squash는 cheat(플랫폼 안으로 파고듦)라 다 실패. 해결: pinch를 **명시적 opt-in 상호작용**(bamboo-grab/rope 패턴)으로 — 플랫폼이 슬라임을 잡고 명시적으로 hold/extrude.

- 페어링: `partner`+`isLeader`. leader 탑승 → leader Departing + `partner.TriggerFromLeader()`. 반대 이동은 각 waypoints가 결정.

### A. 플랫폼: catch → stall → tilt → report (`WaypointPlatformV2.UpdateSandwich`, leader만)
- leader가 두 plate face 사이 gap 계산. `gap < 2R` & 슬라임이 axis 사이 & |lateral| ≤ halfSpan → **catch**.
- catch 시: `slime.SetPinch(axisN, tangent, gapCenter, gap, halfSpan)`로 매 프레임 보고. `gap ≤ 2R − pinchPressDepth`면 **stall**(자신+partner `pinchStall=true` → `AdvanceRigidTarget`에서 `pathParam` freeze → 더 안 밀고 멈춤). 슬라임 쪽으로 **scissor tilt**(comp01 비례, 기존 spring).
- 슬라임이 release되면(보고 끊김) stall 해제 → 정상 이동 재개.

### B. 슬라임: held + extrude (`SlimeController.Pinch.cs` partial)
핵심: **Phase A의 양면 cancel이 이미 슬라임을 중앙에 잡아둠**(=예전 burial). 그걸 원하는 상태로 쓰고, lateral만 명시적으로 소유 → collider ignore도, lateral-squirt resolver도 불필요.
- `SetPinch(...)` 보고 → pinch hold 진입/갱신(매 프레임 갱신, 안 오면 자동 release).
- `UpdatePinch`(FixedUpdate 맨 끝, OutputVelocity 후): 위치를 **소유** — `rb.position = gapCenter + tangent·pinchLateral`(두 plate 사이 중앙 + 제어된 lateral), `velocity/linearVelocity = 0`. Phase A의 옆 사출을 덮어씀 → `pinchExtrudeSpeed`가 유일한 가로 이동.
- gap-driven `PinchCompression01 = clamp01((2R−gap)/2R)`(intensity 없음).
- **Phase**: Press(플랫폼이 depth까지 누름, comp ≥ `pinchPressTargetComp` 또는 `pinchPressMaxTime`) → Hold(`pinchHoldTime` 동안 눌린 채) → Extrude(`pinchExtrudeSpeed`로 가까운 열린 쪽 이동) → |lateral| ≥ halfSpan이면 edge 너머로 R만큼 밀어 release(재pinch 방지).
- 노브: `SlimeSettings` → `pinchPressTargetComp(0.5)/pinchPressMaxTime(1.2)/pinchHoldTime(0.35)/pinchExtrudeSpeed(1.5)`; `WaypointPlatformV2.pinchPressDepth(0.45)`.

### C. 비주얼: gap-driven conform squash (`SlimeVisual.ComputePinchSquash`)
- `PinchActive`/`PinchCompression01`/`PinchAxis` controller에서 pull(기존 패턴). **intensity 없음** — 두께비 = 실제 gap.
- 축방향 scale = `max(pinchMinThickness, 1 − comp01)`, 수직 scale = `1/sqrt(along)`(면적보존 bulge). 압착축을 body-local로 분해해 anisotropic localScale로 합성. 슬라임이 gap 중앙에 held라 plate에 conform(파고들지 않음). SmoothDamp(`pinchSquashSmoothTime`)로 부드럽게. 노브: `SlimeVisualSettings.pinchMinThickness(0.15)/pinchSquashSmoothTime(0.05)`.
- ※ flat-face conform(stadium 실루엣)은 shader 작업 필요 — 현재는 gap-driven 타원 squash(ShaderGraph+Quibli 변종 2개라 shader 수술 회피). 필요하면 후속.

## 배치 체크리스트
- **공통/F1**: Rigidbody2D(Kinematic) + BoxCollider2D + `WaypointPlatformV2`, waypoint 2개. 탑승 → 흔들림+sag+댐핑 이동→도착, `holdRange` 밖으로 점프 → 복귀+재무장.
- **틸트/얼음**: `iceMaterial`=Ice.asset, `enableTilt`, `idleTipBias>0`. 가만히 있는 슬라임 → 플랫폼 기울어 미끄러져 떨어짐 → 재점프 강제.
- **샌드위치**: v2 2개, leader.`partner`=상대, `isLeader` 체크, waypoints 반대(위/아래). leader 탑승 → 둘이 수렴 → 두 데크가 슬라임 쪽으로 벌어지며(scissor) 슬라임이 코어 pinch-resolver로 열린 옆으로 압출(wedge·관통 없이). `riderLayers`는 슬라임 레이어로. `SurfaceProbeModeOverride(Multi)`는 자동 부착됨.

## 기존 `WaypointPlatform` 과 차이
| | v1 (`WaypointPlatform`) | v2 (`WaypointPlatformV2`) |
|---|---|---|
| 이동 | 연속 loop/ping-pong, rigid lerp | 탑승 트리거 1회 왕복, SmoothDamp 댐핑 |
| 탑승 피드백 | 없음 | sag 흔들림 + 이탈 반동 |
| 틸트/얼음 | 없음(별도 `WaypointTiltPlatform`) | 내장 토글 + idle-tip |
| 도착 유지 | 없음 | 범위 내 유지 → 이탈 시 복귀 (ThinIce식) |
| 페어 | 없음 | leader/partner 샌드위치 + scissor 열림 + 코어 pinch escape 압출 |
