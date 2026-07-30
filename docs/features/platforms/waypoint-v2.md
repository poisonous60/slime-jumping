# WaypointPlatform v2 (`WaypointPlatformV2`)

`PlatformMotor` 상속. 사각형(BoxCollider2D) 이동 플랫폼. 기존 `WaypointPlatform`의 단순 lerp 루프 위에 **탑승 피드백 + 2가지 신규 거동**을 얹은 버전. 모든 연출은 `ComputeMotion` 의 단일 `(pos, rot)` 반환에 합쳐지므로 `PlatformMotor` 의 `DeltaPosition`/`DeltaRotation` → `PlatformCarrier` 라이더 캐리가 그대로 정확하게 동작. 샌드위치 슬라임 사출은 **코어 depenetration 수정**(아래 Pinch escape)이 담당, 플랫폼은 열림 연출만.

스크립트: `Assets/Scripts/Platforms/WaypointPlatformV2.cs`

## 라이더 감지 (신규 센서 없음)
`PlatformCarrier`(order -200)가 부착 중 매 FixedUpdate `ApplyRiderInfluence(pos,mass,vel,charging)`, 이탈 시 `RemoveRider()` 호출. v2는 `ApplyRiderInfluence`에서 `riderInfluencedThisStep` 플래그 + `riderPos/riderMass/riderVel` 캐시 → 같은 프레임 `ComputeMotion`(order -100)에서 소비 후 클리어. `prevRiderPresent` 로 **탑승 rising-edge** 산출.

## 연출 (매 FixedUpdate `ComputeMotion` 합성)
1. **Rigid path target** — `localWaypoints` 폴리라인을 `travelSpeed`로 진행, `easeCurve` per-leg 이징. Phase가 방향 결정. waypoint <2 개 또는 `mountTriggered` off → 정지(틸트/얼음 전용).
2. **Damped follow (UI 댐핑)** — `Vector2.SmoothDamp(smoothPos, rigidTarget, …, curSmoothTime)`. 탑승 시 smoothTime↑ → 묵직한 UI-스프링 지연감. smoothTime 은 `riderOnTop ? mountedSmoothTime : followSmoothTime` 로 **즉시 전환하지 않고 `smoothTimeBlendTime`(초) 동안 `MoveTowards` 블렌드**. SmoothDamp 는 등속 타깃을 `travelSpeed × smoothTime` 만큼 뒤에서 따라가므로, 값을 즉시 뒤집으면 그 위치 빚(3 m/s·(0.18−0.05) = 0.39u)을 한 settle 안에 몰아서 갚음 → 발사 순간 데크가 진행방향으로 확 튀고 착지 순간 멈칫. 수직 플랫폼에서 특히 조작감을 해쳤음. 블렌드로 완만하게 상환.
3. **Vertical sag 스프링** — under-damped. 목표 `riderPresent ? -sagDepth*riderMass : 0`. 탑승 → 살짝 가라앉으며 흔들림, 이탈 → overshoot 반동. rb를 실제로 움직이므로 슬라임이 출렁임을 같이 탐.
4. **Tilt 스프링** (`enableTilt`) — `TiltPlatform` 수식 재사용: 라이더 localX/halfWidth → targetTilt, 스프링-댐퍼 → currentTilt. **콜라이더가 기울어** 슬라임이 미끄러짐.
5. 반환 `(smoothPos + (0, sagY), baseRot + currentTilt)`.

## 얼음 → 계속 점프 강제
- `iceMaterial` 필드(= `Settings/SurfaceMaterials/Ice.asset`)를 `OverrideSurfaceMaterial`로 노출 → 부착 시 carrier가 자동 push, 낮은 `slideResistanceMultiplier`로 미끄러짐.
- 얼음 + 가장자리 틸트 = **플랫폼 중심이 불안정 평형**: 미세 drift → 틸트 → 얼음 슬라이드 → 틸트↑ → 떨어짐. 정중앙에서도 떨어지도록 `idleTipBias`(deg): 라이더 속도 < `idleSpeedThreshold` 일 때 라이더 쪽으로 틸트를 약간 seed (창발적, 꼼수 아님).

## Feature 1 — 탑승 트리거 + 범위 유지 (상태기계)
`Phase { Parked, Departing, Holding, Returning }`, `mountTriggered` 토글.
- **Parked**(target=wp0): 탑승 rising-edge(또는 `TriggerFromPartner()`) → Departing.
- **Departing**: wp[last]까지 진행, 도착(`pathParam≈last` && smoothPos 근접) → Holding.
- **Holding**(target=wp[last]): 탑승 rising-edge → 즉시 Returning + `partner.RequestReturn()` (도착지에 서 있는 데크를 다시 밟으면 왕복이 역방향으로 재시작 — 샌드위치 페어면 zone 밖으로 나갔다 올 필요 없이 **그 자리에서 재교차**). rising-edge 없으면 매 프레임 `Physics2D.OverlapCircle(wp[last], holdRange, riderLayers)`, 범위 내 슬라임 없으면 → Returning. (부착이 아닌 **근접** — 슬라임이 공중이어도 근처면 유지. `ThinIceRespawnZone` exit 패턴과 동일.)
- **Returning**: wp0 복귀 → Parked(재무장).

