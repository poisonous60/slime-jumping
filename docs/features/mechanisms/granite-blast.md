# GraniteBlast (그라니테 블라스트 — 에너지포 해저드)

세 가지 발사 모드를 가진 에너지 캐논 해저드. *주술회전* Ryu Ishigori의 *Granite Blast* (저주에너지 발산) 기법에서 차용. 모든 모드가 빔/라인 형태로 발사됨.

## 모드
| Mode | 거동 |
|------|------|
| Branching | N개의 곡선 텐드릴(번개)이 머즐에서 직접 발사되어 각자의 경로로 슬라임 주변 지면(`groundSnapMask`)에 떨어짐. 트렁크 없음 — `launchSpreadDegrees` 가 머즐 P0 에서 텐드릴들을 부채꼴로 흩어버림. |
| Beam | 한 발의 굵은 빔이 머즐에서 첫 차폐물까지 fireStart+firing+postFire 기간 지속. 라인 위 슬라임 1회 명중. |
| Burst | `burstFlashCount` 개의 짧은 flash beam 이 콘(`burstSpreadDegrees`) 으로 부채꼴 발사. 각 flash 는 GraniteBlastBeam 인스턴스(`burstFlashDuration` 동안 표시). |

## FSM (4단계 발사)
```
Idle
  → Detecting(detectionDelay)
  → PreFire(preFireDuration)        — 발사 전 준비
  → FireStart(fireStartDuration)    — 발사 시작
  → Firing(firingDuration)          — 발사 중
  → PostFire(postFireDuration)      — 발사 이후
  → Cooldown(cooldownDuration)
  → Idle (또는 타깃 보이면 PreFire 로 루프)
```
- `loseSightGrace`로 탐지 히스테리시스.
- **PreFire**: `chargeGlow` ParticleSystem 스케일 0.05x → 1x ease-out, 슬라임이 회피할 수 있는 텔레그래프 라인 표시(`showPreFireTelegraph`). 발사 방향(`lockedDir`) + 원점(`lockedOrigin`) 이 단계 시작 시 락인 → 이후 모든 페이즈가 같은 조준선 사용.
- **FireStart**: 머즐 플래시 + `fireStartSound` + 모드별 1차 웨이브 스폰 (Branching = 텐드릴 `branchingFireStartFraction` (기본 30%), Beam = 빔 1개, Burst = 모든 플래시 `burstFireStartFraction` (기본 100%)).
- **Firing**: 잔여 텐드릴/총탄을 firingDuration 동안 균등 staggered 스폰. Beam 은 자체 라이프타임으로 유지.
- **PostFire**: chargeGlow 정지, `postFireSound`. 신규 스폰 없음. 진행 중인 텐드릴/빔/총탄은 자체 페이드 진행.
- **Cooldown**: 다음 발사 가능까지 정적 대기.

| 페이즈 | Branching | Beam | Burst |
|--------|-----------|------|-------|
| FireStart | 텐드릴 `branchingFireStartFraction` (기본 30%) — 머즐에서 직접 발사 | 빔 1개 (lifetime = fireStart+firing+postFire) | 모든 플래시 `burstFireStartFraction` (기본 100%) — 부채꼴이 한꺼번에 보이도록 |
| Firing | 잔여 종단 텐드릴 staggered | (자체 유지) | (자체 유지 — 기본값에서는 추가 스폰 없음) |
| PostFire | (스폰 없음 — fade 진행 중) | 내부 fade 시작 | (스폰 없음) |

## 명중 처리
**2026-04-29 stage2 개편 이후**: 더 이상 `ApplyHazardEffect` 를 거치지 않음 — granite blast 는 stun-less push 채널. `SlimeController.ApplyHazardPush(deltaV)` 로 외부 velocity 채널(`AddExternalVelocity` 와 동일 경로)에 직접 누적되며, `OnHazardPushed` 이벤트가 동시에 발화 (SlimeBurnVisual 이 구독). 슬라임은 입력이 살아있는 상태로 발사 주체에서 멀어지도록 밀려남.

