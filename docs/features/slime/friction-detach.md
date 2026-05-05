# 원심력/Horizon 기반 Detach 매커니즘

작성일: 2026-05-05
최종 갱신: 2026-05-05 (구현 완료 후 최신 상태 반영)

---

## 1. 목적

기존 attached → airborne 분기는 [SlimeController.Attached.cs:13-30](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Attached.cs#L13-L30) 의 표면 각도 (ceilingThreshold ≈ 107°) 만으로 결정됨. 결과:

- 90° 코너에 도달하면 슬라임이 vertex 를 중심으로 휘감기듯 wrap.
- Stage2 ice 의 inertial slide 가 의도한 ski-jump 효과를 못 만들고 코너 따라 휘어짐.

**해결**: 표면 각도가 아닌 **(원심력 임계) AND (진행 방향에 지면 없음)** 두 조건의 AND 게이트를 새로 추가. 기존 ceiling 분기는 그대로 유지 — friction detach 는 추가 사유로 작동.

핵심 사용자 의도:
- 빠른 속도 + 표면 끝에서 진행 방향이 허공으로 향하면 → 진행 방향 그대로 직선 비행 (ski-jump).
- 빠른 속도 + 곡면 위에서도 다음 진행 방향에 지면이 있으면 → 계속 wrap.
- 느린 속도 → 어떤 표면이든 wrap.
- 모든 표면 동일 매커니즘 (Stage1/Stage2 통일).

---

## 2. 게이트 구조

매 `FixedUpdateAttached` 에서 [SlimeController.Attached.cs:174](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Attached.cs#L174) 의 `ProjectVelocityAgainstContacts` 직후에 검사. 모든 게이트 통과 시 `ForceAirborneWithVelocity(velocity)` 호출.

```
detach
  ⇔  toggleOn
    ∧  NOT transientFrame
    ∧  centrifugalAdmissible        (formula 의존, 2.2 절)
    ∧  horizonClear                 (geometric raycast, 2.3 절)
```

### 2.1 Transient guard (검사 skip 조건)

다음 한 frame 은 검사 자체 skip:

| 조건 | 의미 |
|---|---|
| `!arcHasPrevTangent` | 첫 attach frame (이전 tangent 없음) |
| `surfaceCollider != prevSurfaceCollider` | 다른 collider 로 막 진입 |
| `stickTimer > 0f` | 착지 stick decel 중 |
| `tangentSpeedAbs < cornerDetachMinSpeed` | 정지 상태 (false-positive 차단) |

`tangentSignedSpeed = Vector2.Dot(velocity, surfaceTangent)` — post-clip velocity 의 tangent 성분. velocity 가 `ProjectVelocityAgainstContacts` 후라 wedge 에서 magnitude 가 줄어 minSpeed 가드에 자연 보수적.

### 2.2 Centrifugal admissibility (formula 4종)

`SlimeSettings.frictionDetachFormula` enum 으로 식 선택. ω = `|Vector2.SignedAngle(arcPrevTangent, surfaceTangent)| / dt` (rad/s).

| Formula | 식 | 의미 | 의존 파라미터 |
|---|---|---|---|
| `VelocityTimesOmega` (default) | `\|v\| · ω > available` | v² / R 전통식. 빠를수록 작은 곡률에도 detach. | `cornerAdhesionAccel` |
| `ConstantTimesOmega` | `cornerVelocityThreshold · ω > available` | 속도 무관, 표면각만 영향. | `cornerVelocityThreshold`, `cornerAdhesionAccel` |
| `VelocityTimesOmegaWithMinAngle` | `\|Δθ\| > cornerMinAngleDeg AND \|v\|·ω > available` | v² 식 + 저각 게이트. 거의 직선 표면에선 trip 안 함. | `cornerMinAngleDeg`, `cornerAdhesionAccel` |
| `AngularRateThreshold` | `ω > cornerOmegaThreshold` | 순수 각속도 임계. v 무관, available 무관. | `cornerOmegaThreshold` |

`available = |Vector2.Dot(GravityVector, surfaceNormal)| + cornerAdhesionAccel`. `Abs` 사용 이유는 overhang (Dot < 0) 에서 floor 와 같은 임계 유지 (사용자 의도: "overhang 더 쉬워지지 않게").

### 2.3 Horizon raycast (geometric admissibility)

원심력만으로는 곡률만 보고 "지면이 있는지" 모름. 두 번째 게이트:

```csharp
Vector2 velDir = velocity.normalized;
float lookahead = max(cornerDetachHorizonMinDistance,
                      tangentSpeedAbs · cornerDetachHorizonTime);
Vector2 projectedCenter = rb.position + velDir · lookahead;
float probeDist = slimeRadius + cornerDetachHorizonProbeSkin;
RaycastHit2D hit = Physics2D.Raycast(projectedCenter, -surfaceNormal, probeDist, groundLayer);
horizonClear = hit.collider == null;
```

해석: "현재 velocity 방향으로 짧게 projection 한 위치에서 발 아래에 지면이 있는가?" 없으면 detach (허공으로 비행), 있으면 wrap (그냥 표면 따라감).

`ignoredColliders` 에 등록된 collider 는 hit 무시 (post-launch 잔재 차단).

기존에 시도된 "slime 외곽에서 velocity 방향 수평 raycast" 는 평지에서도 false detach (ray 가 슬라임 중심 높이라 지면을 못 맞힘) 발생 → 발 아래 ray 로 변경.

---

## 3. Detach velocity

```csharp
ForceAirborneWithVelocity(velocity);   // post-clip velocity 그대로
```

ProjectVelocityAgainstContacts 직후라 surface tangent 에 align 된 상태. 사용자 결정: "슬라임이 이동하는 방향 그대로 launch."

이전에 bigSpike (대각도 변화) 에서 `arcPrevTangent.normalized * tangentSignedSpeed` 로 분기하던 것은 horizon-based 도입 후 단순화 — convex/bigSpike/curveSustained 는 detach 게이트에서 빠지고 visual ratio 입력으로만 남음.

---

## 4. Reset 경로

friction state (`sameSignFrames`, `cornerStressRatio`, `smoothedCornerStressRatio`) 두 곳에서 reset:

- [SlimeController.cs:1108-1119](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs#L1108-L1119) `ChangeState` — Attached 또는 Charging 진입 시
- [SlimeController.Launch.cs:21-24](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Launch.cs#L21-L24) `FinalizeAirborneExit` — Airborne 진입 시

Charging reset 이 빠지면 Attached → Charging 진입 시 마지막 Attached frame 의 ratio 가 동결되어 charge 모션 내내 visual 에 stale 표시 (LP filter 가 Attached 외부에서 update 되지 않으므로 자연 감쇠도 안 함).

---

## 5. Visual

세 단계.

### Phase 1 — banana-bend (떨어질듯 말듯)

[SlimeDeform.hlsl](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeDeform.hlsl) 의 단계 6 (vertex displacement). Leading hemisphere (front in tangent direction) 가 `tangentDirWS` + `surfaceNormalWS` 방향으로 push, trailing hemisphere 는 그대로 → 진짜 banana-bend.

`SlimeVisual.cs` 가 controller 의 `CornerStressRatio` 를 매 frame shader uniform 으로 push:

```csharp
stressAmount = settings.cornerStressCurve.Evaluate(InverseLerp(min, 1, ratio));
```

ratio 는 controller 단에서 LP-filtered (`cornerStressRatioSmoothRate` 기본 25/s) + sameSignFrames ≥ 1 게이트 → 한 frame ω spike 만으로는 visual 발현 X.

### Phase 2 — 자동 복귀

ratio 가 다시 떨어지면 SmoothDamp 가 본 형태로 자연 ease. 별도 코드 없음.

### Phase 3 — pong (release 순간)

`SlimeController.OnFrictionDetach` event 구독. detach 시 fast-layer scale 에 damped cosine overlay (`pongDuration` 기본 0.18s, `pongOscFreq` 1.2 cycle).

```csharp
curve = Exp(-pongDecay · t) · Cos(pongOscFreq · π · t)
amount = curve · pongIntensity · pongStretchMax
finalScale += new Vector2(-amount · 0.4, amount)   // tangent pinch + outward bulge
```

`pongIntensity = Clamp01(overshoot / pongFullOvershoot)` — overshoot = ratio - 1.

---

## 6. Inspector 시각화 (분석 도구)

### 6.1 Heatmap — `SlimeSettingsFrictionDetachEditor`

[Editor 스크립트](../../../SlimeJumping_v2/Assets/Scripts/Editor/SlimeSettingsFrictionDetachEditor.cs) 가 `SlimeSettings` 의 default inspector 끝에 v × R 평면 detach 영역을 320×200 텍스처로 표시.

축: X = v ∈ [0, 25 m/s], Y = R ∈ [0.1, 5 m]. 픽셀 분류:

- **초록**: 평지 (`available = |g| + adhesion`) AND 벽 (`available = adhesion`) 모두 wrap
- **빨강**: 두 표면 모두 detach
- **주황**: 평지 wrap, 벽 detach (= 표면각에 따라 임계 변동) — 표면각 의존 식의 시그니처
- **회색**: `v < cornerDetachMinSpeed` 컷오프

`AngularRateThreshold` 는 표면각 무관이라 주황 띠 없음. 다른 모드는 주황 띠 있음.

각 모드/파라미터 변경 시 즉시 다시 그림 (캐시 키로 변동 감지).

### 6.2 임계 속도 표

각 R 값 (0.3 / 0.5 / 1 / 2 / 3 / 5 m) 에서 평지·벽 임계 속도를 numerical sweep 으로 계산. mode 별 자동 분기. 임계 안 잡히면 `∞` 표시.

### 6.3 Runtime debug — `FrictionDetachDebugUI`

[Debug 컴포넌트](../../../SlimeJumping_v2/Assets/Scripts/Debug/FrictionDetachDebugUI.cs) 가 `SlimeController.FrictionDebug` snapshot 을 IMGUI 로 매 frame 표시. 각 검사 변수 (tangentSpeed, dTheta, ω, required, available, ratio, convex, sameSignFrames, transient guard 사유) 가 색 상자로 보임. detach 발동 시 banner pulse + Console 로그 (체크박스 `logDetachEvents` 로 on/off).

---

## 7. 파라미터 가이드

`SlimeSettings` ScriptableObject:

| 필드 | 기본 | 의미 |
|---|---|---|
| `useFrictionBasedDetachment` | true | 마스터 토글. false 시 기존 ceiling 분기만 동작. |
| `frictionDetachFormula` | VelocityTimesOmega | 식 선택 (2.2 표 참고). |
| `cornerAdhesionAccel` | 60 m/s² | 점착 한계. available = gravityHelp + 이 값. |
| `cornerVelocityThreshold` | 8 m/s | ConstantTimesOmega 의 가상 속도. |
| `cornerMinAngleDeg` | 5° | WithMinAngle 의 저각 게이트. |
| `cornerOmegaThreshold` | 5 rad/s | AngularRateThreshold 의 ω 임계. |
| `cornerDetachMinSpeed` | 1.5 m/s | 정지 false-positive 차단. |
| `cornerDetachSustainFrames` | 2 | (visual ratio 용) curve sustain frame 수. |
| `cornerBigSpikeAngle` | 0.4 rad | (visual ratio 용) one-frame spike threshold. |
| `cornerStressRatioSmoothRate` | 25/s | visual ratio LP filter cutoff. |
| `cornerDetachHorizonTime` | 0.08 s | horizon lookahead = max(min, \|v\|·이 값). |
| `cornerDetachHorizonMinDistance` | 0.15 m | horizon 최소 거리. |
| `cornerDetachHorizonProbeSkin` | 0.25 m | 발 아래 raycast 깊이 = slimeRadius + 이 값. |

`SlimeVisualSettings` (Phase 1/3 visual):

| 필드 | 기본 |
|---|---|
| `cornerStressMinRatio` | 0.5 |
| `cornerStressCurve` | EaseInOut(0,0,1,1) |
| `cornerStressTangentMax` | 0.35 |
| `cornerStressOutwardMax` | 0.20 |
| `pongDuration` | 0.18 s |
| `pongDecay` | 4/s |
| `pongOscFreq` | 1.2 |
| `pongStretchMax` | 0.4 |
| `pongFullOvershoot` | 1.5 |

### 튜닝 시나리오

| 증상 | 손볼 파라미터 |
|---|---|
| 전반적으로 너무 쉽게 떨어짐 | `cornerAdhesionAccel` ↑ (V·ω 식) 또는 `cornerOmegaThreshold` ↑ (Angular 식) |
| 빠른 슬라임만 detach 시키고 싶음 | `cornerDetachMinSpeed` ↑ |
| 평지 sliding 중 false detach | `cornerDetachHorizonProbeSkin` ↑ (발 아래 깊이 여유) |
| 절벽 끝 detach 가 너무 늦음 | `cornerDetachHorizonTime` ↑ (lookahead 길이) |
| 작은 곡률에 빠른 slime 이 trip 됨 | formula 를 `WithMinAngle` 또는 `ConstantTimesOmega` 로 변경 |
| 표면각 무관하게 일정한 임계 원함 | formula 를 `AngularRateThreshold` 로 변경 |

---

## 8. 변경 파일

물리 / 컨트롤러:
- [`SlimeJumping_v2/Assets/Scripts/Settings/SlimeSettings.cs`](../../../SlimeJumping_v2/Assets/Scripts/Settings/SlimeSettings.cs) — 13 필드 + enum
- [`SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs`](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.cs) — 인스턴스 상태 3, public API (`CornerStressRatio`, `TangentSpeed`, `OnFrictionDetach`, `FrictionDebug`), reset hook
- [`SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Attached.cs`](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Attached.cs) — 검사 블록 (line ~175-310)
- [`SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Launch.cs`](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Launch.cs) — `FinalizeAirborneExit` reset 추가

비주얼 / 셰이더:
- [`SlimeJumping_v2/Assets/Scripts/Settings/SlimeVisualSettings.cs`](../../../SlimeJumping_v2/Assets/Scripts/Settings/SlimeVisualSettings.cs) — 9 필드
- [`SlimeJumping_v2/Assets/Scripts/Visual/SlimeVisual.cs`](../../../SlimeJumping_v2/Assets/Scripts/Visual/SlimeVisual.cs) — 이벤트 구독, pong overlay, shader push
- [`SlimeJumping_v2/Assets/Art/Shader/SlimeDeform.hlsl`](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeDeform.hlsl) — 단계 6 (banana-bend) + 입력 4
- [`SlimeJumping_v2/Assets/Art/Shader/SlimeBody3D.shader`](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeBody3D.shader) — Properties + CBUFFER + 3 Pass 호출 인자
- [`SlimeJumping_v2/Assets/Art/Shader/SlimeBody_Quibli.shader`](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeBody_Quibli.shader) + [`SlimeBody_QuibliDeformInject.hlsl`](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeBody_QuibliDeformInject.hlsl) — 동기화

분석 / 디버그:
- [`SlimeJumping_v2/Assets/Scripts/Editor/SlimeSettingsFrictionDetachEditor.cs`](../../../SlimeJumping_v2/Assets/Scripts/Editor/SlimeSettingsFrictionDetachEditor.cs) — heatmap + 임계 속도 표 + 범례
- [`SlimeJumping_v2/Assets/Scripts/Debug/FrictionDetachDebugUI.cs`](../../../SlimeJumping_v2/Assets/Scripts/Debug/FrictionDetachDebugUI.cs) — runtime IMGUI + 로그 토글

---

## 9. 향후 확장 후보

현재 추가 안 한 항목 (필요 시):

- **표면별 차별화**: `SurfaceMaterial` / `SurfaceModifiers` 에 `overrideCornerAdhesion`, `cornerAdhesionAccel` 필드. ice 만 약하게, grass 강하게 등. 사용자 결정으로 v1 에서 제외.
- **OnLaunch event 발화**: friction detach 가 audio/SFX 채널의 OnLaunch 도 발동시킬지. 현재 발동 안 함 (audit 권고). 별도 SFX 가 필요하면 `OnFrictionDetach` 신규 구독.
- **2D 셰이더 (`SlimeBody.shadergraph`) 동기화**: SpriteRenderer 가 step-6 deprecation 후 사라지므로 미적용. 비교 윈도가 길어지면 Shader Graph 의 Custom Function 노드에 입력 4개 추가 필요.
- **Charging 중 검사**: 현재는 Attached 한정. Charging 중 빠른 곡면 통과 시 detach 가 의미있는지 — 현재는 charge 가 끝나야 평가됨.
