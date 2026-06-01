# Snowstorm2D (눈폭풍 구간)

스테이지 폭 단위로 깔리는 BoxCollider2D 트리거. 기본값은 Jump King "Stormwall Pass" 식 4-페이즈 사이클로 좌/우 바람이 번갈아 불며, 슬라임이 zone 내부로 들어오면 화면 전체에 storm 비주얼이 fade-in 되고 나가면 fade-out 됨. 필요하면 `sections`로 구간별 고정 바람/무풍을 지정하고, `windShelterAreas`로 실내·벽 뒤처럼 슬라임이 바람 영향을 받지 않는 쉘터를 지정한다. WindZone2D_v2와 별개 컴포넌트 — v2는 단일 정적 바람 의미론, Snowstorm2D는 timer-driven + presence-gated + section override.

관련 파일: [Snowstorm2D.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/Snowstorm2D.cs), [SnowstormTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/SnowstormTestSetup.cs).

## 4-페이즈 사이클

```
WindLeft  →  CalmAfterLeft  →  WindRight  →  CalmAfterRight  →  loop
  -X            0                +X              0
```

각 페이즈는 `windDuration` (wind) 또는 `calmDuration` (calm) 동안 유지. 페이즈 전환 경계에서 magnitude가 `transitionDuration` 동안 lerp (부호 즉시 flip 시 시각/물리 점프 방지). `currentDirection`은 마지막 non-zero sign을 유지 — calm 통과 시점에 sign이 0으로 떨어지지 않아 입자/strip 위치가 갑자기 반대편으로 점프하는 일 없음.

`startPhase` + `startPhaseOffset`으로 디자이너가 시작 페이즈 사전 설정. offset이 한 페이즈 길이를 넘으면 while 루프로 다음 페이즈로 자동 전진 (multi-phase pre-roll).

## 구간별 바람 override

`sections` 배열에 child `BoxCollider2D` 같은 trigger collider를 넣으면, 슬라임 위치가 해당 collider 안에 있을 때 그 section 설정이 기존 4-페이즈 사이클보다 우선된다. 배열 앞쪽이 우선순위가 높다.

| 필드 | 설명 |
|------|------|
| `label` | 디자이너용 이름. 런타임 로직은 읽지 않음 |
| `area` | 이 section 범위를 표시하는 Collider2D. `OverlapPoint(slime.Position)` 기준 |
| `usePhaseCycle` | ON이면 이 구간도 기존 4-페이즈 사이클 사용. OFF이면 `windAcceleration` 고정값 사용 |
| `windAcceleration` | 수평 바람 가속도. X만 사용하고 Y는 OnValidate에서 0으로 정규화. 예: `(-6, 0)` = 약한 왼쪽 상시 바람, `(0, 0)` = 바람 없는 눈 연출 구간 |
| `affectsSlime` | OFF이면 이 section의 바람은 슬라임을 밀지 않음 |
| `affectsSnowParticles` | OFF이면 `snowForceField`에는 0을 넣어 ambient Snow.prefab 입자도 바람에 밀리지 않음 |

`windShelterAreas`는 section과 별도. 슬라임이 여기에 들어가면 active section/phase가 어떤 바람을 만들고 있든 **슬라임 push만 차단**한다. 그래서 실내, 뒤에 벽이 있는 오목한 공간, 바람막이 뒤처럼 "눈폭풍은 보이지만 캐릭터는 바람을 안 맞는" 구간을 표현할 수 있다. 비주얼, 오디오, post volume은 active section의 intensity를 그대로 따른다.

Inspector의 `Wind Shelter Areas` 섹션에는 전용 생성 버튼이 있다.
- `+ Box`: Snowstorm 자식 `WindShelterArea_N` GameObject 생성 + `BoxCollider2D(isTrigger=true)` 추가 + 배열에 자동 등록
- `+ Polygon`: 같은 방식으로 `PolygonCollider2D(isTrigger=true)` 사각 path를 생성해 배열에 자동 등록
- 각 row의 `-`: 배열에서 제거. 대상 collider가 Snowstorm 직속 자식이고 이름이 `WindShelterArea_N` 숫자 suffix 형식인 자동 생성 오브젝트면 GameObject까지 같이 삭제한다. 수동으로 넣은 외부 collider는 참조만 제거한다.

## 핵심 필드

