# SlimeJumping — 핵심 메커닉 설계 v6 (인덱스)

> **문서 분할 (2026-04-24):** 기능별로 세분화하여 [features/](features/) 아래로 재구성. 이 파일은 목차 + 링크 허브 역할.
> 설계와 구현 현황이 각 기능 파일에 통합됨.
>
> **메커닉 정리 (2026-05-11):** "조작감 실험" 잔재 제거 리팩토링. 다음 기능을 코드에서 **완전 삭제**: Ground Dash (A/D 더블탭), Shift Air Dash (`SlimeAirDash`/`SlimeAirDashVisual` 컴포넌트 포함), Hop (W 표면 점프 + 모멘텀 + hop 표면 머티리얼 필드 7종 + `launchedByHop`/`hopConversionBonus`/`hopConversionEnergyBonus`), Volume Charging (모래 위 표면-없는 차징), WASD 표면 좌우 이동(crawl input — `crawlVelocity` 채널은 착지 관성 운반·감쇠 용도로만 유지). 또한 dormant 설정 정리: `cornerBigSpikeAngle`/`cornerDetachSustainFrames` (옛 friction-detach 공식 잔재), SurfaceProbe Phase A/B anti-stutter 토글 6종 (전부 OFF였음). `frictionDetachFormula` enum 4종 + switch, surfaceProbe Single/Multi 자동 스위칭, Energy-Conversion Coyote, GravityField, Rope 시스템, `hyperDashInertiaSource` enum, arc-compensation, surfaceNormal 평활화는 **유지**. 표면 이동 수단은 이제 "중력 슬라이드" + "차지 슬링샷" 둘뿐. 이어서 동작-무변경 dedup 추출: `ComputeSlideSpeed`/`UpdateStickTransition`(Attached↔Charging 공유), `CaptureLandingImpact`/`RouteLandedTangent`/`ComputeEnergyConversionSpawnDistance` + `EnterChargingFromLanding`/`AttachFromLanding`(Airborne 착지 두 분기 중복 제거), `RegisterLaunchClearance`/`LaunchPowerForRatio`. 마지막으로 거대 메서드 분리(동작 무변경): `FixedUpdateAttached`의 ~170줄 마찰-이탈 블록 → `CheckFrictionDetach(dt)`; `FixedUpdate` → `UpdateGravityDirection`/`UpdateSurfaceContact`/`ApplyExternalDeltaV`/`OutputVelocity` 4단계 + state dispatch.

## 컨셉

**점성 슬라임** — 모든 표면에 달라붙는 것이 자연 상태. 이동은 크롤링과 차징 발사로 수행.
WASD + Space 키로 조작. 모든 시간 단위는 **초(seconds)** 기준.

**장르 지향**: 점프킹, Egging On, 셀레스트, 구구피자 계열의 정밀 플랫포머.
조작 반응성과 예측 가능성이 최우선. 소프트바디 비주얼은 게임플레이에 영향을 주지 않음.

## 구현 아키텍처 — 2단계 분리

- **Phase 1** (완료): 단일 원형 콜라이더 플랫포머. 충돌, 이동, 차징, 발사, 에어 컨트롤 — 전부 원 하나.
- **Phase 1.5** (완료): 절차적 변형 비주얼 — 코드 기반 squash/stretch. 물리와 완전 분리. 상세 [features/slime/visual-deformation.md](features/slime/visual-deformation.md).
- **Phase 2** (미구현): 소프트바디 비주얼 오버레이 — 중심 + 6개 노드 스프링. Phase 1.5를 교체 가능한 장기 옵션.

## 슬라임 컨트롤러

