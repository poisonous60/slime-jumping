# Snowball (눈덩이 — Stage 2 동적 장애물)

마리오 메이커 / 셀레스트 영감의 굴러가는 / 날아오는 눈덩이. 단일 컴포넌트 `Snowball.cs` 가 두 모드 (Rolling / Flying) 를 처리하며, 슬라임이 위에서 밟으면 자동 점프 + 부서짐, 옆/아래에서 부딪히면 밀려남 + 부서짐. 단일 배치와 스포너 기반 ObjectPool 둘 다 지원.

## 모드

| Mode | 거동 |
|------|------|
| Rolling | 발판 위를 한 방향 (±1) 으로 등속 굴러감. 표면 추종 raycast 로 슬로프/곡선 자연 추종. 임계치 이상 가팔라진 오르막 만나면 부서짐. 절벽에서는 포물선 낙하로 전환. |
| Flying | 자유 각도 (`flyDirection`) 로 등속 직진. 중력 없음. 지형/벽 충돌 시 부서짐. 수명 한계 시 조용히 제거. 시각 회전은 시간 기반 등속. |

## Phase 머신

```
Active        — 굴러감 / 날아감
  → Falling   (Rolling 모드 전용 — 절벽 진입)
  → Crumbling (부서짐 visual + break SFX, crumbleDuration 동안)
  → Gone      (visual/collider off, respawn 또는 destroy/pool 반환)
  → Active    (respawnTime 후 overlap-clear 통과 시 재시작)
```

- **Active (Rolling)**: `FixedUpdateRolling` 매 프레임 `currentSpeed += -gravity * motionDir.y * dt` 로 표면 tangent 중력 적분 → `Mathf.Min(currentSpeed, maxSpeed)` cap → `motionDir * currentSpeed * dt` 이동. 오르막 정점에서 currentSpeed 가 음수가 되면 `direction *= -1` + `motionDir = -motionDir` + `currentSpeed = -currentSpeed` 로 자연 반전. Forward-motion probe (`motionDir` 방향 raycast, `radius + lookahead`) 와 forward-down probe 둘 다 `breakSlopeDelta` 기준으로 판정 — 한 쪽이라도 임계 초과면 `Break()`. forward-down ray 가 발판 못 찾으면 `EnterFalling()`.
- **Active (Flying)**: `FixedUpdateFlying` 이 `flyDirection.normalized * maxSpeed * dt` 등속 이동, `Physics2D.CircleCast` 로 충돌 검사. lifetime (`maxLifetime`, 기본 5s) 초과 시 `SilentCleanup` (no VFX, 부활 정책은 EnterGone 위임).
- **Falling**: 절벽 진입 시 호출되는 `EnterFalling` 이 `fallVelocity = motionDir * currentSpeed` 로 시작 (Rolling 누적 속도 보존). 매 FixedUpdate `fallVelocity.y -= fallGravity * dt` 적분 + 옵션 `horizontalDecay` 지수 감쇠. `Physics2D.CircleCast` 로 충돌 검출 → `Vector2.Angle(hit.normal, Vector2.up) ≤ maxLandingAngle` (기본 45°) 면 `ResumeRollingFromLanding` (낙하 운동량을 새 표면 tangent 로 투영해 `currentSpeed = clamp(dot(lastFallVel, motionDir), 0, maxSpeed)` 으로 복귀), 초과면 `Break`. `maxFallTime` 초과 시 `SilentCleanup`.
- **Crumbling**: `EnterCrumbling` 이 `SpawnFragments` (Random unit-circle 방향으로 `crumbleFragmentPrefab` N개 instantiate, 각 fragment 에 random magnitude `linearVelocity` 부여), `snowDustParticles.Play(true)`, `impulseEmitter.Emit(cameraShakeForce)` (Cinemachine), break SFX 재생, `HideVisualAndCollider` 호출. `crumbleDuration` (기본 0.2s) 후 `EnterGone`.
- **Gone**: `respawnTime < 0` 면 `OnDespawn?.Invoke(this)` (스포너) 또는 `Destroy(gameObject)` (단일 배치). 양수면 `phaseTimer = respawnTime` 카운트다운 후 `Physics2D.OverlapCircle(startPosition, radius, blockingLayers)` 로 시작 위치 비어있는지 검사. 비었으면 `EnterActive`, 막혀있으면 다음 FixedUpdate 까지 대기 (CrumblingPlatform 패턴).