### Phase Timing
| 필드 | 기본 | 설명 |
|------|------|------|
| `windDuration` | 5s | wind 페이즈 지속 |
| `calmDuration` | 2.5s | calm 페이즈 지속 |
| `transitionDuration` | 0.8s | 페이즈 magnitude lerp 시간. 0 = instant |
| `startPhase` | CalmAfterRight | 시작 페이즈 |
| `startPhaseOffset` | 0 | 시작 시점 사이클 pre-roll 초 |

### Wind (슬라임 push)
| 필드 | 기본 | 설명 |
|------|------|------|
| `windAccelerationMagnitude` | 18 | wind 페이즈 peak 가속도 (units/s²) |
| `terminalSpeed` | 14 | 바람 방향 max 속도. 도달 시 가속 감쇠 |
| `sections` | empty | 구간별 phase override. 비어 있으면 기존 동작 그대로 |
| `windShelterAreas` | empty | 슬라임 물리 push만 막는 쉘터 collider 목록 |
| `airborneMultiplier` | 1.0 | Airborne/Stunned 상태 배율 |
| `attachedMultiplier` | 0.7 | Attached 상태 배율 |
| `chargingMultiplier` | 0.2 | Charging 상태 배율 |

### Visual — Snow ForceField
WindZone2D_v2와 동일 모델. ambient Snow.prefab을 zone bounds 안에서 가속.
| 필드 | 기본 | 설명 |
|------|------|------|
| `snowForceField` | (wire) | 자식 ParticleSystemForceField. OnValidate에서 BoxCollider2D 기준 size/위치 자동 싱크 |
| `snowDrag` | 1.5 | linear drag — terminal ≈ `magnitude / snowDrag` |

### Visual — ambient Snow.prefab boost (옵셔널)
`snowParticles` 슬롯에 씬의 [Snow.prefab](../../../SlimeJumping_v2/Assets/Prebs/VFX/Snow.prefab) 인스턴스 wire하면, storm 활성 중 그 PS의 emission rate + startSize를 lerp로 부스트. baseline은 OnEnable에 lazy capture (전체 MinMaxCurve 보존, mode-safe). OnDisable에 자동 복원.
| 필드 | 기본 | 설명 |
|------|------|------|
| `snowParticles` | null | 부스트 대상 ambient PS |
| `snowPeakRate` | 1800 | peak intensity 시 절대 emission rate |
| `snowPeakSizeMultiplier` | 1.8 | peak 시 baseline 위 상대 size 배율 |

### Visual — Storm-owned PS pair (top + side)
Snowstorm2D 직속 자식 ParticleSystem 2개 — `stormParticles` (top-strip) + `sideStormParticles` (upwind-side-strip). **카메라 앵커 Local sim** — 매 LateUpdate에 PS transform=카메라 위치, simulationSpace=Local로 입자가 viewport에 묶임 → 슬라임 빠른 점프에도 입자 안 뒤처짐.

| 필드 | 기본 | 설명 |
|------|------|------|
| `stormParticles` | (wire) | top-strip PS — viewport 위쪽 밖 horizontal strip |
| `sideStormParticles` | (wire) | upwind-side-strip PS — viewport 옆쪽 밖 vertical strip |
| `targetCamera` | Camera.main fallback | 추종 대상 |
| `stripThickness` | 2 | spawn strip 두께 (위쪽 strip의 Y, 측면 strip의 X) |
| `stripOffset` | 1.5 | viewport edge로부터 strip까지 안전 마진 |
| `coverageMultiplier` | 1.4 | strip 길이 = viewport extent × multiplier |
| `stormCalmRate` | 900 | top-strip calm baseline emission |
| `stormPeakRate` | 1800 | top-strip peak emission |
| `sideStormPeakRate` | 1000 | side-strip peak emission (calm baseline 0 — vx=0이라 측면 spawn 무의미) |
| `stormDriftSpeed` | 14 | velocityOverLifetime.x = `direction × drift × intensity` |
| `stormFallBias` | 3 | velocityOverLifetime.y constant downward (gravityModifier 위 추가) |
| `stormFallJitter` | 0.4 | vy ±jitter 비율 — 평행선 패턴 깸 |

#### Spawn 전략
| strip | local position | local scale | 역할 |
|-------|---------------|-------------|------|
| top | `(0, halfH + stripOffset + thickness/2, 0)` | `(halfW × 2 × cov, thickness, 0)` | 위에서 떨어짐, viewport 상단부 채움 |
| side | `(-direction × (halfW + stripOffset + thickness/2), 0, 0)` | `(thickness, halfH × 2 × cov, 0)` | 측면에서 횡단, viewport 하단부 채움 |