| Mode | impulse 처리 | 다중 명중 |
|------|---------------|-----------|
| Branching | 텐드릴이 슬라임에 halt 시 `slime.ApplyHazardPush(awayDir * knockbackImpulse)` 1회. | 텐드릴 1개당 최대 1회. |
| Beam (단발) | 첫 hit 시 1회 `awayDir * knockbackImpulse`, 그 다음 빔은 halt 상태로 끝까지 유지. | 빔 인스턴스 1개당 최대 1회. |
| Beam (`sustainedHit=true`) | 매 프레임 muzzle→maxRange 재-sweep. 슬라임이 path 안에 있으면 매 프레임 `awayDir * knockbackAcceleration * dt` 누적. 슬라임이 빔에서 빠져나가면 빔이 다음 차폐물까지 통과. | 매 프레임 — 가속도 채널이라 오래 머물수록 deltaV 가 커짐. |
| Burst | 각 flash 가 단발 Beam 과 동일 — flash 1개당 최대 1회 impulse. | flash 수만큼. |

| Mode | 검출 방식 | 다중 명중 |
|------|----------|-----------|
| Branching | 텐드릴 풀 드로우 시점에 각 세그먼트 `Physics2D.CircleCastAll(radius=hitRadius)`. 첫 슬라임 히트로 종료. | 텐드릴 1개당 최대 1회. |
| Beam | 매 프레임 `CircleCastAll(radius=hitRadius)`. 슬라임 발견 시 1회만 적용. | 빔 인스턴스 1개당 최대 1회. |
| Burst | 각 flash 가 GraniteBlastBeam 으로 동작 — `CircleCastAll(radius=hitRadius)` 매 프레임. | flash 1개당 최대 1회. |

`hitRadius` (emitter, 기본 0.4) 가 세 모드 공통의 CircleCast 반경. 비주얼 라인 굵기와 무관하게 히트 볼륨을 튜닝 — `OnDrawGizmosSelected` 에서 muzzle→aim 방향 sweep tube 로 시각화됨.

## 시각
- 모든 빔/텐드릴이 단일 LineRenderer (TurretHazard.bulletTracer 와 동일한 셋업: `Sprites/Default` + `Texture2D.whiteTexture` + 정점 컬러).
- Material 은 emitter 가 캐싱 (`GetCoreMaterial`) — 발사당 신규 할당 없음.
- 너비: Beam `beamCoreWidth` (기본 0.55), Tendril `tendrilCoreWidth` (기본 0.12), Burst flash `burstFlashCoreWidth` (기본 0.22).
- 색상: 기본 `coreColor = #D9F5FF` (near-white blue). Hold/fade 단계 PerlinNoise ±18% 너비 펄스 → "에너지 크래클" 느낌.
- Z 오프셋 `lineZOffset = -0.5f` 로 2.5D 지형 앞에 그림.
- (이전 버전에서 시도한 3-line halo/glow/core 스택은 알파 0.18~0.45 가 chargeGlow 파티클에 묻혀 보이지 않아 single-line 으로 단순화. 글로우는 두께·밝기·길이 펄스로 표현.)

## 텐드릴 path 생성 (Branching 모드 — 곡선 + 지면 강하)
각 텐드릴은 4개의 컨트롤 포인트로 정의된 cubic Bezier 곡선을 따라 그려져 슬라임의 **과거 위치 트레일** 위에 차례차례 떨어짐 ("뒤쫓아 오는" 체이스 느낌).

```
P0 = lockedOrigin (muzzle 위치)                   ← 모든 텐드릴이 머즐에서 직접 출발
P1 = origin + dir(조준축 ± launchSpreadDegrees) * tendrilLaunchDistance    ← 측면 발사 — 머즐에서 부채꼴로 흩어짐
P2 = endpoint + Vector2.up * bendHeight           ← 위로 휘었다가 떨어지도록 끌어올리는 anchor
P3 = SampleGroundEndpoint(trailSnapshot[i])       ← FireStart 시 스냅샷된 트레일 샘플 i를 중심으로
                                                    [-targetRadius, +targetRadius] 측면 오프셋 후 지면 스냅

segments = clamp(round(controlPolygonLength / tendrilSegmentLength), 6, 48)
            ← 텐드릴별로 동적 결정. 거리 멀수록 정점 ↑, 짧으면 ↓.
baseSamples[i] = CubicBezier(P0, P1, P2, P3, t)
points[0]      = baseSamples[0]
points[N-1]    = baseSamples[N-1]
points[i]      = baseSamples[i] + perp * Random(-jaggedness, jaggedness) * sin(t·π) * avgSegLen·1.5
```

