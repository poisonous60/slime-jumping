# SlimeJumping — 핵심 메커닉 설계 v6 (인덱스)

> **문서 분할 (2026-04-24):** 기능별로 세분화하여 [features/](features/) 아래로 재구성. 이 파일은 목차 + 링크 허브 역할.
> 설계와 구현 현황이 각 기능 파일에 통합됨.

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
- [Surface Attach & Detection](features/slime/surface-attach.md) — Primary + Constraints, 2-Phase Depenetration
- [Crawling & Slide](features/slime/crawling.md) — 접선 이동 + 중력 분해
- [Ground Dash](features/slime/ground-dash.md) — A/D 더블탭 순간 부스트
- [Shift Dash](features/slime/air-dash.md) — Shift 8방향 대시, Attached/Airborne (Celeste-style, 독립 컴포넌트)
- [Hop](features/slime/hop.md) — W 표면 점프 + 모멘텀
- [Hyper Dash](features/slime/hyper-dash.md) — Dash → Charge → Launch 콤보
- [Stick Timer](features/slime/stick-timer.md) — 착지 고정 + Charge Entry Grace
- [Attach Debounce](features/slime/attach-debounce.md) — 코너 오실레이션 방지
- [Charge & Launch](features/slime/charge-launch.md) — 판정원 모델, 발사 공식
- [Energy Conversion](features/slime/energy-conversion.md) — 확장 판정원, 에너지 보존 공식
- [Airborne](features/slime/airborne.md) — 중력, 에어컨트롤, 착지 분기
- [Stunned](features/slime/stunned.md) — 기절 상태, HazardEffect
- [Surface Material](features/slime/surface-material.md) — 표면별 AngleScaledValue 배율
- [Visual Deformation](features/slime/visual-deformation.md) — Phase 1.5 절차적 변형 (C-1 ~ C-7)

## 플랫폼

- [Common (PlatformMotor / Carrier)](features/platforms/common-platform.md)
- [Crumbling](features/platforms/crumbling.md) — 무너지는 플랫폼
- [Thin Ice](features/platforms/thin-ice.md) — Crumbling 확장 (SFX + 파티클 + 부드러운 재생성)
- [Rolling Disk](features/platforms/rolling-disk.md) — 360° 회전 원형
- [Pendulum](features/platforms/pendulum.md) — 매달린 진자
- [Springy Branch](features/platforms/springy-branch.md) — 탄력 나뭇가지
- [Deformable Mud](features/platforms/deformable-mud.md) — 진흙/찰흙 소성 변형
- [Rope](features/platforms/rope.md) — Verlet + 반동 Launch (v1 = sag-based, [v2 = timing-based](features/platforms/rope-v2.md), [v3 = trampoline-coupled](features/platforms/rope-v3.md))

## 메커니즘

- [Categories](features/mechanisms/mechanism-categories.md) — 6분류 (환경존/위험/플랫폼효과/인터랙터블/오버라이드/소프트피직스)
- [Portal](features/mechanisms/portal.md) — 텔레포터 쌍 + PortalSwapper (v2: 소비/재생성, 자동 페어링, 색 지정)
- [Signal / Button](features/mechanisms/signal-button.md) — SignalSource, WeightDescendPlatform
- [Wind Zone](features/mechanisms/wind-zone.md)
- [Drag Zone](features/mechanisms/drag-zone.md) — 감속/모래 + 볼륨 차징
- [Gravity Field](features/mechanisms/gravity-field.md) — 중력 방향 치환
- [Water Zone](features/mechanisms/water-zone.md) — 물 v1/v2 + 파도 + Underwater 포스트
- [Push Door v1/v2/v3](features/mechanisms/push-door.md) — 밀어서 여는 문 (v2: 살롱 도어 충격량 모델, v3: 표준 FBX 각도 모델)
- [Jump Pad](features/mechanisms/jump-pad.md)
- [Jump Ball](features/mechanisms/jump-ball.md) — 트리거 자동 점프, 보빙 비주얼
- [Conveyor Belt](features/mechanisms/conveyor-belt.md)
- [Spike Hazard](features/mechanisms/spike-hazard.md)
- [Saw/Knife Hazard](features/mechanisms/saw-knife-hazard.md) — SliceEffect
- [Turret Hazard](features/mechanisms/turret-hazard.md) — CCTV 확장 사격
- [Icicle Hazard](features/mechanisms/icicle-hazard.md) — 낙하 고드름 (진동 → 낙하 → 파괴)
- [Falling Train](features/mechanisms/falling-train.md) — 1회 한정 낙하 기차 (텔레그래프 → 연결부 분리 → 곡선 낙하 → 숏컷)
- [Granite Blast Attackable](features/mechanisms/granite-blast.md#12-attackable-슬라임이-닿으면-파괴되는-카운터) — 슬라임이 닿으면 BulbShatter warp + WorldTimeFreezeService 협력형 freeze 후 emitter 파괴되는 카운터
- [Tier 1 Interactables](features/mechanisms/tier1-interactables.md) — E키 (Sit, TV, Shrine 등)
- [Tier 1 Fullscreen Hazards](features/mechanisms/tier1-fullscreen-hazards.md) — Jumpscare, CameraFlash
- [Memo](features/mechanisms/memo.md) — 월드 스페이스 내레이션

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
