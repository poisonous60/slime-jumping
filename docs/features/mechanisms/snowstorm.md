# Snowstorm2D (눈폭풍 구간)

스테이지 폭 단위로 깔리는 PolygonCollider2D 트리거 (zone 범위를 비사각형으로 자유롭게 그릴 수 있음). 기본값은 Jump King "Stormwall Pass" 식 4-페이즈 사이클로 좌/우 바람이 번갈아 불며, 슬라임이 zone 내부로 들어오면 화면 전체에 storm 비주얼이 fade-in 되고 나가면 fade-out 됨. 필요하면 `sections`로 구간별 고정 바람/무풍을 지정하고, `windShelterAreas`로 실내·벽 뒤처럼 슬라임이 바람 영향을 받지 않는 쉘터를 지정한다. WindZone2D_v2와 별개 컴포넌트 — v2는 단일 정적 바람 의미론, Snowstorm2D는 timer-driven + presence-gated + section override.

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
ambient Snow.prefab을 가로로 미는 box ForceField. **zone collider와 무관 — 매 LateUpdate 카메라 따라가며 viewport 전체(× `coverageMultiplier`) 크기로 리사이즈**. PolygonCollider2D는 on/off 트리거일 뿐이고, 폭풍 안에 들어오면 화면 전체 ambient 눈이 옆으로 흩날린다(존 박스 안에서만이 아니라). drift 강도는 `slimePresenceWeight`로 게이트 → 존 밖은 수직 낙하, 안은 full 횡류로 fade in/out.
| 필드 | 기본 | 설명 |
|------|------|------|
| `snowForceField` | (wire) | 자식 ParticleSystemForceField. 정적 config(shape/drag/layer)만 OnValidate/OnEnable에서 설정, geometry는 런타임 카메라 viewport 추종. direction = `currentWindAcceleration × presence` |
| `snowDrag` | 1.5 | linear drag — terminal ≈ `magnitude / snowDrag` |

### Visual — Stage Snow Swap (기존 스테이지 눈 교체)
스테이지 전반에 항시 깔려 있는 **별도** 눈 PS(예: 스테이지 전역에 배치된 [Snow.prefab](../../../SlimeJumping_v2/Assets/Prebs/VFX/Snow.prefab) 인스턴스, `SnowCameraEmitter` 포함)를 `suppressedSnowParticles` 배열에 wire하면, 슬라임이 zone 안에 있는 동안 그 눈이 **fade-out**된다. storm 비주얼이 presence로 fade-IN 하는 것과 정확히 반대 → 둘이 **교체(cross-fade)**되어 눈이 겹쳐 두 배로 쌓이지 않음. zone 밖에선 스테이지 눈 그대로, 안에선 꺼짐.

| 필드 | 기본 | 설명 |
|------|------|------|
| `suppressedSnowParticles` | empty | zone 진입 시 끌 기존 스테이지 눈 PS 배열 |

게이트 모델: emission rate = `baseline × (1 − slimePresenceWeight)`, 렌더러 알파 = `1 − slimePresenceWeight` (storm PS와 동일한 MPB `_BaseColor`/`_Color`/`_TintColor` fade). 알파까지 같이 내려야 in-flight 입자(눈 lifetime 5-9s)도 lockstep으로 사라져 깔끔히 교체됨. baseline은 OnEnable lazy capture (전체 MinMaxCurve 보존, mode-safe), OnDisable에 emission + 알파 모두 복원 → zone 사라지면 스테이지 눈 풀로 재개.

`Sync Storm Vy To Stage Snow` context menu: `suppressedSnowParticles[0]`의 낙하 속도(velocityOverLifetime.y, 없으면 startSpeed)를 calm/left/right peak Vy에 복사 — 경계 cross-fade가 "같은 눈이 거세지는" 느낌이 되도록 storm 낙하 속도를 스테이지 눈에 맞춤.

### Visual — Storm-owned PS (전체 화면 볼륨 스폰)
Snowstorm2D 직속 자식 ParticleSystem 1개 — `stormParticles`. **카메라 앵커 Local sim** — 매 LateUpdate에 PS transform=카메라 위치, simulationSpace=Local로 입자가 viewport에 묶임 → 슬라임 빠른 점프에도 입자 안 뒤처짐.

**Spawn box = viewport 전체 × `coverageMultiplier` (볼륨 스폰).** 이전의 top-strip + side-strip 쌍을 대체 — strip 방식은 emission rate 변화(입장 시 phase reset, calm→wind 램프)가 strip에서만 주입돼 새 밀도가 화면 꼭대기부터 띠로 내려오는 게 보였음 ("눈이 한 번에 생성되고 떨어지는" 현상). 볼륨 스폰은 rate 변화가 화면 전체에 즉시 균일 반영되고, 풍향 무관하게 viewport를 커버해 side strip이 불필요. 화면 중간 spawn/death 팝은 colorOverLifetime 알파 페이드(0→15% in, 85→100% out, Snow.prefab과 동일 키 배치 — ConfigureStormForFade에서 코드로 강제)로 가림.