- `sin(t·π)` taper로 양 끝은 매끈, 중간만 kink → 번개 실루엣.
- `groundSnapMask` 가 0 이면 `bulletHitMask` 로 fallback. 슬라임 자체에 스냅하지 않으려면 Environment 레이어만 지정 권장.
- 트렁크 없음 — 머즐 P0 에서 N개의 텐드릴이 각자 P1 lateral 발산으로 펼쳐지는 구조.

### Trail-tracking (체이스 효과 + 라이브 추적)
- 매 `Update` 마다 `trailSampleInterval` (기본 0.04 s) 간격으로 `target.position` 을 `trailBuffer` 에 기록. (`trailDuration + trailLagOffset`) 보다 오래된 샘플은 폐기.
- 각 텐드릴 i 는 `lagFraction = 1 - i/(N-1)` 를 보유. **i=0 (먼저 스폰) → lagFraction=1 (가장 과거), i=N-1 (마지막 스폰) → lagFraction=0 (가장 최근)**.
- 텐드릴 P3 (지면 강하 지점) 는 **스폰 시점에** `QueryTrailAt(Time.time - (trailLagOffset + lagFraction * trailDuration))` 으로 실시간 결정. 즉 PreFire 이후 (FireStart/Firing 단계에서) 도 아직 안 나간 텐드릴들은 슬라임의 새 위치를 따라감.
- `trailLagOffset` (기본 0): 가장 최근 텐드릴조차 슬라임이 N초 전에 있던 위치를 노림. 0 이면 최신 텐드릴은 라이브 위치, 0.3 이면 항상 0.3초 뒤처져서 박힘.
- `trailDuration` (기본 0.6): 체이스 스프레드. 가장 오래된 텐드릴은 `trailLagOffset + trailDuration` 초 전 위치를 노림.
- 두 값 모두 0 이면 모든 텐드릴이 슬라임의 라이브 위치를 노리는 라이브 추적 모드.
- 텔레그래프(PreFire) 의 `lockedDir` 은 PreFire 시작 시점에 고정 — 텐드릴 머즐 출발 방향(P1) 만 영향. 끝점(P3) 은 항상 라이브 트레일 쿼리 기반.

## 발사체 — Burst 모드 (`GraniteBlastBullet`)
- `Rigidbody2D` (Kinematic) + `CircleCollider2D` (trigger).
- 매 `FixedUpdate`: `rb.position = rb.position + dir * speed * dt` (프로젝트 룰: `transform.position` 대신 `rb.position`).
- 빠른 속도에서 `Collider2D.Cast`로 스윕 → 얇은 벽 터널링 방지.
- 비주얼/콜라이더 분리: 비주얼 (`Head` SpriteRenderer + `TrailRenderer`)은 `Visual` 자식 트랜스폼 위. 회전은 비주얼만, 콜라이더는 회전하지 않음.
- 명중/수명 만료 시 트레일을 detach 후 `autodestruct=true`로 자연 페이드.

## 피드백 (모두 옵셔널)
- 탐지: `detectionIndicator` 활성 + `detectSound`.
- 차징: `chargeGlow` ParticleSystem 스케일링 + `chargeSound`.
- 발사: `muzzleFlash` 버스트 + `fireSound`.
- 임팩트 VFX: `impactPrefab` 을 `Instantiate + Destroy(duration + lifetime + 0.2)` 로 자동 해제.
- **임팩트 SFX**: `groundImpactSound` 가 각 라인 프로젝타일이 지면에 닿을 때 1회 재생됨.
  - Branching: 텐드릴 풀 드로우 시점 (`drawT >= 1`) 에 P3 endpoint 위치에서 재생 → 텐드릴 N개 = N번.
  - Beam: 첫 프레임 ground 차폐 감지 시 hitPoint 에서 1회.
  - Burst: 각 GraniteBlastBeam flash 가 동일 로직으로 1회씩 → flash 수만큼.
  - `groundImpactPitchJitter` (기본 0.1) 로 ±10% pitch 랜덤화 → 다중 발생 시 단조로움 방지.