- [State Machine](features/slime/state-machine.md) — 4상태 + ChangeState + IExternalController override
- [Surface Attach & Detection](features/slime/surface-attach.md) — Primary + Constraints, 2-Phase Depenetration *(2026-06-01: 천장 점프 — 천장은 영구 Attach 안 하지만 charge 버퍼/홀드 시 잠깐 charge 진입 → 아래로 내리꽂기 발사 가능)*
- [Crawling & Slide](features/slime/crawling.md) — 접선 이동 + 중력 분해 *(2026-05-11: WASD crawl 입력 제거 — `crawlVelocity` 채널은 착지 관성 운반·감쇠 전용)*
- ~~Ground Dash~~ *(2026-05-11 제거 — A/D 더블탭 순간 부스트, 코드 전체 삭제)*
- ~~Shift Dash~~ *(2026-05-11 제거 — `SlimeAirDash`/`SlimeAirDashVisual` 컴포넌트 포함 삭제)*
- ~~Hop~~ *(2026-05-11 제거 — W 표면 점프 + 모멘텀, hop 표면 머티리얼 필드 포함 삭제)*
- [Hyper Dash](features/slime/hyper-dash.md) — Charge → Launch 시 접선 관성 상속 (`hyperDashInertiaSource` enum 유지)
- [Stick Timer](features/slime/stick-timer.md) — 착지 고정 + Charge Entry Grace
- [Attach Debounce](features/slime/attach-debounce.md) — 코너 오실레이션 방지
- [Charge & Launch](features/slime/charge-launch.md) — 판정원 모델, 발사 공식 *(천장 점프 진입 경로 포함 — 천장에서 charge → 아래 발사)*
- [Energy Conversion](features/slime/energy-conversion.md) — 확장 판정원, 에너지 보존 공식
- [Airborne](features/slime/airborne.md) — 중력, 에어컨트롤, 착지 분기
- [Stunned](features/slime/stunned.md) — 기절 상태, HazardEffect
- [Surface Material](features/slime/surface-material.md) — 표면별 AngleScaledValue 배율 (포함: `landingExcessGain` — charged-launch 착지의 누적 가속을 표면 단위로 제어)
- [Visual Deformation](features/slime/visual-deformation.md) — Phase 1.5 절차적 변형 (C-1 ~ C-7)
- [Friction Detach](features/slime/friction-detach.md) — 원심/Horizon 기반 detach (formula 4종 + 발 아래 raycast, banana-bend 셰이더 stage 6, Phase 1/2/3 visual)

## 플랫폼