| 필드 | 기본 | 설명 |
|------|------|------|
| `stormParticles` | (wire) | 전체 화면 볼륨 스폰 PS |
| `targetCamera` | Camera.main fallback | 추종 대상 |
| `coverageMultiplier` | 1.4 | base spawn box = viewport extent × multiplier (양 축) |
| `stormParticleLifetime` | 2.5s | 입자 수명. upstream 마진 크기도 결정 (아래 참고) |
| `stormCalmRate` | 900 | calm baseline emission (화면 내 밀도 기준 — 박스 면적비로 자동 스케일) |
| `stormPeakRate` | 1800 | peak emission (동일) |
| `calmVelocity` | (0, -2.75) | calm 시 입자 속도 (스테이지 눈 낙하 속도와 일치) |
| `leftPeakVelocity` / `rightPeakVelocity` | (∓14, -2.75) | peak wind 시 입자 속도 |
| `stormFallJitter` | 0.4 | vy ±jitter 비율 — 평행선 패턴 깸 |

#### 입자 운동 (Local space)
```
vel.space = Local
vel = Lerp(calmVelocity, directionPeakVelocity, intensity)
vel.y per-particle jitter: MinMaxCurve(vy × (1 + jitter), vy × (1 - jitter))
gravityModifier = 0, startSpeed = 0  (velocityOverLifetime이 유일한 속도 소스)
```

#### Upstream 마진 (스폰 영역 희박 문제)
균일 볼륨 스폰의 정상상태 밀도는 박스 가장자리(입자가 들어오는 쪽)에서 0이고, 수명 동안 이동 거리(`|v| × lifetime`)만큼 하류로 내려와야 포화된다 — 박스를 viewport에 딱 맞추면 화면 위쪽(calm)·upwind 쪽(wind)이 눈에 띄게 희박해짐. Stage2 눈의 `heightMultiplier` overshoot와 같은 아이디어를 정확한 크기로 적용: 매 프레임 spawn box를 속도의 반대 방향(상류)으로 축별 `|v_axis| × stormParticleLifetime`만큼 연장(+중심을 상류로 반마진 이동) → 희박 ramp 전체가 화면 밖. emission rate는 박스/기준 면적비로 곱해 설정 rate가 "화면 내 밀도" 의미를 유지 (안 하면 peak wind에서 박스가 커지며 눈이 옅어짐). maxParticles는 OnEnable에서 ≥20000으로 강제 (peak: ~5800/s × 2.5s ≈ 15k — cap에 걸리면 조용한 스로틀로 희박 문제가 재발).

### Slime Presence Fade
| 필드 | 기본 | 설명 |
|------|------|------|
| `presenceFadeSpeed` | 1.5 | 모든 storm 비주얼 통합 fade 속도 (units/sec). 1.5 ≈ 0.66s |

런타임 `slimePresenceWeight` (0~1)이 슬라임 trigger in/out에 따라 MoveTowards. storm 비주얼이 이 weight를 사용:
- storm PS emission = `Lerp(calmRate, peakRate, intensity)` — **presence 게이트 없이 항상 풀 rate** (steady-state 인구 상시 유지), 가시성은 렌더러 MPB 알파 = `presence`로만 게이트 → 입장 순간 이미 화면에 깔린 눈이 페이드인됨 ("입장하자마자 눈이 새로 생성돼 떨어지는" 현상 방지)
- post Volume weight = `presence × intensity` (직접 할당)
- ambient sound volume = `intensity × presence × peakVolume`
- 스테이지 눈 (suppressedSnowParticles): emission `× (1 − presence)`, 알파 `1 − presence` — storm과 cross-fade

이전 hard zone-bounds particle cull로 인한 boundary snap 제거. 슬라임 zone 이탈 시 storm 알파 점진 0, 스테이지 눈 복귀.

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

씬 Gizmo: polygon outline은 Unity 기본 collider gizmo가 그림. Snowstorm2D는 polygon bounds 중심에 풍향 화살표만 추가 (Application.isPlaying이면 currentMagnitude/Direction 반영, 아니면 정적 windAccelerationMagnitude).

## Setup tool

`SlimeJumping/Mechanism/Setup Snowstorm Test` ([SnowstormTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/SnowstormTestSetup.cs)) — 자동 셋업:
- 기본 zone 40×24 사각 PolygonCollider2D 트리거 + Snowstorm2D (path 편집으로 비사각형 변형 가능)
- 테스트 플랫폼 (Floor / LaunchStep / MiddlePerch / RightLanding)
- 자식 SnowForceField (WindForceField layer)
- 자식 StormParticles 1개 (ConfigureStormPs — Local sim, Noise 모듈, ExternalForces off)
- 자식 StormVolume (글로벌 URP Volume + SnowstormProfile asset 자동 생성)
- Material 재사용: Snow.prefab 에셋의 ParticleSystemRenderer.sharedMaterial을 storm PS에 전달 (인스턴스화 없음). fallback URP Particles/Unlit + null-guard + LogWarning

