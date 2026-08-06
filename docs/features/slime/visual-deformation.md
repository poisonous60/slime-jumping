# 비주얼 시스템 (절차적 변형)

Phase 1.5 — 코드 기반 squash/stretch/관성 변형. 물리(FixedUpdate)와 완전 분리.

`SlimeVisual.cs` — `LateUpdate`에서 모든 비주얼 제어. body localScale/localPosition 조작, 물리에 영향 없음.

**2026-04 이후**: 본체는 3D 메쉬(Icosphere/Sphere) 기반. 2D 스프라이트 구성은 아카이브 보존(비활성).
**셰이더 스택 (2026-04-16)**: [SlimeBody_Quibli.shader](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeBody_Quibli.shader) = Quibli `StylizedLit` + [SlimeDeform.hlsl](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeDeform.hlsl) 5-스테이지 정점 변형. ForwardLit/ShadowCaster pass에 주입.

## Deformation Axis (body.up)
| 상태 | target_up | 비고 |
|------|-----------|------|
| Attached/Charging | `surfaceNormal` | Slerp 보간 |
| Airborne | `Vector2.up` (world up) | 축 분해 stretch가 world X/Y 기준이므로 world up 필수 |

`deformAxisSlerpSpeed`(15)로 보간.

**축은 방향이 아니라 직선**: 모든 squash 항이 `(a, -a)` = 로컬 Y 대칭이라 `+n`과 `-n`이 동일한 변형. targetAxis는 현재 `body.up`과 부호가 맞는 쪽을 고름 → 바닥↔천장 전환 시 slerp 회전량 0. 벽은 정직하게 90° 회전. 셰이더용 `lastSurfaceNormal`은 부호 보존(어느 쪽 면이 표면에 닿는지 구분해야 함).

**메쉬는 회전하지 않는다 (역회전 상쇄)**: `body`(비활성 2D 프레임)의 회전 `R`은 `body3d`에 그대로 실리지만, 자식 `MeshBaseline`이 `R⁻¹`를 들고 있어 합성 행렬이 `R·S·R⁻¹` — 대칭행렬 = 순수 방향성 스케일. squash는 여전히 표면 법선 방향으로 눌리는데(경사면 포함 정확), 메쉬 자체 방향은 월드 고정이라 천장 부착 시 몸이 180° 도는 모습이 안 보인다. 외부 애니(`SetExternalDeform`, box-sleep 등)는 회전을 의도적으로 보여주므로 baseline이 rest로 복귀.

## C-1. Face 추종
**Face 위치 오프셋 (회전 아님)**:
| 상태 | interest |
|------|---------|
| Charging | `-AimDirection * faceChargingScale` |
| Attached/Airborne | `Velocity * faceOffsetScale` |

- 정지 시 `faceLingerDecay`로 잔상 감쇠.
- 아이들 드리프트: `tanh(sin(t) * sharpness)` X축 사코드 패턴.
- 깜빡임: `eyes_closed.png` 스왑, 3~7초 랜덤 간격 0.1초.

**카메라 보정 (원근 시차 상쇄 + 시선)**: Face 는 몸 중심보다 월드 Z 로 0.4 앞에 있는데 슬라임 반지름은 0.2 — 지름의 2배다. perspective 카메라(FOV 40, z −12)에서 슬라임이 화면 중앙을 벗어나면 그만큼 시차가 생겨 얼굴이 몸에서 미끄러진다(중앙에서 4유닛 → 0.133 = 0.67R). 보정은 XY 만 건드린다:

```
parallax = -(P.xy - C.xy) * depth / (P.z - C.z)     // 얼굴을 카메라→몸중심 광선 위로
gaze     = clamp(-(P.xy - C.xy) / (P.z - C.z) * faceCameraGaze, faceCameraGazeMax)
```