## Feature 2 — 샌드위치 페어 + Pinch (명시적 platform↔slime 커플링)
> 재설계 이유: 이전 3개 시도는 pinch를 **generic collision depenetration**(Phase A)에 의존 → off-center 슬라임의 대각선 normal 합이 1~2프레임에 옆으로 사출(시간/step 게이트 우회), kinematic 플랫폼이 dynamic 슬라임을 그냥 통과, localScale·intensity squash는 cheat(플랫폼 안으로 파고듦)라 다 실패. 해결: pinch를 **명시적 opt-in 상호작용**(bamboo-grab/rope 패턴)으로 — 플랫폼이 슬라임을 잡고 명시적으로 hold/extrude.

- 페어링: `partner`+`isLeader`. **트리거는 대칭** — 어느 쪽을 밟아도 그 데크가 Departing + `partner.TriggerFromPartner()`로 상대도 출발. `partner`는 한쪽에만 배선하면 Awake에서 반대 방향 링크를 자동 채움(`partner.partner = this`). 에코 재출발 방지: `TriggerFromPartner()`는 Parked 데크만 수락. `isLeader`는 이제 **pinch 솔브 담당/축 방향(−axis 쪽)** 만 결정 — 밟을 수 있는 데크를 뜻하지 않음. 반대 이동은 각 waypoints가 결정.
- **복귀 = 트리거 박스 zone (ThinIce RespawnZone 모델, 2026-06-16)**: 각 데크가 자기 dest 원으로 복귀 판정하면 partner dest(낮은 쪽)에 압출 슬라임이 머물러 partner만 Holding에 갇힘. 대신 별도 컴포넌트 **`WaypointReturnZone`**(`ThinIceRespawnZone`와 동일 구조: isTrigger BoxCollider2D + Kinematic Rigidbody2D)이 멤버 데크들을 소유. 슬라임이 박스 **enter/exit를 카운트**(`slimeOverlapCount`), **exit→0 시 `ReturnAllMembers()`**가 멤버 전원에 `RequestReturn()` 호출 → leader+partner 동시 복귀. 데크는 `RegisterParentZone`로 zone 등록되면 holdRange 자가복귀를 끄고 zone 명령만 따름(`parentZone != null` 게이트). **출발은 그대로 탑승 트리거**(zone은 복귀만 담당). 멤버십: zone 자식 자동수집 + `explicitMembers`(샌드위치는 leader/partner 둘 다 등록). zone 미배치 시 데크는 legacy holdRange 자가복귀(F1 기본).

> **현재 모델 (2026-06-16): FORCE + 콜라이더 축소 디커플 + 데크 reaction-stall + 셰이더 평면 clamp.**
> 이전 "rb.position 소유 / Press-Hold-Extrude / localScale squash" 모델은 폐기. 충돌은 끄지 않는다 —
> 진짜로 부딪히되 콜라이더를 gap에 맞게 줄여 솔버 fling을 0으로 만들고, 비주얼은 풀사이즈로 두고 셰이더가 gap에 평면 clamp.

### A. 플랫폼: catch → reaction-stall → tilt → report (`WaypointPlatformV2.UpdateSandwich`, leader만)
- leader가 두 plate face 사이 gap 계산(`NominalExtentAlong`, 틸트 무시). `gap < 2R` & 슬라임 axis 사이 & |lateral| ≤ halfSpan → **catch**.
- catch 시: `slime.SetPinch(...)` + 각 벽의 face normal `AddPinchContact` 매 프레임 보고.
- **reaction-stall(연속, 플래그 아님)**: `pinchReactionFactor = clamp01((gap − pinchSqueezeGapMin)/(2R − pinchSqueezeGapMin))` (1=자유 → 0=gapMin에서 정지). 이걸 partner에도 미러(`SetSandwichReaction`). `AdvanceRigidTarget`에서 **닫는 advance(`dParam`)를 rf로 스케일** → `rigidTarget` 자체가 gapMin에서 멈춤(SmoothDamp가 over-travel 타깃을 끝까지 쫓아 닫는 문제 해결). `ComputeMotion`은 추가로 `smoothPos`의 닫는 성분도 (1−rf)만큼 제거(미세 settle).
- **open tilt = reaction torque**: `(1−rf)·off·openTiltTorqueGain`으로 데크가 탈출 쪽으로 벌어짐(기존 spring 복원).
- 보고 끊김(슬라임이 옆으로 빠짐) → `caught=false` → rf=1 → 정상 이동 재개.