스테이지 눈(suppressedSnowParticles)은 씬마다 손으로 wire — setup tool은 storm 내부 부품만 만든다. prewarm은 Snowstorm2D가 OnEnable에 ON으로 강제 (emission이 항상 풀 rate라 presence 게이트와 충돌 없음).

## 디자인 결정 노트

### 왜 Snow.prefab 재사용이 아니라 자체 PS인가
초기 설계는 Snow.prefab 두 인스턴스 (background + foreground)를 부스트하는 방식. 3가지 한계 발견:
1. Snow.prefab의 `SnowCameraEmitter`는 카메라 추종이라 zone 밖에서도 입자 보임
2. `SnowCameraEmitter.emitterArea` 폴리곤이 좁아 화면 중앙 띠만 보이고 위/아래 잘림
3. spawn 영역이 viewport 전체라 풍속 강할 때 입자가 즉시 downwind 이탈, 유효 in-screen 밀도 폭락 + 슬라임 점프 시 World sim이라 입자 뒤에 남음

해결: Snowstorm2D가 자체 PS 직접 소유 + Local sim space + 카메라 앵커. 한계 #3의 "downwind 이탈로 밀도 폭락"은 emission rate를 충분히 높게 잡고 lifetime을 탈출 시간에 auto-size해 흡수.

> **2026-06-11 정리**: 한때 ambient Snow.prefab 복사본("AmbientSnow")을 `snowParticles` 슬롯에 wire해 zone 안 밀도 부스트로 썼으나 제거됨. 스테이지 눈과 외형이 같아 "storm 안에 stage 눈이 보인다"로 읽혔고, World sim + noise wobble로 storm 입자의 균일 낙하와 어긋나는(가끔 위로 떠오르는) 알갱이가 거슬렸음. zone 안 눈은 storm-owned PS가 전담하고, 밀도는 `stormCalmRate`/`stormPeakRate`로 조절.

### 왜 strip 스폰이 아니라 전체 화면 볼륨 스폰인가
초기 구현은 top-strip(위에서 낙하 진입) + side-strip(upwind에서 횡단 진입) 쌍. emission rate 변화가 strip에서만 주입되다 보니 rate가 바뀔 때마다(입장 phase reset, calm→wind 램프) 새 밀도가 화면 꼭대기부터 띠 모양으로 내려오는 게 보였음 — "눈이 한 번에 생성되고 떨어지는" 어색함. 전체 화면 볼륨 스폰은 rate 변화가 화면 전역에 즉시 균일 반영되고 풍향 무관 커버리지로 side strip도 불필요. 화면 중간 spawn/death 팝은 Snow.prefab과 같은 colorOverLifetime 알파 페이드로 가림. 입자 외형(startSize 0.05-0.14, white)과 calm 낙하 속도(-2.75)도 스테이지 눈과 통일해 경계 cross-fade가 "같은 눈이 거세지는" 연출로 읽히게 함.

### 왜 hard cull이 아니라 presence fade인가
초기에 zone bounds AABB cull로 zone 밖 입자 즉시 사망. 결과: 슬라임이 zone 경계 통과하면 입자가 boundary에서 snap → 어색. presence weight 게이트로 emission만 fade하고 잔존 입자는 lifetime 끝까지 자연 낙하 → "폭풍이 점차 잦아드는" 느낌.

### 왜 calm rate가 0이 아닌가
"바람이 멈춘 거지 눈이 멈춘 게 아니다." Emission `Lerp(calm, peak, intensity)` → calm에도 baseline snowfall 유지. 단 vx=0이라 수직 낙하 (사선 drift는 wind 페이즈에서만).

### 왜 vy jitter + Noise 모듈인가
모든 입자 동일 vy → trajectory 평행 → 시각적으로 ruled lines 패턴. `stormFallJitter` ±40% MinMaxCurve range + Noise 모듈로 깸. 자연스러운 snowfall 분포.

### 참고 레퍼런스
- [Celeste Wind Wiki](https://celeste.ink/wiki/Wind) — 입자가 단일 픽셀 흰/연파랑, gravity 변동으로 미세 curve, off-camera 시작
- [Jump King Stormwall Pass Wiki](https://jump-king.fandom.com/wiki/Stormwall_Pass) — 좌/우 4-페이즈 + 두꺼운 눈으로 platform 발 묶힘
- [SLYNYRD Parallax Pixelblog](https://www.slynyrd.com/blog/2019/11/12/pixelblog-23-parallax-scrolling) — 깊이 레이어 분리 패턴
