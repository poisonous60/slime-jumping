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
공통: `HazardEffect { knockbackForce, knockbackDirection = (slimePos − emitterMuzzlePos).normalized, stunDuration, DamageType.Lightning }` → `SlimeController.ApplyHazardEffect`. 슬라임은 항상 발사 주체로부터 멀어지는 방향으로 밀려남 (빔 진행 방향 아님 — 비스듬히 맞아도 표면에서 떼어내는 동작).

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
각 텐드릴은 4개의 컨트롤 포인트로 정의된 cubic Bezier 곡선을 따라 그려져 슬라임 주변 지면에 떨어짐 ("쏟아져 내리는" 느낌).

```
P0 = lockedOrigin (muzzle 위치)                   ← 모든 텐드릴이 머즐에서 직접 출발
P1 = origin + dir(조준축 ± launchSpreadDegrees) * tendrilLaunchDistance    ← 측면 발사 — 머즐에서 부채꼴로 흩어짐
P2 = endpoint + Vector2.up * bendHeight           ← 위로 휘었다가 떨어지도록 끌어올리는 anchor
P3 = SampleGroundEndpoint()                       ← target 주위 [-targetRadius, +targetRadius] 측면 오프셋,
                                                    Vector2.down 으로 groundSnapMask 레이캐스트해 지면 스냅

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
- 임팩트: `impactPrefab`을 `Instantiate + Destroy(duration + lifetime + 0.2)`로 자동 해제.
- 오디오: `SoundService.PlayAt(clip, muzzlePos, SFX_Object, volume)`로 믹서 라우팅.

## 인스펙터 파라미터 요약
| Header | 키 |
|--------|------|
| Mode | `mode` |
| Target | `target`, `detectionRange`, `lineOfSightBlockers` |
| Muzzle | `muzzle`, `aimAtTarget` (true=live target 추적, false=muzzle.right) |
| Phase Timing | `detectionDelay`, `preFireDuration`, `fireStartDuration`, `firingDuration`, `postFireDuration`, `cooldownDuration`, `loseSightGrace` |
| Hit Mask | `maxRange`, `bulletHitMask`, `groundSnapMask` (Branching 텐드릴 endpoint 지면 스냅), `hitRadius` (CircleCast 반경 — 모든 모드 공통) |
| Hazard | `knockbackForce`, `stunDuration` |
| Branching | `tendrilCount`, `targetRadius`, `launchSpreadDegrees`, `tendrilLaunchDistance`, `bendHeight`, `tendrilSegmentLength` (path-length 기반 동적 세그먼트), `tendrilJaggedness`, `tendrilDrawTime / Hold / Fade`, `tendrilCoreWidth`, `branchingFireStartFraction` |
| Beam | `beamFadeTime`, `beamCoreWidth` (lifetime은 fireStart+firing+postFire) |
| Burst | `burstFlashCount`, `burstSpreadDegrees`, `burstFlashDuration`, `burstFlashFadeTime`, `burstFlashCoreWidth`, `burstFireStartFraction` (batch 모드), `burstFlashInterval` (>0 → 한 발씩 N초 간격으로 순차 발사. fraction 무시) |
| Visual Colors | `coreColor`, `lineZOffset` |
| Telegraph | `showPreFireTelegraph`, `telegraphWidth`, `telegraphColor` |
| Audio | `detectSound / preFireSound / fireStartSound / postFireSound` + 볼륨 |
| VFX | `chargeGlow`, `muzzleFlash`, `impactPrefab`, `detectionIndicator` |

## 레퍼런스
- 분기 모드 시각 레퍼런스: 사용자 제공 이미지 (Granite Blast 분기 추적탄 형태).
- 출처: [Granite Blast — Jujutsu Kaisen Wiki](https://jujutsu-kaisen.fandom.com/wiki/Granite_Blast).

## 관련 파일
- [GraniteBlastEmitter.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastEmitter.cs)
- [GraniteBlastTendril.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastTendril.cs)
- [GraniteBlastBeam.cs](../../../SlimeJumping_v2/Assets/Scripts/Mechanisms/GraniteBlastBeam.cs) — Beam 모드 + Burst flash 양쪽에서 사용
- [GraniteBlastTestSetup.cs](../../../SlimeJumping_v2/Assets/Scripts/Editor/GraniteBlastTestSetup.cs) — `SlimeJumping/Mechanism/Setup Granite Blast Test`
