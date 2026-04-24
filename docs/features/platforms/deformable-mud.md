# 변형 플랫폼 (Deformable / Mud / Clay Platform)

슬라임이 부딪칠 때마다 형태가 움푹 패이고, 반복 타격으로 결국 구멍이 뚫려 통과 가능해지는 소성(plastic) 지형. 낙하 시 원래 모양 복원. 2.5D 저폴리 스타일라이즈드 메쉬.

## 형상 모델 — 2-chain
폴리곤 outline이 아니라 **좌·우 두 체인**이 공유된 Y 사다리 위에서 각자 X를 가지는 구조.
- 각 level = `RestLevel(y, leftX, rightX)`. 런타임 상태 = `Level(y, leftRestX, rightRestX, leftCurX, rightCurX, isCap)`. 첫/마지막 level은 cap (변형 안 됨).
- 충격: 왼쪽 타격 → 해당 Y 근처 `leftCurX` 오른쪽으로 push. 오른쪽 대칭. `leftCurX ≥ rightCurX`되는 Y에서 material 소멸 → 슬라임 통과 가능 (pinch-through).
- 분해: 연속된 non-pinched level 구간을 piece로 묶어 각각 별개의 `PolygonCollider2D` path + mesh island 출력.
- 표현 가능: pillar, 원뿔, 호리병, 머쉬룸, stalactite, dome — **monotone-Y** 수직 strip.
- **표현 불가**: 같은 Y에 두 material 구간이 있는 모양 (π 문, 창문 뚫린 벽).

## 충격 → 변형 파이프라인
폴링 방식 (SlimeController 미수정). 매 FixedUpdate:

```
slime.SurfaceCollider == myPoly AND slime.CurrentState == Attached
└─ 직전 프레임 미부착 → 새 착지
   └─ ApplyImpactAt(slime.Position, slime.ImpactNormalSpeed)
      ├─ local 변환 → 가장 가까운 level index
      ├─ local.x vs center → left/right 판정
      └─ falloffYRange 내 level에 smoothstep 가중 push
         · push = impactToDisplacement × impactSpeed × w
         · min(push, maxPerHitDisplacement)
         · 반대쪽 rest wall에서 clamp (무한 push 방지)
```

`maxPerHitDisplacement`는 슬라임의 `SurfaceProbe` 감지 범위보다 작게 유지 — 한 프레임에 벽이 너무 멀리 움직이면 슬라임이 detach됨.

## 낙하 → 복원
슬라임의 local-Y가 플랫폼 최하단 level.y − `fallBelowMargin` 아래로 내려가면 `BeginReset()` → `resetDuration` 동안 모든 level의 `leftCurX`/`rightCurX`를 rest로 smoothstep lerp. Reset 중 재부착해도 변형은 계속 진행 (hit은 reset 위에 누적).

## EmitPiece 파이프라인
연속된 non-pinched level 구간을 piece로 emit:
```
Rebuild()
 ├─ 각 piece [start..end]:
 │   ├─ deformed outline (2*n 포인트: 좌체인 top→bot, 우체인 bot→top)
 │   ├─ rest outline (동일 구조, rest X)
 │   ├─ PolygonCollider2D.SetPath(pieceIdx, outline)
 │   ├─ Ring 0 = deformed (front, z=-d/2)
 │   ├─ Ring 1 = lerp(rest, deformed, rearDeformFactor) (back, z=+d/2)
 │   ├─ EmitFrontFaceGrid() — strip-grid + interior jitter + Perlin Z
 │   ├─ EmitBackFace()       — Ring 1 outline에 ear-clip 삼각분할
 │   └─ EmitSideStrip()       — outline edge마다 front↔back 단일 quad
 └─ mesh.SetVertices/Normals/UVs/Triangles (+32-bit index if >65k)
```
ear-clip은 piece가 항상 simple monotone-Y라 실패 케이스 없음. Side strip은 edge별 flat normal (vert 공유 안 함).

