# 잘린 슬라임 — 천장(오버행) 부착 시 떨림

**보고:** 2026-08-03 · **상태:** 원인 미확정, 재현 절차 확보 · **씬:** MainScene / stage3_floor2_left

---

## 1. 증상 (유저 원문)

> saw 에 waypoint platform 붙여서 왔다갔다 해서 장애물을 만들었는데, 가끔씩 슬라임이 점프할 때 saw 에
> 닿아서 **윗면이 잘리고 천장에 붙는** 상황이 생긴다. 이때 **떨림이 생긴다.**
> **아랫면이 잘리고 바닥에 붙을 때는 떨림이 안 생긴다.**

- 떨림이 봉합(heal) 중에만 나는지 붙어 있는 내내 나는지 **미확인**
- 안 잘린 상태로 같은 면에 붙어도 떨리는지 **미확인**
- 떨림이 rb 위치인지 비주얼만인지 **미확인**

## 2. 왜 천장에 매달리나 (유저 질문)

버그 아니고 설계값이다.

| 값 | 위치 | 현재 |
|---|---|---|
| `ceilingAngle` (부착 유지 한계) | `Assets/Settings/SlimeSettings.asset` | **150°** |
| `ceilingReattachAngle` (착지 부착 한계) | 동일 | **140°** |

법선이 중력 up 기준 **140° 이내**면 착지해서 붙고, **150°를 넘어야** 떨어진다. 즉 기울어진
오버행은 전부 붙는다. 완전 수평 천장(180°)은 안 붙는다 — `BambooV3_Surface.asset`(180°)만 예외.

씬 실측 — 톱 3개(`Saw_CW (4)/(5)/(6)`, `stage3_floor2_left`) 반경 8 유닛 안의 **부착 가능한 오버행 면**:

| 콜라이더 | 중점 | 길이 | 법선 | up 기준 각 |
|---|---|---|---|---|
| `TerrainShape_TerrainMaker (6) 16_Collider2D (19)` | (23.63, 315.05) | 1.00 | (0.888, −0.460) | **117°** |
| `TerrainShape_TerrainMaker (9) 8_Collider2D` | (23.80, 311.64) | 0.99 | (0.986, −0.165) | 100° |
| `TerrainShape_TerrainMaker (10) 8_Collider2D` | (25.04, 317.80) | 1.45 | (1.000, −0.017) | 91° |
| `TerrainShape_TerrainMaker (10) 7_Collider2D` | (21.47, 307.67) | 2.64 | (−1.000, −0.014) | 91° |

**즉 "천장"이 아니라 91~117° 오버행이다.** 이게 중요한 이유: `GetSurfaceAngle` 이
`acos(|n·up|)` 이라 **117° 오버행을 63° 경사면으로 읽는다** → `ComputeSlideSpeed` 가 실제로
슬라이드를 돌리고 `CheckFrictionDetach` 도 살아 있다. 바닥 케이스(0° 평지, 슬라이드 0)와
**입력 조건 자체가 다르다.**

## 3. 재현 절차

톱에 정확히 스치는 걸 노릴 필요 없다. 컷 방향은 진입 속도로 정해지므로 메뉴로 강제할 수 있다.

`SlimeSplitSequence.Begin` 은 `outDir = perp(entryVelocity)`. 정지 상태면 `entry = right*8` →
**상처 법선 = up = 윗면 컷**.

| 케이스 | 절차 |
|---|---|
| **버그 (윗면 컷 + 오버행)** | Play → 슬라임을 (23.63, 315.05) 오버행 밑면에 붙임 → 거의 정지 상태에서 `SlimeJumping/Effects/Trigger Split Now (Play Mode)` |
| **정상 대조 (아랫면 컷 + 바닥)** | Play → 평지에 붙임 → `SlimeJumping/Effects/Trigger Self Split Now (Play Mode)` (= `BeginSelfSplit(down)` → 아랫면 컷) |
| **컷 무관 대조** | 안 잘린 채 같은 오버행에 붙여 두고 떨리는지 확인 |

