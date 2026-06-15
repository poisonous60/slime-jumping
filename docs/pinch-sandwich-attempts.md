# WaypointPlatformV2 샌드위치 압착 — 핸드오프 (PAUSED 2026-06-16)

## 목표
두 `WaypointPlatformV2`가 위/아래로 다가와 슬라임을 압착 → 슬라임이 (1) 눈에 보이게 **납작하게** 눌리고 (2) 천천히 옆으로 밀려 빠져나가고 (3) 플랫폼은 슬라임에 걸려 멈췄다가 빠져나가면 재개.

**범위**: pinch는 게임에서 **사각 통로 1군데**뿐. 범용 conform·CPU 메시 변형 **안 함**(그 박스 케이스에 타깃 구현, 필요하면 하드코딩). 단 범위만 좁히고 **결과 퀄리티는 유지** — 보기 좋게.

## 현재 상태 (시도7, 로그 검증됨)
콜라이더 축소(`SetBodyScale`로 gap에 맞춤) + 슬라임 옆속도(압축비례) + 데크 반작용 감속.
- ✓ **통과(축 fling) 사라짐**: 콜라이더가 gap에 맞게 줄어(scale→0.4) 겹침 0 → `velAxis=0`.
- ✓ 슬라임이 옆으로 빠져나가고 release됨.
- ✗ **데크가 안 멈춤**: gap이 gapMin(0.12) 무시하고 **0까지 닫음**.
- ✗ **옆 배출 거침**: `moveTan~26` >> `velTan(4)` → gap~0서 솔버가 옆으로 squirt(내 속도 아님).
- ✗ **비주얼이 작아짐**(scale 0.4): "납작"이 아니라 "작아짐".

## 할 것 (재개 시, 순서대로)
1. **데크를 gapMin에서 실제로 멈춰라.** 현재 반작용은 `smoothPos`의 닫는 성분만 스케일 → `SmoothDamp`가 over-travel된 `rigidTarget`을 계속 쫓아 끝까지 닫음. → `rigidTarget`(=pathParam) 자체를 gap=gapMin 지점에서 멈춰야(advance 정지) 데크가 실제로 선다.
2. **옆 배출의 솔버 squirt 제거.** gap~0서 콜라이더(min scale 0.4 → 반경 0.08)가 겹쳐 솔버가 옆으로 던짐(moveTan 26). #1로 데크를 gapMin서 멈추면 gap이 콜라이더 지름 위에 유지돼 겹침 0 → squirt 사라짐. 그담 슬라임 옆속도는 `pinchExtrudeSpeed`로 제어.
3. **비주얼: 작아짐 X → 납작 O.** pinch 중 **비주얼은 full-size 유지**(`BodyScaleRatio`가 비주얼 축소를 구동하니 decouple — 콜라이더만 줄이고 비주얼 스케일은 1 유지). 변형: 윗/아랫면 **평면 clamp**(닿는 방향 하나뿐 → 셰이더로 충분) + 옆면 **부피보존 곡선 bulge**.

## 함정 (재발 방지)
- 강체 원 + 실제 충돌 + 닫는 키네마틱 플레이트 = 솔버가 슬라임 사출. (시도7서 축방향은 콜라이더 축소+velocity overwrite로 죽임. 옆 squirt는 위 #1·#2로 해결.)
- `Mat_SlimeBody3D_Quibli._VertexDisplaceScale = 0` → `SlimeDeform.hlsl` GPU 메시 변형 **전체 OFF**. 일반 squash는 `body.localScale`. pinch는 전용 `_PinchDisplaceScale`로 decouple(적용됨).
- `SetBodyScale`/`BodyScaleRatio`(`SlimeController.BodyScale.cs`) = 실제 콜라이더 반경 + 프로브 + **비주얼**까지 축소.
- 셰이더는 임의 콜라이더 모양 conform 불가(콜라이더 쿼리 못 함). 박스 = 평면 clamp로 충분.

## 코드 / 노브 / 디버그
- 코드: `SlimeController.Pinch.cs`(SetPinch/UpdatePinch/AddPinchContact/ReleasePinch), `WaypointPlatformV2.cs`(UpdateSandwich, ComputeMotion 반작용, AdvanceRigidTarget), `SlimeVisual.cs`(BuildPinchDeform + 메시 MPB push), `SlimeDeform.hlsl` section 3.5 + `SlimePinchData.hlsl`.
- 노브: `SlimeSettings{pinchExtrudeSpeed, pinchMaxExtrudeSpeed, pinchSqueezeMinScale, pinchSqueezeFitMargin}`, `WaypointPlatformV2{pinchSqueezeGapMin, openTiltTorqueGain, openTiltMax}`, `SlimeVisualSettings{pinchDisplaceScale, pinchSquashSmoothTime}`.
- 디버그: `SlimeJumping.Debugging.PinchDebugger`(Slime에 부착, inspector `logging` 토글, `[Pinch]` 태그 로그).
- 테스트 씬: 메뉴 `SlimeJumping/Platform/Setup WaypointPlatformV2 Test` → C 클러스터.