- **깊이 불변** → 3D 메쉬 관통·소팅 무영향. **회전 없음** — 카메라에 Aim 이 없어 회전은 이미 정렬돼 있고, `SlimeVisual` lossyScale 이 (0.2, 0.2, 2) 비균등이라 기울이면 스프라이트가 10:1 로 전단된다.
- 화면 중앙이면 두 항 모두 0 → 평상시 화면은 보정 전과 동일. 전환 감지·게이트 불필요.
- `CinemachineBrain` 의 LateUpdate 순서가 이 스크립트와 미정의라 `CinemachineCore.CameraUpdatedEvent` 에서 한 번 더 적용한다 — 브레인이 먼저 돌았으면 no-op, 나중이면 하드컷 1프레임 팝을 막는다.
- 직교 전환(F5) 중에는 시차가 없으므로 투영행렬 `m33`(0=perspective, 1=ortho)을 그대로 페이드 가중치로 쓴다.
- 카메라는 `CinemachineBrain.OutputCamera` 우선, 없으면 `Camera.main`, 둘 다 없으면 1회 `LogError` (조용히 꺼지지 않게).

## C-2. 충격 squash
Airborne → Attached/Charging 전환 시:
```
strength = Clamp01(|Dot(lastNonZeroVelocity, surfaceNormal)| / impactSpeedReference)
overlay = e^(-decay * t) * cos(freq * PI * t)   // 감쇠 코사인 → squash + overshoot + settle
```
Fast layer — 날카로운 반응.

## C-4. 속도 stretch
- **표면**: `speed / velocityStretchSpeedRef` 비례. body.up=surfaceNormal → velocity는 X축 → `(+stretch, -stretch)`.
- **Airborne**: 축 분해. velocity를 중력 방향(Y)/수평 방향(X)으로 독립 분해. `velocityStretchCurvePower` 비선형, `velocityStretchCrossDamping` 교차 축 감쇠.
- **Velocity lean**: body center를 velocity 반대로 오프셋. "앞은 고정, 뒤가 끌려옴". Slow layer.

## C-5. 표면 squash + 중력 sag
- **법선 성분** (`Dot(gravity, normal)`): 표면 누르는 힘 → `(+squash, -squash)`. 평지=최대, 벽=0.
- **접선 성분** (`Dot(gravity, tangent)`): 흘러내리는 힘 → pivot offset (sag). 평지=0, 벽=최대.
- `smoothedSquashStrength`를 별도 SmoothDamp → scale과 shader asymmetry 동일 곡선.

## C-6. 호흡
`sin(breathPhase * 2PI) * breathAmplitude` 미세 진동. Fast layer. 항상 활성.

## C-7. 차징 비주얼
`ChargingLaunchSpeed`에서 `chargingCurve` (0~1) 계산.
- **Squash** (slow): `chargingSquashCurve.Evaluate(x)`. X = `chargingCurve + (activeChargeRadius/chargeRadius - 1)`. 커브 Y값이 그대로 squash (가산 아님).
- **Tremor** (fast): `chargingTremorThreshold` 이상 활성. `sin` 기반 고주파, `t²` 진폭. "위험한 에너지" 긴장감.
- **Offset** (slow): surface push + aim pull 슬링샷. aim pull은 chargingCurve 미적용 — 각도 즉시 반응.

## 비대칭 셰이더
- `_Asymmetry`: UV.y 리맵으로 아래쪽 압축 → 달걀 모양. `smoothedSquashStrength * surfaceSquashAsymmetry`. 평지=최대, 벽=대칭, Airborne=대칭.
- `_ChargeDip`: 차징 중 하단 인덴트. `chargingCurve * chargingDipMax`.
- `_DipCenterU`: dip 좌우 이동. `Dot(aim, tangent) * chargingDipLateralShift`.