- [Common (PlatformMotor / Carrier)](features/platforms/common-platform.md)
- [Waypoint v2](features/platforms/waypoint-v2.md) — 사각형. SmoothDamp 댐핑 follow(탑승 시 더 묵직) + 수직 sag 흔들림/반동 + 가장자리 틸트 + 얼음(`OverrideSurfaceMaterial`) + idle-tip로 중심 불안정화해 가만히 있으면 미끄러져 떨어짐(재점프 강제). F1 = 탑승 트리거 1회 왕복 + 도착 범위 유지/이탈 시 복귀(ThinIce식). F2 = leader/partner 샌드위치, 압착 시 기존 depenetration + `AddExternalVelocity`로 슬라임을 옆으로 부드럽게 압출(SlimeController 미수정).
- [Crumbling](features/platforms/crumbling.md) — 무너지는 플랫폼
- [Thin Ice](features/platforms/thin-ice.md) — Crumbling 확장 (SFX + 파티클 + 부드러운 재생성)
- [Thin Ice v2](features/platforms/thin-ice-v2.md) — Quibli toon lighting + 절차적 Voronoi 크랙 셰이더 + 공유 generic shard 라이브러리 + `ThinIceV2Config` 단일 설정 자산 (Resources auto-load) + 점프-즉시-shatter + 그룹 단위 respawn zone. U자처럼 여러 ThinIce child collider로 만든 표면도 별도 guide anchor 없이 기존 `SurfaceProbe` snap을 사용한다.
- [Rolling Disk](features/platforms/rolling-disk.md) — 360° 회전 원형
- [Pendulum](features/platforms/pendulum.md) — 매달린 진자
- [Springy Branch](features/platforms/springy-branch.md) — 탄력 나뭇가지
- [Bamboo](features/platforms/bamboo.md) — 수직 대나무. SpringyBranch 물리 식 + vertical 적용, N-segment collider polyline + single shader-bent cylinder visual (sub-pixel coincident). 기본적으로 visible node frequency를 physics `segmentCount`에 동기화해 보이는 마디/구간 경계가 물리 segment와 일치. 마디 bulge + sheath scar 지오메트리, Quibli-style 2-tone + height gradient color. `BambooSettings.enableTipGrab` / `enableSideReboundV2` 독립 bool 2개 (예전 상호배타 `interactionMode` enum 제거 — 둘 다 켜면 옆구리 V2 반동 + 꼭대기 tip-grab 동시 동작, 스프링 계수 우선순위 grab>v2>기본). V2는 측면 충돌을 더 크게 휘게 한 뒤 Space charging release 방향을 따라 보너스를 더한다. V2 launch-release 반작용은 전용 접촉 높이 보정 노브를 받아 아래쪽 점프는 덜 흔들리고 위쪽 점프는 더 크게 흔들린다. 대나무 하강 jitter 원인 추적용 `BambooSurfaceJitterDebugger`를 슬라임에 붙여 segment switch / normal jump / carry residual / probe churn을 관측한다.
- [Deformable Mud](features/platforms/deformable-mud.md) — 진흙/찰흙 소성 변형
- [Rope](features/platforms/rope.md) — Verlet + 반동 Launch (v1 = sag-based, [v2 = timing-based](features/platforms/rope-v2.md), [v3 = trampoline-coupled](features/platforms/rope-v3.md))
- [Piano Keyboard](features/platforms/piano-keyboard.md) — 연주 가능한 업라이트 피아노. 슬라임이 흰 건반 착지 시 속도 비례 dip + 댐핑 스프링 복귀(출렁) + 셈여림 적용 CC0 피아노 음 재생. 흰 건반만 collider(검은 건반 비주얼만), 건반별 독립 판정으로 seam 끼면 2건반 동시. Unity-MIDI-Piano FBX/CC0 샘플 추출, `PianoSettings` 그래프 튜닝(velocity→volume/depth, spring stiffness/damping). **하모나이즈 모드**(opt-in `PianoConfig.harmonize` — 착지마다 코드 진행 전진 + 착지 위치=멜로디 스냅 + `PianoVoicePool` 화음 + velocity density, 끄면 원음 그대로). authored 스키마 7종×4코드(장조 4 + 나란한단조 2 + 차용 1), 확률은 **루프 경계에서만** 굴려 다음 스키마 선택 → 루프마다 ~50%로 장↔단 전환. **연주 장식 11종**(롤드 코드·보이스리딩·페달포인트·알베르티·칩튠 아르페지오·아포자투라·서스펜션·Drop-2·서스테인 페달 연동·심퍼세틱 레조넌스·글리산도 런, 전부 개별 토글, 모든 장식의 첫 음은 착지 즉시). **건반 시각은 울린 음마다 1:1** — 발음 이벤트가 88건반(검은 건반 포함)을 그 타이밍에 누름. *자동 반주는 2회 시도 후 전량 폐기(2026-07-23).*

## 메커니즘

