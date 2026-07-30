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
- [Waypoint v2](features/platforms/waypoint-v2.md) — 사각형. SmoothDamp 댐핑 follow(탑승 시 더 묵직, smoothTime 은 `smoothTimeBlendTime` 동안 블렌드 — 즉시 전환하면 발사/착지마다 위치 지연 빚을 몰아 갚아 데크가 튀고 멈칫) + 수직 sag 흔들림/반동 + 가장자리 틸트 + 얼음(`OverrideSurfaceMaterial`) + idle-tip로 중심 불안정화해 가만히 있으면 미끄러져 떨어짐(재점프 강제). F1 = 탑승 트리거 1회 왕복 + 도착 범위 유지/이탈 시 복귀(ThinIce식). F2 = leader/partner 샌드위치, 압착 시 기존 depenetration + `AddExternalVelocity`로 슬라임을 옆으로 부드럽게 압출(SlimeController 미수정).
- [Crumbling](features/platforms/crumbling.md) — 무너지는 플랫폼
- [Thin Ice](features/platforms/thin-ice.md) — Crumbling 확장 (SFX + 파티클 + 부드러운 재생성)
- [Thin Ice v2](features/platforms/thin-ice-v2.md) — Quibli toon lighting + 절차적 Voronoi 크랙 셰이더 + 공유 generic shard 라이브러리 + `ThinIceV2Config` 단일 설정 자산 (Resources auto-load) + 점프-즉시-shatter + 그룹 단위 respawn zone(Box 또는 Polygon collider — 판정 전부 실제 모양 기준). U자처럼 여러 ThinIce child collider로 만든 표면도 별도 guide anchor 없이 기존 `SurfaceProbe` snap을 사용한다.
- [Rolling Disk](features/platforms/rolling-disk.md) — 360° 회전 원형
- [Pendulum](features/platforms/pendulum.md) — 매달린 진자
- Tilt — 라이더 무게로 기울어지는 시소 판자. v1(`TiltPlatform`)은 라이더 위치에 비례한 **목표 각도로 수렴하는 스프링**이라 라이더가 중앙을 지나면 목표가 0 이 되어 즉시 복원한다 — 반대쪽 끝까지 건너갈 시간이 없다. v2(`TiltPlatformV2`, 신규 파일 · v1 무수정)는 목표 각도를 버리고 **토크를 적분**한다: 팔 길이 → 각가속도 → 각속도 → 각도. 각속도가 상태로 남으니 라이더가 반대쪽으로 이동하는 동안에도 판자는 처음 밟은 쪽으로 계속 기울고, 반대쪽을 밟아 토크가 뒤집혀도 그 각속도를 먼저 죽여야 방향이 바뀐다. 게임플레이용 유예 항 `leverLag` 이 유효 팔 길이를 라이더 실제 위치의 지수 추종으로 만들어 뒤집힘을 한 번 더 늦춘다. 수평 복원 스프링은 기본 **0**(꺼짐)이고, 켜도 `(1−|lever|)` 로 가중돼 팔 길이가 감쇠하는 만큼만 페이드인한다 — 라이더 유무로 껐다 켜면 슬라임이 점프하는 프레임에 `-θ·k` 가 계단으로 들어와(15°·6 = +90 °/s², 라이더 토크의 2배) 판자가 즉시 평평해지고, 하중 아래에서 돌면 그게 v1 의 평형 수렴이라 적분기의 존재 이유가 사라진다. 한편 `|θ|>max` 하드 스톱이 `ω=0` 을 만들므로, 차징이 끝나기 전에 판자가 바닥에 닿으면 발사 후 물려줄 각속도가 애초에 없다 — 잔여 스윙을 보이게 하려면 `tiltAccel` 을 낮추거나 `maxTiltAngle` 을 올린다.
- [Springy Branch](features/platforms/springy-branch.md) — 탄력 나뭇가지
- [Bamboo](features/platforms/bamboo.md) — 수직 대나무. SpringyBranch 물리 식 + vertical 적용, N-segment collider polyline + single shader-bent cylinder visual (sub-pixel coincident). 기본적으로 visible node frequency를 physics `segmentCount`에 동기화해 보이는 마디/구간 경계가 물리 segment와 일치. 마디 bulge + sheath scar 지오메트리, Quibli-style 2-tone + height gradient color. `BambooSettings.enableTipGrab` / `enableSideReboundV2` 독립 bool 2개 (예전 상호배타 `interactionMode` enum 제거 — 둘 다 켜면 옆구리 V2 반동 + 꼭대기 tip-grab 동시 동작, 스프링 계수 우선순위 grab>v2>기본). V2는 측면 충돌을 더 크게 휘게 한 뒤 Space charging release 방향으로 발사한다. **발사 부스트 = 화면에 보이는 스윙(`ω × arm`)의 carrier 상속뿐** — charge power에 속도를 직접 더하던 raw 보너스 그래프(`v2ChargedLaunchBonus*`)는 대나무 휨 상태와 무관해 예측 불가라 삭제(2026-07-24), SpringyBranch와 같은 모델로 통일. 천장 = `maxTipSpeed × velocityTransferScale × v2CarrierScale`, 그리고 SpringyBranch/TautLine과 동일한 `boostFalloffStart/EndSpeed` 게이트로 이미 빠른 발사 위에는 스택되지 않는다. V2 launch-release 반작용은 전용 접촉 높이 보정 노브를 받아 아래쪽 점프는 덜 흔들리고 위쪽 점프는 더 크게 흔들린다. 루트 Transform Z 회전으로 **기울여 배치 가능** — tip-grab 진입 dot, V2 side-contact 판정, slam guard, bend pump 입력 속도가 모두 world up 대신 대나무 자신의 rest 축(`StalkUp`)/가로 속도 기준이라 기울여도 겉모습만이 아니라 판정까지 따라간다 (수직일 때는 이전과 수치 동일). 대나무 하강 jitter 원인 추적용 `BambooSurfaceJitterDebugger`를 슬라임에 붙여 segment switch / normal jump / carry residual / probe churn을 관측한다.
- [Bamboo V3](features/platforms/bamboo-v3.md) — **물리 기반 점프대 대나무 (신규 게임플레이용, v1은 그대로 유지).** 정직한 캔틸레버 ODE 하나 + 진짜 임펄스 3개(무게 `r×mg` / 착지 완전비탄성 포획 / 발사 뉴턴3법칙)로만 휜다 — 투영도 커브도 높이 보정도 없다(`r×J` 외적이 입사각을, `|r|`이 접촉 높이를 이미 처리). 비물리 규칙은 **접착 파단 하나**: 접촉점 표면 가속도 `a = |r|·√(α²+ω⁴)`가 `adhesionBreakAccel`을 넘으면 표면 속도 `ω×arm`을 들려 자동으로 튕겨낸다 → **세게 박으면 스페이스 없이 발사, 약하게 닿으면 그냥 발판.** 튕기는 타이밍은 스프링에서 저절로 나옴(rest 통과 직후 감속 최대 = 트램펄린 지점). 라이더 유무로 스프링/캡/감쇠가 안 바뀐다(v1이 발사 반동을 죽이던 주범). 발사 반작용은 `RemoveRider`가 아니라 **`OnLaunch` 이벤트**로 잡는다 — 붙은 슬라임의 `velocity`가 carrier 운반분을 빠뜨려서 detach 시점 속도로는 발사/기어감/hazard/teleport를 구분할 수 없기 때문. **노브 26→5** (`naturalFreqHz` 2.0, `dampingRatio` 0.3, `maxDeflectionDeg` 60, `maxTipSpeed` 18, `adhesionBreakAccel` 40). SlimeController 수정 0 — 기존 `OnAfterMotion`/`ForceAirborneWithVelocity`/`OnLaunch`/`ISurfaceAnchorProvider` 훅만 사용. 발사 부스트는 `launchBoostScale`에 SpringyBranch/TautLine과 동일한 `boostFalloffStart/EndSpeed` pre-bonus 게이트(2026-07-26)를 곱해, 이미 빠른 발사 위에는 줄기 부스트가 스택되지 않는다. **밑면 매달림은 "던져질 수 있나"로 갈린다**: 대나무 표면은 글로벌 천장 한계를 180°로 덮어써서 밑에서 때린 슬라임이 부착하고 스프링이 로드될 동안 붙어 있는다. 지금 스윙이 throw 게이트(peak bend·사출 속도)를 만족시킬 수 있으면 던져질 때까지 매달리고, 못 던지는 스윙이면(약한 접촉·링다운으로 소진된 스윙·스프링 없는 쓰러진 장대) `underhangClingTime` 0.2초만 붙어 있다 미끄러진다 — 매달리기가 영구 상태가 되지 않는다.
- Bamboo On ThinIce — **BambooV3 × ThinIceV2 결합.** 얼음 위에 선 대나무: 대나무 착지 N회(기본 3)마다 밑의 얼음이 한 단계씩 금 가고(N번째에 파괴), 얼음이 깨지면 — 대나무 점프 때문이든 슬라임이 얼음을 직접 밟아서든 — 대나무가 Falling Train 방식으로 물리 낙하한다(현재 스윙 각속도를 물려받아 넘어지고, 라이더는 태운 채 떨어지며, 안착하면 밟을 수 있는 데브리로 굳음). 쓰러진 corpse는 장대 전체 길이를 따라 먼지 퍼프를 터뜨리며 "뿅" 사라진다(포프 — 착지 먼지와 동일한 퍼프). 사라지는 조건은 셋 중 먼저 오는 것: 붕괴 후 `corpseMaxLifeSeconds`(4초) 경과 / 멈춘 뒤(Box2D 수면 또는 지면 Static 안착) `corpseRestPoofSeconds`(2초) 경과 / zone 밖으로 완전히 굴러 나감. 원래 대나무가 다시 설 때도 **똑같은 퍼프**가 새로 선 장대의 길이를 따라 터진다. corpse 수명은 재생성 타이밍과 독립이라, 떨어진 장대와 다시 선 대나무가 한동안 **동시에 존재할 수 있다**. 쓰러진 corpse가 다른 살아있는 대나무 위에 얹히면 그 대나무가 무게로 휘고(전용 bambooMass 노브), 슬라임이 corpse 위에서 점프하면 반동으로 corpse가 밀린다(corpseRecoilImpulse). **그 위의 슬라임도 받침 대나무까지 전달된다**(`ridingSlimeMass`): 올라타 있는 동안의 무게, 착지하는 순간의 충격, 발사로 떠날 때의 반동 3채널 — 다리처럼 두 대나무에 걸쳐 있으면 각 받침이 절반씩 받는다. 얹힌 corpse는 **라이더가 아니라 외부 하중**으로 전달되므로(라이더 슬롯은 슬라임 전용), corpse가 얹혀 있어도 슬라임이 그 대나무에 착지하면 평소와 똑같이 랜딩킥이 들어가 휜다. 쓰러진 대나무는 붕괴 `respawnDelaySeconds`(2초) 뒤 얼음과 함께 자동 복구되고, 그 전에 슬라임이 존을 벗어나면 그 즉시 복구된다. 낙하 중인 corpse는 자기 라이더(붙어 있는 슬라임)와 물리 충돌하지 않는다 — 라이더는 표면 앵커가 잡고 있고, 밑면에 매달린 슬라임과의 접촉은 낙하를 제동만 했다. 부분적으로 금 간 얼음은 슬라임이 respawn zone을 벗어나면 초기화되고, 존 안에 있어도 대나무에 접촉이 `crackHealSeconds`(2초)간 없으면 금이 회복된다 — 부분 진행분이 무기한 남아 있지 않는다. **기존 파일 0 수정** — 신규 `BambooOnThinIce` 컴포넌트(얼음 GO에 부착)가 public 표면 폴링 + corpse 클론 스왑으로만 구현, 파일 삭제 = 완전 롤백. 노브는 per-instance `jumpsToShatter` 하나.
- [Deformable Mud](features/platforms/deformable-mud.md) — 진흙/찰흙 소성 변형
- [Rope](features/platforms/rope.md) — Verlet + 반동 Launch (v1 = sag-based, [v2 = timing-based](features/platforms/rope-v2.md), [v3 = trampoline-coupled](features/platforms/rope-v3.md))
- [Piano Keyboard](features/platforms/piano-keyboard.md) — 연주 가능한 업라이트 피아노. 슬라임이 흰 건반 착지 시 속도 비례 dip + 댐핑 스프링 복귀(출렁) + 셈여림 적용 CC0 피아노 음 재생. 흰 건반만 collider(검은 건반 비주얼만), 건반별 독립 판정으로 seam 끼면 2건반 동시. Unity-MIDI-Piano FBX/CC0 샘플 추출, `PianoSettings` 그래프 튜닝(velocity→volume/depth, spring stiffness/damping). **하모나이즈 모드**(opt-in `PianoConfig.harmonize` — 착지마다 코드 진행 전진 + 착지 위치=멜로디 스냅 + `PianoVoicePool` 화음 + velocity density, 끄면 원음 그대로). authored 스키마 7종×4코드(장조 4 + 나란한단조 2 + 차용 1), 확률은 **루프 경계에서만** 굴려 다음 스키마 선택 → 루프마다 ~50%로 장↔단 전환. **연주 장식 11종**(롤드 코드·보이스리딩·페달포인트·알베르티·칩튠 아르페지오·아포자투라·서스펜션·Drop-2·서스테인 페달 연동·심퍼세틱 레조넌스·글리산도 런, 전부 개별 토글, 모든 장식의 첫 음은 착지 즉시). **건반 시각은 울린 음마다 1:1** — 발음 이벤트가 88건반(검은 건반 포함)을 그 타이밍에 누름. *자동 반주(자동 **생성** 반주)는 2회 시도 후 전량 폐기(2026-07-23) — 반려 사유가 싱크가 아니라 생성 음악의 퀄리티였음.* **유휴 자동 연주 = 곡 로테이션**(2026-07-24, `PianoConfig.autoPlay`) — 생성이 아니라 **사람이 연주한 녹음 재생**이라 그 반려 사유가 없다. **곡이 화성의 주인**: 슬라임이 피아노에 *올라타는 순간* 다음 곡이 뽑히고 진행 전체가 그 곡의 조성으로 전조된다 → 슬라임이 치는 화음이 이미 그 곡에 속함 → 떠나서 곡이 시작되면 **이어서 들린다**. 곡마다 장식 11종 프리셋도 따로 갖는다. **곡별 미리보기 = 실측 타건 재현**(2026-07-27 재작성): 코드 심볼만 남기던 두 판(조성·장식 배정 → 도입부 코드열 + texture 스칼라 5개)이 같은 이유로 반려됐다 — **곡 간 차이는 코드 심볼 위에 있는데 그 층을 평균내 버려서** 모든 곡이 같은 블록 화음으로 나왔다. 지금 한 스텝은 코드가 아니라 **제스처 예산(0.35초) 안에 들어오는 곡의 타건들**이고, 착지를 여러 번 하면 도입부가 순서대로 지나간다 — 느린 곡은 착지당 한 타건, 빠른 곡은 곡의 실제 간격대로 굴러가는 짧은 음형(빠른 곡을 착지당 한 음씩 내면 곡을 5배 느리게 재생하는 셈이라 속도가 사라진다). 스텝은 그 음들을 **음마다** 담는다(시차 / 세기 / **눌림 + 잔향 두 값**) + 그 순간 곡이 쓰는 피치클래스(밟은 건반의 스냅 목표). **낼 때는 곡의 음 순서를 따라 치지 않고 한 화음으로 뭉쳐 살짝 굴린다** — 전사를 문자 그대로 재생하면 곡을 어설프게 흉내내는 것처럼 들려서, 화성과 연주 방식만으로 인상을 전한다(버리는 건 선율 순서뿐, 세기·길이·잔향·음역은 실측 그대로). 곡의 노래하는 음도 빼지 않는다 — 그 자리에 밟은 건반을 꽂았더니 곡의 선율선이 사라지고 임의 음이 앞에 나와 겉돌았다. 밟은 건반은 그 화음 안의 한 성부로 섞인다. 길이를 한 값으로 뭉치면 안 된다 — 0.2초 스타카토가 3.4초 페달 밑에 있으면 **짧은 타건 + 긴 꼬리**이지 3.4초짜리 음이 아니다(뭉쳤다가 "통통 튀는 곡이 늘어진다"로 반려됨). 음량·음색도 **곡이 친 세기로만** 정한다 — 착지 속도도, 곡별 정규화도 안 쓴다(곡마다 최대값으로 나누면 여리게 친 곡이 센 곡만큼 커진다). 세기→음색 스윕 구간은 0~1이 아니라 전사 세기가 실제로 몰려 있는 0.35~0.85 — 0~1에 걸면 여리게 쳐도 안 어두워져서 "가볍게 치는 느낌"이 안 난다. **끊는 시점은 곡의 실측 길이와 밟고 있는 시간 중 더 긴 쪽**(`sustainWhileStanding`) — 실측 눌림이 수십 ms거나 오탐인 구간이 있어서 한 번 밟은 음이 즉시 끊겼다. 둘 중 하나로 정하지 않는다: 누름은 음을 재시작하지 않고 댐퍼만 막으므로, 짧은 실측은 점프까지 유지되고 긴 실측은 떠난 뒤에도 남은 길이만큼 더 울린다. **밟은 건반을 울리는 화음의 음으로 강제하는 옵션**(`snapLandedKeyToChord`) — 밟은 자리 근처(최대 온음)에서 화성 밖 음이 절대 안 나오게 한다. 소리를 아예 안 내는 방식은 반려됐다(*"밟은 근처의 소리는 나야해, 화성에 맞춰서 나야하지만 나긴 해야 한다"*) — 곡이 이미 그 음을 치고 있으면 옥타브를 옮겨서라도 밟은 음은 들린다(같은 피치 겹침은 별개 발음으로 안 들림). 런타임은 **재구성하지 않는다** — 보이스리딩·velocity density·장식은 전부 화음 모양을 *지어내는* 장치였고, 지어내는 것이 곡의 실측 보이싱을 덮어쓴 원인이라 preview 곡에선 전부 우회한다. **연주 표현 3종**도 같이 복원: 울림 유지 후 지수 댐퍼(클립이 실제 녹음이라 이미 스스로 감쇠하는데 릴리즈 내내 선형으로 줄이던 게 "샘플 재생 같다"의 최대 원인), 세기→음색(해머 비선형 근사 — 샘플이 1레이어라 밝게는 못 만들고 약한 타건을 어둡게), 음마다 다른 음량·길이. 곡별 장식 프리셋은 전곡 폐기(sustainPedal만 잔존, 장식 코드는 수동용 존치). preview 곡은 단조도 실제 으뜸음 사용. 조성은 전사에서 자동 추출하되 **preview 없는 폴백 곡은 나란한장조(+3)에 tonic을 놓는다** — authored 단조 스키마 M1/M2가 tonic의 나란한단조를 만들도록 설계돼 있어서, 그냥 넣으면 곡과 3도 어긋남. 조성/장식은 `[NonSerialized]` **런타임 override**라 PianoConfig 에셋은 절대 안 바뀜(직접 쓰면 에디터에 영구 저장돼 디자이너 튜닝이 파괴됨). 소리=mp3, 건반=오프라인 전사 노트, **클립 재생 헤드가 유일한 클럭**이라 어긋남이 구조적으로 불가능(1차 자동 반주 실패 원인의 정면 해결). 전사는 로컬 오프라인 transkun(MIT), 자작 DSP는 지표 열세로 폐기. 현재 42곡. **음악 라이선스가 구조를 강제** — 현 곡들(AudioHero)은 싱크권만이라 mp3 재생+화면 싱크는 되고 샘플러 재연주는 불가; PD 곡으로 바꾸면 제약 해소.

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
- Lightning Strike — 하늘에서 수직 낙하하는 반복 낙뢰. 리스트 순서대로 `gap` 간격 낙하 + `cycleGap` 후 반복, 1회 낙하는 `boltsPerStrike` 발 연속 버스트, 명중 시 기둥 좌/우 판정 수평 튕김 + Granite Blast 동일 임팩트(스파크/폭발/SFX/HitStop/쉐이크/눈 carve). 전조는 착지점 지면을 감싸는 작은 푸른 정전기 (`telegraphLead` 초 선행, `LightningTelegraphSpark`) — 지면 밴드를 따라 흩어진 아크들이 떴다 내려앉으며 지직거리고, 낙하 경로 선은 그리지 않는다. Granite Blast Branching 텐드릴의 베지어 지그재그 + 팁 스윕만 떼낸 독립 구현 (`LightningStrikeField` / `LightningBolt` / `LightningStrikeConfig`)
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
