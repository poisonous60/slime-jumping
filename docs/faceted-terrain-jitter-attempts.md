# Faceted terrain 떨림 — 카메라/비주얼 측면 시도 기록

**상태:** 2026-04-28 대부분 해결. SurfaceProbe 측 3-단 수정으로 spurious contact 제거 + Unity 물리 보정 우회. 자세한 내용은 문서 끝의 "해결 (2026-04-28)" 섹션 참조.

## 문제

저폴리곤(faceted) 지형 위에서 슬라임이 sliding 할 때 (특히 U-bottom Ice 커브) 화면에 미세한 떨림(1–3px)이 보임.

## 근본 원인 (분석)

faceted MeshCollider 위에서 슬라임이 움직일 때 떨림이 발생하는 채널은 최소 3개:

1. **`surfaceNormal` 점프** — 삼각형 경계마다 normal이 discrete하게 변함. `velocity = surfaceTangent * speed` 재투영 때문에 속도 방향이 매 프레임 흔들림.
2. **`isTouchingSurface` flicker** — multi-contact probe의 active-set churn (`aC=1↔3↔0`). attached/airborne dispatch가 매 프레임 토글 가능.
3. **`rb.position` snap** — Unity 물리의 sphere-on-poly contact resolution이 삼각형 가장자리에서 위치를 discretely 점프시킴. velocity 재계산을 거치지 않고 위치 자체가 튐.

(1)은 `surfaceNormalSmoothRate` 저역통과로 부분 완화 가능. (2)와 (3)은 normal smoothing으로 못 잡음.

## 이전에 시도한 물리/시뮬레이션 측면 (이번 세션 이전)

- `Fixed Timestep` 0.02 → 0.01666667 (50→60 Hz). 50/60Hz 비트 패턴 제거. `relAccelPx` 5–9 → 1–2px²/frame. 잔여 떨림 남음.
- `Rigidbody2D.interpolation = Extrapolate` (selective shield 포함). hop 응답성 저하 + U-curve 미해결. revert.
- `surfaceNormalSmoothRate` 20 ↔ 40 튜닝. 20이 40보다 나음 (rate가 클수록 필터 약해짐). 단조롭지 않은 trade-off — Mode A (raw facet jitter, 저속 dominant) ↔ Mode B (corner lag judder, 고속 dominant).

## 이번 세션에서 시도한 카메라/비주얼 측면

핵심 아이디어: **visual decoupling** — 물리는 정확히 두고 렌더링만 별도 Transform에서 low-pass된 위치로 함. AAA 표준 기법.

### 시도 1: `SlimeVisualFollower` (단순 low-pass + snap on burst)

- 새 컴포넌트 `SlimeVisualFollower`를 `SlimeVisual` child에 부착.
- LateUpdate (order -100)에서 `slime.transform.position` 읽고 30Hz 지수 저역통과로 `transform.position` 작성.
- `OnLaunch` / `OnAirDashStarted` 구독 → 발사 시 snap.
- Cinemachine `SlimeVCam`의 Follow target을 `Slime` 루트에서 `Slime/SlimeVisual`로 재연결.

**결과:** U-curve 떨림 사라짐 ✓. 그러나 hop 시 "밀렸다가 당겨지는 느낌" — 카메라가 같은 smoothed source를 따라가서 hop 동안 카메라가 lag → 월드가 비균일하게 뒤로 밀렸다가 catch-up. snapOnLaunch=true 였지만 snap target이 `slime.transform.position` (Unity Interpolate된 값, rb.position보다 lag) 이라 snap 후에도 시점이 한 frame 어긋남.

### 시도 2: state-aware passthrough (Attached/Charging만 smoothing)

- `slime.CurrentState` 검사. Airborne/Stunned에서는 `smoothedPos = src` (passthrough), Attached/Charging에서만 low-pass.

**결과:** hop은 깔끔해짐 ✓. 그러나 착지(Airborne→Attached) 순간 smoothing이 갑자기 engaged → 첫 프레임 lerp(prev=passthrough_value, src, 0.39) 결과 visual이 src 대비 0.61\*delta 만큼 lag. 사용자가 "몸이 앞으로 쏠린다"고 보고. state binary switch는 transition discontinuity가 본질적 문제.

### 시도 3: predict-correct filter

- `predicted = smoothedPos + slime.Velocity * dt`, `smoothedPos = lerp(predicted, src, k)`.
- Steady-state 속도 의존 lag을 0으로 cancel하는 1차 complementary filter. 이론상 hop·slide·landing 모두에서 이상적.