- [Categories](features/mechanisms/mechanism-categories.md) — 6분류 (환경존/위험/플랫폼효과/인터랙터블/오버라이드/소프트피직스)
- [Portal](features/mechanisms/portal.md) — 텔레포터 쌍 + PortalSwapper (v2: 소비/재생성, 자동 페어링, 색 지정)
- [Signal / Button](features/mechanisms/signal-button.md) — SignalSource, WeightDescendPlatform
- [Wind Zone](features/mechanisms/wind-zone.md) — v1 (slime physics push), v2 (= v1 + Snow ParticleSystemForceField 연동)
- [Snowstorm](features/mechanisms/snowstorm.md) — 스테이지 전체 눈폭풍 (기본 Jump King 4-페이즈 사이클: WindLeft → CalmAfterLeft → WindRight → CalmAfterRight, 구간별 `sections` override로 약한 상시 좌풍/무풍 눈 연출 등 지정 가능, `windShelterAreas`로 실내·벽 뒤처럼 슬라임 push만 차단, 짧은 lerp ramp 전환, WindZone2D_v2 동일 슬라임 push, 카메라 앵커 Local sim PS의 전체 화면 볼륨 스폰으로 viewport 전 영역 커버 [rate 변화가 화면 전역에 균일 반영 — strip 스폰의 "위에서 띠로 내려오는" 어색함 제거], 입자 외형·낙하속도를 스테이지 눈과 통일 + suppressedSnowParticles cross-fade로 "같은 눈이 거세지는" 경계 연출, slimePresenceWeight 통합 fade로 zone in/out 시 모든 비주얼 [particles + URP Volume vignette/cool-tint/WhiteBalance + ambient loop] 동기 fade in/out, calm 시 emission baseline 유지로 "바람만 멈추고 눈은 계속" 표현)
- [Drag Zone](features/mechanisms/drag-zone.md) — 감속/모래 + 볼륨 차징
- [Gravity Field](features/mechanisms/gravity-field.md) — 중력 방향 치환
- [Water Zone](features/mechanisms/water-zone.md) — 물 v1/v2 + 파도 + Underwater 포스트
- [Cold Zone](features/mechanisms/cold-zone.md) — Stage2 동결 비주얼 (슬라임 색 lerp + 표면 frost + warm/cold 듀얼 trail RT alpha-over)
- [Water Current Zone](features/mechanisms/water-current-zone.md) — 벽면 흐르는 물살 (centerline 리본 + 5-vert 반-튜브 단면 + 좌/우 채널 파도 시뮬 + Water_FlowingRefraction 셰이더)
- [Push Door v1/v2/v3](features/mechanisms/push-door.md) — 밀어서 여는 문 (v2: 살롱 도어 충격량 모델, v3: 표준 FBX 각도 모델)
- [Jump Pad](features/mechanisms/jump-pad.md)
- [Jump Ball](features/mechanisms/jump-ball.md) — 트리거 자동 점프, 보빙 비주얼
- [Hammer](features/mechanisms/hammer.md) — 폴대 꼭대기 피벗 좌우 진자 스윙 망치, `speedProfile` 곡선으로 속도 형상(끝에서 정지/최저점 최속) + 닿으면 머리 접선속도(`ω×반경`)로 발사
- [Conveyor Belt](features/mechanisms/conveyor-belt.md)
- [Spike Hazard](features/mechanisms/spike-hazard.md)
- [Saw/Knife Hazard](features/mechanisms/saw-knife-hazard.md) — SliceEffect + [Slime Split](features/mechanisms/slime-split.md)(잘림=작아진 둥근 슬라임 + 자크식 회수: collider.radius 실제 축소(BodyScale) · 상처=메니스커스 막이 림→중심 동심 봉합(스프링 차오름+타격 퀴버, 평면 후퇴 없음) · 손실 부피만큼 조각 사출(sliver→droplet→바닥 잔존, melt/얼룩 폐기) · 조각은 근접 시 자석 흡인→합체 벌지로 흡수 · heal=흡수 게이트(분실 시 느린 자연 회복) · 점프력=몸집 파생 · 지속 단면/클립 없음)
- [Turret Hazard](features/mechanisms/turret-hazard.md) — CCTV 확장 사격
- [Icicle Hazard](features/mechanisms/icicle-hazard.md) — 낙하 고드름 (진동 → 낙하 → 파괴)
- [Falling Train](features/mechanisms/falling-train.md) — 1회 한정 낙하 기차 (텔레그래프 → 연결부 분리 → 곡선 낙하 → 숏컷)
- [Snowball](features/mechanisms/snowball.md) — Stage 2 굴러가는 / 날아오는 눈덩이 (Rolling/Flying mode + Falling 서브상태 + ObjectPool 스포너 + [SnowField 상호작용](features/mechanisms/snowball.md#snowfield-상호작용-phase-9): 연속 carve, 거리 비례 반경 성장, 착지 splash 분화구+둔덕)
- [Granite Blast Attackable](features/mechanisms/granite-blast.md#12-attackable-슬라임이-닿으면-파괴되는-카운터) — 슬라임이 닿으면 BulbShatter warp + WorldTimeFreezeService 협력형 freeze 후 emitter 파괴되는 카운터
- [Tier 1 Interactables](features/mechanisms/tier1-interactables.md) — E키 (Sit, TV, Shrine 등)
- [Tier 1 Fullscreen Hazards](features/mechanisms/tier1-fullscreen-hazards.md) — Jumpscare, CameraFlash
- [Memo](features/mechanisms/memo.md) — 월드 스페이스 내레이션

## 최적화

- [OffscreenSleeper](features/optimization/offscreen-sleeper.md) — 화면 밖 idle 시뮬 비용 회수 (마커 + 매니저 + 결정 트리, 13종 후보)

## 인프라

- [Velocity Field Contract](features/infra/velocity-field-contract.md) — velocity/rb.linearVelocity/slide+crawl 계층
- [External Velocity Two-Channel](features/infra/external-velocity-two-channel.md) — AddExternal/Multiply, IPlatformEffect 라이프사이클
- [Teleport](features/infra/teleport.md) — TeleportTo 전체 리셋
- [Timer Architecture](features/infra/timer-architecture.md) — wall-clock vs fixedDelta, IgnoredColliderList 모드
- [Input Summary](features/infra/input-summary.md)
- [Trail (진행 트레일)](features/infra/trail.md) — 항아리 게임 스타일 영속 자국
- [VFX Infrastructure](features/infra/vfx-infra.md) — ImpulseEmitter, SliceEffect, Blur, Two-Ramp Y Blend, 옵션패널(Background Camera On/Off 포함)
- [Tuning Parameters](features/infra/tuning-parameters.md) — SlimeSettings 전체 목록
- [Script Structure](features/infra/script-structure.md) — 전체 스크립트 인벤토리

## 설계 원칙

1. **원 하나가 게임 전부**: 단일 원형 콜라이더로 모든 게임플레이. 소프트바디는 비주얼 오버레이.
2. **점성 슬라임**: 표면에 닿으면 붙는다. 바운스 없음. 운동량 전부 흡수.
3. **판정원이 파워와 방향을 동시 결정**: 조준점 위치 하나로 발사 벡터 결정. 별도 계수 없음.
4. **불안정함은 시간이 만든다**: 벽/경사 슬라이드로 차징 시간 제한. 안정도 시스템 없음.
5. **차징은 커밋**: 캔슬 불가. WASD로 발사까지 능동적 조절.
6. **사전 커밋 보상**: 에너지 전환은 부딪히기 전에 Space를 미리 누른 플레이어가 보상받음.
7. **조준 관성은 기하학적**: 가장자리에서 방향 전환이 어려움. 파라미터 불필요.
8. **플레이어 조절 중력**: W/S로 중력 스케일 조절. 에너지 전환은 에너지 보존 공식으로 정규화.
9. **마찰 해체**: 이동 속도, 관성, 미끄러짐을 독립 파라미터로 분리.
10. **시스템 분리**: 중력 → 접선 분해. 에어 컨트롤 → 방향 회전. 차징 → 판정원 내부. 드리프트 → 표면 법선. 각각 독립.
11. **상호작용 오브젝트 비주얼은 물리에서 유도**: Transform 이동, 수학 기반 변형, 스프링-댐퍼 3가지로만 표현. 외부 애니메이션 클립(rig+keyframe)은 AI 캐릭터, 배경 장식, 시네마틱에만.