top 단독으로는 +X 풍향 시 (top-left → bottom-right) 사선 패턴이 되어 좌하단이 빔. side가 그 갭 메움.

#### 입자 운동 (Local space)
```
vel.space = Local
vel.x = direction × stormDriftSpeed × intensity
vel.y = MinMaxCurve(-fallBias × (1 + jitter), -fallBias × (1 - jitter))
gravityModifier = 0.6  (configure 시점, 누적 가속)
```

가로 drift는 intensity 스케일 (calm vx=0 = 수직 낙하). 세로 fall은 jitter range로 입자별 변동 → 평행선 패턴 깨짐. Noise 모듈도 추가 (strength 1.2, freq 0.6) → 유기적 perturbation.

### Slime Presence Fade
| 필드 | 기본 | 설명 |
|------|------|------|
| `presenceFadeSpeed` | 1.5 | 모든 storm 비주얼 통합 fade 속도 (units/sec). 1.5 ≈ 0.66s |

런타임 `slimePresenceWeight` (0~1)이 슬라임 trigger in/out에 따라 MoveTowards. 모든 storm 비주얼이 이 weight를 곱해 사용:
- top emission = `Lerp(calmRate, peakRate, intensity) × presence`
- side emission = `peakRate × intensity × presence`
- post Volume weight = `presence × intensity` (직접 할당)
- ambient sound volume = `intensity × presence × peakVolume`
- ambient Snow.prefab boost: `gatedIntensity = intensity × presence`로 lerp

이전 hard zone-bounds particle cull로 인한 boundary snap 제거. 슬라임 zone 이탈 시 emission 점진 0, 잔존 입자는 lifetime 끝까지 자연 wind-down.

### Post-process
| 필드 | 기본 | 설명 |
|------|------|------|
| `postVolume` | (wire) | 글로벌 URP Volume. Vignette + ColorAdjustments + WhiteBalance 프로파일 |

`UnderwaterVolumeBinder` 패턴 차용하되 직접 weight 할당 (presence가 이미 smooth).

### Camera Shake (옵셔널, 기본 OFF)
| 필드 | 기본 | 설명 |
|------|------|------|
| `impulseEmitter` | null | `CinemachineImpulseSource` + `ImpulseEmitter` ([ImpulseEmitter.cs](../../../SlimeJumping_v2/Assets/Scripts/Effects/ImpulseEmitter.cs)) |
| `shakeForce` | 0 | 페이즈 전환 시 magnitude delta × force로 1-shot impulse. 0 = 비활성 |

### Audio
| 필드 | 기본 | 설명 |
|------|------|------|
| `ambientLoopClip` | null | wind ambient loop 클립 |
| `ambientPeakVolume` | 0.6 | peak intensity × presence 시 볼륨 |

[`SoundService.PlayLooping`](../../../SlimeJumping_v2/Assets/Scripts/Audio/SoundService.cs) 패턴. volume이 0에 가까워지면 `StopLoop`로 풀 슬롯 해제 (calm + slime out 시 양쪽 게이트 닫힘).

## 슬라임 Push 모델

WindZone2D_v2와 동일한 `AddSustainedFlowImpulse` 컨트랙트. `windAcceleration` vector를 timer state machine이 동적 계산:
```
worldDir = normalize(currentWindAcceleration)
multiplier = state별 (Airborne/Attached/Charging/Stunned)
falloff   = clamp01(1 - dot(slime.Velocity, worldDir) / terminalSpeed)
deltaV    = worldDir × currentWindAcceleration.magnitude × multiplier × falloff × dt
slime.AddSustainedFlowImpulse(deltaV)
```

Attached/Charging에서 `slideVelocity`/`crawlVelocity` 영구 채널 적분 → frame 누적. v2 동일 의미론. `activeSectionAffectsSlime=false` 또는 `windShelterAreas` 내부일 때는 이 push 단계만 skip한다. 외부 힘 파이프라인 상세는 [../infra/external-velocity-two-channel.md](../infra/external-velocity-two-channel.md).

`OnTriggerEnter2D`에서 `trackedSlime` 등록 + `slimeInside=true`. `OnTriggerExit2D`에서 해제. timer/visuals는 슬라임 존재와 무관하게 항상 진행 (presence weight만 따로 fade).

## 디버그 가시성

