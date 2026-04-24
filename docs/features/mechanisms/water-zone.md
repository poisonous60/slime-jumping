# WaterZone (물)

`EnvironmentalZone` 구현. 부력 + 드래그 + 낙하 캡 + attach 차단.

## 파라미터 (v1)
| 필드 | 기본 | 설명 |
|------|------|------|
| `entryMultiplier` | 0.4 | 진입 시 속도 곱 |
| `exitMultiplier` | 2.5 | 탈출 시 속도 곱 |
| `dragFactor` | 0.4 | 초당 속도 잔류율. `MultiplyVelocity(Pow(dragFactor, dt))` |
| `buoyancy` | 12 | 상향 가속도. `AddExternalVelocity(gravityUp * buoyancy * dt)` |
| `maxFallSpeed` | 4 | 하향 속도 캡 (`ClampFallSpeed`) |
| `blockAttach` | true | 물 안에서 표면 부착 차단 |

## blockAttach
`PushBlockAttach`/`PopBlockAttach` 카운터 기반.
- Airborne 착지 시 체크, Attached/Charging FixedUpdate에서도 체크.
- 차단 시 법선 속도만 상쇄하고 air control은 유지 (벽에서 A/D로 이탈 가능).

## WaterZone_v2 (동적 폴리곤 물)
공중부양 원형/다각형 물 연출. 물리 계약은 동일하되 외곽선이 파도치는 폐곡선.

**파일 분리**:
- `WaterZone_v2.cs` — physics/serialize
- `.Shape.cs` — outline/normals
- `.Simulation.cs` — 스프링-댐퍼 파도
- `.Mesh.cs` — Fan 메쉬 + vertex color
- `.Interaction.cs` — 슬라임 impulse

**Fan 메쉬**: 중심 + Catmull-Rom 서브디비전(기본 5)된 perimeter. `mesh.colors[0]` = 중심(deep), `[1..]` = 외곽(shallow+rim). Shader에서 `color.rgb` depth tint, `color.a` rim glow 강도.

**파도 물리**: 각 outline 포인트의 `displacement`/`velocity`가 Hooke 스프링 + 댐퍼 + 이웃 전파로 루프 전체 파동. 슬라임 진입 시 속도를 표면 법선으로 투영하여 가장 가까운 점에 impulse, `spreadNeighbors`만큼 falloff 전파.

가속도 식:
```
accel = -spread·(이웃차) - springK·변위 - damping·속도
```

**튜닝 규칙**:
- `spread` = 이웃 커플링 (Laplacian). 파동 전파 속도 `c ∝ √spread`. 크면 옆으로 잘 퍼짐.
- `springK` = 각 점을 rest로 끌어당기는 힘. 크면 각 점이 독립 스프링 → 루프 전체 동시 출렁임 (jelly).
- `damping` = 속도 감쇠. 파동이 절반 죽기 전 이동 거리 `x_half ≈ √spread / damping × 0.69`.
- `springK >> spread` → jelly (피할 것).
- `spread >> springK` + `damping` 충분히 작음 → traveling wave (목표).
- 예: `springK=0, spread=36, damping=0.1, spreadNeighbors=0, impulseFactor=40~50`.
- `spreadNeighbors=0`: 초기 킥을 1점에 집중해야 선명한 파면.

## Visual 셰이더
**v2**: `Assets/Art/Shader/Water_v2.shader` (URP Universal2D, HLSL)
- Depth tint: vertex color RGB 선형 보간 (중심→외곽 그라디언트).
- 스크롤링 noise: world-space XY UV에 2장 noise 반대 방향 스크롤, `_DistortionStrength`로 color warp.
- Sparkle: `step(_SparkleThreshold, nA*nB)` — 두 noise peak 겹침.
- Rim glow: `pow(saturate(color.a), _RimPower) * _RimColor`.

**v3 (2026-04-16)**: `Water_v3.shader` — v2를 `UniversalForward` + `Universal2D` 두 pass 제공 + `water_grid_sheet.png` 캐스틱스 오버레이 추가. Forward+ 렌더링 지원.

## Underwater 포스트 효과 (v1/v2 공통)
슬라임이 물 안에 있을 때 화면 전체 톤 변경.
- `Prebs/VP_Underwater.asset` (VolumeProfile): ColorAdjustments(cyan colorFilter + saturation -15), Vignette(cyan, intensity 0.35), ChromaticAberration(0.2), LensDistortion(-0.1).
- `UnderwaterVolumeBinder.cs`: `SlimeController.InFluid` 기반으로 Global Volume weight를 `MoveTowards(fadeSpeed=5)`로 lerp.
- 씬에 `UnderwaterVolume` GO(Global Volume + Binder)로 배치. WaterZone/v2가 `Push/PopInFluid` 호출하는 훅 재사용.

## 오디오 브로드캐스트 (v2)
`WaterZoneAudioManager` (씬에 하나). 인스펙터에 `hardEnter/softEnter/exit/underwaterLoop` 4개 클립 + `splashVolume/splashPitchJitter/underwaterLoopVolume` 3개 공용 레벨 지정.
- `Awake`(및 `ContextMenu > Apply To All Water Zones`)에서 `FindObjectsByType<WaterZone_v2>` → `WaterZone_v2.ApplyAudioSettings(...)` 일괄 주입.
- 존 개별 인스펙터 지정 불필요.
- 이 3개 레벨은 `WaterZone_v2`에서 `[SerializeField]` 제거, 매니저 전용 소유.
- `hardEnterThreshold`/`audioRefSpeed`는 여전히 존별 인스펙터 필드.

## EnvironmentalZone (추상 베이스)
`OnTriggerEnter2D`/`OnTriggerExit2D`로 슬라임 추적. `FixedUpdate`에서 `ApplyEffect(slime, dt)` 호출.
- 가상 메서드: `OnSlimeEnter`, `OnSlimeExit`, `ApplyEffect` (abstract).
- WindZone2D, DragZone은 아직 미이관 (동일 패턴이나 독립 구현).

## 관련 파일
- [WaterZone.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterZone.cs)
- [WaterZone_v2.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/WaterZone_v2.cs)
- [EnvironmentalZone.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/EnvironmentalZone.cs)