## 4. 실측 설정값

| 항목 | 값 | 비고 |
|---|---|---|
| `slimeRadius` | 0.2 | |
| `contactSkin` | 0.02 | **R의 10%** — Phase B 데드밴드 폭 |
| `depenetrationSkin` | 0.01 | 브로드페이즈 패딩만 |
| `cutColliderCount` | 6 | 로브 개수 |
| `cutColliderArcSamples` | 64 | **로브 방향 양자화 = 5.625°** |
| `surfaceProbeSmoothSmallLedges` | **True** | 렛지 완화 경로 켜져 있음 |
| `smallLedgeStepHeight` | 0.15 | `anchorTolerance` 를 이만큼 늘림 |
| `effectiveNormalSoftBandMultiplier` | 5 | 소프트밴드 = 0.10 |
| `surfaceNormalSmoothRate` | 40 | |
| `attachedCheckMultiplier` | 4 | detectRange = 0.2 |

## 5. 코드 분석 — 확인된 것

**정착 수학은 위/아래 대칭이다.** 잘린 몸의 접지는 로브 원 union 으로 계산되고, 아래 경로 전부
방향 무관이다:

- [SurfaceProbe.cs:108-133](../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceProbe.cs#L108-L133) `MeasuredDistanceToSegment/Collider` = `min(로브별 gap) + EffRadius`
- [SurfaceProbe.cs:556-575](../../SlimeJumping_v2/Assets/Scripts/Physics/SurfaceProbe.cs#L556-L575) Phase B — `gap > contactSkin` 일 때만 당김
- [MultiContactSurfaceProbe.cs:478](../../SlimeJumping_v2/Assets/Scripts/Physics/MultiContactSurfaceProbe.cs#L478) Phase A — `gap < 0` 일 때만 밀어냄

평형 밴드 `gap ∈ [0, contactSkin]` 은 바닥/천장이 동일. **따라서 컷 형상 자체에는 위아래 비대칭이
없다. 비대칭은 다른 데 있다.**

부착 중 중력은 위치에 안 실린다 (`rb.gravityScale = 0` @ [SlimeController.cs:352](../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs#L352),
Attached 는 teleport 모드로 `rb.linearVelocity = 0`). 즉 "천장이라 중력에 끌려 떨어진다"는 아니다.

## 6. 가설

### H1 — Attached↔Airborne 한 프레임 깜빡임 (유력)

접촉/법선이 한 프레임 흔들려 `FixedUpdateAttached` 의 detach 조건
([SlimeController.Attached.cs:14-16](../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Attached.cs#L14-L16))
을 스치면 Airborne 로 떨어졌다가 재부착한다.

**바닥에서는 그 한 프레임이 안 보이고**(이미 지면에 놓여 있음) **오버행에서는 낙하 + 재부착으로
보인다.** 이러면 코드에 위아래 비대칭이 없어도 증상은 비대칭으로 나온다.

같은 기전이 로프에서 이미 실증됨 — [SlimeController.Airborne.cs:50-53](../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Airborne.cs#L50-L53)
주석: *"detach → fall through → re-teleport every FU, visible as up-down oscillation."*

**판별:** 프로브 로그에서 `st=` 가 Attached/Airborne 사이를 오가는지. 프레임 번호 점프도 신호.

### H2 — 로브 배치 양자화 (유력)

[SlimeController.CutShape.cs:252-265](../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.CutShape.cs#L252-L265)
가 로브 방향을 64샘플(5.625°)로 반올림한다. 봉합 중 `b` 와 `R` 이 매 프레임 변하므로 선택 인덱스가
±1 튀고, 로브 중심이 **최대 |c|·0.098 ≈ 0.02 (R의 10%)** 점프한다 → `gap` 이 그만큼 점프 →
Phase A/B 가 `rb.position` 을 그만큼 옮긴다.

**단, 로브 0 만은 항상 `base0` 정확히 = 상처면에 접한다.** 즉:

- 아랫면 컷 + 바닥 → 접촉 로브 = 로브 0 → **안 튐** ✅ (유저 관찰과 일치)
- 윗면 컷 + 오버행(117°) → 상처 법선(up)과 표면 법선(117°)이 **63° 어긋남** → 접촉 로브가
  양자화되는 로브 1~5 중 하나 → **튐**

**판별:** 프로브에 로브 중심/반지름을 찍어 프레임 간 점프 확인. 또는 `cutColliderArcSamples`
64 → 128 로 올려 떨림 진폭이 절반 되는지 (되면 H2 확정).

### H3 — 오버행 슬라이드 + friction-detach 채터

117° 오버행이 63° 경사로 읽혀 `slideVelocity` 가 실제로 돈다. 잘린 몸의 요동치는 법선이
`CheckFrictionDetach` 의 ω 항을 흔들면 detach/재부착 채터.

**판별:** `dTan` 이 0 이 아닌지 (슬라이드 중인지), `FrictionDetachDebug` 의 `centrifugalAdmissible`.

### H5 — 로브가 몸 반경을 벗어남 (원인 확정, 수정 적용)

세션 중 콘솔에서 발견: `[CutShape] lobe escapes EffRadius`. 컷 로브 하나가 몸 원 밖으로 나가 그 반지름이 그대로 `CircleCollider2D` 에 써진다 → 프로브의 `min(로브별 gap)` 이 헛값 → `rb.position` 점프.

원인·증명·수정: [cutshape-lobe-escape.md](cutshape-lobe-escape.md).

떨림과 인과가 있는지는 **미확인** — 수정 후에도 떨리면 이 가설은 탈락.

### H4 — 비주얼 전용

`SlimeVisual` 의 deform/스프링만 떨리고 `rb.position` 은 고요.

**판별:** 프로브 `[SplitVis]` 줄의 `rbY` 고정 + `bodyLocalY`/`scaleY` 진동.

## 7. 계측 상태

[SlimeSplitJitterProbe](../../SlimeJumping_v2/Assets/Scripts/Debug/SlimeSplitJitterProbe.cs) 가 이
버그류 전용으로 이미 있다 (order 3002, SnapToSurface 이후 샘플).

- **2026-08-03: Play 중인 인스턴스에서 `enabled = true` 로 켜둠.** Play 를 끄면 원복된다.
- 영구화하려면 Play 정지 후 Hierarchy `Slime` → `Slime Split Jitter Probe` 체크.
- `onlyWhenSplit = true` 라 잘린 동안에만 찍는다 (스팸 제한적).
- 지금 로그로 판별 가능: H1(`st=`), H3(`dTan`), H4(`[SplitVis]` rbY vs bodyLocalY)
- **H2 는 못 찍는다** — 로브 중심/반지름 로깅 추가 필요
  (`SlimeController.CutLobeCount/GetCutLobeCentre/GetCutLobeRadius` 는 이미 public).

## 8. 다음 액션

1. §3 절차로 재현 → 콘솔 로그 확보 → H1~H4 판별
2. H2 빠른 검증: `cutColliderArcSamples` 64 → 128, 떨림 진폭 반감 여부
3. 원인 확정 전 코드 수정 금지 (CLAUDE.md · 값으로 원인부터 지목)

## 9. 관련 문서

- `plans/handoffs/HANDOFF_slime-cut-float-and-bulge_2026-07-30.md` — 이전 세대(radial `Reach()`)
  기준이라 일부 stale. 현행은 로브 CircleCollider2D union.
- `docs/features/mechanisms/slime-split.md` — 기능 문서 (§3 폐기 목록: 제안 전 필독)
- `docs/faceted-terrain-jitter-attempts.md` — 컷 무관 별건 떨림
