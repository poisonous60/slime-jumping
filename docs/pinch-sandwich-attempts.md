# WaypointPlatformV2 샌드위치 압착 — 핸드오프

## 목표
두 `WaypointPlatformV2`가 위/아래로 다가와 슬라임을 압착 → 슬라임이 (1) 눈에 보이게 **납작하게** 눌리고 (2) 천천히 옆으로 밀려 빠져나가고 (3) 플랫폼은 슬라임에 걸려 멈췄다가 빠져나가면 재개.

**범위**: pinch는 게임에서 **사각 통로 1군데**뿐. 범용 conform·CPU 메시 변형 **안 함**(그 박스 케이스에 타깃 구현, 필요하면 하드코딩). 단 범위만 좁히고 **결과 퀄리티는 유지** — 보기 좋게.

## 현재 상태 (시도8, 2026-06-16 — 할 일 1·2·3 + 정렬 fix 구현, **유저 테스트 통과 ✓**)
컴파일 0 err/0 msg. 런타임 로그/probe로 데크 정지·정렬 검증. **치수 메모**: `slimeRadius=0.2`(에셋값) → **2R=0.4**. 따라서 gap 0.12 = **30% 두께**(이전 doc의 "2R=0.8/15%"는 오기).

### 1. ✓ 데크를 gapMin에서 멈춤 — **hard clamp** (`WaypointPlatformV2.ClampPinchGap`)
- **rf-스케일만으론 실패함**(런타임 로그 검증): gap이 `0.27→0.13→0.086→…→0.000`까지 닫힘. 이유: (a) `rigidTarget`이 lagging `smoothPos`보다 앞서 over-travel → smoothPos는 측정 gap이 gapMin 신호를 보낼 땐 이미 타깃이 gap~0, (b) 데크 도착 시 `Phase.Holding`이 `pathParam=N-1`(full close)로 pin → rf-스케일 우회. soft reaction은 이걸 못 이김.
- **해결 = 위치 hard clamp**: 각 데크가 자기 leading face를 공유 gap-center에서 `gapMin/2` 안으로 못 들어오게 clamp(`ClampPinchGap`, `smoothPos`+최종 `outPos` 둘 다). 두 데크가 같은 center 기준이라 결과 gap = 정확히 `pinchSqueezeGapMin`. SmoothDamp lag/phase 로직 무관한 보장된 최소.
- center는 leader가 두 face 중점으로 계산(`pinchCenterAxisProj`), `SetSandwichReaction`로 partner 미러. `pinchReactionFactor==1`(미포획) 또는 axis 미설정 시 inert.
- `smoothPos`를 clamp하면 wp[last]에 못 닿아 **Holding 전환 자체가 안 일어남** → full-close pin 우회 제거. rf-스케일(`AdvanceRigidTarget`)·soft reaction은 그대로 둠(부드러운 감속 + clamp 후 gap=gapMin이라 rf=0 → pathParam 자연 freeze, release 시 깔끔 재개).
- **노브**: `pinchSqueezeGapMin`(데크별, 양쪽 동일하게). 2R=0.4 기준 **0.12 = 30% 두께**(현재값, 제대로 된 팬케이크). 더 두껍게=올림, 더 납작=내림. 씬 인스턴스 `C_Leader_Bottom`+`C_Partner_Top`.

### 2. ✓ 옆 배출 솔버 squirt 제거 (콜라이더 축소를 gap에 맞게)
- 데크가 gapMin(0.12)에서 멈추므로 gap이 항상 0.12 위. 콜라이더를 `(gap−fitMargin)/2` 반경으로 줄이면 지름 = gap−margin < gap → **겹침 0** → 솔버가 던질 게 없음.
- 시도7 버그였던 `pinchSqueezeMinScale(0.4)` floor 제거 — 그 floor가 콜라이더를 지름 0.32로 묶어 0.12 gap에 겹쳐 squirt(moveTan~26) 유발했음. 이제 code const `MinPinchColliderScale(0.05)`만 degeneracy guard.
- 옆속도는 `pinchExtrudeSpeed` 압축비례 식이 유일 제어(겹침 0이라 솔버 개입 없음).

### 3. ✓ 비주얼 "작아짐 X → 납작 O" (콜라이더/비주얼 디커플 + 셰이더 평면 clamp)
- **디커플**: 새 `pinchColliderScale`(`SlimeController.Pinch.cs`) = 콜라이더+probe만 축소, `bodyScaleRatio`와 분리(`ApplyBodyScale`에서 곱). `BodyScaleRatio`(비주얼 구동)·`SlimeRadius`(플랫폼 fullR) 미변경 → **비주얼 풀사이즈 유지**.
- **셰이더 평면 clamp**(`SlimeDeform.hlsl` 3.5 재작성): 각 벽 face plane(중심에서 world half-gap=`(1−comp)·R`)을 넘는 버텍스를 **월드공간에서 plane 위로 투영** → gap에 정확히 맞는 진짜 평평한 면(plate 관통 없음). 메시 -90°X·flatten 스케일에 안 휘게 world space.
- **부피보존 측면 bulge**: 지배 압착축 기준 `perp ×= 1/sqrt(1−comp)` 한 번(양면 contact 같은 축 공유 → 중복 방지). 납작하면서 옆으로 넓게 퍼짐.
- C#에서 `_PinchHalfGap[]`(world half-gap) MPB push 추가. comp는 `pinchSquashSmoothTime` smooth라 catch/release pop 없음.