### B. 슬라임: force exit + 콜라이더 축소(비주얼 디커플) (`SlimeController.Pinch.cs` partial)
- `SetPinch(...)` 보고 → pinch 진입/갱신(안 오면 자동 release). 진입 시 한 번 **Airborne 강제**(applied velocity가 적분되는 유일 상태).
- gap-driven `PinchCompression01 = clamp01((2R−gap)/2R)`.
- **옆 배출 = 압축비례 속도**: `velocity = tangent · sign · min(pinchExtrudeSpeed·comp/(1−comp), pinchMaxExtrudeSpeed)` (순수 tangent, 축방향 0). 매 step `RegisterLaunchClearance` + `attachLockout`으로 재부착 차단.
- **무관통 = 콜라이더만 축소**: `SetPinchColliderScale(((gap−pinchSqueezeFitMargin)/2)/SlimeRadius)`. `pinchColliderScale`는 `bodyScaleRatio`와 **분리**(ApplyBodyScale에서 곱) → 콜라이더+probe만 줄고, `SlimeRadius`(플랫폼이 fullR 계산에 쓰는 논리 반경)·**비주얼**은 안 줄음. 데크가 gapMin에서 멈추므로 gap > 줄어든 콜라이더 지름 → 겹침 0 → 솔버 squirt 없음. floor = code const `MinPinchColliderScale(0.05)`.
- 노브: `SlimeSettings` → `pinchExtrudeSpeed(1.5)/pinchMaxExtrudeSpeed(4)/pinchSqueezeFitMargin(0.04)`; `WaypointPlatformV2.pinchSqueezeGapMin(0.12)/openTiltTorqueGain/openTiltMax`. (DEAD: `pinchSqueezeMinScale`, `pinchPress*`/`pinchHold*`.)

### C. 비주얼: 풀사이즈 유지 + 셰이더 평면 clamp + 부피보존 bulge (`SlimeVisual.BuildPinchDeform` → `SlimeDeform.hlsl` 3.5)
- 비주얼 스케일은 **1 유지**(BodyScaleRatio 미변경) — "작아짐"이 아니라 "납작". per-contact face normal + comp + **world half-gap**(`(1−comp)·R`) + **gap-center**(`_PinchCenterWS`)를 MPB로 push.
- 셰이더 3.5: **월드공간 평면 clamp** — 각 벽 face plane(**gap-center**에서 halfGap)을 넘는 버텍스를 plane 위로 투영 → gap에 정확히 맞는 진짜 평평한 면(plate 관통 없음). 메시의 -90°X·flatten 스케일에 안 휘게 world space. **기준점은 메시 원점 아님 → gap-center**(body3d가 visual offset으로 드리프트 → 메시 원점 쓰면 슬랩이 판에서 밀려 떠 보임). 그 뒤 지배 압착축 기준 **부피보존 측면 bulge**(`perp ×= 1/sqrt(1−comp)`, 한 번만 — 양면 contact 같은 축 공유).
- comp는 `pinchSquashSmoothTime` smooth → pop 없이 ease. 치수: `slimeRadius=0.2` → 2R=0.4, gap 0.12 = 30% 두께. (DEAD: `pinchDisplaceScale`/`pinchMinThickness` — 평면 clamp가 gap에 직접 맞춰 불필요.)

## 배치 체크리스트
- **공통/F1**: Rigidbody2D(Kinematic) + BoxCollider2D + `WaypointPlatformV2`, waypoint 2개. 탑승 → 흔들림+sag+댐핑 이동→도착, `holdRange` 밖으로 점프 → 복귀+재무장.
- **틸트/얼음**: `iceMaterial`=Ice.asset, `enableTilt`, `idleTipBias>0`. 가만히 있는 슬라임 → 플랫폼 기울어 미끄러져 떨어짐 → 재점프 강제.
- **샌드위치**: v2 2개, 한쪽 `partner`=상대(반대 링크 자동), 그 쪽에 `isLeader` 체크, waypoints 반대(위/아래). 아무 쪽 탑승 → 둘이 수렴 → 슬라임이 `pinchSqueezeGapMin`에서 데크를 멈추고(reaction-stall) 압축비례 속도로 열린 옆으로 압출, 데크는 탈출 쪽으로 벌어짐(reaction torque). 비주얼은 풀사이즈 그대로 gap에 평면 clamp(납작). `riderLayers`는 슬라임 레이어로. `SurfaceProbeModeOverride(Multi)`는 자동 부착됨.

## 기존 `WaypointPlatform` 과 차이
| | v1 (`WaypointPlatform`) | v2 (`WaypointPlatformV2`) |
|---|---|---|
| 이동 | 연속 loop/ping-pong, rigid lerp | 탑승 트리거 1회 왕복, SmoothDamp 댐핑 |
| 탑승 피드백 | 없음 | sag 흔들림 + 이탈 반동 |
| 틸트/얼음 | 없음(별도 `WaypointTiltPlatform`) | 내장 토글 + idle-tip |
| 도착 유지 | 없음 | 범위 내 유지 → 이탈 시 복귀 (ThinIce식) |
| 페어 | 없음 | leader/partner 샌드위치 + scissor 열림 + 코어 pinch escape 압출 |