## Front face — strip-grid
[pogo_mud_generator_v1.py](../../../docs/tmp/pogo_mud_generator_v1.py)의 Delaunay 내부 포인트 + Perlin noise 컨셉을 chain 구조에 맞게 단순화:
- 각 level row × `(frontInteriorCols + 2)` column 격자.
- col 0 = 좌체인, col 마지막 = 우체인 (boundary, 절대 변위 없음 → side strip과 매끄럽게 접합).
- 상단·하단 cap row = 전체 boundary.
- interior 꼭짓점:
  - X/Y에 `frontInteriorJitter × 반 셀` 랜덤 offset.
  - Z에 `frontNoiseAmount × Perlin(x,y)` 만큼 카메라 방향(-Z)으로 돌출.
- per-tri flat normal → URP Lit 저폴리 페이싯.
- RNG seed는 `frontJitterSeed + piece 시작 level`로 고정 → 재빌드 시 jitter 패턴 안정.

## 재질
URP Lit + Cull Off (양면) + Smoothness 0.08 + BaseColor(0.62, 0.42, 0.28). 양면 처리는 procedural mesh winding과 Unity culling 규약 불일치 우회 (프로토타입 실용적 선택).

## 작성/편집 워크플로우
- **Hierarchy 우클릭 → `GameObject/SlimeJumping/Deformable Platform/*`** — Rectangle Short/Medium/Tall/Wide Slab, Hourglass, Taper, Dome Top 프리셋으로 즉시 생성.
- **Inspector `restLevels` 리스트** — 각 level의 y/leftX/rightX 직접 편집.
- **Scene view handle** — 노란 점(좌체인)/청록 점(우체인) 드래그. `Shift+클릭`으로 level 삽입, `Ctrl/Cmd+클릭`으로 삭제.
- **Inspector 하단 프리셋 버튼** — `genWidth`/`genHeight`/`genSegmentCount` 기반 재생성.
- **`OnValidate`** + `[ExecuteAlways]` — 에디터에서 파라미터 변경 시 즉시 mesh 재빌드.

## 파라미터
| 파라미터 | 기본값 | 설명 |
|---------|--------|------|
| impactToDisplacement | 0.04 | 충돌 속도(m/s) 당 밀리는 m |
| maxPerHitDisplacement | 0.12 | 한 hit 당 최대 거리 (detach 방지 상한) |
| falloffYRange | 0.8 | 주변 level 영향 Y 거리 |
| attachedCreepPerSec | 0 | 부착 중 초당 추가 (기본 0) |
| depth | 0.8 | Z 축 두께 |
| rearDeformFactor | 0.1 | 뒷면 변형 비율 (0=고정, 1=앞과 동일) |
| fallBelowMargin | 1.5 | 이 거리만큼 내려가면 reset 트리거 |
| resetDuration | 0.6 | reset lerp 시간 |
| frontInteriorCols | 6 | level row 당 내부 column |
| frontInteriorJitter | 0.4 | 내부 포인트 X/Y 랜덤 (0~1, 1=반 셀폭) |
| frontNoiseAmount | 0.15 | 내부 포인트 Z 돌출 |
| frontNoiseScale | 1.5 | Perlin 주파수 |

## 현재 한계 / 추후 작업
- **winding 미정리**: 단면 material로 돌리려면 front/back/side 각 face tri winding을 Unity CW-front로 재정렬 필요.
- **cap row 측면 변형 불가**: top/bottom cap은 `isCap=true`로 push 불가. 윗면 발자국/크레이터 표현 없음.
- **물리 크레이터 없음**: Blender 스크립트 `craters` (원형 local 오목) 미포팅.
- **이동 플랫폼 결합 테스트 없음**: `PlatformMotor` 조합 미검증.

## 관련 파일
- [DeformablePlatform.cs](../../../SlimeJumping_v2/Assets/Scripts/Platforms/DeformablePlatform.cs)
- [DeformablePlatformEditor.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/DeformablePlatformEditor.cs)