### 3b. ✓ 평면 clamp 정렬 fix — gap-center 기준 (clamp가 "떠 보임" 유발하던 버그)
- **증상**: 슬라임이 "공중에 떠 보임 / 너무 많이 깎임". probe로 확인: body3d 메시 원점이 gap-center에서 **−0.027 Y 드리프트**(velocity lean/sag/rest offset 파이프라인). 셰이더가 **메시 원점** 기준으로 clamp → 깎인 슬랩이 0.027 아래로 밀려 윗판 아래에 뜨고 아랫판은 관통.
- **해결**: clamp 기준점을 메시 원점(`TransformObjectToWorld(0)`) → **진짜 gap-center**(`SlimeController.PinchCenter`=플랫폼이 보고하는 plate 중점)로 교체. 버텍스는 절대 월드 plane(gap-center ± halfGap = 실제 양 판면)에 clamp → 슬라임이 gap을 꽉 채우고 양 판에 닿음. 메시 드리프트 무관.
- C# push `_PinchCenterWS`(world), 셰이더 `centerWS = _PinchCenterWS.xyz`.

## 유저 테스트 — 통과 ✓
- 데크가 gapMin(0.12)에서 정지(끝까지 안 닫힘). 옆 배출 부드러움. 슬라임 납작 + gap 꽉 채움 + 판에 정렬(안 뜸).
- 튜닝 노브: `pinchSqueezeGapMin`(납작 정도/데크 정지 gap, 2R=0.4), `pinchExtrudeSpeed`·`pinchMaxExtrudeSpeed`(배출 속도), `SlimeVisualSettings.volumePreservation`(측면 bulge 양, 현재 0.30), `pinchSqueezeFitMargin`(콜라이더 여유).
- **주의**: 현재 gapMin은 씬에 직렬화된 0.12. (세션 중 live 0.24 set은 play 재시작에 리셋됨 — 0.24는 2R=0.8 오기에 기반한 값이라 무시.)

## 함정 (재발 방지)
- 강체 원 + 실제 충돌 + 닫는 키네마틱 플레이트 = 솔버가 슬라임 사출. → 데크를 gapMin서 멈추고(#1) 콜라이더를 gap 아래로 줄여(#2) 겹침 0으로 죽임.
- `pinchColliderScale`은 **`SlimeRadius`에 안 들어감**(논리 반경 = 플랫폼 fullR 계산용). 콜라이더 실반경만 줄임. 이거 섞으면 catch 기하 깨짐.
- `Mat_SlimeBody3D_Quibli._VertexDisplaceScale = 0` → 일반 mesh deform OFF. pinch 3.5는 거기 안 의존(평면 clamp는 gap에 직접 맞춤). `pinchDisplaceScale`/`pinchMinThickness`는 이제 **DEAD**(평면 clamp가 대체).
- 셰이더 평면 clamp는 **world space**(`TransformObjectToWorld`/`TransformWorldToObject`)라야 메시 bookkeeping 변환에 안 휘어짐. OS push로 하면 non-uniform scale에 왜곡.
- 평면 clamp 기준점은 **메시 원점 아님 → gap-center**(`PinchCenter`). body3d는 visual offset 파이프라인으로 드리프트해서 메시 원점 쓰면 슬랩이 판에서 밀려 뜸. (3b 버그.)
- 데크 정지는 soft reaction/rf-scale **불충분**(rigidTarget over-travel + Holding pin). **위치 hard clamp(`ClampPinchGap`)**가 보장. rf-scale은 부드러운 감속 보조로만 둠.

## 코드 / 노브 / 디버그
- 코드: `SlimeController.Pinch.cs`(SetPinch/UpdatePinch/`SetPinchColliderScale`/ReleasePinch/`PinchCenter`), `SlimeController.BodyScale.cs`(`ApplyBodyScale` = bodyScaleRatio×pinchColliderScale), `WaypointPlatformV2.cs`(UpdateSandwich, `AdvanceRigidTarget` rf-stall, ComputeMotion reaction, **`ClampPinchGap`** hard gap floor, `SetSandwichReaction`+centerAxisProj), `SlimeVisual.cs`(BuildPinchDeform + `_PinchHalfGap`/`_PinchCenterWS` push), `SlimeDeform.hlsl` 3.5(gap-center clamp+bulge) + `SlimePinchData.hlsl`(`_PinchHalfGap`/`_PinchCenterWS`).
- 노브: `SlimeSettings{pinchExtrudeSpeed, pinchMaxExtrudeSpeed, pinchSqueezeFitMargin}`, `WaypointPlatformV2{pinchSqueezeGapMin, openTiltTorqueGain, openTiltMax}`, `SlimeVisualSettings{pinchSquashSmoothTime, volumePreservation}`. (DEAD: `pinchSqueezeMinScale`, `pinchDisplaceScale`, `pinchMinThickness`, `pinchPress*`/`pinchHold*`.)
- 디버그: `SlimeJumping.Debugging.PinchDebugger`(Slime에 부착, inspector `logging` 토글, `[Pinch]` 태그 로그). gap/comp/scale/velTan/moveTan/velAxis 확인.
- 테스트 씬: 메뉴 `SlimeJumping/Platform/Setup WaypointPlatformV2 Test` → C 클러스터.