- 오디오 라우팅: `SoundService.PlayAt(clip, worldPos, SFX_Object, volume, pitch)`.

## Inspector 표시
`GraniteBlastEmitterEditor` (`Assets/Scripts/Editor/`) 가 [Header] attribute 를 reflection 으로 읽어 현재 `mode` 와 무관한 모드 섹션 (Branching/Beam/Burst) 을 자동으로 숨김. 새 필드 추가 시 동일 헤더 아래에 두면 자동 분류됨.

## 인스펙터 파라미터 요약
| Header | 키 |
|--------|------|
| Mode | `mode` |
| Target | `target`, `detectionRange`, `lineOfSightBlockers` |
| Muzzle | `muzzle`, `aimAtTarget` (true=live target 추적, false=muzzle.right) |
| Phase Timing | `detectionDelay`, `preFireDuration`, `fireStartDuration`, `firingDuration`, `postFireDuration`, `cooldownDuration`, `loseSightGrace` |
| Hit Mask | `maxRange`, `bulletHitMask`, `groundSnapMask` (Branching 텐드릴 endpoint 지면 스냅), `hitRadius` (CircleCast 반경 — 모든 모드 공통) |
| Knockback | `knockbackImpulse` (one-shot deltaV m/s), `knockbackAcceleration` (sustained deltaV/s² m/s²), `sustainedHit` (Beam 한정) — 2026-04-29 stage2 개편 후 stunDuration 제거됨 |
| Firing Mode Control | `autoFire` (false 면 외부 `FireOnce()` 호출에서만 발사) |
| Branching | `tendrilCount`, `targetRadius`, `launchSpreadDegrees`, `tendrilLaunchDistance`, `bendHeight`, `tendrilSegmentLength` (path-length 기반 동적 세그먼트), `tendrilJaggedness`, `tendrilDrawTime / Hold / Fade`, `tendrilCoreWidth`, `branchingFireStartFraction`, `trailDuration` (체이스 스프레드 sec), `trailLagOffset` (최신 텐드릴의 기본 랙 sec), `trailSampleInterval` |
| Beam | `beamFadeTime`, `beamCoreWidth` (lifetime은 fireStart+firing+postFire) |
| Burst | `burstFlashCount`, `burstSpreadDegrees`, `burstFlashDuration`, `burstFlashFadeTime`, `burstFlashCoreWidth`, `burstFireStartFraction` (batch 모드), `burstFlashInterval` (>0 → 한 발씩 N초 간격으로 순차 발사. fraction 무시) |
| Visual Colors | `coreColor`, `lineZOffset` |
| Telegraph | `showPreFireTelegraph`, `telegraphWidth`, `telegraphColor` |
| Audio | `detectSound / preFireSound / fireStartSound / postFireSound / groundImpactSound` + 각 볼륨, `groundImpactPitchJitter` |
| VFX | `chargeGlow`, `muzzleFlash`, `impactPrefab`, `detectionIndicator` |