인스펙터 **Runtime (read-only)** 섹션 — 게임 중 실시간 표시:
- `currentPhase` (enum)
- `phaseElapsed` (현재 페이즈 안의 경과 초)
- `currentMagnitude` (현재 가속도 절대값)
- `currentDirection` (마지막 non-zero sign)
- `currentWindAcceleration` (현재 실제 바람 벡터)
- `activeSectionIndex` / `activeSectionAffectsSlime` / `activeSectionAffectsSnowParticles`
- `slimeInside` (트리거 상태)
- `slimePresenceWeight` (0~1 fade weight)

씬 Gizmo: zone collider AABB + 풍향 화살표 (Application.isPlaying이면 currentMagnitude/Direction 반영, 아니면 정적 windAccelerationMagnitude).

## Setup tool

`SlimeJumping/Mechanism/Setup Snowstorm Test` ([SnowstormTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/SnowstormTestSetup.cs)) — 자동 셋업:
- 기본 zone 40×24 BoxCollider2D 트리거 + Snowstorm2D
- 테스트 플랫폼 (Floor / LaunchStep / MiddlePerch / RightLanding)
- 자식 SnowForceField (WindForceField layer)
- 자식 StormParticles_Top + StormParticles_Side (모두 ConfigureStormPs로 동일 정적 traits 적용 — Local sim, lifetime 3-5s, startSize 0.10-0.18, gravity 0.6, Noise 모듈, ExternalForces off, prewarm OFF)
- 자식 StormVolume (글로벌 URP Volume + SnowstormProfile asset 자동 생성)
- ambient Snow.prefab 1개 인스턴스 (snowParticles 슬롯에 wire, optional 부스트용)
- Material 재사용: Snow.prefab의 ParticleSystemRenderer.sharedMaterial을 storm PS에 전달. fallback URP Particles/Unlit + null-guard + LogWarning

prewarm은 양 PS 모두 OFF — presence 게이트와 충돌 (씬 시작 시 슬라임 위치 무관 burst 방지).

## 디자인 결정 노트

### 왜 Snow.prefab 재사용이 아니라 자체 PS인가
초기 설계는 Snow.prefab 두 인스턴스 (background + foreground)를 부스트하는 방식. 3가지 한계 발견:
1. Snow.prefab의 `SnowCameraEmitter`는 카메라 추종이라 zone 밖에서도 입자 보임
2. `SnowCameraEmitter.emitterArea` 폴리곤이 좁아 화면 중앙 띠만 보이고 위/아래 잘림
3. spawn 영역이 viewport 전체라 풍속 강할 때 입자가 즉시 downwind 이탈, 유효 in-screen 밀도 폭락 + 슬라임 점프 시 World sim이라 입자 뒤에 남음

해결: Snowstorm2D가 자체 PS 직접 소유 + Local sim space + 카메라 앵커 + upwind-edge spawn strip. ambient Snow.prefab은 optional 보조로만 wire (스테이지 전반의 baseline snowfall).

### 왜 hard cull이 아니라 presence fade인가
초기에 zone bounds AABB cull로 zone 밖 입자 즉시 사망. 결과: 슬라임이 zone 경계 통과하면 입자가 boundary에서 snap → 어색. presence weight 게이트로 emission만 fade하고 잔존 입자는 lifetime 끝까지 자연 낙하 → "폭풍이 점차 잦아드는" 느낌.

### 왜 calm rate가 0이 아닌가
"바람이 멈춘 거지 눈이 멈춘 게 아니다." Top emission `Lerp(calm=900, peak=1800, intensity)` → calm에도 baseline snowfall 유지. 단 vx=0이라 수직 낙하 (사선 drift는 wind 페이즈에서만). Side emission은 calm 0 — vx=0이면 측면 spawn 입자가 viewport 안 못 들어오고 edge 밖에서만 떨어져 무의미.

### 왜 vy jitter + Noise 모듈인가
모든 입자 동일 vy → trajectory 평행 → 시각적으로 ruled lines 패턴. `stormFallJitter` ±40% MinMaxCurve range + Noise 모듈 (strength 1.2, freq 0.6, damping)로 깸. 자연스러운 snowfall 분포.

### 참고 레퍼런스
- [Celeste Wind Wiki](https://celeste.ink/wiki/Wind) — 입자가 단일 픽셀 흰/연파랑, gravity 변동으로 미세 curve, off-camera 시작
- [Jump King Stormwall Pass Wiki](https://jump-king.fandom.com/wiki/Stormwall_Pass) — 좌/우 4-페이즈 + 두꺼운 눈으로 platform 발 묶힘
- [SLYNYRD Parallax Pixelblog](https://www.slynyrd.com/blog/2019/11/12/pixelblog-23-parallax-scrolling) — 깊이 레이어 분리 패턴