## 변형 합성 공식
```
// Slow layer: SmoothDamp
targetScale = (1,1) + surfaceSquash(C-5) + velocityStretch(C-4) + chargingSquash(C-7)
smoothedScale = SmoothDamp(prev, targetScale, deformSmoothTime)

// Fast layer: 직접 합산
finalScale = smoothedScale + impactOverlay(C-2) + breathing(C-6) + chargingTremor(C-7)
finalScale = Clamp(0.4, 1.8)

// Offset
targetOffset = gravitySag(C-5) + velocityLean(C-4) + chargingOffset(C-7)
smoothedOffset = SmoothDamp(prev, targetOffset, offsetSmoothTime)

// Visual Clearance Clamp (코너 침범 방지)
// smoothedOffset/finalScale는 수정하지 않고 appliedOffset/appliedScale 로컬만 클램프
// sag 방향 레이캐스트(QueryClearance, groundLayer) → 인접 표면 clearance → ratio 기반 축소
// **trigger collider 스킵** — clamp 은 SOLID 표면 관통 방지용. one-way/통과 가능한
// trigger(TautLine 줄, hazard, water 존 등 — Environment layer 에 isTrigger)는 무시.
// 안 그러면 줄 근처서 raycast 가 줄 trigger 를 거리 0 에 맞아 ratio→0 → body offset 이
// 0 으로 collapse → rb.position 안 바뀌는데 visual 만 ~1m 점프(왔다갔다). queriesHitTriggers
// 가 True 라 layer-mask raycast 도 trigger 를 맞으므로 isTrigger 명시 스킵 필요.
appliedOffset = smoothedOffset * ratio
appliedScale.x = 1 + max(0, deltaX) * ratio

// Shader (MaterialPropertyBlock): _Asymmetry, _ChargeDip, _DipCenterU
```

## 공개 API (Controller 노출)
| 필드 | 설명 |
|------|------|
| `Velocity` | 현재 속도 |
| `SurfaceNormal` | 효과적 법선 |
| `CurrentState` | 상태 머신 |
| `AimDirection` | `-aimOffset.normalized`, fallback: surfaceNormal |
| `ChargingProgress` | 0~1 (`aimOffset.magnitude / activeChargeRadius`) |
| `ChargingLaunchSpeed` | 현재 aimOffset 기반 예상 발사 속도 |
| `ActiveChargeRadius`, `IsAtChargeFullAim`, `ChargeFullAimProgress` | 풀차지 유예 정보 |

## External Deform Gate
`IsExternallyAnimated` 플래그 + `SetExternalDeform(scale, asymmetry, chargeDip)` API. SitPose 등 외부 컨트롤러가 사용. MPB 경유로 SRP Batcher 호환.

**3D 메쉬 호환**: `ApplySitDepthShift/RestoreSitDepthShift`가 face 스프라이트와 sibling `body3d` 서브트리 Z 동시 이동. `SetExternalSurfaceNormal(Vector2.up)`으로 sit 중 셰이더 법선을 world-up 고정. `BodyNativeHeight`는 mesh `sharedMesh.bounds × rest lossyScale` 기반 rest-only 캐시.

## Charge Ring (디버그용, 기본 비활성)
- 차징 중 접촉점에 foreshortened 타원 링. `SlimeVisualSettings.chargeRingEnabled` 토글.
- `SlimeChargeRingVisual.cs` + `SlimeChargeRing_Unlit.shader` (Visible + OccludedSilhouette 2-pass).
- 방향 표현 안 함 — 호는 항상 표면 outward(+Y local)에서 CW로 채움.
- `chargeRingSurfaceTiltDegrees`(55°) 기울여 perspective 카메라가 foreshortened 타원으로 렌더.
- `chargeRingNormalFollowSpeed` Slerp로 각도 전환 보간.

## 관련
- [charge-launch.md](charge-launch.md)
- [../infra/trail.md](../infra/trail.md)
- 상세 사양: [슬라임_비주얼_진행현황_및_계획.md](../../슬라임_비주얼_진행현황_및_계획.md)