**결과:** hop 끊김 + U-curve 떨림 재발생. 원인: faceted surface 위 `slime.Velocity` 자체가 매 프레임 jittery (surfaceTangent 회전이 그대로 velocity에 transfer됨). predictor가 jittery velocity를 쓰면 prediction도 jittery → 필터가 무용지물. predict-correct는 **velocity가 smooth해야** 동작.

### 시도 4: 카메라 decoupling (body smooth, 카메라는 raw 추적)

- `SlimeVisualFollower` 단순 low-pass로 revert.
- `SlimeVCam` Follow target을 `Slime` 루트로 다시 변경 → 카메라는 rb.position 직접 추적 (Unity Interpolate). hop lag 없음.

**결과:** 그러나 SlimeVisual의 child인 `Face`는 smoothing되고, Slime 루트의 sibling이었던 `SlimeVisual3d`(=body3d 메쉬)는 smoothing 안 됨 → 얼굴과 몸이 분리되어 보이는 시각 버그 (스크린샷 첨부 이슈).

### 시도 5: `SlimeVisual3d`도 smoothing — 두 번째 follower 추가

- `SlimeVisual3d`에도 `SlimeVisualFollower` 부착.

**결과:** 문제 지속. 원인: [SlimeVisual.cs:311](../../../SlimeJumping_v2/Assets/Scripts/Visual/SlimeVisual.cs#L311) `body3d.localPosition = body3dRestPosition + bulkPosition`이 매 LateUpdate (default order 0)에 실행됨. 이 write가 SlimeVisual3d의 world position을 부모(slime root, 비-smoothing) 기준으로 재계산 → order -100에서 follower가 쓴 smoothed world position을 덮어씀.

### 시도 6: `SlimeVisual3d`를 `SlimeVisual` 아래로 reparent

- `SlimeVisual3d`를 `SlimeVisual`의 child로 이동 (worldPositionStays=true). `SlimeVisual3d`의 follower 제거. SlimeVisual 한 개의 follower로 face + body3d 모두 커버.

**결과:** 사용자가 "더 이상해진다"고 보고. 정확한 visual artifact 미확인 (사용자가 즉시 revert 요청). 가능한 원인: `body3dRestRotation`/`body3dRestScale` 등이 startup에 캡처되는데 reparent 후 부모의 transform이 바뀌어 deformation pipeline의 기대값과 어긋남. 또는 SlimeVisual의 squash/stretch가 child가 된 SlimeVisual3d에 누적 적용되어 시각이 왜곡.

## 최종 상태 (revert)

- `Slime.prefab` → HEAD 복원 (`SlimeVisualFollower` 제거, `SlimeVisual3d` 원래 위치로 복귀)
- `SlimeCameraTargetProxy.cs` → HEAD 복원 (`visualOverride` 필드 제거)
- `SlimeVisualFollower.cs` → 삭제
- `MainScene.unity` → 그대로. `SlimeVCam` Follow는 `Slime` 루트 (원래 상태). 사전 존재하던 Stage1/지형 관련 diff(3708줄)는 보존.
- `surfaceNormalSmoothRate = 40` 은 그대로 (변경 안 함). 사용자가 20이 더 낫다고 했으므로 필요시 `SlimeSettings.asset`에서 조정.

## 학습한 것

1. **단순 low-pass는 본질적으로 velocity-dependent lag을 만든다.** body와 camera가 같은 source를 follow하면 lag이 보이지 않지만 (둘 다 같이 lag), hop 시 월드가 비균일하게 lag → "밀림" 느낌.
2. **state-aware switching은 transition discontinuity가 근본적 한계.** 어느 쪽 방향으로 switch하든 한 프레임의 lerp 도약이 발생.
3. **predict-correct는 predictor input(velocity)이 smooth할 때만 유효.** faceted terrain에서는 velocity 자체가 떨려서 무용.
4. **`SlimeVisual.cs`가 `body3d.localPosition`을 매 프레임 write 한다.** 어떤 visual decoupling도 이 write를 우회하거나 통합해야 함. 단순히 SlimeVisual3d에 follower 추가는 안 됨.
5. **reparent는 deformation pipeline의 rest pose 캡처에 영향을 줄 수 있다.** SlimeVisual.cs는 startup에 `body3dRestPosition/Rotation/Scale`을 캡처. reparent로 부모가 바뀌면 이 rest 값들의 의미가 바뀜.

## 다음에 시도해 볼 만한 방향 (미시도)

가장 promising한 순서:

1. **콜라이더 메쉬 자체를 smooth하게** — 지형 생성 단계 (Stage1/Stage2 작업 중)에서 collider mesh만 별도로 subdivide + Laplacian smooth. visual mesh는 faceted 그대로. 떨림의 모든 channel(1·2·3)을 원천 제거. 런타임 비용 0. 시각-콜라이더 미세 어긋남이 가시적이지 않다면 가장 깨끗한 해법.
2. **Spline-based motion** — 지형 가장자리에 spline 생성, attached 상태에서 spline tangent를 권위 있는 surface direction으로 사용. (1)·(2)·(3) 해결되지만 코드량 큼.
3. **ProCamera2D 도입** — 카메라 측 smoothing은 ProCamera2D Core(Follow smoothness per axis)로 대체 가능. 단, body 자체의 떨림은 ProCamera2D가 안 잡으므로 주된 문제 해결은 안 됨. platformer의 다른 카메라 기능(dead zones, parallax, screen shake 등)이 필요해질 때 별개로 도입 고려.
4. **다시 visual decoupling을 시도하려면** — `SlimeVisual.cs`의 `body3d.localPosition` write를 어떻게 다룰지부터 설계. 옵션:
   - `body3d` reference를 `SlimeVisual3d` 자체가 아니라 그 child(예: `MeshBaseline`)로 변경. 그러면 follower가 SlimeVisual3d.position 쓰고, SlimeVisual.cs는 body3d.localPosition 쓰는데 둘이 다른 transform이라 충돌 안 함.
   - 또는 deformation pipeline 자체를 smoothed source 기준으로 재구성.

## 해결 (2026-04-28)

크롤로 평면을 미끄러질 때 떨림이 드러난 게 결정적 단서. 점프-only 이동에서는 안 보이던 게 ice 스테이지에서 평면 슬라이드 상황이 생기면서 표면화. 디버그로 정지 상태에서 표면 contact 각도를 보니 평면 위에 있는데도 `[0]~ 39° [1]° 0.3° [2]~ 37°` 같은 값이 잡혀 있었음 — **떨림은 contact 자체가 잘못 잡히던 것의 부산물**이었지, 단순 곡면 normal jump 가 아니었음.

세 가지 원인이 직렬로 작용했고, 셋 다 [MultiContactSurfaceProbe.cs](../SlimeJumping_v2/Assets/Scripts/Physics/MultiContactSurfaceProbe.cs) / [SlimeController.cs](../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs) 에서 수정함.

### 원인 1 — Ring-sample probe artifacts

레거시 `MultiContactSurfaceProbe.PopulateContacts` 는 슬라임 주변 8 개 ring probe 각각에서 `col.ClosestPoint(probe)` 를 한 번씩 호출해서 contact 를 모았음. 두 가지 구조적 결함:

- **Cap at 3-4 contacts**: `ClosestPoint` 는 콜라이더 전체에 대한 *single global minimum* 을 반환. 8 probe 가 사실상 여러 facet 을 보지 못하고 silhouette 의 동일 지점으로 수렴. 곡면에 5-8 개 facet 이 닿아 있어도 distinct contact 는 ~3-4 개로 한정.
- **Spurious "fake normal" contacts**: probe 가 polygon **내부** 에 들어가면 Unity 2D `ClosestPoint` 는 입력 점 자체를 반환. `toSlime = pos - probe = -ringDir × probeR` → "normal" 이 ring 방향의 음수가 되어, 실제 surface 와 무관한 가짜 contact 가 됨.

### 원인 2 — Clamped-to-vertex spurious contacts (edge enumeration 의 함정)

원인 1 을 고치려고 ring sampling 을 **direct polygon edge enumeration** 으로 교체. 각 collider 의 모든 edge segment 를 순회하면서 `ClosestPointOnSegment(slime, a, b)` 로 contact 를 추출. 그런데 이렇게만 바꿨더니 사용자가 평면 위에 정지해 있는데도 39° / 37° / 45° 같은 표면이 잡힘.

원인: `ClosestPointOnSegment` 는 segment 양 끝에서 `t` 를 [0, 1] 로 clamp 함. 슬라임이 블록 위에 있을 때, 블록의 **back / side / bottom edge** 들의 closest point 는 그 edge 의 **vertex (= 가까운 모서리)** 에 clamp 됨. `(slime − corner).normalized` 는 모서리에서 슬라임으로 가는 bisector 방향이고, 실제 그 edge 의 surface normal 과는 무관. 결과적으로 슬라임이 닿지도 않는 edge 들이 위 방향과 한참 어긋난 normal 로 contact 에 끼어들어 EffectiveNormal weighted sum 을 오염시킴.

**Fix**: contact normal 을 bisector 가 아니라 **edge 의 진짜 outward perpendicular** 로 바꾸고, **slime 이 그 outward 방향 측에 있을 때만** 채택.

- Path winding 을 한 번 계산 (shoelace signed area; +1 = CCW, -1 = CW).
- CCW polygon 은 edge direction 의 right perpendicular `(dy, -dx)` 가 outward, CW 는 left perpendicular `(-dy, dx)`. windingSign 으로 통합: `outward = (dy * w, -dx * w)`.
- Validity filter: `Dot(slime − closest, outward) > 0` 만 통과. interior contact (segment 내부 t∈(0,1)) 은 자동 통과; clamped-to-vertex contact 중 슬라임이 edge line 의 *반대편* 에 있는 경우(= 블록 뒷면/옆면 edge 의 가짜 contact) 는 모두 걸러짐.
- Convex 코너 (예: 90° corner) 는 양쪽 인접 edge 가 각자의 outward 로 contact 두 개 emit; EffectiveNormal weighted sum 이 자연스럽게 bisector 가 되므로 코너 contact 는 그대로 살아남음.

이 단계에서 사용자 디버그가 평면 위에서 정상 (top edge 만 잡힘) 으로 회복되고 떨림도 줄어듦.

### 원인 3 — Unity 물리 step 의 사후 보정

`SnapToSurface` 는 FixedUpdate 안에서 `rb.position` 을 표면에 anchor 하지만, 그 다음 Unity 의 물리 step 이 `rb.position += linearVelocity * dt` + 자체 contact resolver 를 돌림. 렌더가 sample 하는 위치는 **물리 step 이 끝난 뒤** 의 값이므로 우리 snap 은 **render 에 직접 보이지 않음**. faceted polygon 의 edge 경계에서 Unity 의 contact resolver 가 frame 마다 다른 보정을 줘서 jitter 가 생김 (이게 docs 위쪽의 "channel 3").

**Fix** ([SlimeController.cs:551-572](../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs#L551-L572)): Attached / Charging 에서 명시적 integration 으로 전환.

```csharp
if (currentState == SlimeState.Attached || currentState == SlimeState.Charging)
{
    rb.position += finalVel * dt;       // 우리가 직접 적분
    surfaceProbe.SnapToSurface(...);    // 적분 후 다시 anchor
    rb.linearVelocity = Vector2.zero;   // Unity 가 추가로 움직이지 않게
}
else
{
    rb.linearVelocity = finalVel;       // Airborne / Stunned 은 기존대로 Unity 적분
}
```

`rb.linearVelocity = 0` 으로 두면 Unity 물리 step 이 사실상 no-op (적분 0 + contact correction trigger 안 됨). render sample = 우리 SnapToSurface 결과. `rb.linearVelocity` 를 외부에서 읽는 곳은 grep 으로 확인해 보니 모두 debug-only 컴포넌트라 게임플레이 영향 없음. 외력 (wind, conveyor, JumpPad 등) 은 이미 컨트롤러의 `velocity` 필드로 누적되므로 Unity 의 linearVelocity 채널 의존성이 없음.

### 결과

- 평면 위 정지: contact 1 개, 0° (실제 표면만).
- 곡면 슬라이드 (U-bottom / Ice): 떨림 대폭 감소. 잔여 변동은 polygon vertex 통과 시 인접 facet 이 soft band 에 들어오고 나가는 *진짜* 변화 — `surfaceProbeDedupeDot` 0.97 → 0.93 으로 인접-facet 머지를 더 적극적으로 하면 더 줄일 수 있음.
- 90° 코너 (기존에도 안 떨리던 케이스): 회귀 없음.

### 영향 범위 / 회귀 가능성

- `SingleContactSurfaceProbe` 는 더 이상 사용 안 함 (legacy 보존). 이번 수정은 `MultiContactSurfaceProbe` 전용.
- `EdgeCollider2D` (open polyline) 는 winding 개념이 없으므로 기존 bisector normal 유지. 대부분의 맵이 PolygonCollider2D / CompositeCollider2D 를 쓰므로 영향 적음.
- 미러된 transform (음수 scale) 에서도 winding 이 뒤집히지 않도록 winding 계산은 world-space 변환 후 수행.
- Attached → Airborne 전환: state 가 바뀐 다음 frame 부터는 `rb.linearVelocity = finalVel` 가 다시 사용되므로 정상 발사 / 점프 모션 유지. 전환 frame 자체의 점프 시점은 ChangeState 가 호출되는 시점 기준이라 별도 이슈 없음.

### 미시도지만 더 깊이 가려면

문서 위쪽 "다음에 시도해 볼 만한 방향" 의 1, 2 번은 여전히 유효 — 콜라이더 메쉬 smooth / spline-based motion. 현재 수정으로 떨림이 "허용 범위" 까지 줄었다면 거기서 멈추고, 더 까다로운 곡면 (낮은 R, 높은 v) 에서 다시 떨림이 부각되면 검토.