## 슬라임 상호작용

`OnTriggerEnter2D` / `OnTriggerStay2D` 가 `HandleTrigger` 로 라우팅. `phase != Active` 이면 즉시 return.

**눈덩이끼리 접촉**: `other.GetComponentInParent<Snowball>()` 로 검출, 자기 자신만 `Break()` 호출 (상대는 자기 trigger 이벤트로 자체 break — Unity 가 양쪽 모두에 OnTriggerEnter 발화).

**슬라임 검출**: `other.GetComponentInParent<SlimeController>()`. 위/옆 판정은 `(slime.Position - rb.position).normalized.y > topNormalThreshold` (기본 0.5).

| 접촉 | 응답 | API |
|------|------|-----|
| 위 | `slime.ForceAirborneWithVelocity(jumpDir * jumpSpeed + horizontalPreserve)` — JumpBall.cs 패턴. `jumpSpeed = max(fixedJumpSpeed, incomingSpeed * incomingSpeedPercent + bonusSpeed)`. `jumpDir` 은 슬라임 attached 면 `SurfaceNormal`, airborne 이면 `Vector2.up` (눈덩이가 회전 중이라 `transform.up` 은 흔들려서 슬라임 기준이 안전). | `SlimeController.ForceAirborneWithVelocity` ([SlimeController.Launch.cs:39](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Launch.cs#L39)) |
| 옆/아래 | `slime.ForceAirborneWithVelocity(((toSlime + Vector2.up * knockbackUpBias).normalized) * knockbackSpeed)`. 슬라임 기존 속도는 덮어씀, stun/lockout 없음. | 동일 API. |

`ForceAirborneWithVelocity` 가 Charging 상태에서도 `aimOffset = Vector2.zero`, `isVolumeCharging = false` 로 깔끔하게 charge 취소 → Airborne 전환을 보장 ([SlimeController.Launch.cs:50-54](../../../SlimeJumping_v2/Assets/Scripts/Controller/SlimeController.Launch.cs#L50-L54)).

어느 응답 쪽이든 응답 직후 `Break()` 호출 → 눈덩이 항상 부서짐.

## 라이프사이클 패턴

`respawnTime` 한 변수로 동작 결정:
- **`> 0`** → `CrumblingPlatform.UpdateGone` 패턴 ([CrumblingPlatform.cs:137-167](../../../SlimeJumping_v2/Assets/Scripts/Platforms/CrumblingPlatform.cs#L137-L167)) 자체 부활. 시작 위치/방향 캐시 (`startPosition`, `startDirection`) 사용.
- **`< 0`** → 일회용. `OnDespawn` 이벤트에 구독자 있으면 풀 반환, 없으면 `Destroy(gameObject)`.

`Snowball.Initialize(worldPos, rollDir, flyDir)` 가 풀 매니지드 인스턴스를 재무장하면서 `respawnTime = -1f` 강제. 풀 내 인스턴스가 자체 부활하면 풀에 영영 안 돌아가는 풋건을 방지.

## SnowballSpawner — `ObjectPool<Snowball>` 기반 cadence

[`SnowballSpawner.cs`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/SnowballSpawner.cs) 가 일정 간격으로 prefab 인스턴스를 spawn. `UnityEngine.Pool.ObjectPool<Snowball>` 로 풀링 — 프로젝트 내 첫 도입 사례.

| 인스펙터 | 기본값 | 설명 |
|---------|-------|------|
| Snowball Prefab | None | 인스턴스화할 Snowball prefab |
| Direction Override | 1 | Rolling 모드 prefab 의 direction 덮어씀 (±1) |
| Fly Direction Override | (-1, 0) | Flying 모드 prefab 의 flyDirection 덮어씀 |
| Interval | 3 | spawn 간격 (s) |
| Start Delay | 0 | 첫 spawn 지연 — 여러 스포너 동기화 깨기용 |
| Interval Jitter | 0.2 | interval 의 ±X 비율로 jitter |
| Max Alive | 3 | 동시 존재 최대 수. 도달 시 다음 spawn 스킵 |
| Spawn Clip | None | spawn 시 단발 SFX |
| Spawn VFX | None | spawn 시 instantiate 할 ParticleSystem prefab |

풀 동작:
- `createFunc`: `Instantiate(prefab, transform)` 후 `SetActive(false)`, `s.OnDespawn += HandleDespawn` 영구 구독.
- `actionOnGet/Release`: `SetActive(true/false)`.
- `actionOnDestroy`: `Destroy(gameObject)` (`pool.Clear()` 시 호출).
- `Spawn` 후 `s.Initialize(transform.position, directionOverride, flyDirectionOverride)` — Initialize 가 `respawnTime = -1` 강제하므로 자체 부활 안 함 → 부서지면 `EnterGone` 의 `OnDespawn.Invoke(this)` → `HandleDespawn` 이 `pool.Release(s)` 로 반환.

## Audio

세 채널:
- **Loop AudioSource** (3D positional, 디자이너가 prefab 에 attach): Active phase 동안 mode 별 클립 (`rollingClip` / `flyingClip`) 재생. `EnterCrumbling`/`SilentCleanup` 에서 stop, `EnterActive`/`Initialize` 에서 다시 play.
- **Break one-shot** (`breakClip`): `EnterCrumbling` 에서 `SoundService.PlayAt(breakClip, rb.position, SoundCategory.SFX_Object, sfxVolume)`.
- **Spawn one-shot** (스포너의 `spawnClip`): SnowballSpawner.Spawn 에서 동일 SoundService 호출.

[`SnowballAudioManager.cs`](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/SnowballAudioManager.cs) 가 씬 내 모든 Snowball 에 `ApplyAudioSettings(rolling, flying, break, volume)` broadcast. JumpBallAudioManager / IcicleAudioManager 패턴.

## 셰이더 / 머티리얼

[`Snowball_Quibli.shader`](../../../SlimeJumping_v2/Assets/Art/Shader/Snowball_Quibli.shader) — Quibli StylizedLit 클론. `Ground_Quibli_Snow.shader` 와 달리 vertex displacement 제거, footprint 제거 — 둥근 메쉬에 fragment 폴리시만 적용.

[`Snowball_FragmentInject.hlsl`](../../../SlimeJumping_v2/Assets/Art/Shader/Snowball_FragmentInject.hlsl) — `Ground_SnowFragmentInject.hlsl` 의 fresnel + glitter 블록 복사. mask 의존 footprint 블록 제거. 키워드 `_SNOWBALL_FRESNEL` / `_SNOWBALL_GLITTER` (snow 키워드와 분리하여 충돌 방지).

[`SnowballMaterialBinder.cs`](../../../SlimeJumping_v2/Assets/Scripts/Rendering/SnowballMaterialBinder.cs) — runtime material clone (SnowField pattern 압축). 디자이너가 base material + 텍스처/컬러/키워드 토글을 인스펙터에 채우면, 실행 시 `new Material(baseMaterial)` 클론에 push 후 `Renderer.sharedMaterial` 에 할당. 원본 `.mat` 자산은 read-only 유지.

**SnowField 와 톤 통일하는 방법**: SnowField 와 같은 `_GradientRamp` 텍스처 (blue→white toon ramp) 를 SnowballMaterialBinder.shadowRamp 에 할당. base color 는 살짝 다르게 (예: 약간 푸른 끼) 두면 자연스러운 일치.

## 콜라이더 / 레이어

- **Trigger Collider2D + Kinematic Rigidbody2D**: `JumpBall.cs` 패턴. 위치는 raycast 로 스크립트 제어, trigger 는 슬라임/눈덩이 감지 전용. (Kinematic Rigidbody 는 `OnTriggerEnter2D` 가 신뢰성 있게 발화하기 위한 Unity 표준).
- **`terrainMask`**: 굴러갈 수 있는 발판 레이어. 자기 자신 레이어는 제외해야 raycast 가 자기 collider 에 안 걸림. SnowballTestSetup 에서 layer 7 사용.
- **`blockingLayers`**: 부활 시점 시작 위치 overlap-clear 검사용.

## 인스펙터 파라미터

| 그룹 | 필드 | 기본값 | 설명 |
|---|---|---|---|
| Mode | Mode | Rolling | Rolling / Flying enum |
| Motion | Max Speed | 6 | m/s. Rolling 의 cap (중력 가속 상한). Flying 은 이 값으로 등속. (구 `Speed` — FormerlySerializedAs 로 호환) |
| | Initial Speed | 2 | m/s. Rolling 시작 속도. 평지면 이대로 유지, 슬로프면 gravity 로 가감속 |
| | Gravity | 20 | m/s². 표면 tangent 따라 작용. 0 = 옛 constant-speed 동작 |
| | Radius | 0.5 | 회전·raycast offset 기준 |
| | Terrain Mask | (designer) | 굴러갈 수 있는 발판 레이어 |
| Rolling | Direction | 1 | ±1, surface tangent 기준 시작 방향. 오르막 정점에서 자동 반전됨 |
| | Break Slope Delta | 30 | 현재 표면 대비 +X° 가팔라진 오르막 만나면 부서짐. forward-motion + forward-down 양쪽 probe 에 동일 적용 |
| | Lookahead | 0.3 | forward-down/forward-motion ray 의 미리보기 거리 (m) |
| | Ray Depth | 0.5 | forward-down ray down 깊이 (m) |
| Falling | Fall Gravity | 30 | 절벽 진입 후 자체 중력 (m/s²) |
| | Horizontal Decay | 0 | 1/s 지수 감쇠. 0 = 완전 보존 |
| | Max Fall Time | 3 | 한계 시간 (s). 초과 시 silent cleanup |
| | Max Landing Angle | 45 | 절대각. 이하면 Rolling 재개, 초과면 break |
| Flying | Fly Direction | (-1, 0) | 정규화 안 된 자유 각도 OK |
| | Max Lifetime | 5 | 한계 시간 (s) |
| | Flight Spin Speed | 360 | deg/s. 부호는 flyDirection.x 가 결정 |
| Slime — Top | Fixed Jump Speed | 18 | 최소 점프 속도 |
| | Incoming Speed Percent | 0.7 | 입사 속도 보존 비율 |
| | Bonus Speed | 5 | 입사 속도에 더할 보너스 |
| | Top Normal Threshold | 0.5 | (slime - snowball).normalized.y > X 면 위 판정 |
| | Preserve Horizontal On Stomp | true | 입사 수평 운동량 보존 |
| Slime — Side | Knockback Speed | 12 | 옆 충돌 시 속도 magnitude (덮어씀) |
| | Knockback Up Bias | 0.3 | 밀림 방향에 위쪽 섞을 비율 |
| Lifecycle | Respawn Time | 3 | 초. 음수 = 일회용 |
| | Blocking Layers | Everything | 부활 시점 overlap 검사 레이어 |
| | Crumble Duration | 0.2 | 부서짐 visual 지속 시간 (s) |
| Break VFX | Crumble Fragment Prefab | None | 작은 Rigidbody2D 조각 prefab |
| | Fragment Burst Count | 6 | spawn 할 조각 수 |
| | Fragment Speed | 4 | 조각 outward 속도 (random ±30%) |
| | Snow Dust Particles | None | 부서질 때 함께 재생할 dust 파티클 |
| | Impulse Emitter | None | Cinemachine 카메라 셰이크 (옵션) |
| | Camera Shake Force | 0.5 | impulse force |
| Audio | Loop Source | None | 3D positional, looping AudioSource |
| | Rolling Clip | None | Rolling 모드 loop clip |
| | Flying Clip | None | Flying 모드 loop clip |
| | Break Clip | None | 부서질 때 단발 SFX |
| | Sfx Volume | 1 | 모든 SFX 공통 볼륨 |
| Visuals | Visual Root | None | 회전 적용 대상 자식 transform (collider 와 분리) |
| | Visual Renderer | None | 부서질 때 끌 메인 visual renderer |

## 동작 불변식

1. **콜라이더 분리**: visualRoot 의 회전이 trigger 콜라이더에 영향 없음 (CLAUDE.md 규약). visualRoot 는 자식 transform.
2. **rb.position 사용**: `transform.position` 대신 `rb.position` (CLAUDE.md 규약). Kinematic Rigidbody 라 trigger 가 정상 발화.
3. **셰이더 surgical**: 기존 `Ground_*` 셰이더 / HLSL 절대 미수정. fresnel/glitter 블록은 복사. 회귀 위험 0.
4. **Pool 안전**: `Initialize` 가 `respawnTime = -1` 강제 → 풀 인스턴스가 자체 부활하지 않음.
5. **WorldTimeFreezeService 존중**: Update / FixedUpdate 진입 시 `IsFrozen` 체크 (JumpBall 패턴).

## SnowField 상호작용 (Phase 9)

Rolling 중 표면이 [SnowField](../platforms/snow-field.md) 로 등록된 host collider 위면 세 가지가 동시에 발동:

1. **연속 carve** — 매 FixedUpdate `field.Carve(snapHit.point, carveDepthPerSec * dt, radius * carveRadiusMultiplier)`. 그라나잇 블래스트의 단발 임팩트와 같은 채널을 누적 호출해 매끈한 도랑 생성.
2. **점진 성장** — `radius += growthPerMeterByRadius.Evaluate(radius) * step.magnitude`. AnimationCurve x 축이 raw 반경(m), y 축이 미터당 자라는 양. `maxRadius` 하드캡. `ApplySizeFromRadius` 가 visualRoot 스케일 + CircleCollider2D 반경 동기화 — 게임플레이 튜닝(maxSpeed/gravity/breakSlope)은 손대지 않음, **순수 visual + collider 스케일**.
3. **Rolling trail VFX** — `rollingSnowParticles.emission.enabled` 토글. RateOverDistance 는 prefab 측에 위임해 속도 자동 스케일.

**활성 게이트**: `snapHit.collider` 가 `SnowFieldRegistry` 에 등록되어 있고 `field.SampleDepthRatio01(snapHit.point) > snowActiveRatioThreshold` 일 때만. 자기가 다 파먹은 도랑에 다시 굴러들어와도 무한 성장 / 헛 입자 안 나오도록.

**라이프 리셋**: `Awake` 에서 `startRadius = radius` 캐시. `EnterActive` (자체 respawn) / `Initialize` (pool spawn) 둘 다 `radius = startRadius; ApplySizeFromRadius();` 로 인스펙터 시작값 복원 — 자라난 채로 다음 라이프 시작하지 않음.

### 착지 Splash (Falling → Rolling)

절벽에서 떨어져 SnowField 위에 다시 착지하는 순간 (`ResumeRollingFromLanding`):

```
field.Splash(hit.point, splashDipDepth, splashDipRadius, splashRingRaise, splashRingRadius);
landingSplashParticles.Play(true);
```

- 중앙에 `splashDipDepth` 만큼 한 번 푹 파임 (단발 carve)
- 외곽 ring 영역에 `splashRingRaise` 만큼 둔덕이 솟음 — 신규 `SnowField.Splash` API 가 [`SnowSettings.heightMaxMultiplier`](../platforms/snow-field.md#heightmax--splash--raiselocal-phase-9) 캡까지 raise
- `landingSplashParticles` (별도 ParticleSystem 슬롯, `snowDustParticles` 와 분리) burst
- `splashMinImpactSpeed` 이하면 splash 발동 안 함 — 부드러운 착지는 조용히 다시 굴러감

기존 `snowDustParticles` 는 작은 눈덩이 조각이 흩어지는 시각이라 "눈가루가 흩날린다" 느낌과 안 맞음 — landingSplashParticles 별도 슬롯으로 분리한 이유.

### 인스펙터 (Snow Interaction)

| 그룹 | 필드 | 기본값 | 설명 |
|---|---|---|---|
| Continuous (Rolling) | Carve Depth Per Sec | 0.06 | m/s. 매 FixedUpdate 누적되는 carve 깊이. 0 = carve 비활성 |
| | Carve Radius Multiplier | 1.0 | `carveRadius = ball.radius × this` |
| | Snow Active Ratio Threshold | 0.05 | `SampleDepthRatio01 > threshold` 일 때만 carve/grow/VFX |
| Growth | Growth Per Meter By Radius | curve | x = 현재 반경(m), y = 미터당 자라는 양(m/m). 작은 공일수록 빠르게, 큰 공이면 둔화 |
| | Max Radius | 3 | 성장 하드 캡 (m) |
| Landing Splash | Splash Dip Depth | 0.18 | m. 중앙 분화구 깊이 |
| | Splash Dip Radius | 0.6 | m. 분화구 footprint 반경 |
| | Splash Ring Raise | 0.10 | m. 옆 둔덕 높이 (heightMax 캡 적용) |
| | Splash Ring Radius | 1.2 | m. raise 가 적용되는 외곽 반경 (dipRadius < 이 값) |
| | Splash Min Impact Speed | 1.5 | m/s. 이하 컷오프 |
| VFX | Rolling Snow Particles | None | 굴러가는 동안 emission 토글되는 자식 PS |
| | Landing Splash Particles | None | 착지 splash 한 번 burst 하는 자식 PS |

## 관련 파일

- [Snowball.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/Snowball.cs) — 메인 컴포넌트
- [SnowballSpawner.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/SnowballSpawner.cs) — `ObjectPool<Snowball>` 스포너
- [SnowballAudioManager.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/SnowballAudioManager.cs) — 씬 broadcast
- [SnowballMaterialBinder.cs](../../../SlimeJumping_v2/Assets/Scripts/Rendering/SnowballMaterialBinder.cs) — runtime material clone
- [Snowball_Quibli.shader](../../../SlimeJumping_v2/Assets/Art/Shader/Snowball_Quibli.shader) — Quibli 클론, 변위/footprint 제거
- [Snowball_FragmentInject.hlsl](../../../SlimeJumping_v2/Assets/Art/Shader/Snowball_FragmentInject.hlsl) — fresnel + glitter
- [SnowballTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/SnowballTestSetup.cs) — 메뉴 `SlimeJumping/Mechanism/Setup Snowball Test`

## 알려진 한계 / 트레이드오프

- **셰이더 fresnel/glitter 코드 중복**: `Ground_SnowFragmentInject.hlsl` 과 `Snowball_FragmentInject.hlsl` 이 거의 동일한 fresnel/glitter 블록을 가짐. 추후 코드 중복이 부담스러우면 `Shader/SnowPolish_Shared.hlsl` 추출하고 양쪽에서 #include. surgical-changes 원칙으로 첫 버전은 복사.
- **부서짐 시각**: 첫 버전은 fragment burst + dust 파티클 + 카메라 셰이크. 디자이너 피드백 후 부서짐 표현이 약하면 전용 shatter 메쉬 prefab 으로 업그레이드.
- **이동 플랫폼 위 Snowball**: `PlatformCarrier` 추적 안 함. 회전/이동 플랫폼 위에 있어도 자기 속도로만 굴러감 — 마리오 메이커 / 셀레스트 동일.
- **Falling 의 horizontalDecay**: 1/s 지수 감쇠 (Phase 7 plow drag 와 동일 단위). `Mathf.Exp(-decay*dt)` 곱이 적용됨. 0 = 완전 보존, 4 ≈ 1초에 ~98% 감쇠.