## 레퍼런스
- 분기 모드 시각 레퍼런스: 사용자 제공 이미지 (Granite Blast 분기 추적탄 형태).
- 출처: [Granite Blast — Jujutsu Kaisen Wiki](https://jujutsu-kaisen.fandom.com/wiki/Granite_Blast).

## 관련 파일
- [GraniteBlastEmitter.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastEmitter.cs) — Core (FSM, public API, 모든 SerializeField, Start/Update/transitions)
- [GraniteBlastEmitter.Targeting.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastEmitter.Targeting.cs) — partial: aim 해석, LOS, raycast/circlecast, slime trail
- [GraniteBlastEmitter.Telegraph.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastEmitter.Telegraph.cs) — partial: PreFire telegraph LineRenderer
- [GraniteBlastEmitter.Branching.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastEmitter.Branching.cs) — partial: PlanBranching/SpawnTendrilsUpTo + NavGraph A* 분기
- [GraniteBlastEmitter.Beam.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastEmitter.Beam.cs) — partial: SpawnBeam
- [GraniteBlastEmitter.Burst.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastEmitter.Burst.cs) — partial: PlanBurst/SpawnBulletsUpTo
- [GraniteBlastEmitter.Impact.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastEmitter.Impact.cs) — partial: PlayGroundImpactSound, SpawnGroundExplosion, GetCoreMaterial, OnDrawGizmosSelected
- [GraniteBlastTendril.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastTendril.cs) — Bezier `Init` + 폴리라인 `InitPath` (NavGraph A* 결과)
- [GraniteBlastBeam.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastBeam.cs) — Beam 모드 + Burst flash 양쪽에서 사용. `sustainedHit` 분기 포함.
- [GraniteBlastNavGraph.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastNavGraph.cs) — 디자이너 노드/엣지 + A*
- [GraniteBlastTrigger.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastTrigger.cs) — 트리거 진입 시 `FireOnce()`
- [SlimeBurnVisual.cs](../../../SlimeJumping_v2/Assets/Scripts/Visual/SlimeBurnVisual.cs) — 화상 색 + 막혔을 때 압축 시각
- [GraniteBlastTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/GraniteBlastTestSetup.cs) — `SlimeJumping/Mechanism/Setup Granite Blast Test`

## Stage 2 개편 (2026-04-29)

Stage 2 기믹 사용을 위해 다음과 같이 확장됨:

### 1. partial class 분할
`GraniteBlastEmitter` 가 7개 partial 파일로 분할 (위 "관련 파일" 참고). 모든 SerializeField 와 FSM 은 Core 에 남고, 모드별 로직 / 타겟팅 / 텔레그래프 / 임팩트는 sibling partial 로 이동. Inspector 동작은 그대로 (`GraniteBlastEmitterEditor` reflection 기반이라 partial 분할 영향 없음).

### 2. 외부 트리거 발사
- `[Header("Firing Mode Control")] autoFire` — false 면 자동 감지 비활성, `FireOnce()` 외부 호출만으로 발사.
- `public bool FireOnce()` — Idle/Cooldown 상태에서 PreFire 로 진입 (preFireDuration 텔레그래프 정상 재생). Firing 중이면 거부 (false 반환).
- `GraniteBlastTrigger` 컴포넌트 — 트리거 콜라이더 진입 시 슬라임 감지 → `FireOnce()`. `allowRetrigger=false` 기본 (1회성), `retriggerCooldown` 으로 반복 fire 가능.
- 사용 패턴: 큰 1회성 레이저 이벤트 (특정 장소 진입 시 전조 + 발사) → emitter 의 `autoFire=false` + `GraniteBlastTrigger` 1개 배치.

### 3. KB 채널 (stun 제거)
- 기존 `HazardEffect / ApplyHazardEffect` 경로 제거. `IHazardSource` 인터페이스도 더 이상 implement 하지 않음.
- 새 `SlimeController.ApplyHazardPush(Vector2 deltaV)` 가 `AddExternalVelocity` 채널로 누적하면서 `OnHazardPushed` 이벤트 발화. SlimeBurnVisual 이 구독.
- emitter 인스펙터: `knockbackImpulse` (one-shot, 단발 hit), `knockbackAcceleration` (sustained, m/s²), `sustainedHit` (Beam 한정 토글).
- 결과: 슬라임은 stun 없이 그저 밀림 — 격투게임 zoning 게임플레이 가능. 하지만 가속도 채널이 누적되므로 sustained 빔에 오래 노출되면 큰 push 가 됨.

### 4. Beam sustainedHit 모드
- `sustainedHit=true` 시 빔이 매 프레임 muzzle 부터 maxRange 까지 재-sweep, 슬라임이 path 위에 있으면 그 자리에 halt + `knockbackAcceleration * dt` 인가. 슬라임이 빠져나가면 다음 차폐물까지 통과.
- 기존 `sustainedHit=false` 빔은 첫 hit 에서 halt + impulse 1회 (legacy 동작 유지).
- ground impact VFX/SFX 는 첫 ground 접촉에서 1회만 발화 (`groundImpactPlayed` 가드).

### 5. Z 축 path 지원
- muzzle Z != 0: 이미 자동 지원. `transform.position.z` 가 매 spawn 마다 라이브로 읽혀 P0/P3 기준 Z 가 됨.
- 새 인스펙터 필드:
  - `bowDepthZ` (Branching, Z 방향 중간 부풀림. >0 = 카메라 쪽으로, <0 = 뒤쪽으로)
  - `controlPointZJitter` (Branching, ±units 의 P1/P2 Z 노이즈 — 텐드릴마다 깊이가 흔들리도록)
- `GraniteBlastTendril.BuildCurvedJaggedPath` 가 더 이상 baseZ 한 평면으로 강제 평탄화하지 않음 — 베지어 샘플의 자체 Z 를 보존, perp jitter 는 XY-only.
- Beam/Burst 는 여전히 직선 (Z 적용 시 그릴 라인이 곡선이 되어야 해서 큰 변경 필요 — 추후 작업).
- 충돌 검출은 그대로 `Physics2D.CircleCast` (XY 평면 투영). Z 는 시각 효과만.

### 6. NavGraph A* 경로
- `GraniteBlastNavGraph` 컴포넌트: 디자이너가 Vector3 노드 + (a,b) 엣지 인덱스 페어를 인스펙터에 배치. local-space 저장 (zone 이동 시 따라옴). `OnDrawGizmos` 로 노드/엣지 시각화.
- 내부 구현: 바이너리 min-heap, 스크래치 버퍼 재사용, 작은 그래프(<100 node) 기준 발사당 alloc 거의 0.
- emitter 인스펙터의 `[Header("Branching Mode")] navGraph` 슬롯에 wire 시 활성화. null 이면 기존 직선 발사.

#### LOS-aware 라우팅 게이트 (2026-04-30)
NavGraph 가 켜져있어도 무조건 그래프를 통과하지 않음 — 3-way 게이트:

1. **Direct LOS**: `IsDirectPathClear(muzzle, slime, navWallMask)` 통과 시 (`Physics2D.Linecast` 가 벽을 못 만남) → NavGraph 스킵, 베지어 직선 발사. 벽 없는 일반 케이스에서 NavGraph 가 안 끼어들어 유도성 유지.
2. **Routed**: 직선이 막혀 있고 `FindPath` 가 LOS-clear entry/exit 노드를 찾으면 → 폴리라인 텐드릴.
3. **Fallback**: 직선 막힘 + 깨끗한 entry/exit 노드 없음 (또는 그래프 단절) → 베지어 폴백.

핵심 로직:
- `ClosestVisibleNode(query, wallMask)` 가 노드 순회하면서 query → 노드 사이에 `Physics2D.Linecast(wallMask)` 통과한 노드만 후보로 채택, 그 중 거리 최소를 선택. 모두 차단되면 -1 반환.
- `wallMask = emitter.NavWallMask` (default = Environment 레이어 7). 0 이면 LOS 게이트 비활성 (legacy Euclidean nearest 동작).
- Slime 레이어 (6) 는 wallMask 에 포함하면 안 됨 — slime in path 는 정상 (보트가 슬라임 맞으면 됨), 벽만 차단해야 함. default 가 Environment-only 인 이유.
- A* edge 자체는 런타임에 LOS 검사 안 함 (디자이너가 손수 깐 edge 신뢰). 디자이너 실수는 아래 edge validation 으로 편집 시 잡음.

#### Edit-time edge validation (2026-04-30)
- NavGraph 인스펙터의 `edgeValidationMask` (default = Environment) 로 `OnDrawGizmos` 마다 각 edge 에 `Physics2D.Linecast` 검사. 차단된 edge 는 `blockedEdgeColor` (빨간색) 로 그려짐 → 디자이너가 즉시 발견.
- 캐시는 `OnValidate` 에서 dirty 표시 후 다음 gizmo draw 시 재계산. dirty 가 아니면 raycast 안 도림.
- 런타임에는 안 읽힘 — 순수 authoring aid. (런타임은 위 LOS-aware FindPath 가 안전망)
- 텐드릴은 `InitPath` (폴리라인) 경로로 생성 — 베지어 대신 waypoint 따라 `tendrilSegmentLength` 단위로 sub-sample, 동일한 perp jaggedness 적용. travelSpeed 는 폴리라인 전체 길이 기준이라 길수록 오래 걸림.
- Beam/Burst 는 NavGraph 미지원 (직선 빔이 그래프 따라 휘는 시각이 어색해서 의도적으로 제외).
- 사용 패턴: 한 zone 의 슬라임 동선 위에 노드를 깔고 emitter 를 zone 입구에 두면 레이저가 zone 의 안전 라인을 따라 슬라임을 추격.

### 7. groundExplosion VFX
- `[Header("VFX")] groundExplosionPrefab` (ParticleSystem) — 지면 hit 일 때만 인스턴스화 (슬라임 hit 은 `impactPrefab` 작은 spark 만). 폭발 반경/AoE 는 없음 — 순수 VFX.
- Tendril/Beam 둘 다 슬라임 vs 환경 분기에서 `owner.SpawnGroundExplosion` 호출. 슬라임 hit 시에는 호출 안 함.

### 8. SlimeBurnVisual (피격 표현)
- 슬라임 GO 에 부착. `SlimeController.OnHazardPushed (Vector2 deltaV, Vector2 hitPoint)` 구독.
- **화상 자국 (OS-rest decal array)** [2026-04-30 redesign — 셸 + world-space half-space 방식 폐기]: 별도 셸 GO/셰이더 없이, 본체 [SlimeBody_Quibli.shader](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeBody_Quibli.shader) (실제 렌더 경로) 와 fallback [SlimeBody3D.shader](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeBody3D.shader) 가 직접 burn 을 그림. 공통 로직은 [SlimeBurn.hlsl](../../../SlimeJumping_v2/Assets/Art/Shader/SlimeBurn.hlsl) include 1개. 각 hit 은 OS-rest 좌표의 `Vector4` (`xyz` = 메시 rest-pose 오브젝트 공간 hit 위치, `w` = `radius * fade`) 로 저장 — 최대 24개 슬롯, 가장 오래된 것 evict. Quibli 본체 셰이더는 SnowField Phase 3 와 동일한 wrapper 패턴: `SlimeBurnVaryings { Quibli::Varyings v; float3 osPosRest : TEXCOORD9 }`, 신규 `SlimeBurnPassVertex` 가 `SLIME_APPLY_DEFORM` *이전에* `input.positionOS.xyz` 를 `osPosRest` 로 캡쳐 후 stock `LitPassVertex` 호출, 신규 `SlimeBurnPassFragment` 가 stock `StylizedPassFragment(i.v)` 결과에 `SampleBurn(osPosRest)` 마스크 post-blend (`col.rgb = lerp(col.rgb, 0, burnMask)`). OccludedSilhouette 패스는 wrapper 없이 `OccVaryings.osPosRest : TEXCOORD2` 직접 추가 + fillRGB 에 동일 lerp.
- **표면 부착 보장**: rest-pose OS 는 vertex shader 의 squash/stretch/dip/asymmetric squash 어떤 단계에서도 다시 쓰이지 않음 (rasterizer 가 원본 mesh-asset triangle 의 barycentric 으로 보간). 따라서 hit 이후 슬라임이 점프, 표면 정렬 회전, 비대칭 수축, 차징 dip, velocity stretch 어떤 변형을 겪어도 burn 디스크는 항상 *맞은 표면 패치* 위에 계속 붙어 있음 — 미끄러짐/잘림 현상 모두 해소.
- **누적 state**: `List<Burn>` (`osPos`, `radius`, `bornTime`). 새 hit 이 기존 burn 의 `mergeDistance` (기본 0.18 OS 단위) 안에 들어오면 슬롯 추가 대신 해당 burn 의 `bornTime` 갱신 + 반경을 `burnRadius * 1.4` 까지 bump → 같은 자리 반복 피격 시 슬롯 낭비 없이 더 진하고 넓어짐.
- **회복 (per-burn fade)**: 각 burn 은 `recoveryStartDelay` (기본 2.5s) 동안 full radius 유지 후 `recoveryDuration` (기본 2.5s) 동안 선형으로 radius 0 까지 감쇠. 각 burn 이 독립 타이머라 과거 hit 부터 자연스럽게 사라짐 — 이전 "전체 동시 fade" 대비 시각이 자연스러워짐.
- **MPB push 흐름**: SlimeBurnVisual 은 body MeshRenderer 의 `GetPropertyBlock` → `SetVectorArray("_Burns", burnPacked)` + `SetInt("_BurnCount", n)` + `SetFloat("_BurnStrength", 1)` → `SetPropertyBlock` 을 매 LateUpdate 에서 수행. `[DefaultExecutionOrder(100)]` 로 SlimeVisual.LateUpdate (default 0) *이후*에 돌아 SlimeVisual 이 같은 MPB 에 푼 deform 값을 GetPropertyBlock 이 보존 → burn 데이터가 그 위에 layered. `burnPacked` 배열은 24 크기로 한 번 할당 (Unity 가 첫 SetVectorArray 에서 길이를 잠그므로 이후 resize 무효).
- **셰이더 SRP-batcher 영향**: `_Burns[24]` / `_BurnCount` / `_BurnStrength` 는 `UnityPerMaterial` CBUFFER *밖*에 선언 (uniform array 가 CBUFFER 안에 있으면 SRP batcher 가 막힘). 슬라임 인스턴스가 1개라 배치 손실은 무시 가능. ForwardLit + OccludedSilhouette 두 패스 모두 `osPosRest` varying + `SampleBurn()` 적용; ShadowCaster 는 검정 위에 burn 안 보이므로 스킵.
- **막혔을 때 압축** (기존 그대로): rb.position 슬라이딩 윈도우 (0.18s) 안에서 슬라임이 `stuckPositionEpsilon` 미만 이동 + 최근 `pushReceiveWindow` 내 hit 이면 → `SlimeVisual.SetExternalCompression(compressionPeak)` 펌프.
- 폐기: `SlimeBurnMark.cs` (이전 cluster decal 컴포넌트), `SlimeBurnShell.shader` (셸 셰이더 — 2026-04-30 redesign 시 삭제), `SlimeVisual.RegisterMirroredRenderer` / `UnregisterMirroredRenderer` / `MirrorMpbToRegistered` (셸 전용 미러 푸시 API + 호출처 3곳 — 다른 소비자가 없어 함께 삭제), 글로벌 sprite tint, 절차적 scorch 텍스처.
- 신규 helper: `SlimeVisual.Body / Body3d` public Transform 게터 — SlimeBurnVisual Awake 에서 body3d 우선, body 폴백, 그 자식 MeshFilter 의 `transform.InverseTransformPoint` 로 world hit → rest-pose OS 변환.

### 9. 인스펙터 신규 필드 요약
| Header | 신규 필드 |
|--------|----------|
| Firing Mode Control | `autoFire` |
| Knockback | `knockbackImpulse`, `knockbackAcceleration`, `sustainedHit` |
| Branching Mode | `navGraph`, `navWallMask` (Environment 기본), `bowDepthZ`, `controlPointZJitter` |
| VFX | `groundExplosionPrefab` |

NavGraph 인스펙터에도 `edgeValidationMask` (default Environment), `blockedEdgeColor` (빨간색) 추가됨.

### 10. 제거된 필드
- `knockbackForce`, `stunDuration` — `IHazardSource.GetHazardEffect()` 까지 함께 삭제.
